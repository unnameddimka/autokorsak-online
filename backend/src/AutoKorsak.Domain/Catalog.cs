namespace AutoKorsak.Domain;

/// <summary>Shared canonical player record. Changed only by an explicit tournament-to-catalog upsert in P0.</summary>
public sealed class PlayerCatalogEntry
{
    public Guid Id { get; set; }
    public string LocalFirstName { get; set; } = "";
    public string LocalSurname { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string Surname { get; set; } = "";
    public int Rating { get; set; }
    public string Rank { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Country { get; set; } = "";
    public string Club { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string Team { get; set; } = "";
    public string Coach { get; set; } = "";
    /// <summary>Optional external identifier (EGD PIN); 0 when unknown.</summary>
    public long EgdPin { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public string DisplayName =>
        string.Join(' ', new[] { LocalSurname, LocalFirstName }.Where(s => s.Length > 0)) is { Length: > 0 } local
            ? local
            : string.Join(' ', new[] { Surname, FirstName }.Where(s => s.Length > 0));

    public void CopyFrom(Participant p, DateTimeOffset now)
    {
        LocalFirstName = p.LocalFirstName;
        LocalSurname = p.LocalSurname;
        FirstName = p.FirstName;
        Surname = p.Surname;
        Rating = p.Rating;
        Rank = p.Rank;
        Grade = p.Grade;
        Country = p.Country;
        Club = p.Club;
        Nationality = p.Nationality;
        Team = p.Team;
        Coach = p.Coach;
        EgdPin = p.EgdPin;
        UpdatedAt = now;
    }

    public bool Matches(string search)
    {
        var s = Normalize(search);
        return s.Length == 0 || new[] { DisplayName, $"{LocalFirstName} {LocalSurname}", $"{Surname} {FirstName}", $"{FirstName} {Surname}", Club }
            .Any(v => Normalize(v).Contains(s, StringComparison.Ordinal));
    }

    public static string Normalize(string s) =>
        string.Join(' ', s.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

public enum UpsertAction { Update, Create, NeedsDecision }

public sealed record UpsertCandidate(Guid Id, string DisplayName, int Rating, string Rank, string Club);

public sealed record UpsertPlanItem(
    int ParticipantId,
    string ParticipantName,
    UpsertAction Action,
    Guid? CatalogEntryId,
    IReadOnlyList<UpsertCandidate> Candidates);

public sealed record UpsertPlan(int ToCreate, int ToUpdate, int NeedsDecision, IReadOnlyList<UpsertPlanItem> Items);

public enum UpsertDecisionKind { Create, Link, Skip }

public sealed record UpsertDecision(int ParticipantId, UpsertDecisionKind Decision, Guid? CatalogEntryId);

public sealed record UpsertOutcome(int Created, int Updated, int Skipped);

/// <summary>
/// Plans and applies an explicit tournament → catalog upsert. Linked participants update their entry;
/// unlinked participants with possible namesakes require an explicit decision — never a silent merge.
/// </summary>
public static class CatalogUpsert
{
    public static UpsertPlan Plan(Tournament t, IReadOnlyList<PlayerCatalogEntry> catalog)
    {
        var byId = catalog.ToDictionary(c => c.Id);
        var items = new List<UpsertPlanItem>();
        foreach (var p in t.Participants)
        {
            if (p.CatalogEntryId is { } linked && byId.ContainsKey(linked))
            {
                items.Add(new UpsertPlanItem(p.Id, p.DisplayName, UpsertAction.Update, linked, []));
                continue;
            }
            var candidates = catalog.Where(c => IsPossibleMatch(p, c))
                .Select(c => new UpsertCandidate(c.Id, c.DisplayName, c.Rating, c.Rank, c.Club))
                .ToList();
            items.Add(new UpsertPlanItem(p.Id, p.DisplayName,
                candidates.Count == 0 ? UpsertAction.Create : UpsertAction.NeedsDecision, null, candidates));
        }
        return new UpsertPlan(
            items.Count(i => i.Action == UpsertAction.Create),
            items.Count(i => i.Action == UpsertAction.Update),
            items.Count(i => i.Action == UpsertAction.NeedsDecision),
            items);
    }

    private static bool IsPossibleMatch(Participant p, PlayerCatalogEntry c)
    {
        if (p.EgdPin != 0 && p.EgdPin == c.EgdPin) return true;
        var local = PlayerCatalogEntry.Normalize(p.DisplayName);
        return local.Length > 0 && local == PlayerCatalogEntry.Normalize(c.DisplayName);
    }

    /// <summary>
    /// Applies the plan. Mutates <paramref name="t"/> (catalog links) and returns the catalog entries to insert or update.
    /// </summary>
    public static (UpsertOutcome Outcome, List<PlayerCatalogEntry> Changed) Apply(
        Tournament t, IReadOnlyList<PlayerCatalogEntry> catalog, IReadOnlyList<UpsertDecision> decisions, DateTimeOffset now)
    {
        var plan = Plan(t, catalog);
        var byId = catalog.ToDictionary(c => c.Id);
        var decisionMap = decisions.GroupBy(d => d.ParticipantId).ToDictionary(g => g.Key, g => g.Last());
        var changed = new List<PlayerCatalogEntry>();
        int created = 0, updated = 0, skipped = 0;

        foreach (var item in plan.Items)
        {
            var participant = t.FindParticipant(item.ParticipantId)!;
            decisionMap.TryGetValue(item.ParticipantId, out var decision);
            if (decision?.Decision == UpsertDecisionKind.Skip) { skipped++; continue; }

            PlayerCatalogEntry entry;
            switch (item.Action)
            {
                case UpsertAction.Update:
                    entry = byId[item.CatalogEntryId!.Value];
                    updated++;
                    break;
                case UpsertAction.NeedsDecision when decision is null:
                    throw new DomainException("catalog.decisionRequired",
                        new Dictionary<string, object?> { ["name"] = item.ParticipantName });
                case UpsertAction.NeedsDecision when decision.Decision == UpsertDecisionKind.Link:
                    if (decision.CatalogEntryId is not { } target || item.Candidates.All(c => c.Id != target))
                        throw new DomainException("catalog.invalidLinkTarget",
                            new Dictionary<string, object?> { ["name"] = item.ParticipantName });
                    entry = byId[target];
                    updated++;
                    break;
                default:
                    entry = new PlayerCatalogEntry { Id = Guid.NewGuid() };
                    created++;
                    break;
            }
            entry.CopyFrom(participant, now);
            participant.CatalogEntryId = entry.Id;
            if (!changed.Contains(entry)) changed.Add(entry);
        }
        return (new UpsertOutcome(created, updated, skipped), changed);
    }

    public static ParticipantInput ToParticipantInput(PlayerCatalogEntry c) => new(
        c.LocalFirstName, c.LocalSurname, c.FirstName, c.Surname, c.Rating, c.Rank, 0, c.Grade,
        c.Country, c.Club, c.Nationality, c.Team, c.Coach, false, [], "", c.Id);
}
