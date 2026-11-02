using System.Text.RegularExpressions;
using Ocrx.Contracts;

namespace SignaturePlugin;

/// <summary>
/// Decides what a read of the counter ROI is: the signature number (and where), a fragment of it that an edge
/// of the rect cut, or nothing credible. Everything that observes, locks onto or follows the number goes
/// through here, so none of them can be laxer than the others.
/// </summary>
/// <remarks>
/// Two live failures, 2026-11-01, are why this is strict.
/// <list type="bullet">
/// <item><b>A fragment is a valid number.</b> The counter's left edge sliced the first digit of "17,140"; OCR
/// read "7,140", a perfectly well-formed signature (Borase x2) that was emitted. A cut on the left shows as the
/// word's box sitting near the rect's edge, but how near does not settle it. Measured on four cockpit frames
/// with the edge moved across the digits: once the "1" is cut away the box of "7,200" starts 0-6 px from the
/// edge (5-6 px with the edge 4 px into the glyph), and the whole number reads correctly with the box 0-2 px
/// from the edge (the edge 2 px into the glyph's side bearing) as well as with margins of 2-14. So against
/// the edge alone only a box within <see cref="TouchingPx"/> is called cut, and only when there is nothing to
/// compare it with — it sometimes loses a correct read to be sure of dropping a fragment. With a number already
/// followed the width settles it: a box within <see cref="LeftSafePx"/> of the edge that is also
/// <see cref="LostGlyphPx"/> narrower than the number was has lost a digit, wherever its edge is (and a whole
/// number against the edge is not a cut, which is what a number moving into the rect's edge looks like). A cut on
/// the right can never read as a valid number (the thousands groups are three digits), but it is how the rect
/// finds out it is too narrow, so it is reported too.</item>
/// <item><b>The lax fallback.</b> When no word was a comma-grouped number, the old code accepted the whole read
/// if the <em>parser</em> would — and the parser takes "15.9km" as 15.9, "40" as 40, and a number with a unit
/// after it — then followed the digit-bearing words, whatever they were: the distance line under the badge
/// ("15.9km", "15,9km", "IS.9krn") and a mixed token ("fknmYD100") were acquired and followed. Now a number is
/// a comma-grouped word (letters attached, a decimal point or a unit beside it all disqualify it), or a read
/// with no letters that parses to a whole number of a thousand or more, or a bare run of 4-6 digits that is on
/// the signature table. Nothing else is a reading, a position or an observation.</item>
/// </list>
/// </remarks>
internal static partial class CounterReader
{
    /// <summary>The narrowest word taken for the number, reference px at FOV zoom 1: "2,000" is 46 wide and no
    /// signature has fewer than five characters, so an 18 px "40" is a fragment.</summary>
    public const double MinWidth = 32;

    /// <summary>With no number followed to compare with, a box starting closer than this to the rect's left edge
    /// (reference px) is taken for a fragment.</summary>
    public const double TouchingPx = 2;

    /// <summary>A number followed already that starts closer to the left edge than this, and is narrower by
    /// <see cref="LostGlyphPx"/>, has lost its first glyph: the calibrated left margin is 9, the safe zone 8.</summary>
    public const double LeftSafePx = 8;

    public const double LostGlyphPx = 5;

    /// <summary>A fragment reaching to within this of the rect's right edge is cut on the right.</summary>
    public const double RightEdgePx = 4;

    [GeneratedRegex(@"^\d{4,6}$")]
    private static partial Regex Bare();

    /// <param name="read">The counter's Detailed result.</param>
    /// <param name="isKnown">Whether a value is on the signature table; null means none is.</param>
    /// <param name="scale">Frame pixels per reference pixel at the current zoom: the thresholds above scale by it.</param>
    /// <param name="history">The number as last followed (frame pixels), null when none is.</param>
    public static CounterReading Read(OcrRegionResult read, Func<double, bool>? isKnown, double scale, NumberBox? history)
    {
        var raw = read.Text.Trim();
        var rect = new NumberBox((int)read.RoiX, (int)read.RoiY, (int)read.RoiWidth, (int)read.RoiHeight);
        var minWidth = MinWidth * scale;

        string? text = null;
        NumberBox? box = null;

        var number = SignatureCandidates.Find(read, isKnown)
            .Where(c => c.Box.Width >= minWidth)
            .OrderByDescending(c => c.Known)
            .ThenBy(c => c.Box.X)
            .FirstOrDefault();
        if (number is not null)
        {
            box = number.Box;
            // A read that is only the number goes through untouched; one with the pin icon or a second line in it
            // does not parse as it stands, and the word alone is what was read.
            text = IsWholeNumber(raw) ? raw : number.Text;
        }
        else if (IsWholeNumber(raw))
        {
            // OCR dropped the comma ("3600"), or wrote it as a period ("2.000"): no letters, a whole number.
            var digits = read.AllWords().Where(w => w.Text.Count(char.IsAsciiDigit) >= 2).ToList();
            text = raw;
            if (digits.Count > 0)
                box = FrameBox(read, digits.Select(w => w.CropRect).Aggregate(Union));
            if (box is { } b && b.Width < minWidth)
                (text, box) = (null, null);
        }
        else
        {
            var bare = read.AllWords()
                .Select(w => (Word: w, Ok: Bare().IsMatch(w.Text.Trim()) && isKnown?.Invoke(double.Parse(w.Text.Trim(), System.Globalization.CultureInfo.InvariantCulture)) == true))
                .FirstOrDefault(x => x.Ok);
            if (bare.Ok && FrameBox(read, bare.Word.CropRect) is { } bb && bb.Width >= minWidth)
                (text, box) = (bare.Word.Text.Trim(), bb);
        }

        if (text is null || box is not { } found)
            return text is not null
                ? new CounterReading(text, null, CounterCut.None, raw)
                : RightCut(read, rect, scale, history, raw) ?? new CounterReading("", null, CounterCut.None, raw);

        var fromLeft = found.X - rect.X;
        var cut = history is { } h
            ? fromLeft < LeftSafePx * scale && found.Width < h.Width - LostGlyphPx * scale
            : fromLeft < TouchingPx * scale;
        return cut
            ? new CounterReading("", found, CounterCut.Left, raw)
            : new CounterReading(text, found, CounterCut.None, raw);
    }

    /// <summary>
    /// The read as a clean number: nothing but digits and separators, accepted by the parser, a whole number
    /// between the smallest and largest signature. "15.9" has a decimal point, "40" is too small, "15.9km" has a
    /// unit; none is a reading.
    /// </summary>
    private static bool IsWholeNumber(string raw)
        => raw.Length > 0
           && raw.All(c => char.IsAsciiDigit(c) || c is ',' or '.' or '/' or ' ')
           && SignatureParser.TryParse(raw, out var value)
           && value == Math.Floor(value)
           && value is >= SignatureCandidates.MinValue and <= SignatureCandidates.MaxValue;

    /// <summary>
    /// The last digits are outside the rect: some letter-free word with a digit in it reaches to its right edge,
    /// level with and starting where the number being followed does. Only with a number to compare it to — a lone
    /// piece of text at the edge of a widened rect is just text.
    /// </summary>
    private static CounterReading? RightCut(OcrRegionResult read, NumberBox rect, double scale, NumberBox? history, string raw)
    {
        if (history is not { } h)
            return null;

        var pieces = read.AllWords()
            .Where(w => w.Text.Any(char.IsAsciiDigit) && !w.Text.Any(char.IsLetter))
            .Select(w => FrameBox(read, w.CropRect))
            .ToList();
        if (pieces.Count == 0)
            return null;

        var union = pieces.Aggregate((a, b) => new NumberBox(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right) - Math.Min(a.X, b.X), Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Y, b.Y)));
        var reachesEdge = rect.Right - union.Right < RightEdgePx * scale;
        var sameLine = Math.Abs(union.CentreY - h.CentreY) <= 10 * scale && Math.Abs(union.X - h.X) <= LeftSafePx * scale;
        return reachesEdge && sameLine ? new CounterReading("", union, CounterCut.Right, raw) : null;
    }

    private static NumberBox FrameBox(OcrRegionResult read, RectF crop)
    {
        var (x0, y0) = read.ToFramePoint(crop.X, crop.Y);
        var (x1, y1) = read.ToFramePoint(crop.Right, crop.Bottom);
        return new NumberBox(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    private static RectF Union(RectF a, RectF b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new RectF(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }
}
