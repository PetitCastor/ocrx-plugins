using ItemTrackerPlugin.Ingest;
using ItemTrackerPlugin.Model;

namespace ItemTrackerPlugin.Tests;

/// <summary>
/// The log lines that move nothing but say what the player was doing. Lines are real
/// 2026-10-28 and August 2026 records, trimmed of the player name only.
/// </summary>
public class LogGapParserTests
{
    private static InventoryEvent? Event(string raw)
    {
        Assert.True(LogLine.TryParse(raw, out var line), $"line did not match the log shape: {raw}");
        return InventoryEventParser.Parse(line);
    }

    private static string FreightPage(string next, string end, string inv = "204821708183:Location:3058615591") =>
        "<2026-10-28T19:29:16.564Z> [Notice] <InventoryManagement> Freight Inventory Grid " +
        $"InventoryDataRequest Successful Inventory[{inv}] Elapsed[0.394277]. " +
        $"Page info: HasPreviousPage[0], HasNextPage[{next}], StartCursor[MA==], EndCursor[{end}]. " +
        "[Team_CoreGameplayFeatures][Inventory][InventoryContainer]";

    private static string Complete(int request, string type, string result) =>
        $"<2026-08-04T11:54:30.473Z> [Notice] <Player Inventory Request Complete> Request[{request}] " +
        $"for 'Pilot' [2001] Type[{type}] Result[{result}] Item[0] CanLockQueue[Yes]. " +
        "[Team_CoreGameplayFeatures][Inventory]";

    [Fact]
    public void Reads_a_freight_page_that_has_a_next_page()
    {
        var page = Assert.IsType<FreightPageSeen>(Event(FreightPage("1", "MjUw")));

        Assert.True(page.HasNextPage);
        Assert.Equal(250, page.EndCursor);
        Assert.Equal(InventoryKind.Location, page.Inventory.Kind);
        Assert.Equal("3058615591", page.Inventory.SubId);
    }

    [Fact]
    public void Reads_the_last_page_of_a_freight_list()
    {
        var page = Assert.IsType<FreightPageSeen>(Event(FreightPage("0", "MTI=")));

        Assert.False(page.HasNextPage);
        Assert.Equal(12, page.EndCursor);
    }

    [Fact]
    public void Keeps_a_freight_page_whose_cursor_does_not_decode()
    {
        var page = Assert.IsType<FreightPageSeen>(Event(FreightPage("0", "bm90YW51bWJlcg==")));

        Assert.Null(page.EndCursor);
    }

    [Fact]
    public void Ignores_the_page_size_line_that_follows_a_freight_page()
    {
        Assert.Null(Event(
            "<2026-10-28T19:29:16.564Z> [Notice] <InventoryManagement> Freight Inventory Grid " +
            "InventoryDataRequest Successful for Player[Pilot] Inventory[204821708183:Location:3058615591] " +
            "Inventory Result Item Count:[250]. [Team_CoreGameplayFeatures][Inventory][InventoryContainer]"));
    }

    [Fact]
    public void Ignores_a_freight_page_of_something_that_is_not_a_station()
    {
        Assert.Null(Event(FreightPage("0", "MTI=", inv: "681562156430:Container:0")));
    }

    [Fact]
    public void Reads_a_stack_press_instead_of_a_move_completion()
    {
        var press = Assert.IsType<StackAllPressed>(Event(Complete(10, "StackAll", "Succeed")));

        Assert.Equal(10, press.RequestNo);
        Assert.True(press.Succeeded);
    }

    [Fact]
    public void A_failed_stack_press_is_still_a_press_but_did_not_succeed()
    {
        Assert.False(Assert.IsType<StackAllPressed>(Event(Complete(11, "StackAll", "Failed"))).Succeeded);
    }

    [Fact]
    public void Other_completions_are_still_move_completions()
    {
        var done = Assert.IsType<MoveCompleted>(Event(Complete(12, "Move", "Succeed")));

        Assert.Equal(12, done.RequestNo);
    }

    [Fact]
    public void Reads_the_close_of_a_station_inventory()
    {
        var ev = Event(
            "<2026-10-28T19:28:57.349Z> [Notice] <Request Terminate Access To Inventory> Player[Pilot] " +
            "terminating access to [204821708183:Location:3058615591] [Team_CoreGameplayFeatures][Inventory]");

        Assert.Equal("3058615591", Assert.IsType<InventoryClosed>(ev).LocationId);
    }

    [Fact]
    public void Ignores_the_close_of_a_container()
    {
        Assert.Null(Event(
            "<2026-10-28T19:28:57.349Z> [Notice] <Request Terminate Access To Inventory> Player[Pilot] " +
            "terminating access to [680760615382:Container:0] [Team_CoreGameplayFeatures][Inventory]"));
    }

    [Fact]
    public void Reads_a_kiosk_buy_request()
    {
        var ev = Event(
            "<2026-08-09T12:01:52.875Z> [Notice] <CEntityComponentShopUIProvider::SendShopBuyRequest> " +
            "Sending SShopBuyRequest - playerId[204821708183] shopId[747184179940] shopName[SCShop_Cargo_Office] " +
            "kioskId[747184179935] client_price[19175.000000] itemClassGUID[27adea05-f94d-4439-872d-b043a631c34f] " +
            "itemName[grin_tractor_01] quantity[1]  [Team_CoreGameplayFeatures][Shops][UI]");

        var buy = Assert.IsType<ShopBuyRequested>(ev);
        Assert.Equal("SCShop_Cargo_Office", buy.ShopName);
        Assert.Equal("grin_tractor_01", buy.ItemName);
        Assert.Equal(1, buy.Quantity);
        Assert.Equal(19175.0, buy.Price);
    }

    [Fact]
    public void A_kiosk_quantity_too_large_to_read_is_dropped_not_thrown()
    {
        Assert.Null(Event(
            "<2026-08-09T12:01:52.875Z> [Notice] <CEntityComponentShopUIProvider::SendShopBuyRequest> " +
            "Sending SShopBuyRequest - playerId[1] shopId[2] shopName[SCShop_Cargo_Office] kioskId[3] " +
            "client_price[1.0] itemClassGUID[x] itemName[grin_tractor_01] quantity[99999999999]  [Team_CoreGameplayFeatures][Shops][UI]"));
    }

    [Fact]
    public void Reads_the_answer_to_a_kiosk_request()
    {
        var ev = Event(
            "<2026-08-09T12:01:53.141Z> [Notice] <CEntityComponentShopUIProvider::RmShopFlowResponse> " +
            "Received ShopFlowResponse - playerId[204821708183] shopId[747184179940] shopName[SCShop_Cargo_Office] " +
            "kioskId[747184179935] kioskState[BuyRequestProcessing] result[Success] type[Buying] " +
            "[Team_CoreGameplayFeatures][Shops][UI]");

        var flow = Assert.IsType<ShopFlowResolved>(ev);
        Assert.True(flow.Succeeded);
        Assert.Equal("Buying", flow.Type);
        Assert.Equal("BuyRequestProcessing", flow.KioskState);
    }
}
