using ItemTrackerPlugin.Resolve;

namespace ItemTrackerPlugin.Reconcile;

/// <summary>A ledger placement with what it is matched by: the station it sits at directly and its name key.</summary>
internal sealed record ReconcileRow(ItemHolding Holding, string? Station, string? Key);
