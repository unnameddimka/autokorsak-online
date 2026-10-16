namespace AutoKorsak.Domain;

/// <summary>Ranking criteria known to the application.</summary>
public static class CriterionKeys
{
    /// <summary>Total points (McMahon only).</summary>
    public const string Points = "points";
    public const string Wins = "wins";
    public const string Sos = "sos";
    public const string Sodos = "sodos";
    public const string Sosos = "sosos";
    // Shown and stored, not calculated in P0.
    public const string PointsExcludingAbsence = "pointsExcludingAbsence";
    public const string ExtraPoints = "extraPoints";
    public const string UpDown = "upDown";
    public const string DirectEncounter = "directEncounter";

    /// <summary>Prefix for criteria imported with a legacy token this application does not know.</summary>
    public const string LegacyPrefix = "legacy:";

    public static readonly IReadOnlyList<string> All =
        [Points, Wins, Sos, Sodos, Sosos, PointsExcludingAbsence, ExtraPoints, UpDown, DirectEncounter];

    public static readonly IReadOnlySet<string> Calculated = new HashSet<string> { Points, Wins, Sos, Sodos, Sosos };

    public static bool IsKnown(string key) => All.Contains(key) || key.StartsWith(LegacyPrefix, StringComparison.Ordinal);

    /// <summary>Total points and wins are distinct only in McMahon; other systems offer wins only.</summary>
    public static bool AppliesTo(string key, TournamentSystem? system) =>
        key != Points || system == TournamentSystem.McMahon;

    public static List<CriterionSetting> Defaults(TournamentSystem? system)
    {
        var enabled = new HashSet<string> { Sos, Sodos, Sosos, system == TournamentSystem.McMahon ? Points : Wins };
        return All.Select(k => new CriterionSetting { Key = k, Enabled = enabled.Contains(k) }).ToList();
    }

    /// <summary>Ensures every known criterion appears exactly once, keeping the given order first.</summary>
    public static List<CriterionSetting> Complete(IEnumerable<CriterionSetting> given)
    {
        var result = new List<CriterionSetting>();
        foreach (var c in given)
        {
            if (!IsKnown(c.Key) || result.Any(r => r.Key == c.Key)) continue;
            result.Add(new CriterionSetting { Key = c.Key, Enabled = c.Enabled });
        }
        foreach (var key in All.Where(k => result.All(r => r.Key != k)))
            result.Add(new CriterionSetting { Key = key, Enabled = false });
        return result;
    }
}

/// <summary>Standings columns known to the application.</summary>
public static class ColumnKeys
{
    public const string Place = "place";
    public const string Name = "name";
    public const string Country = "country";
    public const string Club = "club";
    public const string Team = "team";
    public const string Coach = "coach";
    public const string Grade = "grade";
    public const string Rank = "rank";
    public const string Rating = "rating";
    public const string Group = "group";
    public const string Rounds = "rounds";
    public const string Criteria = "criteria";
    /// <summary>Post-tournament rating; deferred beyond P0 but kept in the layout.</summary>
    public const string NewRating = "newRating";

    public static readonly IReadOnlyList<string> All =
        [Place, Name, Country, Club, Team, Coach, Grade, Rank, Rating, Group, Rounds, Criteria, NewRating];

    public static List<LayoutColumn> Defaults() =>
        All.Select(k => new LayoutColumn { Key = k, Visible = k != NewRating }).ToList();

    public static List<LayoutColumn> Complete(IEnumerable<LayoutColumn> given)
    {
        var result = new List<LayoutColumn>();
        foreach (var c in given)
        {
            if (!All.Contains(c.Key) || result.Any(r => r.Key == c.Key)) continue;
            result.Add(new LayoutColumn { Key = c.Key, Visible = c.Visible || c.Key == Name });
        }
        foreach (var key in All.Where(k => result.All(r => r.Key != k)))
            result.Add(new LayoutColumn { Key = key, Visible = false });
        return result;
    }
}
