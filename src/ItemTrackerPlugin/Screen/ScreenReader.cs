using Ocrx.Contracts;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.Screen;

/// <summary>Turns one tick into the screen's context and, when an item is hovered, an observation.</summary>
public sealed class ScreenReader(Func<ItemClassResolver?> classes)
{
    /// <summary>Null when a header did not read this tick: the screen is then unknown, not changed.</summary>
    public static ScreenHeader? ReadHeader(TickData tick)
    {
        if (!tick.TryGetText(ScreenRois.HeaderRight, out var right) ||
            !tick.TryGetText(ScreenRois.HeaderLeft, out var left))
            return null;

        return HeaderParser.Parse(right, left);
    }

    /// <summary>
    /// Null when nothing is hovered, the screen is unknown, or the regions of its set are not in
    /// this tick yet (a switch applies from the next tick).
    /// </summary>
    public ScreenObservation? Read(TickData tick, ScreenHeader header)
    {
        if (header.Context == ScreenContext.Unknown) return null;

        if (header.Context != ScreenContext.Personal && Tooltip(tick, ScreenRois.TooltipRight) is { } right)
        {
            var page = PageOf(tick);
            var capacity = tick.TryGetOcr(ScreenRois.Capacity, out var cap)
                ? CapacityParser.Parse(string.Join(' ', cap.Lines.Select(l => l.Text)))
                : null;
            var tile = HoveredTile(tick);
            return Build(header.Context, header.Detail, right, QuantityAt(tick, tile), tile, page, capacity, "tooltip-right");
        }

        return Tooltip(tick, ScreenRois.TooltipLeft) is { } left
            ? Build(ScreenContext.Personal, null, left, null, null, null, null, "tooltip-left")
            : null;
    }

    private ScreenObservation Build(
        ScreenContext panel, string? ship, TooltipReading tooltip, int? quantity, GridTile? tile,
        PageCounter? page, CapacityReading? capacity, string source) =>
        new(panel, ship, tooltip, classes()?.Resolve(tooltip), quantity, tile, page, capacity, source);

    private static PageCounter? PageOf(TickData tick)
    {
        foreach (var scale in ScreenRois.PageScales)
        {
            if (tick.TryGetText(ScreenRois.Page(scale), out var text) && PageParser.Parse(text) is { } page)
                return page;
        }

        return null;
    }

    private static TooltipReading? Tooltip(TickData tick, RoiId id) =>
        tick.TryGetOcr(id, out var ocr) ? TooltipParser.Parse(ocr.Lines.Select(l => l.Text).ToList()) : null;

    private static GridTile? HoveredTile(TickData tick)
    {
        var bands = new List<PixelPatchSampler?>();
        for (var band = 0; band <= GridLayout.Rows; band++)
            bands.Add(tick.TryGetPixels(ScreenRois.Band(band), out var pixels) ? pixels : null);

        return HoverDetector.Find(bands);
    }

    /// <summary>A wide tile draws its badge in the corner of its last slot.</summary>
    private static int? QuantityAt(TickData tick, GridTile? tile)
    {
        if (tile is not { } t) return null;

        foreach (var scale in ScreenRois.BadgeScales)
        {
            if (tick.TryGetText(ScreenRois.Badge(t.Row, t.Column + t.Span - 1, scale), out var text) &&
                QuantityBadgeParser.Parse(text) is { } quantity)
                return quantity;
        }

        return null;
    }
}
