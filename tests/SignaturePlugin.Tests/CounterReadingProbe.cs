using Ocrx.Sdk;
using SignaturePluginRois = global::SignaturePlugin.Rois;

namespace SignaturePlugin.Tests;

/// <summary>
/// Stands in for <see cref="SignaturePlugin"/> on a replay whose value names no ore. It subscribes
/// the plugin's own ROIs under the plugin's own client name — the engine resolves that name to the
/// star-citizen games-catalog entry, so the frame is mapped with that game's reference and scale
/// mode — and emits the counter's raw OCR text on every tick, where the real plugin would stay
/// silent for a number no cluster matches.
/// </summary>
/// <remarks>
/// The real plugin keeps the calibrated rect on a replay, since it cannot know the FOV a recorded
/// frame was taken at. A frame whose manifest entry records the game's stored vertical FOV passes it
/// here instead, and the counter is zoomed exactly as a live session at that FOV would zoom it.
/// </remarks>
internal sealed class CounterReadingProbe(double? verticalFov = null) : IOcrxPlugin
{
    private readonly RoiSubscription _counter =
        StarCitizenFovZoom.Apply(SignaturePluginRois.Counter, StarCitizenFovZoom.Factor(verticalFov));

    public string Name => "SignaturePlugin";

    public IReadOnlyList<RoiSubscription> Rois => [_counter];

    public Task OnTickAsync(TickContext ctx, CancellationToken ct)
    {
        var text = ctx.Tick.TryGetText(_counter.Id, out var read) ? read : string.Empty;
        ctx.Services.Emit(new CaptureRecord(ctx.Tick.Timestamp, Name, TriggerKind.Auto, text));
        return Task.CompletedTask;
    }
}
