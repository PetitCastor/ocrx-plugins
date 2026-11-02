using Ocrx.Contracts;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// <see cref="SignatureCandidates"/>: which OCR words count as a possible signature. The decoy strings are
/// what Windows OCR actually returned for the HUD of four real cockpit captures (2026-11-01) at the tracker's
/// search scales — misreads included ("15.9krrt", "902" for "90º") — and the last test replays every line of
/// those captures.
/// </summary>
public class SignatureCandidatesTests
{
    private static bool Reads(string text, out double value, Func<double, bool>? known = null)
        => SignatureCandidates.TryReadValue(text, known, out value, out _);

    [Theory]
    [InlineData("2,000", 2000)]
    [InlineData("11,700", 11700)]
    [InlineData("17,200", 17200)]
    [InlineData("10,000", 10000)]
    [InlineData("111,700", 111700)]
    [InlineData("999,999", 999999)]
    [InlineData("11,200", 11200)] // "17,200" misread at 2x: still a candidate, still where the badge is
    // '/' is how Windows OCR often draws this HUD font's comma (SignatureParser folds it the same way).
    [InlineData("17/200", 17200)]
    // Letter-for-digit confusions: a single glyph misread must not lose the badge.
    [InlineData("2,OOO", 2000)]
    [InlineData("I7,200", 17200)]
    [InlineData("1l,700", 11700)]
    [InlineData("S,000", 5000)]
    public void Accepts_a_signature_shaped_number(string text, double expected)
    {
        Assert.True(Reads(text, out var value));
        Assert.Equal(expected, value);
    }

    /// <summary>Every one of these is on the HUD next to the badge in the captures.</summary>
    [Theory]
    [InlineData("16.9K")]
    [InlineData("10.2K")]
    [InlineData("4.3K")]
    [InlineData("15.9km")]
    [InlineData("15.9krrt")]
    [InlineData("16.2km")]
    [InlineData("13.9km")]
    [InlineData("5.0km")]
    [InlineData("27m/s")]
    [InlineData("20m/S")]
    [InlineData("5.0")]
    [InlineData("88.5")]
    [InlineData("89.1")]
    [InlineData("0/20")]
    [InlineData("O/ 20")]
    [InlineData("20")]
    [InlineData("89%")]
    [InlineData("90º")]
    [InlineData("902")]       // "90º" as OCR reads it
    [InlineData("1444")]
    [InlineData("1211/5")]
    [InlineData("IS,9km")]
    [InlineData("12:30")]
    [InlineData("READY")]
    [InlineData("UNKNOWN")]
    [InlineData("STRONG")]
    [InlineData("PWR")]
    [InlineData("")]
    public void Rejects_hud_numbers_and_text(string text) => Assert.False(Reads(text, out _));

    /// <summary>A power MFD shows "4.314". The period must not be taken for a thousands comma here (the
    /// parser does so for the counter crop), because 4314 is within tolerance of the table's 4300 entry.</summary>
    [Theory]
    [InlineData("4.314")]
    [InlineData("2.000")]
    [InlineData("4314")]   // no separator at all: could be any count or distance on the HUD
    [InlineData("17200")]
    public void Does_not_take_a_period_or_nothing_for_the_thousands_comma(string text)
        => Assert.False(Reads(text, out _, known: _ => true));

    [Theory]
    [InlineData("1,00")]      // not groups of three
    [InlineData("1,0000")]
    [InlineData("0,200")]     // below any signature
    [InlineData("1,234,567")] // beyond any cluster total
    [InlineData("12,34,567")]
    public void Rejects_malformed_or_out_of_range_groups(string text) => Assert.False(Reads(text, out _));

    [Fact]
    public void Reports_whether_the_value_is_on_the_table()
    {
        Assert.True(SignatureCandidates.TryReadValue("2,000", v => v == 2000, out _, out var known));
        Assert.True(known);
        Assert.True(SignatureCandidates.TryReadValue("17,200", v => v == 2000, out _, out known));
        Assert.False(known);
    }

    /// <summary>A search tile at 2.5x on tile origin (100, 200): crop pixels are 2.5 per frame pixel.</summary>
    private static OcrRegionResult Result(params (string Text, double X, double Y, double W, double H)[][] lines)
        => new(string.Join('\n', lines.Select(l => string.Join(' ', l.Select(w => w.Text)))),
            lines.Select(l => new OcrLineInfo(string.Join(' ', l.Select(w => w.Text)),
                l.Select(w => new OcrWordInfo(w.Text, new RectF(w.X, w.Y, w.W, w.H))).ToList())).ToList(),
            2.5, 100, 200, 400, 250);

    [Fact]
    public void Maps_the_word_box_back_to_frame_pixels_using_the_applied_scale()
    {
        var found = Assert.Single(SignatureCandidates.Find(Result([("17,200", 500, 300, 140, 40)]), null));

        // 100 + 500/2.5, 200 + 300/2.5, 140/2.5 x 40/2.5
        Assert.Equal(new NumberBox(300, 320, 56, 16), found.Box);
        Assert.Equal(new NumberBox(100, 200, 400, 250), found.Tile);
        Assert.Equal(17200, found.Value);
    }

    /// <summary>The pin icon is often read as a stray "9" or "Q" one text height left of the digits; merging
    /// would make "917,200" with a box that includes the pin.</summary>
    [Theory]
    [InlineData("9")]
    [InlineData("Q")]
    [InlineData("Ql")]
    public void A_stray_pin_glyph_beside_the_number_is_not_absorbed(string pin)
    {
        var found = Assert.Single(SignatureCandidates.Find(
            Result([(pin, 100, 300, 30, 35), ("17,200", 150, 300, 140, 40)]), null));

        Assert.Equal("17,200", found.Text);
        Assert.Equal(160, found.Box.X); // 100 + 150/2.5
    }

    /// <summary>Windows OCR sometimes splits the number at the comma.</summary>
    [Fact]
    public void Adjacent_fragments_of_one_number_are_joined()
    {
        var found = Assert.Single(SignatureCandidates.Find(
            Result([("17,", 150, 300, 70, 40), ("200", 225, 300, 70, 40)]), null));

        Assert.Equal("17,200", found.Text);
        Assert.Equal(new NumberBox(160, 320, 58, 16), found.Box);
    }

    /// <summary>The real read of the badge in cockpit frame 2 at the widened counter: "17" 4 px below "200", the
    /// comma at the baseline, all one line. (Scale 1, so crop pixels are frame pixels from the rect's origin.)</summary>
    [Fact]
    public void The_number_split_in_three_with_misaligned_boxes_is_still_one_number()
    {
        var read = new OcrRegionResult("17 , 200",
            [new OcrLineInfo("17 , 200", [
                new OcrWordInfo("17", new RectF(10, 17, 17, 13)),
                new OcrWordInfo(",", new RectF(32, 25, 3, 4)),
                new OcrWordInfo("200", new RectF(39, 13, 27, 14))])],
            1.0, 1258, 448, 84, 52);

        var found = Assert.Single(SignatureCandidates.Find(read, null));

        Assert.Equal("17,200", found.Text);
        Assert.Equal(new NumberBox(1268, 461, 56, 17), found.Box);
    }

    [Fact]
    public void Distant_fragments_are_not_joined()
    {
        Assert.Empty(SignatureCandidates.Find(
            Result([("17,", 150, 300, 70, 40), ("200", 600, 300, 70, 40)]), null));
    }

    [Fact]
    public void Words_on_different_lines_are_not_joined()
    {
        Assert.Empty(SignatureCandidates.Find(
            Result([("17,", 150, 300, 70, 40)], [("200", 150, 360, 70, 40)]), null));
    }

    /// <summary>
    /// Every digit-bearing line Windows OCR produced over four real cockpit frames — about 130 distinct
    /// readings of MFD numbers, distances, "90º" and the rest, plus the real badge read as "17,200", "11,200"
    /// and "10,000" — yields candidates only at the badge.
    /// </summary>
    [Fact]
    public void The_real_cockpit_frames_yield_the_badge_and_nothing_else()
    {
        var frames = CockpitFrame.Load();
        Assert.Equal(4, frames.Count);

        foreach (var frame in frames)
        {
            var candidates = new List<SignatureCandidate>();
            foreach (var line in frame.Lines)
            {
                var words = line.Select(w => new OcrWordInfo(w.Text, new RectF(w.Box.X, w.Box.Y, w.Box.Width, w.Box.Height))).ToList();
                var ocr = new OcrRegionResult(
                    string.Join(' ', line.Select(w => w.Text)), [new OcrLineInfo(string.Join(' ', line.Select(w => w.Text)), words)],
                    1.0, 0, 0, (uint)frame.Width, (uint)frame.Height);
                candidates.AddRange(SignatureCandidates.Find(ocr, null));
            }

            Assert.NotEmpty(candidates);
            Assert.All(candidates, c => Assert.True(CockpitFrame.IsBadge(c.Box), $"{frame.Name}: '{c.Text}' at {c.Box} is a decoy"));
        }
    }
}
