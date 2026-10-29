using ItemTrackerPlugin.Screen;
using Ocrx.Contracts;

namespace ItemTrackerPlugin.Tests;

public sealed class HoverDetectorTests
{
    private static readonly (byte B, byte G, byte R) Hovered = (68, 59, 45);
    private static readonly (byte B, byte G, byte R) Bright = (233, 226, 184);
    private static readonly (byte B, byte G, byte R) Resting = (25, 17, 8);

    /// <summary>A band sampler at <paramref name="scale"/> times the reference size, with a one-pixel
    /// horizontal line at reference row <paramref name="lineY"/> from x0 to x1 (reference space).</summary>
    private static PixelPatchSampler Band(int band, double scale, int x0 = 0, int x1 = 0, int lineY = 10,
        (byte B, byte G, byte R) color = default)
    {
        var rect = GridLayout.BorderBand(band);
        var width = (int)(rect.Width * scale);
        var height = (int)(rect.Height * scale);
        var bgra = new byte[width * 4 * height];
        for (var i = 0; i < bgra.Length; i += 4) bgra[i + 3] = 255;

        if (x1 > x0)
        {
            var y = (int)(lineY * scale);
            for (var x = (int)((x0 - rect.X) * scale); x < (int)((x1 - rect.X) * scale); x++)
            {
                var i = y * width * 4 + x * 4;
                bgra[i] = color.B;
                bgra[i + 1] = color.G;
                bgra[i + 2] = color.R;
            }
        }

        return new PixelPatchSampler(bgra, width * 4, width, height, (int)(rect.X * scale), (int)(rect.Y * scale));
    }

    private static PixelPatchSampler Empty(int band, double scale) => Band(band, scale);

    private static IReadOnlyList<PixelPatchSampler?> Bands(double scale, params (int Band, int X0, int X1, (byte, byte, byte) Color)[] lines)
    {
        var bands = new List<PixelPatchSampler?>();
        for (var b = 0; b <= GridLayout.Rows; b++)
        {
            var line = lines.FirstOrDefault(l => l.Band == b);
            bands.Add(line.X1 > 0
                ? Band(b, scale, line.X0, line.X1, color: (line.Color.Item1, line.Color.Item2, line.Color.Item3))
                : Empty(b, scale));
        }

        return bands;
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void A_border_run_in_two_consecutive_bands_is_the_hovered_tile(double scale)
    {
        // The wide tile of the corpus: top edge in band 3, bottom edge in band 4, x 2032..2306.
        var tile = HoverDetector.Find(Bands(scale, (3, 2032, 2306, Hovered), (4, 2032, 2306, Hovered)));

        Assert.Equal(new GridTile(3, 1, 2), tile);
    }

    [Fact]
    public void The_brightest_border_the_game_pulses_to_counts()
    {
        var tile = HoverDetector.Find(Bands(1.0, (1, 1886, 2011, Bright), (2, 1886, 2011, Bright)));

        Assert.Equal(new GridTile(1, 0, 1), tile);
    }

    [Fact]
    public void A_single_run_is_not_a_border()
    {
        Assert.Null(HoverDetector.Find(Bands(1.0, (3, 2032, 2306, Hovered))));
    }

    [Fact]
    public void Two_runs_of_different_extent_are_not_a_tile()
    {
        Assert.Null(HoverDetector.Find(Bands(1.0, (3, 1886, 2011, Hovered), (4, 2032, 2306, Hovered))));
    }

    [Fact]
    public void A_resting_border_is_ignored()
    {
        Assert.Null(HoverDetector.Find(Bands(1.0, (3, 2032, 2306, Resting), (4, 2032, 2306, Resting))));
    }

    [Fact]
    public void A_run_wider_than_two_slots_is_a_panel_edge()
    {
        Assert.Null(HoverDetector.Find(Bands(1.0, (3, 1878, 2466, Hovered), (4, 1878, 2466, Hovered))));
    }

    [Fact]
    public void A_short_run_is_an_icon_not_an_edge()
    {
        Assert.Null(HoverDetector.Find(Bands(1.0, (3, 2032, 2100, Hovered), (4, 2032, 2100, Hovered))));
    }

    [Fact]
    public void Bands_that_did_not_read_find_nothing()
    {
        Assert.Null(HoverDetector.Find(Enumerable.Repeat<PixelPatchSampler?>(null, GridLayout.Rows + 1).ToList()));
    }

    [Fact]
    public void Band_pixels_fit_the_wire_budget_at_4k()
    {
        // 4K is 1.5x the reference height; the payload scales with the area.
        foreach (var rect in Enumerable.Range(0, GridLayout.Rows + 1).Select(GridLayout.BorderBand))
            Assert.True(WireLimits.FitsPixelBudget((uint)(rect.Width * 1.5), (uint)(rect.Height * 1.5)));
    }
}
