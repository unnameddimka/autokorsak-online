using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoKorsak.Api;
using AutoKorsak.Domain;
using AutoKorsak.LegacyXml;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AutoKorsak.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"autokorsak-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Storage:DatabasePath", DatabasePath);

    public HttpClient Client()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(CsrfHeaderMiddleware.HeaderName, "1");
        return client;
    }
}

public class ApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private static readonly JsonSerializerOptions Json = Storage.CreateJsonOptions();

    public void Dispose() => _factory.Dispose();

    private static async Task<JsonNode> Body(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)!;
    }

    private static async Task<string> ErrorCode(HttpResponseMessage response)
    {
        Assert.False(response.IsSuccessStatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>();
    }

    private static async Task<JsonNode> Import(HttpClient client, byte[] xml)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(xml);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        content.Add(file, "file", "tournament.xml");
        return await Body(await client.PostAsync("/api/tournaments/import", content));
    }

    [Fact]
    public async Task Create_tournament_player_pairing_and_result_persist_across_restart()
    {
        var client = _factory.Client();
        var created = await Body(await client.PostAsJsonAsync("/api/tournaments",
            LegacyXmlTests.Settings("Кубок", TournamentSystem.Swiss), Json));
        var id = created["tournament"]!["id"]!.GetValue<string>();

        await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/participants", LegacyXmlTests.Player("Перший"), Json));
        await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/participants", LegacyXmlTests.Player("Другий"), Json));
        var paired = await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/1/pairings",
            LegacyXmlTests.Pair(1, 2), Json));
        var pairingId = paired["result"]!["pairingId"]!.GetValue<int>();
        await Body(await client.PutAsJsonAsync($"/api/tournaments/{id}/pairings/{pairingId}",
            LegacyXmlTests.Pair(1, 2, GameResult.WhiteWins), Json));

        // A new host over the same database file simulates a container restart.
        var storage = new Storage(_factory.DatabasePath);
        var t = storage.GetTournament(Guid.Parse(id))!;
        Assert.Equal("Кубок", t.Name);
        Assert.Equal(2, t.Participants.Count);
        Assert.Equal(GameResult.WhiteWins, t.Rounds.Single(r => r.Number == 1).Pairings.Single().Result);
    }

    [Fact]
    public async Task Import_edit_export_reimport_keeps_result_change()
    {
        var client = _factory.Client();
        var imported = await Import(client, Fixtures.Read(Fixtures.ReferenceRelativePath));
        var id = imported["tournament"]!["id"]!.GetValue<string>();
        Assert.Equal("Группа А", imported["tournament"]!["name"]!.GetValue<string>());

        var tournament = imported["tournament"]!.Deserialize<Tournament>(Json)!;
        var game = tournament.Rounds.Single(r => r.Number == 6).Pairings.Single(p => p.BoardNumber == 1);
        var input = new PairingInput(game.BlackId, game.WhiteId, false, game.BoardNumber, false, 0, 0, false, GameResult.BlackWins, false, "", false);
        await Body(await client.PutAsJsonAsync($"/api/tournaments/{id}/pairings/{game.Id}", input, Json));

        var standings = await Body(await client.GetAsync($"/api/tournaments/{id}/standings"));
        Assert.Contains(standings["rows"]!.AsArray(), r => r!["values"]!["wins"]!.GetValue<decimal>() == 5m && r["position"]!.GetValue<int>() <= 2);

        var xml = await client.GetByteArrayAsync($"/api/tournaments/{id}/export.xml");
        var reimported = await Import(client, xml);
        var back = reimported["tournament"]!.Deserialize<Tournament>(Json)!;
        Assert.Equal(GameResult.BlackWins, back.Rounds.Single(r => r.Number == 6).Pairings.Single(p => p.BoardNumber == 1).Result);
    }

    [Fact]
    public async Task Duplicate_assignment_is_rejected_with_localizable_code()
    {
        var client = _factory.Client();
        var imported = await Import(client, Fixtures.Read(Fixtures.ReferenceRelativePath));
        var id = imported["tournament"]!["id"]!.GetValue<string>();
        var response = await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/6/pairings", LegacyXmlTests.Pair(1, 2), Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("pairing.playerAlreadyPaired", await ErrorCode(response));
    }

    [Fact]
    public async Task Invalid_xml_does_not_create_a_tournament()
    {
        var client = _factory.Client();
        using var content = new MultipartFormDataContent { { new ByteArrayContent("<Tournament"u8.ToArray()), "file", "bad.xml" } };
        var response = await client.PostAsync("/api/tournaments/import", content);
        Assert.Equal("xml.malformed", await ErrorCode(response));
        var list = await Body(await client.GetAsync("/api/tournaments"));
        Assert.Empty(list.AsArray());
    }

    [Fact]
    public async Task Catalog_changes_only_on_explicit_upsert_and_snapshots_stay_independent()
    {
        var client = _factory.Client();
        var first = await Import(client, Fixtures.Read(Fixtures.ReferenceRelativePath));
        var id = first["tournament"]!["id"]!.GetValue<string>();
        Assert.Empty((await Body(await client.GetAsync("/api/catalog"))).AsArray());

        var plan = await Body(await client.GetAsync($"/api/tournaments/{id}/catalog-upsert"));
        Assert.Equal(10, plan["toCreate"]!.GetValue<int>());
        await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/catalog-upsert", new { decisions = Array.Empty<object>() }));
        Assert.Equal(10, (await Body(await client.GetAsync("/api/catalog?limit=50"))).AsArray().Count);

        // Editing the tournament player does not touch the catalog.
        var input = LegacyXmlTests.Player("Кобзар Гліб", rating: 1999) with { Rank = "9k" };
        await Body(await client.PutAsJsonAsync($"/api/tournaments/{id}/participants/1", input, Json));
        var entry = (await Body(await client.GetAsync("/api/catalog?search=Кобзар"))).AsArray().Single()!;
        Assert.Equal(1240, entry["rating"]!.GetValue<int>());

        // A second tournament holds its own snapshot; upserting the first one later does not change it.
        var second = await Import(client, Fixtures.Read(Fixtures.ReferenceRelativePath));
        var secondId = second["tournament"]!["id"]!.GetValue<string>();
        await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/catalog-upsert", new { decisions = Array.Empty<object>() }));
        entry = (await Body(await client.GetAsync("/api/catalog?search=Кобзар"))).AsArray().Single()!;
        Assert.Equal(1999, entry["rating"]!.GetValue<int>());
        var secondNow = await Body(await client.GetAsync($"/api/tournaments/{secondId}"));
        var kobzar = secondNow["tournament"]!["participants"]!.AsArray().Single(p => p!["id"]!.GetValue<int>() == 1)!;
        Assert.Equal(1240, kobzar["rating"]!.GetValue<int>());
    }

    [Fact]
    public async Task Paired_player_cannot_become_preliminary_and_nothing_changes()
    {
        var client = _factory.Client();
        var imported = await Import(client, Fixtures.Read(Fixtures.ReferenceRelativePath));
        var id = imported["tournament"]!["id"]!.GetValue<string>();
        var input = LegacyXmlTests.Player("Кобзар Гліб", preliminary: true, rating: 1240) with { Rank = "9k" };

        var response = await client.PutAsJsonAsync($"/api/tournaments/{id}/participants/1", input, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("participant.pairedCannotBePreliminary", await ErrorCode(response));
        var reloaded = (await Body(await client.GetAsync($"/api/tournaments/{id}")))["tournament"]!.Deserialize<Tournament>(Json)!;
        Assert.False(reloaded.FindParticipant(1)!.Preliminary);
        Assert.Equal(6, reloaded.AllPairings().Count(x => x.Pairing.Involves(1)));
    }

    [Fact]
    public async Task Round_robin_generation_stage_change_and_preparation_endpoints()
    {
        var client = _factory.Client();
        var created = await Body(await client.PostAsJsonAsync("/api/tournaments",
            LegacyXmlTests.Settings("Коло", TournamentSystem.RoundRobin, 7), Json));
        var id = created["tournament"]!["id"]!.GetValue<string>();
        for (var i = 1; i <= 4; i++)
            await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/participants", LegacyXmlTests.Player($"Гравець {i}"), Json));

        // 7 rounds for 4 players: a terminal round robin needs 3 rounds, so the server asks to adjust (spec 0002 §4).
        var mismatch = await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/1/generate", new { allRounds = true, source = "random" }, Json);
        Assert.Equal("generate.roundCountAdjustment", await ErrorCode(mismatch));

        var staged = await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/stages", new { startRound = 4, system = "swiss" }, Json));
        Assert.Equal("swiss", staged["tournament"]!["system"]!.GetValue<string>());
        Assert.True(staged["systemLocked"]!.GetValue<bool>());

        var generated = await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/2/generate",
            new { allRounds = true, source = "random" }, Json));
        Assert.Equal(6, generated["result"]!["created"]!.GetValue<int>());
        var stage = generated["tournament"]!["stages"]![0]!;
        Assert.True(stage["randomStartingNumbers"]!.GetValue<bool>());
        Assert.Equal(4, stage["frozenRosterIds"]!.AsArray().Count);

        var swissRound = await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/4/generate", new { }, Json);
        Assert.Equal("generate.notAvailable", await ErrorCode(swissRound));

        await Body(await client.PutAsJsonAsync($"/api/tournaments/{id}/round", new { currentRound = 2, includeCurrentRound = true }, Json));
        var preparation = await Body(await client.GetAsync($"/api/tournaments/{id}/standings/preparation"));
        Assert.Equal(2, preparation["round"]!.GetValue<int>());
        Assert.Equal(4, preparation["rows"]!.AsArray().Count);
    }

    [Fact]
    public async Task Frozen_round_robin_member_availability_edit_is_refused_and_nothing_changes()
    {
        var client = _factory.Client();
        var created = await Body(await client.PostAsJsonAsync("/api/tournaments",
            LegacyXmlTests.Settings("Коло", TournamentSystem.RoundRobin, 3), Json));
        var id = created["tournament"]!["id"]!.GetValue<string>();
        for (var i = 1; i <= 4; i++)
            await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/participants", LegacyXmlTests.Player($"Гравець {i}"), Json));
        await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/1/generate", new { source = "random" }, Json));

        var response = await client.PutAsJsonAsync($"/api/tournaments/{id}/participants/2",
            LegacyXmlTests.Player("Гравець 2") with { NotPlayingRounds = [2] }, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("participant.rosterMemberUnavailable", await ErrorCode(response));
        var reloaded = (await Body(await client.GetAsync($"/api/tournaments/{id}")))["tournament"]!.Deserialize<Tournament>(Json)!;
        Assert.Empty(reloaded.FindParticipant(2)!.NotPlayingRounds);
        var all = await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/1/generate", new { allRounds = true }, Json));
        Assert.Equal(4, all["result"]!["created"]!.GetValue<int>());
    }

    [Fact]
    public async Task Pre_0001_tournament_keeps_the_settings_lock_after_its_last_pairing_is_deleted()
    {
        // Build an authentic pre-0001 document: no "stages", no "pairingsEverDrawn", no "firstEligibleRound".
        var t = TournamentEditor.Create(LegacyXmlTests.Settings("Старий турнір", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        TournamentEditor.AddParticipant(t, LegacyXmlTests.Player("Перший"));
        TournamentEditor.AddParticipant(t, LegacyXmlTests.Player("Другий"));
        var pairing = TournamentEditor.AddPairing(t, 1, LegacyXmlTests.Pair(1, 2)); // unplayed
        var node = JsonNode.Parse(JsonSerializer.Serialize(t, Json))!.AsObject();
        node.Remove("stages");
        node.Remove("pairingsEverDrawn");
        foreach (var p in node["participants"]!.AsArray()) p!.AsObject().Remove("firstEligibleRound");
        var old = JsonSerializer.Deserialize<Tournament>(node.ToJsonString(), Json)!;
        Assert.False(old.PairingsEverDrawn);
        new Storage(_factory.DatabasePath).SaveTournament(old); // as stored by a pre-0001 version

        var client = _factory.Client();
        Assert.True((await Body(await client.GetAsync($"/api/tournaments/{t.Id}")))["systemLocked"]!.GetValue<bool>());
        await Body(await client.DeleteAsync($"/api/tournaments/{t.Id}/pairings/{pairing.Id}"));
        var after = await Body(await client.GetAsync($"/api/tournaments/{t.Id}"));
        Assert.True(after["systemLocked"]!.GetValue<bool>());
        Assert.True(after["tournament"]!["pairingsEverDrawn"]!.GetValue<bool>()); // persisted on that save

        var change = await client.PutAsJsonAsync($"/api/tournaments/{t.Id}/settings",
            LegacyXmlTests.Settings("Старий турнір", TournamentSystem.McMahon), Json);
        Assert.Equal("settings.systemLocked", await ErrorCode(change));
    }

    [Fact]
    public async Task Starting_numbers_round_count_confirmation_clear_and_result_lock_endpoints()
    {
        var client = _factory.Client();
        var created = await Body(await client.PostAsJsonAsync("/api/tournaments",
            LegacyXmlTests.Settings("Коло", TournamentSystem.RoundRobin, 5), Json));
        var id = created["tournament"]!["id"]!.GetValue<string>();
        for (var i = 1; i <= 4; i++)
            await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/participants", LegacyXmlTests.Player($"Гравець {i}"), Json));

        var saved = await Body(await client.PutAsJsonAsync($"/api/tournaments/{id}/starting-numbers", new
        {
            entries = new[] { new { participantId = 1, number = 2 }, new { participantId = 2, number = 1 }, new { participantId = 3, number = 3 }, new { participantId = 4, number = 4 } },
        }, Json));
        Assert.Equal(2, saved["roundRobin"]!["numbers"]!["1"]!.GetValue<int>());

        var prompt = await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/1/generate", new { allRounds = true, source = "entered" }, Json);
        Assert.Equal(HttpStatusCode.Conflict, prompt.StatusCode);
        Assert.Equal("generate.roundCountAdjustment", await ErrorCode(prompt));

        var drawn = await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/1/generate",
            new { allRounds = true, source = "entered", roundCount = 3 }, Json));
        Assert.Equal(3, drawn["tournament"]!["numberOfRounds"]!.GetValue<int>());
        Assert.Equal([2, 1, 3, 4], drawn["tournament"]!["stages"]![0]!["frozenRosterIds"]!.AsArray().Select(x => x!.GetValue<int>()));

        var frozenEdit = await client.PutAsJsonAsync($"/api/tournaments/{id}/starting-numbers",
            new { entries = new[] { new { participantId = 1, number = 3 } } }, Json);
        Assert.Equal("startingNumbers.frozen", await ErrorCode(frozenEdit));

        // Enter a result: generate and clear are both rejected by the API.
        var t = drawn["tournament"]!.Deserialize<Tournament>(Json)!;
        var game = t.Rounds[0].Pairings[0];
        await Body(await client.PutAsJsonAsync($"/api/tournaments/{id}/pairings/{game.Id}",
            new PairingInput(game.BlackId, game.WhiteId, false, game.BoardNumber, false, 0, 0, false, GameResult.BlackWins, false, "", false), Json));
        Assert.Equal("generate.lockedByResults", await ErrorCode(await client.PostAsJsonAsync(
            $"/api/tournaments/{id}/rounds/2/generate", new { confirmReplace = true }, Json)));
        Assert.Equal("clear.lockedByResults", await ErrorCode(await client.PostAsJsonAsync(
            $"/api/tournaments/{id}/rounds/1/clear-round-robin", new { confirm = true }, Json)));

        // Remove the result, then clear: single stage and no pairings left → Settings unlocked.
        await Body(await client.PutAsJsonAsync($"/api/tournaments/{id}/pairings/{game.Id}?confirmDiscardResult=true",
            new PairingInput(game.BlackId, game.WhiteId, false, game.BoardNumber, false, 0, 0, false, GameResult.Unset, false, "", false), Json));
        var preview = await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/1/clear-round-robin", new { confirm = false }, Json);
        Assert.Equal("clear.confirm", await ErrorCode(preview));
        var cleared = await Body(await client.PostAsJsonAsync($"/api/tournaments/{id}/rounds/1/clear-round-robin", new { confirm = true }, Json));
        Assert.True(cleared["result"]!["settingsUnlocked"]!.GetValue<bool>());
        Assert.False(cleared["systemLocked"]!.GetValue<bool>());
        Assert.False(cleared["roundRobin"]!["frozen"]!.GetValue<bool>());
        Assert.Equal(1, cleared["roundRobin"]!["numbers"]!["2"]!.GetValue<int>());
    }

    [Fact]
    public async Task Mutations_without_client_header_are_refused()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tournaments", LegacyXmlTests.Settings("X", TournamentSystem.Swiss), Json);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Retained_legacy_xml_is_not_sent_to_the_browser()
    {
        var client = _factory.Client();
        var imported = await Import(client, Fixtures.Read(Fixtures.ReferenceRelativePath));
        var text = imported.ToJsonString();
        Assert.DoesNotContain("legacyXml", text);
        Assert.DoesNotContain("legacySourceXml", text);
        Assert.True(imported["imported"]!.GetValue<bool>());
    }
}
