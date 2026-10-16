using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AutoKorsak.Api;
using AutoKorsak.Domain;
using AutoKorsak.LegacyXml;
using static AutoKorsak.Tests.LegacyXmlTests;

namespace AutoKorsak.Tests;

/// <summary>Acceptance tests for spec 0001 (pairing stages, round robin, byes, joining rounds, preparation view).</summary>
public class Spec0001Tests
{
    private sealed class FixedRandom(params int[] values) : IRandomSource
    {
        private int _i;
        public int Next(int maxExclusive) => values[_i++ % values.Length] % maxExclusive;
    }

    private static readonly IRandomSource Ordered = new FixedRandom(0);

    private static Tournament RoundRobin(int players, int? rounds = null)
    {
        var cycle = players % 2 == 0 ? players - 1 : players;
        var t = TournamentEditor.Create(Settings("RR", TournamentSystem.RoundRobin, rounds ?? cycle), DateTimeOffset.UnixEpoch);
        for (var i = 1; i <= players; i++) TournamentEditor.AddParticipant(t, Player($"P{i}", rating: 2000 - i * 10));
        return t;
    }

    private static GenerationResult Generate(Tournament t, int round, bool all = false, bool random = false, bool confirm = false, IRandomSource? source = null) =>
        PairingSchedule.GenerateRoundRobin(t, round, all, random, confirm, source ?? Ordered);

    private static string Json(Tournament t) => JsonSerializer.Serialize(t, Storage.Json);

    private static Tournament Clone(Tournament t) => JsonSerializer.Deserialize<Tournament>(Json(t), Storage.Json)!;

    private static List<(int, int?)> Fixture(Tournament t, int round) =>
        t.Rounds.Single(r => r.Number == round).Pairings.Select(p => (p.BlackId!.Value, p.WhiteId)).OrderBy(x => x.Item1).ToList();

    private static void AssertFullCycle(Tournament t, int players)
    {
        var rounds = players % 2 == 0 ? players - 1 : players;
        var games = t.AllPairings().Where(x => !x.Pairing.IsBye).Select(x => x.Pairing).ToList();
        Assert.Equal(players * (players - 1) / 2, games.Count);
        Assert.Equal(games.Count, games.Select(g => (Math.Min(g.BlackId!.Value, g.WhiteId!.Value), Math.Max(g.BlackId.Value, g.WhiteId.Value))).Distinct().Count());
        Assert.All(t.Rounds.Where(r => r.Number <= rounds), r => Assert.Equal(players / 2, r.Pairings.Count(p => !p.IsBye)));
        foreach (var p in t.Participants)
        {
            var black = games.Count(g => g.BlackId == p.Id);
            var white = games.Count(g => g.WhiteId == p.Id);
            Assert.Equal(players - 1, black + white);
            Assert.True(Math.Abs(black - white) <= 1, $"{p.DisplayName}: {black} black / {white} white");
        }
    }

    // 1. Four players
    [Fact]
    public void Four_player_round_robin_follows_the_Berger_table()
    {
        var t = RoundRobin(4);
        Generate(t, 1, all: true);

        Assert.Equal(3, t.Rounds.Count);
        AssertFullCycle(t, 4);
        // FIDE Berger table for 4 (first listed = first move = Black): 1-4 2-3 | 4-3 1-2 | 2-4 3-1
        Assert.Equal([(1, (int?)4), (2, 3)], Fixture(t, 1));
        Assert.Equal([(1, (int?)2), (4, 3)], Fixture(t, 2));
        Assert.Equal([(2, (int?)4), (3, 1)], Fixture(t, 3));
        Assert.All(t.AllPairings(), x => Assert.False(x.Pairing.Protected));
        Assert.All(t.AllPairings(), x => Assert.Equal(GameResult.Unset, x.Pairing.Result));
    }

    [Fact]
    public void Six_player_schedule_matches_the_FIDE_Berger_table()
    {
        string Row(int r) => string.Join(' ', PairingSchedule.BergerRound(6, r - 1).Games.Select(g => $"{g.Black}-{g.White}"));
        Assert.Equal("1-6 2-5 3-4", Row(1));
        Assert.Equal("6-4 5-3 1-2", Row(2));
        Assert.Equal("2-6 3-1 4-5", Row(3));
        Assert.Equal("6-5 1-4 2-3", Row(4));
        Assert.Equal("3-6 4-2 5-1", Row(5));
    }

    // 2. Five players
    [Fact]
    public void Five_player_round_robin_has_one_bye_per_round_and_per_player()
    {
        var t = RoundRobin(5);
        Generate(t, 1, all: true);

        Assert.Equal(5, t.Rounds.Count);
        Assert.Equal(5, t.Participants.Count); // no fictitious participant
        AssertFullCycle(t, 5);
        Assert.All(t.Rounds, r => Assert.Single(r.Pairings, p => p.IsBye));
        Assert.All(t.Rounds, r => Assert.Null(r.Pairings.Single(p => p.IsBye).WhiteId));
        var byes = t.AllPairings().Where(x => x.Pairing.IsBye).Select(x => x.Pairing.BlackId!.Value).Order().ToList();
        Assert.Equal(t.Participants.Select(p => p.Id).Order(), byes);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(11)]
    [InlineData(12)]
    public void Full_cycles_are_complete_and_colour_balanced(int players)
    {
        var t = RoundRobin(players);
        Generate(t, 1, all: true);
        AssertFullCycle(t, players);
    }

    // 3. Starting numbers
    [Fact]
    public void Starting_numbers_follow_registration_order_or_one_persisted_random_permutation()
    {
        var ordered = RoundRobin(5);
        Generate(ordered, 2);
        Assert.Equal([1, 2, 3, 4, 5], ordered.Stages[0].FrozenRosterIds);
        Assert.False(ordered.Stages[0].RandomStartingNumbers);

        var random = RoundRobin(5);
        Generate(random, 1, random: true, source: new FixedRandom(1, 3, 0, 1));
        var roster = random.Stages[0].FrozenRosterIds!;
        Assert.True(random.Stages[0].RandomStartingNumbers);
        Assert.Equal([1, 2, 3, 4, 5], roster.Order());
        Assert.NotEqual([1, 2, 3, 4, 5], roster);

        // Later requests reuse the persisted mapping whatever the checkbox says, also after a reload.
        var reloaded = Clone(random);
        Generate(reloaded, 3, random: false);
        Generate(reloaded, 4, random: true, source: new FixedRandom(4, 2, 1, 0));
        Assert.Equal(roster, reloaded.Stages[0].FrozenRosterIds);
        var (games, _) = PairingSchedule.BergerRound(5, 2);
        var expected = games.Select(g => (roster[g.Black - 1], (int?)roster[g.White - 1])).OrderBy(x => x.Item1).ToList();
        Assert.Equal(expected, Fixture(reloaded, 3).Where(x => x.Item2 is not null).ToList());
    }

    // 4 and 6. Order independence, scope, idempotence
    [Fact]
    public void Out_of_order_generation_equals_all_round_generation_and_is_idempotent()
    {
        var single = RoundRobin(6);
        foreach (var r in new[] { 4, 1, 5, 3, 2 }) Generate(single, r);
        var all = RoundRobin(6);
        Generate(all, 3, all: true);

        for (var r = 1; r <= 5; r++) Assert.Equal(Fixture(all, r), Fixture(single, r));

        var before = Json(all);
        var result = Generate(all, 1, all: true);
        Assert.Equal(0, result.Created);
        Assert.Equal(before, Json(all));
    }

    [Fact]
    public void Single_round_generation_touches_only_that_round()
    {
        var t = RoundRobin(4);
        Generate(t, 2);
        Assert.Equal([2], t.Rounds.Where(r => r.Pairings.Count > 0).Select(r => r.Number));
    }

    // 5. Stage length checks happen at generation
    [Fact]
    public void Terminal_round_robin_stage_needs_exactly_one_cycle_checked_at_generation()
    {
        var t = RoundRobin(4, rounds: 5); // settings save is fine during registration
        Assert.Equal("generate.roundCountMismatch", Assert.Throws<DomainException>(() => Generate(t, 1)).Code);
        Assert.Null(t.Stages[0].FrozenRosterIds);

        var prefix = RoundRobin(4, rounds: 7);
        PairingSchedule.ChangeSystem(prefix, 4, TournamentSystem.Swiss, false);
        Generate(prefix, 1, all: true); // three RR rounds of the cycle, then Swiss
        Assert.Equal([1, 2, 3], prefix.Rounds.Where(r => r.Pairings.Count > 0).Select(r => r.Number));

        var tooLong = RoundRobin(4, rounds: 7);
        PairingSchedule.ChangeSystem(tooLong, 5, TournamentSystem.Swiss, false);
        Assert.Equal("generate.stageTooLong", Assert.Throws<DomainException>(() => Generate(tooLong, 1)).Code);
    }

    // 7. Played pairings are immutable
    [Fact]
    public void Played_game_with_swapped_colours_is_compatible_and_left_untouched()
    {
        var t = RoundRobin(4);
        Generate(t, 1);
        var game = t.Rounds[0].Pairings[0];
        TournamentEditor.UpdatePairing(t, game.Id,
            new PairingInput(game.WhiteId, game.BlackId, false, game.BoardNumber, true, 2, 0.5m, false, GameResult.WhiteWins, true, "swapped", false), false);
        var stored = JsonSerializer.Serialize(game, Storage.Json);

        Generate(t, 1, all: true);

        Assert.Equal(stored, JsonSerializer.Serialize(t.Rounds[0].Pairings.Single(p => p.Id == game.Id), Storage.Json));
        Assert.Equal(2, t.Rounds[0].Pairings.Count);
    }

    [Fact]
    public void Played_wrong_opponent_blocks_the_whole_request_with_zero_changes()
    {
        var t = RoundRobin(4);
        // Round 2 should be 1-2 and 4-3; record a played 1-3 game instead.
        TournamentEditor.AddPairing(t, 2, Pair(1, 3, GameResult.BlackWins));
        var before = Json(t);

        var ex = Assert.Throws<DomainException>(() => Generate(t, 1, all: true, confirm: true));

        Assert.Equal("generate.playedPairingConflicts", ex.Code);
        Assert.Equal(before, Json(t)); // no roster freeze, no pairings, no ID increments
    }

    // 8. Unplayed replacement needs confirmation
    [Fact]
    public void Replacing_unplayed_pairings_requires_confirmation()
    {
        var t = RoundRobin(4);
        TournamentEditor.AddPairing(t, 1, Pair(1, 2)); // unplayed, not the round-1 fixture
        var before = Json(t);

        var ex = Assert.Throws<DomainException>(() => Generate(t, 1));
        Assert.Equal("generate.replaceUnplayed", ex.Code);
        Assert.True(ex.IsConflict);
        Assert.Equal(before, Json(t));

        var result = Generate(t, 1, confirm: true);
        Assert.Equal(1, result.Replaced);
        Assert.Equal([(1, (int?)4), (2, 3)], Fixture(t, 1));
    }

    // 9. No manual protection in RR
    [Fact]
    public void Round_robin_pairings_are_never_protected_but_other_systems_keep_protection()
    {
        var t = RoundRobin(4);
        var manual = TournamentEditor.AddPairing(t, 1, Pair(1, 4) with { Protected = true });
        Assert.False(manual.Protected);

        var swiss = TournamentEditor.Create(Settings("Swiss", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        TournamentEditor.AddParticipant(swiss, Player("A"));
        TournamentEditor.AddParticipant(swiss, Player("B"));
        Assert.True(TournamentEditor.AddPairing(swiss, 1, Pair(1, 2) with { Protected = true }).Protected);
    }

    [Fact]
    public void Imported_protection_does_not_override_the_round_robin_schedule()
    {
        var t = RoundRobin(4);
        var p = TournamentEditor.AddPairing(t, 1, Pair(1, 2));
        p.Protected = true; // as imported legacy metadata
        Assert.Equal("generate.replaceUnplayed", Assert.Throws<DomainException>(() => Generate(t, 1)).Code);
        Generate(t, 1, confirm: true);
        Assert.Equal([(1, (int?)4), (2, 3)], Fixture(t, 1));
    }

    // 10. Withdrawal handled by referee-decision results
    [Fact]
    public void Withdrawn_player_stays_on_schedule_and_referee_results_survive_regeneration()
    {
        var t = RoundRobin(4);
        Generate(t, 1, all: true);
        foreach (var (_, p) in t.AllPairings().Where(x => x.Pairing.Involves(4)).ToList())
        {
            var result = p.BlackId == 4 ? GameResult.WhiteWins : GameResult.BlackWins;
            TournamentEditor.UpdatePairing(t, p.Id, new PairingInput(p.BlackId, p.WhiteId, false, p.BoardNumber, false, 0, 0, false, result, true, "withdrawn", false), false);
        }
        var reloaded = Clone(t);
        Generate(reloaded, 1, all: true);
        var technical = reloaded.AllPairings().Where(x => x.Pairing.Involves(4)).Select(x => x.Pairing).ToList();
        Assert.Equal(3, technical.Count);
        Assert.All(technical, p => Assert.True(p.ResultByReferee));
        Assert.Contains(4, reloaded.Stages[0].FrozenRosterIds!);
    }

    // 11. Bye scoring
    [Fact]
    public void Bye_is_one_win_and_one_point_and_feeds_opponents_SOS()
    {
        var t = RoundRobin(3);
        Generate(t, 1, all: true);
        // Every game: the lower ID wins.
        foreach (var (_, p) in t.AllPairings().Where(x => !x.Pairing.IsBye).ToList())
            TournamentEditor.UpdatePairing(t, p.Id, new PairingInput(p.BlackId, p.WhiteId, false, p.BoardNumber, false, 0, 0, false,
                p.BlackId < p.WhiteId ? GameResult.BlackWins : GameResult.WhiteWins, false, "", false), false);

        t.CurrentRound = 3;
        var s = StandingsCalculator.Calculate(t);
        decimal Get(int id, string key) => s.Rows.Single(r => r.ParticipantId == id).Values[key]!.Value;
        // P1: 2 game wins + bye; P2: 1 + bye; P3: 0 + bye.
        Assert.Equal(3m, Get(1, CriterionKeys.Wins));
        Assert.Equal(2m, Get(2, CriterionKeys.Wins));
        Assert.Equal(1m, Get(3, CriterionKeys.Wins));
        Assert.Equal(2m + 1m, Get(1, CriterionKeys.Sos)); // opponents P2 (2) and P3 (1); no bye opponent
        Assert.DoesNotContain("byesNotScored", s.Notices);
        Assert.All(s.Rows, r => Assert.Single(r.Rounds, c => c.Kind == CellKind.Bye));
    }

    [Fact]
    public void Manual_bye_scores_in_a_swiss_tournament_too()
    {
        var t = TournamentEditor.Create(Settings("Swiss", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        TournamentEditor.AddParticipant(t, Player("A"));
        TournamentEditor.AddPairing(t, 1, Pair(1, 0) with { IsBye = true, WhiteId = null });
        Assert.Equal(1m, StandingsCalculator.Calculate(t).Rows.Single().Values[CriterionKeys.Wins]);
    }

    // 12 and 14. Stage transition
    [Fact]
    public void Round_robin_to_swiss_keeps_history_and_enables_no_swiss_generator()
    {
        var t = RoundRobin(4, rounds: 7);
        PairingSchedule.ChangeSystem(t, 4, TournamentSystem.Swiss, false);
        Generate(t, 1, all: true);
        foreach (var (_, p) in t.AllPairings().ToList())
            TournamentEditor.UpdatePairing(t, p.Id, new PairingInput(p.BlackId, p.WhiteId, false, p.BoardNumber, false, 0, 0, false, GameResult.BlackWins, false, "", false), false);
        var history = JsonSerializer.Serialize(t.Rounds, Storage.Json);

        var reloaded = Clone(t);
        Assert.Equal(TournamentSystem.Swiss, reloaded.System);
        Assert.Equal([1, 4], reloaded.Stages.Select(s => s.StartRound));
        Assert.Equal(JsonSerializer.Serialize(reloaded.Rounds, Storage.Json), history);
        Assert.Equal("generate.notAvailable", Assert.Throws<DomainException>(() => Generate(reloaded, 4)).Code);
        reloaded.CurrentRound = 4;
        var s = StandingsCalculator.Calculate(reloaded);
        Assert.Equal(6m, s.Rows.Sum(r => r.Values[CriterionKeys.Wins]!.Value)); // 6 RR games still count
    }

    [Fact]
    public void Stage_change_rules_protect_played_rounds_and_require_confirmation()
    {
        var t = RoundRobin(4, rounds: 7);
        PairingSchedule.ChangeSystem(t, 4, TournamentSystem.Swiss, false);
        Generate(t, 1, all: true);
        TournamentEditor.AddPairing(t, 5, Pair(1, 2)); // unplayed Swiss-stage pairing

        var noConfirm = Assert.Throws<DomainException>(() => PairingSchedule.ChangeSystem(t, 5, TournamentSystem.McMahon, false));
        Assert.Equal("stage.unplayedPairingsWillBeDeleted", noConfirm.Code);
        Assert.Equal("stage.notAfterLastBoundary",
            Assert.Throws<DomainException>(() => PairingSchedule.ChangeSystem(t, 3, TournamentSystem.McMahon, true)).Code);
        Assert.Equal("stage.roundRobinAfterOtherSystem",
            Assert.Throws<DomainException>(() => PairingSchedule.ChangeSystem(t, 6, TournamentSystem.RoundRobin, true)).Code);

        var played = TournamentEditor.AddPairing(t, 6, Pair(3, 4, GameResult.BlackWins));
        var before = Json(t);
        Assert.Equal("stage.roundsHaveResults",
            Assert.Throws<DomainException>(() => PairingSchedule.ChangeSystem(t, 5, TournamentSystem.McMahon, true)).Code);
        Assert.Equal(before, Json(t));

        TournamentEditor.DeletePairing(t, played.Id, true);
        var result = PairingSchedule.ChangeSystem(t, 5, TournamentSystem.McMahon, true);
        Assert.Equal(1, result.DeletedPairings);
        Assert.Equal(TournamentSystem.McMahon, t.System);
        Assert.Equal(6, t.AllPairings().Count(x => x.Round.Number <= 3)); // RR rounds kept
    }

    // 13 and 21. Joining rounds
    [Fact]
    public void Late_swiss_entrant_has_no_false_absences_and_round_robin_roster_is_unchanged()
    {
        var t = RoundRobin(4, rounds: 7);
        PairingSchedule.ChangeSystem(t, 4, TournamentSystem.Swiss, false);
        Generate(t, 1, all: true);
        foreach (var (_, p) in t.AllPairings().ToList())
            TournamentEditor.UpdatePairing(t, p.Id, new PairingInput(p.BlackId, p.WhiteId, false, p.BoardNumber, false, 0, 0, false, GameResult.BlackWins, false, "", false), false);
        TournamentEditor.AddPairing(t, 4, Pair(1, 2, GameResult.BlackWins));
        TournamentEditor.AddPairing(t, 4, Pair(3, 4, GameResult.BlackWins));
        TournamentEditor.AddPairing(t, 5, Pair(1, 3, GameResult.BlackWins));
        TournamentEditor.AddPairing(t, 5, Pair(2, 4, GameResult.BlackWins));
        t.CurrentRound = 6;

        var late = TournamentEditor.AddParticipant(t, Player("Late"));

        Assert.Equal(6, late.FirstEligibleRound);
        Assert.Equal([1, 2, 3, 4], t.Stages[0].FrozenRosterIds);
        var s = StandingsCalculator.Calculate(t);
        Assert.DoesNotContain(StandingsCalculator.NoticeSkippedRounds, s.Notices);
        Assert.All(s.Rows.Single(r => r.ParticipantId == late.Id).Rounds.Take(5), c => Assert.Equal(CellKind.NotJoined, c.Kind));
        Assert.Equal("pairing.playerNotJoined",
            Assert.Throws<DomainException>(() => TournamentEditor.AddPairing(t, 5, Pair(late.Id, 1))).Code);
    }

    [Fact]
    public void Round_robin_entry_is_only_at_the_stage_start_before_the_draw_and_never_after()
    {
        var t = RoundRobin(4);
        Assert.Equal("participant.joinMidRoundRobin",
            Assert.Throws<DomainException>(() => TournamentEditor.AddParticipant(t, Player("Mid") with { FirstEligibleRound = 2 })).Code);
        t.CurrentRound = 2;
        Assert.Equal(1, TournamentEditor.AddParticipant(t, Player("P5")).FirstEligibleRound); // pre-draw default normalized to start
        TournamentEditor.UpdateSettings(t, Settings("RR", TournamentSystem.RoundRobin, 5));

        var pending = t.Participants.Single(p => p.DisplayName == "P5");
        pending.FirstEligibleRound = 3; // inconsistent registration (e.g. older data)
        Assert.Equal("generate.participantJoinsMidRoundRobin", Assert.Throws<DomainException>(() => Generate(t, 1)).Code);
        pending.FirstEligibleRound = 1;

        Generate(t, 1);
        Assert.Equal("participant.noStageAfterRoundRobin",
            Assert.Throws<DomainException>(() => TournamentEditor.AddParticipant(t, Player("After freeze"))).Code);
        var roundOne = t.Rounds[0].Pairings.Single(p => p.Involves(pending.Id));
        TournamentEditor.DeletePairing(t, roundOne.Id, confirmDiscardResult: false);
        Assert.Equal("participant.rosterMember",
            Assert.Throws<DomainException>(() => TournamentEditor.DeleteParticipant(t, pending.Id)).Code);
        Assert.Equal("participant.rosterMemberPreliminary",
            Assert.Throws<DomainException>(() => TournamentEditor.UpdateParticipant(t, pending.Id, Player("P5", preliminary: true))).Code);
    }

    // 15. Legacy XML
    [Fact]
    public void Pure_round_robin_export_is_blocked_and_mixed_tournament_exports_as_swiss_with_reported_losses()
    {
        var pure = RoundRobin(4);
        Generate(pure, 1, all: true);
        var blocked = ExportAnalyzer.Analyze(pure);
        Assert.True(blocked.Blocked);
        Assert.Equal("export.systemNotMappable", blocked.BlockingCode);

        var mixed = RoundRobin(4, rounds: 7);
        PairingSchedule.ChangeSystem(mixed, 4, TournamentSystem.Swiss, false);
        Generate(mixed, 1, all: true);
        mixed.CurrentRound = 4;
        TournamentEditor.AddParticipant(mixed, Player("Late"));

        var xml = LegacyXmlExporter.Export(mixed);
        Assert.Equal("2", xml.Root!.Element("Kind")!.Value);
        Assert.Equal(6, xml.Root.Elements("TournamentRound").Where(r => (int)r.Element("RoundNumber")! <= 3).SelectMany(r => r.Elements("Pairing")).Count());
        var report = ExportAnalyzer.Analyze(mixed);
        Assert.False(report.Blocked);
        Assert.Contains(report.Limitations, l => l.Field == "stages");
        Assert.Contains(report.Limitations, l => l.Field == "firstEligibleRound" && l.Count == 1);
    }

    // 16. Old data
    [Fact]
    public void Pre_0001_json_loads_as_a_single_stage_without_touching_history()
    {
        var t = Fixtures.Import();
        var node = JsonNode.Parse(Json(t))!.AsObject();
        node.Remove("stages");
        foreach (var p in node["participants"]!.AsArray()) p!.AsObject().Remove("firstEligibleRound");
        var old = node.ToJsonString();

        var loaded = JsonSerializer.Deserialize<Tournament>(old, Storage.Json)!;
        loaded.EnsureStages();

        Assert.Single(loaded.Stages);
        Assert.Equal(TournamentSystem.Swiss, loaded.Stages[0].System);
        Assert.All(loaded.Participants, p => Assert.Equal(1, p.FirstEligibleRound));
        Assert.Equal(JsonSerializer.Serialize(t.Rounds, Storage.Json), JsonSerializer.Serialize(loaded.Rounds, Storage.Json));
        Assert.Equal(StandingsCalculator.Calculate(t), StandingsCalculator.Calculate(loaded), new StandingsComparer());
    }

    [Fact]
    public void Unidentified_imported_stage_stays_unknown_and_has_no_generator()
    {
        var t = Fixtures.Import(Fixtures.AvailabilityRelativePath);
        Assert.Null(t.Stages.Single().System);
        Assert.Equal("generate.notAvailable", Assert.Throws<DomainException>(() => Generate(t, 1)).Code);
    }

    // 17. Preparation view
    [Fact]
    public void Preparation_view_uses_points_before_the_round_then_rating_not_coefficients()
    {
        var t = TournamentEditor.Create(Settings("Swiss", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        var a = TournamentEditor.AddParticipant(t, Player("A", rating: 1000));
        var b = TournamentEditor.AddParticipant(t, Player("B", rating: 1500));
        var c = TournamentEditor.AddParticipant(t, Player("C", rating: 1200));
        var d = TournamentEditor.AddParticipant(t, Player("D", rating: 1100));
        TournamentEditor.AddPairing(t, 1, Pair(a.Id, c.Id, GameResult.BlackWins));
        TournamentEditor.AddPairing(t, 1, Pair(b.Id, d.Id, GameResult.BlackWins));
        TournamentEditor.AddPairing(t, 2, Pair(a.Id, b.Id, GameResult.BlackWins)); // round 2 result must be ignored
        t.CurrentRound = 2;

        var prep = StandingsCalculator.CalculatePreparation(t);

        Assert.Equal([1], prep.ScoredRounds);
        // A and B both have 1 point before round 2: B first by rating; then C (1200) before D (1100).
        Assert.Equal([b.Id, a.Id, c.Id, d.Id], prep.Rows.Select(r => r.ParticipantId));
        Assert.Equal([1m, 1m, 0m, 0m], prep.Rows.Select(r => r.GroupScore));
        var bRow = prep.Rows[0];
        Assert.Equal(4, bRow.Rounds[0].OpponentPosition); // D is displayed in row 4
        // The official standings are unaffected and still count round 2.
        Assert.Equal(a.Id, StandingsCalculator.Calculate(t).Rows[0].ParticipantId);
    }

    // 18. McMahon preparation
    [Fact]
    public void McMahon_preparation_adds_start_scores_and_byes()
    {
        var t = TournamentEditor.Create(Settings("MM", TournamentSystem.McMahon), DateTimeOffset.UnixEpoch);
        var a = TournamentEditor.AddParticipant(t, Player("A"));
        var b = TournamentEditor.AddParticipant(t, Player("B"));
        var c = TournamentEditor.AddParticipant(t, Player("C"));
        a.StartScore = -2;
        b.StartScore = 1;
        TournamentEditor.AddPairing(t, 1, Pair(a.Id, b.Id, GameResult.BlackWins));
        TournamentEditor.AddPairing(t, 1, Pair(c.Id, 0) with { IsBye = true, WhiteId = null });
        t.CurrentRound = 2;

        var groups = StandingsCalculator.CalculatePreparation(t).Rows.ToDictionary(r => r.ParticipantId, r => r.GroupScore);

        Assert.Equal(-1m, groups[a.Id]);
        Assert.Equal(1m, groups[b.Id]);
        Assert.Equal(1m, groups[c.Id]);
    }

    // 19. Settings system lock
    [Fact]
    public void Settings_system_is_editable_only_before_any_pairing_and_with_a_single_stage()
    {
        var t = RoundRobin(4);
        TournamentEditor.UpdateSettings(t, Settings("RR", TournamentSystem.Swiss, 3));
        Assert.Equal(TournamentSystem.Swiss, t.Stages.Single().System);
        TournamentEditor.UpdateSettings(t, Settings("RR", TournamentSystem.RoundRobin, 3));

        Generate(t, 1);
        Assert.True(TournamentEditor.IsSystemLocked(t));
        Assert.Equal("settings.systemLocked",
            Assert.Throws<DomainException>(() => TournamentEditor.UpdateSettings(t, Settings("RR", TournamentSystem.Swiss, 3))).Code);
        TournamentEditor.UpdateSettings(t, Settings("Renamed", TournamentSystem.RoundRobin, 3)); // other settings still editable

        var staged = TournamentEditor.Create(Settings("S", TournamentSystem.Swiss, 6), DateTimeOffset.UnixEpoch);
        PairingSchedule.ChangeSystem(staged, 3, TournamentSystem.McMahon, false);
        Assert.Equal("settings.systemLocked",
            Assert.Throws<DomainException>(() => TournamentEditor.UpdateSettings(staged, Settings("S", TournamentSystem.Swiss, 6))).Code);
    }

    // 20. Joining-round defaults
    [Fact]
    public void Joining_round_defaults_and_backfill()
    {
        var t = TournamentEditor.Create(Settings("Swiss", TournamentSystem.Swiss, 3), DateTimeOffset.UnixEpoch);
        var a = TournamentEditor.AddParticipant(t, Player("A"));
        var b = TournamentEditor.AddParticipant(t, Player("B"));
        Assert.Equal(1, a.FirstEligibleRound);

        TournamentEditor.AddPairing(t, 1, Pair(a.Id, b.Id)); // unplayed: round 1 has no recorded results
        Assert.Equal(1, TournamentEditor.AddParticipant(t, Player("C")).FirstEligibleRound);
        t.Rounds[0].Pairings[0].Result = GameResult.BlackWins;
        Assert.Equal(2, TournamentEditor.AddParticipant(t, Player("D")).FirstEligibleRound);
        Assert.Equal(1, TournamentEditor.AddParticipant(t, Player("Paper") with { FirstEligibleRound = 1 }).FirstEligibleRound);

        t.CurrentRound = 3;
        TournamentEditor.AddPairing(t, 3, Pair(a.Id, b.Id, GameResult.BlackWins));
        Assert.Equal("participant.joinRoundRequired",
            Assert.Throws<DomainException>(() => TournamentEditor.AddParticipant(t, Player("Too late"))).Code);
        Assert.Equal(3, t.NumberOfRounds); // no phantom round
    }

    // 22. Finalizing preliminary registrations
    [Fact]
    public void Finalization_never_admits_into_a_frozen_round_robin()
    {
        var t = RoundRobin(4);
        var prelim = TournamentEditor.AddParticipant(t, Player("Prelim", preliminary: true));
        Generate(t, 1);

        Assert.Equal("participant.noStageAfterRoundRobin",
            Assert.Throws<DomainException>(() => TournamentEditor.UpdateParticipant(t, prelim.Id, Player("Prelim"))).Code);
        var none = TournamentEditor.FinalizeAllPreliminary(t);
        Assert.Empty(none.Finalized);
        Assert.Equal("participant.noStageAfterRoundRobin", none.Skipped.Single().Code);
        Assert.True(prelim.Preliminary);

        var mixed = RoundRobin(4, rounds: 7);
        PairingSchedule.ChangeSystem(mixed, 4, TournamentSystem.Swiss, false);
        var early = TournamentEditor.AddParticipant(mixed, Player("Early", preliminary: true));
        var later = TournamentEditor.AddParticipant(mixed, Player("Later", preliminary: true) with { FirstEligibleRound = 5 });
        Generate(mixed, 1);

        var report = TournamentEditor.FinalizeAllPreliminary(mixed);
        Assert.Equal([later.Id], report.Finalized);
        Assert.Equal(early.Id, report.Skipped.Single().Id);
        Assert.Equal("participant.joinAfterRoundRobin", report.Skipped.Single().Code);
        Assert.Equal(4, report.Skipped.Single().Args["round"]);
    }

    // ---------- review of e7b57590 ----------

    // P1-01
    [Fact]
    public void Frozen_roster_member_cannot_become_unavailable_in_a_round_robin_round()
    {
        var t = RoundRobin(4);
        Generate(t, 1); // freezes the roster; round 2 not drawn yet
        var member = t.FindParticipant(2)!;
        var before = Json(t);

        var ex = Assert.Throws<DomainException>(() =>
            TournamentEditor.UpdateParticipant(t, member.Id, Player("P2", rating: member.Rating) with { NotPlayingRounds = [2] }));

        Assert.Equal("participant.rosterMemberUnavailable", ex.Code);
        Assert.True(ex.IsConflict);
        Assert.Equal(before, Json(t));
        Generate(t, 1, all: true); // the schedule stays generatable
        Assert.Equal(3, t.Rounds.Count(r => r.Pairings.Count == 2));
    }

    [Fact]
    public void Availability_after_a_round_robin_stage_stays_editable_for_roster_members()
    {
        var t = RoundRobin(4, rounds: 7);
        PairingSchedule.ChangeSystem(t, 4, TournamentSystem.Swiss, false);
        Generate(t, 1);
        var updated = TournamentEditor.UpdateParticipant(t, 2, Player("P2", rating: 1980) with { NotPlayingRounds = [5] });
        Assert.Equal([5], updated.NotPlayingRounds);
    }

    // P2-01
    [Fact]
    public void Settings_system_lock_is_remembered_after_all_pairings_are_deleted()
    {
        var t = RoundRobin(4);
        Generate(t, 1);
        foreach (var (_, p) in t.AllPairings().ToList()) TournamentEditor.DeletePairing(t, p.Id, false);
        Assert.Empty(t.AllPairings());

        var reloaded = Clone(t);
        Assert.True(TournamentEditor.IsSystemLocked(reloaded));
        Assert.Equal("settings.systemLocked",
            Assert.Throws<DomainException>(() => TournamentEditor.UpdateSettings(reloaded, Settings("RR", TournamentSystem.Swiss, 3))).Code);

        var swiss = TournamentEditor.Create(Settings("Swiss", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        TournamentEditor.AddParticipant(swiss, Player("A"));
        TournamentEditor.AddParticipant(swiss, Player("B"));
        var manual = TournamentEditor.AddPairing(swiss, 1, Pair(1, 2));
        TournamentEditor.DeletePairing(swiss, manual.Id, false);
        Assert.True(TournamentEditor.IsSystemLocked(Clone(swiss)));
    }

    // P2-02
    [Fact]
    public void Unplayed_pairing_with_swapped_colours_is_restored_only_after_confirmation()
    {
        var t = RoundRobin(4);
        Generate(t, 1);
        var p = t.Rounds[0].Pairings[0]; // 1-4
        TournamentEditor.UpdatePairing(t, p.Id, new PairingInput(p.WhiteId, p.BlackId, false, p.BoardNumber, false, 0, 0, false, GameResult.Unset, false, "", false), false);
        var before = Json(t);

        var ex = Assert.Throws<DomainException>(() => Generate(t, 1));
        Assert.Equal("generate.replaceUnplayed", ex.Code);
        Assert.Equal(before, Json(t));

        Generate(t, 1, confirm: true);
        Assert.Equal([(1, (int?)4), (2, 3)], Fixture(t, 1));
    }

    // P2-03
    [Fact]
    public void A_credited_bye_counts_as_an_outcome_for_joining_defaults()
    {
        var t = TournamentEditor.Create(Settings("Swiss", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        var a = TournamentEditor.AddParticipant(t, Player("A"));
        TournamentEditor.AddPairing(t, 1, Pair(a.Id, 0) with { IsBye = true, WhiteId = null });
        Assert.True(TournamentEditor.RoundHasResults(t, 1));
        Assert.Equal(2, TournamentEditor.AddParticipant(t, Player("B")).FirstEligibleRound);
    }

    [Fact]
    public void A_credited_bye_blocks_a_stage_change_and_needs_confirmation_to_delete()
    {
        var t = TournamentEditor.Create(Settings("Swiss", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        var a = TournamentEditor.AddParticipant(t, Player("A"));
        var bye = TournamentEditor.AddPairing(t, 3, Pair(a.Id, 0) with { IsBye = true, WhiteId = null });
        var before = Json(t);

        Assert.Equal("stage.roundsHaveResults",
            Assert.Throws<DomainException>(() => PairingSchedule.ChangeSystem(t, 3, TournamentSystem.McMahon, true)).Code);
        Assert.Equal(before, Json(t));
        Assert.Equal("pairing.resultWouldBeDiscarded",
            Assert.Throws<DomainException>(() => TournamentEditor.DeletePairing(t, bye.Id, false)).Code);
    }

    [Fact]
    public void A_credited_bye_that_conflicts_with_the_schedule_blocks_generation()
    {
        var t = RoundRobin(3); // round 1 fixture: bye for player 1, game 2-3
        TournamentEditor.AddPairing(t, 1, Pair(2, 0) with { IsBye = true, WhiteId = null });
        var before = Json(t);

        var ex = Assert.Throws<DomainException>(() => Generate(t, 1, confirm: true));

        Assert.Equal("generate.playedPairingConflicts", ex.Code);
        Assert.Equal(before, Json(t));
    }

    [Fact]
    public void The_fixture_bye_is_kept_when_regenerating_an_odd_round_robin()
    {
        var t = RoundRobin(5);
        Generate(t, 1, all: true);
        var byes = t.AllPairings().Where(x => x.Pairing.IsBye).Select(x => x.Pairing.Id).ToList();
        Generate(t, 1, all: true);
        Assert.Equal(byes, t.AllPairings().Where(x => x.Pairing.IsBye).Select(x => x.Pairing.Id).ToList());
    }

    private sealed class StandingsComparer : IEqualityComparer<StandingsResult>
    {
        public bool Equals(StandingsResult? x, StandingsResult? y) =>
            JsonSerializer.Serialize(x, Storage.Json) == JsonSerializer.Serialize(y, Storage.Json);

        public int GetHashCode(StandingsResult obj) => 0;
    }
}
