using Ocrx.Contracts;
using Ocrx.Sdk;
using Xunit;

namespace SignaturePlugin.Tests;

public class RoiScalesTests
{
    [Fact]
    public void FullSize_HandsBackTheSameSubscription() =>
        Assert.Same(Rois.Counter, RoiScales.Apply(Rois.Counter, 100));

    [Fact]
    public void Growing_PinsTheLeftEdgeAndKeepsTheOcrScale()
    {
        var grown = RoiScales.Apply(Rois.Counter, 150);

        Assert.Equal(new RoiRect(1264, 443, 105, 66), grown.Rect);
        Assert.Equal(Rois.Counter.Scale, grown.Scale);
        Assert.Equal(Rois.Counter.Id, grown.Id);
        Assert.Equal(Rois.Counter.Kind, grown.Kind);
    }

    [Fact]
    public void Doubling_NeverMovesTheLeftEdgeAndGrowsRightUpAndDown()
    {
        var grown = RoiScales.Apply(Rois.Counter, 200);

        Assert.Equal(new RoiRect(1264, 432, 140, 88), grown.Rect);
    }

    [Fact]
    public void AnEdgeRect_GrowsInsideTheReferenceSpace()
    {
        var corner = new RoiSubscription("edge", new RoiRect(2500, 1400, 60, 40), 1.0, RoiKind.Text);

        var grown = RoiScales.Apply(corner, 200);

        Assert.Equal(2500u, grown.Rect.X);
        Assert.Equal(2560u, grown.Rect.X + grown.Rect.Width);
        Assert.Equal(1440u, grown.Rect.Y + grown.Rect.Height);
    }

    [Fact]
    public void ARectAtTheTop_GrowsDownOnly()
    {
        var top = new RoiSubscription("top", new RoiRect(100, 0, 60, 40), 1.0, RoiKind.Text);

        var grown = RoiScales.Apply(top, 200);

        Assert.Equal(0u, grown.Rect.Y);
        Assert.Equal(60u, grown.Rect.Height);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(99, 100)]
    [InlineData(125, 125)]
    [InlineData(201, 200)]
    public void Normalize_ClampsIntoTheSliderRange(int input, int expected) =>
        Assert.Equal(expected, RoiScales.Normalize(input));

    [Theory]
    [InlineData("100", true, 100)]
    [InlineData("200", true, 200)]
    [InlineData("99", false, 99)]
    [InlineData("201", false, 201)]
    [InlineData("1.5", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void TryParse_AcceptsOnlyAWholePercentageInRange(string? value, bool ok, int percent)
    {
        Assert.Equal(ok, RoiScales.TryParse(value, out var parsed));
        Assert.Equal(percent, parsed);
    }
}
