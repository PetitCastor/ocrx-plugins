using System.Text.Json;
using System.Text.Json.Nodes;
using ItemTrackerPlugin.App;
using ItemTrackerPlugin.Model;
using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Resolve;
using ItemTrackerPlugin.ViewData;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;

namespace ItemTrackerPlugin.Tests;

public sealed class ViewDocumentTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Stored = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"itrk-view-{Guid.NewGuid():N}");

    public ViewDocumentTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ItemHolding Held(InventoryKind kind, string key, string label, string itemClass = "Drink_a", int quantity = 1) =>
        new(null, itemClass, quantity, [new HoldingLink(kind, key, label)], Stored, "Store", 1.0, false, []);

    private static TrackerState.Snapshot Snapshot(
        IReadOnlyList<(ItemHolding Held, PlacementState State)> items, IReadOnlyList<UnknownArrival>? arrivals = null) =>
        new(
            items.Select(i => new NamedHolding(i.Held, new ResolvedName(i.Held.ItemClass, i.Held.ItemClass.Replace('_', ' '), NameMatch.None, null))).ToList(),
            Resolver: null,
            Now,
            IngestHealth.Empty,
            new Reconciliation(
                items.Select(i => new ReconciledHolding(i.Held, i.State, $"because {i.State}", i.State == PlacementState.Verified ? Now : null)).ToList(),
                arrivals ?? [],
                [new StationCoverage("123", Now, 2, 3, 9, null)],
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
    public void A_document_that_fits_is_written_whole_and_replaces_the_last_one()
    {
        var path = Path.Combine(_dir, "view-data.json");
        var first = Snapshot([(Held(InventoryKind.Location, "123", "Keeger"), PlacementState.Verified)]);

        ViewPublisher.WriteSmallestFit(first, path);
        ViewPublisher.WriteSmallestFit(TrackerState.Snapshot.Empty, path);

        using var written = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(ViewDocument.Schema, written.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal(0, written.RootElement.GetProperty("systems").GetArrayLength());
    }

    [Fact]
    public void A_document_over_the_limit_is_refused_and_the_last_good_one_stays()
    {
        var path = Path.Combine(_dir, "view-data.json");
        ViewPublisher.WriteSmallestFit(Snapshot([(Held(InventoryKind.Location, "123", "Keeger"), PlacementState.Verified)]), path);
        var before = File.ReadAllText(path);

        var huge = Enumerable.Range(0, 9000)
            .Select(i => (Held(InventoryKind.Location, "123", "Keeger", $"Item_{i:00000}_with_a_long_class_name_to_fill_the_document"), PlacementState.Deduced))
            .ToList();

        Assert.Throws<InvalidOperationException>(() => ViewPublisher.WriteSmallestFit(Snapshot(huge), path));
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void A_document_too_big_with_full_history_is_built_again_with_less()
    {
        var asked = new List<int>();
        var fitted = ViewPublisher.Fit(history =>
        {
            asked.Add(history);
            return new JsonObject { ["history"] = history, ["pad"] = new string('x', 130_000 * history + 100_000) };
        });

        Assert.Equal([ViewDocument.FullHistory, 3], asked);
        Assert.Equal(3, fitted!["history"]!.GetValue<int>());
    }

    [Fact]
    public void The_size_is_counted_the_way_the_sdk_writes_it_indented()
    {
        // About 0.9 MB compact, well over 1 MiB once indented: many small objects, which indentation inflates most.
        JsonObject Build(int history) => new()
        {
            ["rows"] = new JsonArray(Enumerable.Range(0, 12_000 * (history + 1) / 9)
                .Select(i => (JsonNode)new JsonObject { ["a"] = i, ["b"] = "xxxxxxxxxxxxxxxxxxxx", ["c"] = new JsonArray(1, 2, 3) }).ToArray()),
        };

        var compact = Build(ViewDocument.FullHistory).ToJsonString().Length;
        var fitted = ViewPublisher.Fit(Build);

        Assert.True(compact < PluginView.MaxDataBytes, "the fixture must be under the limit compact");
        Assert.NotNull(fitted);
        PluginView.WriteData(Path.Combine(_dir, "indented.json"), fitted);
    }

    [Fact]
    public void A_document_that_does_not_fit_even_without_history_has_no_fit()
    {
        var asked = new List<int>();

        Assert.Null(ViewPublisher.Fit(history =>
        {
            asked.Add(history);
            return new JsonObject { ["pad"] = new string('x', PluginView.MaxDataBytes) };
        }));
        Assert.Equal([ViewDocument.FullHistory, 3, 0], asked);
    }

    [Fact]
    public void The_shipped_view_declares_itself_and_loads_nothing_from_outside()
    {
        PluginViewAssert.IsDeclaredAndSelfContained(AppContext.BaseDirectory);
    }

    [Fact]
    public void The_page_needs_no_inline_script_the_engines_policy_would_block()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "view", "index.html"));

        Assert.DoesNotContain("onclick=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("<script type=\"module\" src=\"main.js\"></script>", html);
        Assert.DoesNotMatch(@"<script(?![^>]*\bsrc=)", html);
    }

    private static string[] Keys(JsonNode node) => node.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();

    private static string FixturePath([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "ItemTrackerView", "fixtures", "view-data.json"));
}
