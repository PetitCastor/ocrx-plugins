namespace ItemTrackerPlugin.Ingest;

/// <summary>
/// <c>&lt;Request Terminate Access To Inventory&gt; Player[..] terminating access to
/// [&lt;id&gt;:Location:&lt;loc&gt;]</c> — the station inventory left the screen. It closes the
/// context a <see cref="LocationNamed"/> line opens, and is written for each close, including
/// the ones the game does on its own when another inventory takes its place.
/// </summary>
public sealed record InventoryClosed(
    DateTimeOffset Timestamp,
    string LocationId) : InventoryEvent(Timestamp);
