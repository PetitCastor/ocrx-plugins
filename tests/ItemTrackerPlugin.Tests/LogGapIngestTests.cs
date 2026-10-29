using System.Globalization;
using ItemTrackerPlugin.Data;
using ItemTrackerPlugin.Ingest;
using Microsoft.Data.Sqlite;

namespace ItemTrackerPlugin.Tests;

public sealed class LogGapIngestTests : IDisposable
{
    private const string Station = "3058615591";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"itrk-gap-{Guid.NewGuid():N}");
    private readonly TrackerDb _db;
    private int _second;

    public LogGapIngestTests()
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

    private string Next() =>
        new DateTime(2026, 10, 28, 19, 0, 0, DateTimeKind.Utc).AddSeconds(_second++)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.000'Z'", CultureInfo.InvariantCulture);

    private string Page(string next, string end, string station = Station) =>
        $"<{Next()}> [Notice] <InventoryManagement> Freight Inventory Grid InventoryDataRequest Successful " +
        $"Inventory[204821708183:Location:{station}] Elapsed[0.3]. Page info: HasPreviousPage[0], " +
        $"HasNextPage[{next}], StartCursor[MA==], EndCursor[{end}]. [Team_CoreGameplayFeatures][Inventory][InventoryContainer]";

    private IngestStats Ingest(params string[] lines)
    {
        File.AppendAllText(Path.Combine(_dir, "Game.log"), string.Join("\n", lines) + "\n");
        return new LogIngestor(_db, _dir).IngestAll();
    }

    private string? Scalar(string sql)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private string? Total(string station = Station) =>
        Scalar($"SELECT total FROM freight_total WHERE location_id = '{station}'");

    [Fact]
    public void A_full_scroll_gives_the_station_its_total()
    {
        var stats = Ingest(Page("1", "MjUw"), Page("1", "NTAw"), Page("0", "NTQw"));

        Assert.Equal(3, stats.FreightPages);
        Assert.Equal("540", Total());
        Assert.Equal("500", Scalar($"SELECT floor FROM freight_total WHERE location_id = '{Station}'"));
    }

    [Fact]
    public void A_filtered_list_after_a_full_scroll_does_not_lower_the_total()
    {
        Ingest(Page("1", "MjUw"), Page("1", "NTAw"), Page("0", "NTQw"));

        // The player searched: 12 hits, one page, no next.
        Ingest(Page("0", "MTI="));

        Assert.Equal("540", Total());
    }

    [Fact]
    public void A_filtered_list_seen_first_is_replaced_by_the_full_one()
    {
        Ingest(Page("0", "MTI="));
        Assert.Equal("12", Total());

        Ingest(Page("1", "MjUw"), Page("0", "NTQw"));

        Assert.Equal("540", Total());
    }

    [Fact]
    public void A_last_page_that_does_not_pass_the_floor_is_not_a_total()
    {
        // Page 1 said there is more beyond 500; a last page ending at 500 contradicts it.
        Ingest(Page("1", "NTAw"), Page("0", "NTAw"));

        Assert.Equal("", Scalar($"SELECT COALESCE(total, '') FROM freight_total WHERE location_id = '{Station}'"));
    }

    [Fact]
    public void Reading_the_same_total_again_keeps_it()
    {
        Ingest(Page("0", "NTQw"));
        Ingest(Page("0", "NTQw"));

        Assert.Equal("540", Total());
    }

    [Fact]
    public void Stations_are_kept_apart()
    {
        Ingest(Page("0", "NTQw"), Page("0", "MTI=", station: "141810852"));

        Assert.Equal("540", Total());
        Assert.Equal("12", Total("141810852"));
    }

    [Fact]
    public void A_cursor_that_does_not_decode_records_nothing()
    {
        var stats = Ingest(Page("0", "bm90YW51bWJlcg=="));

        Assert.Equal(0, stats.FreightPages);
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM freight_total"));
    }

    [Fact]
    public void Signals_are_recorded_once_and_stay_out_of_the_holdings()
    {
        var lines = new[]
        {
            $"<{Next()}> [Notice] <Player Inventory Request Complete> Request[10] for 'Pilot' [2001] " +
            "Type[StackAll] Result[Succeed] Item[0] CanLockQueue[Yes]. [Team_CoreGameplayFeatures][Inventory]",
            $"<{Next()}> [Notice] <RequestLocationInventory> Player[Pilot] requested inventory for " +
            "Location[AsteroidClusterBase_Nyx_Social_Keeger_001] [Team_CoreGameplayFeatures][Inventory]",
            $"<{Next()}> [Notice] <Request Terminate Access To Inventory> Player[Pilot] terminating access to " +
            $"[204821708183:Location:{Station}] [Team_CoreGameplayFeatures][Inventory]",
        };

        var first = Ingest(lines);
        var second = new LogIngestor(_db, _dir).IngestAll();

        Assert.Equal(3, first.SignalsRecorded);
        Assert.False(first.ChangedHoldings);
        Assert.Equal(0, second.SignalsRecorded);
        Assert.Equal("stack,opened,closed",
            Scalar("SELECT group_concat(kind) FROM (SELECT kind FROM log_signal ORDER BY ts)"));
        Assert.Equal("AsteroidClusterBase_Nyx_Social_Keeger_001", Scalar("SELECT detail FROM log_signal WHERE kind = 'opened'"));
        Assert.Equal(Station, Scalar("SELECT ref FROM log_signal WHERE kind = 'closed'"));
    }

    [Fact]
    public void An_open_is_keyed_by_the_location_id_waiting_for_its_name()
    {
        Ingest(
            $"<{Next()}> [Notice] <Update Inventory Location> Player [Pilot] is changing location. " +
            $"Landing [0] -> [0]. Location [1] -> [{Station}]. [Team_CoreGameplayFeatures][Inventory]",
            $"<{Next()}> [Notice] <RequestLocationInventory> Player[Pilot] requested inventory for " +
            "Location[AsteroidClusterBase_Nyx_Social_Keeger_001] [Team_CoreGameplayFeatures][Inventory]");

        // The same key the close of that inventory uses, so IT-03 can match the two.
        Assert.Equal(Station, Scalar("SELECT ref FROM log_signal WHERE kind = 'opened'"));
    }

    [Fact]
    public void A_later_open_of_an_already_named_station_resolves_its_id_from_the_name()
    {
        Ingest(
            $"<{Next()}> [Notice] <Update Inventory Location> Player [Pilot] is changing location. " +
            $"Landing [0] -> [0]. Location [1] -> [{Station}]. [Team_CoreGameplayFeatures][Inventory]",
            $"<{Next()}> [Notice] <RequestLocationInventory> Player[Pilot] requested inventory for " +
            "Location[AsteroidClusterBase_Nyx_Social_Keeger_001] [Team_CoreGameplayFeatures][Inventory]");
        _second += 60;

        Ingest(
            $"<{Next()}> [Notice] <RequestLocationInventory> Player[Pilot] requested inventory for " +
            "Location[AsteroidClusterBase_Nyx_Social_Keeger_001] [Team_CoreGameplayFeatures][Inventory]");

        Assert.Equal("2", Scalar($"SELECT COUNT(*) FROM log_signal WHERE kind = 'opened' AND ref = '{Station}'"));
    }

    [Fact]
    public void A_kiosk_buy_with_an_unreadable_price_keeps_the_rest()
    {
        Ingest(
            $"<{Next()}> [Notice] <CEntityComponentShopUIProvider::SendShopBuyRequest> Sending SShopBuyRequest - " +
            "playerId[1] shopId[2] shopName[SCShop_Cargo_Office] kioskId[3] client_price[n/a] " +
            "itemClassGUID[27adea05] itemName[grin_tractor_01] quantity[2]  [Team_CoreGameplayFeatures][Shops][UI]");

        Assert.Equal("grin_tractor_01 x2", Scalar("SELECT detail FROM log_signal WHERE kind = 'shop_buy'"));
    }

    [Fact]
    public void A_kiosk_buy_is_recorded_with_its_answer()
    {
        Ingest(
            $"<{Next()}> [Notice] <CEntityComponentShopUIProvider::SendShopBuyRequest> Sending SShopBuyRequest - " +
            "playerId[1] shopId[2] shopName[SCShop_Cargo_Office] kioskId[3] client_price[19175.000000] " +
            "itemClassGUID[27adea05] itemName[grin_tractor_01] quantity[1]  [Team_CoreGameplayFeatures][Shops][UI]",
            $"<{Next()}> [Notice] <CEntityComponentShopUIProvider::RmShopFlowResponse> Received ShopFlowResponse - " +
            "playerId[1] shopId[2] shopName[SCShop_Cargo_Office] kioskId[3] kioskState[BuyRequestProcessing] " +
            "result[Success] type[Buying] [Team_CoreGameplayFeatures][Shops][UI]");

        Assert.Equal("grin_tractor_01 x1 @ 19175", Scalar("SELECT detail FROM log_signal WHERE kind = 'shop_buy'"));
        Assert.Equal("Buying: Success (BuyRequestProcessing)", Scalar("SELECT detail FROM log_signal WHERE kind = 'shop_flow'"));
    }
}
