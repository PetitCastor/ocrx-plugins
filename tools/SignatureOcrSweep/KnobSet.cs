using System.Globalization;

namespace SignatureOcrSweep;

/// <summary>
/// One point on the sweep axes. The defaults are the shipped pipeline: the plugin's own scale (already
/// divided by the FOV zoom), no margin, red channel, cubic interpolation, nothing else. Engine mode can
/// express a knob set only when <see cref="IsEngineExpressible"/>: ROI rect and scale are all the engine
/// exposes today.
/// </summary>
/// <param name="Name">The spec string, used as the knob-set label in every output.</param>
/// <param name="Scale">Absolute OCR scale replacing the plugin's, applied to the FOV-zoomed rect.</param>
/// <param name="NormalizedScale">Resolution-normalized scale: the scale used at 1440 px height, multiplied by
/// 1440 / frame height (and divided by the FOV zoom) so the upscaled glyph height stays constant.</param>
/// <param name="Margin">Reference-space pixels added to every side of the rect after the FOV zoom. Reference
/// space rather than frame space so engine mode can express it and both modes map the same rect.</param>
public sealed record KnobSet(
    string Name,
    double? Scale = null,
    double? NormalizedScale = null,
    int Margin = 0,
    ChannelMode Channel = ChannelMode.Red,
    ContrastMode Contrast = ContrastMode.None,
    double Gamma = 1.0,
    BinarizeMode Binarize = BinarizeMode.None,
    int Threshold = 128,
    InterpolationKind Interpolation = InterpolationKind.Cubic,
    double Sharpen = 0.0)
{
    public static KnobSet Baseline { get; } = new("baseline");

    public bool IsEngineExpressible =>
        Channel == ChannelMode.Red && Contrast == ContrastMode.None && Binarize == BinarizeMode.None
        && Interpolation == InterpolationKind.Cubic && Sharpen == 0.0;

    /// <summary>
    /// Parses <c>baseline</c> or a <c>+</c>-joined list of <c>key=value</c> terms, for example
    /// <c>scale=8</c> or <c>scale=8+channel=lum+interp=fant</c>. Keys: scale, norm, margin, channel
    /// (red|lum|max|value), contrast (none|stretch|gamma:G), bin (none|otsu|fixed:N), interp
    /// (cubic|fant|linear|nearest), sharpen (none|unsharp:AMOUNT).
    /// </summary>
    public static KnobSet Parse(string spec)
    {
        var knobs = new KnobSet(spec.Trim());
        if (knobs.Name.Length == 0 || knobs.Name.Equals("baseline", StringComparison.OrdinalIgnoreCase))
            return Baseline;

        foreach (var term in knobs.Name.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = term.IndexOf('=');
            if (eq < 0)
                throw new FormatException($"knob term '{term}' in '{spec}' is not key=value");

            var key = term[..eq].ToLowerInvariant();
            var value = term[(eq + 1)..].ToLowerInvariant();
            var colon = value.IndexOf(':');
            var (head, tail) = colon < 0 ? (value, "") : (value[..colon], value[(colon + 1)..]);
            knobs = key switch
            {
                "scale" => knobs with { Scale = Number(value, term) },
                "norm" => knobs with { NormalizedScale = Number(value, term) },
                "margin" => knobs with { Margin = (int)Number(value, term) },
                "channel" => knobs with
                {
                    Channel = head switch
                    {
                        "red" => ChannelMode.Red,
                        "lum" or "luma" or "luminance" => ChannelMode.Luminance,
                        "max" or "maxrgb" => ChannelMode.MaxRgb,
                        "value" or "hsv" => ChannelMode.HsvValue,
                        _ => throw new FormatException($"unknown channel '{value}'"),
                    },
                },
                "contrast" => head switch
                {
                    "none" => knobs with { Contrast = ContrastMode.None },
                    "stretch" => knobs with { Contrast = ContrastMode.Stretch },
                    "gamma" => knobs with { Contrast = ContrastMode.Gamma, Gamma = Number(tail, term) },
                    _ => throw new FormatException($"unknown contrast '{value}'"),
                },
                "bin" => head switch
                {
                    "none" => knobs with { Binarize = BinarizeMode.None },
                    "otsu" => knobs with { Binarize = BinarizeMode.Otsu },
                    "fixed" => knobs with { Binarize = BinarizeMode.Fixed, Threshold = (int)Number(tail, term) },
                    _ => throw new FormatException($"unknown binarization '{value}'"),
                },
                "interp" => knobs with
                {
                    Interpolation = head switch
                    {
                        "cubic" => InterpolationKind.Cubic,
                        "fant" => InterpolationKind.Fant,
                        "linear" => InterpolationKind.Linear,
                        "nearest" => InterpolationKind.NearestNeighbor,
                        _ => throw new FormatException($"unknown interpolation '{value}'"),
                    },
                },
                "sharpen" => head switch
                {
                    "none" => knobs with { Sharpen = 0.0 },
                    "unsharp" => knobs with { Sharpen = tail.Length == 0 ? 1.0 : Number(tail, term) },
                    _ => throw new FormatException($"unknown sharpen '{value}'"),
                },
                _ => throw new FormatException($"unknown knob '{key}' in '{spec}'"),
            };
        }

        if (knobs.Scale is <= 0 || knobs.NormalizedScale is <= 0)
            throw new FormatException($"'{spec}': scale and norm must be positive");
        if (knobs.Scale is not null && knobs.NormalizedScale is not null)
            throw new FormatException($"'{spec}': scale and norm are alternatives, pick one");
        return knobs;
    }

    private static double Number(string text, string term) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)
            ? n
            : throw new FormatException($"'{term}': '{text}' is not a number");
}
