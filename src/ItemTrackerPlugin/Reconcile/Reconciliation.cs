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

    /// <summary>Per page of each visited station, for the in-game status.</summary>
    public IReadOnlyList<PageStatus> Pages { get; init; } = [];

    /// <summary>Rows in a state, ledger placements and unknown arrivals together.</summary>
    public int Count(PlacementState state) =>
        Holdings.Count(h => h.State == state) + Arrivals.Count(a => a.State == state);

    /// <summary>The station's own items by state: placements it holds directly, and what the screen showed there that the ledger lacks.</summary>
    public StateTally At(string locationId)
    {
        var tally = new StateTally();
        foreach (var h in Holdings.Where(h => h.Holding.Chain is [{ Kind: Model.InventoryKind.Location } direct, ..] && direct.Key == locationId))
            tally = Add(tally, h.State, h.Holding.Quantity);
        foreach (var a in Arrivals.Where(a => a.LocationId == locationId))
            tally = Add(tally, a.State, a.Quantity);
        return tally;
    }

    private static StateTally Add(StateTally t, PlacementState state, int units) => state switch
    {
        PlacementState.Verified => t with { Verified = t.Verified + units },
        PlacementState.Deduced => t with { Deduced = t.Deduced + units },
        PlacementState.Stale => t with { Stale = t.Stale + units },
        _ => t with { Conflict = t.Conflict + units },
    };
}
