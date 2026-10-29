namespace ItemTrackerPlugin.Reconcile;

/// <summary>How far a placement can be trusted, from which sources agree on it.</summary>
public enum PlacementState
{
    /// <summary>Seen on screen at its place since its last logged move.</summary>
    Verified,

    /// <summary>Known from the logs only.</summary>
    Deduced,

    /// <summary>A game build changed since it was last verified or moved: updates move items without logging it.</summary>
    Stale,

    /// <summary>The screen and the ledger disagree on how many of it are there.</summary>
    Conflict,
}
