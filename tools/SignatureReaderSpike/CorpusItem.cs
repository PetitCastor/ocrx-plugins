namespace SignatureReaderSpike;

/// <summary>
/// One labelled corpus crop: the band plus its margin, cut from a frame of the recorded size.
/// <see cref="Value"/> is null for a crop that has not been labelled yet.
/// </summary>
internal sealed record CorpusItem(string Name, int? Value, string Font, int FrameWidth, int FrameHeight, Bgra Wide)
{
    public double Scale => Band.Scale(FrameHeight);

    /// <summary>The band as the plugin would receive it, moved by an offset given in reference pixels.</summary>
    public Bgra BandAt(double shiftX = 0, double shiftY = 0)
    {
        var band = Band.Of(FrameWidth, FrameHeight);
        var (mx, my) = Band.Margin(FrameHeight);
        return Wide.Crop(mx + (int)Math.Round(shiftX * Scale), my + (int)Math.Round(shiftY * Scale), band.Width, band.Height);
    }
}
