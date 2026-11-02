using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>The once-per-window <c>--verbose</c> summary a live run is judged by.</summary>
public class TrackingStatsTests
{
    private TimeSpan _now = TimeSpan.Zero;

    private TrackingStats Stats() => new(() => _now);

    [Fact]
    public void Nothing_is_reported_before_the_window_is_over()
    {
        var stats = Stats();
        stats.Tick(TrackingState.Idle, TrackingStats.BaselineArea, false, 0.5);

        _now = TrackingStats.Interval - TimeSpan.FromMilliseconds(1);

        Assert.Null(stats.TakeSummary());
    }

    [Fact]
    public void A_window_reports_ticks_state_split_ocr_area_per_state_tiles_pushes_and_handler_time()
    {
        var stats = Stats();
        for (var i = 0; i < 6; i++)
            stats.Tick(TrackingState.Idle, TrackingStats.BaselineArea, false, 1.0);
        for (var i = 0; i < 3; i++)
            stats.Tick(TrackingState.Tracking, 1.5 * TrackingStats.BaselineArea, false, 2.0);
        stats.Tick(TrackingState.Lost, 4.6 * TrackingStats.BaselineArea, true, 5.0);
        stats.Push();
        stats.Push();
        _now = TrackingStats.Interval;

        var line = stats.TakeSummary();

        Assert.NotNull(line);
        Assert.Contains("ticks=10", line);
        Assert.Contains("state idle/tracking/lost=60/30/10%", line);
        Assert.Contains("idle=1.00x tracking=1.50x lost=4.60x", line);
        Assert.Contains("search-tiles=1", line);
        Assert.Contains("pushes=2", line);
        Assert.Contains("handler avg=1.70ms max=5.0ms", line);
        Assert.Contains("110880 px", line);
    }

    [Fact]
    public void A_state_with_no_ticks_has_no_area_and_the_window_resets()
    {
        var stats = Stats();
        stats.Tick(TrackingState.Idle, TrackingStats.BaselineArea, false, 1.0);
        stats.Push();
        _now = TrackingStats.Interval;
        var first = stats.TakeSummary();
        Assert.Contains("tracking=- lost=-", first);

        // Next window: a fresh count, and no line at all when nothing happened in it.
        _now += TrackingStats.Interval;
        Assert.Null(stats.TakeSummary());

        stats.Tick(TrackingState.Idle, TrackingStats.BaselineArea, false, 1.0);
        _now += TrackingStats.Interval;
        var second = stats.TakeSummary();
        Assert.Contains("ticks=1", second);
        Assert.Contains("pushes=0", second);
    }
}
