using System.Text.RegularExpressions;
using Ocrx.Contracts;

namespace SignaturePlugin;

/// <summary>
/// Picks the words in an OCR result that could be the RS signature number: digits grouped in
/// thousands, "2,000", "11,700", "111,700". Nothing here looks at colour or shape — only at what the
/// text says and where it sits — so it holds whatever the badge around the number looks like on a
/// given ship.
/// </summary>
/// <remarks>
/// The HUD is full of other text and numbers, and a false candidate is a lock on the wrong thing, so the
/// rule is strict about form and loose only about the single-glyph misreads a search tile's low OCR
/// scale produces. Measured on four 2560x1440 cockpit frames (2026-11-01), the words that must NOT
/// qualify include "15.9km" (read as <c>15.9krrt</c>), "90º" (read as <c>902</c>), "16.2km", "16.9K",
/// "10.2K", "4.3K", "88.5", "27m/s", "5.0 G", "0/ 20", "89%", "5.0km", the PWR/WPN/THR/SHLD strip, the
/// chat text and the "READY TO SCAN" panel. Those fail because they carry a unit, a decimal point, a
/// slash group or fewer than four digits; "11,700" and "17,200" pass because they are exactly a
/// one-to-three digit head followed by comma-and-three-digit groups.
/// <para>
/// A period is NOT taken for the thousands comma here, though <see cref="SignatureParser"/> does take it
/// when reading the counter crop: a search tile sees the whole HUD, and a power-MFD readout of "4.314"
/// (read from two of the four cockpit frames at several scales) is exactly a period-grouped number that
/// even lands on a table entry (4300, within tolerance). The comma OCR misreads as a period in one try it
/// reads correctly in another, so the cost is a retry, not a missed badge.
/// </para>
/// <para>
/// Nor is a run of digits with no separator ("4314"): it could be anything numeric on the HUD, and the
/// table is no filter for it — the table's 4300 entry tolerates "4314" too. Reading the comma as nothing is
/// rarer than reading it as a period, and like that miss it costs a retry at another scale, not the badge.
/// </para>
/// </remarks>
internal static partial class SignatureCandidates
{
    /// <summary>The smallest value taken as a signature (the lowest table entry, debris, is 2000).</summary>
    public const int MinValue = 1000;

    /// <summary>The largest: six figures covers any cluster total the table can derive.</summary>
    public const int MaxValue = 999_999;

    /// <summary>Largest horizontal gap, in text heights, across which adjacent words are read as one
    /// number ("17," next to "200"). The pin glyph sits about one height away from the digits and
    /// must not be absorbed — see the single-word preference in <see cref="Find"/>.</summary>
    private const double MergeGapHeights = 0.6;

    private const int MaxMergedWords = 3;

    [GeneratedRegex(@"^\d{1,3}(?:,\d{3})+$")]
    private static partial Regex Grouped();

    /// <summary>
    /// Whether <paramref name="text"/> reads as a signature value. <paramref name="isKnown"/> says
    /// whether a value is on the signature table (reported in <paramref name="known"/>; it never decides
    /// whether the text qualifies); null means no table.
    /// </summary>
    public static bool TryReadValue(string text, Func<double, bool>? isKnown, out double value, out bool known)
    {
        value = 0;
        known = false;

        var t = Fold(text.Trim());
        if (t.Length < 4 || t.Length > 9)
            return false;

        if (!Grouped().IsMatch(t))
            return false;

        if (!double.TryParse(t.Replace(",", ""), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out value)
            || value < MinValue || value > MaxValue)
            return false;

        known = isKnown?.Invoke(value) ?? false;
        return true;
    }

    /// <summary>
    /// Every candidate in a Detailed OCR result, with its box in frame pixels. Adjacent words are
    /// merged ("17," "200") only where no word in the run already stands alone as a candidate: the pin
    /// icon is often read as a stray "9" or "Q" one text-height to the left of the digits, and
    /// absorbing it would turn "17,200" into "917,200" with a box that includes the pin.
    /// </summary>
    public static IReadOnlyList<SignatureCandidate> Find(OcrRegionResult ocr, Func<double, bool>? isKnown)
    {
        var tile = new NumberBox((int)ocr.RoiX, (int)ocr.RoiY, (int)ocr.RoiWidth, (int)ocr.RoiHeight);
        var found = new List<SignatureCandidate>();

        foreach (var line in ocr.Lines)
        {
            var words = line.Words;
            var single = new bool[words.Count];
            for (var i = 0; i < words.Count; i++)
            {
                if (TryReadValue(words[i].Text, isKnown, out var value, out var known))
                {
                    single[i] = true;
                    if (UnitFollows(words, i))
                        continue;

                    found.Add(new SignatureCandidate(words[i].Text, value, known, ToFrame(ocr, words[i].CropRect), tile));
                }
            }

            for (var i = 0; i < words.Count; i++)
            {
                if (single[i])
                    continue;

                for (var n = 2; n <= MaxMergedWords && i + n <= words.Count; n++)
                {
                    if (!Adjacent(words[i + n - 2].CropRect, words[i + n - 1].CropRect))
                        break;

                    var run = words.Skip(i).Take(n).ToList();
                    if (Enumerable.Range(i, n).Any(k => single[k]))
                        break;

                    var text = string.Concat(run.Select(w => w.Text));
                    if (!TryReadValue(text, isKnown, out var value, out var known))
                        continue;

                    if (UnitFollows(words, i + n - 1))
                        break;

                    var union = run.Select(w => w.CropRect).Aggregate(Union);
                    found.Add(new SignatureCandidate(text, value, known, ToFrame(ocr, union), tile));
                    break;
                }
            }
        }

        return found;
    }

    /// <summary>OCR's usual single-glyph confusions in this HUD font, folded before matching. Only
    /// applied to a string that already has a real digit, so a word of letters is never turned into a
    /// number. A '/' is how Windows OCR often renders the thousands comma
    /// (<see cref="SignatureParser"/> folds it the same way).</summary>
    private static string Fold(string text)
    {
        if (!text.Any(char.IsAsciiDigit))
            return text;

        return string.Concat(text.Select(c => c switch
        {
            '/' => ',',
            'O' or 'o' => '0',
            'l' or 'I' or '|' => '1',
            'S' or 's' => '5',
            'B' => '8',
            'Z' or 'z' => '2',
            _ => c,
        }));
    }

    /// <summary>A short run of letters right after the number — "km", "krn", "K", "m" — makes it a distance or a
    /// quantity, whatever the digits look like: "1,500 km" is not a signature.</summary>
    private static bool UnitFollows(IReadOnlyList<OcrWordInfo> words, int index)
        => index + 1 < words.Count
           && words[index + 1].Text.Length is > 0 and <= 4
           && words[index + 1].Text.All(char.IsLetter)
           && Adjacent(words[index].CropRect, words[index + 1].CropRect);

    private static bool Adjacent(RectF left, RectF right)
    {
        var height = Math.Max(left.Height, right.Height);
        // Vertically loose: OCR draws the boxes of one line's words up to ~4 px apart (a comma sits at the
        // baseline, and "17" was seen 4 px below "200" in the same read of the real badge).
        return right.X - left.Right <= MergeGapHeights * height
            && Math.Abs(left.CenterY - right.CenterY) <= 0.8 * height;
    }

    private static RectF Union(RectF a, RectF b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new RectF(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }

    private static NumberBox ToFrame(OcrRegionResult ocr, RectF crop)
    {
        var (x0, y0) = ocr.ToFramePoint(crop.X, crop.Y);
        var (x1, y1) = ocr.ToFramePoint(crop.Right, crop.Bottom);
        return new NumberBox(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }
}
