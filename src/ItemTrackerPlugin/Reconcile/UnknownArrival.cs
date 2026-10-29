namespace ItemTrackerPlugin.Reconcile;

/// <summary>
/// Something the screen showed at a station where the ledger never placed anything of the kind: the
/// starting inventory the logs lack, or a server-side arrival. It has no geid, only what was seen.
/// </summary>
/// <param name="ItemClass">The class the screen side resolved, when the game text was there.</param>
public sealed record UnknownArrival(
    string LocationId,
    string Name,
    string? ItemClass,
    int Quantity,
    DateTimeOffset SeenAt,
    int? Page,
    PlacementState State,
    string Reason);
