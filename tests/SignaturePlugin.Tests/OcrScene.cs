using Ocrx.Contracts;
using Ocrx.Contracts.Proto;
using Ocrx.Sdk;

namespace SignaturePlugin.Tests;

/// <summary>
/// A screen made of text, scanned through whatever ROI set the plugin currently holds the way the engine
/// would: a Detailed ROI comes back as the words inside it (with per-word boxes in upscaled-crop space, the
/// frame rect and the scale it was read at, exactly as <c>ScanLoop</c> fills them), and a Text ROI as the
/// words' text. The counter is a Detailed ROI, so what it reads is whatever words lie inside its rect. The set
/// pushed on one tick is what the next tick is scanned with, so motion, loss and the search are exercised end
/// to end instead of asserted on hand-fed results.
/// </summary>
/// <remarks>
/// OCR here is deterministic: a word wholly inside a ROI is always read. Real OCR is not (about 80% of
/// tiles read a number that is plainly there, and a miss repeats until the picture changes), so a test of
/// that behaviour uses <see cref="Blind"/>. A word cut by a tile edge is read the way OCR reads one — only
/// the visible part of its characters, with a box clipped to the tile — so an edge-handling bug shows up as
/// a lock on a fragment.
/// </remarks>
internal sealed class OcrScene
{
    private static readonly System.Reflection.MethodInfo FromProto =
        typeof(TickData).GetMethod("From", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(TickData), "From");

    private readonly List<SceneWord> _words = [];
    private ulong _seq = 1000;
    private int _nextLine;

    public OcrScene(int width = 2560, int height = 1440)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>When set, a read returns nothing while this says so for (frame rect, scale): a crop OCR is
    /// blind to at that scale and position.</summary>
    public Func<RoiRect, double, bool>? Blind { get; set; }

    public IReadOnlyList<SceneWord> Words => _words;

    public static OcrScene From(CockpitFrame frame)
    {
        var scene = new OcrScene(frame.Width, frame.Height);
        foreach (var line in frame.Lines)
            scene.AddLine(line.Select(w => (w.Text, w.Box)).ToArray());
        return scene;
    }

    public OcrScene Add(string text, NumberBox box) => AddLine([(text, box)]);

    public OcrScene AddLine(params (string Text, NumberBox Box)[] words)
    {
        var line = _nextLine++;
        foreach (var (text, box) in words)
            _words.Add(new SceneWord(text, box, line));
        return this;
    }

    public OcrScene Remove(Func<SceneWord, bool> predicate)
    {
        _words.RemoveAll(w => predicate(w));
        return this;
    }

    /// <summary>One tick: every ROI in <paramref name="rois"/> answered from the scene, its rect mapped to the
    /// frame the way the SDK maps it.</summary>
    public TickData Scan(IReadOnlyList<RoiSubscription> rois)
    {
        var tick = new TickResult
        {
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            FrameSeq = _seq++,
            FrameWidth = (uint)Width,
            FrameHeight = (uint)Height,
        };

        foreach (var roi in rois)
        {
            var frame = RoiScaler.ToFrame(roi.Rect, Width, Height);
            var result = new RoiResult { RoiId = roi.Id.Value, FrameRect = frame.ToProto() };
            if (roi.Kind == RoiKind.Text)
            {
                result.Kind = RoiResultKind.Text;
                result.EffectiveScale = roi.Scale;
                result.Text = Read(frame, roi.Scale).Text;
            }
            else
            {
                result.Kind = RoiResultKind.Detailed;
                result.FillFrom(Read(frame, roi.Scale));
            }

            tick.Results.Add(result);
        }

        return (TickData)FromProto.Invoke(null, [tick])!;
    }

    private OcrRegionResult Read(RoiRect tile, double scale)
    {
        var lines = new List<OcrLineInfo>();
        if (Blind?.Invoke(tile, scale) != true)
        {
            foreach (var line in _words.GroupBy(w => w.Line))
            {
                var words = line.Select(w => Clip(w, tile, scale)).Where(w => w is not null).Select(w => w!).ToList();
                if (words.Count > 0)
                    lines.Add(new OcrLineInfo(string.Join(' ', words.Select(w => w.Text)), words));
            }
        }

        return new OcrRegionResult(string.Join('\n', lines.Select(l => l.Text)), lines, scale, tile.X, tile.Y, tile.Width, tile.Height);
    }

    /// <summary>The word as the tile shows it: whole, clipped (its visible characters only), or absent.</summary>
    private static OcrWordInfo? Clip(SceneWord word, RoiRect tile, double scale)
    {
        var b = word.Box;
        long left = Math.Max(b.X, tile.X), right = Math.Min(b.Right, tile.X + tile.Width);
        long top = Math.Max(b.Y, tile.Y), bottom = Math.Min(b.Bottom, tile.Y + tile.Height);
        if (right <= left || bottom <= top)
            return null;

        var visible = (right - left) / (double)b.Width;
        var text = word.Text;
        if (visible < 0.999)
        {
            if (visible < 0.4)
                return null;

            var keep = Math.Max(1, (int)Math.Round(text.Length * visible));
            text = b.X < tile.X ? text[^keep..] : text[..keep];
        }

        return new OcrWordInfo(text, new Ocrx.Contracts.RectF((left - tile.X) * scale, (top - tile.Y) * scale, (right - left) * scale, (bottom - top) * scale));
    }
}
