using Ocrx.Contracts;

namespace SignaturePlugin;

/// <summary>
/// How the 2560x1440 reference canvas sits inside a captured frame, so a pixel position found in a
/// patch (frame space) can be turned back into the reference space ROIs are declared in.
/// </summary>
/// <remarks>
/// Mirrors the SDK's own fit — uniform scale by the smaller axis ratio, canvas centred — because the
/// SDK only exposes the forward direction publicly. It is exact on every frame at least as wide as
/// 16:9, where the SDK's two scale modes agree; on a taller frame the engine may scale by height
/// instead, and the positions this reports would be off by the difference.
/// </remarks>
internal readonly record struct ReferenceFit(double Scale, double OffsetX, double OffsetY)
{
    public static ReferenceFit For(int frameWidth, int frameHeight)
    {
        var scale = Math.Min((double)frameWidth / RoiScaler.ReferenceWidth, (double)frameHeight / RoiScaler.ReferenceHeight);
        return new ReferenceFit(scale,
            (frameWidth - RoiScaler.ReferenceWidth * scale) / 2.0,
            (frameHeight - RoiScaler.ReferenceHeight * scale) / 2.0);
    }

    public double ToFrameX(double referenceX) => OffsetX + referenceX * Scale;
    public double ToFrameY(double referenceY) => OffsetY + referenceY * Scale;
    public double ToReferenceX(double frameX) => (frameX - OffsetX) / Scale;
    public double ToReferenceY(double frameY) => (frameY - OffsetY) / Scale;
}
