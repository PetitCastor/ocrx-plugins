namespace ItemTrackerPlugin.Screen;

/// <summary>
/// The first block of an item tooltip. Fields the tooltip does not print are null; consumables
/// have no <see cref="RegisteredToYou"/>. The quantity is never here: only the tile badge shows it.
/// </summary>
public sealed record TooltipReading(
    string Name,
    long VolumeMicroScu,
    string? Manufacturer,
    string? ItemType,
    string? Class,
    int? Size,
    string? AmmoCount,
    string? ContentsRemaining,
    bool RegisteredToYou);
