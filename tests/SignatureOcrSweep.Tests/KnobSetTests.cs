using Xunit;

namespace SignatureOcrSweep.Tests;

public class KnobSetTests
{
    [Fact]
    public void Baseline_is_the_shipped_pipeline_and_engine_expressible()
    {
        var knobs = KnobSet.Parse("baseline");

        Assert.Equal(ChannelMode.Red, knobs.Channel);
        Assert.Equal(InterpolationKind.Cubic, knobs.Interpolation);
        Assert.Null(knobs.Scale);
        Assert.True(knobs.IsEngineExpressible);
    }

    [Fact]
    public void Terms_compose_and_the_name_is_the_spec()
    {
        var knobs = KnobSet.Parse("scale=8+channel=lum+contrast=gamma:0.7+bin=fixed:100+interp=fant+sharpen=unsharp:1.5+margin=4");

        Assert.Equal(8, knobs.Scale);
        Assert.Equal(ChannelMode.Luminance, knobs.Channel);
        Assert.Equal(ContrastMode.Gamma, knobs.Contrast);
        Assert.Equal(0.7, knobs.Gamma);
        Assert.Equal(BinarizeMode.Fixed, knobs.Binarize);
        Assert.Equal(100, knobs.Threshold);
        Assert.Equal(InterpolationKind.Fant, knobs.Interpolation);
        Assert.Equal(1.5, knobs.Sharpen);
        Assert.Equal(4, knobs.Margin);
        Assert.False(knobs.IsEngineExpressible);
        Assert.StartsWith("scale=8+", knobs.Name);
    }

    [Theory]
    [InlineData("scale=8")]
    [InlineData("norm=6")]
    [InlineData("margin=-4")]
    public void Scale_norm_and_margin_are_all_engine_mode_can_express(string spec) =>
        Assert.True(KnobSet.Parse(spec).IsEngineExpressible);

    [Theory]
    [InlineData("scale")]
    [InlineData("scale=abc")]
    [InlineData("channel=purple")]
    [InlineData("frobnicate=1")]
    [InlineData("scale=4+norm=6")]
    public void Malformed_specs_are_rejected(string spec) =>
        Assert.Throws<FormatException>(() => KnobSet.Parse(spec));

    [Theory]
    [InlineData("scale=0")]
    [InlineData("scale=-3")]
    [InlineData("norm=0")]
    [InlineData("scale=NaN")]
    [InlineData("scale=Infinity")]
    public void Non_positive_or_non_finite_scales_are_rejected(string spec) =>
        Assert.Throws<FormatException>(() => KnobSet.Parse(spec));
}
