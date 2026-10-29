namespace ItemTrackerPlugin.Window;

/// <summary>What the plugin puts on screen when it starts.</summary>
internal enum WindowStart
{
    /// <summary>A replay: no tray icon, no window.</summary>
    None,

    /// <summary>The tray icon only; the window waits for a click.</summary>
    TrayOnly,

    /// <summary>The tray icon, and the window shown at once.</summary>
    Open,
}
