using ItemTrackerPlugin.Data;
using Microsoft.Data.Sqlite;

namespace ItemTrackerPlugin.Tests;

/// <summary>A throwaway store to name places in, so a test can build against a real catalogue.</summary>
internal sealed class ScratchStore : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"itrk-scratch-{Guid.NewGuid():N}");

    public ScratchStore()
    {
        Directory.CreateDirectory(_directory);
        Db = new TrackerDb(Path.Combine(_directory, "tracker.db"));
        Db.Initialize();
    }

    public TrackerDb Db { get; }

    /// <summary>Pairs a location id with the internal string the game logged for it.</summary>
    public void Name(string id, string raw, int evidence) =>
        Execute(
            "INSERT INTO location_name(location_id, name, evidence_count) VALUES($id, $raw, $evidence)",
            ("$id", id), ("$raw", raw), ("$evidence", evidence));

    /// <summary>Records sightings of what a location id is called, or which system it sits in.</summary>
    public void Vote(string id, string kind, string value) =>
        Execute(
            "INSERT INTO place_evidence(location_id, kind, value, hits) VALUES($id, $kind, $value, 3)",
            ("$id", id), ("$kind", kind), ("$value", value));

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var cn = Db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
