using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// What tracking costs the engine, asserted. The tracker runs inside the engine's scan loop on a game machine:
/// a tick's cost is the OCR it asks for (upscaled pixels, summed over its ROIs; the counter alone, 70x44 at 6x,
/// is <see cref="TrackingStats.BaselineArea"/>) and every change to the ROI set is a push the engine republishes
/// to its UI and overlay. Tiling the screen with OCR tiles on every tick was measured at 4.8x the baseline in
/// OCR, 10x while searching, and a push on almost every tick — a lag spike on every read — so the counter reads
/// the number itself and the search is rare.
/// </summary>
public class TrackingBudgetTests
{
    private static readonly double Baseline = TrackingStats.BaselineArea;

    private static NumberBox Number(int x, int y, int width = 56, int height = 15) => new(x, y, width, height);

    /// <summary>Nothing read: exactly the plugin before tracking — one counter ROI, no tiles, no pushes.</summary>
    [Fact]
    public void Idle_costs_the_counter_alone_and_no_pushes()
    {
        var rig = new TrackerRig();

        rig.Run(200);

        Assert.Equal(0, rig.Pushes);
        Assert.Equal(0, rig.TileTicks);
        Assert.Equal(200 * Baseline, rig.Area);
        Assert.Equal(110_880, Baseline); // 70x44 at 6x, the figure the budget is stated in
    }

    /// <summary>A number standing still: the rect is placed once, with margin to spare, then nothing — at most
    /// 1.5x the baseline in OCR (a wide number like "17,200" needs a wider rect than the calibrated 70 px).</summary>
    [Fact]
    public void Tracking_a_standing_number_costs_at_most_one_and_a_half_times_the_baseline_and_no_further_pushes()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,200", Number(1273, 464)));
        rig.Run(3);
        Assert.Equal(TrackingState.Tracking, rig.Tracker.State); // a tracker that never locked would pass the rest
        Assert.Equal(1, rig.Acquired);
        var pushes = rig.Pushes;
        var area = rig.Area;

        rig.Run(200);

        Assert.Equal(pushes, rig.Pushes);
        Assert.Equal(0, rig.TileTicks);
        Assert.InRange((rig.Area - area) / 200 / Baseline, 1.0, 1.5);
    }

    /// <summary>The measured motion, 10 px a tick, one direction: the rect is placed with five ticks of room
    /// ahead, so it is pushed about every five ticks, never lost, and the OCR stays within 2.4x the baseline
    /// (a 120-150 px wide crop; measured on this machine the engine's OCR of a 120x44 crop at 6x takes no longer
    /// than the 70x44 one, ~4-8 ms, and only doubles past ~2x the area).</summary>
    [Fact]
    public void Tracking_a_moving_number_pushes_about_twice_a_second_and_stays_within_twice_and_a_half_the_baseline()
    {
        var rig = new TrackerRig();
        var x = 1273;
        for (var tick = 0; tick < 15; tick++)
        {
            rig.Scene.Remove(_ => true).Add("17,200", Number(x, 464));
            rig.Step();
        }

        var pushes = rig.Pushes;
        var area = rig.Area;
        const int moving = 100;
        for (var tick = 0; tick < moving; tick++)
        {
            x -= 10;
            rig.Scene.Remove(_ => true).Add("17,200", Number(x, 464));
            rig.Step();
            Assert.True(rig.LastArea <= 2.4 * Baseline + 1, $"tick {tick}: {rig.LastArea / Baseline:0.00}x");
        }

        var seconds = moving / 10.0;
        Assert.InRange((rig.Pushes - pushes) / seconds, 1.0, 2.6);
        Assert.InRange((rig.Area - area) / moving / Baseline, 1.0, 2.4);
        Assert.Equal(0, rig.TileTicks);
        Assert.Equal(TrackingState.Tracking, rig.Tracker.State);
    }

    /// <summary>Slow drift (2 px a tick) is cheaper still.</summary>
    [Fact]
    public void Slow_drift_pushes_less_than_twice_a_second()
    {
        var rig = new TrackerRig();
        var x = 1273;
        var y = 464.0;
        for (var tick = 0; tick < 15; tick++)
        {
            rig.Scene.Remove(_ => true).Add("17,200", Number(x, (int)y));
            rig.Step();
        }

        Assert.Equal(1, rig.Acquired);
        var pushes = rig.Pushes;
        for (var tick = 0; tick < 100; tick++)
        {
            x -= 2;
            y -= 1;
            rig.Scene.Remove(_ => true).Add("17,200", Number(x, (int)y));
            rig.Step();
        }

        Assert.InRange((rig.Pushes - pushes) / 10.0, 0, 2.0);
        Assert.Equal(TrackingState.Tracking, rig.Tracker.State); // it followed the drift: never lost, never re-acquired
        Assert.Equal(1, rig.Acquired);
    }

    /// <summary>Lost: at most one search tile in any five ticks, a push each way, and then it stops altogether.</summary>
    [Fact]
    public void A_lost_number_costs_at_most_one_small_tile_every_five_ticks_and_the_search_ends()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,200", Number(1273, 464)));
        rig.Run(15);
        rig.Scene.Remove(_ => true);

        var tiles = new List<bool>();
        var worst = 0.0;
        for (var tick = 0; tick < 60; tick++)
        {
            rig.Step();
            tiles.Add(rig.LastHadTile);
            worst = Math.Max(worst, rig.LastArea / Baseline);
        }

        for (var i = 0; i + 5 <= tiles.Count; i++)
            Assert.True(tiles.Skip(i).Take(5).Count(t => t) <= 1, $"two tiles within five ticks at {i}");

        // The tile is 400x250 at 2x = 400,000 px: 3.6x the baseline on its own, on 1 tick in 5, for 3 seconds,
        // beside a counter of at most 2.4x.
        Assert.True(worst <= 2.4 + 400_000 / Baseline + 0.01, $"worst tick {worst:0.00}x");
        Assert.Equal(TrackingState.Idle, rig.Tracker.State);

        // The search has ended: the next 100 ticks are the idle cost.
        var area = rig.Area;
        var tileTicks = rig.TileTicks;
        var pushes = rig.Pushes;
        rig.Run(100);
        Assert.Equal(tileTicks, rig.TileTicks);
        Assert.Equal(pushes, rig.Pushes);
        Assert.Equal(100 * Baseline, rig.Area - area);
    }

    /// <summary>Total work for a whole loss: a handful of tiles (a whole-screen sweep would be hundreds).</summary>
    [Fact]
    public void A_whole_loss_costs_a_handful_of_tiles()
    {
        var rig = new TrackerRig(new OcrScene().Add("17,200", Number(1273, 464)));
        rig.Run(15);
        rig.Scene.Remove(_ => true);

        rig.Run(60);

        Assert.InRange(rig.TileTicks, 1, 6); // 3 s, one every 5 ticks
        Assert.InRange(rig.Pushes, 1, 2 * 6 + 2);
    }
}
