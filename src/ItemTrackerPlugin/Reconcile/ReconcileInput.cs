using ItemTrackerPlugin.Resolve;

namespace ItemTrackerPlugin.Reconcile;

/// <summary>The three sources: the ledger, the screen, and the log's freight totals.</summary>
/// <param name="Holdings">The ledger as it stands now, from <see cref="HoldingResolver.ResolveAll"/>.</param>
/// <param name="Moves">Every recorded move, to wind the ledger back to the time of a frame.</param>
/// <param name="Updates">Game builds first played, oldest first.</param>
/// <param name="FreightTotals">Item count per station location id, where a full scroll gave one.</param>
/// <param name="PlaceLabel">What to call a station in a reason.</param>
public sealed record ReconcileInput(
    IReadOnlyList<ItemHolding> Holdings,
    IReadOnlyList<MoveRecord> Moves,
    IReadOnlyList<HoldingResolver.GameUpdate> Updates,
    IReadOnlyList<StoredObservation> Observations,
    IReadOnlyList<StationVisit> Visits,
    IReadOnlyDictionary<string, int> FreightTotals,
    Func<string, string> PlaceLabel);
