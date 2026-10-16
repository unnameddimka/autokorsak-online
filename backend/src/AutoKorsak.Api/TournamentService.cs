using System.Text.Json;
using AutoKorsak.Domain;
using AutoKorsak.LegacyXml;

namespace AutoKorsak.Api;

/// <summary>What the browser receives: the tournament without retained legacy XML, plus small derived facts.</summary>
public sealed record TournamentView(
    Tournament Tournament,
    bool Imported,
    IReadOnlyDictionary<int, int> GameCounts,
    bool SystemLocked,
    int? DefaultJoinRound,
    RoundRobinState? RoundRobin,
    object? Result = null);

/// <summary>
/// Application service: loads an aggregate, applies a domain operation and saves it atomically.
/// Writes are serialized (single local referee instance; no multi-user concurrency in P0).
/// </summary>
public sealed class TournamentService(Storage storage, TimeProvider clock)
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public IReadOnlyList<TournamentSummary> List() => storage.ListTournaments();

    public Tournament Load(Guid id)
    {
        var t = storage.GetTournament(id) ?? throw new NotFoundException("tournament.notFound");
        t.EnsureStages(); // pre-0001 data: one stage with the stored system (persisted on the next save)
        return t;
    }

    public TournamentView Get(Guid id) => View(Load(id));

    public TournamentView Create(SettingsInput input) =>
        Locked(() =>
        {
            var t = TournamentEditor.Create(input, clock.GetUtcNow());
            t.UpdatedAt = t.CreatedAt;
            storage.SaveTournament(t);
            return View(t);
        });

    public TournamentView Import(byte[] data) =>
        Locked(() =>
        {
            var t = LegacyXmlImporter.Import(data, clock.GetUtcNow());
            storage.SaveTournament(t);
            return View(t);
        });

    public TournamentView Mutate(Guid id, Func<Tournament, object?> operation) =>
        Locked(() =>
        {
            var t = Load(id);
            var result = operation(t);
            t.UpdatedAt = clock.GetUtcNow();
            storage.SaveTournament(t);
            return View(t, result);
        });

    public void Delete(Guid id) =>
        Locked<object?>(() => storage.DeleteTournament(id) ? null : throw new NotFoundException("tournament.notFound"));

    public IReadOnlyList<PlayerCatalogEntry> SearchCatalog(string? search, int limit) =>
        storage.ListCatalog()
            .Where(c => c.Matches(search ?? ""))
            .OrderBy(c => c.DisplayName, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();

    public PlayerCatalogEntry? FindCatalogEntry(Guid id) => storage.ListCatalog().FirstOrDefault(c => c.Id == id);

    public UpsertPlan PlanUpsert(Guid id) => CatalogUpsert.Plan(Load(id), storage.ListCatalog());

    public TournamentView ApplyUpsert(Guid id, IReadOnlyList<UpsertDecision> decisions) =>
        Locked(() =>
        {
            var t = Load(id);
            var now = clock.GetUtcNow();
            var (outcome, changed) = CatalogUpsert.Apply(t, storage.ListCatalog(), decisions, now);
            t.UpdatedAt = now;
            storage.SaveTournament(t, changed);
            return View(t, outcome);
        });

    private static T Locked<T>(Func<T> action)
    {
        WriteLock.Wait();
        try { return action(); }
        finally { WriteLock.Release(); }
    }

    public static TournamentView View(Tournament t, object? result = null)
    {
        t.EnsureStages();
        var copy = JsonSerializer.Deserialize<Tournament>(JsonSerializer.Serialize(t, Storage.Json), Storage.Json)!;
        copy.LegacySourceXml = null;
        foreach (var p in copy.Participants) p.LegacyXml = null;
        foreach (var r in copy.Rounds)
        {
            r.LegacyXml = null;
            foreach (var p in r.Pairings) p.LegacyXml = null;
        }
        var games = t.Participants.ToDictionary(p => p.Id, p => TournamentEditor.GameCount(t, p.Id));
        return new TournamentView(copy, t.LegacySourceXml is not null, games,
            TournamentEditor.IsSystemLocked(t), TournamentEditor.DefaultJoinRound(t), PairingSchedule.DescribeRoundRobin(t), result);
    }
}
