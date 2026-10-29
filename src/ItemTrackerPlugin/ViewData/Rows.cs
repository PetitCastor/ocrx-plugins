using System.Text.Json.Nodes;
using ItemTrackerPlugin.App;
using ItemTrackerPlugin.Model;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Resolve;

namespace ItemTrackerPlugin.ViewData;

/// <summary>Every item the view shows, each as a JSON row under the place it is grouped by.</summary>
internal sealed class Rows
{
    private readonly HoldingResolver? _resolver;
    private readonly int _history;
    private readonly ILookup<string, MoveRecord> _movesByClass;
    private readonly Dictionary<ItemHolding, ReconciledHolding> _judged;

    public Rows(TrackerState.Snapshot snapshot, Reconciliation reconciliation, HoldingResolver? resolver, int historyPerItem)
    {
        _resolver = resolver;
        _history = historyPerItem;
        _movesByClass = historyPerItem > 0 && resolver is not null
            ? resolver.Moves.ToLookup(m => m.ItemClass, StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<MoveRecord>().ToLookup(m => m.ItemClass);
        _judged = new Dictionary<ItemHolding, ReconciledHolding>(ReferenceEqualityComparer.Instance);
        foreach (var held in reconciliation.Holdings) _judged[held.Holding] = held;

        var items = new List<Row>();
        foreach (var named in snapshot.Holdings)
        {
            if (Ledger(named) is { } row) items.Add(row);
        }

        items.AddRange(reconciliation.Arrivals.Select(Arrival));
        Items = items;
    }

    public IReadOnlyList<Row> Items { get; }

    private Row? Ledger(NamedHolding named)
    {
        var h = named.Holding;
        var (kind, id, label) = h switch
        {
            { IsAtStation: true } => (RowKind.Station, h.Root!.Key, ""),
            { IsInShip: true } => (RowKind.Ship, h.Root!.Key, ""),
            { IsDropped: true } => (RowKind.Dropped, h.Root!.Key, h.Root.Label),
            _ => (RowKind.Station, "", ""),
        };
        // Worn, carried and lost-track items are not the tracker's business: it exists to find what was put away.
        if (id.Length == 0 && kind == RowKind.Station) return null;

        _judged.TryGetValue(h, out var judged);
        var state = judged?.State ?? PlacementState.Deduced;
        var json = new JsonObject
        {
            ["name"] = named.Display,
            ["itemClass"] = h.ItemClass,
            ["quantity"] = h.Quantity,
            ["state"] = ViewDocument.StateName(state),
            ["reason"] = judged?.Reason ?? "Not compared with the screen",
            ["seenAt"] = judged?.SeenAt is { } seen ? ViewDocument.Iso(seen) : null,
            ["lastMove"] = ViewDocument.Iso(h.LastMove),
            ["lastMoveType"] = h.LastMoveType,
            ["where"] = h.Where,
            ["caveats"] = new JsonArray(h.Caveats.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["history"] = History(h),
        };
        return new Row(kind, id, label, json);
    }

    private static Row Arrival(UnknownArrival a) => new(RowKind.Arrival, a.LocationId, "", new JsonObject
    {
        ["name"] = a.Name,
        ["itemClass"] = a.ItemClass,
        ["quantity"] = a.Quantity,
        ["state"] = ViewDocument.StateName(a.State),
        ["reason"] = a.Reason,
        ["seenAt"] = ViewDocument.Iso(a.SeenAt),
        ["lastMove"] = null,
        ["lastMoveType"] = "Seen on screen",
        ["where"] = "no log line places it here",
        ["caveats"] = new JsonArray(),
        ["history"] = new JsonArray(),
    });

    /// <summary>The newest moves of the item, newest first: its own for a named one, else those of its kind at its place.</summary>
    private JsonArray History(ItemHolding h)
    {
        var moves = new JsonArray();
        if (_history == 0 || _resolver is null) return moves;

        IEnumerable<MoveRecord> source = h.Geid is { } geid
            ? _resolver.History(geid)
            : _movesByClass[h.ItemClass].Where(m => Touches(m, h.Root));

        foreach (var m in source.OrderByDescending(m => m.Timestamp).Take(_history))
        {
            moves.Add(new JsonObject
            {
                ["at"] = ViewDocument.Iso(m.Timestamp),
                ["type"] = m.ArrivedBy,
                ["from"] = Endpoint(m.SourceKind, m.SourceKey),
                ["to"] = Endpoint(m.TargetKind, m.TargetKey),
                ["amount"] = m.Units,
                ["result"] = m.Result,
            });
        }

        return moves;
    }

    private static bool Touches(MoveRecord m, HoldingLink? root) =>
        root is not null
        && ((m.SourceKind == root.Kind && m.SourceKey == root.Key) || (m.TargetKind == root.Kind && m.TargetKey == root.Key));

    private string Endpoint(InventoryKind kind, string key) => kind switch
    {
        InventoryKind.Location => _resolver!.PlaceLabel(key),
        InventoryKind.Container => "a container",
        InventoryKind.Equipped => "your character",
        InventoryKind.World => "the ground",
        InventoryKind.Ship => _resolver!.ShipLabel(key),
        _ => "elsewhere",
    };
}
