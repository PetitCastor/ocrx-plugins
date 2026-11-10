namespace SignatureReaderSpike;

/// <summary>An integer pixel rectangle.</summary>
internal readonly record struct Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}
