using Ocrx.Contracts;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.Tests;

/// <summary>Services whose first <c>UpdateRoisAsync</c> fails the way a dropped pipe does.</summary>
internal sealed class FailingOnceServices : IPluginServices
{
    public int Calls { get; private set; }

    public EngineInfo Engine { get; } = new(
        "test-engine", 1, 2560, 1440, ReplayMode: true, "en", [], TimeSpan.FromMilliseconds(250));

    public void Emit(CaptureRecord record)
    {
    }

    public Task<string?> DumpFrameAsync(RoiRect? roi, string prefix, CancellationToken ct) => Task.FromResult<string?>(null);

    public Task<OcrRegionResult?> ReadRoiAsync(RoiSubscription roi, CancellationToken ct) => Task.FromResult<OcrRegionResult?>(null);

    public Task UpdateRoisAsync(IReadOnlyList<RoiSubscription> rois, CancellationToken ct = default) =>
        ++Calls == 1 ? Task.FromException(new IOException("pipe closed")) : Task.CompletedTask;

    public void Log(string message)
    {
    }

    public void LogVerbose(string message)
    {
    }
}
