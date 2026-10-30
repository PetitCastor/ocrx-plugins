using System.Text.Json;
using System.Text.Json.Nodes;
using ItemTrackerPlugin.App;
using ItemTrackerPlugin.Model;
using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Resolve;
using ItemTrackerPlugin.ViewData;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.Tests;

public sealed class ViewDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Stored = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    private static ItemHolding Held(InventoryKind kind, string key, string label, string itemClass = "Drink_a", int quantity = 1) =>
        new(null, itemClass, quantity, [new HoldingLink(kind, key, label)], Stored, "Store", 1.0, false, []);

    private static TrackerState.Snapshot Snapshot(
        IReadOnlyList<(ItemHolding Held, PlacementState State)> items,
        IReadOnlyList<UnknownArrival>? arrivals = null,
        HoldingResolver? resolver = null,
        IReadOnlyList<StationCoverage>? coverage = null,
        IngestHealth? health = null) =>
        new(
            items.Select(i => new NamedHolding(i.Held, new ResolvedName(i.Held.ItemClass, i.Held.ItemClass.Replace('_', ' '), NameMatch.None, null))).ToList(),
            resolver,
            Now,
            health ?? IngestHealth.Empty,
            new Reconciliation(
                items.Select(i => new ReconciledHolding(i.Held, i.State, $"because {i.State}", i.State == PlacementState.Verified ? Now : null)).ToList(),
                arrivals ?? [],
                coverage ?? [new StationCoverage("123", Now, 2, 3, 9, null)],
                Unplaced: 0));

    [Fact]
    public void Stations_ships_and_drops_each_get_their_rows_with_state_and_reason()
    {
        var doc = ViewDocument.Build(Snapshot(
        [
            (Held(InventoryKind.Location, "123", "Keeger", "Drink_a", 2), PlacementState.Verified),
            (Held(InventoryKind.Location, "123", "Keeger", "Gun_b"), PlacementState.Conflict),
            (Held(InventoryKind.Ship, "GLSN_Shiv", "Shiv", "Gun_c"), PlacementState.Deduced),
            (Held(InventoryKind.World, "123", "dropped at Keeger", "Gun_d"), PlacementState.Stale),
            (Held(InventoryKind.Equipped, "", "worn", "Gun_e"), PlacementState.Deduced),
        ]));

        var system = Assert.Single(doc["systems"]!.AsArray());
        var station = Assert.Single(system!["stations"]!.AsArray());
        Assert.Equal(["Drink a", "Gun b"], station!["items"]!.AsArray().Select(i => i!["name"]!.GetValue<string>()));
        var drink = station["items"]![0]!;
        Assert.Equal("verified", drink["state"]!.GetValue<string>());
        Assert.Equal(2, drink["quantity"]!.GetValue<int>());
        Assert.Equal("because Verified", drink["reason"]!.GetValue<string>());
        Assert.Equal("2026-10-29T12:00:00Z", drink["seenAt"]!.GetValue<string>());
        Assert.Null(station["items"]![1]!["seenAt"]);

        Assert.Equal("Gun c", doc["ships"]![0]!["items"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("dropped at Keeger", doc["dropped"]![0]!["place"]!.GetValue<string>());
        Assert.Equal(1, doc["counts"]!["verified"]!.GetValue<int>());
        Assert.Equal(1, doc["counts"]!["conflict"]!.GetValue<int>());
        Assert.DoesNotContain("Gun e", doc.ToJsonString());
    }

    [Fact]
    public void A_station_with_no_name_shows_its_id_and_sits_under_an_unknown_system()
    {
        var doc = ViewDocument.Build(Snapshot([(Held(InventoryKind.Location, "3058615591", "?"), PlacementState.Deduced)]));

        var system = Assert.Single(doc["systems"]!.AsArray());
        Assert.Equal(Place.UnknownSystem, system!["name"]!.GetValue<string>());
        var station = system["stations"]![0]!;
        Assert.Equal("location 3058615591", station["name"]!.GetValue<string>());
        Assert.False(station["resolved"]!.GetValue<bool>());
    }

    [Fact]
    public void What_the_screen_showed_with_no_ledger_side_is_listed_at_its_station()
    {
        var arrival = new UnknownArrival("777", "CRUZ Dark", null, 2, Now, 1, PlacementState.Verified, "Seen at 777 kiosk");
        var doc = ViewDocument.Build(Snapshot([(Held(InventoryKind.Location, "123", "Keeger"), PlacementState.Deduced)], [arrival]));

        var stations = doc["systems"]![0]!["stations"]!.AsArray();
        Assert.Equal(["location 123", "location 777"], stations.Select(s => s!["name"]!.GetValue<string>()));
        var seen = stations[1]!["items"]![0]!;
        Assert.Equal("CRUZ Dark", seen["name"]!.GetValue<string>());
        Assert.Equal("verified", seen["state"]!.GetValue<string>());
        Assert.Equal("Seen on screen", seen["lastMoveType"]!.GetValue<string>());
        Assert.Equal(1, doc["counts"]!["verified"]!.GetValue<int>());
    }

    [Fact]
    public void A_station_the_game_gave_two_ids_is_one_place_with_both_ids_items_and_counts()
    {
        using var store = new ScratchStore();
        store.Name("111", "Nyx_Kaboos", evidence: 5);
        store.Name("222", "Nyx_Kaboos", evidence: 1);
        store.Vote("111", "system", "Nyx");
        var resolver = HoldingResolver.Load(store.Db);

        var doc = ViewDocument.Build(Snapshot(
            [
                (Held(InventoryKind.Location, "111", "Kaboos", "Drink_a"), PlacementState.Verified),
                (Held(InventoryKind.Location, "222", "Kaboos", "Gun_b", 3), PlacementState.Deduced),
            ],
            resolver: resolver,
            coverage: [new StationCoverage("222", Now, 1, 1, 4, 4)]));

        var system = Assert.Single(doc["systems"]!.AsArray());
        Assert.Equal("Nyx", system!["name"]!.GetValue<string>());
        var station = Assert.Single(system["stations"]!.AsArray())!;
        Assert.Equal("111", station["id"]!.GetValue<string>());
        Assert.Equal(["111", "222"], station["aliasIds"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.Equal(["Drink a", "Gun b"], station["items"]!.AsArray().Select(i => i!["name"]!.GetValue<string>()));
        Assert.Equal(1, station["counts"]!["verified"]!.GetValue<int>());
        Assert.Equal(3, station["counts"]!["deduced"]!.GetValue<int>());
        Assert.True(station["coverage"]!["complete"]!.GetValue<bool>());
        Assert.DoesNotContain("(Nyx)", station["name"]!.GetValue<string>());
    }

    [Fact]
    public void Where_the_player_stands_is_given_as_the_place_its_id_belongs_to()
    {
        using var store = new ScratchStore();
        store.Name("111", "Nyx_Kaboos", evidence: 5);
        store.Name("222", "Nyx_Kaboos", evidence: 1);
        var resolver = HoldingResolver.Load(store.Db);
        var health = IngestHealth.Empty with { CurrentLocationId = "222" };

        var doc = ViewDocument.Build(Snapshot([(Held(InventoryKind.Location, "111", "Kaboos"), PlacementState.Deduced)], resolver: resolver, health: health));

        Assert.Equal("111", doc["here"]!.GetValue<string>());
        Assert.Null(ViewDocument.Build(Snapshot([]))["here"]);
    }

    [Fact]
    public void A_station_is_dated_by_the_newest_move_sighting_or_visit_there()
    {
        var doc = ViewDocument.Build(Snapshot([(Held(InventoryKind.Location, "123", "Keeger"), PlacementState.Deduced)]));
        Assert.Equal("2026-10-29T12:00:00Z", doc["systems"]![0]!["stations"]![0]!["lastActivity"]!.GetValue<string>());

        var unvisited = ViewDocument.Build(Snapshot([(Held(InventoryKind.Location, "456", "Elsewhere"), PlacementState.Deduced)]));
        Assert.Equal("2026-10-21T18:00:00Z", unvisited["systems"]![0]!["stations"]![0]!["lastActivity"]!.GetValue<string>());
    }

    [Fact]
    public void A_visit_shows_how_much_of_the_station_it_covered()
    {
        var doc = ViewDocument.Build(Snapshot([(Held(InventoryKind.Location, "123", "Keeger"), PlacementState.Deduced)]));

        var coverage = doc["systems"]![0]!["stations"]![0]!["coverage"]!;
        Assert.Equal(2, coverage["pagesSeen"]!.GetValue<int>());
        Assert.Equal(3, coverage["pages"]!.GetValue<int>());
        Assert.False(coverage["complete"]!.GetValue<bool>());
    }

    [Fact]
    public void No_snapshot_yet_is_an_empty_document_not_an_error()
    {
        var doc = ViewDocument.Build(TrackerState.Snapshot.Empty);

        Assert.Empty(doc["systems"]!.AsArray());
        Assert.Empty(doc["ships"]!.AsArray());
        Assert.Empty(doc["dropped"]!.AsArray());
        Assert.Equal(0, doc["counts"]!["verified"]!.GetValue<int>());
    }

    [Fact]
    public void The_fixture_the_page_is_tested_with_has_the_keys_the_plugin_writes()
    {
        var built = ViewDocument.Build(Snapshot(
        [
            (Held(InventoryKind.Location, "123", "Keeger"), PlacementState.Verified),
            (Held(InventoryKind.Ship, "GLSN_Shiv", "Shiv"), PlacementState.Deduced),
            (Held(InventoryKind.World, "123", "dropped at Keeger"), PlacementState.Stale),
        ]));
        var fixture = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();

        Assert.Equal(Keys(built), Keys(fixture));
        Assert.Equal(Keys(built["systems"]![0]!), Keys(fixture["systems"]![0]!));
        Assert.Equal(Keys(built["systems"]![0]!["stations"]![0]!), Keys(fixture["systems"]![0]!["stations"]![0]!));
        Assert.Equal(Keys(built["systems"]![0]!["stations"]![0]!["coverage"]!), Keys(fixture["systems"]![0]!["stations"]![0]!["coverage"]!));
        Assert.Equal(Keys(built["systems"]![0]!["stations"]![0]!["items"]![0]!), Keys(fixture["systems"]![0]!["stations"]![0]!["items"]![0]!));
        Assert.Equal(Keys(built["ships"]![0]!), Keys(fixture["ships"]![0]!));
        Assert.Equal(Keys(built["dropped"]![0]!), Keys(fixture["dropped"]![0]!));
        Assert.Equal(Keys(built["counts"]!), Keys(fixture["counts"]!));
    }

    [Fact]
    public void The_shipped_view_sits_beside_the_exe_and_loads_nothing_from_outside()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "view");

        Assert.True(File.Exists(Path.Combine(dir, "index.html")));
        foreach (var file in Directory.EnumerateFiles(dir))
            Assert.DoesNotMatch(@"(?i)(https?:)?//[a-z0-9.-]+\.[a-z]{2,}", File.ReadAllText(file));
    }

    [Fact]
    public void The_page_needs_no_inline_script_its_policy_would_block()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "view", "index.html"));

        Assert.Contains("default-src 'self'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onclick=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("<script type=\"module\" src=\"main.js\"></script>", html);
        Assert.DoesNotMatch(@"<script(?![^>]*\bsrc=)", html);
    }

    private static string[] Keys(JsonNode node) => node.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();

    private static string FixturePath([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "ItemTrackerView", "fixtures", "view-data.json"));
}
