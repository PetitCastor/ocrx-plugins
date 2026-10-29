using System.Globalization;
using ItemTrackerPlugin.Screen;

namespace ItemTrackerPlugin.Overlay;

/// <summary>
/// The one line the overlay shows while an inventory screen is open, and when it changes. A kiosk reads
/// <c>Keeger kiosk · p.2/5 · 14 ✓ · 1 ⚠</c>; a ship locker, <c>Shiv locker · p.1/2</c>. A page that
/// turns fully verified gets a short line of its own for a moment. Outside a kiosk or a ship locker
/// the line is cleared, after the same few ticks the region switch waits, so one misread header does
/// not blink it.
/// </summary>
internal sealed class InventoryStatus
{
    private const int ClearAfterTicks = 3;
    private static readonly TimeSpan PageNoticeFor = TimeSpan.FromSeconds(4);

    private readonly HashSet<(string Station, int Page)> _announced = [];
    private string? _shown;
    private int _absentTicks;
    private DateTime _noticeUntil;

    /// <summary>
    /// True when the overlay must change: <paramref name="line"/> is then the new text, or null to hide it.
    /// </summary>
    /// <param name="station">Asked only for a kiosk.</param>
    public bool Next(
        ScreenHeader header, PageCounter? page, DateTime now, Func<StationStatus?> station, out string? line)
    {
        line = null;
        if (header.Context is not (ScreenContext.Kiosk or ScreenContext.ShipStorage))
        {
            _announced.Clear();
            if (_shown is null || ++_absentTicks < ClearAfterTicks) return false;

            _absentTicks = 0;
            _shown = null;
            return true;
        }

        _absentTicks = 0;
        var text = header.Context == ScreenContext.Kiosk ? KioskLine(page, now, station) : ShipLine(header.Detail, page);
        if (text == _shown) return false;

        _shown = line = text;
        return true;
    }

    private string KioskLine(PageCounter? page, DateTime now, Func<StationStatus?> station)
    {
        if (station() is not { } st) return "Kiosk" + PageOf(page);

        var head = $"{st.Label} kiosk";
        if (page is { } p && st.Pages.Any(s => s.Page == p.Page && s.Verified) && _announced.Add((st.LocationId, p.Page)))
            _noticeUntil = now + PageNoticeFor;

        // The notice outlives the tick that earned it, or the next tick's line would wipe it.
        if (now < _noticeUntil && page is { } shown)
            return $"{head} · page {shown.Page} verified ✓";

        var line = head + PageOf(page) + $" · {st.Tally.Verified} ✓";
        if (st.Tally.Conflict > 0) line += $" · {st.Tally.Conflict} ⚠";
        if (st.Tally.Stale > 0) line += $" · {st.Tally.Stale} stale";
        return line;
    }

    private static string ShipLine(string? model, PageCounter? page) =>
        (model is { Length: > 0 } ? $"{model} locker" : "Ship locker") + PageOf(page);

    private static string PageOf(PageCounter? page) =>
        page is { } p ? string.Create(CultureInfo.InvariantCulture, $" · p.{p.Page}/{p.Of}") : "";
}
