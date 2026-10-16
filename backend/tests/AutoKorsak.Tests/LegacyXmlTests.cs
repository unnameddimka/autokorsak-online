using System.Text;
using System.Xml.Linq;
using AutoKorsak.Domain;
using AutoKorsak.LegacyXml;

namespace AutoKorsak.Tests;

public class LegacyXmlTests
{
    private static Tournament RoundTrip(Tournament t) =>
        LegacyXmlImporter.Import(LegacyXmlExporter.ToBytes(LegacyXmlExporter.Export(t)), DateTimeOffset.UnixEpoch);

    [Fact]
    public void Reference_fixture_imports_with_expected_shape()
    {
        var t = Fixtures.Import();

        Assert.Equal("Группа А", t.Name);
        Assert.Equal(TournamentSystem.Swiss, t.System);
        Assert.Equal(2, t.LegacyKind);
        Assert.Equal(19, t.BoardSize);
        Assert.Equal(10, t.Participants.Count);
        Assert.Equal(6, t.NumberOfRounds);
        Assert.Equal(6, t.Rounds.Count);
        Assert.All(t.Rounds, r => Assert.Equal(5, r.Pairings.Count));
        Assert.Equal(30, t.AllPairings().Count(x => x.Pairing.Result is GameResult.BlackWins or GameResult.WhiteWins));
        Assert.Equal(6, t.CurrentRound);
        Assert.True(t.IncludeCurrentRound);
        // Full local name stays in LocalSurname without guessing a split.
        var kobzar = t.ByName("Кобзар Гліб");
        Assert.Equal("Кобзар Гліб", kobzar.LocalSurname);
        Assert.Equal("", kobzar.LocalFirstName);
        Assert.Equal(1240, kobzar.Rating);
        Assert.Equal("9k", kobzar.Rank);
        Assert.Equal(
            [CriterionKeys.Wins, CriterionKeys.Sos, CriterionKeys.Sodos, CriterionKeys.Sosos],
            t.Criteria.Where(c => c.Enabled).Select(c => c.Key));
    }

    [Fact]
    public void Reference_round_six_matches_legacy_pairing_export()
    {
        var t = Fixtures.Import();
        var round6 = t.Rounds.Single(r => r.Number == 6).Pairings
            .Select(p => (p.BoardNumber, t.FindParticipant(p.BlackId!.Value)!.DisplayName, t.FindParticipant(p.WhiteId!.Value)!.DisplayName, p.Result))
            .ToList();
        // From legacy-autokorsak/pairing-А_11.12.21.txt
        Assert.Equal(
        [
            (1, "Медведський Іван", "Кобзар Гліб", GameResult.WhiteWins),
            (2, "Калашнік Іван", "Стеценко Олексій", GameResult.WhiteWins),
            (3, "Кисельова Марія", "Єрємєєв Матвій", GameResult.WhiteWins),
            (4, "Смирнов Аркадій", "Шевченко Валерія", GameResult.BlackWins),
            (5, "Колот Олександра", "Бровкін Олександр", GameResult.BlackWins),
        ], round6);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Unedited_export_preserves_every_original_element(string fixture)
    {
        var original = LegacyXmlImporter.Parse(Fixtures.Read(fixture));
        var t = LegacyXmlImporter.Import(Fixtures.Read(fixture), DateTimeOffset.UnixEpoch);

        var exported = LegacyXmlImporter.Parse(LegacyXmlExporter.ToBytes(LegacyXmlExporter.Export(t)));

        Assert.True(XNode.DeepEquals(Normalize(original.Root!), Normalize(exported.Root!)),
            "Export without edits must be semantically identical to the source document.");
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Unedited_round_trip_keeps_model_equivalent(string fixture)
    {
        var t = Fixtures.Import(fixture);
        var back = RoundTrip(t);
        Assert.Equal(Snapshot(t), Snapshot(back));
        Assert.Empty(ExportAnalyzer.Analyze(t).Limitations);
    }

    [Fact]
    public void Edits_survive_round_trip_and_leave_others_unchanged()
    {
        var t = Fixtures.Import();
        var before = Snapshot(t);
        var kobzar = t.ByName("Кобзар Гліб");
        kobzar.Rating = 1300;
        kobzar.Comment = "Коментар судді ✓";
        var game = t.Rounds.Single(r => r.Number == 6).Pairings.Single(p => p.BoardNumber == 4);
        game.Result = GameResult.WhiteWins;

        var back = RoundTrip(t);

        Assert.Equal(1300, back.ByName("Кобзар Гліб").Rating);
        Assert.Equal("Коментар судді ✓", back.ByName("Кобзар Гліб").Comment);
        Assert.Equal(GameResult.WhiteWins, back.Rounds.Single(r => r.Number == 6).Pairings.Single(p => p.BoardNumber == 4).Result);
        var changedLines = Snapshot(back).Split('\n').Except(before.Split('\n')).ToList();
        Assert.Equal(2, changedLines.Count); // one participant line, one pairing line
    }

    [Fact]
    public void Unsupported_fields_survive_round_trip()
    {
        var t = Fixtures.Import();
        t.ByName("Кобзар Гліб").Rating = 1250;
        var xml = Encoding.UTF8.GetString(LegacyXmlExporter.ToBytes(LegacyXmlExporter.Export(t)));

        Assert.Equal(30, Count(xml, "<ResultPoints>0</ResultPoints>"));
        Assert.Equal(248, Count(xml, "<Country typeversion=\"1\">"));
        Assert.Contains("<PrintWalllistFont>Courier New</PrintWalllistFont>", xml);
        Assert.Contains("<RatingLowestOneDanRating>2050</RatingLowestOneDanRating>", xml);
        Assert.Contains("<UpDownBalance>0</UpDownBalance>", xml);
        Assert.Contains("<VersionCreated>AutoKorsak 1.38</VersionCreated>", xml);
    }

    [Fact]
    public void Unicode_survives_round_trip_exactly()
    {
        var t = Fixtures.Import();
        t.Description = "Опис: ґ є ї і — 囲碁 ★";
        t.ByName("Кобзар Гліб").LocalFirstName = "Ґлібʼян";
        var back = RoundTrip(t);
        Assert.Equal("Группа А", back.Name);
        Assert.Equal("Опис: ґ є ї і — 囲碁 ★", back.Description);
        Assert.Contains(back.Participants, p => p.LocalFirstName == "Ґлібʼян");
    }

    [Fact]
    public void Byes_and_availability_from_additional_fixtures_are_read()
    {
        var bye = Fixtures.Import(Fixtures.ByeRelativePath);
        Assert.Contains(bye.AllPairings(), x => x.Pairing.IsBye);

        var odd = Fixtures.Import(Fixtures.OddPlayersRelativePath);
        Assert.Equal(9, odd.Participants.Count);
        Assert.Contains(odd.AllPairings(), x => x.Pairing.IsBye && x.Pairing.BlackId is not null);

        var availability = Fixtures.Import(Fixtures.AvailabilityRelativePath);
        Assert.Contains(availability.Participants, p => p.NotPlayingRounds.Count > 0);
    }

    [Fact]
    public void New_tournament_exports_valid_reimportable_xml_without_sample_participants()
    {
        var t = TournamentEditor.Create(Settings("Новий турнір", TournamentSystem.Swiss), DateTimeOffset.UnixEpoch);
        var a = TournamentEditor.AddParticipant(t, Player("Тест Гравець"));
        var b = TournamentEditor.AddParticipant(t, Player("Інший Гравець"));
        TournamentEditor.AddPairing(t, 1, Pair(a.Id, b.Id, GameResult.BlackWins));

        var xml = Encoding.UTF8.GetString(LegacyXmlExporter.ToBytes(LegacyXmlExporter.Export(t)));
        var back = LegacyXmlImporter.Import(Encoding.UTF8.GetBytes(xml), DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain("Кобзар", xml);
        Assert.DoesNotContain("Группа", xml);
        Assert.Equal("Новий турнір", back.Name);
        Assert.Equal(TournamentSystem.Swiss, back.System);
        Assert.Equal(2, back.Participants.Count);
        Assert.Equal(5, back.Rounds.Count);
        Assert.Equal(GameResult.BlackWins, back.Rounds[0].Pairings.Single().Result);
    }

    [Fact]
    public void Unverified_system_mapping_blocks_export_instead_of_guessing()
    {
        var t = TournamentEditor.Create(Settings("McMahon", TournamentSystem.McMahon), DateTimeOffset.UnixEpoch);
        var ex = Assert.Throws<DomainException>(() => LegacyXmlExporter.Export(t));
        Assert.Equal("export.systemNotMappable", ex.Code);
        Assert.True(ExportAnalyzer.Analyze(t).Blocked);
    }

    [Fact]
    public void App_only_fields_are_reported_as_export_limitations()
    {
        var t = Fixtures.Import();
        t.ByName("Кобзар Гліб").Team = "Київ";
        t.ByName("Кобзар Гліб").Coach = "Тренер";
        var report = ExportAnalyzer.Analyze(t);
        Assert.False(report.Blocked);
        Assert.Contains(report.Limitations, l => l.Field == "team" && l.Count == 1);
        Assert.Contains(report.Limitations, l => l.Field == "coach");
    }

    [Theory]
    [InlineData("<Tournament typeversion=\"6\"><Name>x", "xml.malformed")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE t [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><Tournament typeversion=\"6\">&x;</Tournament>", "xml.malformed")]
    [InlineData("<Other />", "xml.notTournament")]
    [InlineData("<Tournament typeversion=\"5\"><NumberOfRounds>3</NumberOfRounds></Tournament>", "xml.unsupportedVersion")]
    [InlineData("", "xml.empty")]
    public void Invalid_documents_are_rejected(string xml, string code)
    {
        var ex = Assert.ThrowsAny<DomainException>(() => LegacyXmlImporter.Import(Encoding.UTF8.GetBytes(xml), DateTimeOffset.UnixEpoch));
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void Duplicate_participant_ids_are_rejected()
    {
        var xml = Encoding.UTF8.GetString(Fixtures.Read(Fixtures.ReferenceRelativePath)).Replace("<Id>2</Id>", "<Id>1</Id>");
        var ex = Assert.Throws<DomainException>(() => LegacyXmlImporter.Import(Encoding.UTF8.GetBytes(xml), DateTimeOffset.UnixEpoch));
        Assert.Equal("xml.duplicateParticipantId", ex.Code);
    }

    [Fact]
    public void Missing_pairing_references_are_rejected()
    {
        var xml = Encoding.UTF8.GetString(Fixtures.Read(Fixtures.ReferenceRelativePath)).Replace("<White>4</White>", "<White>99</White>");
        var ex = Assert.Throws<DomainException>(() => LegacyXmlImporter.Import(Encoding.UTF8.GetBytes(xml), DateTimeOffset.UnixEpoch));
        Assert.Equal("xml.missingPairingReference", ex.Code);
    }

    [Theory]
    [InlineData("x")]   // malformed
    [InlineData("0")]   // out of range (below 1)
    [InlineData("9")]   // out of range (13x13.xml has 5 rounds)
    [InlineData("dup")] // repeated value
    public void Invalid_round_absence_values_are_rejected_not_dropped(string value)
    {
        var xml = Encoding.UTF8.GetString(Fixtures.Read(Fixtures.AvailabilityRelativePath));
        const string original = "<NotPlayingInRound>5</NotPlayingInRound>";
        Assert.Contains(original, xml);
        var replacement = value == "dup" ? original + original : $"<NotPlayingInRound>{value}</NotPlayingInRound>";
        var bad = xml.Replace(original, replacement);

        var ex = Assert.Throws<DomainException>(() => LegacyXmlImporter.Import(Encoding.UTF8.GetBytes(bad), DateTimeOffset.UnixEpoch));

        Assert.Equal("xml.invalidValue", ex.Code);
        Assert.Equal("NotPlayingInRound", ex.Args["element"]);
    }

    [Fact]
    public void Deeply_nested_documents_are_rejected()
    {
        var xml = "<Tournament typeversion=\"6\">" + string.Concat(Enumerable.Repeat("<a>", 40)) + string.Concat(Enumerable.Repeat("</a>", 40)) + "</Tournament>";
        var ex = Assert.Throws<DomainException>(() => LegacyXmlImporter.Import(Encoding.UTF8.GetBytes(xml), DateTimeOffset.UnixEpoch));
        Assert.Equal("xml.tooDeep", ex.Code);
    }

    public static TheoryData<string> AllFixtures() => new(Fixtures.All);

    internal static SettingsInput Settings(string name, TournamentSystem system, int rounds = 5) =>
        new(name, "", system, 19, StoneColor.Black, rounds, false, false, false, false, null, null, null, null);

    internal static ParticipantInput Player(string localName, bool preliminary = false, int rating = 1000) =>
        new("", localName, "", "", rating, "10k", 0, "", "", "", "", "", "", preliminary, [], "");

    internal static PairingInput Pair(int black, int white, GameResult result = GameResult.Unset, int? board = null) =>
        new(black, white, false, board, false, 0, 0, false, result, false, "", false);

    private static int Count(string text, string fragment)
    {
        var count = 0;
        for (var i = text.IndexOf(fragment, StringComparison.Ordinal); i >= 0; i = text.IndexOf(fragment, i + 1, StringComparison.Ordinal)) count++;
        return count;
    }

    private static XElement Normalize(XElement e) =>
        new(e.Name, e.Attributes().OrderBy(a => a.Name.ToString()),
            e.HasElements ? e.Elements().Select(Normalize) : (object)e.Value);

    /// <summary>Text snapshot of all supported fields (excluding internal ids and retained XML), one entity per line.</summary>
    internal static string Snapshot(Tournament t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"T|{t.Name}|{t.Description}|{t.System}|{t.LegacyKind}|{t.BoardSize}|{t.FirstMoveColor}|{t.NumberOfRounds}|{t.HandicapUsed}|{t.AllowDraws}|{t.RankFromRating}|{t.CustomizeCalculation}|{t.CurrentRound}|{t.IncludeCurrentRound}");
        sb.AppendLine("C|" + string.Join(',', t.Criteria.Select(c => $"{c.Key}:{c.Enabled}")));
        sb.AppendLine("L|" + string.Join(',', t.Layout.Columns.Select(c => $"{c.Key}:{c.Visible}")) + $"|{t.Layout.ShowColors}|{t.Layout.HideEmptyColumns}");
        foreach (var p in t.Participants)
            sb.AppendLine($"P|{p.Id}|{p.LocalFirstName}|{p.LocalSurname}|{p.FirstName}|{p.Surname}|{p.Rating}|{p.Rank}|{p.Grade}|{p.Club}|{p.EgdPin}|{p.StartScore}|{p.Preliminary}|{string.Join(',', p.NotPlayingRounds)}|{p.Comment}");
        foreach (var r in t.Rounds)
            foreach (var p in r.Pairings)
                sb.AppendLine($"G|{r.Number}|{p.BlackId}|{p.WhiteId}|{p.IsBye}|{p.BoardNumber}|{p.BoardNumberFixed}|{p.Handicap}|{p.AdditionalKomi}|{p.AllowDraw}|{p.Result}|{p.LegacyResultToken}|{p.ResultByReferee}|{p.Comment}|{p.Protected}");
        return sb.ToString();
    }
}
