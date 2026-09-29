using Xunit;

namespace SignatureOcrSweep.Tests;

public class PixelOpsTests
{
    // Bgra8 pixels: B, G, R, A.
    private static byte[] Pixels(params (byte B, byte G, byte R)[] p) =>
        p.SelectMany(x => new byte[] { x.B, x.G, x.R, 255 }).ToArray();

    [Fact]
    public void Red_collapse_copies_red_into_blue_and_green_like_the_engine()
    {
        var px = Pixels((10, 20, 200));

        PixelOps.Collapse(px, ChannelMode.Red);

        Assert.Equal(new byte[] { 200, 200, 200, 255 }, px);
    }

    [Fact]
    public void Other_channels_write_their_grey_to_all_three()
    {
        var lum = Pixels((0, 0, 255));
        var max = Pixels((10, 90, 30));

        PixelOps.Collapse(lum, ChannelMode.Luminance);
        PixelOps.Collapse(max, ChannelMode.MaxRgb);

        Assert.Equal(new byte[] { 76, 76, 76, 255 }, lum);
        Assert.Equal(new byte[] { 90, 90, 90, 255 }, max);
    }

    [Fact]
    public void Stretch_maps_the_range_to_full_scale_and_leaves_a_flat_image_alone()
    {
        var px = Pixels((50, 50, 50), (100, 100, 100), (150, 150, 150));
        var flat = Pixels((7, 7, 7), (7, 7, 7));

        PixelOps.Stretch(px);
        PixelOps.Stretch(flat);

        Assert.Equal(new byte[] { 0, 128, 255 }, new[] { px[2], px[6], px[10] });
        Assert.Equal(7, flat[2]);
    }

    [Fact]
    public void Otsu_separates_a_bimodal_image_and_binarizing_yields_black_and_white()
    {
        var px = Pixels((20, 20, 20), (25, 25, 25), (30, 30, 30), (200, 200, 200), (210, 210, 210), (220, 220, 220));

        var threshold = PixelOps.OtsuThreshold(px);
        PixelOps.Threshold(px, threshold);

        Assert.InRange(threshold, 31, 200);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255 }, Enumerable.Range(0, 6).Select(i => px[4 * i + 2]).ToArray());
    }

    [Fact]
    public void Unsharp_mask_keeps_a_flat_area_and_exaggerates_an_edge()
    {
        var flat = Enumerable.Repeat(new byte[] { 90, 90, 90, 255 }, 9).SelectMany(x => x).ToArray();
        Assert.Equal(flat, PixelOps.UnsharpMask(flat, 3, 3, 1.0));

        // A 3x1 ramp 0, 100, 200: the ends are pushed away from the middle, the middle stays put.
        var ramp = Pixels((0, 0, 0), (100, 100, 100), (200, 200, 200));
        var sharp = PixelOps.UnsharpMask(ramp, 3, 1, 1.0);
        Assert.Equal(100, sharp[4]);
        Assert.True(sharp[0] == 0 && sharp[8] > 200);
    }
}
