using ItemTrackerPlugin.Data;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Screen;
using Microsoft.Data.Sqlite;

namespace ItemTrackerPlugin.Tests;

public sealed class ObservationStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"itrk-obs-{Guid.NewGuid():N}");
    private readonly TrackerDb _db;

    public ObservationStoreTests()
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

    private List<StoredObservation> Load()
    {
        using var cn = _db.Open();
        return ObservationStore.Load(cn);
    }

    private static readonly ScreenObservation Badger = new(
        ScreenContext.Kiosk, null,
        new TooltipReading("CF-227 Badger Repeater", 252000, "Klaus & Werner", "Laser Repeater", null, 2, "0/0", null, true),
        "KLWE_LaserRepeater_S2", 2, new GridTile(3, 1, 1), new PageCounter(1, 4), null, "tooltip-right");

    [Fact]
    public void An_observation_keeps_its_provenance()
    {
        var at = new DateTimeOffset(2026, 10, 28, 19, 28, 7, TimeSpan.Zero);
        ObservationStore.Add(_db, Badger, at);

        var stored = Assert.Single(Load());
        Assert.Equal(at, stored.At);
        Assert.Equal(ScreenContext.Kiosk, stored.Panel);
        Assert.Equal("CF-227 Badger Repeater", stored.Name);
        Assert.Equal("KLWE_LaserRepeater_S2", stored.ItemClass);
        Assert.Equal(2, stored.Quantity);
        Assert.Equal((1, 4, 3, 1), (stored.Page, stored.Pages, stored.Row, stored.Column));
        Assert.Equal("tooltip-right", stored.SourceRoi);
        Assert.Contains("Klaus", stored.RawText);
    }

    [Fact]
    public void A_rebuild_from_the_logs_keeps_what_the_screen_showed()
    {
        ObservationStore.Add(_db, Badger with { Tile = null, Page = null, Quantity = null, ItemClass = null }, DateTimeOffset.UtcNow);

        _db.ResetIngest();
        _db.Initialize();

        var stored = Assert.Single(Load());
        Assert.Null(stored.Quantity);
        Assert.Null(stored.Row);
    }
}
