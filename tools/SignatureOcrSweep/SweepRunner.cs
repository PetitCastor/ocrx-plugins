namespace SignatureOcrSweep;

/// <summary>Reads every manifest frame under every knob set in one mode. Rows are added to a caller-owned
/// list as frames finish, so a run that dies part-way still leaves its finished rows behind.</summary>
public static class SweepRunner
{
    public const string OfflineMode = "offline";
    public const string EngineMode = "engine";

    private const int Attempts = 2;

    public static async Task RunAsync(
        string mode,
        string corpusDir,
        IReadOnlyList<ManifestFrame> frames,
        IReadOnlyList<KnobSet> knobSets,
        OfflineReader? offline,
        EngineReader? engine,
        int maxImageDimension,
        List<SweepRow> rows,
        TextWriter log)
    {
        if (mode == EngineMode)
        {
            var unsupported = knobSets.Where(k => !k.IsEngineExpressible).Select(k => k.Name).ToList();
            if (unsupported.Count > 0)
                throw new InvalidOperationException(
                    "engine mode only exposes ROI rect and scale; not expressible there: " + string.Join(", ", unsupported));
        }

        foreach (var frame in frames)
        {
            var path = Path.Combine(corpusDir, frame.File);
            if (!File.Exists(path))
            {
                log.WriteLine($"skip {frame.File}: not in {corpusDir}");
                continue;
            }

            // One retry per frame: a spawned engine occasionally fails to come up or drain. A second failure
            // becomes error rows, so one bad frame does not cost the rest of the sweep.
            Exception? failure = null;
            for (var attempt = 1; attempt <= Attempts; attempt++)
            {
                try
                {
                    rows.AddRange(await ReadFrameAsync(mode, path, frame, knobSets, offline, engine, maxImageDimension));
                    failure = null;
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failure = ex;
                    log.WriteLine($"{mode}: {frame.File} attempt {attempt}/{Attempts} failed: {ex.Message}");
                }
            }

            if (failure is not null)
            {
                rows.AddRange(knobSets.Select(k => new SweepRow(
                    mode, frame.File, frame.ConfigurationLabel, frame.VerticalFov, "", k.Name, "", 0, "", null,
                    frame.Signature, Verdict.Error, $"{failure.GetType().Name}: {failure.Message}")));
            }

            log.WriteLine($"{mode}: {frame.File} ({frame.ConfigurationLabel}) {(failure is null ? "done" : "ERROR")}");
        }
    }

    private static async Task<List<SweepRow>> ReadFrameAsync(
        string mode, string path, ManifestFrame frame, IReadOnlyList<KnobSet> knobSets,
        OfflineReader? offline, EngineReader? engine, int maxImageDimension)
    {
        using var bitmap = await FrameLoader.LoadAsync(path);
        var plans = knobSets.Select((k, i) => (Id: $"k{i}", Knobs: k,
                Plan: ReadPlan.Create(bitmap.PixelWidth, bitmap.PixelHeight, frame.VerticalFov, k, maxImageDimension)))
            .ToList();

        IReadOnlyDictionary<string, EngineRead>? engineReads = null;
        if (mode == EngineMode)
            engineReads = await engine!.ReadAsync(path, plans.ToDictionary(p => p.Id, p => p.Plan));

        var size = $"{bitmap.PixelWidth}x{bitmap.PixelHeight}";
        var rows = new List<SweepRow>();
        foreach (var (id, knobs, plan) in plans)
        {
            var planned = $"{plan.FrameRect.X},{plan.FrameRect.Y},{plan.FrameRect.Width},{plan.FrameRect.Height}";
            string raw, crop = planned, note = "";
            var scale = plan.EffectiveScale;
            var failed = false;

            if (engineReads is null)
            {
                raw = await offline!.ReadAsync(bitmap, plan, knobs);
            }
            else
            {
                var read = engineReads.TryGetValue(id, out var r)
                    ? r
                    : new EngineRead("", "probe reported no result for this ROI", null, null);
                raw = read.Text;
                if (read.Error is not null)
                {
                    failed = true;
                    note = read.Error;
                }
                else
                {
                    crop = read.Crop!;
                    scale = read.Scale!.Value;
                    if (crop != planned || Math.Abs(scale - plan.EffectiveScale) > 1e-6)
                        note = $"{SweepRow.CropMismatchPrefix}: engine applied {crop} at scale {scale:0.###}, " +
                            $"plan {planned} at scale {plan.EffectiveScale:0.###} " +
                            "(the engine is probably mapping in fit mode: the games catalog did not resolve for SignaturePlugin)";
                }
            }

            double? parsed = null;
            var verdict = failed ? Verdict.Error : VerdictClassifier.Classify(raw, frame.Signature, out parsed);
            rows.Add(new SweepRow(mode, frame.File, frame.ConfigurationLabel, frame.VerticalFov, size, knobs.Name,
                crop, scale, raw, parsed, frame.Signature, verdict, note));
        }

        return rows;
    }
}
