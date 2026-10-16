using System.Text.Json;
using System.Text.Json.Nodes;
using AutoKorsak.Api;
using AutoKorsak.Domain;
using AutoKorsak.LegacyXml;
using static AutoKorsak.Tests.LegacyXmlTests;

namespace AutoKorsak.Tests;

/// <summary>Acceptance tests for spec 0002 (manual starting numbers, round-count adjustment, clear draw, result lock).</summary>
public class Spec0002Tests
{
    private sealed class FixedRandom(params int[] values) : IRandomSource
    {
        private int _i;
        public int Next(int maxExclusive) => values[_i++ % values.Length] % maxExclusive;
    }

    private static Tournament RoundRobin(int players, int? rounds = null)
    {
        var cycle = players % 2 == 0 ? players - 1 : players;
        var t = TournamentEditor.Create(Settings("RR", TournamentSystem.RoundRobin, rounds ?? cycle), DateTimeOffset.UnixEpoch);
        for (var i = 1; i <= players; i++) TournamentEditor.AddParticipant(t, Player($"P{i}", rating: 1000 + i * 10));
        return t;
    }

    private static void Number(Tournament t, params (int Id, int? Number)[] entries) =>
        TournamentEditor.SetStartingNumbers(t, entries.Select(e => new StartingNumberEntry(e.Id, e.Number)).ToList());

    private static GenerationResult Generate(Tournament t, int round = 1, bool all = false, StartingNumberSource source = StartingNumberSource.Entered,
        int? roundCount = null, bool confirm = false, IRandomSource? random = null) =>
        PairingSchedule.GenerateRoundRobin(t, round, all, source, roundCount, confirm, random ?? new FixedRandom(0));

    private static string Json(Tournament t) => JsonSerializer.Serialize(t, Storage.Json);

    private static Tournament Clone(Tournament t) => JsonSerializer.Deserialize<Tournament>(Json(t), Storage.Json)!;

    private static void SetResult(Tournament t, Pairing p, GameResult result, bool byReferee = false) =>
        TournamentEditor.UpdatePairing(t, p.Id, new PairingInput(p.BlackId, p.WhiteId, p.IsBye, p.BoardNumber, false, 0, 0, false, result, byReferee, "", false), true);

    // 1. Field availability
    [Fact]
    public void Starting_numbers_exist_only_for_tournaments_with_a_round_robin_stage()
    {
        var swiss = TournamentEditor.Create(Settings("S", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        var a = TournamentEditor.AddParticipant(swiss, Player("A"));
        Assert.Null(PairingSchedule.DescribeRoundRobin(swiss));
        Assert.Equal("startingNumbers.noRoundRobin", Assert.Throws<DomainException>(() => Number(swiss, (a.Id, 1))).Code);

        var rr = RoundRobin(3);
        Number(rr, (1, 2));
        Assert.Equal(2, PairingSchedule.DescribeRoundRobin(rr)!.Numbers[1]);
    }

    // 2 and 22. Drafts, validation at generation, batch atomicity
    [Fact]
    public void Partial_and_duplicate_drafts_save_but_generation_reports_every_problem_without_writes()
    {
        var t = RoundRobin(4);
        Number(t, (1, 1), (2, 1), (3, 7)); // P4 empty, 1 duplicated, 7 out of range, 2-4 missing
        var before = Json(t);

        var ex = Assert.Throws<DomainException>(() => Generate(t));

        Assert.Equal("generate.startingNumbersInvalid", ex.Code);
        Assert.Equal(4, ex.Args["max"]);
        var root = JsonNode.Parse(JsonSerializer.Serialize(ex.Args, Storage.Json))!;
        Assert.Equal(4, root["missing"]![0]!["id"]!.GetValue<int>());
        Assert.Equal(3, root["outOfRange"]![0]!["id"]!.GetValue<int>());
        Assert.Equal(7, root["outOfRange"]![0]!["value"]!.GetValue<int>());
        Assert.Equal(1, root["duplicates"]![0]!["number"]!.GetValue<int>());
        Assert.Equal([1, 2], root["duplicates"]![0]!["ids"]!.AsArray().Select(x => x!.GetValue<int>()));
        Assert.Equal([2, 3, 4], root["gaps"]!.AsArray().Select(x => x!.GetValue<int>()));
        Assert.Equal(before, Json(t));

        Number(t, (1, 4), (2, 3), (3, 2), (4, 1));
        Generate(t);
        Assert.Equal([4, 3, 2, 1], t.Stages[0].FrozenRosterIds);
    }

    [Fact]
    public void Batch_save_is_atomic_and_uses_the_same_rules_as_the_player_card()
    {
        var t = RoundRobin(3);
        var prelim = TournamentEditor.AddParticipant(t, Player("Prelim", preliminary: true));
        var before = Json(t);

        Assert.Equal("startingNumbers.invalidValue", Assert.Throws<DomainException>(() => Number(t, (1, 1), (2, 1000))).Code);
        Assert.Equal("startingNumbers.ineligible", Assert.Throws<DomainException>(() => Number(t, (1, 1), (prelim.Id, 2))).Code);
        Assert.Equal(before, Json(t)); // no row applied

        // Player card: null = unchanged, 0 = clear, n = set; the same rules apply.
        TournamentEditor.UpdateParticipant(t, 1, Player("P1", rating: 1010) with { StartingNumber = 3 });
        Assert.Equal(3, t.FindParticipant(1)!.StartingNumber);
        TournamentEditor.UpdateParticipant(t, 1, Player("P1", rating: 1010));
        Assert.Equal(3, t.FindParticipant(1)!.StartingNumber);
        TournamentEditor.UpdateParticipant(t, 1, Player("P1", rating: 1010) with { StartingNumber = 0 });
        Assert.Null(t.FindParticipant(1)!.StartingNumber);
        Assert.Equal("startingNumbers.ineligible", Assert.Throws<DomainException>(() =>
            TournamentEditor.UpdateParticipant(t, prelim.Id, Player("Prelim", preliminary: true) with { StartingNumber = 5 })).Code);
    }

    // Review of 4ecfd464, P2-01: a typed 0 is invalid in the batch API; null clears.
    [Fact]
    public void Batch_zero_is_rejected_without_changes_and_null_clears()
    {
        var t = RoundRobin(3);
        Number(t, (1, 3));
        var before = Json(t);

        var ex = Assert.Throws<DomainException>(() => Number(t, (2, 1), (1, 0)));

        Assert.Equal("startingNumbers.invalidValue", ex.Code);
        Assert.Equal(0, ex.Args["value"]);
        Assert.Equal(before, Json(t)); // neither row applied
        Number(t, (1, null));
        Assert.Null(t.FindParticipant(1)!.StartingNumber);
    }

    // 3. Manual order defines the schedule
    [Fact]
    public void Manual_permutation_defines_the_frozen_order_regardless_of_registration_and_rating()
    {
        var t = RoundRobin(4);
        Number(t, (1, 3), (2, 1), (3, 4), (4, 2));
        Generate(t, 2); // out of order
        Assert.Equal([2, 4, 1, 3], t.Stages[0].FrozenRosterIds);
        var reloaded = Clone(t);
        Generate(reloaded, 1);
        Assert.Equal([2, 4, 1, 3], reloaded.Stages[0].FrozenRosterIds);
        // Round 1 of the Berger table: 1-4, 2-3 by starting number.
        Assert.Contains(reloaded.Rounds.Single(r => r.Number == 1).Pairings, p => p.BlackId == 2 && p.WhiteId == 3);
        Assert.Equal(1, PairingSchedule.DescribeRoundRobin(reloaded)!.Numbers[2]);
    }

    // 4 and 21. Random overwrite and inactive drafts
    [Fact]
    public void Random_draw_overwrites_all_eligible_drafts_and_keeps_inactive_drafts()
    {
        var t = RoundRobin(4);
        var late = TournamentEditor.AddParticipant(t, Player("Inactive", preliminary: true));
        Number(t, (1, 1), (2, 1)); // partial and duplicated: no barrier for random
        late.StartingNumber = 2; // retained draft from when the player was eligible

        Generate(t, source: StartingNumberSource.Random, random: new FixedRandom(1, 2, 0));

        var roster = t.Stages[0].FrozenRosterIds!;
        Assert.True(t.Stages[0].RandomStartingNumbers);
        Assert.Equal([1, 2, 3, 4], roster.Order());
        for (var i = 0; i < roster.Count; i++) Assert.Equal(i + 1, t.FindParticipant(roster[i])!.StartingNumber);
        Assert.Equal(2, late.StartingNumber); // untouched and not part of N
        Generate(t, 2, source: StartingNumberSource.Random, random: new FixedRandom(3));
        Assert.Equal(roster, t.Stages[0].FrozenRosterIds); // no redraw once frozen
    }

    [Fact]
    public void Inactive_drafts_do_not_cause_errors_and_rejoin_validation_when_eligible()
    {
        var t = RoundRobin(3);
        Number(t, (1, 1), (2, 2), (3, 3));
        var p = TournamentEditor.AddParticipant(t, Player("Later", preliminary: true));
        p.StartingNumber = 2; // duplicate of P2, but inactive
        TournamentEditor.UpdateSettings(t, Settings("RR", TournamentSystem.RoundRobin, 3));
        var copy = Clone(t);
        Generate(copy); // the inactive draft is ignored
        Assert.Equal([1, 2, 3], copy.Stages[0].FrozenRosterIds);

        TournamentEditor.UpdateParticipant(t, p.Id, Player("Later")); // now eligible; draft kept, not renumbered
        Assert.Equal(2, p.StartingNumber);
        var ex = Assert.Throws<DomainException>(() => Generate(t, roundCount: 3));
        Assert.Equal("generate.startingNumbersInvalid", ex.Code);
    }

    // 5. 0001 data
    [Fact]
    public void Drawn_0001_tournaments_show_frozen_numbers_and_undrawn_ones_are_not_auto_numbered()
    {
        var drawn = RoundRobin(4);
        drawn.Stages[0].FrozenRosterIds = [3, 1, 4, 2]; // as frozen by 0001, no drafts
        var state = PairingSchedule.DescribeRoundRobin(drawn)!;
        Assert.True(state.Frozen);
        Assert.Equal(1, state.Numbers[3]);
        Assert.Equal(4, state.Numbers[2]);

        var undrawn = RoundRobin(4);
        var ex = Assert.Throws<DomainException>(() => Generate(undrawn));
        Assert.Equal("generate.startingNumbersInvalid", ex.Code);
        Assert.Null(undrawn.Stages[0].FrozenRosterIds);
    }

    // 6. Round-count adjustment
    [Fact]
    public void Eight_players_and_five_rounds_prompt_for_seven_and_confirm_resizes_and_draws_atomically()
    {
        var t = RoundRobin(8, rounds: 5);
        Number(t, Enumerable.Range(1, 8).Select(i => (i, (int?)i)).ToArray());
        var before = Json(t);

        var prompt = Assert.Throws<DomainException>(() => Generate(t, all: true));
        Assert.Equal("generate.roundCountAdjustment", prompt.Code);
        Assert.True(prompt.IsConflict);
        Assert.Equal(8, prompt.Args["players"]);
        Assert.Equal(7, prompt.Args["required"]);
        Assert.Equal(5, prompt.Args["current"]);
        Assert.Equal(before, Json(t)); // "cancel" = no further request
        Assert.Equal("generate.roundCountAdjustment", Assert.Throws<DomainException>(() => Generate(t, all: true, roundCount: 6)).Code);

        var result = Generate(t, all: true, roundCount: 7);
        Assert.Equal(7, t.NumberOfRounds);
        Assert.Equal(7, result.NumberOfRounds);
        Assert.Equal(7, t.Rounds.Count(r => r.Pairings.Count == 4));
    }

    // 7. Safe shrink
    [Fact]
    public void Shrinking_refuses_when_trimmed_rounds_hold_anything_and_works_when_safe()
    {
        var t = RoundRobin(8, rounds: 10);
        Number(t, Enumerable.Range(1, 8).Select(i => (i, (int?)i)).ToArray());
        t.Participants[0].NotPlayingRounds = [9];
        var before = Json(t);
        var blocked = Assert.Throws<DomainException>(() => Generate(t, all: true, roundCount: 7));
        Assert.Equal("generate.roundCountShrinkBlocked", blocked.Code);
        Assert.Equal("9", blocked.Args["rounds"]);
        Assert.Equal(before, Json(t));

        t.Participants[0].NotPlayingRounds = [];
        TournamentEditor.AddPairing(t, 10, Pair(1, 2));
        Assert.Equal("generate.roundCountShrinkBlocked", Assert.Throws<DomainException>(() => Generate(t, all: true, roundCount: 7)).Code);
        TournamentEditor.DeletePairing(t, t.Rounds.Single(r => r.Number == 10).Pairings[0].Id, false);

        var later = TournamentEditor.AddParticipant(t, Player("Later", preliminary: true) with { FirstEligibleRound = 8 });
        Assert.Equal("generate.roundCountShrinkBlocked", Assert.Throws<DomainException>(() => Generate(t, all: true, roundCount: 7)).Code);
        TournamentEditor.DeleteParticipant(t, later.Id);

        Generate(t, all: true, roundCount: 7);
        Assert.Equal(7, t.NumberOfRounds);
        Assert.DoesNotContain(t.Rounds, r => r.Number > 7);
    }

    // 8. Odd roster and RR prefix
    [Fact]
    public void Odd_rosters_need_N_rounds_and_a_round_robin_prefix_never_resizes_the_tournament()
    {
        var odd = RoundRobin(5, rounds: 4);
        Number(odd, Enumerable.Range(1, 5).Select(i => (i, (int?)i)).ToArray());
        Assert.Equal(5, Assert.Throws<DomainException>(() => Generate(odd)).Args["required"]);

        var prefix = RoundRobin(4, rounds: 7);
        Number(prefix, (1, 1), (2, 2), (3, 3), (4, 4));
        PairingSchedule.ChangeSystem(prefix, 4, TournamentSystem.Swiss, false);
        Generate(prefix, all: true);
        Assert.Equal(7, prefix.NumberOfRounds);
        Assert.Equal([1, 4], prefix.Stages.Select(s => s.StartRound));
    }

    // 9. Atomicity across confirmations
    [Fact]
    public void Refused_or_failed_requests_never_write_part_of_a_random_draw_or_resize()
    {
        var t = RoundRobin(4, rounds: 5);
        Number(t, (1, 4), (2, 3), (3, 2), (4, 1));
        t.Participants[2].NotPlayingRounds = [2]; // fails late, during planning
        var before = Json(t);

        Assert.Equal("generate.roundCountAdjustment",
            Assert.Throws<DomainException>(() => Generate(t, all: true, source: StartingNumberSource.Random)).Code);
        Assert.Equal("generate.playerUnavailable",
            Assert.Throws<DomainException>(() => Generate(t, all: true, source: StartingNumberSource.Random, roundCount: 3)).Code);
        Assert.Equal(before, Json(t)); // numbers, round count, roster and pairings unchanged

        t.Participants[2].NotPlayingRounds = [];
        Generate(t, all: true, source: StartingNumberSource.Random, roundCount: 3, random: new FixedRandom(2, 0, 1));
        Assert.Equal(3, t.NumberOfRounds);
        Assert.True(t.Stages[0].RandomStartingNumbers);
    }

    // 10 and 11. Clear
    [Fact]
    public void Clear_removes_every_pairing_of_the_stage_including_manual_protected_and_byes_but_not_later_stages()
    {
        var t = RoundRobin(5, rounds: 7);
        Number(t, Enumerable.Range(1, 5).Select(i => (i, (int?)i)).ToArray());
        PairingSchedule.ChangeSystem(t, 4, TournamentSystem.Swiss, false);
        Generate(t, all: true);
        t.Rounds[0].Pairings[0].Protected = true; // e.g. imported metadata
        var swissGame = TournamentEditor.AddPairing(t, 4, Pair(1, 2, GameResult.BlackWins));
        t.CurrentRound = 4;
        Assert.Equal(3m, StandingsCalculator.Calculate(t).Rows.Sum(r => r.Rounds.Count(c => c.Kind == CellKind.Bye)));
        var before = Json(t);

        var confirm = Assert.Throws<DomainException>(() => PairingSchedule.ClearRoundRobin(t, 1, false));
        Assert.Equal("clear.confirm", confirm.Code);
        Assert.Equal(9, confirm.Args["count"]); // 3 rounds × (2 games + 1 bye)
        Assert.Equal(3, confirm.Args["byes"]);
        Assert.Equal(before, Json(t)); // cancel is a no-op

        var result = PairingSchedule.ClearRoundRobin(t, 2, true);

        Assert.Equal(9, result.Deleted);
        Assert.All(t.Rounds.Where(r => r.Number <= 3), r => Assert.Empty(r.Pairings));
        Assert.Contains(t.Rounds.Single(r => r.Number == 4).Pairings, p => p.Id == swissGame.Id);
        Assert.Null(t.Stages[0].FrozenRosterIds);
        Assert.Equal(0, StandingsCalculator.Calculate(t).Rows.Sum(r => r.Rounds.Count(c => c.Kind == CellKind.Bye)));
        Assert.False(result.SettingsUnlocked); // two stages
        Assert.True(TournamentEditor.IsSystemLocked(t));
    }

    // 12. Clear keeps numbers as editable drafts
    [Fact]
    public void Clear_keeps_the_last_numbers_as_editable_drafts_and_a_new_random_draw_overwrites_them()
    {
        var t = RoundRobin(4);
        Generate(t, source: StartingNumberSource.Random, random: new FixedRandom(1, 1, 0));
        var first = t.Stages[0].FrozenRosterIds!.ToList();
        foreach (var p in t.Participants) p.StartingNumber = null; // as if drawn by 0001 without drafts

        PairingSchedule.ClearRoundRobin(t, 1, true);

        for (var i = 0; i < first.Count; i++) Assert.Equal(i + 1, t.FindParticipant(first[i])!.StartingNumber);
        Number(t, (first[0], 4), (first[3], 1)); // editable again
        Generate(t, source: StartingNumberSource.Entered);
        Assert.Equal(first[3], t.Stages[0].FrozenRosterIds![0]);

        PairingSchedule.ClearRoundRobin(t, 1, true);
        Generate(t, source: StartingNumberSource.Random, random: new FixedRandom(0, 0, 0));
        Assert.True(t.Stages[0].RandomStartingNumbers);
    }

    // 13, 14, 15, 17. Result lock lifecycle
    [Fact]
    public void Entered_two_player_results_lock_generate_and_clear_until_all_are_removed()
    {
        var t = RoundRobin(5);
        Number(t, Enumerable.Range(1, 5).Select(i => (i, (int?)i)).ToArray());
        Generate(t, 1); // only round 1 drawn (games + bye)
        Assert.Empty(PairingSchedule.DescribeRoundRobin(t)!.LockedBy); // the bye alone never locks

        var game = t.Rounds[0].Pairings.First(p => !p.IsBye);
        SetResult(t, game, GameResult.WhiteWins, byReferee: true);
        var before = Json(t);

        var gen = Assert.Throws<DomainException>(() => Generate(t, 2, confirm: true)); // even undrawn later rounds
        Assert.Equal("generate.lockedByResults", gen.Code);
        Assert.Equal($"1/{game.BoardNumber}", gen.Args["games"]);
        Assert.Equal("clear.lockedByResults", Assert.Throws<DomainException>(() => PairingSchedule.ClearRoundRobin(t, 1, true)).Code);
        Assert.Equal(before, Json(t));

        game.Result = GameResult.Unknown; // imported unknown token also counts
        game.LegacyResultToken = "X";
        Assert.Throws<DomainException>(() => PairingSchedule.ClearRoundRobin(t, 1, true));

        SetResult(t, game, GameResult.Unset); // the referee removes every entered result
        Assert.Empty(PairingSchedule.DescribeRoundRobin(t)!.LockedBy);
        Generate(t, 2);
        PairingSchedule.ClearRoundRobin(t, 1, true);
    }

    // 16 and 23. Settings unlock exception
    [Fact]
    public void Single_stage_clear_unlocks_settings_but_individual_deletion_and_multi_stage_clear_do_not()
    {
        var t = RoundRobin(4);
        Number(t, (1, 1), (2, 2), (3, 3), (4, 4));
        Generate(t, all: true);

        foreach (var (_, p) in t.AllPairings().ToList()) TournamentEditor.DeletePairing(t, p.Id, true);
        Assert.True(TournamentEditor.IsSystemLocked(t)); // deleting one by one keeps the 0001 lock
        Assert.Throws<DomainException>(() => PairingSchedule.ClearRoundRobin(t, 1, false)); // a cancelled clear changes nothing
        Assert.True(t.PairingsEverDrawn);

        var result = PairingSchedule.ClearRoundRobin(t, 1, true);
        Assert.True(result.SettingsUnlocked);
        var reloaded = Clone(t);
        Assert.False(TournamentEditor.IsSystemLocked(reloaded));
        TournamentEditor.UpdateSettings(reloaded, Settings("RR", TournamentSystem.Swiss, 3));
        var again = Clone(reloaded);
        Assert.Equal(TournamentSystem.Swiss, again.System);
        Assert.Equal(TournamentSystem.Swiss, again.Stages.Single().System);
        Assert.Null(PairingSchedule.DescribeRoundRobin(again));
    }

    // Export report
    [Fact]
    public void Starting_numbers_are_reported_as_not_carried_by_the_legacy_xml()
    {
        var t = RoundRobin(4, rounds: 7);
        Number(t, (1, 1), (2, 2), (3, 3), (4, 4));
        PairingSchedule.ChangeSystem(t, 4, TournamentSystem.Swiss, false);
        Generate(t, all: true);
        var report = ExportAnalyzer.Analyze(t);
        Assert.Contains(report.Limitations, l => l.Field == "startingNumber" && l.Count == 4);
    }
}
