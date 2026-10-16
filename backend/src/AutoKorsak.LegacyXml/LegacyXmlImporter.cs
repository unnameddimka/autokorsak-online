using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using AutoKorsak.Domain;

namespace AutoKorsak.LegacyXml;

/// <summary>
/// Reads AutoKorsak 1.38 tournament XML into the domain model. Parsing is hardened against XXE
/// (DTDs prohibited, no resolver) and bounded in size and depth. Any validation failure throws a
/// <see cref="DomainException"/> with an <c>xml.*</c> code before anything is persisted.
/// </summary>
public static class LegacyXmlImporter
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxDepth = 32;

    public static Tournament Import(byte[] data, DateTimeOffset now)
    {
        var doc = Parse(data);
        var tournament = FromDocument(doc, now);
        tournament.LegacySourceXml = doc.Declaration + Environment.NewLine + doc.Root!.ToString(SaveOptions.DisableFormatting);
        return tournament;
    }

    public static XDocument Parse(byte[] data)
    {
        if (data.Length == 0) throw new DomainException("xml.empty");
        if (data.Length > MaxBytes) throw new DomainException("xml.tooLarge", new Dictionary<string, object?> { ["maxMb"] = MaxBytes / 1024 / 1024 });

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxBytes,
            MaxCharactersFromEntities = 1024,
            CloseInput = true,
        };
        try
        {
            using (var depthCheck = XmlReader.Create(new MemoryStream(data), settings))
                while (depthCheck.Read())
                    if (depthCheck.Depth > MaxDepth) throw new DomainException("xml.tooDeep");

            using var reader = XmlReader.Create(new MemoryStream(data), settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new DomainException("xml.malformed", new Dictionary<string, object?>
            {
                ["line"] = ex.LineNumber,
                ["position"] = ex.LinePosition,
            });
        }
    }

    public static Tournament FromDocument(XDocument doc, DateTimeOffset now)
    {
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "Tournament") throw new DomainException("xml.notTournament");
        var version = (string?)root.Attribute("typeversion");
        if (version != LegacyFormat.SupportedTypeVersion)
            throw new DomainException("xml.unsupportedVersion", Args(("version", version ?? "")));

        var kind = Int(root, "Kind", 0);
        TournamentSystem? system = kind == LegacyFormat.SwissKind ? TournamentSystem.Swiss : null;
        var rounds = Int(root, "NumberOfRounds", 0);
        if (rounds is < 1 or > TournamentEditor.MaxRounds) throw Invalid("NumberOfRounds", rounds.ToString(CultureInfo.InvariantCulture));

        var t = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = Str(root, "Name"),
            Description = Str(root, "Description"),
            System = system,
            LegacyKind = kind,
            BoardSize = Int(root, "Boardsize", 19),
            FirstMoveColor = Int(root, "FirstMoveColor", 0) == 0 ? StoneColor.Black : StoneColor.White,
            NumberOfRounds = rounds,
            HandicapUsed = Bool(root, "HandicapUsed"),
            AllowDraws = Bool(root, "AllowJigo"),
            RankFromRating = Bool(root, "RatingDeterminesRank"),
            CustomizeCalculation = Bool(root, "CustomizeCalculation"),
            CurrentRound = Math.Clamp(Int(root, "CurrentRoundNumber", 1), 1, rounds),
            IncludeCurrentRound = Bool(root, "TakeCurrentRoundInAccount"),
            PairingConstraints = new PairingConstraints
            {
                AvoidSameCountry = Bool(root, "MakePairingOutsideTopGroupSameCountry"),
                AvoidSameClub = Bool(root, "MakePairingOutsideTopGroupSameClub"),
                AvoidSameNationality = Bool(root, "MakePairingOutsideTopGroupSameNationality"),
                AvoidSameTeam = Bool(root, "MakePairingOutsideTopGroupSameTeam"),
                AvoidSameCoach = Bool(root, "MakePairingOutsideTopGroupSameCoach"),
            },
            Calculation = new CalculationSettings
            {
                SkippedRoundScoring = Bool(root, "CalculationScoreAddAlternate") ? SkippedRoundScoring.Alternate
                    : Bool(root, "CalculationScoreAddNothing") ? SkippedRoundScoring.AddNothing
                    : SkippedRoundScoring.AddHalf,
                AddNothingToOwnCoefficients = Bool(root, "CalculationScoreZeroToOwnCoefs"),
                AddMaximumToOpponentCoefficients = Bool(root, "CalculationScoreMaxToCompetitorCoefs"),
                TopGroupOnly = Bool(root, "CustomizeForTopGroupOnly"),
            },
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (t.BoardSize is < 2 or > 52) throw Invalid("Boardsize", t.BoardSize.ToString(CultureInfo.InvariantCulture));

        ReadWalllist(root, t);

        foreach (var el in root.Elements("IndividualParticipant"))
        {
            var p = ReadParticipant(el, rounds);
            if (t.Participants.Any(x => x.Id == p.Id)) throw new DomainException("xml.duplicateParticipantId", Args(("id", p.Id)));
            t.Participants.Add(p);
        }

        foreach (var el in root.Elements("TournamentRound"))
            ReadRound(el, t);
        t.Rounds.Sort((a, b) => a.Number.CompareTo(b.Number));
        t.EnsureStages(); // legacy XML has one system for the whole tournament: a single initial stage
        t.PairingsEverDrawn = t.Rounds.Any(r => r.Pairings.Count > 0);
        return t;
    }

    private static void ReadWalllist(XElement root, Tournament t)
    {
        var walllist = root.Element("Walllist");
        var criteria = new List<CriterionSetting>();
        var columns = new List<LayoutColumn>();
        if (walllist is not null)
        {
            foreach (var token in walllist.Elements("SortCriterionDescriptor").Select(e => Str(e, "ShortName")))
                criteria.Add(new CriterionSetting { Key = LegacyFormat.CriterionKey(token, t.System), Enabled = true });
            var byToken = LegacyFormat.ColumnTokens.ToDictionary(kv => kv.Value, kv => kv.Key);
            foreach (var token in walllist.Elements("WallListMemberDescriptor").Select(e => Str(e, "ShortName")))
                if (byToken.TryGetValue(token, out var key)) columns.Add(new LayoutColumn { Key = key, Visible = true });
            t.Layout.ShowColors = Bool(walllist, "ShowColors");
            t.Layout.HideEmptyColumns = Bool(walllist, "HideEmptyColumns");
        }
        t.Criteria = CriterionKeys.Complete(criteria);
        t.Layout.Columns = ColumnKeys.Complete(columns);
    }

    private static Participant ReadParticipant(XElement el, int rounds)
    {
        var idText = el.Element("Id")?.Value;
        if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id < 0)
            throw Invalid("IndividualParticipant/Id", idText ?? "");
        var player = el.Element("GoPlayer") ?? new XElement("GoPlayer");
        var club = player.Element("Club");
        var grade = Int(player, "Grade", 0);
        return new Participant
        {
            Id = id,
            Preliminary = Bool(el, "PreliminaryRegistration"),
            Comment = Str(el, "Comment"),
            StartScore = Int(el, "StartScores", 0),
            NotPlayingRounds = ReadNotPlayingRounds(el, rounds),
            FirstName = Str(player, "FirstName"),
            Surname = Str(player, "Surname"),
            LocalFirstName = Str(player, "LocalFirstName"),
            LocalSurname = Str(player, "LocalSurname"),
            Rank = Str(player, "GoLevel"),
            // Club structure inside GoPlayer is not verified: read a name when one is present, never write it back.
            Club = club is null ? "" : (club.Element("Name")?.Value ?? (club.HasElements ? "" : club.Value)),
            Grade = grade == 0 ? "" : grade.ToString(CultureInfo.InvariantCulture),
            Rating = Int(player, "Rating", 0),
            EgdPin = Long(player, "EgdPin"),
            LegacyXml = el.ToString(SaveOptions.DisableFormatting),
        };
    }

    /// <summary>
    /// Per-round absence is a supported field, so malformed, out-of-range or repeated values are rejected rather
    /// than dropped (dropping them would silently lose data on export).
    /// </summary>
    private static List<int> ReadNotPlayingRounds(XElement participant, int rounds)
    {
        var result = new List<int>();
        foreach (var e in participant.Elements("NotPlayingInRound"))
        {
            var text = e.Value.Trim();
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var round)
                || round < 1 || round > rounds || result.Contains(round))
                throw Invalid("NotPlayingInRound", text);
            result.Add(round);
        }
        result.Sort();
        return result;
    }

    private static void ReadRound(XElement el, Tournament t)
    {
        var number = Int(el, "RoundNumber", 0);
        if (number < 1 || number > t.NumberOfRounds) throw new DomainException("xml.roundOutOfRange", Args(("round", number)));
        if (t.Rounds.Any(r => r.Number == number)) throw new DomainException("xml.duplicateRound", Args(("round", number)));

        var shell = new XElement(el);
        shell.Elements("Pairing").Remove();
        var round = new Round { Number = number, LegacyXml = shell.ToString(SaveOptions.DisableFormatting) };
        var used = new HashSet<int>();

        foreach (var pe in el.Elements("Pairing"))
        {
            var isBye = Bool(pe, "PairingWithBye");
            var black = Int(pe, "Black", 0);
            var white = Int(pe, "White", 0);
            int? blackId = isBye && black < 0 ? null : black;
            int? whiteId = isBye ? null : white;

            foreach (var id in new[] { blackId, whiteId }.OfType<int>())
            {
                if (t.FindParticipant(id) is null)
                    throw new DomainException("xml.missingPairingReference", Args(("round", number), ("id", id)));
                if (!used.Add(id))
                    throw new DomainException("xml.playerPairedTwice", Args(("round", number), ("id", id)));
            }

            var token = pe.Element("Result")?.Value;
            var result = LegacyFormat.ParseResult(token);
            round.Pairings.Add(new Pairing
            {
                Id = t.NextPairingId++,
                BlackId = blackId,
                WhiteId = whiteId,
                IsBye = isBye,
                BoardNumber = Int(pe, "BoardNumber", 0),
                BoardNumberFixed = Bool(pe, "BoardNumberFixed"),
                Handicap = Int(pe, "Handicap", 0),
                AdditionalKomi = Dec(pe, "AdditionalKomi"),
                AllowDraw = Bool(pe, "AllowJigo"),
                Result = result,
                LegacyResultToken = result == GameResult.Unknown ? token : null,
                ResultByReferee = Bool(pe, "ResultByReferee"),
                Comment = Str(pe, "Comment"),
                Protected = Bool(pe, "ForcedPairing"),
                LegacyXml = pe.ToString(SaveOptions.DisableFormatting),
            });
        }
        t.Rounds.Add(round);
    }

    // Absent elements mean the legacy serializer's default value.
    private static string Str(XElement parent, string name) => parent.Element(name)?.Value ?? "";

    private static bool Bool(XElement parent, string name)
    {
        var el = parent.Element(name);
        if (el is null) return false;
        return el.Value.Trim() switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            var v => throw Invalid(name, v),
        };
    }

    private static int Int(XElement parent, string name, int fallback)
    {
        var el = parent.Element(name);
        if (el is null) return fallback;
        return int.TryParse(el.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw Invalid(name, el.Value);
    }

    private static long Long(XElement parent, string name)
    {
        var el = parent.Element(name);
        if (el is null) return 0;
        return long.TryParse(el.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw Invalid(name, el.Value);
    }

    private static decimal Dec(XElement parent, string name)
    {
        var el = parent.Element(name);
        if (el is null) return 0;
        return decimal.TryParse(el.Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : throw Invalid(name, el.Value);
    }

    private static DomainException Invalid(string element, string value) =>
        new("xml.invalidValue", Args(("element", element), ("value", value.Length > 50 ? value[..50] : value)));

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}
