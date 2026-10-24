using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AutoKorsak.Api;
using AutoKorsak.Domain;
using AutoKorsak.LegacyXml;
using static AutoKorsak.Tests.LegacyXmlTests;

namespace AutoKorsak.Tests;

/// <summary>Acceptance tests for spec 0003 (automatic RR round count, pure-RR participation, the "both win" result).</summary>
public class Spec0003Tests
{
    private sealed class FixedRandom(params int[] values) : IRandomSource
    {
        private int _i;
        public int Next(int maxExclusive) => values[_i++ % values.Length] % maxExclusive;
    }

    /// <summary>A pure round robin whose Settings asked for <paramref name="requestedRounds"/> (ignored).</summary>
    private static Tournament RoundRobin(int players, int requestedRounds = 5, bool preliminary = false)
    {
        var t = TournamentEditor.Create(Settings("RR", TournamentSystem.RoundRobin, requestedRounds), DateTimeOffset.UnixEpoch);
        for (var i = 1; i <= players; i++) TournamentEditor.AddParticipant(t, Player($"P{i}", preliminary, rating: 1000 + i * 10));
        return t;
    }

    private static GenerationResult Draw(Tournament t, int round = 1, bool all = true) =>
        PairingSchedule.GenerateRoundRobin(t, round, all, StartingNumberSource.Random, false, new FixedRandom(0));

    private static string Json(Tournament t) => JsonSerializer.Serialize(t, Storage.Json);

    private static Tournament Clone(Tournament t) => JsonSerializer.Deserialize<Tournament>(Json(t), Storage.Json)!;

    private static PairingInput Edit(Pairing p, GameResult result, int? black = null, int? white = null, bool byReferee = false) =>
        new(black ?? p.BlackId, white ?? p.WhiteId, p.IsBye, p.BoardNumber, false, 0, 0, p.AllowDraw, result, byReferee, "", false);

    // 1 and 4. Six players: Settings cannot set the count; the draw derives and persists five rounds.
    [Fact]
    public void Six_players_get_five_rounds_from_the_draw_and_settings_never_override_the_count()
    {
        var t = RoundRobin(6, requestedRounds: 9);
        Assert.Equal(5, t.NumberOfRounds); // provisional default; the submitted 9 is ignored
        Assert.True(t.RoundCountPending());

        TournamentEditor.UpdateSettings(t, Settings("RR", TournamentSystem.RoundRobin, 2)); // forged / stale value
        Assert.Equal(5, t.NumberOfRounds);

        var result = Draw(t);
        Assert.Equal(5, result.NumberOfRounds);
        Assert.False(t.RoundCountPending());
        Assert.Equal(5, t.Rounds.Count(r => r.Pairings.Count == 3));

        TournamentEditor.UpdateSettings(t, Settings("RR renamed", TournamentSystem.RoundRobin, 12));
        var reloaded = Clone(t);
        Assert.Equal(5, reloaded.NumberOfRounds);
        Assert.Equal("RR renamed", reloaded.Name);
    }

    // 2. Three players with a stale count of five: three rounds and one bye per round.
    [Fact]
    public void Three_players_get_three_rounds_with_byes_whatever_the_stale_count()
    {
        var t = RoundRobin(3);
        t.NumberOfRounds = 6; // e.g. stored by an older version for an expected six players
        Draw(t);
        Assert.Equal(3, t.NumberOfRounds);
        Assert.Equal(3, t.Rounds.Count);
        Assert.All(t.Rounds, r =>
        {
            Assert.Single(r.Pairings, p => p.IsBye);
            Assert.Single(r.Pairings, p => !p.IsBye);
        });
    }

    // Review P2-01 of f20db3f: a current round valid only under the provisional count must not block the draw.
    [Fact]
    public void Drawing_all_rounds_from_a_round_beyond_the_derived_count_resizes_and_clamps_the_current_round()
    {
        var t = RoundRobin(3);
        TournamentEditor.SetCurrentRound(t, 5, true); // provisional count 5
        var result = Draw(t, round: 5);
        Assert.Equal([1, 2, 3], result.Rounds);
        Assert.Equal(3, t.NumberOfRounds);
        Assert.Equal(3, t.CurrentRound);
        Assert.Equal(3, t.Rounds.Count(r => r.Pairings.Count == 2));
    }

    [Fact]
    public void A_single_round_beyond_the_derived_count_is_refused_without_writes()
    {
        var t = RoundRobin(3);
        TournamentEditor.SetCurrentRound(t, 5, true);
        var before = Json(t);
        var ex = Assert.Throws<DomainException>(() => Draw(t, round: 5, all: false));
        Assert.Equal("round.outOfRange", ex.Code);
        Assert.Equal(3, ex.Args["max"]);
        Assert.Equal(before, Json(t));

        Draw(t, round: 4, all: true); // frozen roster afterwards: unchanged behaviour for later requests
        Assert.Equal("round.outOfRange", Assert.Throws<DomainException>(() => Draw(t, round: 4, all: true)).Code);
    }

    // 3. Only the final roster counts; fewer than two final players fails without writes.
    [Fact]
    public void Preliminary_players_do_not_inflate_the_schedule_and_one_final_player_fails_atomically()
    {
        var t = RoundRobin(4);
        TournamentEditor.AddParticipant(t, Player("Maybe 1", preliminary: true));
        TournamentEditor.AddParticipant(t, Player("Maybe 2", preliminary: true));
        Assert.Equal(3, Draw(Clone(t)).NumberOfRounds);

        var lonely = RoundRobin(1);
        TournamentEditor.AddParticipant(lonely, Player("Maybe", preliminary: true));
        var before = Json(lonely);
        Assert.Equal("generate.tooFewPlayers", Assert.Throws<DomainException>(() => Draw(lonely)).Code);
        Assert.Equal(before, Json(lonely));
    }

    // 4. Other systems keep the editable count; leaving RR before the draw makes it editable again.
    [Fact]
    public void Non_round_robin_systems_keep_an_editable_round_count()
    {
        var swiss = TournamentEditor.Create(Settings("S", TournamentSystem.Swiss, 7), DateTimeOffset.UnixEpoch);
        Assert.Equal(7, swiss.NumberOfRounds);

        var t = RoundRobin(4, requestedRounds: 9);
        TournamentEditor.UpdateSettings(t, Settings("S", TournamentSystem.Swiss, 8));
        Assert.Equal(8, t.NumberOfRounds);
        Assert.False(t.IsPureRoundRobin());
    }

    // 5. Unsafe shrink fails atomically with an explanation; a safe one needs no confirmation.
    [Fact]
    public void An_unsafe_implicit_shrink_fails_atomically()
    {
        var t = RoundRobin(4);
        t.NumberOfRounds = 8;
        TournamentEditor.AddPairing(t, 7, Pair(1, 2));
        var before = Json(t);
        var ex = Assert.Throws<DomainException>(() => Draw(t));
        Assert.Equal("generate.roundCountShrinkBlocked", ex.Code);
        Assert.Equal(3, ex.Args["required"]);
        Assert.Equal("7", ex.Args["rounds"]);
        Assert.Equal(before, Json(t));
    }

    // Review P1-01 of f20db3f: a round with retained legacy XML but no pairings is never dropped by the resize.
    [Fact]
    public void A_metadata_only_round_beyond_the_derived_count_blocks_the_shrink_without_changes()
    {
        var t = RoundRobin(3);
        t.NumberOfRounds = 6;
        t.Rounds.Add(new Round { Number = 5, LegacyXml = "<TournamentRound><RoundNumber>5</RoundNumber><Extra>kept</Extra></TournamentRound>" });
        var before = Json(t);
        var ex = Assert.Throws<DomainException>(() => Draw(t));
        Assert.Equal("generate.roundCountShrinkBlocked", ex.Code);
        Assert.True(ex.IsConflict);
        Assert.Equal("5", ex.Args["rounds"]);
        Assert.Equal(before, Json(t));
    }

    [Fact]
    public void An_empty_round_without_metadata_still_shrinks_automatically()
    {
        var t = RoundRobin(3);
        t.NumberOfRounds = 6;
        t.Rounds.Add(new Round { Number = 6 }); // e.g. left behind by deleting its only pairing
        Draw(t);
        Assert.Equal(3, t.NumberOfRounds);
        Assert.DoesNotContain(t.Rounds, r => r.Number > 3);
    }

    [Fact]
    public void Imported_round_xml_blocks_the_shrink_after_switching_an_undrawn_import_to_round_robin()
    {
        // A legacy file whose round elements hold no pairings imports without the Settings lock; its rounds keep their
        // original elements (LegacyXml). Built from the reference fixture with every <Pairing> element removed.
        var xml = System.Text.RegularExpressions.Regex.Replace(
            Encoding.UTF8.GetString(Fixtures.Read(Fixtures.ReferenceRelativePath)), @"<Pairing[\s>][\s\S]*?</Pairing>", "");
        var t = LegacyXmlImporter.Import(Encoding.UTF8.GetBytes(xml), DateTimeOffset.UnixEpoch);
        Assert.Empty(t.AllPairings());
        Assert.False(TournamentEditor.IsSystemLocked(t));
        TournamentEditor.UpdateSettings(t, Settings(t.Name, TournamentSystem.RoundRobin));
        Assert.True(t.IsPureRoundRobin());
        foreach (var p in t.Participants.Skip(4)) p.Preliminary = true; // 4 final players: 3 rounds instead of 6
        Assert.All(t.Rounds, r => Assert.NotNull(r.LegacyXml));
        var before = Json(t);
        var ex = Assert.Throws<DomainException>(() => PairingSchedule.GenerateRoundRobin(t, 1, true, StartingNumberSource.Random, false, new FixedRandom(0)));
        Assert.Equal("generate.roundCountShrinkBlocked", ex.Code);
        Assert.Equal("4, 5, 6", ex.Args["rounds"]);
        Assert.Equal(before, Json(t));
    }

    // 6. A frozen roster is stable; Clear releases it and the next draw recalculates.
    [Fact]
    public void Frozen_roster_and_count_stay_until_clear_and_the_next_draw_recalculates()
    {
        var t = RoundRobin(4);
        Draw(t);
        Assert.Equal(3, t.NumberOfRounds);

        // A finalized newcomer cannot enter a drawn pure round robin; a preliminary one can be registered.
        Assert.Equal("participant.noStageAfterRoundRobin",
            Assert.Throws<DomainException>(() => TournamentEditor.AddParticipant(t, Player("Late"))).Code);
        var late = TournamentEditor.AddParticipant(t, Player("Late", preliminary: true));
        Draw(t, round: 2, all: false); // regenerating an unplayed round keeps roster and count
        Assert.Equal(3, t.NumberOfRounds);
        Assert.Equal(4, t.Stages[0].FrozenRosterIds!.Count);

        PairingSchedule.ClearRoundRobin(t, 1, true);
        Assert.True(t.RoundCountPending());
        TournamentEditor.UpdateParticipant(t, late.Id, Player("Late"));
        Draw(t);
        Assert.Equal(5, t.NumberOfRounds);
        Assert.Equal(5, t.Stages[0].FrozenRosterIds!.Count);

        // An entered result (including "both win") still locks generation and Clear.
        var game = t.Rounds[0].Pairings.First(p => !p.IsBye);
        TournamentEditor.UpdatePairing(t, game.Id, Edit(game, GameResult.BothWin), false);
        Assert.Equal("generate.lockedByResults", Assert.Throws<DomainException>(() => Draw(t)).Code);
        Assert.Equal("clear.lockedByResults", Assert.Throws<DomainException>(() => PairingSchedule.ClearRoundRobin(t, 1, true)).Code);
    }

    // 7. Hidden join/skip flags: defaults for newcomers, ignored but kept for existing data.
    [Fact]
    public void Pure_round_robin_ignores_hidden_joining_rounds_and_absences_without_erasing_them()
    {
        var t = RoundRobin(3);
        var newcomer = TournamentEditor.AddParticipant(t,
            Player("New") with { FirstEligibleRound = 3, NotPlayingRounds = [2] }); // hidden fields from a stale client
        Assert.Equal(1, newcomer.FirstEligibleRound);
        Assert.Empty(newcomer.NotPlayingRounds);

        // Stale flags as stored by an older version (or set while the tournament used another system).
        var stale = t.Participants[0];
        stale.FirstEligibleRound = 3;
        stale.NotPlayingRounds = [2, 4];
        TournamentEditor.UpdateParticipant(t, stale.Id, Player("P1 edited") with { FirstEligibleRound = 1, NotPlayingRounds = [] });
        Assert.Equal(3, stale.FirstEligibleRound); // input for hidden fields is ignored; stored values are kept
        Assert.Equal([2, 4], stale.NotPlayingRounds);
        Assert.Contains(stale.Id, PairingSchedule.DescribeRoundRobin(t)!.Eligible);

        Draw(t); // the stale flags neither exclude the player nor block the draw
        Assert.Equal(3, t.NumberOfRounds);
        Assert.Contains(stale.Id, t.Stages[0].FrozenRosterIds!);
        Assert.Equal([2, 4], stale.NotPlayingRounds);

        t.CurrentRound = 3;
        var standings = StandingsCalculator.Calculate(t);
        Assert.DoesNotContain(standings.Rows.SelectMany(r => r.Rounds), c => c.Kind is CellKind.NotJoined or CellKind.NotPlaying);
        Assert.DoesNotContain(StandingsCalculator.NoticeSkippedRounds, standings.Notices);
    }

    [Fact]
    public void A_round_robin_followed_by_another_system_keeps_the_explicit_participation_rules()
    {
        var t = RoundRobin(4);
        t.NumberOfRounds = 7;
        PairingSchedule.ChangeSystem(t, 4, TournamentSystem.Swiss, false);
        Assert.False(t.IsPureRoundRobin());
        TournamentEditor.UpdateSettings(t, Settings("RR", TournamentSystem.Swiss, 8)); // editable again
        Assert.Equal(8, t.NumberOfRounds);
        Draw(t, all: false); // round 2 stays undrawn, so only the roster rule can refuse
        var ex = Assert.Throws<DomainException>(() =>
            TournamentEditor.UpdateParticipant(t, 2, Player("P2", rating: 1020) with { NotPlayingRounds = [2] }));
        Assert.Equal("participant.rosterMemberUnavailable", ex.Code);
    }

    [Fact]
    public void Finalizing_and_numbering_use_round_one_in_a_pure_round_robin()
    {
        var t = RoundRobin(2, preliminary: true);
        t.Participants[0].FirstEligibleRound = 4;
        var result = TournamentEditor.FinalizeAllPreliminary(t);
        Assert.Equal(2, result.Finalized.Count);
        TournamentEditor.SetStartingNumbers(t, [new StartingNumberEntry(t.Participants[0].Id, 1)]);
        Assert.Equal(1, t.Participants[0].StartingNumber);
    }

    // 8. Technical defeat = a normal referee result on the scheduled game.
    [Fact]
    public void Technical_defeat_is_a_referee_result_on_the_scheduled_game()
    {
        var t = RoundRobin(4);
        Draw(t);
        var game = t.Rounds[0].Pairings[0];
        TournamentEditor.UpdatePairing(t, game.Id, Edit(game, GameResult.WhiteWins, byReferee: true), false);
        Assert.True(game.ResultByReferee);
        Assert.Equal(4, t.Stages[0].FrozenRosterIds!.Count);
        Assert.Equal(6, t.AllPairings().Count(x => !x.Pairing.IsBye));
    }

    // 15. Both win: one point and one win each, everywhere.
    [Fact]
    public void Both_win_credits_a_point_and_a_win_to_each_player_without_notices()
    {
        var t = TournamentEditor.Create(Settings("S", TournamentSystem.Swiss, 2), DateTimeOffset.UnixEpoch);
        foreach (var name in new[] { "A", "B", "C", "D" }) TournamentEditor.AddParticipant(t, Player(name));
        TournamentEditor.AddPairing(t, 1, Pair(1, 2, GameResult.BothWin));
        TournamentEditor.AddPairing(t, 1, Pair(3, 4, GameResult.BlackWins));
        t.CurrentRound = 1;

        var s = StandingsCalculator.Calculate(t); // default Swiss criteria: wins, SOS, SODOS, SOSOS
        decimal V(int id, string key) => s.Rows.Single(r => r.ParticipantId == id).Values[key]!.Value;
        Assert.Equal(1, V(1, CriterionKeys.Wins));
        Assert.Equal(1, V(2, CriterionKeys.Wins));
        Assert.Equal(1, V(1, CriterionKeys.Sos)); // the opponent's single point
        Assert.Equal(1, V(2, CriterionKeys.Sodos)); // a defeated opponent with one point
        Assert.Equal(0, V(4, CriterionKeys.Wins));
        Assert.Equal(1, s.Rows.Single(r => r.ParticipantId == 1).Place);
        Assert.Equal(2, s.Rows.Single(r => r.ParticipantId == 1).PlaceTo); // A and B share first place
        Assert.Empty(s.Notices);
        var cell = s.Rows.Single(r => r.ParticipantId == 2).Rounds[0];
        Assert.Equal(CellOutcome.Win, cell.Outcome);

        t.CurrentRound = 2;
        var prep = StandingsCalculator.CalculatePreparation(t);
        Assert.Equal(1, prep.Rows.Single(r => r.ParticipantId == 1).GroupScore);
        Assert.Equal(1, prep.Rows.Single(r => r.ParticipantId == 2).GroupScore);
    }

    [Fact]
    public void Both_win_survives_colour_swap_and_reload_and_is_protected_like_any_result()
    {
        var t = TournamentEditor.Create(Settings("S", TournamentSystem.Swiss, 2), DateTimeOffset.UnixEpoch);
        TournamentEditor.AddParticipant(t, Player("A"));
        TournamentEditor.AddParticipant(t, Player("B"));
        TournamentEditor.AddParticipant(t, Player("C"));
        var game = TournamentEditor.AddPairing(t, 1, Pair(1, 2, GameResult.BothWin));

        TournamentEditor.UpdatePairing(t, game.Id, Edit(game, GameResult.BothWin, black: 2, white: 1), false); // no confirmation
        Assert.Equal(2, game.BlackId);
        Assert.Equal(GameResult.BothWin, game.Result);

        Assert.Equal("pairing.resultWouldBeDiscarded",
            Assert.Throws<DomainException>(() => TournamentEditor.UpdatePairing(t, game.Id, Edit(game, GameResult.BothWin, white: 3), false)).Code);
        Assert.Equal("pairing.resultWouldBeDiscarded",
            Assert.Throws<DomainException>(() => TournamentEditor.DeletePairing(t, game.Id, false)).Code);

        Assert.Contains("\"bothWin\"", Json(t));
        Assert.Equal(GameResult.BothWin, Clone(t).AllPairings().Single().Pairing.Result);
    }

    // 16. Legacy XML: "1-1" is the verified legacy token for both win.
    [Fact]
    public void Legacy_one_one_token_imports_as_both_win_and_round_trips_unchanged()
    {
        var source = Encoding.UTF8.GetString(Fixtures.Read(Fixtures.ReferenceRelativePath));
        var first = source.IndexOf("<Result>1-0</Result>", StringComparison.Ordinal);
        var xml = Encoding.UTF8.GetBytes(source[..first] + "<Result>1-1</Result>" + source[(first + "<Result>1-0</Result>".Length)..]);

        var t = LegacyXmlImporter.Import(xml, DateTimeOffset.UnixEpoch);
        var both = t.AllPairings().Where(x => x.Pairing.Result == GameResult.BothWin).ToList();
        Assert.Single(both);
        Assert.Null(both[0].Pairing.LegacyResultToken);
        Assert.Equal(29, t.AllPairings().Count(x => x.Pairing.Result is GameResult.BlackWins or GameResult.WhiteWins)); // 30 − 1

        var exported = LegacyXmlImporter.Parse(LegacyXmlExporter.ToBytes(LegacyXmlExporter.Export(t)));
        Assert.True(XNode.DeepEquals(Normalize(LegacyXmlImporter.Parse(xml).Root!), Normalize(exported.Root!)));
        Assert.Empty(ExportAnalyzer.Analyze(t).Limitations);
    }

    [Fact]
    public void Newly_entered_both_win_exports_as_one_one_without_a_limitation_and_unknown_tokens_stay_opaque()
    {
        var t = Fixtures.Import();
        var game = t.Rounds.Single(r => r.Number == 6).Pairings.Single(p => p.BoardNumber == 2);
        game.Result = GameResult.BothWin;
        var xml = Encoding.UTF8.GetString(LegacyXmlExporter.ToBytes(LegacyXmlExporter.Export(t)));
        Assert.Contains("<Result>1-1</Result>", xml);
        Assert.DoesNotContain(ExportAnalyzer.Analyze(t).Limitations, l => l.Field == "result");

        var other = Fixtures.Import();
        var odd = other.Rounds[0].Pairings[0];
        odd.Result = GameResult.Unknown;
        odd.LegacyResultToken = "S-S";
        var back = LegacyXmlImporter.Import(LegacyXmlExporter.ToBytes(LegacyXmlExporter.Export(other)), DateTimeOffset.UnixEpoch);
        Assert.Equal(GameResult.Unknown, back.Rounds[0].Pairings[0].Result);
        Assert.Equal("S-S", back.Rounds[0].Pairings[0].LegacyResultToken);

        var draw = Fixtures.Import();
        draw.Rounds[0].Pairings[0].Result = GameResult.Draw; // still unmapped: reported, never invented
        Assert.Contains(ExportAnalyzer.Analyze(draw).Limitations, l => l.Field == "result");
    }

    private static XElement Normalize(XElement e) =>
        new(e.Name, e.Attributes().OrderBy(a => a.Name.ToString()),
            e.HasElements ? e.Elements().Select(Normalize) : (object)e.Value);
}
