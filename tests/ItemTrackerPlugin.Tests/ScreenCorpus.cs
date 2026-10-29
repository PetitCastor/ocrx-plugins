using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ItemTrackerPlugin.Tests;

/// <summary>
/// A kiosk frame corpus under <c>tests/fixtures/corpus</c>, read from the source tree. Frames are
/// replayed in file-name order, so the engine's frame sequence number is the file's one-based
/// position; the capture time is in the name and a replay tick does not carry it.
/// </summary>
internal sealed partial class ScreenCorpus
{
    public sealed record Expected(
        string Panel, string Name, string? ItemType, int? Row, int? Column, int? Quantity, int? Page, int? Pages);

    public sealed record Frame(int Seq, string File, DateTime CapturedAtLocal, Expected? Observation);

    private ScreenCorpus(string directory, IReadOnlyList<Frame> frames)
    {
        Directory = directory;
        Frames = frames;
    }

    public string Directory { get; }
    public IReadOnlyList<Frame> Frames { get; }

    public static ScreenCorpus Load(string name, [CallerFilePath] string here = "")
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "fixtures", "corpus", name));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));

        var frames = new List<Frame>();
        foreach (var entry in manifest.RootElement.GetProperty("frames").EnumerateArray())
        {
            var file = entry.GetProperty("file").GetString()!;
            Expected? expected = null;
            if (entry.TryGetProperty("observation", out var o) && o.ValueKind == JsonValueKind.Object)
            {
                expected = new Expected(
                    o.GetProperty("panel").GetString()!,
                    o.GetProperty("name").GetString()!,
                    Str(o, "itemType"), Int(o, "row"), Int(o, "column"), Int(o, "quantity"), Int(o, "page"), Int(o, "pages"));
            }

            frames.Add(new Frame(frames.Count + 1, file, TimeOf(file), expected));
        }

        return new ScreenCorpus(directory, frames);
    }

    /// <summary><c>frame_20261028_152807_868.png</c> is 2026-10-28 15:28:07.868 on the capturing machine.</summary>
    public static DateTime TimeOf(string fileName)
    {
        var m = FrameName().Match(Path.GetFileName(fileName));
        if (!m.Success) throw new FormatException($"not a frame file name: {fileName}");

        return DateTime.ParseExact(
            $"{m.Groups["d"].Value}{m.Groups["t"].Value}{m.Groups["ms"].Value}", "yyyyMMddHHmmssfff",
            CultureInfo.InvariantCulture);
    }

    private static string? Str(JsonElement o, string key) => o.TryGetProperty(key, out var v) ? v.GetString() : null;

    private static int? Int(JsonElement o, string key) => o.TryGetProperty(key, out var v) ? v.GetInt32() : null;

    [GeneratedRegex(@"^frame_(?<d>\d{8})_(?<t>\d{6})_(?<ms>\d{3})\.png$", RegexOptions.CultureInvariant)]
    private static partial Regex FrameName();
}
