namespace AutoKorsak.Domain;

public enum CellKind { None, Game, Bye, NotPlaying, NotJoined }

public enum CellOutcome { Unset, Win, Loss, Draw, BothLose, Unknown }

public sealed record RoundCell(
    int Round,
    CellKind Kind,
    int? OpponentId = null,
    int? OpponentPosition = null,
    CellOutcome? Outcome = null,
    StoneColor? Color = null,
    bool ByReferee = false);

public sealed record CriterionColumn(string Key, bool Calculated);

public sealed record StandingRow(
    int Position,
    int Place,
    int PlaceTo,
    int ParticipantId,
    IReadOnlyDictionary<string, decimal?> Values,
    IReadOnlyList<RoundCell> Rounds);

public sealed record StandingsResult(
    IReadOnlyList<int> DisplayedRounds,
    IReadOnlyList<int> CountedRounds,
    IReadOnlyList<CriterionColumn> Criteria,
    IReadOnlyList<StandingRow> Rows,
    IReadOnlyList<string> Notices);

public sealed record PreparationRow(int Position, int ParticipantId, decimal GroupScore, IReadOnlyList<RoundCell> Rounds);

/// <summary>Pairing-preparation view for round <see cref="Round"/>: score groups from rounds before it, then rating.</summary>
public sealed record PreparationResult(
    int Round,
    TournamentSystem? System,
    IReadOnlyList<int> DisplayedRounds,
    IReadOnlyList<int> ScoredRounds,
    IReadOnlyList<PreparationRow> Rows,
    IReadOnlyList<string> Notices);

/// <summary>
/// Derives standings from pairings and results. Verified rules: win = 1 point, "both win" = a win for each player
/// (spec 0003 §7.2), a bye = one win and one point
/// (spec 0001 §4), SOS = sum of opponents' points, SODOS = sum of points of defeated opponents, SOSOS = sum of
/// opponents' SOS. Unsupported situations are reported as notices, never silently "corrected".
/// </summary>
public static class StandingsCalculator
{
    public const string NoticeSpecialResults = "specialResultsSimplified";
    public const string NoticeUnfinishedGames = "unfinishedGames";
    public const string NoticeSkippedRounds = "skippedRoundsNotAdjusted";
    public const string NoticeMcMahon = "mcMahonStartScoresNotApplied";
    public const string NoticeNotCalculated = "criteriaNotCalculated";

    private sealed record Game(int Opponent, decimal OwnScore, CellOutcome Outcome);

    /// <summary>Scores and cells over a set of rounds; shared by the official standings and the preparation view.</summary>
    private sealed class Tally
    {
        public required Dictionary<int, List<Game>> Games { get; init; }
        public required Dictionary<int, int> Byes { get; init; }
        public required Dictionary<int, Dictionary<int, RoundCell>> Cells { get; init; }
        public required SortedSet<string> Notices { get; init; }

        public decimal Points(int id) => Games[id].Sum(g => g.OwnScore) + Byes[id];
        public decimal Wins(int id) => Games[id].Count(g => g.Outcome == CellOutcome.Win) + Byes[id];
    }

    private static Tally Compute(Tournament t, IReadOnlyList<int> displayed, IReadOnlyCollection<int> counted)
    {
        var ids = t.Participants.Select(p => p.Id).ToList();
        var tally = new Tally
        {
            Games = ids.ToDictionary(id => id, _ => new List<Game>()),
            Byes = ids.ToDictionary(id => id, _ => 0),
            Cells = ids.ToDictionary(id => id, _ => new Dictionary<int, RoundCell>()),
            Notices = new SortedSet<string>(StringComparer.Ordinal),
        };

        foreach (var roundNumber in displayed)
        {
            var round = t.Rounds.FirstOrDefault(r => r.Number == roundNumber);
            var isCounted = counted.Contains(roundNumber);
            foreach (var p in round?.Pairings ?? [])
            {
                if (p.IsBye)
                {
                    // A bye is a victory without a played game: one win and one point, no opponent (spec 0001 §4).
                    if (p.BlackId is { } byePlayer && tally.Cells.TryGetValue(byePlayer, out var c))
                    {
                        c[roundNumber] = new RoundCell(roundNumber, CellKind.Bye, Outcome: CellOutcome.Win);
                        if (isCounted) tally.Byes[byePlayer]++;
                    }
                    continue;
                }
                if (p.BlackId is not { } b || p.WhiteId is not { } w || !tally.Games.ContainsKey(b) || !tally.Games.ContainsKey(w)) continue;

                var (bOut, wOut) = Outcomes(p.Result);
                tally.Cells[b][roundNumber] = new RoundCell(roundNumber, CellKind.Game, w, null, bOut, StoneColor.Black, p.ResultByReferee);
                tally.Cells[w][roundNumber] = new RoundCell(roundNumber, CellKind.Game, b, null, wOut, StoneColor.White, p.ResultByReferee);
                if (!isCounted) continue;

                switch (p.Result)
                {
                    case GameResult.Unset:
                        tally.Notices.Add(NoticeUnfinishedGames);
                        continue;
                    case GameResult.Unknown:
                        tally.Notices.Add(NoticeSpecialResults);
                        continue;
                    case GameResult.BothLose or GameResult.Draw:
                        tally.Notices.Add(NoticeSpecialResults);
                        break;
                }
                tally.Games[b].Add(new Game(w, Score(bOut), bOut));
                tally.Games[w].Add(new Game(b, Score(wOut), wOut));
            }
        }

        // An explicit absence in a counted round is always a skipped round. An available but unpaired player only
        // has one once the round is being played (any game has a result). Rounds before a player's joining round
        // are not skipped rounds at all (spec 0001 §2.2).
        var playedRounds = t.Rounds
            .Where(r => r.Pairings.Any(p => !p.IsBye && p.Result != GameResult.Unset))
            .Select(r => r.Number)
            .ToHashSet();
        foreach (var participant in t.Participants)
            foreach (var r in displayed.Where(r => !tally.Cells[participant.Id].ContainsKey(r)))
            {
                if (!t.HasJoined(participant, r))
                {
                    tally.Cells[participant.Id][r] = new RoundCell(r, CellKind.NotJoined);
                    continue;
                }
                var notPlaying = !t.Plays(participant, r);
                tally.Cells[participant.Id][r] = new RoundCell(r, notPlaying ? CellKind.NotPlaying : CellKind.None);
                if (counted.Contains(r) && !participant.Preliminary && (notPlaying || playedRounds.Contains(r)))
                    tally.Notices.Add(NoticeSkippedRounds);
            }
        return tally;
    }

    public static StandingsResult Calculate(Tournament t)
    {
        var current = Math.Clamp(t.CurrentRound, 1, Math.Max(1, t.NumberOfRounds));
        var displayed = Enumerable.Range(1, current).ToList();
        var counted = t.IncludeCurrentRound ? displayed : displayed.Where(r => r < current).ToList();
        var tally = Compute(t, displayed, counted);
        var notices = tally.Notices;
        var ids = t.Participants.Select(p => p.Id).ToList();

        var points = ids.ToDictionary(id => id, tally.Points);
        var wins = ids.ToDictionary(id => id, tally.Wins);
        var sos = ids.ToDictionary(id => id, id => tally.Games[id].Sum(g => points[g.Opponent]));
        var sodos = ids.ToDictionary(id => id, id => tally.Games[id].Sum(g => g.OwnScore * points[g.Opponent]));
        var sosos = ids.ToDictionary(id => id, id => tally.Games[id].Sum(g => sos[g.Opponent]));
        if (t.System == TournamentSystem.McMahon) notices.Add(NoticeMcMahon);

        var criteria = t.Criteria
            .Where(c => c.Enabled && CriterionKeys.AppliesTo(c.Key, t.System))
            .Select(c => new CriterionColumn(c.Key, CriterionKeys.Calculated.Contains(c.Key)))
            .ToList();
        if (criteria.Any(c => !c.Calculated)) notices.Add(NoticeNotCalculated);

        decimal? Value(string key, int id) => key switch
        {
            CriterionKeys.Points => points[id],
            CriterionKeys.Wins => wins[id],
            CriterionKeys.Sos => sos[id],
            CriterionKeys.Sodos => sodos[id],
            CriterionKeys.Sosos => sosos[id],
            _ => null,
        };

        var sortKeys = criteria.Where(c => c.Calculated).Select(c => c.Key).ToList();
        var values = ids.ToDictionary(
            id => id,
            id => (IReadOnlyDictionary<string, decimal?>)criteria.ToDictionary(c => c.Key, c => Value(c.Key, id)));

        // Places come from the enabled criteria only; rating and name merely give a stable order within a tie.
        var ordered = t.Participants
            .OrderBy(p => p, Comparer<Participant>.Create((a, b) => CompareByCriteria(sortKeys, values, a.Id, b.Id)))
            .ThenByDescending(p => p.Rating)
            .ThenBy(p => p.DisplayName, StringComparer.Ordinal)
            .ThenBy(p => p.Id)
            .ToList();

        var position = Positions(ordered);
        var rows = new List<StandingRow>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var id = ordered[i].Id;
            var first = i;
            while (first > 0 && CompareByCriteria(sortKeys, values, ordered[first - 1].Id, id) == 0) first--;
            var last = i;
            while (last < ordered.Count - 1 && CompareByCriteria(sortKeys, values, ordered[last + 1].Id, id) == 0) last++;
            rows.Add(new StandingRow(i + 1, first + 1, last + 1, id, values[id], CellsFor(tally, id, displayed, position)));
        }

        return new StandingsResult(displayed, counted, criteria, rows, notices.ToList());
    }

    /// <summary>
    /// Pairing-preparation view for the current round R (spec 0001 §7): score group from rounds strictly before R
    /// (byes included; McMahon stages add the stored start score), then rating, name and ID. SOS-type coefficients
    /// are deliberately not used. Independent of the "include current round" switch; not an official ranking.
    /// </summary>
    public static PreparationResult CalculatePreparation(Tournament t)
    {
        var current = Math.Clamp(t.CurrentRound, 1, Math.Max(1, t.NumberOfRounds));
        var displayed = Enumerable.Range(1, current).ToList();
        var scored = displayed.Where(r => r < current).ToList();
        var tally = Compute(t, displayed, scored);
        var system = t.StageFor(current).System;
        var mcMahon = system == TournamentSystem.McMahon;

        var group = t.Participants.ToDictionary(p => p.Id, p => tally.Points(p.Id) + (mcMahon ? p.StartScore : 0));
        var ordered = t.Participants
            .OrderByDescending(p => group[p.Id])
            .ThenByDescending(p => p.Rating)
            .ThenBy(p => p.DisplayName, StringComparer.Ordinal)
            .ThenBy(p => p.Id)
            .ToList();
        var position = Positions(ordered);
        var rows = ordered
            .Select((p, i) => new PreparationRow(i + 1, p.Id, group[p.Id], CellsFor(tally, p.Id, displayed, position)))
            .ToList();
        return new PreparationResult(current, system, displayed, scored, rows, tally.Notices.ToList());
    }

    private static Dictionary<int, int> Positions(List<Participant> ordered) =>
        ordered.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i + 1);

    private static List<RoundCell> CellsFor(Tally tally, int id, List<int> displayed, Dictionary<int, int> position) =>
        displayed
            .Select(r => tally.Cells[id][r] is { OpponentId: { } opp } cell ? cell with { OpponentPosition = position[opp] } : tally.Cells[id][r])
            .ToList();

    private static int CompareByCriteria(List<string> keys, Dictionary<int, IReadOnlyDictionary<string, decimal?>> values, int a, int b)
    {
        foreach (var key in keys)
        {
            var cmp = (values[b][key] ?? 0).CompareTo(values[a][key] ?? 0); // descending
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    private static (CellOutcome Black, CellOutcome White) Outcomes(GameResult result) => result switch
    {
        GameResult.BlackWins => (CellOutcome.Win, CellOutcome.Loss),
        GameResult.WhiteWins => (CellOutcome.Loss, CellOutcome.Win),
        GameResult.BothLose => (CellOutcome.BothLose, CellOutcome.BothLose),
        GameResult.BothWin => (CellOutcome.Win, CellOutcome.Win), // spec 0003 §7.2: one point and one win each
        GameResult.Draw => (CellOutcome.Draw, CellOutcome.Draw),
        GameResult.Unknown => (CellOutcome.Unknown, CellOutcome.Unknown),
        _ => (CellOutcome.Unset, CellOutcome.Unset),
    };

    private static decimal Score(CellOutcome outcome) => outcome switch
    {
        CellOutcome.Win => 1m,
        CellOutcome.Draw => 0.5m,
        _ => 0m,
    };
}
