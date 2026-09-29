using Xunit;

namespace SignatureOcrSweep.Tests;

public class ReadPlanTests
{
    [Fact]
    public void Reference_frame_reads_the_calibrated_rect_at_the_shipped_scale()
    {
        var plan = ReadPlan.Create(2560, 1440, null, KnobSet.Baseline);

        Assert.Equal((1264u, 454u, 70u, 44u), (plan.FrameRect.X, plan.FrameRect.Y, plan.FrameRect.Width, plan.FrameRect.Height));
        Assert.Equal(6.0, plan.EffectiveScale);
        Assert.Equal(1.0, plan.FovFactor);
    }

    [Fact]
    public void A_taller_frame_maps_by_height_not_fit()
    {
        // 1600x1200: the reference scales by 1200/1440 and is centred, the canvas overhanging both sides.
        var plan = ReadPlan.Create(1600, 1200, null, KnobSet.Baseline);

        Assert.Equal((787u, 378u, 58u, 37u), (plan.FrameRect.X, plan.FrameRect.Y, plan.FrameRect.Width, plan.FrameRect.Height));
    }

    [Fact]
    public void A_wide_fov_zooms_the_rect_toward_centre_and_divides_the_scale()
    {
        var plan = ReadPlan.Create(2560, 1440, 77.5521, KnobSet.Baseline);

        Assert.InRange(plan.FovFactor, 0.72, 0.73);
        Assert.InRange(plan.FrameRect.Y, 520u, 535u); // 454 pulled toward 720: 720 - 266 * 0.723
        Assert.Equal(6.0 / plan.FovFactor, plan.EffectiveScale, 6);
    }

    [Fact]
    public void A_fov_at_or_below_the_floor_leaves_the_calibrated_read_alone()
    {
        var plan = ReadPlan.Create(2560, 1440, 58.7155, KnobSet.Baseline);

        Assert.Equal(1.0, plan.FovFactor);
        Assert.Equal(6.0, plan.EffectiveScale);
    }

    [Fact]
    public void Knobs_override_scale_or_normalise_it_to_frame_height()
    {
        Assert.Equal(8.0, ReadPlan.Create(1600, 1200, null, KnobSet.Parse("scale=8")).EffectiveScale);
        Assert.Equal(6.0 * 1440 / 1200, ReadPlan.Create(1600, 1200, null, KnobSet.Parse("norm=6")).EffectiveScale, 9);
    }

    [Fact]
    public void Margin_grows_the_reference_rect_on_every_side()
    {
        var plan = ReadPlan.Create(2560, 1440, null, KnobSet.Parse("margin=4"));

        Assert.Equal((1260u, 450u, 78u, 52u), (plan.FrameRect.X, plan.FrameRect.Y, plan.FrameRect.Width, plan.FrameRect.Height));
    }

    [Fact]
    public void The_scale_is_clamped_to_the_ocr_max_dimension()
    {
        var plan = ReadPlan.Create(2560, 1440, null, KnobSet.Parse("scale=200"), maxImageDimension: 10000);

        Assert.Equal(10000.0 / 70, plan.EffectiveScale, 9);
    }

    [Fact]
    public void Clamp_trims_a_rect_that_overhangs_the_frame_edge()
    {
        var clamped = ReadPlan.Clamp(new Ocrx.Contracts.RoiRect(2550, 1430, 70, 44), 2560, 1440);

        Assert.Equal((2550u, 1430u, 10u, 10u), (clamped.X, clamped.Y, clamped.Width, clamped.Height));
    }

    [Fact]
    public void Clamp_leaves_an_in_frame_rect_alone()
    {
        var rect = new Ocrx.Contracts.RoiRect(10, 20, 30, 40);

        Assert.Equal(rect, ReadPlan.Clamp(rect, 2560, 1440));
    }

    [Fact]
    public void A_margin_larger_than_the_rect_origin_stops_at_the_frame_origin()
    {
        var plan = ReadPlan.Create(2560, 1440, null, KnobSet.Parse("margin=2000"));

        Assert.Equal((0u, 0u), (plan.FrameRect.X, plan.FrameRect.Y));
        Assert.Equal((2560u, 1440u), (plan.FrameRect.X + plan.FrameRect.Width, plan.FrameRect.Y + plan.FrameRect.Height));
    }

    [Fact]
    public void A_negative_margin_bigger_than_the_rect_leaves_one_pixel()
    {
        var plan = ReadPlan.Create(2560, 1440, null, KnobSet.Parse("margin=-100"));

        Assert.True(plan.FrameRect.Width >= 1 && plan.FrameRect.Height >= 1);
    }

    [Fact]
    public void Normalised_scale_is_divided_by_the_fov_zoom_as_well()
    {
        var plan = ReadPlan.Create(1600, 1200, 77.5521, KnobSet.Parse("norm=6"));

        Assert.Equal(6.0 * 1440 / 1200 / plan.FovFactor, plan.EffectiveScale, 9);
        Assert.True(plan.FovFactor < 1);
    }
}
