using System.Text.Json;

namespace SignatureOcrSweep;

/// <summary>Reads <c>manifest.json</c> from a corpus directory, in the parity corpus's shape
/// (<c>{ "frames": [ { "file", "signature", ... } ] }</c>) plus the optional fields on <see cref="ManifestFrame"/>.
/// Entries without <c>signature</c> load (ore-name entries of the parity corpus do), and read as unlabelled.</summary>
public static class CorpusManifest
{
    public const string FileName = "manifest.json";

    public static IReadOnlyList<ManifestFrame> Load(string corpusDir)
    {
        var path = Path.Combine(corpusDir, FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"no {FileName} in corpus directory '{corpusDir}'", path);
        return Parse(File.ReadAllText(path));
    }

    public static IReadOnlyList<ManifestFrame> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Array)
            throw new FormatException("manifest must contain a 'frames' array");

        var result = new List<ManifestFrame>();
        foreach (var frame in frames.EnumerateArray())
        {
            var file = frame.TryGetProperty("file", out var f) ? f.GetString() : null;
            if (string.IsNullOrWhiteSpace(file))
                throw new FormatException("manifest frame 'file' is required");

            result.Add(new ManifestFrame(
                file,
                Number(frame, "signature"),
                frame.TryGetProperty("configuration", out var c) ? c.GetString() ?? "unlabelled" : "unlabelled",
                Number(frame, "verticalFov"),
                frame.TryGetProperty("lossy", out var l) && l.ValueKind == JsonValueKind.True,
                frame.TryGetProperty("note", out var n) ? n.GetString() : null));
        }

        var duplicate = result.GroupBy(f => f.File, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new FormatException($"manifest lists '{duplicate.Key}' more than once; a frame needs exactly one entry");

        return result;
    }

    private static double? Number(JsonElement frame, string name) =>
        frame.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
}
