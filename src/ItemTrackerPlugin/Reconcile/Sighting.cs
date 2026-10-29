namespace ItemTrackerPlugin.Reconcile;

/// <summary>
/// How many of one kind of item a kiosk visit showed at a station: the sum over the distinct tiles of
/// that name. <see cref="Exact"/> is false when a tile's badge was not read, so a one there may be more.
/// <see cref="Whole"/> is true when the visit saw the whole station, so what it did not show is not there.
/// </summary>
internal sealed record Sighting(
    string LocationId,
    string Key,
    string Name,
    string? ItemClass,
    DateTimeOffset At,
    int? Page,
    int Observed,
    bool Exact,
    bool Whole);
