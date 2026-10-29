namespace ItemTrackerPlugin.Screen;

/// <summary>
/// At the time of one frame, the panel <see cref="Panel"/> showed this item hovered. No
/// reconciliation with the log happens here.
/// </summary>
/// <param name="Panel">Which inventory the hovered tile belongs to: the right panel's context, or
/// <see cref="ScreenContext.Personal"/> for a tooltip on the left panel.</param>
/// <param name="ShipModel">The ship, for <see cref="ScreenContext.ShipStorage"/>.</param>
/// <param name="ItemClass">Null when the game text is not installed or the name is ambiguous.</param>
/// <param name="Quantity">The tile badge; null when none was read, which cannot tell "one" from
/// "not read".</param>
/// <param name="Tile">Null for a left-panel tooltip and when no hover border was found.</param>
/// <param name="Page">The right panel's page counter; null for a left-panel tooltip.</param>
/// <param name="SourceRoi">The region the tooltip came from.</param>
public sealed record ScreenObservation(
    ScreenContext Panel,
    string? ShipModel,
    TooltipReading Tooltip,
    string? ItemClass,
    int? Quantity,
    GridTile? Tile,
    PageCounter? Page,
    CapacityReading? Capacity,
    string SourceRoi);
