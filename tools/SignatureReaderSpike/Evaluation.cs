using System.Diagnostics;

namespace SignatureReaderSpike;

/// <summary>
/// Measures the reader on the labelled corpus. Each frame is read with templates built from every
/// OTHER frame, so a frame never recognises its own pixels.
/// </summary>
internal static class Evaluation
{
    /// <summary>Band offsets, in reference pixels, that keep every corpus badge inside the band.</summary>
    private static readonly (double X, double Y)[] Shifts = [(-Band.MarginX, 0), (Band.MarginX, 0), (0, -Band.MarginY), (0, Band.MarginY)];

    private const int TimingRuns = 200;

    /// <returns>
    /// 0 when no wrong value was accepted (band centred or shifted) and no badge-free frame was
    /// accepted; 1 when one was; 2 when the badge-free check could not run because it found no frame.
    /// </returns>
    public static int Run(string corpusDirectory, string? negativesDirectory, string channels)
    {
        var items = Corpus.Load(corpusDirectory).Where(item => item.Value is not null).ToList();
        var samples = CollectSamples(items, channels);
        Console.WriteLine($"training samples: {samples.Count} from {samples.Select(s => s.Frame).Distinct().Count()}/{items.Count} frames");

        int found = 0, right = 0, covered = 0, acceptedWrong = 0, shiftRight = 0, shiftWrong = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var templates = Build(samples.Where(s => s.Frame != i));
            var truth = item.Value!.Value.ToString();
            // "Covered": some other frame supplies a template for every digit of this value, in this font.
            var isCovered = truth.All(ch => templates.Mean(item.Font, ch - '0') is not null);
            var read = Reader.Read(item.BandAt(), item.Scale, templates, channels);
            var accepted = Gate.Accepts(read);
            var ok = accepted && read!.Value == item.Value;
            found += read is null ? 0 : 1;
            covered += isCovered ? 1 : 0;
            right += ok ? 1 : 0;
            acceptedWrong += accepted && !ok ? 1 : 0;

            var shifted = 0;
            foreach (var (x, y) in Shifts)
            {
                var moved = Reader.Read(item.BandAt(x, y), item.Scale, templates, channels);
                if (!Gate.Accepts(moved))
                    continue;
                if (moved!.Value == item.Value)
                    shifted++;
                else
                    shiftWrong++;
            }

            shiftRight += shifted;
            var verdict = ok ? "OK      " : accepted ? "WRONG   " : isCovered ? "rejected" : "no-templ";
            Console.WriteLine($"{i + 1,2} {item.Name,-38} {item.Value,6} {item.Font} h {read?.Badge.DigitHeight,2} read {read?.Text ?? "(none)",-8} {verdict} "
                + $"mean {read?.MeanScore:0.00} worst {read?.MinScore:0.00} margin {read?.MinMargin:0.00} shifted {shifted}/{Shifts.Length}");
        }

        var all = Build(samples);
        var negativesAccepted = Negatives(negativesDirectory, all, channels, out var negatives);

        Console.WriteLine();
        Console.WriteLine($"badge found          {found}/{items.Count}");
        Console.WriteLine($"value right          {right}/{items.Count} ({covered} frames have every digit covered by other frames)");
        Console.WriteLine($"wrong value accepted {acceptedWrong} centred, {shiftWrong} shifted");
        Console.WriteLine($"shifted band right   {shiftRight}/{right * Shifts.Length} ({Shifts.Length} shifts of each of the {right} frames read centred)");
        Console.WriteLine($"badge-free accepted  {negativesAccepted}/{negatives}");
        Console.WriteLine($"template coverage    {all.Coverage}  (font:digits)");
        Time(items, all, channels);
        if (acceptedWrong + shiftWrong + negativesAccepted > 0)
            return 1;
        if (negatives > 0)
            return 0;
        // A missing folder must not look like a pass: the default path only resolves from the repository root.
        Console.Error.WriteLine($"No badge-free frame found in '{negativesDirectory}': that check did not run.");
        return 2;
    }

    /// <summary>
    /// Segmentation only, no templates: a frame whose cell count matches its label hands one sample per digit.
    /// </summary>
    private static List<(int Frame, string Font, int Digit, float[] Feature)> CollectSamples(List<CorpusItem> items, string channels)
    {
        var samples = new List<(int Frame, string Font, int Digit, float[] Feature)>();
        for (var i = 0; i < items.Count; i++)
        {
            var truth = items[i].Value!.Value.ToString();
            var read = Reader.Read(items[i].BandAt(), items[i].Scale, null, channels);
            if (read is null || read.Cells.Count != truth.Length)
                continue;
            for (var c = 0; c < truth.Length; c++)
                samples.Add((i, items[i].Font, truth[c] - '0', read.Cells[c].Feature));
        }

        return samples;
    }

    private static Templates Build(IEnumerable<(int Frame, string Font, int Digit, float[] Feature)> samples)
    {
        var templates = new Templates();
        foreach (var s in samples)
            templates.Add(s.Font, s.Digit, s.Feature);
        return templates;
    }

    /// <summary>Full frames with no badge anywhere: any accepted reading is a false positive.</summary>
    private static int Negatives(string? directory, Templates templates, string channels, out int count)
    {
        count = 0;
        if (directory is null || !Directory.Exists(directory))
            return 0;
        var accepted = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.png").Order())
        {
            count++;
            var frame = Bgra.Load(path);
            var band = Band.Of(frame.Width, frame.Height);
            var read = Reader.Read(frame.Crop(band.X, band.Y, band.Width, band.Height), Band.Scale(frame.Height), templates, channels);
            if (!Gate.Accepts(read))
                continue;
            accepted++;
            Console.WriteLine($"badge-free frame accepted: {Path.GetFileName(path)} read {read!.Text} mean {read.MeanScore:0.00} worst {read.MinScore:0.00}");
        }

        return accepted;
    }

    private static void Time(List<CorpusItem> items, Templates templates, string channels)
    {
        if (items.FirstOrDefault(item => item.FrameHeight == 1440) is not { } item)
            return;
        var band = item.BandAt();
        Reader.Read(band, item.Scale, templates, channels);
        var watch = Stopwatch.StartNew();
        for (var run = 0; run < TimingRuns; run++)
            Reader.Read(band, item.Scale, templates, channels);
        Console.WriteLine($"time per read        {watch.Elapsed.TotalMilliseconds / TimingRuns:0.00} ms ({band.Width}x{band.Height} band, "
            + $"{channels.Length} channel x {Reader.Thresholds.Length} thresholds, unoptimised)");
    }
}
