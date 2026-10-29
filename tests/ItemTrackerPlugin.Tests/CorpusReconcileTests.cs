using ItemTrackerPlugin.Data;
using ItemTrackerPlugin.Ingest;
using ItemTrackerPlugin.Model;
using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Resolve;
using ItemTrackerPlugin.Screen;
using Microsoft.Data.Sqlite;

namespace ItemTrackerPlugin.Tests;

/// <summary>
/// The IT-03 kiosk corpus against its own Game.log. The observations are the manifest's, not a fresh
/// OCR run: the replay-parity test already pins that the frames read as the manifest says, and this
/// one needs no engine.
/// </summary>
public sealed class CorpusReconcileTests : IDisposable
{
    private const string Keeger = "3058615591";

    /// <summary>
    /// What the batch Move at 19:28:26Z took out of the kiosk into the backpack and left there. The
    /// fifth item of the batch, the mining module, went back to the kiosk at 19:32:48.
    /// </summary>
    private static readonly string[] BatchMoved =
    [
        "Paint_Star_Runner_BIS2951", "Paint_Scorpius_BIS2952_Black_Red", "Paint_Aurora_Luminalia_2951_White_Blue",
    ];

    /// <summary>The frames were captured in EDT; their file names carry that local time.</summary>
    private static readonly TimeSpan CaptureOffset = TimeSpan.FromHours(-4);

    /// <summary>The <c>global.ini</c> lines of the items the frames show, as the 4.x client has them.</summary>
    private static readonly GameLocalization Text = GameLocalization.Parse(
    [
        "item_NameBEHR_LaserCannon_S2=M4A Cannon Defense Division",
        "item_NameDrink_bottle_cruz_01_dark_a=CRUZ Dark",
        "item_NameKLWE_LaserRepeater_S2=CF-227 Badger Repeater",
        "item_Name_thp_light_arms_01_01_01=Aztalan Arms",
        "item_Name_thp_light_legs_01_01_01=Aztalan Legs",
        "item_Nameklwe_pistol_energy_01_chromic01=Arclight \"Glimmer\" Pistol",
    ]);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"itrk-recon-{Guid.NewGuid():N}");
    private readonly TrackerDb _db;
    private readonly ScreenCorpus _corpus = ScreenCorpus.Load("item-tracker-kiosk");

    public CorpusReconcileTests()
    {
        Directory.CreateDirectory(_dir);
        File.Copy(Path.Combine(_corpus.Directory, "Game_20261028.log"), Path.Combine(_dir, "Game.log"));
        _db = new TrackerDb(Path.Combine(_dir, "tracker.db"));
        _db.Initialize();
        new LogIngestor(_db, _dir).IngestAll();

        foreach (var frame in _corpus.Frames)
        {
            if (frame.Observation is not { } o) continue;
            var at = new DateTimeOffset(frame.CapturedAtLocal, CaptureOffset);
            ObservationStore.Add(_db, new StoredObservation(
                at, Enum.Parse<ScreenContext>(o.Panel), null, o.Name, null, o.Quantity, o.Page, o.Pages, o.Row, o.Column,
                o.Panel == "Personal" ? "tooltip-left" : "tooltip-right", frame.File));
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private Reconciliation Reconcile()
    {
        var resolver = HoldingResolver.Load(_db, new DateTimeOffset(2026, 10, 28, 20, 0, 0, TimeSpan.Zero));
        var input = ReconcileSources.Load(_db, resolver, resolver.ResolveAll());
        return new Reconciler(new ItemNamer(Text)).Run(input);
    }

    [Fact]
    public void Every_kiosk_frame_is_placed_at_Keeger_and_nothing_conflicts()
    {
        var result = Reconcile();

        Assert.Equal(0, result.Unplaced);
        Assert.Equal(0, result.Count(PlacementState.Conflict));
        Assert.All(result.Coverage, c => Assert.Equal(Keeger, c.LocationId));
        Assert.All(result.Coverage, c => Assert.False(c.Complete));
    }

    [Fact]
    public void What_the_logs_never_placed_there_is_seen_with_unknown_origin()
    {
        var arrivals = Reconcile().Arrivals.ToDictionary(a => a.Name);

        Assert.Equal(
            ["Arclight \"Glimmer\" Pistol", "Aztalan Arms", "CF-227 Badger Repeater", "CRUZ Dark", "M4A Cannon Defense Division"],
            arrivals.Keys.Order(StringComparer.Ordinal));
        Assert.All(arrivals.Values, a => Assert.Equal(Keeger, a.LocationId));
        Assert.All(arrivals.Values, a => Assert.Equal(PlacementState.Verified, a.State));
        Assert.Equal(2, arrivals["CRUZ Dark"].Quantity);
        Assert.EndsWith("origin unknown", arrivals["M4A Cannon Defense Division"].Reason);
    }

    [Fact]
    public void The_batch_move_takes_its_items_out_of_the_kiosk_and_later_frames_agree()
    {
        var result = Reconcile();

        foreach (var itemClass in BatchMoved)
        {
            var rows = result.Holdings.Where(h => h.Holding.ItemClass == itemClass).ToList();
            Assert.NotEmpty(rows);
            Assert.All(rows, r => Assert.NotEqual(Keeger, r.Holding.Chain.FirstOrDefault()?.Key));
            Assert.All(rows, r => Assert.Equal(PlacementState.Deduced, r.State));
        }

        // Nothing the ledger has directly at the kiosk is contradicted by the frames after the move.
        Assert.DoesNotContain(result.Holdings, h => h.Holding.Chain is [{ Kind: InventoryKind.Location, Key: Keeger }, ..] && h.State == PlacementState.Conflict);
    }
}
