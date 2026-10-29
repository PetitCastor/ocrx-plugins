namespace ItemTrackerPlugin.Ingest;

/// <summary>
/// <c>&lt;CEntityComponentShopUIProvider::RmShopFlowResponse&gt; Received ShopFlowResponse ...
/// kioskState[..] result[..] type[..]</c> — the server's answer to a kiosk request. Only
/// <c>type[Buying]</c> has been seen.
/// </summary>
public sealed record ShopFlowResolved(
    DateTimeOffset Timestamp,
    string ShopName,
    string KioskState,
    string Result,
    string Type) : InventoryEvent(Timestamp)
{
    public bool Succeeded => string.Equals(Result, "Success", StringComparison.OrdinalIgnoreCase);
}
