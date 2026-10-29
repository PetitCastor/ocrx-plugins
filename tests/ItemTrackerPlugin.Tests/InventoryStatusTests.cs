using ItemTrackerPlugin.Overlay;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Screen;

namespace ItemTrackerPlugin.Tests;

public sealed class InventoryStatusTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ScreenHeader Kiosk = new(ScreenContext.Kiosk, null);

    private static StationStatus Keeger(StateTally tally, params PageStatus[] pages) => new("1", "Keeger", tally, pages);

    private static string? Line(InventoryStatus status, ScreenHeader header, PageCounter? page, StationStatus? station, int seconds = 0) =>
        status.Next(header, page, T0.AddSeconds(seconds), () => station, out var line) ? line ?? "<clear>" : null;

    [Fact]
    public void A_kiosk_reads_station_page_verified_and_conflicts()
    {
        var line = Line(new InventoryStatus(), Kiosk, new PageCounter(2, 5), Keeger(new StateTally(14, 3, 0, 1)));

        Assert.Equal("Keeger kiosk · p.2/5 · 14 ✓ · 1 ⚠", line);
    }

    [Fact]
    public void Stale_shows_only_when_there_is_some()
    {
        var line = Line(new InventoryStatus(), Kiosk, new PageCounter(1, 1), Keeger(new StateTally(4, 0, 2, 0)));

        Assert.Equal("Keeger kiosk · p.1/1 · 4 ✓ · 2 stale", line);
    }

    [Fact]
    public void A_kiosk_with_no_known_station_still_says_it_is_up()
    {
        Assert.Equal("Kiosk · p.3/7", Line(new InventoryStatus(), Kiosk, new PageCounter(3, 7), station: null));
        Assert.Equal("Kiosk", Line(new InventoryStatus(), Kiosk, page: null, station: null));
    }

    [Fact]
    public void A_ship_locker_names_the_model_and_the_page()
    {
        var line = Line(new InventoryStatus(), new ScreenHeader(ScreenContext.ShipStorage, "SHIV"), new PageCounter(1, 2), station: null);

        Assert.Equal("SHIV locker · p.1/2", line);
    }

    [Fact]
    public void An_unchanged_line_is_not_emitted_again()
    {
        var status = new InventoryStatus();
        var st = Keeger(new StateTally(1, 0, 0, 0));

        Assert.NotNull(Line(status, Kiosk, new PageCounter(1, 2), st));
        Assert.Null(Line(status, Kiosk, new PageCounter(1, 2), st));
        Assert.Equal("Keeger kiosk · p.2/2 · 1 ✓", Line(status, Kiosk, new PageCounter(2, 2), st));
    }

    [Fact]
    public void Nothing_is_shown_outside_an_inventory_context()
    {
        var status = new InventoryStatus();

        Assert.Null(Line(status, ScreenHeader.Unknown, null, null));
        Assert.Null(Line(status, new ScreenHeader(ScreenContext.Personal, null), null, null));
    }

    [Fact]
    public void Leaving_clears_after_three_ticks_and_one_misread_does_not()
    {
        var status = new InventoryStatus();
        Line(status, Kiosk, new PageCounter(1, 1), station: null);

        Assert.Null(Line(status, ScreenHeader.Unknown, null, null));
        Assert.Null(Line(status, Kiosk, new PageCounter(1, 1), station: null));
        Assert.Null(Line(status, ScreenHeader.Unknown, null, null));
        Assert.Null(Line(status, ScreenHeader.Unknown, null, null));
        Assert.Equal("<clear>", Line(status, ScreenHeader.Unknown, null, null));
        Assert.Null(Line(status, ScreenHeader.Unknown, null, null));
    }

    [Fact]
    public void A_page_that_turns_verified_gets_a_short_line_for_a_moment()
    {
        var status = new InventoryStatus();
        var page = new PageCounter(2, 5);
        var before = Keeger(new StateTally(3, 0, 0, 0), new PageStatus("1", 2, 3, 2));
        var after = Keeger(new StateTally(4, 0, 0, 0), new PageStatus("1", 2, 3, 3));

        Assert.Equal("Keeger kiosk · p.2/5 · 3 ✓", Line(status, Kiosk, page, before));
        Assert.Equal("Keeger kiosk · page 2 verified ✓", Line(status, Kiosk, page, after, seconds: 1));
        Assert.Null(Line(status, Kiosk, page, after, seconds: 2));
        Assert.Equal("Keeger kiosk · p.2/5 · 4 ✓", Line(status, Kiosk, page, after, seconds: 6));
    }

    [Fact]
    public void A_verified_page_is_announced_once_per_visit()
    {
        var status = new InventoryStatus();
        var page = new PageCounter(2, 5);
        var verified = Keeger(new StateTally(4, 0, 0, 0), new PageStatus("1", 2, 3, 3));

        Assert.Contains("verified", Line(status, Kiosk, page, verified));
        Line(status, Kiosk, page, verified, seconds: 10);
        Assert.Null(Line(status, Kiosk, page, verified, seconds: 11));

        for (var i = 0; i < 3; i++) Line(status, ScreenHeader.Unknown, null, null, seconds: 12);
        Assert.Contains("verified", Line(status, Kiosk, page, verified, seconds: 20));
    }

    [Fact]
    public void The_notice_never_names_a_page_other_than_the_one_that_earned_it()
    {
        var status = new InventoryStatus();
        var verified = Keeger(new StateTally(4, 0, 0, 0), new PageStatus("1", 2, 3, 3));

        Assert.Equal("Keeger kiosk · page 2 verified ✓", Line(status, Kiosk, new PageCounter(2, 5), verified));
        Assert.Equal("Keeger kiosk · p.3/5 · 4 ✓", Line(status, Kiosk, new PageCounter(3, 5), verified, seconds: 1));
    }

    [Fact]
    public void One_misread_header_does_not_let_a_page_announce_itself_again()
    {
        var status = new InventoryStatus();
        var page = new PageCounter(2, 5);
        var verified = Keeger(new StateTally(4, 0, 0, 0), new PageStatus("1", 2, 3, 3));

        Line(status, Kiosk, page, verified);
        Line(status, Kiosk, page, verified, seconds: 10);
        Line(status, ScreenHeader.Unknown, null, null, seconds: 11);

        Assert.Null(Line(status, Kiosk, page, verified, seconds: 12));
    }
}
