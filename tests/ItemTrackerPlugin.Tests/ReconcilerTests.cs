using ItemTrackerPlugin.Model;
using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Resolve;
using ItemTrackerPlugin.Screen;

namespace ItemTrackerPlugin.Tests;

public sealed class ReconcilerTests
{
    private const string Keeger = "3058615591";
    private const string Elsewhere = "2204875565";
    private const string Badger = "KLWE_LaserRepeater_S2";
    private const string Cruz = "Drink_bottle_cruz_01_dark_a";

    private static readonly DateTimeOffset Opened = new(2026, 10, 28, 19, 25, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Seen = new(2026, 10, 28, 19, 28, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Stored = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    /// <summary>The capturing machine's zone, so reasons read in the time the player saw.</summary>
    private static readonly TimeZoneInfo Edt = TimeZoneInfo.CreateCustomTimeZone("EDT", TimeSpan.FromHours(-4), "EDT", "EDT");

    private static readonly ItemNamer Namer = new(GameLocalization.Parse(
    [
        "item_NameKLWE_LaserRepeater_S2=CF-227 Badger Repeater",
        "item_NameDrink_bottle_cruz_01_dark_a=CRUZ Dark",
        "item_NameBEHR_LaserCannon_S2=M4A Cannon",
    ]));

    private static ItemHolding At(string station, string itemClass, int quantity = 1, DateTimeOffset? since = null, string? geid = null) =>
        new(geid, itemClass, quantity, [new HoldingLink(InventoryKind.Location, station, "Keeger")],
            since ?? Stored, "Store", 1.0, geid is null, []);

    private static StoredObservation Kiosk(
        string name, int? quantity = null, int? page = 1, int? row = 0, int? column = 0, DateTimeOffset? at = null, int pages = 3) =>
        new(at ?? Seen, ScreenContext.Kiosk, null, name, null, quantity, page, pages, row, column, "tooltip-right", "{}");

    private static MoveRecord Move(string itemClass, DateTimeOffset at, string from, InventoryKind toKind, string to, int amount = 1) =>
        new(1, at, "Move", itemClass, null, amount, InventoryKind.Location, from, toKind, to, to, "succeed");

    private static Reconciliation Run(
        IReadOnlyList<ItemHolding> holdings,
        IReadOnlyList<StoredObservation> observations,
        IReadOnlyList<MoveRecord>? moves = null,
        IReadOnlyList<HoldingResolver.GameUpdate>? updates = null,
        IReadOnlyDictionary<string, int>? freight = null,
        IReadOnlyList<StationVisit>? visits = null) =>
        new Reconciler(Namer, Edt).Run(new ReconcileInput(
            holdings, moves ?? [], updates ?? [], observations, visits ?? [new StationVisit(Keeger, Opened)],
            freight ?? new Dictionary<string, int>(), id => id == Keeger ? "Keeger" : $"location {id}"));

    [Fact]
    public void Seen_in_the_same_number_is_verified_with_where_and_when()
    {
        var result = Run([At(Keeger, Badger)], [Kiosk("CF-227 Badger Repeater", page: 2)]);

        var row = Assert.Single(result.Holdings);
        Assert.Equal(PlacementState.Verified, row.State);
        Assert.Equal("Seen at Keeger kiosk, 28 Oct 15:28, page 2", row.Reason);
    }

    [Fact]
    public void Never_seen_is_deduced_from_the_logs()
    {
        var row = Assert.Single(Run([At(Keeger, Badger)], []).Holdings);

        Assert.Equal(PlacementState.Deduced, row.State);
        Assert.Equal("Stored 21 Oct, not seen on screen since", row.Reason);
    }

    [Fact]
    public void A_different_count_is_a_conflict_on_the_kind_not_a_guessed_geid()
    {
        var result = Run([At(Keeger, Cruz, geid: "1"), At(Keeger, Cruz, geid: "2")], [Kiosk("CRUZ Dark", quantity: 3)]);

        Assert.All(result.Holdings, h => Assert.Equal(PlacementState.Conflict, h.State));
        Assert.All(result.Holdings, h => Assert.Equal("Ledger says 2, kiosk showed 3", h.Reason));
        Assert.Empty(result.Arrivals);
    }

    [Fact]
    public void Fewer_seen_on_a_partial_view_proves_nothing()
    {
        // Another tile of the same drink may sit on a page or a cell nobody hovered.
        var row = Assert.Single(Run([At(Keeger, Cruz, quantity: 5)], [Kiosk("CRUZ Dark", quantity: 2)]).Holdings);

        Assert.Equal(PlacementState.Deduced, row.State);
    }

    [Fact]
    public void Fewer_seen_on_a_view_of_the_whole_station_is_a_conflict()
    {
        var row = Assert.Single(Run(
            [At(Keeger, Cruz, quantity: 5)],
            [Kiosk("CRUZ Dark", quantity: 2, pages: 1)],
            freight: new Dictionary<string, int> { [Keeger] = 2 }).Holdings);

        Assert.Equal(PlacementState.Conflict, row.State);
        Assert.Equal("Ledger says 5, kiosk showed 2", row.Reason);
    }

    [Fact]
    public void A_move_during_the_visit_resorts_the_grid_and_drops_the_tiles_seen_before_it()
    {
        // Two drinks on (0,0) before one left; after it the other shows on (1,1). Summed, that is three.
        var result = Run(
            [At(Keeger, Cruz)],
            [
                Kiosk("CRUZ Dark", quantity: 2, row: 0, column: 0, at: Seen.AddMinutes(-2)),
                Kiosk("CRUZ Dark", quantity: null, row: 1, column: 1),
            ],
            [Move(Cruz, Seen.AddMinutes(-1), Keeger, InventoryKind.Container, "680760615382")]);

        Assert.Equal(PlacementState.Verified, Assert.Single(result.Holdings).State);
    }

    [Fact]
    public void An_arrival_after_the_frame_is_not_counted_as_there_at_the_frame()
    {
        // One drink was there and seen; a second was stored after the frame, on two log lines.
        var later = Seen.AddMinutes(5);
        var stored = new MoveRecord(2, later, "Store", Cruz, "22", 1, InventoryKind.Equipped, "", InventoryKind.Location, Keeger, Keeger, "succeed");
        var result = Run(
            [At(Keeger, Cruz, geid: "11"), At(Keeger, Cruz, since: later, geid: "22")],
            [Kiosk("CRUZ Dark")],
            [stored, stored with { Id = 3, MoveType = "StoreItem" }]);

        Assert.Equal(PlacementState.Verified, result.Holdings.Single(h => h.Holding.Geid == "11").State);
        Assert.Equal(PlacementState.Deduced, result.Holdings.Single(h => h.Holding.Geid == "22").State);
    }

    [Fact]
    public void An_open_the_log_could_not_tie_to_an_id_ends_the_visit_before_it()
    {
        var result = Run(
            [At(Keeger, Badger)],
            [Kiosk("CF-227 Badger Repeater")],
            visits: [new StationVisit(Keeger, Opened.AddMinutes(-30)), new StationVisit(null, Opened)]);

        Assert.Equal(1, result.Unplaced);
        Assert.Equal(PlacementState.Deduced, Assert.Single(result.Holdings).State);
    }

    [Fact]
    public void An_unread_badge_short_of_the_ledger_proves_nothing()
    {
        var row = Assert.Single(Run([At(Keeger, Cruz, quantity: 2)], [Kiosk("CRUZ Dark", quantity: null)]).Holdings);

        Assert.Equal(PlacementState.Deduced, row.State);
    }

    [Fact]
    public void Two_tiles_of_one_kind_add_up_and_a_reread_tile_counts_once()
    {
        var result = Run(
            [At(Keeger, Badger, quantity: 3)],
            [
                Kiosk("CF-227 Badger Repeater", quantity: 2, row: 0, column: 0),
                Kiosk("CF-227 Badger Repeater", quantity: 2, row: 0, column: 0, at: Seen.AddSeconds(5)),
                Kiosk("CF-227 Badger Repeater", quantity: null, row: 1, column: 2, at: Seen.AddSeconds(9)),
            ]);

        Assert.Equal(PlacementState.Verified, Assert.Single(result.Holdings).State);
    }

    [Fact]
    public void Verified_then_moved_away_is_deduced_at_its_new_place()
    {
        var movedAt = Seen.AddMinutes(10);
        var moved = new ItemHolding(
            "9", Badger, 1, [new HoldingLink(InventoryKind.Location, Elsewhere, "Elsewhere")], movedAt, "Move", 1.0, false, []);

        var row = Assert.Single(Run([moved], [Kiosk("CF-227 Badger Repeater")], [Move(Badger, movedAt, Keeger, InventoryKind.Location, Elsewhere)]).Holdings);

        Assert.Equal(PlacementState.Deduced, row.State);
        Assert.Equal("Moved here 28 Oct, not seen on screen since", row.Reason);
    }

    [Fact]
    public void One_of_two_leaving_after_the_frame_leaves_the_other_verified()
    {
        // The frame showed two; one left afterwards. At the time of the frame the ledger had two.
        var result = Run(
            [At(Keeger, Cruz)],
            [Kiosk("CRUZ Dark", quantity: 2)],
            [Move(Cruz, Seen.AddMinutes(1), Keeger, InventoryKind.Container, "680760615382")]);

        Assert.Equal(PlacementState.Verified, Assert.Single(result.Holdings).State);
    }

    [Fact]
    public void A_build_change_after_the_last_sighting_makes_it_stale()
    {
        var result = Run(
            [At(Keeger, Badger)],
            [Kiosk("CF-227 Badger Repeater")],
            updates: [new HoldingResolver.GameUpdate(Seen.AddDays(1), 12519617)]);

        var row = Assert.Single(result.Holdings);
        Assert.Equal(PlacementState.Stale, row.State);
        Assert.Equal("Game updated to build 12519617 since", row.Reason);
    }

    [Fact]
    public void A_build_change_before_the_sighting_does_not()
    {
        var result = Run(
            [At(Keeger, Badger)],
            [Kiosk("CF-227 Badger Repeater")],
            updates: [new HoldingResolver.GameUpdate(Seen.AddDays(-1), 12519617)]);

        Assert.Equal(PlacementState.Verified, Assert.Single(result.Holdings).State);
    }

    [Fact]
    public void A_build_change_after_a_deduced_move_makes_it_stale()
    {
        var row = Assert.Single(Run([At(Keeger, Badger)], [], updates: [new HoldingResolver.GameUpdate(Stored.AddDays(2), 12572603)]).Holdings);

        Assert.Equal(PlacementState.Stale, row.State);
    }

    [Fact]
    public void A_filtered_view_leaves_the_other_kinds_untouched()
    {
        // One page of three seen, and no freight total: a partial view verifies what it showed only.
        var result = Run([At(Keeger, Badger), At(Keeger, Cruz)], [Kiosk("CF-227 Badger Repeater")]);

        Assert.Equal(PlacementState.Verified, result.Holdings.Single(h => h.Holding.ItemClass == Badger).State);
        Assert.Equal(PlacementState.Deduced, result.Holdings.Single(h => h.Holding.ItemClass == Cruz).State);
        Assert.False(Assert.Single(result.Coverage).Complete);
    }

    [Fact]
    public void A_complete_view_turns_what_it_did_not_show_into_a_conflict()
    {
        // Every page of a one-page list and a count equal to the freight total: the whole station.
        var result = Run(
            [At(Keeger, Badger), At(Keeger, Cruz)],
            [Kiosk("CF-227 Badger Repeater", pages: 1)],
            freight: new Dictionary<string, int> { [Keeger] = 1 });

        Assert.True(Assert.Single(result.Coverage).Complete);
        var cruz = result.Holdings.Single(h => h.Holding.ItemClass == Cruz);
        Assert.Equal(PlacementState.Conflict, cruz.State);
        Assert.Equal("Ledger says 1, kiosk showed 0", cruz.Reason);
    }

    [Fact]
    public void A_count_short_of_the_freight_total_is_not_the_whole_station()
    {
        var result = Run(
            [At(Keeger, Badger), At(Keeger, Cruz)],
            [Kiosk("CF-227 Badger Repeater", pages: 1)],
            freight: new Dictionary<string, int> { [Keeger] = 40 });

        Assert.False(Assert.Single(result.Coverage).Complete);
        Assert.Equal(PlacementState.Deduced, result.Holdings.Single(h => h.Holding.ItemClass == Cruz).State);
    }

    [Fact]
    public void A_kind_the_ledger_never_placed_there_is_an_arrival_of_unknown_origin()
    {
        var result = Run([], [Kiosk("M4A Cannon", quantity: 2, page: 3)]);

        var arrival = Assert.Single(result.Arrivals);
        Assert.Equal(Keeger, arrival.LocationId);
        Assert.Equal("M4A Cannon", arrival.Name);
        Assert.Equal(2, arrival.Quantity);
        Assert.Equal(PlacementState.Verified, arrival.State);
        Assert.Equal("Seen at Keeger kiosk, 28 Oct 15:28, page 3, origin unknown", arrival.Reason);
        Assert.Equal(1, result.Count(PlacementState.Verified));
    }

    [Fact]
    public void A_kind_that_left_after_the_frame_was_there_and_is_no_arrival()
    {
        var result = Run([], [Kiosk("M4A Cannon")], [Move("BEHR_LaserCannon_S2", Seen.AddMinutes(1), Keeger, InventoryKind.Container, "1")]);

        Assert.Empty(result.Arrivals);
    }

    [Fact]
    public void A_kiosk_frame_before_any_station_open_is_unplaced()
    {
        var result = Run([At(Keeger, Badger)], [Kiosk("CF-227 Badger Repeater", at: Opened.AddMinutes(-1))]);

        Assert.Equal(1, result.Unplaced);
        Assert.Equal(PlacementState.Deduced, Assert.Single(result.Holdings).State);
    }

    [Fact]
    public void The_frame_belongs_to_the_last_station_opened_before_it()
    {
        var result = Run(
            [At(Keeger, Badger)],
            [Kiosk("CF-227 Badger Repeater")],
            visits: [new StationVisit(Keeger, Opened.AddMinutes(-30)), new StationVisit(Elsewhere, Opened)]);

        Assert.Equal(PlacementState.Deduced, Assert.Single(result.Holdings).State);
        Assert.Equal(Elsewhere, Assert.Single(result.Arrivals).LocationId);
    }

    [Fact]
    public void The_personal_inventory_and_ship_lockers_are_not_reconciled()
    {
        var personal = Kiosk("CF-227 Badger Repeater") with { Panel = ScreenContext.Personal };
        var ship = Kiosk("CF-227 Badger Repeater") with { Panel = ScreenContext.ShipStorage, ShipModel = "SHIV" };

        var result = Run([At(Keeger, Badger)], [personal, ship]);

        Assert.Equal(PlacementState.Deduced, Assert.Single(result.Holdings).State);
        Assert.Empty(result.Arrivals);
        Assert.Equal(0, result.Unplaced);
    }

    [Fact]
    public void An_item_inside_a_container_at_the_station_is_not_on_the_grid()
    {
        var inBox = new ItemHolding(
            "7", Badger, 1,
            [new HoldingLink(InventoryKind.Container, "680760615382", "box"), new HoldingLink(InventoryKind.Location, Keeger, "Keeger")],
            Stored, "Move", 1.0, false, []);

        var result = Run([inBox], [Kiosk("CF-227 Badger Repeater")]);

        Assert.Equal(PlacementState.Deduced, Assert.Single(result.Holdings).State);
        Assert.Single(result.Arrivals);
    }
}
