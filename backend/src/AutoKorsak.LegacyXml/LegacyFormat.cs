using System.Globalization;
using System.Xml.Linq;
using AutoKorsak.Domain;

namespace AutoKorsak.LegacyXml;

/// <summary>
/// Verified facts about the AutoKorsak 1.38 XML format (<c>Tournament typeversion="6"</c>).
/// Only mappings observed in real legacy files belong here; see docs/decisions.md.
/// </summary>
internal static class LegacyFormat
{
    public const string SupportedTypeVersion = "6";
    public const int SwissKind = 2;

    public static readonly string[] ParticipantOrder =
    [
        "Id", "Joker", "PreliminaryRegistration", "SuperBarMember", "Comment", "StartScores", "StartNumber",
        "ScoreAdjustment", "ScoreAdjustmentValue", "NotPlayingInRound", "GoPlayer",
    ];

    public static readonly string[] GoPlayerOrder =
        ["AsianName", "FirstName", "Surname", "LocalFirstName", "LocalSurname", "GoLevel", "Club", "Grade", "Rating", "EgdPin"];

    public static readonly string[] PairingOrder =
    [
        "ForcedPairing", "PairingWithBye", "Black", "White", "Handicap", "AdditionalKomi", "AllowJigo", "UpDownBalance",
        "Result", "ResultByReferee", "ResultPoints", "BoardNumber", "BoardNumberFixed", "Comment",
    ];

    public static readonly string[] RoundOrder = ["RoundNumber", "Pairing"];

    public static readonly string[] WalllistOrder =
    [
        "ShowCountry", "ShowClub", "ShowClubEgdName", "ShowClubAbbreviateName", "ShowLevels", "ShortNotationForLevel",
        "ShowBarMembership", "ShowRatings", "MarkPreliminaryRegisteredParticipants", "MarkResultsByReferree", "ShowColors",
        "ShowHandicap", "ShowWarningMissingPairing", "Fontsize", "NameColumnWidth", "ClubColumnWidth",
        "ClubColumnAlignmentCenter", "WeakSortCriteria", "SortCriterionDescriptor", "WallListMemberDescriptor", "HideEmptyColumns",
    ];

    public static readonly IReadOnlyDictionary<string, string> ColumnTokens = new Dictionary<string, string>
    {
        [ColumnKeys.Place] = "Place",
        [ColumnKeys.Name] = "Name",
        [ColumnKeys.Country] = "Country",
        [ColumnKeys.Club] = "Club",
        [ColumnKeys.Team] = "Team",
        [ColumnKeys.Coach] = "Coach",
        [ColumnKeys.Grade] = "Grade",
        [ColumnKeys.Rank] = "Rank",
        [ColumnKeys.Rating] = "Rating",
        [ColumnKeys.Group] = "Group",
        [ColumnKeys.Rounds] = "Tours",
        [ColumnKeys.Criteria] = "Criterias",
        [ColumnKeys.NewRating] = "NewRating",
    };

    public const string ScoreToken = "Score";

    /// <summary>
    /// Legacy "Score" is McMahon points in McMahon and the (win-based) score otherwise. Wins in McMahon and the
    /// other legacy criteria (Перемоги, Очки₂, Дод., Підйом, Зустріч) have no verified token.
    /// </summary>
    public static string? CriterionToken(string key, TournamentSystem? system) => key switch
    {
        CriterionKeys.Wins => system == TournamentSystem.McMahon ? null : ScoreToken,
        CriterionKeys.Points => system == TournamentSystem.McMahon ? ScoreToken : null,
        CriterionKeys.Sos => "SOS",
        CriterionKeys.Sodos => "SODOS",
        CriterionKeys.Sosos => "SOSOS",
        _ when key.StartsWith(CriterionKeys.LegacyPrefix, StringComparison.Ordinal) => key[CriterionKeys.LegacyPrefix.Length..],
        _ => null,
    };

    public static string CriterionKey(string token, TournamentSystem? system) => token switch
    {
        ScoreToken => system == TournamentSystem.McMahon ? CriterionKeys.Points : CriterionKeys.Wins,
        "SOS" => CriterionKeys.Sos,
        "SODOS" => CriterionKeys.Sodos,
        "SOSOS" => CriterionKeys.Sosos,
        _ => CriterionKeys.LegacyPrefix + token,
    };

    public static string? ResultToken(GameResult result) => result switch
    {
        GameResult.Unset => "?-?",
        GameResult.BlackWins => "1-0",
        GameResult.WhiteWins => "0-1",
        GameResult.BothLose => "0-0",
        _ => null,
    };

    public static GameResult ParseResult(string? token) => token switch
    {
        null or "" or "?-?" => GameResult.Unset,
        "1-0" => GameResult.BlackWins,
        "0-1" => GameResult.WhiteWins,
        "0-0" => GameResult.BothLose,
        _ => GameResult.Unknown,
    };

    public static string Format(bool value) => value ? "true" : "false";
    public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static XDocument LoadTemplate()
    {
        using var stream = typeof(LegacyFormat).Assembly.GetManifestResourceStream("new-tournament-template.xml")
            ?? throw new InvalidOperationException("Embedded XML template is missing.");
        return XDocument.Load(stream);
    }

    private static readonly Lazy<string[]> RootOrderLazy = new(() =>
        LoadTemplate().Root!.Elements().Select(e => e.Name.LocalName).Distinct()
            .Concat(["IndividualParticipant", "TournamentRound"]).ToArray());

    public static string[] RootOrder => RootOrderLazy.Value;
}
