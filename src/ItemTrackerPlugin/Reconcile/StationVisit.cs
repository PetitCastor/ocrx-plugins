namespace ItemTrackerPlugin.Reconcile;

/// <summary>
/// A station inventory the log says the player opened (<c>log_signal</c> kind <c>opened</c>). The kiosk
/// header names no station, so a frame is placed at the last one opened before it.
/// </summary>
/// <param name="LocationId">Null when the log could not tie the open to an id: its frames are unplaced.</param>
public sealed record StationVisit(string? LocationId, DateTimeOffset OpenedAt);
