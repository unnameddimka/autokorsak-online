using System.Text.RegularExpressions;

namespace AutoKorsak.Domain;

public sealed record SettingsInput(
    string Name,
    string? Description,
    TournamentSystem? System,
    int BoardSize,
    StoneColor FirstMoveColor,
    int NumberOfRounds,
    bool HandicapUsed,
    bool AllowDraws,
    bool RankFromRating,
    bool CustomizeCalculation,
    List<CriterionSetting>? Criteria,
    StandingsLayout? Layout,
    PairingConstraints? PairingConstraints,
    CalculationSettings? Calculation);

public sealed record ParticipantInput(
    string? LocalFirstName,
    string? LocalSurname,
    string? FirstName,
    string? Surname,
    int Rating,
    string? Rank,
    int RankShift,
    string? Grade,
    string? Country,
    string? Club,
    string? Nationality,
    string? Team,
    string? Coach,
    bool Preliminary,
    List<int>? NotPlayingRounds,
    string? Comment,
    Guid? CatalogEntryId = null,
    int? FirstEligibleRound = null);

public sealed record PairingInput(
    int? BlackId,
    int? WhiteId,
    bool IsBye,
    int? BoardNumber,
    bool BoardNumberFixed,
    int Handicap,
    decimal AdditionalKomi,
    bool AllowDraw,
    GameResult Result,
    bool ResultByReferee,
    string? Comment,
    bool Protected);

public sealed record BulkDeleteResult(int Deleted, IReadOnlyList<int> SkippedWithGames);

public sealed record SkippedParticipant(int Id, string Name, string Code, IReadOnlyDictionary<string, object?> Args);

public sealed record FinalizeResult(IReadOnlyList<int> Finalized, IReadOnlyList<SkippedParticipant> Skipped);

/// <summary>All state-changing tournament operations and their validation rules.</summary>
public static partial class TournamentEditor
{
    public const int MaxRounds = 50;
    public const int MaxTextLength = 2000;
    public const int MaxNameLength = 200;

    [GeneratedRegex(@"^(?:(?:[1-9]|[12][0-9]|30)k|[1-9]d|[1-9]p)$")]
    private static partial Regex RankPattern();

    public static bool IsValidRank(string rank) => rank.Length == 0 || RankPattern().IsMatch(rank);

    // ---------- tournament ----------

    public static Tournament Create(SettingsInput input, DateTimeOffset now)
    {
        if (input.System is null) throw new DomainException("settings.systemRequired");
        var t = new Tournament
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            Criteria = CriterionKeys.Defaults(input.System),
            Layout = new StandingsLayout { Columns = ColumnKeys.Defaults() },
        };
        ApplySettings(t, input);
        return t;
    }

    public static void UpdateSettings(Tournament t, SettingsInput input) => ApplySettings(t, input);

    private static void ApplySettings(Tournament t, SettingsInput input)
    {
        t.EnsureStages();
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) throw new DomainException("settings.nameRequired");
        if (name.Length > MaxNameLength) throw new DomainException("validation.tooLong", Args(("field", "name"), ("max", MaxNameLength)));
        if ((input.Description ?? "").Length > MaxTextLength)
            throw new DomainException("validation.tooLong", Args(("field", "description"), ("max", MaxTextLength)));
        if (input.System is null && t.LegacyKind is null) throw new DomainException("settings.systemRequired");
        if (input.BoardSize is < 5 or > 25) throw new DomainException("settings.invalidBoardSize");
        if (input.NumberOfRounds is < 1 or > MaxRounds)
            throw new DomainException("settings.invalidRoundCount", Args(("max", MaxRounds)));

        var lastPairedRound = t.Rounds.Where(r => r.Pairings.Count > 0).Select(r => r.Number).DefaultIfEmpty(0).Max();
        if (input.NumberOfRounds < lastPairedRound)
            throw new DomainException("settings.roundsHavePairings", Args(("round", lastPairedRound)));
        var lastStageStart = t.Stages.Max(s => s.StartRound);
        if (input.NumberOfRounds < lastStageStart)
            throw new DomainException("settings.roundsHaveStages", Args(("round", lastStageStart)));
        var lastJoinRound = t.Participants.Select(p => p.FirstEligibleRound).DefaultIfEmpty(1).Max();
        if (input.NumberOfRounds < lastJoinRound)
            throw new DomainException("settings.participantsJoinLater", Args(("round", lastJoinRound)));

        // Spec 0001 §2.1: Settings may change the system only while that cannot rewrite a historical stage.
        var systemChanged = input.System != t.System;
        if (systemChanged && IsSystemLocked(t)) throw DomainException.Conflict("settings.systemLocked");

        t.Name = name;
        t.Description = input.Description ?? "";
        t.System = input.System;
        if (systemChanged) t.Stages[0].System = input.System;
        t.BoardSize = input.BoardSize;
        t.FirstMoveColor = input.FirstMoveColor;
        t.NumberOfRounds = input.NumberOfRounds;
        t.HandicapUsed = input.HandicapUsed;
        t.AllowDraws = input.AllowDraws;
        t.RankFromRating = input.RankFromRating;
        t.CustomizeCalculation = input.CustomizeCalculation;
        if (input.Criteria is not null) t.Criteria = CriterionKeys.Complete(input.Criteria);
        if (input.Layout is not null)
            t.Layout = new StandingsLayout
            {
                Columns = ColumnKeys.Complete(input.Layout.Columns ?? []),
                ShowColors = input.Layout.ShowColors,
                HideEmptyColumns = input.Layout.HideEmptyColumns,
            };
        if (input.PairingConstraints is not null) t.PairingConstraints = input.PairingConstraints;
        if (input.Calculation is not null) t.Calculation = input.Calculation;

        t.Rounds.RemoveAll(r => r.Number > t.NumberOfRounds);
        foreach (var p in t.Participants) p.NotPlayingRounds.RemoveAll(r => r > t.NumberOfRounds);
        t.CurrentRound = Math.Clamp(t.CurrentRound, 1, t.NumberOfRounds);
    }

    /// <summary>The Settings system selector is read-only once any pairing exists or a second stage is defined.</summary>
    public static bool IsSystemLocked(Tournament t)
    {
        t.EnsureStages();
        return t.Stages.Count > 1 || t.PairingsEverDrawn || t.Stages.Any(s => s.IsFrozen) || t.Rounds.Any(r => r.Pairings.Count > 0);
    }

    public static void SetCurrentRound(Tournament t, int currentRound, bool includeCurrentRound)
    {
        if (currentRound < 1 || currentRound > t.NumberOfRounds)
            throw new DomainException("round.outOfRange", Args(("max", t.NumberOfRounds)));
        t.CurrentRound = currentRound;
        t.IncludeCurrentRound = includeCurrentRound;
    }

    // ---------- participants ----------

    public static Participant AddParticipant(Tournament t, ParticipantInput input)
    {
        var p = new Participant { Id = t.Participants.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1 };
        Apply(t, p, input, isNew: true);
        p.CatalogEntryId = input.CatalogEntryId;
        t.Participants.Add(p);
        return p;
    }

    public static Participant UpdateParticipant(Tournament t, int id, ParticipantInput input)
    {
        var p = t.FindParticipant(id) ?? throw new NotFoundException("participant.notFound");
        Apply(t, p, input, isNew: false);
        return p;
    }

    private static void Apply(Tournament t, Participant p, ParticipantInput input, bool isNew)
    {
        string Clean(string? s, string field, int max = MaxNameLength)
        {
            var v = s ?? "";
            if (v.Length > max) throw new DomainException("validation.tooLong", Args(("field", field), ("max", max)));
            return v;
        }

        var localFirst = Clean(input.LocalFirstName, "localFirstName");
        var localSurname = Clean(input.LocalSurname, "localSurname");
        var first = Clean(input.FirstName, "firstName");
        var surname = Clean(input.Surname, "surname");
        if (string.IsNullOrWhiteSpace(localSurname + localFirst + surname + first))
            throw new DomainException("participant.nameRequired");

        var rank = (input.Rank ?? "").Trim();
        if (!IsValidRank(rank)) throw new DomainException("participant.invalidRank", Args(("rank", rank)));
        if (input.Rating is < -1000 or > 4000) throw new DomainException("participant.invalidRating");
        if (input.RankShift is < -30 or > 30) throw new DomainException("participant.invalidRankShift");

        var notPlaying = (input.NotPlayingRounds ?? []).Distinct().Order().ToList();
        if (notPlaying.Any(r => r < 1 || r > t.NumberOfRounds))
            throw new DomainException("round.outOfRange", Args(("max", t.NumberOfRounds)));

        int joinRound;
        if (input.FirstEligibleRound is { } requested) joinRound = requested;
        else if (!isNew) joinRound = p.FirstEligibleRound;
        else joinRound = DefaultJoinRound(t, input.Preliminary)
            // No adjusted default: validate the unadjusted round so the specific reason (e.g. a frozen round robin) is reported.
            ?? UnadjustedJoinRound(t)
            ?? throw new DomainException("participant.joinRoundRequired", Args(("max", t.NumberOfRounds)));
        ValidateParticipation(t, p, isNew, joinRound, input.Preliminary);

        if (!isNew)
        {
            // Preliminary players must not be paired; refuse a new final → preliminary transition while pairings exist.
            // An already-preliminary imported player with games stays editable (the state is not newly created).
            if (input.Preliminary && !p.Preliminary && GameCount(t, p.Id) is var games and > 0)
                throw DomainException.Conflict("participant.pairedCannotBePreliminary", Args(("count", games)));

            // Marking a player unavailable for a round they are already paired in would leave an invalid pairing.
            foreach (var r in notPlaying.Where(r => !p.NotPlayingRounds.Contains(r)))
            {
                if (t.Rounds.FirstOrDefault(x => x.Number == r)?.Pairings.Any(x => x.Involves(p.Id)) == true)
                    throw DomainException.Conflict("participant.pairedInRound", Args(("round", r)));
                // A frozen round-robin schedule must stay generatable (spec 0001 §2.3); withdrawals use
                // referee-decision results instead (§5.3).
                if (t.StageFor(r).FrozenRosterIds?.Contains(p.Id) == true)
                    throw DomainException.Conflict("participant.rosterMemberUnavailable", Args(("round", r)));
            }
        }

        p.LocalFirstName = localFirst;
        p.LocalSurname = localSurname;
        p.FirstName = first;
        p.Surname = surname;
        p.Rating = input.Rating;
        p.Rank = rank;
        p.RankShift = input.RankShift;
        p.Grade = Clean(input.Grade, "grade");
        p.Country = Clean(input.Country, "country");
        p.Club = Clean(input.Club, "club");
        p.Nationality = Clean(input.Nationality, "nationality");
        p.Team = Clean(input.Team, "team");
        p.Coach = Clean(input.Coach, "coach");
        p.Preliminary = input.Preliminary;
        p.FirstEligibleRound = joinRound;
        p.NotPlayingRounds = notPlaying;
        p.Comment = Clean(input.Comment, "comment", MaxTextLength);
    }

    public static int GameCount(Tournament t, int participantId) =>
        t.AllPairings().Count(x => x.Pairing.Involves(participantId));

    public static void DeleteParticipant(Tournament t, int id)
    {
        var p = t.FindParticipant(id) ?? throw new NotFoundException("participant.notFound");
        var games = GameCount(t, id);
        if (games > 0) throw DomainException.Conflict("participant.hasGames", Args(("count", games)));
        if (t.Stages.Any(s => s.FrozenRosterIds?.Contains(id) == true))
            throw DomainException.Conflict("participant.rosterMember");
        t.Participants.Remove(p);
    }

    /// <summary>
    /// Finalizes every preliminary participant that may take part from their joining round; the others stay
    /// preliminary and are reported with the reason (spec 0001 §2.4).
    /// </summary>
    public static FinalizeResult FinalizeAllPreliminary(Tournament t)
    {
        var finalized = new List<int>();
        var skipped = new List<SkippedParticipant>();
        foreach (var p in t.Participants.Where(p => p.Preliminary).ToList())
        {
            try
            {
                ValidateParticipation(t, p, isNew: false, p.FirstEligibleRound, preliminary: false);
                p.Preliminary = false;
                finalized.Add(p.Id);
            }
            catch (DomainException ex)
            {
                skipped.Add(new SkippedParticipant(p.Id, p.DisplayName, ex.Code, ex.Args));
            }
        }
        return new FinalizeResult(finalized, skipped);
    }

    // ---------- joining round and round-robin roster rules (spec 0001 §2.2–2.4) ----------

    /// <summary>True when any pairing of the round already credits an outcome, including a bye (spec 0001 §2.2).</summary>
    public static bool RoundHasResults(Tournament t, int round) =>
        t.Rounds.FirstOrDefault(r => r.Number == round)?.Pairings.Any(p => p.HasAwardedOutcome) == true;

    /// <summary>
    /// Proposed joining round for a newly admitted participant: the current round if it has no recorded results,
    /// otherwise the next one, adjusted for round-robin stages. Null when no valid round exists.
    /// </summary>
    public static int? DefaultJoinRound(Tournament t, bool preliminary = false)
    {
        t.EnsureStages();
        var current = Math.Clamp(t.CurrentRound, 1, t.NumberOfRounds);
        var round = RoundHasResults(t, current) ? current + 1 : current;
        if (round > t.NumberOfRounds) return null;
        foreach (var stage in t.Stages.Where(s => s.IsRoundRobin))
        {
            var end = t.StageEnd(stage);
            if (!stage.IsFrozen && round > stage.StartRound && round <= end) round = stage.StartRound; // pre-draw registration
            else if (stage.IsFrozen && !preliminary && round >= stage.StartRound && round <= end) round = end + 1;
        }
        return round > t.NumberOfRounds ? null : round;
    }

    private static int? UnadjustedJoinRound(Tournament t)
    {
        var current = Math.Clamp(t.CurrentRound, 1, t.NumberOfRounds);
        var round = RoundHasResults(t, current) ? current + 1 : current;
        return round > t.NumberOfRounds ? null : round;
    }

    /// <summary>Validates a joining round and registration state against existing games and round-robin stages.</summary>
    internal static void ValidateParticipation(Tournament t, Participant p, bool isNew, int joinRound, bool preliminary)
    {
        t.EnsureStages();
        if (joinRound < 1 || joinRound > t.NumberOfRounds)
            throw new DomainException("participant.invalidJoinRound", Args(("max", t.NumberOfRounds)));
        if (!isNew)
        {
            var firstGame = t.AllPairings().Where(x => x.Pairing.Involves(p.Id)).Select(x => x.Round.Number)
                .DefaultIfEmpty(int.MaxValue).Min();
            if (firstGame < joinRound)
                throw DomainException.Conflict("participant.joinAfterGames", Args(("round", firstGame)));
        }
        foreach (var stage in t.Stages.Where(s => s.IsRoundRobin))
        {
            var end = t.StageEnd(stage);
            if (stage.FrozenRosterIds is { } roster)
            {
                if (!isNew && roster.Contains(p.Id))
                {
                    if (preliminary) throw DomainException.Conflict("participant.rosterMemberPreliminary");
                    if (joinRound > stage.StartRound)
                        throw DomainException.Conflict("participant.rosterMemberJoinRound", Args(("round", stage.StartRound)));
                }
                else if (!preliminary && joinRound <= end)
                {
                    throw t.HasLaterStage(stage)
                        ? DomainException.Conflict("participant.joinAfterRoundRobin", Args(("round", end + 1)))
                        : DomainException.Conflict("participant.noStageAfterRoundRobin");
                }
            }
            else if (!preliminary && joinRound > stage.StartRound && joinRound <= end)
            {
                throw DomainException.Conflict("participant.joinMidRoundRobin",
                    Args(("start", stage.StartRound), ("next", end < t.NumberOfRounds ? end + 1 : null)));
            }
        }
    }

    /// <summary>Deletes preliminary players without games; players with games are kept and reported.</summary>
    public static BulkDeleteResult DeleteAllPreliminary(Tournament t)
    {
        var skipped = new List<int>();
        var deleted = 0;
        foreach (var p in t.Participants.Where(p => p.Preliminary).ToList())
        {
            if (GameCount(t, p.Id) > 0) { skipped.Add(p.Id); continue; }
            t.Participants.Remove(p);
            deleted++;
        }
        return new BulkDeleteResult(deleted, skipped);
    }

    // ---------- pairings ----------

    public static (Round Round, Pairing Pairing) FindPairing(Tournament t, int pairingId) =>
        t.AllPairings().FirstOrDefault(x => x.Pairing.Id == pairingId) is { Pairing: not null } found
            ? found
            : throw new NotFoundException("pairing.notFound");

    public static Pairing AddPairing(Tournament t, int roundNumber, PairingInput input)
    {
        if (roundNumber < 1 || roundNumber > t.NumberOfRounds)
            throw new DomainException("round.outOfRange", Args(("max", t.NumberOfRounds)));
        var round = t.GetRound(roundNumber);
        var pairing = new Pairing { Id = t.NextPairingId };
        ApplyPairing(t, round, pairing, input, isNew: true);
        t.NextPairingId++;
        t.PairingsEverDrawn = true;
        round.Pairings.Add(pairing);
        round.Pairings.Sort((a, b) => a.BoardNumber.CompareTo(b.BoardNumber));
        return pairing;
    }

    public static Pairing UpdatePairing(Tournament t, int pairingId, PairingInput input, bool confirmDiscardResult)
    {
        var (round, pairing) = FindPairing(t, pairingId);
        if (pairing.HasAwardedOutcome && !confirmDiscardResult && !SameGame(pairing, input))
            throw DomainException.Conflict("pairing.resultWouldBeDiscarded");
        ApplyPairing(t, round, pairing, input, isNew: false);
        round.Pairings.Sort((a, b) => a.BoardNumber.CompareTo(b.BoardNumber));
        return pairing;
    }

    public static void DeletePairing(Tournament t, int pairingId, bool confirmDiscardResult)
    {
        var (round, pairing) = FindPairing(t, pairingId);
        if (pairing.HasAwardedOutcome && !confirmDiscardResult)
            throw DomainException.Conflict("pairing.resultWouldBeDiscarded");
        round.Pairings.Remove(pairing);
    }

    /// <summary>
    /// True when the edit keeps the same players with the same per-player outcome
    /// (e.g. a color swap that also flips the result), so no recorded game is lost.
    /// </summary>
    private static bool SameGame(Pairing old, PairingInput input)
    {
        if (old.IsBye != input.IsBye) return false;
        var oldSet = new HashSet<int?> { old.BlackId, old.IsBye ? null : old.WhiteId };
        var newSet = new HashSet<int?> { input.BlackId, input.IsBye ? null : input.WhiteId };
        if (!oldSet.SetEquals(newSet)) return false;
        if (old.BlackId == input.BlackId) return true;
        // Colors swapped: only safe when the result follows the players.
        return (old.Result, input.Result) switch
        {
            (GameResult.BlackWins, GameResult.WhiteWins) or (GameResult.WhiteWins, GameResult.BlackWins) => true,
            (GameResult.BothLose, GameResult.BothLose) or (GameResult.Draw, GameResult.Draw) => true,
            _ => false,
        };
    }

    private static void ApplyPairing(Tournament t, Round round, Pairing pairing, PairingInput input, bool isNew)
    {
        var black = input.BlackId;
        var white = input.IsBye ? null : input.WhiteId;
        if (black is null) throw new DomainException("pairing.playerRequired");
        if (!input.IsBye && white is null) throw new DomainException("pairing.playerRequired");
        if (black == white) throw new DomainException("pairing.samePlayer");

        HashSet<int> previous = isNew ? [] : pairing.PlayerIds().ToHashSet();
        var stage = t.StageFor(round.Number);
        foreach (var id in new[] { black, white }.OfType<int>())
        {
            var player = t.FindParticipant(id) ?? throw new DomainException("pairing.unknownPlayer", Args(("id", id)));
            if (round.Pairings.Any(x => x != pairing && x.Involves(id)))
                throw new DomainException("pairing.playerAlreadyPaired", Args(("name", player.DisplayName), ("round", round.Number)));
            if (previous.Contains(id)) continue; // already in this pairing: legacy data is not re-validated
            if (!player.HasJoinedBy(round.Number))
                throw new DomainException("pairing.playerNotJoined", Args(("name", player.DisplayName), ("round", player.FirstEligibleRound)));
            if (stage.FrozenRosterIds is { } roster && !roster.Contains(id))
                throw new DomainException("pairing.notInRoundRobinRoster", Args(("name", player.DisplayName)));
            if (player.Preliminary)
                throw new DomainException("pairing.playerPreliminary", Args(("name", player.DisplayName)));
            if (!player.PlaysInRound(round.Number))
                throw new DomainException("pairing.playerNotAvailable", Args(("name", player.DisplayName), ("round", round.Number)));
        }

        var board = input.BoardNumber ?? (isNew ? NextBoard(round) : pairing.BoardNumber);
        // Legacy byes occupy no board and are stored with board number 0.
        var boardless = input.IsBye && board == 0;
        if (board < 1 && !boardless) throw new DomainException("pairing.invalidBoard");
        if (!boardless && round.Pairings.Any(x => x != pairing && x.BoardNumber == board))
            throw new DomainException("pairing.boardTaken", Args(("board", board)));
        if (input.Handicap is < 0 or > 9) throw new DomainException("pairing.invalidHandicap");
        if (input.AdditionalKomi is < -50 or > 50 || decimal.Round(input.AdditionalKomi * 2) != input.AdditionalKomi * 2)
            throw new DomainException("pairing.invalidKomi");
        if ((input.Comment ?? "").Length > MaxTextLength)
            throw new DomainException("validation.tooLong", Args(("field", "comment"), ("max", MaxTextLength)));

        var result = input.Result;
        var resultByReferee = input.ResultByReferee;
        if (input.IsBye && !pairing.IsBye)
        {
            // Converting a game into a bye: a two-player result cannot belong to a bye. UpdatePairing has already
            // required explicit confirmation if a recorded result is being discarded.
            result = GameResult.Unset;
            resultByReferee = false;
        }
        else if (input.IsBye && result != GameResult.Unset && result != pairing.Result)
        {
            throw new DomainException("pairing.byeResultNotSupported");
        }
        if (result == GameResult.Draw && !input.AllowDraw && pairing.Result != GameResult.Draw)
            throw new DomainException("pairing.drawNotAllowed");
        if (result == GameResult.Unknown && pairing.Result != GameResult.Unknown)
            throw new DomainException("pairing.invalidResult");

        pairing.BlackId = black;
        pairing.WhiteId = white;
        pairing.IsBye = input.IsBye;
        pairing.BoardNumber = board;
        pairing.BoardNumberFixed = input.BoardNumberFixed;
        pairing.Handicap = input.Handicap;
        pairing.AdditionalKomi = input.AdditionalKomi;
        pairing.AllowDraw = input.AllowDraw;
        if (result != GameResult.Unknown) pairing.LegacyResultToken = null;
        pairing.Result = result;
        pairing.ResultByReferee = resultByReferee;
        pairing.Comment = input.Comment ?? "";
        // Spec 0001 §3.5: round-robin rounds have no manual protection; a kept imported flag may only be cleared.
        pairing.Protected = stage.IsRoundRobin ? !isNew && pairing.Protected && input.Protected : input.Protected;
    }

    public static int NextBoard(Round round) => round.Pairings.Select(p => p.BoardNumber).DefaultIfEmpty(0).Max() + 1;

    internal static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}
