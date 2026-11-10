namespace SignatureReaderSpike;

/// <summary>A located badge inside a band: the digit run, its commas, and the pin icon to its left.</summary>
internal sealed record Badge(List<Comp> Digits, List<Comp> Commas, Box Icon, int Top, int DigitHeight)
{
    public int Left => Digits[0].X;

    public int Right => Digits[^1].Right;

    public Box Extent
    {
        get
        {
            var top = Math.Min(Icon.Y, Top);
            var bottom = Math.Max(Icon.Bottom, Top + DigitHeight);
            return new Box(Icon.X, top, Right - Icon.X, bottom - top);
        }
    }
}
