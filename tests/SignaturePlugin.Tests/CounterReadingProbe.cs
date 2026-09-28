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
internal sealed class CounterReadingProbe : IOcrxPlugin
{
    public string Name => "SignaturePlugin";

    public IReadOnlyList<RoiSubscription> Rois => SignaturePluginRois.All;

    public Task OnTickAsync(TickContext ctx, CancellationToken ct)
    {
        var text = ctx.Tick.TryGetText(SignaturePluginRois.Counter.Id, out var read) ? read : string.Empty;
        ctx.Services.Emit(new CaptureRecord(ctx.Tick.Timestamp, Name, TriggerKind.Auto, text));
        return Task.CompletedTask;
    }
}
