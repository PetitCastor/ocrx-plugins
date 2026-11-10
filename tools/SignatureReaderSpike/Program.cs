using SignatureReaderSpike;

// Run from the repository root:
//   eval   [corpusDir] [negativesDir]   measure the reader on the labelled corpus (the default)
//   sheet  <out.png> [corpusDir]        contact sheet of every located badge, to label crops by eye
//   export <outDir> <frame.png>...      cut corpus crops out of full frames and print their label lines
const string DefaultCorpus = "tools/SignatureReaderSpike/corpus";
const string DefaultNegatives = "tests/fixtures/corpus/item-tracker-kiosk";
const string Channels = "r";

var mode = args.Length > 0 ? args[0] : "eval";
switch (mode)
{
    case "eval":
        return Evaluation.Run(Arg(1) ?? DefaultCorpus, Arg(2) ?? DefaultNegatives, Channels);

    case "sheet" when args.Length >= 2:
    {
        var rows = new List<(string Caption, Bgra? Crop)>();
        foreach (var item in Corpus.Load(Arg(2) ?? DefaultCorpus))
        {
            var band = item.BandAt();
            var extent = Reader.Read(band, item.Scale, null, Channels)?.Badge.Extent;
            var caption = $"{item.Name} {(item.Value is { } v ? v.ToString() : "?")} {item.Font}";
            rows.Add((caption, extent is { } e ? band.Crop(e.X - 3, e.Y - 3, e.Width + 6, e.Height + 6) : null));
        }

        ContactSheet.Save(args[1], rows);
        Console.WriteLine($"{rows.Count} crops, {rows.Count(r => r.Crop is not null)} with a badge -> {args[1]}");
        return 0;
    }

    case "export" when args.Length >= 3:
    {
        Directory.CreateDirectory(args[1]);
        foreach (var path in args.Skip(2))
        {
            var frame = Bgra.Load(path);
            var box = Band.WithMargin(frame.Width, frame.Height);
            var name = Path.GetFileNameWithoutExtension(path) + ".png";
            var target = Path.Combine(args[1], name);
            // Crop clamps to the frame, and a clamped crop would be read at the wrong offset later.
            if (box.X < 0 || box.Y < 0 || box.Right > frame.Width || box.Bottom > frame.Height)
            {
                Console.Error.WriteLine($"{name}: a {frame.Width}x{frame.Height} frame is too small to hold the band and its margin; skipped.");
                continue;
            }

            if (File.Exists(target))
            {
                Console.Error.WriteLine($"{name}: already in {args[1]}; skipped so an existing crop is never overwritten.");
                continue;
            }

            frame.Crop(box.X, box.Y, box.Width, box.Height).Save(target);
            // Value and font are for a human to fill in: ? is skipped by eval and shown by sheet.
            Console.WriteLine($"{name}\t?\t?\t{frame.Width}\t{frame.Height}");
        }

        return 0;
    }

    default:
        Console.Error.WriteLine("usage: eval [corpusDir] [negativesDir] | sheet <out.png> [corpusDir] | export <outDir> <frame.png>...");
        return 2;
}

string? Arg(int index) => args.Length > index ? args[index] : null;
