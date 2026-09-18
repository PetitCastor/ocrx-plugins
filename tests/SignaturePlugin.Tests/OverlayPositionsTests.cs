using Ocrx.Sdk;
using Xunit;

namespace SignaturePlugin.Tests;

public class OverlayPositionsTests
{
    [Theory]
    [InlineData("topleft", OverlayAnchor.TopLeft)]
    [InlineData("topcenter", OverlayAnchor.TopCenter)]
    [InlineData("topright", OverlayAnchor.TopRight)]
    [InlineData("middleleft", OverlayAnchor.MiddleLeft)]
    [InlineData("center", OverlayAnchor.Center)]
    [InlineData("middleright", OverlayAnchor.MiddleRight)]
    [InlineData("bottomleft", OverlayAnchor.BottomLeft)]
    [InlineData("bottomcenter", OverlayAnchor.BottomCenter)]
    [InlineData("bottomright", OverlayAnchor.BottomRight)]
    public void KnownPosition_SetsTheMatchingAnchor(string position, OverlayAnchor anchor)
    {
        var overlay = Apply(position);

        Assert.Equal(anchor, overlay.Anchor);
    }

    [Fact]
    public void BlankPosition_DefaultsToTopCenter()
    {
        var overlay = Apply("");

        Assert.Equal(OverlayAnchor.TopCenter, overlay.Anchor);
    }

    [Fact]
    public void UnknownPosition_ExplainsTheSupportedValues()
    {
        var error = Assert.Throws<ArgumentException>(() => Apply("offscreen"));

        Assert.Contains("Unsupported position 'offscreen'", error.Message);
        Assert.Contains("topleft", error.Message);
        Assert.Contains("bottomright", error.Message);
    }

    private static OverlaySpec Apply(string position)
    {
        var overlay = new OverlaySpec();
        var config = new SignaturePluginConfig
        {
            Position = position,
            Outputs = [new SinkSpec { Type = "overlay", Overlay = overlay }],
        };

        OverlayPositions.Apply(config);
        return overlay;
    }
}
