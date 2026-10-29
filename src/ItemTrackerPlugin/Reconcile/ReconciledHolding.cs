using ItemTrackerPlugin.Resolve;

namespace ItemTrackerPlugin.Reconcile;

/// <summary>A ledger placement with the state the screen and the logs give it, and why.</summary>
public sealed record ReconciledHolding(ItemHolding Holding, PlacementState State, string Reason);
