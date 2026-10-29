using ItemTrackerPlugin.Reconcile;

namespace ItemTrackerPlugin.Overlay;

/// <summary>What the status line says about the station whose kiosk is open.</summary>
/// <param name="Label">Short name for the line, or the id when the station has no name yet.</param>
public sealed record StationStatus(string LocationId, string Label, StateTally Tally, IReadOnlyList<PageStatus> Pages);
