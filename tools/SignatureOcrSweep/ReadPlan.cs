using Ocrx.Contracts;
using Ocrx.Sdk;
using SignaturePlugin;

namespace SignatureOcrSweep;

/// <summary>
/// Where and how one counter read happens for one frame, before any pixel is touched: the
/// subscription the plugin would hand the engine (reference space, FOV-zoomed exactly as
/// <see cref="StarCitizenFovZoom"/> does it, then the knob set's scale and margin), and the frame-space
/// crop and applied scale the engine derives from it. Shared by both modes: engine mode subscribes
/// <see cref="Subscription"/>, offline mode crops <see cref="FrameRect"/> at <see cref="EffectiveScale"/>.
/// </summary>
/// <param name="Subscription">Reference-space ROI and requested OCR scale.</param>
/// <param name="FrameRect">The crop in frame pixels, as <c>RoiScaler.ToFrame</c> maps it and clamped to the frame.</param>
/// <param name="EffectiveScale">The requested scale after the OCR engine's max-dimension clamp.</param>
/// <param name="FovFactor">The zoom factor <see cref="StarCitizenFovZoom.Factor"/> gave for the frame's FOV.</param>
public sealed record ReadPlan(RoiSubscription Subscription, RoiRect FrameRect, double EffectiveScale, double FovFactor)
{
    /// <summary>Star Citizen's calibration as games.json publishes it: 2560x1440, scaled by height. The
    /// engine resolves the client name <c>SignaturePlugin</c> to this reference; any other name gets fit.</summary>
    public static RoiReference Reference { get; } = new(RoiScaler.ReferenceWidth, RoiScaler.ReferenceHeight)
    {
        ScaleMode = RoiScaleMode.Height,
    };

    /// <summary>Windows OCR maximum image dimension: only a default, so planning stays free of the WinRT
    /// projection. The runners pass the real <c>OcrEngine.MaxImageDimension</c>.</summary>
    public const int DefaultMaxImageDimension = 10000;

    public static ReadPlan Create(int frameWidth, int frameHeight, double? verticalFov, KnobSet knobs,
        int maxImageDimension = DefaultMaxImageDimension)
    {
        var factor = StarCitizenFovZoom.Factor(verticalFov);
        var zoomed = StarCitizenFovZoom.Apply(Rois.Counter, factor);

        var scale = knobs.Scale
            ?? (knobs.NormalizedScale is { } norm
                ? norm * RoiScaler.ReferenceHeight / frameHeight / factor
                : zoomed.Scale);

        var rect = zoomed.Rect;
        if (knobs.Margin != 0)
        {
            var m = knobs.Margin;
            var x = Math.Max(0, (long)rect.X - m);
            var y = Math.Max(0, (long)rect.Y - m);
            var right = (long)rect.X + rect.Width + m;
            var bottom = (long)rect.Y + rect.Height + m;
            rect = new RoiRect((uint)x, (uint)y, (uint)Math.Max(1, right - x), (uint)Math.Max(1, bottom - y));
        }

        var subscription = zoomed with { Rect = rect, Scale = scale };
        var mapped = RoiScaler.ToFrame(rect, frameWidth, frameHeight, Reference);
        var clamped = Clamp(mapped, frameWidth, frameHeight);
        return new ReadPlan(subscription, clamped, ClampScale(clamped, scale, maxImageDimension), factor);
    }

    /// <summary>Mirror of <c>OcrPipeline.ClampToBitmap</c>.</summary>
    public static RoiRect Clamp(RoiRect bounds, int width, int height)
    {
        var x = Math.Min(bounds.X, (uint)Math.Max(0, width));
        var y = Math.Min(bounds.Y, (uint)Math.Max(0, height));
        return new RoiRect(x, y,
            Math.Min(bounds.Width, (uint)Math.Max(0, width) - x),
            Math.Min(bounds.Height, (uint)Math.Max(0, height) - y));
    }

    /// <summary>Mirror of <c>OcrPipeline.EffectiveScale</c>.</summary>
    public static double ClampScale(RoiRect bounds, double scale, int maxImageDimension)
    {
        var largestSide = Math.Max(bounds.Width, bounds.Height);
        return largestSide * scale > maxImageDimension ? (double)maxImageDimension / largestSide : scale;
    }
}
