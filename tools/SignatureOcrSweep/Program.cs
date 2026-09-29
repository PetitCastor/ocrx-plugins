using System.Diagnostics;
using Ocrx.Sdk.Testing;
using SignatureOcrSweep;

const string Usage = """
    SignatureOcrSweep: measure the counter OCR read on a directory of PNG frames plus a manifest.json.

      sweep --corpus DIR [--mode offline|engine] [--knob SPEC]... [--scales 3,4,6] [--out DIR]
                         [--engine PATH] [--ocr-lang TAG]     (--ocr-lang: offline mode only)
      gate  --corpus DIR [--knob SPEC]... [--scales ...] [--out DIR] [--engine PATH]
      synth --from FILE --size WxH --to FILE

    Manifest: { "frames": [ { "file", "signature", "configuration", "verticalFov", "lossy", "note" } ] }
    (the parity corpus shape; the last four fields are optional additions it ignores).
    Knob SPEC: baseline, or key=value terms joined by '+': scale=8, norm=6, margin=4, channel=lum|max|value,
    contrast=stretch|gamma:0.7, bin=otsu|fixed:128, interp=fant|linear|nearest, sharpen=unsharp:1.
    Engine mode can only run scale, norm and margin knob sets. 'gate' runs both modes over the knob sets
    engine mode can express, compares the raw text, checks that the engine applied the planned crop and
    scale, and exits 1 on any difference. Engine reads use the engine's own OCR language setting.
    Engine binary: --engine, else OCRX_ENGINE_PATH, else %LOCALAPPDATA%\OcrxEngine\current.
    """;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}

try
{
    var command = args[0];
    var options = Options.Parse(args.Skip(1).ToArray());

    if (command == "synth")
        return await SynthAsync(options);

    var corpusDir = Path.GetFullPath(options.Single("corpus"));
    var frames = CorpusManifest.Load(corpusDir);
    var knobSets = KnobSetsFrom(options);
    var outDir = Path.GetFullPath(options.Optional("out") ?? "sweep-out");
    Directory.CreateDirectory(outDir);

    switch (command)
    {
        case "sweep":
        {
            var mode = options.Optional("mode") ?? SweepRunner.OfflineMode;
            if (mode == SweepRunner.EngineMode && options.Optional("ocr-lang") is not null)
                throw new FormatException("--ocr-lang only applies to offline mode; the engine uses its own engine-config.json ocrLanguage");

            var rows = await RunModeAsync(mode, corpusDir, frames, knobSets, options, outDir);
            PrintPivot(mode, rows, outDir);
            return rows.Any(r => r.Verdict == Verdict.Error) ? 1 : 0;
        }
        case "gate":
        {
            if (options.Optional("ocr-lang") is not null)
                throw new FormatException("gate does not take --ocr-lang: the engine side cannot be told a language, so a non-default one would fail the gate for the wrong reason");

            var gateKnobs = knobSets.Where(k => k.IsEngineExpressible).ToList();
            var skipped = knobSets.Except(gateKnobs).Select(k => k.Name).ToList();
            if (skipped.Count > 0)
                Console.WriteLine("gate skips knob sets engine mode cannot express: " + string.Join(", ", skipped));

            var engineRows = await RunModeAsync(SweepRunner.EngineMode, corpusDir, frames, gateKnobs, options, outDir);
            var offlineRows = await RunModeAsync(SweepRunner.OfflineMode, corpusDir, frames, gateKnobs, options, outDir);
            var lines = GateReport.Compare(engineRows, offlineRows);
            var problems = GateReport.EngineProblems(engineRows);
            Console.WriteLine(GateReport.Text(lines));
            File.WriteAllText(Path.Combine(outDir, "gate.csv"), GateReport.Csv(lines));
            if (problems.Count > 0)
            {
                Console.WriteLine($"GATE FAILED: {problems.Count} engine read(s) were not verified (crop/scale differs from plan, or the read errored):");
                foreach (var problem in problems)
                    Console.WriteLine("  " + problem);
            }
            else
            {
                Console.WriteLine($"CROP CHECK PASSED: the engine applied the planned crop and scale on all {engineRows.Count} reads.");
            }

            PrintPivot(SweepRunner.EngineMode, engineRows, outDir);
            PrintPivot(SweepRunner.OfflineMode, offlineRows, outDir);
            return problems.Count == 0 && lines.All(l => l.Match) ? 0 : 1;
        }
        default:
            Console.Error.WriteLine($"unknown command '{command}'\n\n{Usage}");
            return 2;
    }
}
catch (Exception ex) when (ex is FormatException or InvalidOperationException or FileNotFoundException or ArgumentException or TimeoutException)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 2;
}

static async Task<List<SweepRow>> RunModeAsync(
    string mode, string corpusDir, IReadOnlyList<ManifestFrame> frames, IReadOnlyList<KnobSet> knobSets,
    Options options, string outDir)
{
    OfflineReader? offline = null;
    EngineReader? engine = null;
    if (mode == SweepRunner.OfflineMode)
    {
        offline = new OfflineReader(options.Optional("ocr-lang"));
        Console.WriteLine($"offline mode: OCR recognizer {offline.Language}, max image dimension {OfflineReader.MaxImageDimension}");
    }
    else if (mode == SweepRunner.EngineMode)
    {
        var path = options.Optional("engine") ?? EngineLocator.Resolve();
        engine = new EngineReader(path);
        string? language;
        try { language = new OfflineReader().Language; }
        catch (InvalidOperationException) { language = null; }
        Console.WriteLine($"engine mode: {path} (product version {FileVersionInfo.GetVersionInfo(path).ProductVersion}); " +
            $"engine OCR language comes from its own config, this machine's offline recognizer would be {language ?? "unavailable"}");
    }
    else
    {
        throw new FormatException($"--mode must be offline or engine, not '{mode}'");
    }

    // Rows are written in a finally so a run that dies part-way still leaves the finished frames on disk.
    var rows = new List<SweepRow>();
    try
    {
        await SweepRunner.RunAsync(mode, corpusDir, frames, knobSets, offline, engine,
            OfflineReader.MaxImageDimension, rows, Console.Out);
    }
    finally
    {
        File.WriteAllText(Path.Combine(outDir, $"rows-{mode}.csv"), CsvFile.Rows(rows));
        File.WriteAllText(Path.Combine(outDir, $"pivot-{mode}.csv"), AccuracyPivot.Csv(rows, r => r.Configuration));
    }

    return rows;
}

static void PrintPivot(string mode, List<SweepRow> rows, string outDir)
{
    Console.WriteLine();
    Console.WriteLine($"accuracy, {mode} mode (correct/total, wN wrong numbers, uN unreadable, eN errors, rest blank):");
    Console.WriteLine(AccuracyPivot.Text(rows, r => r.Configuration));
    if (rows.Any(r => r.Configuration.Contains("[lossy]")))
        Console.WriteLine("[lossy] rows are not exact engine pixels (converted screenshots): a secondary set, not evidence for the shipped pipeline.");
    Console.WriteLine($"wrote {Path.Combine(outDir, $"rows-{mode}.csv")} and pivot-{mode}.csv");
}

static List<KnobSet> KnobSetsFrom(Options options)
{
    var specs = options.All("knob").ToList();
    if (options.Optional("scales") is { } scales)
        specs.AddRange(scales.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => $"scale={s}"));
    if (specs.Count == 0)
        specs.Add("baseline");
    return specs.Select(KnobSet.Parse).DistinctBy(k => k.Name).ToList();
}

static async Task<int> SynthAsync(Options options)
{
    var size = options.Single("size").Split('x');
    if (size.Length != 2 || !int.TryParse(size[0], out var width) || !int.TryParse(size[1], out var height))
        throw new FormatException("--size must be WxH");

    using var source = await FrameLoader.LoadAsync(Path.GetFullPath(options.Single("from")));
    using var scaled = await FrameLoader.DownscaleAsync(source, width, height);
    var to = Path.GetFullPath(options.Single("to"));
    await FrameLoader.SavePngAsync(scaled, to);
    Console.WriteLine($"wrote {to} ({scaled.PixelWidth}x{scaled.PixelHeight}), Fant downscale of {source.PixelWidth}x{source.PixelHeight}");
    return 0;
}
