using ItemTrackerPlugin.Resolve;

namespace ItemTrackerPlugin.Reconcile;

/// <summary>A ledger placement with the state the screen and the logs give it, and why.</summary>
/// <param name="SeenAt">The last time the kiosk showed this kind of item at its station, when it ever did.</param>
public sealed record ReconciledHolding(ItemHolding Holding, PlacementState State, string Reason, DateTimeOffset? SeenAt = null);
