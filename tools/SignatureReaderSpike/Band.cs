namespace SignatureReaderSpike;

/// <summary>
/// The fixed search band, in frame pixels. Height-anchored like Star Citizen's HUD: every reference
/// distance scales with frame height / 1440 and x is measured from the frame's centre column.
/// It covers the badge row across the whole FOV range (the row moves from y 462 to y 566 at 1440).
/// 240x190 reference pixels is 45,600 px, inside the 65,536 px budget of a Pixels ROI at 1440p.
/// </summary>
internal static class Band
{
    public const double Left = 100;
    public const double Right = 140;
    public const double Top = 420;
    public const double Bottom = 610;

    /// <summary>How far the corpus crops reach past the band, so a shifted band can be cut from them.</summary>
    public const double MarginX = 60;
    public const double MarginY = 25;

    public static double Scale(int frameHeight) => frameHeight / 1440.0;

    public static Box Of(int frameWidth, int frameHeight)
    {
        var s = Scale(frameHeight);
        var x0 = (int)Math.Round(frameWidth / 2.0 - Left * s);
        var x1 = (int)Math.Round(frameWidth / 2.0 + Right * s);
        var y0 = (int)Math.Round(Top * s);
        var y1 = (int)Math.Round(Bottom * s);
        return new Box(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>The band grown by the corpus margin on every side, still in frame pixels.</summary>
    public static Box WithMargin(int frameWidth, int frameHeight)
    {
        var band = Of(frameWidth, frameHeight);
        var (mx, my) = Margin(frameHeight);
        return new Box(band.X - mx, band.Y - my, band.Width + 2 * mx, band.Height + 2 * my);
    }

    public static (int X, int Y) Margin(int frameHeight)
    {
        var s = Scale(frameHeight);
        return ((int)Math.Round(MarginX * s), (int)Math.Round(MarginY * s));
    }
}
