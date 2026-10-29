namespace ItemTrackerPlugin.Reconcile;

/// <summary>
/// Where one page of a station's kiosk list stands. A kind of item is placed on the page where the
/// screen last showed it, and counts as verified when every placement of it at that station is.
/// </summary>
/// <param name="Kinds">Distinct kinds of item the page showed.</param>
/// <param name="VerifiedKinds">Those whose placements are all verified.</param>
public sealed record PageStatus(string LocationId, int Page, int Kinds, int VerifiedKinds)
{
    /// <summary>True when the page showed something and all of it is verified.</summary>
    public bool Verified => Kinds > 0 && VerifiedKinds == Kinds;
}
