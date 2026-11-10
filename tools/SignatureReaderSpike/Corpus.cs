namespace SignatureReaderSpike;

/// <summary>Reads <c>labels.tsv</c>: file, value (or ?), font tag, frame width, frame height.</summary>
internal static class Corpus
{
    public const string LabelsFile = "labels.tsv";

    public static List<CorpusItem> Load(string directory)
    {
        var items = new List<CorpusItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(Path.Combine(directory, LabelsFile)))
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                continue;
            var parts = line.Split('\t');
            if (parts.Length != 5)
                throw new FormatException($"Expected 5 tab-separated fields in {LabelsFile}: '{line}'");
            if (!seen.Add(parts[0]))
                throw new FormatException($"{parts[0]} is listed twice in {LabelsFile}.");
            int? value = parts[1] == "?" ? null : int.Parse(parts[1]);
            // A labelled value with an unknown font would become a font of its own in the templates.
            if (value is not null && parts[2] is not ("c" or "w"))
                throw new FormatException($"{parts[0]} has a value but its font is '{parts[2]}': use c (condensed) or w (wide).");
            var crop = Bgra.Load(Path.Combine(directory, parts[0]));
            items.Add(new CorpusItem(Path.GetFileNameWithoutExtension(parts[0]), value, parts[2], int.Parse(parts[3]), int.Parse(parts[4]), crop));
        }

        return items;
    }
}
