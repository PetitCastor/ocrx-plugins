namespace ItemTrackerPlugin.Ingest;

/// <summary>
/// <c>&lt;CEntityComponentShopUIProvider::SendShopBuyRequest&gt; Sending SShopBuyRequest ...
/// itemName[..] quantity[n]</c> — the player asked a kiosk for an item. Whether it went through
/// is the <see cref="ShopFlowResolved"/> that follows.
/// </summary>
public sealed record ShopBuyRequested(
    DateTimeOffset Timestamp,
    string ShopName,
    string ItemName,
    int Quantity,
    double Price) : InventoryEvent(Timestamp);
