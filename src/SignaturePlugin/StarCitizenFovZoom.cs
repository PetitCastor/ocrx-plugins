using Ocrx.Contracts;
using Ocrx.Sdk;

namespace SignaturePlugin;

/// <summary>
/// How Star Citizen's field-of-view setting moves the RS badge, and the counter ROI with it.
/// </summary>
/// <remarks>
/// The game draws its HUD in the 3D scene, so the FOV zooms every HUD element about screen centre:
/// the badge's offset above y=720 and its size both follow <c>1/tan(vFOV/2)</c>. Measured
/// 2026-09-29 at 2560x1440 Borderless on one locked rock, slider 85..116 (the game's whole range):
/// offset·tan(vFOV/2) held at 144.1/144.2/144.0 px for sliders 100/110/116, and the badge centre
/// stayed at x≈1283.
/// <para>
/// Below a vertical FOV of about 60.3° (slider ≈92) the badge stops moving: sliders 85 and 90 put
/// it on the same pixels, both matching an effective 60.2-60.3°. The game appears to floor the FOV
/// it actually renders with — most likely its auto-zoom on the locked target, which the user keeps
/// at its default and cannot find in the options. <see cref="Rois.Counter"/> was calibrated inside
/// that flat zone, so the floor is also the calibration FOV and the zoom factor is 1 anywhere at or
/// below it.
/// </para>
/// <para>
/// The OCR scale is divided by the same factor so the upscaled crop the engine hands Windows OCR
/// stays the size it was calibrated at. Replaying the sweep's frames: at slider 100 the zoomed rect
/// reads '17,020' where the fixed rect reads nothing; at 116 only the compensated scale reads.
/// </para>
/// </remarks>
internal static class StarCitizenFovZoom
{
    /// <summary>The smallest vertical FOV, in degrees, the game actually renders with.</summary>
    public const double FloorDegrees = 60.3;

    /// <summary>Cap on the compensated OCR scale; the engine clamps further for Windows OCR's
    /// maximum dimension, so this only keeps a nonsense FOV from asking for an absurd upscale.</summary>
    public const double MaxOcrScale = 12.0;

    private const double CentreX = 1280;
    private const double CentreY = 720;

    private static readonly double FloorTan = Math.Tan(FloorDegrees / 2 * Math.PI / 180);

    /// <summary>
    /// The zoom factor for a stored vertical FOV: 1 at or below the floor, shrinking as the FOV
    /// widens. Null, non-finite or out-of-range input answers 1 — the calibrated rect — rather than
    /// guessing.
    /// </summary>
    public static double Factor(double? verticalFovDegrees)
    {
        if (verticalFovDegrees is not { } fov || !double.IsFinite(fov) || fov <= 0 || fov >= 179)
            return 1.0;

        var effective = Math.Max(fov, FloorDegrees);
        return FloorTan / Math.Tan(effective / 2 * Math.PI / 180);
    }

    /// <summary>
    /// <paramref name="roi"/> zoomed about the reference-space screen centre by
    /// <paramref name="factor"/>, with its OCR scale divided by the same factor. The id and kind are
    /// kept, which is what lets a tick already in flight still match.
    /// </summary>
    public static RoiSubscription Apply(RoiSubscription roi, double factor)
    {
        if (factor == 1.0)
            return roi;

        var r = roi.Rect;
        var left = Math.Round(CentreX + (r.X - CentreX) * factor);
        var top = Math.Round(CentreY + (r.Y - CentreY) * factor);
        var right = Math.Round(CentreX + (r.X + r.Width - CentreX) * factor);
        var bottom = Math.Round(CentreY + (r.Y + r.Height - CentreY) * factor);
        var rect = new RoiRect((uint)left, (uint)top, (uint)(right - left), (uint)(bottom - top));

        return roi with { Rect = rect, Scale = Math.Min(roi.Scale / factor, MaxOcrScale) };
    }
}
