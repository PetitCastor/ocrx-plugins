namespace ItemTrackerPlugin.Reconcile;

/// <summary>How many items of one station are in each state, counting every unit of a stack.</summary>
public readonly record struct StateTally(int Verified, int Deduced, int Stale, int Conflict)
{
    public int Total => Verified + Deduced + Stale + Conflict;
}
