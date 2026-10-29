using Ocrx.Contracts;

namespace ItemTrackerPlugin.Screen;

/// <summary>
/// Finds the hovered tile from the bright border the game draws around it. A tile's top edge sits
/// in one border band and its bottom edge in the next, as the same horizontal run, which is what
/// tells a border from an icon's highlight: only a pair of runs with the same extent counts.
/// </summary>
public static class HoverDetector
{
    /// <summary>Shortest joined edge, in reference pixels, that can be a one-slot tile's.</summary>
    private const int MinRun = 100;

    /// <summary>Shortest piece of an edge on one pixel row, and the largest gap that still joins two.</summary>
    private const int MinPiece = 30;
    private const int JoinGap = 6;
    private const int ExtentTolerance = 14;

    /// <summary>The corner radius eats this much of a run at each end.</summary>
    private const int CornerInset = 8;

    private readonly record struct Run(int Start, int End);

    /// <param name="bands">One sampler per <see cref="GridLayout.BorderBand"/>, or null for a band
    /// that did not read.</param>
    public static GridTile? Find(IReadOnlyList<PixelPatchSampler?> bands)
    {
        var runs = bands.Select(Runs).ToList();
        for (var row = 0; row < GridLayout.Rows && row + 1 < runs.Count; row++)
        {
            foreach (var top in runs[row])
            {
                if (!runs[row + 1].Any(bottom => Math.Abs(bottom.Start - top.Start) <= ExtentTolerance
                                              && Math.Abs(bottom.End - top.End) <= ExtentTolerance))
                    continue;

                var column = (int)Math.Round((double)(top.Start - CornerInset - GridLayout.OriginX) / GridLayout.SlotPitch);
                var span = (int)Math.Round((double)(top.End - top.Start + 2 * CornerInset) / GridLayout.SlotPitch);
                // A run wider than two slots is a panel edge or a divider, not a tile.
                if (span is < 1 or > 2) continue;

                return new GridTile(row, Math.Clamp(column, 0, GridLayout.Columns - 1), span);
            }
        }

        return null;
    }

    /// <summary>
    /// Border-coloured stretches in a band, in reference x. The panel is drawn in perspective, so an
    /// edge is a few pixels off level across a tile and one row of the band holds only part of it;
    /// the pieces from every row are joined where they touch.
    /// </summary>
    private static List<Run> Runs(PixelPatchSampler? band)
    {
        var pieces = new List<Run>();
        if (band is null || band.Width < 2 || band.Height < 1) return pieces;

        var perRef = (double)band.Width / GridLayout.BandWidth;
        var minPiece = (int)(MinPiece * perRef);
        var gapAllowed = Math.Max(2, (int)(4 * perRef));

        for (var y = 0; y < band.Height; y++)
        {
            var start = -1;
            var lastHit = -1;
            for (var x = 0; x <= band.Width; x++)
            {
                var hit = x < band.Width && IsBorder(band.AveragePatch(band.FrameX + x, band.FrameY + y, 0));
                if (hit)
                {
                    if (start < 0) start = x;
                    lastHit = x;
                }
                else if (start >= 0 && (x == band.Width || x - lastHit > gapAllowed))
                {
                    if (lastHit - start >= minPiece)
                        pieces.Add(new Run(GridLayout.BandX + (int)(start / perRef), GridLayout.BandX + (int)(lastHit / perRef)));
                    start = -1;
                }
            }
        }

        var joined = new List<Run>();
        foreach (var piece in pieces.OrderBy(p => p.Start))
        {
            if (joined.Count > 0 && piece.Start <= joined[^1].End + JoinGap)
                joined[^1] = joined[^1] with { End = Math.Max(joined[^1].End, piece.End) };
            else
                joined.Add(piece);
        }

        return joined.Where(r => r.End - r.Start >= MinRun).ToList();
    }

    /// <summary>
    /// The hovered border is a cool blue-grey whose brightness pulses, (R45, G59, B68) in one frame
    /// and (R184, G226, B233) in another, so the test is the hue (blue over green over red, with a
    /// fixed spread) and a floor on the brightness. Resting borders are far darker.
    /// </summary>
    internal static bool IsBorder((byte B, byte G, byte R) c) =>
        c.R <= c.G && c.G <= c.B && c.G - c.R is >= 8 and <= 60 && c.B - c.G is >= 2 and <= 22 && c.G >= 50;
}
