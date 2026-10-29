using ItemTrackerPlugin.Screen;

namespace ItemTrackerPlugin.Reconcile;

/// <summary>
/// One <see cref="ScreenObservation"/> as kept in the store, with its provenance: when the frame was
/// read, which region the tooltip came from and what OCR returned. Nothing says which station it was
/// at; that is found from the log when reconciling, since the log may be read after the frame.
/// </summary>
/// <param name="Quantity">The tile badge; null when none was read.</param>
/// <param name="RawText">The observation as the plugin emitted it, for audit.</param>
public sealed record StoredObservation(
    DateTimeOffset At,
    ScreenContext Panel,
    string? ShipModel,
    string Name,
    string? ItemClass,
    int? Quantity,
    int? Page,
    int? Pages,
    int? Row,
    int? Column,
    string SourceRoi,
    string RawText);
