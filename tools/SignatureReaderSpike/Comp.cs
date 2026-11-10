namespace SignatureReaderSpike;

/// <summary>One connected blob of ink: its label in the label map, its bounding box and its pixel count.</summary>
internal sealed record Comp(int Label, int X, int Y, int Width, int Height, int Area)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}
