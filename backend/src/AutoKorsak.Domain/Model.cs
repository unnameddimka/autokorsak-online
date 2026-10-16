using System.Text.Json.Serialization;

namespace AutoKorsak.Domain;

public enum TournamentSystem { RoundRobin, Scheveningen, Swiss, McMahon }

public enum StoneColor { Black, White }

public enum GameResult
{
    Unset,
    BlackWins,
    WhiteWins,
    /// <summary>Both players lose (legacy token "0-0").</summary>
    BothLose,
    /// <summary>Jigo. No verified legacy token exists yet.</summary>
    Draw,
    /// <summary>A legacy token this application does not understand; kept in <see cref="Pairing.LegacyResultToken"/>.</summary>
    Unknown,
}

public enum SkippedRoundScoring { AddHalf, Alternate, AddNothing }

/// <summary>
/// Aggregate root. Pairings and results are authoritative; standings are derived.
/// Persisted as one JSON document per tournament.
/// </summary>
public sealed class Tournament
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// The most recently selected pairing system (spec 0001 §2.1). Null when imported with a legacy <c>Kind</c> value
    /// whose meaning is not verified. Which system governs each round is recorded in <see cref="Stages"/>.
    /// </summary>
    public TournamentSystem? System { get; set; }

    /// <summary>Raw legacy <c>Kind</c> value from an imported file; preserved on export when the system is unchanged.</summary>
    public int? LegacyKind { get; set; }

    public int BoardSize { get; set; } = 19;
    public StoneColor FirstMoveColor { get; set; } = StoneColor.Black;
    public int NumberOfRounds { get; set; } = 5;
    public bool HandicapUsed { get; set; }
    public bool AllowDraws { get; set; }
    public bool RankFromRating { get; set; }
    public bool CustomizeCalculation { get; set; }

    public List<CriterionSetting> Criteria { get; set; } = [];
    public StandingsLayout Layout { get; set; } = new();
    public PairingConstraints PairingConstraints { get; set; } = new();
    public CalculationSettings Calculation { get; set; } = new();

    public int CurrentRound { get; set; } = 1;
    public bool IncludeCurrentRound { get; set; } = true;

    public List<Participant> Participants { get; set; } = [];
    public List<Round> Rounds { get; set; } = [];

    /// <summary>Ordered pairing stages; empty only in data saved before spec 0001 (see <see cref="EnsureStages"/>).</summary>
    public List<PairingStage> Stages { get; set; } = [];

    public int NextPairingId { get; set; } = 1;

    /// <summary>
    /// Monotonic: set once any pairing has ever been created (spec 0001 §2.1). Deleting pairings later does not make
    /// the tournament "never drawn" again.
    /// </summary>
    public bool PairingsEverDrawn { get; set; }

    /// <summary>The complete original legacy XML document of an imported tournament (null for new tournaments).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacySourceXml { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Participant? FindParticipant(int id) => Participants.FirstOrDefault(p => p.Id == id);

    public Round GetRound(int number)
    {
        var round = Rounds.FirstOrDefault(r => r.Number == number);
        if (round is null)
        {
            round = new Round { Number = number };
            Rounds.Add(round);
            Rounds.Sort((a, b) => a.Number.CompareTo(b.Number));
        }
        return round;
    }

    public IEnumerable<(Round Round, Pairing Pairing)> AllPairings() =>
        Rounds.SelectMany(r => r.Pairings.Select(p => (r, p)));

    /// <summary>
    /// Compatibility defaults for data saved before spec 0001: no stages means one stage from round 1 with the
    /// tournament system, and existing pairings prove the tournament was already drawn (monotonic Settings lock).
    /// </summary>
    public void EnsureStages()
    {
        if (Stages.Count == 0) Stages.Add(new PairingStage { StartRound = 1, System = System });
        if (!PairingsEverDrawn && Rounds.Any(r => r.Pairings.Count > 0)) PairingsEverDrawn = true;
        // Sorting bumps the list version, so only sort when needed (callers may be enumerating the stages).
        for (var i = 1; i < Stages.Count; i++)
            if (Stages[i - 1].StartRound > Stages[i].StartRound)
            {
                Stages.Sort((a, b) => a.StartRound.CompareTo(b.StartRound));
                break;
            }
    }

    public PairingStage StageFor(int round)
    {
        EnsureStages();
        return Stages.Last(s => s.StartRound <= Math.Max(round, 1));
    }

    /// <summary>Last round governed by <paramref name="stage"/>: the round before the next stage, or the final round.</summary>
    public int StageEnd(PairingStage stage)
    {
        EnsureStages();
        var next = Stages.FirstOrDefault(s => s.StartRound > stage.StartRound);
        return next is null ? NumberOfRounds : next.StartRound - 1;
    }

    public bool HasLaterStage(PairingStage stage) => Stages.Any(s => s.StartRound > stage.StartRound);
}

public sealed class CriterionSetting
{
    public string Key { get; set; } = "";
    public bool Enabled { get; set; }
}

public sealed class LayoutColumn
{
    public string Key { get; set; } = "";
    public bool Visible { get; set; }
}

public sealed class StandingsLayout
{
    public List<LayoutColumn> Columns { get; set; } = [];
    public bool ShowColors { get; set; }
    public bool HideEmptyColumns { get; set; } = true;
}

public sealed class PairingConstraints
{
    public bool AvoidSameCountry { get; set; }
    public bool AvoidSameClub { get; set; }
    public bool AvoidSameNationality { get; set; }
    public bool AvoidSameTeam { get; set; }
    public bool AvoidSameCoach { get; set; }
}

public sealed class CalculationSettings
{
    public SkippedRoundScoring SkippedRoundScoring { get; set; } = SkippedRoundScoring.AddHalf;
    public bool AddNothingToOwnCoefficients { get; set; }
    public bool AddMaximumToOpponentCoefficients { get; set; }
    public bool TopGroupOnly { get; set; }
}

/// <summary>Tournament-local player snapshot. Independent of the shared catalog after creation.</summary>
public sealed class Participant
{
    /// <summary>Identifier local to this tournament (legacy <c>IndividualParticipant/Id</c>).</summary>
    public int Id { get; set; }
    public Guid? CatalogEntryId { get; set; }

    public string LocalFirstName { get; set; } = "";
    public string LocalSurname { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string Surname { get; set; } = "";

    public int Rating { get; set; }
    /// <summary>Go rank in legacy notation, e.g. "9k", "2d". Empty when unknown.</summary>
    public string Rank { get; set; } = "";
    public int RankShift { get; set; }
    public string Grade { get; set; } = "";
    public string Country { get; set; } = "";
    public string Club { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string Team { get; set; } = "";
    public string Coach { get; set; } = "";
    public long EgdPin { get; set; }
    /// <summary>McMahon start score (legacy <c>StartScores</c>). Displayed in the "group" column.</summary>
    public int StartScore { get; set; }

    public bool Preliminary { get; set; }
    /// <summary>First round in which the participant takes part (spec 0001 §2.2). Earlier rounds are not skipped rounds.</summary>
    public int FirstEligibleRound { get; set; } = 1;
    public List<int> NotPlayingRounds { get; set; } = [];
    public string Comment { get; set; } = "";

    /// <summary>Original <c>IndividualParticipant</c> element, kept so unmapped legacy data survives export.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyXml { get; set; }

    public string DisplayName => JoinName(LocalSurname, LocalFirstName) is { Length: > 0 } local
        ? local
        : JoinName(Surname, FirstName);

    public bool PlaysInRound(int round) => round >= FirstEligibleRound && !NotPlayingRounds.Contains(round);

    public bool HasJoinedBy(int round) => round >= FirstEligibleRound;

    private static string JoinName(string surname, string firstName) =>
        string.Join(' ', new[] { surname, firstName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed class Round
{
    public int Number { get; set; }
    public List<Pairing> Pairings { get; set; } = [];

    /// <summary>Original <c>TournamentRound</c> element (pairings are re-generated on export).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyXml { get; set; }
}

public sealed class Pairing
{
    public int Id { get; set; }
    public int? BlackId { get; set; }
    public int? WhiteId { get; set; }
    /// <summary>Bye: only <see cref="BlackId"/> is used (legacy stores the player in <c>Black</c>).</summary>
    public bool IsBye { get; set; }

    public int BoardNumber { get; set; }
    public bool BoardNumberFixed { get; set; }
    public int Handicap { get; set; }
    public decimal AdditionalKomi { get; set; }
    public bool AllowDraw { get; set; }

    public GameResult Result { get; set; }
    public string? LegacyResultToken { get; set; }
    public bool ResultByReferee { get; set; }
    public string Comment { get; set; } = "";

    /// <summary>Manually protected: a future automatic pairing must keep players and colors unchanged.</summary>
    public bool Protected { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyXml { get; set; }

    /// <summary>A two-player (or legacy) result has been recorded.</summary>
    public bool HasRecordedResult => Result != GameResult.Unset;

    /// <summary>
    /// The pairing already credits an outcome: a recorded result, or a bye with a real player (one win and one point,
    /// spec 0001 §4). Such pairings are immutable to automatic pairing and stage changes (spec 0001 §1).
    /// </summary>
    public bool HasAwardedOutcome => HasRecordedResult || (IsBye && BlackId is not null);

    public IEnumerable<int> PlayerIds()
    {
        if (BlackId is { } b) yield return b;
        if (!IsBye && WhiteId is { } w) yield return w;
    }

    public bool Involves(int participantId) => PlayerIds().Contains(participantId);
}

/// <summary>
/// A pairing method effective from <see cref="StartRound"/> until the next stage (spec 0001 §2). Stages control how
/// pairings are created; they never partition scoring.
/// </summary>
public sealed class PairingStage
{
    public int StartRound { get; set; } = 1;

    /// <summary>Null for an imported stage whose legacy system is not identified.</summary>
    public TournamentSystem? System { get; set; }

    /// <summary>Round robin only: participant IDs in Berger starting-number order (index + 1). Null until the first draw.</summary>
    public List<int>? FrozenRosterIds { get; set; }

    /// <summary>Round robin only: whether the frozen starting numbers were assigned by a random permutation.</summary>
    public bool RandomStartingNumbers { get; set; }

    [JsonIgnore]
    public bool IsRoundRobin => System == TournamentSystem.RoundRobin;

    [JsonIgnore]
    public bool IsFrozen => FrozenRosterIds is not null;
}
