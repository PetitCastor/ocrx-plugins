using Ocrx.Contracts;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;

namespace SignaturePlugin.Tests;

/// <summary>
/// <see cref="FakePluginServices"/> whose ROI pushes fail a set number of times first — the one
/// failure mode the SDK does not swallow for the plugin.
/// </summary>
internal sealed class FlakyRoiServices(FakePluginServices inner, int failures) : IPluginServices
{
    private int _failuresLeft = failures;

    public FakePluginServices Inner => inner;

    public Task UpdateRoisAsync(IReadOnlyList<RoiSubscription> rois, CancellationToken ct = default)
    {
        if (_failuresLeft > 0)
        {
            _failuresLeft--;
            throw new InvalidDataException("engine rejected the set");
        }

        return inner.UpdateRoisAsync(rois, ct);
    }

    public EngineInfo Engine => inner.Engine;
    public void Emit(CaptureRecord record) => inner.Emit(record);
    public void EmitCleared(DateTime timestamp, string plugin) => inner.EmitCleared(timestamp, plugin);
    public Task<string?> DumpFrameAsync(RoiRect? roi, string prefix, CancellationToken ct) => inner.DumpFrameAsync(roi, prefix, ct);
    public Task<OcrRegionResult?> ReadRoiAsync(RoiSubscription roi, CancellationToken ct) => inner.ReadRoiAsync(roi, ct);
    public Task PublishSettingsAsync(SettingsSpec spec, CancellationToken ct = default) => inner.PublishSettingsAsync(spec, ct);
    public Task RebuildOutputsAsync(PluginConfig config, CancellationToken ct = default) => inner.RebuildOutputsAsync(config, ct);
    public void Log(string message) => inner.Log(message);
    public void LogVerbose(string message) => inner.LogVerbose(message);
}
