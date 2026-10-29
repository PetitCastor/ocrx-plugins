namespace ItemTrackerPlugin.Screen;

/// <param name="Context">The screen the headers name.</param>
/// <param name="Detail">The ship model for <see cref="ScreenContext.ShipStorage"/>, as the game
/// prints it; null otherwise.</param>
public sealed record ScreenHeader(ScreenContext Context, string? Detail)
{
    public static readonly ScreenHeader Unknown = new(ScreenContext.Unknown, null);
}
