using Ocrx.Contracts;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// <see cref="CounterReader"/>: what a read of the counter ROI is. The strings and geometry are the ones from the
/// live run of 2026-11-01 — the false "7,140" (real value 17,140) emitted when the rect's left edge sliced the
/// first digit, the 18 px "40" that was locked, and the distance line under the badge ("15.9km", "15,9km",
/// "IS.9krn", "fknmYD100") that was acquired and followed.
/// </summary>
public class CounterReaderTests
{
    /// <summary>The counter rect the reads below are taken in: 80x44 at (1260,450), the calibrated one widened for "17,200".</summary>
    private static readonly NumberBox Rect = new(1260, 450, 80, 44);

    private static (string Text, int X, int Y, int W, int H) Word(string text, int x, int y, int w, int h = 15) => (text, x, y, w, h);

    /// <summary>One OCR result at scale 1 for <see cref="Rect"/>, the words given in frame pixels, one line each.</summary>
    private static OcrRegionResult Read(params (string Text, int X, int Y, int W, int H)[][] lines)
        => new(string.Join('\n', lines.Select(l => string.Join(' ', l.Select(w => w.Text)))),
            lines.Select(l => new OcrLineInfo(string.Join(' ', l.Select(w => w.Text)),
                l.Select(w => new OcrWordInfo(w.Text, new RectF(w.X - Rect.X, w.Y - Rect.Y, w.W, w.H))).ToList())).ToList(),
            1.0, (uint)Rect.X, (uint)Rect.Y, (uint)Rect.Width, (uint)Rect.Height);

    private static CounterReading Of(OcrRegionResult read, NumberBox? history = null, Func<double, bool>? known = null)
        => CounterReader.Read(read, known, 1.0, history);

    [Theory]
    [InlineData("17,200")]
    [InlineData("3,600")]
    [InlineData("3600")]
    [InlineData("2.000")]   // the comma read as a period: the counter crop holds nothing but the number
    [InlineData("17/200")]
    public void A_read_that_is_only_the_number_is_the_reading_unchanged(string raw)
    {
        var reading = Of(Read([Word(raw, 1275, 465, 56)]));

        Assert.Equal(raw, reading.Text);
        Assert.True(reading.HasNumber);
        Assert.Equal(new NumberBox(1275, 465, 56, 15), reading.Box);
        Assert.Equal(CounterCut.None, reading.Cut);
    }

    /// <summary>A counter widened to follow the badge takes in the pin icon, read as a stray "9".</summary>
    [Fact]
    public void The_pin_icon_beside_the_number_is_left_out()
    {
        var reading = Of(Read([Word("9", 1262, 465, 9, 14), Word("17,200", 1285, 465, 56)]));

        Assert.Equal("17,200", reading.Text);
        Assert.Equal(1285, reading.Box!.Value.X);
    }

    /// <summary>The distance line under the number is text, not a reading, and the number beside it is still found.</summary>
    [Fact]
    public void A_distance_line_under_the_number_is_left_out()
    {
        var reading = Of(Read(
            [Word("17,200", 1275, 463, 56)],
            [Word("UNKNOWN", 1270, 484, 70, 12)],
            [Word("15.9km", 1275, 491, 50, 13)]));

        Assert.Equal("17,200", reading.Text);
        Assert.Equal(new NumberBox(1275, 463, 56, 15), reading.Box);
    }

    /// <summary>The words acquired, followed or observed in the live run, and the unit variants OCR made of "km".
    /// Letters attached to digits, a decimal point, or a mixed token never make a reading.</summary>
    [Theory]
    [InlineData("15.9km")]
    [InlineData("15,9km")]     // the dangerous one: a comma where the decimal point is
    [InlineData("15.9krn")]
    [InlineData("15.9kin")]
    [InlineData("15.9kfrn")]
    [InlineData("15.9gkm")]
    [InlineData("15.9gkrn")]
    [InlineData("15.9krTl")]
    [InlineData("IS.9krn")]
    [InlineData("IS,9km")]
    [InlineData("5.9km")]
    [InlineData("fknmYD100")]
    [InlineData("1,500km")]
    [InlineData("15.9")]
    [InlineData("40")]
    [InlineData(", 240")]
    [InlineData("240")]
    [InlineData("140")]
    public void The_distance_line_and_fragments_are_never_a_reading(string raw)
    {
        var width = raw.Length * 9;
        var reading = Of(Read([Word(raw, 1270, 491, width)]), known: v => true);

        Assert.Equal("", reading.Text);
        Assert.False(reading.HasNumber);
        Assert.Null(reading.Box);
        Assert.Equal(raw, reading.Raw);
    }

    /// <summary>A unit as its own word beside the number: "1,500" "km" is a distance, not a signature.</summary>
    [Theory]
    [InlineData("km")]
    [InlineData("krn")]
    [InlineData("K")]
    [InlineData("m")]
    public void A_unit_word_right_after_the_number_disqualifies_it(string unit)
    {
        var reading = Of(Read([Word("1,500", 1270, 491, 46), Word(unit, 1320, 491, 18)]));

        Assert.False(reading.HasNumber);
        Assert.Equal("", reading.Text);
    }

    /// <summary>The 18 px "40" the tracker was locked on: nothing that narrow is a signature (a "2,000" is 46).</summary>
    [Fact]
    public void A_word_much_narrower_than_a_number_is_never_the_number()
    {
        Assert.False(Of(Read([Word("2,000", 1275, 465, 18, 12)])).HasNumber);
        Assert.True(Of(Read([Word("2,000", 1275, 465, 46)])).HasNumber);
    }

    [Fact]
    public void A_bare_run_of_digits_is_a_reading_only_when_it_is_on_the_table()
    {
        var word = Word("17200", 1275, 465, 46);

        Assert.True(Of(Read([word]), known: v => v == 17200).HasNumber);   // as a whole read: parses, a whole number
        Assert.False(Of(Read([Word("17200", 1275, 465, 46), Word("km", 1325, 465, 18)]), known: v => false).HasNumber);
    }

    // ---- the 7,140 case ----

    /// <summary>
    /// The live failure. The rect's left edge sliced the "1" of "17,140"; OCR read "7,140", a valid number
    /// (Borase x2), its box starting against the edge. It is cut, and says so.
    /// </summary>
    [Fact]
    public void A_number_against_the_left_edge_is_cut_and_has_no_text()
    {
        var reading = Of(Read([Word("7,140", 1261, 465, 47)]));

        Assert.True(reading.IsCut);
        Assert.Equal(CounterCut.Left, reading.Cut);
        Assert.Equal("", reading.Text);
        Assert.False(reading.HasNumber);
        Assert.Equal(1261, reading.Box!.Value.X);
        Assert.Equal("7,140", reading.Raw);
    }

    /// <summary>The whole number, margins at the calibrated 9 and at the smallest the safe zone allows, is not cut.</summary>
    [Theory]
    [InlineData(1269)]  // 9 px from the edge: where the calibration has it
    [InlineData(1268)]  // 8: the safe zone's floor
    [InlineData(1262)]  // 2: close, but a whole number with nothing followed to compare it with
    public void A_whole_number_with_a_margin_is_not_cut(int x)
        => Assert.False(Of(Read([Word("17,140", x, 465, 56)])).IsCut);

    [Theory]
    [InlineData(1260)]
    [InlineData(1261)]
    public void A_number_touching_the_left_edge_is_cut_when_nothing_is_followed(int x)
        => Assert.Equal(CounterCut.Left, Of(Read([Word("7,140", x, 465, 47)])).Cut);

    /// <summary>With a number followed, a whole number at the edge is not a cut: that is a number moving into the
    /// edge, which the tracker answers by moving the rect, not by dropping the read.</summary>
    [Fact]
    public void A_whole_number_against_the_edge_is_not_cut_when_it_is_as_wide_as_the_one_followed()
        => Assert.False(Of(Read([Word("17,140", 1261, 465, 56)]), new NumberBox(1271, 465, 56, 15)).IsCut);

    /// <summary>
    /// A whole glyph can be gone with the box still a few pixels in from the edge (measured: with the edge 4 px
    /// into the "1" the box of "7,200" starts 5-6 px from it). With the number being followed — 56 wide —
    /// a box that is 9 narrower and near the edge has lost a digit; the same box far from the edge is a different
    /// number that really is shorter.
    /// </summary>
    [Fact]
    public void A_narrower_number_near_the_edge_has_lost_a_digit_when_the_number_followed_was_wider()
    {
        var followed = new NumberBox(1269, 465, 56, 15);

        var cut = Of(Read([Word("7,140", 1266, 465, 47)]), followed);
        Assert.Equal(CounterCut.Left, cut.Cut);

        // The same width further in is just a shorter number; the same offset at the same width is the same number.
        Assert.False(Of(Read([Word("7,140", 1275, 465, 47)]), followed).IsCut);
        Assert.False(Of(Read([Word("17,140", 1266, 465, 56)]), followed).IsCut);
    }

    /// <summary>A cut on the right never reads as a valid number; with a number followed it is still recognised, so the rect can be widened.</summary>
    [Fact]
    public void A_fragment_reaching_the_right_edge_is_a_right_cut_when_it_continues_a_followed_number()
    {
        var followed = new NumberBox(1269, 465, 56, 15);

        var reading = Of(Read([Word("17,20", 1269, 465, 70)]), followed);
        Assert.Equal(CounterCut.Right, reading.Cut);
        Assert.Equal("", reading.Text);

        Assert.Equal(CounterCut.None, Of(Read([Word("17,20", 1269, 465, 70)])).Cut); // nothing followed: just text
        Assert.Equal(CounterCut.None, Of(Read([Word("17,20", 1269, 465, 50)]), followed).Cut); // not at the edge
    }
}
