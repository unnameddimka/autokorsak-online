namespace AutoKorsak.Domain;

public enum CellKind { None, Game, Bye, NotPlaying }

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

/// <summary>
/// Derives standings from pairings and results. Implements only verified rules for regular games:
/// win = 1 point, SOS = sum of opponents' points, SODOS = sum of points of defeated opponents,
/// SOSOS = sum of opponents' SOS. Unsupported situations are reported as notices, never silently "corrected".
/// </summary>
public static class StandingsCalculator
{
    public const string NoticeByes = "byesNotScored";
    public const string NoticeSpecialResults = "specialResultsSimplified";
    public const string NoticeUnfinishedGames = "unfinishedGames";
    public const string NoticeSkippedRounds = "skippedRoundsNotAdjusted";
    public const string NoticeMcMahon = "mcMahonStartScoresNotApplied";
    public const string NoticeNotCalculated = "criteriaNotCalculated";

    private sealed record Game(int Opponent, decimal OwnScore, CellOutcome Outcome);

    public static StandingsResult Calculate(Tournament t)
    {
        var current = Math.Clamp(t.CurrentRound, 1, Math.Max(1, t.NumberOfRounds));
        var displayed = Enumerable.Range(1, current).ToList();
        var counted = t.IncludeCurrentRound ? displayed : displayed.Where(r => r < current).ToList();
        var notices = new SortedSet<string>(StringComparer.Ordinal);

        var ids = t.Participants.Select(p => p.Id).ToList();
        var games = ids.ToDictionary(id => id, _ => new List<Game>());
        var cells = ids.ToDictionary(id => id, _ => new Dictionary<int, RoundCell>());

        foreach (var roundNumber in displayed)
        {
            var round = t.Rounds.FirstOrDefault(r => r.Number == roundNumber);
            var isCounted = counted.Contains(roundNumber);
            foreach (var p in round?.Pairings ?? [])
            {
                if (p.IsBye)
                {
                    if (p.BlackId is { } byePlayer && cells.TryGetValue(byePlayer, out var c))
                        c[roundNumber] = new RoundCell(roundNumber, CellKind.Bye);
                    if (isCounted) notices.Add(NoticeByes);
                    continue;
                }
                if (p.BlackId is not { } b || p.WhiteId is not { } w || !games.ContainsKey(b) || !games.ContainsKey(w)) continue;

                var (bOut, wOut) = Outcomes(p.Result);
                cells[b][roundNumber] = new RoundCell(roundNumber, CellKind.Game, w, null, bOut, StoneColor.Black, p.ResultByReferee);
                cells[w][roundNumber] = new RoundCell(roundNumber, CellKind.Game, b, null, wOut, StoneColor.White, p.ResultByReferee);
                if (!isCounted) continue;

                switch (p.Result)
                {
                    case GameResult.Unset:
                        notices.Add(NoticeUnfinishedGames);
                        continue;
                    case GameResult.Unknown:
                        notices.Add(NoticeSpecialResults);
                        continue;
                    case GameResult.BothLose or GameResult.Draw:
                        notices.Add(NoticeSpecialResults);
                        break;
                }
                games[b].Add(new Game(w, Score(bOut), bOut));
                games[w].Add(new Game(b, Score(wOut), wOut));
            }
        }

        // An explicit absence (NotPlayingRounds) in a counted round is always a skipped round. An available but
        // unpaired player only has one once the round is being played (any game has a result); before that the
        // round is simply awaiting pairings/results.
        var playedRounds = t.Rounds
            .Where(r => r.Pairings.Any(p => !p.IsBye && p.Result != GameResult.Unset))
            .Select(r => r.Number)
            .ToHashSet();
        foreach (var participant in t.Participants)
            foreach (var r in displayed.Where(r => !cells[participant.Id].ContainsKey(r)))
            {
                var notPlaying = !participant.PlaysInRound(r);
                cells[participant.Id][r] = new RoundCell(r, notPlaying ? CellKind.NotPlaying : CellKind.None);
                if (counted.Contains(r) && !participant.Preliminary && (notPlaying || playedRounds.Contains(r)))
                    notices.Add(NoticeSkippedRounds);
            }

        var points = ids.ToDictionary(id => id, id => games[id].Sum(g => g.OwnScore));
        var wins = ids.ToDictionary(id => id, id => (decimal)games[id].Count(g => g.Outcome == CellOutcome.Win));
        var sos = ids.ToDictionary(id => id, id => games[id].Sum(g => points[g.Opponent]));
        var sodos = ids.ToDictionary(id => id, id => games[id].Sum(g => g.OwnScore * points[g.Opponent]));
        var sosos = ids.ToDictionary(id => id, id => games[id].Sum(g => sos[g.Opponent]));
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

        var position = ordered.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i + 1);
        var rows = new List<StandingRow>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var id = ordered[i].Id;
            var first = i;
            while (first > 0 && CompareByCriteria(sortKeys, values, ordered[first - 1].Id, id) == 0) first--;
            var last = i;
            while (last < ordered.Count - 1 && CompareByCriteria(sortKeys, values, ordered[last + 1].Id, id) == 0) last++;

            var roundCells = displayed
                .Select(r => cells[id][r] is { OpponentId: { } opp } cell ? cell with { OpponentPosition = position[opp] } : cells[id][r])
                .ToList();
            rows.Add(new StandingRow(i + 1, first + 1, last + 1, id, values[id], roundCells));
        }

        return new StandingsResult(displayed, counted, criteria, rows, notices.ToList());
    }

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
