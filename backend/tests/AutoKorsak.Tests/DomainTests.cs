using AutoKorsak.Domain;
using static AutoKorsak.Tests.LegacyXmlTests;

namespace AutoKorsak.Tests;

public class StandingsTests
{
    [Fact]
    public void Reference_tournament_standard_scores_and_tie_breaks()
    {
        var t = Fixtures.Import();
        var s = StandingsCalculator.Calculate(t);

        Assert.Equal([1, 2, 3, 4, 5, 6], s.CountedRounds);
        AssertRow(t, s, "Кобзар Гліб", place: 1, wins: 6, sos: 21, sodos: 21, sosos: 111);
        AssertRow(t, s, "Єрємєєв Матвій", place: 2, wins: 5, sos: 17, sodos: 11, sosos: 113);
        Assert.Empty(s.Notices);
    }

    [Fact]
    public void Reference_order_matches_legacy_table()
    {
        var t = Fixtures.Import();
        var names = StandingsCalculator.Calculate(t).Rows.Select(r => t.FindParticipant(r.ParticipantId)!.DisplayName);
        // From legacy-autokorsak/table-А_11.12.21.txt
        Assert.Equal(
        [
            "Кобзар Гліб", "Єрємєєв Матвій", "Смирнов Аркадій", "Стеценко Олексій", "Калашнік Іван",
            "Колот Олександра", "Медведський Іван", "Кисельова Марія", "Бровкін Олександр", "Шевченко Валерія",
        ], names);
    }

    [Fact]
    public void Round_cells_reference_opponent_positions()
    {
        var t = Fixtures.Import();
        var s = StandingsCalculator.Calculate(t);
        var kobzar = s.Rows.Single(r => r.ParticipantId == t.ByName("Кобзар Гліб").Id);
        // Legacy table: 6+ 5+ 4+ 2+ 3+ 7+
        Assert.Equal([6, 5, 4, 2, 3, 7], kobzar.Rounds.Select(c => c.OpponentPosition!.Value));
        Assert.All(kobzar.Rounds, c => Assert.Equal(CellOutcome.Win, c.Outcome));
    }

    [Fact]
    public void Excluding_current_round_recomputes_standings()
    {
        var t = Fixtures.Import();
        t.IncludeCurrentRound = false;
        var s = StandingsCalculator.Calculate(t);
        Assert.Equal([1, 2, 3, 4, 5], s.CountedRounds);
        Assert.Equal(5m, s.Rows.Single(r => r.ParticipantId == t.ByName("Кобзар Гліб").Id).Values[CriterionKeys.Wins]);
    }

    [Fact]
    public void Changing_a_result_updates_standings()
    {
        var t = Fixtures.Import();
        var game = t.Rounds.Single(r => r.Number == 6).Pairings.Single(p => p.BoardNumber == 1);
        game.Result = GameResult.BlackWins; // Медведський beats Кобзар
        var s = StandingsCalculator.Calculate(t);
        Assert.Equal(5m, s.Rows.Single(r => r.ParticipantId == t.ByName("Кобзар Гліб").Id).Values[CriterionKeys.Wins]);
        Assert.Equal(3m, s.Rows.Single(r => r.ParticipantId == t.ByName("Медведський Іван").Id).Values[CriterionKeys.Wins]);
    }

    [Fact]
    public void Unsupported_situations_are_reported()
    {
        var t = Fixtures.Import(Fixtures.ByeRelativePath);
        var s = StandingsCalculator.Calculate(t);
        Assert.Contains(StandingsCalculator.NoticeByes, s.Notices);
    }

    private static void AssertRow(Tournament t, StandingsResult s, string name, int place, decimal wins, decimal sos, decimal sodos, decimal sosos)
    {
        var row = s.Rows.Single(r => r.ParticipantId == t.ByName(name).Id);
        Assert.Equal(place, row.Place);
        Assert.Equal(wins, row.Values[CriterionKeys.Wins]);
        Assert.Equal(sos, row.Values[CriterionKeys.Sos]);
        Assert.Equal(sodos, row.Values[CriterionKeys.Sodos]);
        Assert.Equal(sosos, row.Values[CriterionKeys.Sosos]);
    }
}

public class EditorRuleTests
{
    private static (Tournament T, Participant A, Participant B, Participant C) Setup()
    {
        var t = TournamentEditor.Create(Settings("Test", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        return (t, TournamentEditor.AddParticipant(t, Player("A")), TournamentEditor.AddParticipant(t, Player("B")), TournamentEditor.AddParticipant(t, Player("C")));
    }

    [Fact]
    public void Player_cannot_be_paired_twice_in_a_round()
    {
        var (t, a, b, c) = Setup();
        TournamentEditor.AddPairing(t, 1, Pair(a.Id, b.Id));
        var ex = Assert.Throws<DomainException>(() => TournamentEditor.AddPairing(t, 1, Pair(c.Id, a.Id)));
        Assert.Equal("pairing.playerAlreadyPaired", ex.Code);
        TournamentEditor.AddPairing(t, 2, Pair(c.Id, a.Id)); // a different round is fine
    }

    [Fact]
    public void Board_numbers_are_unique_per_round()
    {
        var (t, a, b, c) = Setup();
        var d = TournamentEditor.AddParticipant(t, Player("D"));
        TournamentEditor.AddPairing(t, 1, Pair(a.Id, b.Id, board: 1));
        var ex = Assert.Throws<DomainException>(() => TournamentEditor.AddPairing(t, 1, Pair(c.Id, d.Id, board: 1)));
        Assert.Equal("pairing.boardTaken", ex.Code);
    }

    [Fact]
    public void Preliminary_player_cannot_be_paired_until_finalized()
    {
        var (t, a, _, _) = Setup();
        var p = TournamentEditor.AddParticipant(t, Player("Prelim", preliminary: true));
        var ex = Assert.Throws<DomainException>(() => TournamentEditor.AddPairing(t, 1, Pair(a.Id, p.Id)));
        Assert.Equal("pairing.playerPreliminary", ex.Code);

        Assert.Equal(1, TournamentEditor.FinalizeAllPreliminary(t));
        TournamentEditor.AddPairing(t, 1, Pair(a.Id, p.Id));
    }

    [Fact]
    public void Unavailable_player_cannot_be_paired_in_that_round()
    {
        var (t, a, b, _) = Setup();
        TournamentEditor.UpdateParticipant(t, b.Id, Player("B") with { NotPlayingRounds = [1] });
        var ex = Assert.Throws<DomainException>(() => TournamentEditor.AddPairing(t, 1, Pair(a.Id, b.Id)));
        Assert.Equal("pairing.playerNotAvailable", ex.Code);
        TournamentEditor.AddPairing(t, 2, Pair(a.Id, b.Id));
    }

    [Fact]
    public void Completed_games_are_not_discarded_silently()
    {
        var (t, a, b, c) = Setup();
        var p = TournamentEditor.AddPairing(t, 1, Pair(a.Id, b.Id, GameResult.BlackWins));

        Assert.Equal("pairing.resultWouldBeDiscarded",
            Assert.Throws<DomainException>(() => TournamentEditor.DeletePairing(t, p.Id, confirmDiscardResult: false)).Code);
        Assert.Equal("pairing.resultWouldBeDiscarded",
            Assert.Throws<DomainException>(() => TournamentEditor.UpdatePairing(t, p.Id, Pair(a.Id, c.Id, GameResult.BlackWins), false)).Code);
        Assert.Equal("participant.hasGames",
            Assert.Throws<DomainException>(() => TournamentEditor.DeleteParticipant(t, a.Id)).Code);

        // A color swap that keeps the winner is the same game.
        TournamentEditor.UpdatePairing(t, p.Id, Pair(b.Id, a.Id, GameResult.WhiteWins), false);
        TournamentEditor.DeletePairing(t, p.Id, confirmDiscardResult: true);
        TournamentEditor.DeleteParticipant(t, a.Id);
    }

    [Fact]
    public void Deleting_preliminary_players_keeps_those_with_games()
    {
        var (t, a, b, _) = Setup();
        var withGame = TournamentEditor.AddParticipant(t, Player("P1"));
        TournamentEditor.AddPairing(t, 1, Pair(a.Id, withGame.Id));
        TournamentEditor.UpdateParticipant(t, withGame.Id, Player("P1", preliminary: true));
        TournamentEditor.AddParticipant(t, Player("P2", preliminary: true));

        var result = TournamentEditor.DeleteAllPreliminary(t);

        Assert.Equal(1, result.Deleted);
        Assert.Equal([withGame.Id], result.SkippedWithGames);
        Assert.NotNull(t.FindParticipant(withGame.Id));
    }

    [Fact]
    public void Rounds_cannot_shrink_below_paired_rounds()
    {
        var (t, a, b, _) = Setup();
        TournamentEditor.AddPairing(t, 4, Pair(a.Id, b.Id));
        var ex = Assert.Throws<DomainException>(() => TournamentEditor.UpdateSettings(t, Settings("Test", TournamentSystem.Swiss, rounds: 3)));
        Assert.Equal("settings.roundsHavePairings", ex.Code);
    }
}

public class CatalogTests
{
    [Fact]
    public void Upsert_creates_then_updates_linked_entries()
    {
        var t = Fixtures.Import();
        var plan = CatalogUpsert.Plan(t, []);
        Assert.Equal(10, plan.ToCreate);

        var (outcome, changed) = CatalogUpsert.Apply(t, [], [], DateTimeOffset.UnixEpoch);
        Assert.Equal(10, outcome.Created);
        Assert.All(t.Participants, p => Assert.NotNull(p.CatalogEntryId));

        t.ByName("Кобзар Гліб").Rating = 1500;
        var (second, changed2) = CatalogUpsert.Apply(t, changed, [], DateTimeOffset.UnixEpoch);
        Assert.Equal(10, second.Updated);
        Assert.Equal(1500, changed2.Single(c => c.DisplayName == "Кобзар Гліб").Rating);
    }

    [Fact]
    public void Namesakes_require_an_explicit_decision()
    {
        var catalog = new List<PlayerCatalogEntry> { new() { Id = Guid.NewGuid(), LocalSurname = "Кобзар Гліб", Rating = 900 } };
        var t = Fixtures.Import();
        var plan = CatalogUpsert.Plan(t, catalog);
        Assert.Equal(1, plan.NeedsDecision);

        var ex = Assert.Throws<DomainException>(() => CatalogUpsert.Apply(t, catalog, [], DateTimeOffset.UnixEpoch));
        Assert.Equal("catalog.decisionRequired", ex.Code);

        var kobzar = t.ByName("Кобзар Гліб");
        var (outcome, _) = CatalogUpsert.Apply(t, catalog,
            [new UpsertDecision(kobzar.Id, UpsertDecisionKind.Link, catalog[0].Id)], DateTimeOffset.UnixEpoch);
        Assert.Equal(1, outcome.Updated);
        Assert.Equal(9, outcome.Created);
        Assert.Equal(1240, catalog[0].Rating); // all editable fields overwritten, including rating
    }

    [Fact]
    public void Tournament_edits_do_not_touch_catalog_entries()
    {
        var entry = new PlayerCatalogEntry { Id = Guid.NewGuid(), LocalSurname = "Каталог Гравець", Rating = 1000 };
        var t = TournamentEditor.Create(Settings("T", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        var p = TournamentEditor.AddParticipant(t, CatalogUpsert.ToParticipantInput(entry));
        TournamentEditor.UpdateParticipant(t, p.Id, CatalogUpsert.ToParticipantInput(entry) with { Rating = 1800 });
        Assert.Equal(1000, entry.Rating);
        Assert.Equal(entry.Id, p.CatalogEntryId);
    }
}
