using System.Text.Json;
using System.Text.Json.Serialization;
using AutoKorsak.Domain;
using Microsoft.Data.Sqlite;

namespace AutoKorsak.Api;

public sealed record TournamentSummary(Guid Id, string Name, int PlayerCount, int RoundCount, DateTimeOffset UpdatedAt);

/// <summary>
/// SQLite persistence. Each tournament aggregate is stored as one JSON document (written atomically);
/// list columns are denormalized for the tournament list. Catalog entries are stored one row each.
/// </summary>
public sealed class Storage
{
    private const int SchemaVersion = 1;

    public static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private readonly string _connectionString;

    public Storage(string databasePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        Initialize();
    }

    public static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var c = Open();
        Execute(c, null, "PRAGMA journal_mode=WAL;");
        Execute(c, null, """
            CREATE TABLE IF NOT EXISTS tournaments (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                player_count INTEGER NOT NULL,
                round_count INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                data TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS catalog_entries (
                id TEXT PRIMARY KEY,
                updated_at TEXT NOT NULL,
                data TEXT NOT NULL
            );
            """);
        Execute(c, null, $"PRAGMA user_version = {SchemaVersion};");
    }

    public IReadOnlyList<TournamentSummary> ListTournaments()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, name, player_count, round_count, updated_at FROM tournaments ORDER BY updated_at DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<TournamentSummary>();
        while (r.Read())
            list.Add(new TournamentSummary(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetInt32(2), r.GetInt32(3), DateTimeOffset.Parse(r.GetString(4))));
        return list;
    }

    public Tournament? GetTournament(Guid id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT data FROM tournaments WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<Tournament>(json, Json) : null;
    }

    public void SaveTournament(Tournament t, IReadOnlyList<PlayerCatalogEntry>? catalogChanges = null)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO tournaments (id, name, player_count, round_count, updated_at, data)
                VALUES ($id, $name, $players, $rounds, $updated, $data)
                ON CONFLICT(id) DO UPDATE SET name = excluded.name, player_count = excluded.player_count,
                    round_count = excluded.round_count, updated_at = excluded.updated_at, data = excluded.data
                """;
            cmd.Parameters.AddWithValue("$id", t.Id.ToString());
            cmd.Parameters.AddWithValue("$name", t.Name);
            cmd.Parameters.AddWithValue("$players", t.Participants.Count);
            cmd.Parameters.AddWithValue("$rounds", t.NumberOfRounds);
            cmd.Parameters.AddWithValue("$updated", t.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(t, Json));
            cmd.ExecuteNonQuery();
        }
        foreach (var entry in catalogChanges ?? [])
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO catalog_entries (id, updated_at, data) VALUES ($id, $updated, $data)
                ON CONFLICT(id) DO UPDATE SET updated_at = excluded.updated_at, data = excluded.data
                """;
            cmd.Parameters.AddWithValue("$id", entry.Id.ToString());
            cmd.Parameters.AddWithValue("$updated", entry.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(entry, Json));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public bool DeleteTournament(Guid id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM tournaments WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<PlayerCatalogEntry> ListCatalog()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT data FROM catalog_entries";
        using var r = cmd.ExecuteReader();
        var list = new List<PlayerCatalogEntry>();
        while (r.Read()) list.Add(JsonSerializer.Deserialize<PlayerCatalogEntry>(r.GetString(0), Json)!);
        return list;
    }

    private static void Execute(SqliteConnection c, SqliteTransaction? tx, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
