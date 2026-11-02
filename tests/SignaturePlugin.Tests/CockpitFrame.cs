using System.Text.Json;

namespace SignaturePlugin.Tests;

/// <summary>
/// The text Windows OCR found on one real 2560x1440 cockpit capture (<c>signature-frame_20261101_1043*.png</c>,
/// taken by the plugin's own manual-tick dump): every line holding a digit, read from 400x250 tiles at 2, 2.5
/// and 3x, boxes in frame pixels. The same physical text appears more than once with the variations the
/// three scales gave it ("17,200", "11,200", "9 17,200"). The real badge is near (1272, 465); everything else on the frame — MFD readouts,
/// "15.9km", "90º" read as "902", the power-MFD "4.314" — is a decoy.
/// </summary>
internal sealed record CockpitFrame(string Name, int Width, int Height, IReadOnlyList<IReadOnlyList<SceneWord>> Lines)
{
    /// <summary>Where the real badge's number sits in all four captures (the box of its words, give or take OCR wobble).</summary>
    public static readonly NumberBox Badge = new(1262, 450, 70, 30);

    /// <summary>
    /// The badge's number as the counter read it at scale 6 in each of the four captures (the reads the tracker
    /// locked on, from the real run of the tracker over the frames): the word and its box, which is what a counter
    /// at the calibrated rect sees. The recorded tile lines above carry the badge too, but read at scales 2-3 from
    /// tiles, where its box wobbles by several pixels; a test of where the counter sits wants the scale-6 box.
    /// </summary>
    public static readonly IReadOnlyList<(string Text, NumberBox Box)> RealBadges =
    [
        ("17,200", new NumberBox(1267, 463, 56, 15)),
        ("17,200", new NumberBox(1269, 462, 57, 15)),
        ("10,000", new NumberBox(1269, 463, 57, 15)),
        ("17,200", new NumberBox(1271, 462, 57, 15)),
    ];

    public static IReadOnlyList<CockpitFrame> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ocr", "cockpit-frames.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var frames = new List<CockpitFrame>();
        foreach (var f in doc.RootElement.GetProperty("frames").EnumerateArray())
        {
            var lines = new List<IReadOnlyList<SceneWord>>();
            var index = 0;
            foreach (var l in f.GetProperty("lines").EnumerateArray())
            {
                var line = index++;
                lines.Add(l.EnumerateArray()
                    .Select(w => new SceneWord(
                        w.GetProperty("t").GetString()!,
                        new NumberBox(w.GetProperty("x").GetInt32(), w.GetProperty("y").GetInt32(), w.GetProperty("w").GetInt32(), w.GetProperty("h").GetInt32()),
                        line))
                    .ToList());
            }

            frames.Add(new CockpitFrame(f.GetProperty("name").GetString()!, f.GetProperty("width").GetInt32(), f.GetProperty("height").GetInt32(), lines));
        }

        return frames;
    }

    /// <summary>Whether a box is the real badge's number rather than a decoy.</summary>
    public static bool IsBadge(NumberBox box) => Math.Abs(box.CentreX - Badge.CentreX) < 40 && Math.Abs(box.CentreY - Badge.CentreY) < 30;
}
