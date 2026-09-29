using System.Text.Json;
using Ocrx.Contracts;
using Ocrx.Sdk;

namespace SignatureOcrSweep;

/// <summary>
/// The engine-mode probe. For every knob set it subscribes the planned ROI twice, as a Text ROI
/// (<c>kN</c>, the read being measured) and as a Detailed ROI (<c>kN#d</c>, same rect and scale), and emits
/// one JSON object per tick mapping <c>kN</c> to an <see cref="EngineRead"/>. The Detailed result carries
/// the frame rect and scale the engine actually applied, which is how a run can tell height mode from fit
/// mode. The client name has to be <c>SignaturePlugin</c>: the engine resolves it to the star-citizen
/// games-catalog entry, which is what makes ROIs scale by height.
/// </summary>
internal sealed class CounterProbePlugin(IReadOnlyList<RoiSubscription> planned) : IOcrxPlugin
{
    public const string DetailedSuffix = "#d";

    public string Name => "SignaturePlugin";

    public IReadOnlyList<RoiSubscription> Rois { get; } =
        [.. planned, .. planned.Select(r => r with { Id = r.Id.Value + DetailedSuffix, Kind = RoiKind.Detailed })];

    public Task OnTickAsync(TickContext ctx, CancellationToken ct)
    {
        var reads = new Dictionary<string, EngineRead>();
        foreach (var roi in planned)
            reads[roi.Id.Value] = Read(ctx.Tick, roi.Id);

        ctx.Services.Emit(new CaptureRecord(ctx.Tick.Timestamp, Name, TriggerKind.Auto, JsonSerializer.Serialize(reads)));
        return Task.CompletedTask;
    }

    private static EngineRead Read(TickData tick, RoiId id)
    {
        if (!tick.TryGetText(id, out var text))
            return new EngineRead("", tick.ErrorMessage(id) ?? $"text ROI {id.Value}: {tick.Status(id)}", null, null);

        RoiId detailedId = id.Value + DetailedSuffix;
        if (!tick.TryGetOcr(detailedId, out var ocr))
            return new EngineRead(text, tick.ErrorMessage(detailedId) ?? $"detailed ROI {detailedId.Value}: {tick.Status(detailedId)}", null, null);

        return new EngineRead(text, null, $"{ocr.RoiX},{ocr.RoiY},{ocr.RoiWidth},{ocr.RoiHeight}", ocr.EffectiveScale);
    }
}
