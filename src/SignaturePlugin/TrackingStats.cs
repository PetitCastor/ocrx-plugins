using System.Globalization;

namespace SignaturePlugin;

/// <summary>
/// What tracking costs, summarised once per <see cref="Interval"/> for <c>--verbose</c>: how long the plugin
/// spent in each state, how much OCR the engine was asked for per tick in each, how many search tiles and ROI
/// pushes there were, and how long the plugin's own tick handler took. One line per window, never per tick —
/// the numbers are how a live run is judged, so they must not themselves be a cost.
/// </summary>
/// <remarks>
/// OCR area is the upscaled pixels the engine's OCR sees for the ROIs the tick carried (width x scale times
/// height x scale, summed): the thing OCR time follows. It is reported both in pixels and against the
/// calibrated counter on its own (70x44 at 6x = 110,880 px), the cost before tracking existed.
/// </remarks>
internal sealed class TrackingStats
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>The counter ROI alone, calibrated and unzoomed: one tick's OCR area when idle.</summary>
    public static readonly double BaselineArea =
        (double)Rois.Counter.Rect.Width * Rois.Counter.Scale * Rois.Counter.Rect.Height * Rois.Counter.Scale;

    private const int States = 3;

    private readonly Func<TimeSpan> _clock;
    private readonly int[] _ticks = new int[States];
    private readonly double[] _area = new double[States];
    private int _tiles, _pushes;
    private double _handlerTotalMs, _handlerMaxMs;
    private TimeSpan _windowStart;

    public TrackingStats(Func<TimeSpan> clock)
    {
        _clock = clock;
        _windowStart = clock();
    }

    /// <summary>One tick: the state it was scanned in, the OCR area it carried, whether it carried a search tile,
    /// and how long the plugin's handler took.</summary>
    public void Tick(TrackingState state, double ocrArea, bool searchTile, double handlerMs)
    {
        var s = (int)state;
        _ticks[s]++;
        _area[s] += ocrArea;
        if (searchTile)
            _tiles++;
        _handlerTotalMs += handlerMs;
        _handlerMaxMs = Math.Max(_handlerMaxMs, handlerMs);
    }

    public void Push() => _pushes++;

    /// <summary>The summary line once a full window has passed (and anything happened in it), else null.</summary>
    public string? TakeSummary()
    {
        var now = _clock();
        var elapsed = now - _windowStart;
        if (elapsed < Interval)
            return null;

        var ticks = _ticks.Sum();
        string? line = null;
        if (ticks > 0)
        {
            string Share(int s) => (100.0 * _ticks[s] / ticks).ToString("0", CultureInfo.InvariantCulture);
            string Area(int s) => _ticks[s] == 0
                ? "-"
                : (_area[s] / _ticks[s] / BaselineArea).ToString("0.00", CultureInfo.InvariantCulture) + "x";

            line = string.Create(CultureInfo.InvariantCulture,
                $"signature perf {elapsed.TotalSeconds:0.0}s: ticks={ticks} state idle/tracking/lost={Share(0)}/{Share(1)}/{Share(2)}% " +
                $"ocr-area/tick vs counter alone ({BaselineArea:0} px) idle={Area(0)} tracking={Area(1)} lost={Area(2)} " +
                $"search-tiles={_tiles} pushes={_pushes} handler avg={_handlerTotalMs / ticks:0.00}ms max={_handlerMaxMs:0.0}ms");
        }

        Array.Clear(_ticks);
        Array.Clear(_area);
        _tiles = _pushes = 0;
        _handlerTotalMs = _handlerMaxMs = 0;
        _windowStart = now;
        return line;
    }
}
