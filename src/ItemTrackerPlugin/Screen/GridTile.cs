namespace ItemTrackerPlugin.Screen;

/// <summary>A tile of the right-hand grid: zero-based row and first slot column, and how many slot
/// columns it spans (two for the wide tiles).</summary>
public readonly record struct GridTile(int Row, int Column, int Span);
