using System.Text;
using System.Xml;
using System.Xml.Linq;
using AutoKorsak.Domain;
using static AutoKorsak.LegacyXml.LegacyFormat;

namespace AutoKorsak.LegacyXml;

/// <summary>
/// Writes the current tournament state as AutoKorsak 1.38 XML.
/// Imported tournaments are exported by patching their original document, so unknown legacy elements survive;
/// new tournaments start from an embedded template that contains no participant data.
/// Only verified fields are written (see <see cref="LegacyFormat"/>); everything else is left as it was.
/// </summary>
public static class LegacyXmlExporter
{
    public static XDocument Export(Tournament t)
    {
        var doc = t.LegacySourceXml is { } source ? XDocument.Parse(source) : LoadTemplate();
        var root = doc.Root!;

        Set(root, "Name", t.Name, null, RootOrder, cdata: true);
        Set(root, "Description", t.Description, null, RootOrder, cdata: true);
        Set(root, "NumberOfRounds", Format(t.NumberOfRounds), null, RootOrder);
        Set(root, "Kind", Format(KindFor(t)), null, RootOrder);
        if (t.FirstMoveColor == StoneColor.Black) Set(root, "FirstMoveColor", "0", null, RootOrder);
        Set(root, "CurrentRoundNumber", Format(t.CurrentRound), null, RootOrder);
        Set(root, "TakeCurrentRoundInAccount", Format(t.IncludeCurrentRound), "false", RootOrder);
        Set(root, "RatingDeterminesRank", Format(t.RankFromRating), "false", RootOrder);
        Set(root, "HandicapUsed", Format(t.HandicapUsed), "false", RootOrder);
        Set(root, "AllowJigo", Format(t.AllowDraws), "false", RootOrder);
        Set(root, "Boardsize", Format(t.BoardSize), null, RootOrder);
        Set(root, "CustomizeCalculation", Format(t.CustomizeCalculation), "false", RootOrder);
        var calc = t.Calculation;
        Set(root, "CalculationScoreAddHalf", Format(calc.SkippedRoundScoring == SkippedRoundScoring.AddHalf), "false", RootOrder);
        Set(root, "CalculationScoreAddAlternate", Format(calc.SkippedRoundScoring == SkippedRoundScoring.Alternate), "false", RootOrder);
        Set(root, "CalculationScoreAddNothing", Format(calc.SkippedRoundScoring == SkippedRoundScoring.AddNothing), "false", RootOrder);
        Set(root, "CalculationScoreZeroToOwnCoefs", Format(calc.AddNothingToOwnCoefficients), "false", RootOrder);
        Set(root, "CalculationScoreMaxToCompetitorCoefs", Format(calc.AddMaximumToOpponentCoefficients), "false", RootOrder);
        Set(root, "CustomizeForTopGroupOnly", Format(calc.TopGroupOnly), "false", RootOrder);
        var pc = t.PairingConstraints;
        Set(root, "MakePairingOutsideTopGroupSameCountry", Format(pc.AvoidSameCountry), "false", RootOrder);
        Set(root, "MakePairingOutsideTopGroupSameClub", Format(pc.AvoidSameClub), "false", RootOrder);
        Set(root, "MakePairingOutsideTopGroupSameNationality", Format(pc.AvoidSameNationality), "false", RootOrder);
        Set(root, "MakePairingOutsideTopGroupSameTeam", Format(pc.AvoidSameTeam), "false", RootOrder);
        Set(root, "MakePairingOutsideTopGroupSameCoach", Format(pc.AvoidSameCoach), "false", RootOrder);

        WriteWalllist(root, t);

        root.Elements("IndividualParticipant").Remove();
        root.Elements("TournamentRound").Remove();
        foreach (var p in t.Participants) root.Add(WriteParticipant(p));

        var roundNumbers = t.Rounds.Select(r => r.Number);
        if (t.LegacySourceXml is null) roundNumbers = roundNumbers.Concat(Enumerable.Range(1, t.NumberOfRounds));
        foreach (var n in roundNumbers.Where(n => n <= t.NumberOfRounds).Distinct().Order())
            root.Add(WriteRound(t.Rounds.FirstOrDefault(r => r.Number == n) ?? new Round { Number = n }));

        return doc;
    }

    public static byte[] ToBytes(XDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\r\n",
        };
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings)) doc.Save(writer);
        return ms.ToArray();
    }

    private static int KindFor(Tournament t)
    {
        if (t.System == TournamentSystem.Swiss) return SwissKind;
        if (t.System is null && t.LegacyKind is { } kind) return kind;
        throw new DomainException("export.systemNotMappable", new Dictionary<string, object?> { ["system"] = t.System?.ToString() });
    }

    private static void WriteWalllist(XElement root, Tournament t)
    {
        var walllist = root.Element("Walllist");
        if (walllist is null)
        {
            walllist = new XElement("Walllist", new XAttribute("typeversion", "3"));
            InsertOrdered(root, walllist, RootOrder);
        }
        Set(walllist, "ShowColors", Format(t.Layout.ShowColors), "false", WalllistOrder);
        Set(walllist, "HideEmptyColumns", Format(t.Layout.HideEmptyColumns), "false", WalllistOrder);

        var criteriaTokens = t.Criteria
            .Where(c => c.Enabled && CriterionKeys.AppliesTo(c.Key, t.System))
            .Select(c => CriterionToken(c.Key, t.System))
            .OfType<string>().Distinct().ToList();
        ReplaceDescriptors(walllist, "SortCriterionDescriptor", criteriaTokens);

        var known = ColumnTokens.Values.ToHashSet();
        var unknownColumns = walllist.Elements("WallListMemberDescriptor").Select(e => (string?)e.Element("ShortName") ?? "")
            .Where(token => !known.Contains(token));
        var columnTokens = t.Layout.Columns.Where(c => c.Visible).Select(c => ColumnTokens[c.Key]).Concat(unknownColumns).ToList();
        ReplaceDescriptors(walllist, "WallListMemberDescriptor", columnTokens);
    }

    private static void ReplaceDescriptors(XElement walllist, string name, List<string> tokens)
    {
        var existing = walllist.Elements(name).Select(e => (string?)e.Element("ShortName") ?? "").ToList();
        if (existing.SequenceEqual(tokens)) return;
        walllist.Elements(name).Remove();
        foreach (var token in tokens)
            InsertOrdered(walllist, new XElement(name, new XAttribute("typeversion", "2"), new XElement("ShortName", token)), WalllistOrder);
    }

    private static XElement WriteParticipant(Participant p)
    {
        var el = p.LegacyXml is { } xml ? XElement.Parse(xml) : NewParticipantElement();
        Set(el, "Id", Format(p.Id), null, ParticipantOrder);
        Set(el, "PreliminaryRegistration", Format(p.Preliminary), "false", ParticipantOrder);
        Set(el, "Comment", p.Comment, "", ParticipantOrder, cdata: true);
        Set(el, "StartScores", Format(p.StartScore), "0", ParticipantOrder);

        var existingRounds = el.Elements("NotPlayingInRound").Select(e => e.Value.Trim()).ToList();
        var rounds = p.NotPlayingRounds.Select(r => Format(r)).ToList();
        if (!existingRounds.SequenceEqual(rounds))
        {
            el.Elements("NotPlayingInRound").Remove();
            foreach (var r in rounds) InsertOrdered(el, new XElement("NotPlayingInRound", r), ParticipantOrder);
        }

        var player = el.Element("GoPlayer");
        if (player is null)
        {
            player = new XElement("GoPlayer", new XAttribute("typeversion", "1"));
            InsertOrdered(el, player, ParticipantOrder);
        }
        Set(player, "FirstName", p.FirstName, "", GoPlayerOrder, cdata: true);
        Set(player, "Surname", p.Surname, "", GoPlayerOrder, cdata: true);
        Set(player, "LocalFirstName", p.LocalFirstName, "", GoPlayerOrder, cdata: true);
        Set(player, "LocalSurname", p.LocalSurname, "", GoPlayerOrder, cdata: true);
        Set(player, "GoLevel", p.Rank, "", GoPlayerOrder);
        if (p.Grade.Length == 0 || int.TryParse(p.Grade, out _))
            Set(player, "Grade", p.Grade.Length == 0 ? "0" : p.Grade, "0", GoPlayerOrder);
        Set(player, "Rating", Format(p.Rating), "0", GoPlayerOrder);
        Set(player, "EgdPin", Format(p.EgdPin), "0", GoPlayerOrder);
        // Not written (no verified representation): club, country, nationality, team, coach, rank shift.
        return el;
    }

    private static XElement NewParticipantElement() =>
        new("IndividualParticipant", new XAttribute("typeversion", "1"),
            new XElement("Id", "0"),
            new XElement("Joker", "false"),
            new XElement("PreliminaryRegistration", "false"),
            new XElement("SuperBarMember", "false"),
            new XElement("Comment", new XCData("")),
            new XElement("StartScores", "0"),
            new XElement("StartNumber", "0"),
            new XElement("ScoreAdjustment", "false"),
            new XElement("ScoreAdjustmentValue", "0"),
            new XElement("GoPlayer", new XAttribute("typeversion", "1"),
                new XElement("AsianName", "false"),
                new XElement("FirstName", new XCData("")),
                new XElement("Surname", new XCData("")),
                new XElement("LocalFirstName", new XCData("")),
                new XElement("LocalSurname", new XCData("")),
                new XElement("GoLevel", ""),
                new XElement("Club"),
                new XElement("Grade", "0"),
                new XElement("Rating", "0"),
                new XElement("EgdPin", "0")));

    private static XElement WriteRound(Round round)
    {
        var el = round.LegacyXml is { } xml
            ? XElement.Parse(xml)
            : new XElement("TournamentRound", new XAttribute("typeversion", "1"), new XElement("RoundNumber", "0"));
        Set(el, "RoundNumber", Format(round.Number), null, RoundOrder);
        el.Elements("Pairing").Remove();
        foreach (var p in round.Pairings) el.Add(WritePairing(p));
        return el;
    }

    private static XElement WritePairing(Pairing p)
    {
        var el = p.LegacyXml is { } xml ? XElement.Parse(xml) : NewPairingElement();
        Set(el, "ForcedPairing", Format(p.Protected), "false", PairingOrder);
        Set(el, "PairingWithBye", Format(p.IsBye), "false", PairingOrder);
        Set(el, "Black", Format(p.BlackId ?? -1), null, PairingOrder);
        if (p.IsBye) el.Elements("White").Remove();
        else Set(el, "White", Format(p.WhiteId ?? 0), null, PairingOrder);
        Set(el, "Handicap", Format(p.Handicap), "0", PairingOrder);
        Set(el, "AdditionalKomi", Format(p.AdditionalKomi), "0", PairingOrder);
        Set(el, "AllowJigo", Format(p.AllowDraw), "false", PairingOrder);
        // A draw has no verified token: export it as "not set" rather than a wrong winner (reported as a limitation).
        var token = p.Result == GameResult.Unknown ? p.LegacyResultToken : ResultToken(p.Result);
        Set(el, "Result", token ?? "?-?", "?-?", PairingOrder);
        Set(el, "ResultByReferee", Format(p.ResultByReferee), "false", PairingOrder);
        Set(el, "BoardNumber", Format(p.BoardNumber), "0", PairingOrder);
        Set(el, "BoardNumberFixed", Format(p.BoardNumberFixed), "false", PairingOrder);
        Set(el, "Comment", p.Comment, "", PairingOrder, cdata: true);
        return el;
    }

    private static XElement NewPairingElement() =>
        new("Pairing", new XAttribute("typeversion", "1"),
            new XElement("ForcedPairing", "false"),
            new XElement("PairingWithBye", "false"),
            new XElement("Black", "0"),
            new XElement("White", "0"),
            new XElement("Handicap", "0"),
            new XElement("AdditionalKomi", "0"),
            new XElement("AllowJigo", "false"),
            new XElement("UpDownBalance", "0"),
            new XElement("Result", "?-?"),
            new XElement("ResultByReferee", "false"),
            new XElement("ResultPoints", "0"),
            new XElement("BoardNumber", "0"),
            new XElement("BoardNumberFixed", "false"),
            new XElement("Comment", new XCData("")));

    /// <summary>
    /// Sets a child element's value, leaving untouched elements byte-for-byte intact. A missing element whose
    /// value equals the legacy default stays missing (the legacy serializer omits defaults); otherwise it is
    /// inserted at its canonical position.
    /// </summary>
    private static void Set(XElement parent, string name, string value, string? defaultValue, IReadOnlyList<string> order, bool cdata = false)
    {
        var el = parent.Element(name);
        if (el is null)
        {
            if (defaultValue is not null && value == defaultValue) return;
            el = new XElement(name);
            InsertOrdered(parent, el, order);
        }
        else if (el.Value == value && !el.HasElements && (!cdata || el.Nodes().Any() || value.Length == 0))
        {
            return;
        }
        if (cdata) el.ReplaceNodes(new XCData(value));
        else el.Value = value;
    }

    private static void InsertOrdered(XElement parent, XElement child, IReadOnlyList<string> order)
    {
        var index = IndexOf(order, child.Name.LocalName);
        if (index >= 0)
        {
            var before = parent.Elements().LastOrDefault(e => IndexOf(order, e.Name.LocalName) is var i && i >= 0 && i <= index);
            if (before is not null) { before.AddAfterSelf(child); return; }
            var after = parent.Elements().FirstOrDefault(e => IndexOf(order, e.Name.LocalName) > index);
            if (after is not null) { after.AddBeforeSelf(child); return; }
        }
        parent.Add(child);
    }

    private static int IndexOf(IReadOnlyList<string> order, string name)
    {
        for (var i = 0; i < order.Count; i++)
            if (order[i] == name) return i;
        return -1;
    }
}
