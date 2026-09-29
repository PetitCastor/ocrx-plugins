using System.Text.Json;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;

namespace SignatureOcrSweep;

/// <summary>
/// Engine mode, the ground truth: a real engine binary replays the frame through ReplayHarness while
/// <see cref="CounterProbePlugin"/> subscribes the planned ROIs. One engine run per frame carries every
/// knob set as its own ROI, so a sweep is frames-many launches, not frames x knob-sets-many.
/// </summary>
public sealed class EngineReader(string enginePath)
{
    /// <summary>The engine's read for each plan, keyed by the caller's plan ids.</summary>
    public async Task<IReadOnlyDictionary<string, EngineRead>> ReadAsync(
        string framePath, IReadOnlyDictionary<string, ReadPlan> plans, CancellationToken ct = default)
    {
        // The engine replays a directory; give it a private one holding just this frame.
        var corpus = Path.Combine(Path.GetTempPath(), $"signature-ocr-sweep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(corpus);
        File.Copy(framePath, Path.Combine(corpus, Path.GetFileName(framePath)));
        try
        {
            var rois = plans.Select(p => p.Value.Subscription with { Id = p.Key }).ToArray();
            var result = await ReplayHarness.RunAsync(new ReplayOptions
            {
                EnginePath = enginePath,
                CorpusDir = corpus,
                Plugin = new CounterProbePlugin(rois),
                Timeout = TimeSpan.FromMinutes(2),
            }, ct);

            if (result.ExitCode != 0 || result.Reason != StreamEndReason.ReplayCompleted)
                throw new InvalidOperationException(
                    $"{Path.GetFileName(framePath)}: engine replay ended with exit {result.ExitCode}, reason {result.Reason}");
            if (result.Records.Count == 0)
                throw new InvalidOperationException($"{Path.GetFileName(framePath)}: engine emitted no tick");

            return JsonSerializer.Deserialize<Dictionary<string, EngineRead>>(result.Records[0].RawText)
                ?? throw new InvalidOperationException("probe record was not a JSON object");
        }
        finally
        {
            Directory.Delete(corpus, recursive: true);
        }
    }
}
