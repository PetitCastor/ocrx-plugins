using Ocrx.Contracts;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.Screen;

/// <summary>
/// The regions the plugin subscribes, in reference space. The two headers are always on; the rest
/// is subscribed only while a screen that has something to read is up, through
/// <c>UpdateRoisAsync</c> with a different id set (the engine takes any set and applies it from the
/// next tick, measured on engine 2.0.62). An idle plugin therefore costs two small reads per tick.
/// </summary>
public static class ScreenRois
{
    public static readonly RoiId HeaderRight = "header-right";
    public static readonly RoiId HeaderLeft = "header-left";
    public static readonly RoiId TooltipRight = "tooltip-right";
    public static readonly RoiId TooltipLeft = "tooltip-left";
    public static readonly RoiId Capacity = "capacity-right";

    private const double TooltipScale = 2.0;
    /// <summary>
    /// Windows OCR reads a two-character badge at one of these scales and not at the others, a
    /// different one per badge (measured on the corpus: no scale read them all, and none misread).
    /// Each cell is therefore subscribed at all three and the first that parses wins.
    /// </summary>
    internal static readonly double[] BadgeScales = [4.0, 6.0, 8.0];

    /// <summary>The page counter reads at one of these scales and not the other, per frame: the
    /// <c>1/5</c> of the corpus's second frame needs 2, others 3.</summary>
    internal static readonly double[] PageScales = [2.0, 3.0];

    public static RoiId Page(double scale) => $"page-s{scale:0}";

    public static RoiId Band(int band) => $"band-{band}";

    public static RoiId Badge(int row, int column, double scale) => $"badge-{row}-{column}-s{scale:0}";

    private static readonly RoiSubscription[] Headers =
    [
        new(HeaderRight, new RoiRect(1990, 150, 400, 56), 2.0, RoiKind.Text),
        new(HeaderLeft, new RoiRect(300, 150, 330, 56), 2.0, RoiKind.Text),
    ];

    private static readonly RoiSubscription LeftTooltip =
        new(TooltipLeft, new RoiRect(752, 344, 476, 1000), TooltipScale, RoiKind.Detailed);

    public static IReadOnlyList<RoiSubscription> Idle { get; } = Headers;

    private static readonly IReadOnlyList<RoiSubscription> PersonalSet = [.. Headers, LeftTooltip];

    private static readonly IReadOnlyList<RoiSubscription> StorageSet = BuildStorageSet();

    /// <summary>What to subscribe while <paramref name="context"/> is on screen.</summary>
    public static IReadOnlyList<RoiSubscription> For(ScreenContext context) => context switch
    {
        ScreenContext.Kiosk or ScreenContext.ShipStorage => StorageSet,
        ScreenContext.Personal => PersonalSet,
        _ => Idle,
    };

    private static List<RoiSubscription> BuildStorageSet()
    {
        var set = new List<RoiSubscription>(Headers)
        {
            new(TooltipRight, new RoiRect(1336, 344, 476, 1000), TooltipScale, RoiKind.Detailed),
            LeftTooltip,
            new(Capacity, new RoiRect(2165, 205, 260, 52), 3.0, RoiKind.Detailed),
        };

        foreach (var scale in PageScales)
            set.Add(new RoiSubscription(Page(scale), new RoiRect(2110, 1212, 130, 44), scale, RoiKind.Text));

        // Pixels strips are sized for the budget at 4K: 600x40 reference pixels is 216 KB there.
        for (var band = 0; band <= GridLayout.Rows; band++)
            set.Add(new RoiSubscription(Band(band), GridLayout.BorderBand(band), 1.0, RoiKind.Pixels));

        for (var row = 0; row < GridLayout.Rows; row++)
            for (var column = 0; column < GridLayout.Columns; column++)
                foreach (var scale in BadgeScales)
                    set.Add(new RoiSubscription(Badge(row, column, scale), GridLayout.BadgeCell(row, column), scale, RoiKind.Text));

        return set;
    }
}
