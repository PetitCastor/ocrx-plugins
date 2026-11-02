using System.Globalization;
using Ocrx.Contracts;
using Ocrx.Sdk;

namespace SignaturePlugin;

/// <summary>Grows the counter ROI by the user's chosen percentage — right, up and down, never left — so a
/// badge that drifts off the calibrated crop still lands inside it.</summary>
internal static class RoiScales
{
    public const int Min = 100;
    public const int Max = 200;
    public const int Default = Min;

    // The reference space every <see cref="RoiRect"/> is expressed in; a grown rect is held inside it.
    private const double ReferenceWidth = 2560;
    private const double ReferenceHeight = 1440;

    /// <summary>The percentage clamped into the range the slider offers, so a hand-edited out-of-range
    /// config value degrades to the nearest valid size instead of failing the subscription.</summary>
    public static int Normalize(int percent) => Math.Clamp(percent, Min, Max);

    /// <summary>Parses an edit forwarded by the engine; false when it is not a whole percentage in range.</summary>
    public static bool TryParse(string? value, out int percent) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out percent)
        && percent is >= Min and <= Max;

    /// <summary>
    /// <paramref name="roi"/> with its rect grown from a fixed left edge, symmetrically in height. Only the rect changes: the OCR scale
    /// stays, so the digits reach Windows OCR at the size they were calibrated at, with more margin
    /// around them. Edges are held inside the reference space, so a rect near the border grows
    /// lopsidedly instead of going negative. The default percentage hands the subscription back as is.
    /// </summary>
    public static RoiSubscription Apply(RoiSubscription roi, int percent)
    {
        var factor = Normalize(percent) / 100.0;
        if (factor == 1.0)
            return roi;

        var r = roi.Rect;
        var halfHeight = r.Height * factor / 2;
        var centreY = r.Y + r.Height / 2.0;

        // The left edge is pinned: the pin icon sits just left of the digits, and a crop that reaches
        // it gets the icon read as a character.
        var left = (double)r.X;
        var top = Math.Max(0, Math.Round(centreY - halfHeight, MidpointRounding.AwayFromZero));
        var right = Math.Min(ReferenceWidth, left + Math.Round(r.Width * factor, MidpointRounding.AwayFromZero));
        var bottom = Math.Min(ReferenceHeight, Math.Round(centreY + halfHeight, MidpointRounding.AwayFromZero));

        return roi with
        {
            Rect = new RoiRect((uint)left, (uint)top, (uint)Math.Max(1, right - left), (uint)Math.Max(1, bottom - top)),
        };
    }
}
