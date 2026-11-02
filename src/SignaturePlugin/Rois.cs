using Ocrx.Contracts;
using Ocrx.Sdk;

namespace SignaturePlugin;

/// <summary>
/// The regions this plugin subscribes, in reference space (2560x1440), as calibrated. The live set
/// is these rects zoomed for Star Citizen's current FOV (<see cref="StarCitizenFovZoom"/>), with the
/// counter then moved onto the number wherever <see cref="BadgeTracker"/> last read it, plus, briefly, one
/// Detailed OCR tile while it is looking for a number it has lost. <see cref="SignaturePlugin.Rois"/> serves that
/// set and pushes it to the engine whenever it changes.
/// </summary>
public static class Rois
{
    /// <summary>The RS signature number on the floating pin-badge over a scanned target — not
    /// part of the READY TO SCAN MFD panel, which sits separately at screen left. Calibrated
    /// 2026-08-23 against a real 2560x1440 engine capture of a completed mining scan
    /// (`frame_20260823_164053_757.png`, signature 3,400 / Lindinium): verified by direct
    /// OcrPipeline probe, not just visual inspection — a tighter, text-only crop at this same
    /// scale reads "3800" (Windows OCR confuses the HUD font's "4" for "8" without margin around
    /// the glyphs) or returns nothing; this rect's ~20px left/right and ~14px top/bottom margin
    /// around the digits is load-bearing, not slack to trim. The pin icon's densest column sits
    /// at x≈1264, this rect's left edge — a sliver of it is inside the crop, which does not stop
    /// OCR from reading cleanly.
    /// <para>
    /// The badge's measured center (x≈1281.5, ≈50.06% of 2560) matches a screen-centered locked-
    /// target reticle, which is why a fixed rect is plausible here at all — but that is inferred
    /// from this one frame, not confirmed across a scan where the target sits elsewhere on
    /// screen. If a future capture shows the badge off-center, this rect is calibrated to one
    /// camera angle, not a stable HUD element, and needs to be recalibrated. A wider reading
    /// (e.g. a six-digit cluster total) grows left toward the pin — the tighter margin — so watch
    /// that direction first if OCR starts missing again.
    /// </para>
    /// <para>
    /// That warning has since come true, and this rect was too tight for five-digit cluster
    /// totals — recalibrated 2026-08-25 (see below) rather than widened, since the root cause was
    /// OCR channel/scale, not crop size.
    /// </para>
    /// Scale is 6.0, selected 2026-08-25 from a 49-sample scale/channel sweep (red channel + Cubic
    /// read 47/49, versus 28/49 at the old 3.0). The sweep itself is not checked in; the shipped
    /// replay corpus currently has one four-digit frame, so add representative five- and six-digit
    /// frames before treating this setting as replay-proven for wide readings. Do not turn it back
    /// down toward 2-4 without that comparison.</summary>
    /// <remarks>A <c>Detailed</c> ROI rather than <c>Text</c>, so each read also says where the number is. The
    /// engine runs the same crop, scale 6, red-channel grayscale and recognition for both kinds and the text is
    /// the same (58 of 58 counter crops and 720 of 720 shifted rects on four cockpit frames read identically);
    /// the cost is the same OCR pass. Calibrated at a vertical FOV at or below <see cref="StarCitizenFovZoom.FloorDegrees"/>,
    /// where the badge does not move; wider FOVs zoom it toward screen centre.
    /// <para>
    /// This is where the badge sat when calibrated, not where it stays: the badge is anchored to the
    /// scanned target in world space, so it crosses the screen as the ship turns (measured
    /// 2026-11-01: 8-10 px per 100 ms, leaving even a 400x260 rect within seconds). With tracking
    /// on, this rect is the starting point and the fallback; <see cref="BadgeTracker"/> moves it onto
    /// the number OCR finds, by <see cref="DigitsMarginLeft"/> and <see cref="DigitsCentreY"/>. The scale is
    /// kept; the rect is never smaller than this one and grows — with the number's width, and ahead of its
    /// motion — so the margins above only ever get bigger.
    /// </para></remarks>
    public static readonly RoiSubscription Counter =
        new("counter", new RoiRect(1264, 454, 70, 44), 6.0, RoiKind.Detailed);

    /// <summary>Where the digits sit inside <see cref="Counter"/>, in reference pixels at FOV zoom 1,
    /// measured as the OCR word box of "2,000" in the counter reads the user confirmed at the calibrated
    /// spot (counter_20261101_091807_268, 091908_836, 091928_626, 091937_785: scale-6 reads of the 70x44
    /// rect, all giving a 46 px wide word box starting 9-11 px from the rect's left edge). Left margin 9
    /// and right margin 70 - 9 - 46 = 15; the digits' vertical centre 17.5 below the rect's top. The
    /// tracker (<see cref="BadgeTracker"/>) places the rect from the word box OCR finds by these numbers
    /// and widens it for wider words, so a six-digit number keeps the 15 px right margin instead of being
    /// cut; those margins are the load-bearing ones described above.</summary>
    public const int DigitsMarginLeft = 9, DigitsMarginRight = 15, DigitsNominalWidth = 46;

    /// <summary>See <see cref="DigitsMarginLeft"/>: the digits' centre sits <see cref="DigitsCentreY"/> below the
    /// rect's top and this far above its bottom (44 - 17.5).</summary>
    public const double DigitsCentreY = 17.5, DigitsBelowCentre = 26.5;

    /// <summary>A field, not <c>=> [Counter]</c>: the set never changes, and an
    /// expression-bodied property would build a fresh array on every read.</summary>
    public static readonly IReadOnlyList<RoiSubscription> All = [Counter];
}
