using AutoKorsak.Domain;

namespace AutoKorsak.LegacyXml;

public sealed record ExportLimitation(string Scope, string Field, int Count, IReadOnlyList<string> Examples);

public sealed record ExportReport(bool Blocked, string? BlockingCode, IReadOnlyDictionary<string, object?>? BlockingArgs, IReadOnlyList<ExportLimitation> Limitations);

/// <summary>
/// Reports what the legacy XML cannot carry, by exporting, re-importing and comparing with the current state.
/// This keeps the report truthful: anything that would not survive the round-trip is listed.
/// </summary>
public static class ExportAnalyzer
{
    public static ExportReport Analyze(Tournament t)
    {
        Tournament back;
        try
        {
            back = LegacyXmlImporter.FromDocument(LegacyXmlExporter.Export(t), DateTimeOffset.UnixEpoch);
        }
        catch (DomainException ex)
        {
            return new ExportReport(true, ex.Code, ex.Args, []);
        }

        var found = new List<(string Scope, string Field, string Example)>();
        void Check<T>(string scope, string field, string example, T a, T b)
        {
            if (!EqualityComparer<T>.Default.Equals(a, b)) found.Add((scope, field, example));
        }

        Check("tournament", "name", "", t.Name, back.Name);
        Check("tournament", "description", "", t.Description, back.Description);
        Check("tournament", "system", "", t.System, back.System);
        Check("tournament", "boardSize", "", t.BoardSize, back.BoardSize);
        Check("tournament", "firstMoveColor", "", t.FirstMoveColor, back.FirstMoveColor);
        Check("tournament", "numberOfRounds", "", t.NumberOfRounds, back.NumberOfRounds);
        Check("tournament", "handicapUsed", "", t.HandicapUsed, back.HandicapUsed);
        Check("tournament", "allowDraws", "", t.AllowDraws, back.AllowDraws);
        Check("tournament", "rankFromRating", "", t.RankFromRating, back.RankFromRating);
        Check("tournament", "customizeCalculation", "", t.CustomizeCalculation, back.CustomizeCalculation);
        Check("tournament", "currentRound", "", t.CurrentRound, back.CurrentRound);
        Check("tournament", "includeCurrentRound", "", t.IncludeCurrentRound, back.IncludeCurrentRound);
        Check("tournament", "criteria", "", EnabledCriteria(t), EnabledCriteria(back));
        Check("tournament", "layout", "",
            string.Join(',', t.Layout.Columns.Where(c => c.Visible).Select(c => c.Key)) + t.Layout.ShowColors + t.Layout.HideEmptyColumns,
            string.Join(',', back.Layout.Columns.Where(c => c.Visible).Select(c => c.Key)) + back.Layout.ShowColors + back.Layout.HideEmptyColumns);
        Check("tournament", "pairingConstraints", "", Describe(t.PairingConstraints), Describe(back.PairingConstraints));
        Check("tournament", "calculation", "", Describe(t.Calculation), Describe(back.Calculation));
        // Spec 0001 §6: per-round stages, frozen starting numbers and their randomization have no XML representation.
        Check("tournament", "stages", "", Describe(t.Stages), Describe(back.Stages));

        foreach (var p in t.Participants)
        {
            var q = back.FindParticipant(p.Id) ?? new Participant();
            var n = p.DisplayName;
            Check("participant", "localFirstName", n, p.LocalFirstName, q.LocalFirstName);
            Check("participant", "localSurname", n, p.LocalSurname, q.LocalSurname);
            Check("participant", "firstName", n, p.FirstName, q.FirstName);
            Check("participant", "surname", n, p.Surname, q.Surname);
            Check("participant", "rating", n, p.Rating, q.Rating);
            Check("participant", "rank", n, p.Rank, q.Rank);
            Check("participant", "rankShift", n, p.RankShift, q.RankShift);
            Check("participant", "grade", n, p.Grade, q.Grade);
            Check("participant", "country", n, p.Country, q.Country);
            Check("participant", "club", n, p.Club, q.Club);
            Check("participant", "nationality", n, p.Nationality, q.Nationality);
            Check("participant", "team", n, p.Team, q.Team);
            Check("participant", "coach", n, p.Coach, q.Coach);
            Check("participant", "preliminary", n, p.Preliminary, q.Preliminary);
            Check("participant", "availability", n, string.Join(',', p.NotPlayingRounds), string.Join(',', q.NotPlayingRounds));
            Check("participant", "comment", n, p.Comment, q.Comment);
            Check("participant", "firstEligibleRound", n, p.FirstEligibleRound, q.FirstEligibleRound);
            Check("participant", "startingNumber", n, p.StartingNumber, q.StartingNumber); // spec 0002 §6: no XML mapping
        }

        foreach (var round in t.Rounds)
        {
            var other = back.Rounds.FirstOrDefault(r => r.Number == round.Number)?.Pairings ?? [];
            for (var i = 0; i < round.Pairings.Count; i++)
            {
                var p = round.Pairings[i];
                var q = i < other.Count ? other[i] : new Pairing();
                var n = $"{round.Number}/{p.BoardNumber}";
                Check("pairing", "players", n, (p.BlackId, p.WhiteId, p.IsBye), (q.BlackId, q.WhiteId, q.IsBye));
                Check("pairing", "result", n, (p.Result, p.LegacyResultToken), (q.Result, q.LegacyResultToken));
                Check("pairing", "details", n,
                    (p.BoardNumber, p.BoardNumberFixed, p.Handicap, p.AdditionalKomi, p.AllowDraw, p.ResultByReferee, p.Comment, p.Protected),
                    (q.BoardNumber, q.BoardNumberFixed, q.Handicap, q.AdditionalKomi, q.AllowDraw, q.ResultByReferee, q.Comment, q.Protected));
            }
        }

        var limitations = found
            .GroupBy(f => (f.Scope, f.Field))
            .Select(g => new ExportLimitation(g.Key.Scope, g.Key.Field, g.Count(),
                g.Select(x => x.Example).Where(x => x.Length > 0).Take(5).ToList()))
            .ToList();
        return new ExportReport(false, null, null, limitations);
    }

    private static string EnabledCriteria(Tournament t) =>
        string.Join(',', t.Criteria.Where(c => c.Enabled && CriterionKeys.AppliesTo(c.Key, t.System)).Select(c => c.Key));

    private static string Describe(PairingConstraints c) =>
        $"{c.AvoidSameCountry}{c.AvoidSameClub}{c.AvoidSameNationality}{c.AvoidSameTeam}{c.AvoidSameCoach}";

    private static string Describe(IEnumerable<PairingStage> stages) =>
        string.Join(";", (stages.Any() ? stages : [new PairingStage()]).Select(s =>
            $"{s.StartRound}:{s.System}:{(s.FrozenRosterIds is null ? "-" : string.Join(",", s.FrozenRosterIds))}:{s.RandomStartingNumbers}"));

    private static string Describe(CalculationSettings c) =>
        $"{c.SkippedRoundScoring}{c.AddNothingToOwnCoefficients}{c.AddMaximumToOpponentCoefficients}{c.TopGroupOnly}";
}
