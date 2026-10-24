namespace AutoKorsak.Domain;

public sealed record StageChangeResult(int StartRound, TournamentSystem System, int DeletedPairings);

public sealed record GenerationResult(IReadOnlyList<int> Rounds, int Created, int Replaced, int Kept, bool RosterFrozen, int NumberOfRounds);

/// <summary>Where a first round-robin draw takes its starting numbers from (spec 0002 §3).</summary>
public enum StartingNumberSource { Entered, Random }

public sealed record ClearResult(int Deleted, int Byes, bool SettingsUnlocked);

public sealed record LockedGame(int Round, int Board);

public sealed record RoundRobinState(
    int StartRound,
    int EndRound,
    bool Frozen,
    bool RandomStartingNumbers,
    IReadOnlyDictionary<int, int?> Numbers,
    IReadOnlyList<int> Eligible,
    IReadOnlyList<LockedGame> LockedBy);

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
        Tournament t, int round, bool allRounds, StartingNumberSource source, bool confirmReplace, IRandomSource random)
    {
        t.EnsureStages();
        if (round < 1 || round > t.NumberOfRounds)
            throw new DomainException("round.outOfRange", Args(("max", t.NumberOfRounds)));
        var stage = t.StageFor(round);
        if (!stage.IsRoundRobin) throw new DomainException("generate.notAvailable");
        var start = stage.StartRound;

        // Spec 0002 §5.2: any entered two-player result in the stage locks round-robin generation.
        var locked = EnteredResults(t, stage);
        if (locked.Count > 0) throw DomainException.Conflict("generate.lockedByResults", LockArgs(locked));

        // 1. Roster: the frozen one, or the eligible participants ordered by entered or random starting numbers.
        var roster = stage.FrozenRosterIds ?? DrawRoster(t, stage, source, random);
        if (roster.Count < 2) throw new DomainException("generate.tooFewPlayers");
        var players = roster.Count;
        var cycle = players % 2 == 0 ? players - 1 : players;

        // 2. Stage length: a terminal stage must be exactly one cycle. Spec 0003 §2.2: the count follows the roster
        // without a confirmation (this replaces the 0002 prompt); an unsafe resize still fails atomically.
        var roundCount = t.NumberOfRounds;
        if (!t.HasLaterStage(stage))
        {
            var required = start - 1 + cycle;
            if (t.NumberOfRounds != required)
            {
                ValidateRoundCountChange(t, required);
                roundCount = required;
            }
        }
        var end = t.HasLaterStage(stage) ? t.StageEnd(stage) : roundCount;
        var length = end - start + 1;
        if (length > cycle)
            throw new DomainException("generate.stageTooLong", Args(("players", players), ("required", cycle), ("actual", length)));
        // The requested round only selects the stage when all rounds are drawn: a current round beyond a shrunk count
        // (valid under the provisional count) must not block the draw; ApplyRoundCount clamps it (review P2-01 of 0003).
        // A single requested round must exist after the resize.
        if (round > end && !allRounds) throw new DomainException("round.outOfRange", Args(("max", end)));

        var targets = allRounds ? Enumerable.Range(start, length).ToList() : new List<int> { round };

        // 3. Plan every requested round before mutating anything.
        var plans = new List<RoundPlan>();
        foreach (var number in targets)
        {
            var (games, byeNumber) = BergerRound(players, number - start);
            var fixture = games.Select(g => new Slot(roster[g.Black - 1], roster[g.White - 1])).ToList();
            if (byeNumber is { } b) fixture.Add(new Slot(roster[b - 1], null));

            foreach (var id in roster)
            {
                var p = t.FindParticipant(id) ?? throw new DomainException("generate.rosterInvalid");
                if (p.Preliminary || !t.Plays(p, number))
                    throw new DomainException("generate.playerUnavailable", Args(("name", p.DisplayName), ("round", number)));
            }
            plans.Add(PlanRound(t, number, fixture));
        }

        var replaced = plans.Sum(p => p.Remove.Count);
        if (replaced > 0 && !confirmReplace)
            throw DomainException.Conflict("generate.replaceUnplayed",
                Args(("count", replaced), ("rounds", RoundList(plans.Where(p => p.Remove.Count > 0).Select(p => p.Number)))));

        // 4. Apply atomically (nothing above has written): round count, numbers, roster freeze, pairings.
        if (roundCount != t.NumberOfRounds) ApplyRoundCount(t, roundCount);
        var froze = stage.FrozenRosterIds is null;
        if (froze)
        {
            stage.FrozenRosterIds = roster;
            stage.RandomStartingNumbers = source == StartingNumberSource.Random;
            if (source == StartingNumberSource.Random)
                for (var i = 0; i < roster.Count; i++) t.FindParticipant(roster[i])!.StartingNumber = i + 1;
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
        return new GenerationResult(targets, created, replaced, plans.Sum(p => p.Kept), froze, roundCount);
    }

    // ---------- clear round-robin draw (spec 0002 §5) ----------

    public static ClearResult ClearRoundRobin(Tournament t, int round, bool confirm)
    {
        t.EnsureStages();
        if (round < 1 || round > t.NumberOfRounds)
            throw new DomainException("round.outOfRange", Args(("max", t.NumberOfRounds)));
        var stage = t.StageFor(round);
        if (!stage.IsRoundRobin) throw new DomainException("clear.notRoundRobin");
        var locked = EnteredResults(t, stage);
        if (locked.Count > 0) throw DomainException.Conflict("clear.lockedByResults", LockArgs(locked));

        var end = t.StageEnd(stage);
        var rounds = t.Rounds.Where(r => r.Number >= stage.StartRound && r.Number <= end && r.Pairings.Count > 0).ToList();
        var count = rounds.Sum(r => r.Pairings.Count);
        var byes = rounds.Sum(r => r.Pairings.Count(p => p.IsBye));
        if (count == 0 && !stage.IsFrozen) throw new DomainException("clear.nothingToClear");
        if (!confirm)
            throw DomainException.Conflict("clear.confirm",
                Args(("count", count), ("byes", byes), ("rounds", RoundList(rounds.Select(r => r.Number)))));

        // The last effective numbers stay visible as editable drafts (§5.1 step 3).
        if (stage.FrozenRosterIds is { } roster)
            for (var i = 0; i < roster.Count; i++)
                if (t.FindParticipant(roster[i]) is { } p) p.StartingNumber = i + 1;
        foreach (var r in rounds) r.Pairings.Clear();
        stage.FrozenRosterIds = null;
        stage.RandomStartingNumbers = false;

        // §5.1 step 6: the sole non-monotonic exception to the 0001 Settings lock.
        var unlocked = t.Stages.Count == 1 && t.Rounds.All(r => r.Pairings.Count == 0);
        if (unlocked) t.PairingsEverDrawn = false;
        return new ClearResult(count, byes, unlocked);
    }

    /// <summary>The single round-robin stage in 0002 scope (a later RR stage is deferred), or null.</summary>
    public static PairingStage? RoundRobinStage(Tournament t)
    {
        t.EnsureStages();
        return t.Stages.FirstOrDefault(s => s.IsRoundRobin);
    }

    /// <summary>Eligible for the RR numbered roster: finalized and taking part from the stage start (spec 0002 §2.1).</summary>
    public static bool IsRoundRobinEligible(Tournament t, Participant p, PairingStage stage) =>
        !p.Preliminary && t.EffectiveJoinRound(p) <= stage.StartRound;

    /// <summary>Read model for the UI: numbers (frozen or draft), eligibility and the result lock.</summary>
    public static RoundRobinState? DescribeRoundRobin(Tournament t)
    {
        var stage = RoundRobinStage(t);
        if (stage is null) return null;
        var numbers = new Dictionary<int, int?>();
        foreach (var p in t.Participants) numbers[p.Id] = p.StartingNumber;
        if (stage.FrozenRosterIds is { } roster)
        {
            // Frozen numbers for roster members; others keep showing their retained (inactive) draft.
            for (var i = 0; i < roster.Count; i++) numbers[roster[i]] = i + 1;
        }
        var eligible = t.Participants.Where(p => stage.FrozenRosterIds?.Contains(p.Id) ?? IsRoundRobinEligible(t, p, stage))
            .Select(p => p.Id).ToList();
        var locked = EnteredResults(t, stage);
        return new RoundRobinState(stage.StartRound, t.StageEnd(stage), stage.IsFrozen, stage.RandomStartingNumbers,
            numbers, eligible, locked.Select(x => new LockedGame(x.Round, x.Board)).ToList());
    }

    /// <summary>Two-player pairings with an entered result in the stage (byes never lock, spec 0002 §5.2).</summary>
    private static List<(int Round, int Board)> EnteredResults(Tournament t, PairingStage stage)
    {
        var end = t.StageEnd(stage);
        return t.Rounds.Where(r => r.Number >= stage.StartRound && r.Number <= end)
            .SelectMany(r => r.Pairings.Where(p => !p.IsBye && p.HasRecordedResult).Select(p => (r.Number, p.BoardNumber)))
            .OrderBy(x => x.Item1).ThenBy(x => x.Item2).ToList();
    }

    private static Dictionary<string, object?> LockArgs(List<(int Round, int Board)> games) =>
        Args(("count", games.Count), ("games", string.Join(", ", games.Select(g => $"{g.Round}/{g.Board}"))));

    private static void ValidateRoundCountChange(Tournament t, int target)
    {
        if (target < 1 || target > TournamentEditor.MaxRounds)
            throw new DomainException("generate.roundCountOutOfRange", Args(("required", target), ("max", TournamentEditor.MaxRounds)));
        if (target >= t.NumberOfRounds) return;
        var blocked = new SortedSet<int>();
        foreach (var r in t.Rounds.Where(r => r.Number > target && r.Pairings.Count > 0)) blocked.Add(r.Number);
        foreach (var s in t.Stages.Where(s => s.StartRound > target)) blocked.Add(s.StartRound);
        // A pure round robin ignores the hidden joining rounds and absences (spec 0003 §2.3); they are kept as stored.
        if (!t.IsPureRoundRobin())
            foreach (var p in t.Participants)
            {
                if (p.FirstEligibleRound > target) blocked.Add(p.FirstEligibleRound);
                foreach (var r in p.NotPlayingRounds.Where(r => r > target)) blocked.Add(r);
            }
        if (blocked.Count > 0)
            throw DomainException.Conflict("generate.roundCountShrinkBlocked", Args(("required", target), ("rounds", RoundList(blocked))));
    }

    private static void ApplyRoundCount(Tournament t, int target)
    {
        t.NumberOfRounds = target;
        t.Rounds.RemoveAll(r => r.Number > target); // validated empty
        t.CurrentRound = Math.Clamp(t.CurrentRound, 1, target);
    }

    private sealed record Slot(int Black, int? White);

    private sealed record RoundPlan(int Number, List<Pairing> Remove, List<Slot> Create, int Kept);

    private static List<int> DrawRoster(Tournament t, PairingStage stage, StartingNumberSource source, IRandomSource random)
    {
        var end = t.StageEnd(stage);
        var pending = t.Participants
            .Where(p => !p.Preliminary && t.EffectiveJoinRound(p) > stage.StartRound && t.EffectiveJoinRound(p) <= end)
            .Select(p => p.DisplayName).ToList();
        if (pending.Count > 0)
            throw new DomainException("generate.participantJoinsMidRoundRobin",
                Args(("names", string.Join(", ", pending)), ("start", stage.StartRound)));

        var eligible = t.Participants.Where(p => IsRoundRobinEligible(t, p, stage)).OrderBy(p => p.Id).ToList();
        if (source == StartingNumberSource.Random)
        {
            var roster = eligible.Select(p => p.Id).ToList();
            for (var i = roster.Count - 1; i > 0; i--) // Fisher–Yates: one permutation, persisted as is
            {
                var j = random.Next(i + 1);
                (roster[i], roster[j]) = (roster[j], roster[i]);
            }
            return roster;
        }
        ValidateEnteredNumbers(eligible);
        return eligible.OrderBy(p => p.StartingNumber).Select(p => p.Id).ToList();
    }

    /// <summary>Entered numbers must be exactly 1..N among the N eligible players (spec 0002 §2.2).</summary>
    private static void ValidateEnteredNumbers(List<Participant> eligible)
    {
        var n = eligible.Count;
        var missing = eligible.Where(p => p.StartingNumber is null)
            .Select(p => new Dictionary<string, object?> { ["id"] = p.Id, ["name"] = p.DisplayName }).ToList();
        var outOfRange = eligible.Where(p => p.StartingNumber is { } v && (v < 1 || v > n))
            .Select(p => new Dictionary<string, object?> { ["id"] = p.Id, ["name"] = p.DisplayName, ["value"] = p.StartingNumber }).ToList();
        var duplicates = eligible.Where(p => p.StartingNumber is { } v && v >= 1 && v <= n)
            .GroupBy(p => p.StartingNumber!.Value).Where(g => g.Count() > 1)
            .Select(g => new Dictionary<string, object?>
            {
                ["number"] = g.Key,
                ["ids"] = g.Select(p => p.Id).ToList(),
                ["names"] = string.Join(", ", g.Select(p => p.DisplayName)),
            }).ToList();
        var used = eligible.Select(p => p.StartingNumber).OfType<int>().ToHashSet();
        var gaps = Enumerable.Range(1, n).Where(i => !used.Contains(i)).ToList();
        if (missing.Count + outOfRange.Count + duplicates.Count + gaps.Count == 0) return;
        throw new DomainException("generate.startingNumbersInvalid", new Dictionary<string, object?>
        {
            ["max"] = n,
            ["missing"] = missing,
            ["outOfRange"] = outOfRange,
            ["duplicates"] = duplicates,
            ["gaps"] = gaps,
        });
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
