using ItemTrackerPlugin.Screen;
using Ocrx.Contracts;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.Tests;

public sealed class ScreenRoisTests
{
    [Fact]
    public void Idle_set_is_the_two_headers()
    {
        Assert.Equal(["header-right", "header-left"], ScreenRois.Idle.Select(r => r.Id.Value));
        Assert.Same(ScreenRois.Idle, ScreenRois.For(ScreenContext.Unknown));
    }

    [Fact]
    public void Every_set_keeps_the_headers_so_the_context_can_change_back()
    {
        foreach (var context in Enum.GetValues<ScreenContext>())
        {
            var ids = ScreenRois.For(context).Select(r => r.Id.Value).ToList();
            Assert.Contains("header-right", ids);
            Assert.Contains("header-left", ids);
        }
    }

    [Fact]
    public void Kiosk_and_ship_share_one_set_and_personal_reads_only_the_left_tooltip()
    {
        Assert.Same(ScreenRois.For(ScreenContext.Kiosk), ScreenRois.For(ScreenContext.ShipStorage));
        Assert.Equal(
            ["header-right", "header-left", "tooltip-left"],
            ScreenRois.For(ScreenContext.Personal).Select(r => r.Id.Value));
    }

    [Fact]
    public void Region_ids_are_unique_within_a_set()
    {
        foreach (var context in Enum.GetValues<ScreenContext>())
        {
            var ids = ScreenRois.For(context).Select(r => r.Id.Value).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }

    [Fact]
    public void Every_region_lies_inside_the_reference_frame()
    {
        foreach (var roi in ScreenRois.For(ScreenContext.Kiosk))
        {
            Assert.True(roi.Rect.X + roi.Rect.Width <= EngineDefaults.ReferenceWidth, roi.Id.Value);
            Assert.True(roi.Rect.Y + roi.Rect.Height <= EngineDefaults.ReferenceHeight, roi.Id.Value);
        }
    }

    [Fact]
    public void Pixel_regions_fit_the_wire_budget_at_4k()
    {
        foreach (var roi in ScreenRois.For(ScreenContext.Kiosk).Where(r => r.Kind == RoiKind.Pixels))
            Assert.True(WireLimits.FitsPixelBudget((uint)(roi.Rect.Width * 1.5), (uint)(roi.Rect.Height * 1.5)), roi.Id.Value);
    }

    [Fact]
    public void Each_grid_cell_has_a_badge_region_at_every_scale()
    {
        var ids = ScreenRois.For(ScreenContext.Kiosk).Select(r => r.Id.Value).ToHashSet();

        for (var row = 0; row < GridLayout.Rows; row++)
            for (var column = 0; column < GridLayout.Columns; column++)
                foreach (var scale in ScreenRois.BadgeScales)
                    Assert.Contains(ScreenRois.Badge(row, column, scale).Value, ids);
    }
}
