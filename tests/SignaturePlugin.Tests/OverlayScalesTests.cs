using Ocrx.Sdk;
using Xunit;

namespace SignaturePlugin.Tests;

public class OverlayScalesTests
{
    [Fact]
    public void FullSize_LeavesTheOverlayUntouched()
    {
        var overlay = Apply(100, Sample());

        Assert.Equal(400, overlay.Width);
        Assert.Equal(88, overlay.Height);
        Assert.Equal(24, overlay.FontSize);
    }

    [Fact]
    public void HalfSize_ShrinksBoxTextAndTrimTogether()
    {
        var overlay = Apply(50, Sample());

        Assert.Equal(200, overlay.Width);
        Assert.Equal(44, overlay.Height);
        Assert.Equal(12, overlay.FontSize);
        Assert.Equal(8, overlay.Padding);
        Assert.Equal(8, overlay.CornerRadius);
        Assert.Equal(9, overlay.CornerAccentLength);
    }

    [Fact]
    public void Position_OffsetsAreNotScaled()
    {
        var overlay = Apply(50, Sample());

        Assert.Equal(36, overlay.OffsetY);
        Assert.Equal(10, overlay.OffsetX);
    }

    [Fact]
    public void AVisibleOutline_NeverRoundsAwayToNothing()
    {
        var overlay = Apply(50, new OverlaySpec { BorderWidth = 1 });

        Assert.Equal(1, overlay.BorderWidth);
    }

    [Fact]
    public void NoOutline_StaysOff()
    {
        Assert.Equal(0, Apply(50, new OverlaySpec { BorderWidth = 0 }).BorderWidth);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(10, 200)]
    [InlineData(150, 400)]
    [InlineData(-5, 200)]
    public void OutOfRangeConfigValues_ClampToTheSliderRange(int stored, int expectedWidth)
    {
        Assert.Equal(expectedWidth, Apply(stored, Sample()).Width);
    }

    [Fact]
    public void NoOverlayOutput_IsANoOp()
    {
        var config = new SignaturePluginConfig { OverlayScale = 50, Outputs = [] };

        OverlayScales.Apply(config);

        Assert.Empty(config.Outputs);
    }

    [Theory]
    [InlineData("50", true, 50)]
    [InlineData("100", true, 100)]
    [InlineData("75", true, 75)]
    [InlineData("49", false, 0)]
    [InlineData("101", false, 0)]
    [InlineData("7.5", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void TryParse_AcceptsOnlyWholePercentagesInRange(string? value, bool ok, int expected)
    {
        Assert.Equal(ok, OverlayScales.TryParse(value, out var percent));
        if (ok) Assert.Equal(expected, percent);
    }

    private static OverlaySpec Sample() => new()
    {
        Width = 400,
        Height = 88,
        FontSize = 24,
        Padding = 16,
        CornerRadius = 16,
        CornerAccentLength = 18,
        OffsetX = 10,
        OffsetY = 36,
    };

    private static OverlaySpec Apply(int percent, OverlaySpec overlay)
    {
        var config = new SignaturePluginConfig
        {
            OverlayScale = percent,
            Outputs = [new SinkSpec { Type = "overlay", Overlay = overlay }],
        };

        OverlayScales.Apply(config);
        return overlay;
    }
}
