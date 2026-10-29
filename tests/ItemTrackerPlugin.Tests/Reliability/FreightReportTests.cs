using ItemTrackerPlugin.Data;
using ItemTrackerPlugin.Model;
using ItemTrackerPlugin.Resolve;
using Microsoft.Data.Sqlite;

namespace ItemTrackerPlugin.Tests.Reliability;

public sealed class FreightReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"itrk-fr-{Guid.NewGuid():N}");
    private readonly TrackerDb _db;

    public FreightReportTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new TrackerDb(Path.Combine(_dir, "tracker.db"));
        _db.Initialize();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private void Sql(string sql)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static ItemHolding At(string station, int quantity) => new(
        Geid: null, ItemClass: "item", quantity, [new HoldingLink(InventoryKind.Location, station, "Station")],
        DateTimeOffset.UnixEpoch, "Store", Score: 0.8, IdentityAmbiguous: false, Caveats: []);

    [Fact]
    public void The_table_sets_each_stations_total_beside_the_ledger_count()
    {
        Sql("""
            INSERT INTO location_name(location_id, name) VALUES('111', 'Keeger');
            INSERT INTO freight_total(location_id, floor, total) VALUES('111', 500, 540);
            INSERT INTO freight_total(location_id, floor, total) VALUES('222', 250, NULL);
            """);
        var report = new ReliabilityReport("test", 1, 1);

        ReliabilityStudy.MeasureFreight(_db, [At("111", 3), At("111", 2), At("999", 7)], report);

        Assert.Equal(2, report.Counts["freight.stations"]);
        Assert.Equal(1, report.Counts["freight.stations_with_total"]);

        var markdown = report.ToMarkdown(null);
        Assert.Contains("## Freight total vs ledger count", markdown, StringComparison.Ordinal);
        Assert.Contains("| `Keeger` | 540 | 500 | 5 | -535 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| `id:222` | — | 250 | 0 | — |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void No_freight_pages_means_no_section()
    {
        var report = new ReliabilityReport("test", 1, 1);

        ReliabilityStudy.MeasureFreight(_db, [], report);

        Assert.DoesNotContain("Freight total vs ledger count", report.ToMarkdown(null), StringComparison.Ordinal);
    }
}
