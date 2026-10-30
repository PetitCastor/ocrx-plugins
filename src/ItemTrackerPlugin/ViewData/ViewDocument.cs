using System.Globalization;
using System.Text.Json.Nodes;
using ItemTrackerPlugin.App;
using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Resolve;

namespace ItemTrackerPlugin.ViewData;

/// <summary>
/// The one JSON document the read-only view renders: the ledger snapshot with each placement's state,
/// grouped the way the tabs show it (locations by system and station, ships by model, drops by place).
/// Keys are camel-cased by hand, so the shape the page reads is visible here and nowhere else. Text is
/// plain data for the page to put in with <c>textContent</c>; nothing in it is markup.
/// </summary>
public static class ViewDocument
{
    public const int Schema = 1;

    /// <summary>Moves kept per item. The document goes to the window in-process, so nothing caps its size.</summary>
    public const int FullHistory = 8;

    public static JsonObject Build(TrackerState.Snapshot snapshot, int historyPerItem = FullHistory)
    {
        var reconciliation = snapshot.Reconciliation ?? Reconciliation.Empty;
        var resolver = snapshot.Resolver;
        var rows = new Rows(snapshot, reconciliation, resolver, historyPerItem);

        // Screen-only arrivals at a station the ledger holds nothing at still belong on the list. The game
        // re-issues a station's id from time to time, so rows are grouped by the place every alias resolves
        // to, or one station would show up once per id it was ever given.
        var stations = rows.Items
            .Where(r => r.Kind is RowKind.Station or RowKind.Arrival)
            .GroupBy(r => PlaceIdOf(r.PlaceId, resolver))
            .Select(g => Station(g.Key, g.ToList(), reconciliation, resolver))
            .ToList();

        var systems = new JsonArray();
        foreach (var system in stations.GroupBy(s => s.System)
                     .OrderBy(g => g.Key == Place.UnknownSystem).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            systems.Add(new JsonObject
            {
                ["name"] = system.Key,
                ["stations"] = new JsonArray(system.Select(s => (JsonNode)s.Station)
                    .OrderBy(s => s["name"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase).ToArray()),
            });
        }

        return new JsonObject
        {
            ["schema"] = Schema,
            ["builtAt"] = Iso(snapshot.BuiltAt),
            ["counts"] = Tally(reconciliation.Count(PlacementState.Verified), reconciliation.Count(PlacementState.Deduced),
                reconciliation.Count(PlacementState.Stale), reconciliation.Count(PlacementState.Conflict)),
            ["ledger"] = new JsonObject
            {
                ["moves"] = snapshot.Health.Moves,
                ["logFiles"] = snapshot.Health.LogFiles,
                ["newestLogLine"] = snapshot.Health.NewestLogLine is { } newest ? Iso(newest) : null,
            },
            ["here"] = snapshot.Health.CurrentLocationId is { } here ? PlaceIdOf(here, resolver) : null,
            ["systems"] = systems,
            ["ships"] = Ships(rows, resolver),
            ["dropped"] = Dropped(rows),
        };
    }

    private static (string System, JsonObject Station) Station(
        string id, List<Row> items, Reconciliation reconciliation, HoldingResolver? resolver)
    {
        var place = resolver?.Places.ByLocationId(id);
        var aliases = items.Select(r => r.PlaceId).Concat(place?.AliasIds ?? [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var tallies = aliases.Select(reconciliation.At).ToList();
        var coverage = reconciliation.Coverage.Where(c => aliases.Contains(c.LocationId)).OrderBy(c => c.At).LastOrDefault();

        // The system is the heading the page puts the station under, so the name leaves it out.
        var name = place is null
            ? resolver?.PlaceLabel(id) ?? $"location {id}"
            : place.NameConfirmed ? place.LabelInSystem : $"{place.LabelInSystem} (unconfirmed)";

        var station = new JsonObject
        {
            ["id"] = id,
            ["aliasIds"] = new JsonArray(aliases.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray()),
            ["name"] = name,
            ["resolved"] = place is not null,
            ["counts"] = Tally(tallies.Sum(t => t.Verified), tallies.Sum(t => t.Deduced),
                tallies.Sum(t => t.Stale), tallies.Sum(t => t.Conflict)),
            ["lastActivity"] = LastActivity(items, coverage),
            ["coverage"] = coverage is null ? null : new JsonObject
            {
                ["at"] = Iso(coverage.At),
                ["pagesSeen"] = coverage.PagesSeen,
                ["pages"] = coverage.Pages,
                ["observedItems"] = coverage.ObservedItems,
                ["freightTotal"] = coverage.FreightTotal,
                ["complete"] = coverage.Complete,
            },
            ["items"] = Sorted(items),
        };
        return (place?.System ?? Place.UnknownSystem, station);
    }

    /// <summary>The place a location id belongs to, keyed by its canonical id; an id nothing names stands for itself.</summary>
    private static string PlaceIdOf(string locationId, HoldingResolver? resolver) =>
        resolver?.Places.ByLocationId(locationId)?.Id ?? locationId;

    /// <summary>
    /// The newest thing that happened at the station — a logged move, a sighting or a visit — which the page
    /// opens on when the player's own position names no station. Every time is <see cref="Iso"/>-formatted,
    /// so the newest also sorts last as text.
    /// </summary>
    private static string? LastActivity(List<Row> items, StationCoverage? coverage) =>
        items.SelectMany(r => new[] { r.Json["lastMove"], r.Json["seenAt"] })
            .Select(n => n?.GetValue<string>())
            .Append(coverage is null ? null : Iso(coverage.At))
            .Where(t => t is not null)
            .Max(StringComparer.Ordinal);

    private static JsonArray Ships(Rows rows, HoldingResolver? resolver)
    {
        var ships = new JsonArray();
        foreach (var ship in rows.Items.Where(r => r.Kind == RowKind.Ship).GroupBy(r => r.PlaceId)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var retrieved = resolver?.LastRetrieved(ship.Key);
            ships.Add(new JsonObject
            {
                ["class"] = ship.Key,
                ["name"] = resolver?.ShipLabel(ship.Key) ?? ship.Key,
                ["retrievedAt"] = retrieved is { } r ? Iso(r.At) : null,
                ["retrievedAtPlace"] = retrieved?.PlaceLabel,
                ["items"] = Sorted(ship.ToList()),
            });
        }

        return ships;
    }

    private static JsonArray Dropped(Rows rows)
    {
        var places = new JsonArray();
        foreach (var place in rows.Items.Where(r => r.Kind == RowKind.Dropped).GroupBy(r => r.PlaceLabel)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            places.Add(new JsonObject { ["place"] = place.Key, ["items"] = Sorted(place.ToList()) });
        }

        return places;
    }

    private static JsonArray Sorted(List<Row> items) =>
        new(items.OrderBy(i => i.Json["name"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase)
            .Select(i => (JsonNode)i.Json).ToArray());

    internal static JsonObject Tally(int verified, int deduced, int stale, int conflict) => new()
    {
        ["verified"] = verified,
        ["deduced"] = deduced,
        ["stale"] = stale,
        ["conflict"] = conflict,
    };

    internal static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    internal static string StateName(PlacementState state) => state.ToString().ToLowerInvariant();
}
