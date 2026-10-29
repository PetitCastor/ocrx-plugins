namespace ItemTrackerPlugin.Reconcile;

/// <summary>
/// How much of a station one kiosk visit saw. <see cref="Complete"/> needs every page of the list and an
/// observed item count equal to the log's freight total: a filtered or searched list has fewer pages
/// and a smaller count, so it never passes for the whole station.
/// </summary>
/// <param name="Pages">The page count the list showed; 0 when no page counter was read.</param>
/// <param name="FreightTotal">The station's item count from the log, when a full scroll gave one.</param>
public sealed record StationCoverage(
    string LocationId,
    DateTimeOffset At,
    int PagesSeen,
    int Pages,
    int ObservedItems,
    int? FreightTotal)
{
    public bool Complete => Pages > 0 && PagesSeen == Pages && FreightTotal == ObservedItems;
}
