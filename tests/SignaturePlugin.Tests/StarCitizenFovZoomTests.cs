using Ocrx.Contracts;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// The zoom is pinned to the 2026-09-29 FOV sweep (2560x1440, one locked rock): the orange badge box
/// each slider produced must sit inside the zoomed counter rect.
/// </summary>
public class StarCitizenFovZoomTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(200.0)]
    public void Unusable_fov_keeps_the_calibrated_rect(double? fov)
        => Assert.Equal(1.0, StarCitizenFovZoom.Factor(fov));

    /// <summary>Sliders 85 and 90 left the badge on the same pixels: the floor.</summary>
    [Theory]
    [InlineData(54.5364)]
    [InlineData(58.7155)]
    [InlineData(StarCitizenFovZoom.FloorDegrees)]
    public void At_or_below_the_floor_nothing_moves(double fov)
    {
        Assert.Equal(1.0, StarCitizenFovZoom.Factor(fov));
        Assert.Same(Rois.Counter, StarCitizenFovZoom.Apply(Rois.Counter, StarCitizenFovZoom.Factor(fov)));
    }

    /// <summary>Stored FOV, then the measured orange badge box (icon plus digits) at that FOV.</summary>
    [Theory]
    [InlineData(63.0883, 1256u, 478u, 1312u, 497u)] // slider 95
    [InlineData(67.6728, 1256u, 496u, 1308u, 514u)] // slider 100
    [InlineData(77.5521, 1261u, 533u, 1304u, 548u)] // slider 110
    [InlineData(83.9863, 1264u, 554u, 1302u, 566u)] // slider 116, the game's maximum
    public void The_zoomed_rect_follows_the_measured_badge(double fov, uint left, uint top, uint right, uint bottom)
    {
        var rect = StarCitizenFovZoom.Apply(Rois.Counter, StarCitizenFovZoom.Factor(fov)).Rect;

        // The digits start right of the pin icon, so only the vertical span must be fully covered;
        // horizontally the rect must reach past the digits on the right.
        Assert.InRange(top, rect.Y, rect.Y + rect.Height);
        Assert.InRange(bottom, rect.Y, rect.Y + rect.Height);
        Assert.True(rect.X + rect.Width >= right, $"rect ends at {rect.X + rect.Width}, badge at {right}");
        Assert.True(rect.X <= left + 20, $"rect starts at {rect.X}, badge at {left}");
    }

    [Fact]
    public void Wider_fov_raises_the_ocr_scale_to_keep_the_crop_size()
    {
        var factor = StarCitizenFovZoom.Factor(83.9863);
        var zoomed = StarCitizenFovZoom.Apply(Rois.Counter, factor);

        Assert.Equal(Rois.Counter.Scale / factor, zoomed.Scale, 6);
        Assert.Equal(Rois.Counter.Id, zoomed.Id);
        Assert.Equal(Rois.Counter.Kind, zoomed.Kind);
    }

    [Fact]
    public void The_ocr_scale_is_capped()
    {
        var zoomed = StarCitizenFovZoom.Apply(Rois.Counter, 0.1);

        Assert.Equal(StarCitizenFovZoom.MaxOcrScale, zoomed.Scale);
    }

    [Fact]
    public void The_zoom_is_about_screen_centre()
    {
        var roi = Rois.Counter with { Rect = new RoiRect(1280, 720, 0, 0) };

        Assert.Equal(new RoiRect(1280, 720, 0, 0), StarCitizenFovZoom.Apply(roi, 0.5).Rect);
    }
}
