using System.Globalization;
using Ocrx.Sdk;

namespace SignaturePlugin;

/// <summary>Shrinks the whole overlay pill — box, text and trim together — by the user's chosen percentage.</summary>
internal static class OverlayScales
{
    public const int Min = 50;
    public const int Max = 100;
    public const int Default = Max;

    /// <summary>The percentage clamped into the range the slider offers, so a hand-edited out-of-range
    /// config value degrades to the nearest valid size instead of failing the overlay.</summary>
    public static int Normalize(int percent) => Math.Clamp(percent, Min, Max);

    /// <summary>Parses an edit forwarded by the engine; false when it is not a whole percentage in range.</summary>
    public static bool TryParse(string? value, out int percent) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out percent)
        && percent is >= Min and <= Max;

    /// <summary>
    /// Scales the overlay's own geometry. The anchor offsets are left alone: they place the pill on
    /// screen, and a smaller pill should stay where the user put it rather than creep off its edge.
    /// Runs after the theme, so it scales the themed dimensions, never the on-disk base.
    /// </summary>
    public static void Apply(SignaturePluginConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var percent = Normalize(config.OverlayScale);
        if (percent == Default) return;

        var overlay = config.Outputs
            .FirstOrDefault(output => string.Equals(output.Type, "overlay", StringComparison.OrdinalIgnoreCase))
            ?.Overlay;
        if (overlay is null) return;

        var factor = percent / 100.0;
        overlay.Width = Shrink(overlay.Width, factor, floor: 1);
        overlay.Height = Shrink(overlay.Height, factor, floor: 1);
        overlay.FontSize = (float)Math.Max(1, overlay.FontSize * factor);
        overlay.Padding = Shrink(overlay.Padding, factor, floor: 0);
        overlay.CornerRadius = Shrink(overlay.CornerRadius, factor, floor: 0);
        overlay.CornerAccentLength = Shrink(overlay.CornerAccentLength, factor, floor: 0);
        // A visible outline must not round away: a 1 px border at 50% would otherwise vanish.
        overlay.BorderWidth = Shrink(overlay.BorderWidth, factor, floor: overlay.BorderWidth > 0 ? 1 : 0);
    }

    private static int Shrink(int value, double factor, int floor) =>
        Math.Max(floor, (int)Math.Round(value * factor, MidpointRounding.AwayFromZero));
}
