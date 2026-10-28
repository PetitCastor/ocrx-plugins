using ItemTrackerPlugin.Model;

namespace ItemTrackerPlugin.Ingest;

/// <summary>
/// <c>&lt;InventoryManagement&gt; Freight Inventory Grid InventoryDataRequest Successful
/// Inventory[&lt;id&gt;:Location:&lt;loc&gt;] ... Page info: ... HasNextPage[n], ... EndCursor[..]</c> —
/// one page of a station's freight list.
/// <para>
/// <see cref="EndCursor"/> is how far into the list the page reaches. On a page that has a
/// next one it is a lower bound of the total; on the last page it is the total of the list
/// the player was looking at, which is smaller than the station's when the list was searched
/// or filtered. See <see cref="LogIngestor"/> for how the two are combined.
/// </para>
/// </summary>
/// <param name="EndCursor">Null when the cursor did not decode.</param>
public sealed record FreightPageSeen(
    DateTimeOffset Timestamp,
    InventoryRef Inventory,
    bool HasNextPage,
    long? EndCursor) : InventoryEvent(Timestamp);
