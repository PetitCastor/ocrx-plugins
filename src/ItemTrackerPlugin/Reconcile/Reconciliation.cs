namespace ItemTrackerPlugin.Reconcile;

/// <summary>Every ledger placement with its state, what the screen showed that the ledger lacks, and per-visit coverage.</summary>
/// <param name="Unplaced">Kiosk observations no station-open line in the log comes before.</param>
public sealed record Reconciliation(
    IReadOnlyList<ReconciledHolding> Holdings,
    IReadOnlyList<UnknownArrival> Arrivals,
    IReadOnlyList<StationCoverage> Coverage,
    int Unplaced)
{
    public static readonly Reconciliation Empty = new([], [], [], 0);

    /// <summary>Rows in a state, ledger placements and unknown arrivals together.</summary>
    public int Count(PlacementState state) =>
        Holdings.Count(h => h.State == state) + Arrivals.Count(a => a.State == state);
}
