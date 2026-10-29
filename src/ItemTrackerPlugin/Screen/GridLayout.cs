using Ocrx.Contracts;

namespace ItemTrackerPlugin.Screen;

/// <summary>
/// Where the right-hand tile grid sits, in reference space (2560x1440), measured on the
/// 2026-10-28/29 kiosk frames. Four slot columns and six rows; a wide tile takes two slots. The
/// panels sit at the same pixels at FOV 85, 100 and 116, so nothing here depends on the FOV. The
/// grid hugs the right edge of a 16:9 frame; how it moves on any other aspect is not measured.
/// </summary>
public static class GridLayout
{
    public const int Columns = 4;
    public const int Rows = 6;

    public const int OriginX = 1878;
    public const int OriginY = 338;
    public const int SlotPitch = 147;
    public const int RowPitch = 139;

    private const int BandAbove = 4;
    private const int BandHeight = 40;
    public const int BandWidth = 600;
    public const int BandX = OriginX - 4;

    private const int BadgeInsetX = 96;
    private const int BadgeInsetY = 4;
    private const int BadgeWidth = 50;
    private const int BadgeHeight = 44;

    /// <summary>Row <paramref name="band"/>'s top edge and the bottom edge of the row above it both
    /// fall in this strip, so a hovered tile shows as the same border run in two consecutive bands.
    /// Valid for 0 to <see cref="Rows"/> inclusive.</summary>
    public static RoiRect BorderBand(int band) =>
        new(BandX, (uint)(OriginY + RowPitch * band - BandAbove), BandWidth, BandHeight);

    /// <summary>The corner of a slot where a stack count is drawn. Tight enough to leave the tile
    /// border out, which the OCR reads as part of the text.</summary>
    public static RoiRect BadgeCell(int row, int column) =>
        new((uint)(OriginX + SlotPitch * column + BadgeInsetX), (uint)(OriginY + RowPitch * row + BadgeInsetY),
            BadgeWidth, BadgeHeight);
}
