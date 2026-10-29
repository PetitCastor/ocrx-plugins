namespace ItemTrackerPlugin.Window;

/// <summary>
/// Closing the window only hides it, which nothing on screen says. The first close of each run gets
/// a tray balloon saying where the window went; later closes stay quiet.
/// </summary>
internal sealed class CloseNotice
{
    internal const string Title = "Item Tracker";

    internal const string Text = "Item Tracker is still running. Click its icon in the notification area to open it again.";

    private int _shown;

    /// <summary>True on the first call only, whatever thread makes it.</summary>
    public bool TakeFirst() => Interlocked.Exchange(ref _shown, 1) == 0;
}
