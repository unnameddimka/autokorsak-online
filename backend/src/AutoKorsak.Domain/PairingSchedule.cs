namespace AutoKorsak.Domain;

public sealed record StageChangeResult(int StartRound, TournamentSystem System, int DeletedPairings);

public sealed record GenerationResult(IReadOnlyList<int> Rounds, int Created, int Replaced, int Kept, bool RosterFrozen);

/// <summary>Source of randomness for starting-number draws; replaceable in tests.</summary>
public interface IRandomSource
{
    /// <summary>Returns a value in [0, maxExclusive).</summary>
    int Next(int maxExclusive);
}

public sealed class SystemRandomSource : IRandomSource
{
    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);
}

/// <summary>
/// Pairing stages and the round-robin generator (spec 0001). Every operation validates completely before it
/// mutates anything; the application service saves only after success, so failures leave no trace.
/// A game with a recorded result is never modified by these operations.
/// </summary>
public static class PairingSchedule
{
    // ---------- system change from round K (spec 0001 §2.5) ----------

    public static StageChangeResult ChangeSystem(Tournament t, int startRound, TournamentSystem? system, bool confirmDeleteUnplayed)
    {
        t.EnsureStages();
        if (system is not { } target) throw new DomainException("stage.systemRequired");
        if (startRound < 2 || startRound > t.NumberOfRounds)
            throw new DomainException("stage.invalidStartRound", Args(("max", t.NumberOfRounds)));
        var last = t.Stages[^1];
        if (startRound <= last.StartRound)
            throw new DomainException("stage.notAfterLastBoundary", Args(("round", last.StartRound)));
        if (target == TournamentSystem.RoundRobin) throw new DomainException("stage.roundRobinAfterOtherSystem");
        if (t.StageFor(startRound).System == target) throw new DomainException("stage.sameSystem");

        var affected = t.Rounds.Where(r => r.Number >= startRound).ToList();
        var played = affected.Where(r => r.Pairings.Any(p => p.HasAwardedOutcome)).Select(r => r.Number).ToList();
        if (played.Count > 0)
            throw DomainException.Conflict("stage.roundsHaveResults", Args(("rounds", RoundList(played))));
        var unplayed = affected.Where(r => r.Pairings.Count > 0).ToList();
        var count = unplayed.Sum(r => r.Pairings.Count);
        if (count > 0 && !confirmDeleteUnplayed)
            throw DomainException.Conflict("stage.unplayedPairingsWillBeDeleted",
                Args(("count", count), ("rounds", RoundList(unplayed.Select(r => r.Number)))));

        foreach (var r in unplayed) r.Pairings.Clear();
        t.Stages.Add(new PairingStage { StartRound = startRound, System = target });
        t.System = target; // the last selected system (spec 0001 §2.1, §6)
        return new StageChangeResult(startRound, target, count);
    }

    // ---------- round robin (spec 0001 §3) ----------

    /// <summary>
    /// Berger fixture for one round of a single cycle: pairs of starting numbers (first = Black) and the starting
    /// number receiving a bye (odd rosters). Follows the standard FIDE Berger tables; the table's first-listed
    /// player (White in chess, the first mover) plays Black in Go.
    /// </summary>
    public static (List<(int Black, int White)> Games, int? Bye) BergerRound(int players, int index)
    {
        var n = players % 2 == 0 ? players : players + 1; // odd rosters get an internal empty slot n
        var m = n - 1;
        if (index < 0 || index >= m) throw new ArgumentOutOfRangeException(nameof(index));
        var r = index + 1;
        var games = new List<(int, int)>();
        int? bye = null;

        // Player n meets the player p with 2p ≡ r + 1 (mod m); n takes the first move in even rounds.
        var opponent = r % 2 == 1 ? (r + 1) / 2 : n / 2 + r / 2;
        if (n != players) bye = opponent;
        else games.Add(r % 2 == 0 ? (n, opponent) : (opponent, n));

        // Remaining players i, j meet when i + j ≡ r + 1 (mod m); the first mover is the one for whom
        // (other − self) mod m is odd. Boards are listed in FIDE table order.
        var target = r % m + 1;
        var paired = new HashSet<int> { opponent };
        var order = Enumerable.Range(1, m).Select(k => ((opponent - 1 + k) % m) + 1).ToList(); // walk from the n-board outward
        foreach (var i in order)
        {
            if (paired.Contains(i)) continue;
            var j = Mod(target - i - 1, m) + 1;
            if (j == i || paired.Contains(j)) continue;
            paired.Add(i);
            paired.Add(j);
            games.Add(Mod(j - i, m) % 2 == 1 ? (i, j) : (j, i));
        }
        return (games, bye);
    }

    public static GenerationResult GenerateRoundRobin(
        Tournament t, int round, bool allRounds, bool randomStartingNumbers, bool confirmReplace, IRandomSource random)
    {
        t.EnsureStages();
        if (round < 1 || round > t.NumberOfRounds)
            throw new DomainException("round.outOfRange", Args(("max", t.NumberOfRounds)));
        var stage = t.StageFor(round);
        if (!stage.IsRoundRobin) throw new DomainException("generate.notAvailable");
        var start = stage.StartRound;
        var end = t.StageEnd(stage);

        // 1. Roster: the frozen one, or the eligible finalized participants at the stage start.
        var roster = stage.FrozenRosterIds ?? DrawRoster(t, stage, end, randomStartingNumbers, random);
        if (roster.Count < 2) throw new DomainException("generate.tooFewPlayers");
        var players = roster.Count;
        var cycle = players % 2 == 0 ? players - 1 : players;
        var length = end - start + 1;
        if (!t.HasLaterStage(stage) && length != cycle)
            throw new DomainException("generate.roundCountMismatch",
                Args(("players", players), ("required", cycle), ("actual", length), ("start", start)));
        if (length > cycle)
            throw new DomainException("generate.stageTooLong", Args(("players", players), ("required", cycle), ("actual", length)));

        var targets = allRounds ? Enumerable.Range(start, length).ToList() : new List<int> { round };

        // 2. Plan every requested round before mutating anything.
        var plans = new List<RoundPlan>();
        foreach (var number in targets)
        {
            var (games, byeNumber) = BergerRound(players, number - start);
            var fixture = games.Select(g => new Slot(roster[g.Black - 1], roster[g.White - 1])).ToList();
            if (byeNumber is { } b) fixture.Add(new Slot(roster[b - 1], null));

            foreach (var id in roster)
            {
                var p = t.FindParticipant(id) ?? throw new DomainException("generate.rosterInvalid");
                if (p.Preliminary || !p.PlaysInRound(number))
                    throw new DomainException("generate.playerUnavailable", Args(("name", p.DisplayName), ("round", number)));
            }
            plans.Add(PlanRound(t, number, fixture));
        }

        var replaced = plans.Sum(p => p.Remove.Count);
        if (replaced > 0 && !confirmReplace)
            throw DomainException.Conflict("generate.replaceUnplayed",
                Args(("count", replaced), ("rounds", RoundList(plans.Where(p => p.Remove.Count > 0).Select(p => p.Number)))));

        // 3. Apply atomically (nothing above has written).
        var froze = stage.FrozenRosterIds is null;
        if (froze)
        {
            stage.FrozenRosterIds = roster;
            stage.RandomStartingNumbers = randomStartingNumbers;
        }
        var created = 0;
        t.PairingsEverDrawn = true;
        foreach (var plan in plans)
        {
            var r = t.GetRound(plan.Number);
            foreach (var p in plan.Remove) r.Pairings.Remove(p);
            var used = r.Pairings.Select(p => p.BoardNumber).ToHashSet();
            var board = 1;
            foreach (var slot in plan.Create)
            {
                while (used.Contains(board)) board++;
                used.Add(board);
                r.Pairings.Add(new Pairing
                {
                    Id = t.NextPairingId++,
                    BlackId = slot.Black,
                    WhiteId = slot.White,
                    IsBye = slot.White is null,
                    BoardNumber = board,
                    AllowDraw = t.AllowDraws,
                    Result = GameResult.Unset,
                });
                created++;
            }
            r.Pairings.Sort((a, b) => a.BoardNumber.CompareTo(b.BoardNumber));
        }
        return new GenerationResult(targets, created, replaced, plans.Sum(p => p.Kept), froze);
    }

    private sealed record Slot(int Black, int? White);

    private sealed record RoundPlan(int Number, List<Pairing> Remove, List<Slot> Create, int Kept);

    private static List<int> DrawRoster(Tournament t, PairingStage stage, int end, bool random, IRandomSource source)
    {
        var pending = t.Participants
            .Where(p => !p.Preliminary && p.FirstEligibleRound > stage.StartRound && p.FirstEligibleRound <= end)
            .Select(p => p.DisplayName).ToList();
        if (pending.Count > 0)
            throw new DomainException("generate.participantJoinsMidRoundRobin",
                Args(("names", string.Join(", ", pending)), ("start", stage.StartRound)));

        var roster = t.Participants
            .Where(p => !p.Preliminary && p.FirstEligibleRound <= stage.StartRound)
            .Select(p => p.Id).Order().ToList();
        if (random)
            for (var i = roster.Count - 1; i > 0; i--) // Fisher–Yates: one permutation, persisted as is
            {
                var j = source.Next(i + 1);
                (roster[i], roster[j]) = (roster[j], roster[i]);
            }
        return roster;
    }

    /// <summary>
    /// Reconciles a round with its fixture. Pairings with an awarded outcome are immutable: a played game must match a
    /// slot with the same two players in either colour, and a credited bye must be the fixture's bye; otherwise the
    /// request fails. Unplayed games are kept only in the exact Berger orientation; others are replaced (after
    /// confirmation).
    /// </summary>
    private static RoundPlan PlanRound(Tournament t, int number, List<Slot> fixture)
    {
        var existing = t.Rounds.FirstOrDefault(r => r.Number == number)?.Pairings ?? [];
        var open = new List<Slot>(fixture);
        var remove = new List<Pairing>();
        var kept = 0;

        foreach (var p in existing.OrderByDescending(p => p.HasAwardedOutcome))
        {
            var match = open.FirstOrDefault(s => Matches(p, s, anyColour: p.HasRecordedResult));
            if (match is not null)
            {
                open.Remove(match);
                kept++;
            }
            else if (p.HasAwardedOutcome)
            {
                throw DomainException.Conflict("generate.playedPairingConflicts", Args(("round", number), ("board", p.BoardNumber)));
            }
            else
            {
                remove.Add(p);
            }
        }
        return new RoundPlan(number, remove, open, kept);
    }

    private static bool Matches(Pairing p, Slot s, bool anyColour) =>
        s.White is null
            ? p.IsBye && p.BlackId == s.Black
            : !p.IsBye && p.BlackId is { } b && p.WhiteId is { } w
              && ((b == s.Black && w == s.White) || (anyColour && b == s.White && w == s.Black));

    private static int Mod(int a, int m) => ((a % m) + m) % m;

    private static string RoundList(IEnumerable<int> rounds) => string.Join(", ", rounds.Distinct().Order());

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}
