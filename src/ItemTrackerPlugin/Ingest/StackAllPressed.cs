namespace ItemTrackerPlugin.Ingest;

/// <summary>
/// <c>&lt;Player Inventory Request Complete&gt; ... Type[StackAll] Result[..]</c> — the player
/// pressed the stack button. It moves nothing, but the grid may have re-sorted, so a screen
/// read of the list from before it no longer describes what follows.
/// </summary>
public sealed record StackAllPressed(
    DateTimeOffset Timestamp,
    int RequestNo,
    string Result) : InventoryEvent(Timestamp)
{
    public bool Succeeded => string.Equals(Result, "Succeed", StringComparison.OrdinalIgnoreCase);
}
