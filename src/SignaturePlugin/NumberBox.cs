using Ocrx.Contracts;

namespace SignaturePlugin;

/// <summary>
/// The box OCR drew around the signature number (the digits, not the badge around them) as a pixel
/// rectangle. <see cref="SignatureCandidates"/> reports it in frame pixels, the capture's own;
/// <see cref="BadgeTracker"/> keeps it in reference space (2560x1440), which is what survives a
/// resolution change.
/// </summary>
internal readonly record struct NumberBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public double CentreX => X + Width / 2.0;
    public double CentreY => Y + Height / 2.0;

    public RoiRect ToRect() => new((uint)X, (uint)Y, (uint)Width, (uint)Height);
}
