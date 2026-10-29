namespace ItemTrackerPlugin.Window;

/// <summary>
/// What appears when the plugin starts. Asked once per run, on the first successful start: a reconnect
/// to the engine must not pop the window back up after the user closed it, and applying the setting
/// changes the next start, never the window now. A replay shows nothing.
/// </summary>
internal static class WindowStartup
{
    internal static WindowStart Decide(bool replayMode, bool openAtStartup) =>
        replayMode ? WindowStart.None
        : openAtStartup ? WindowStart.Open
        : WindowStart.TrayOnly;

    /// <summary>A Toggle travels as "true" or "false"; anything else is not an edit the plugin can use.</summary>
    internal static bool TryParseToggle(string value, out bool on) => bool.TryParse(value.Trim(), out on);

    internal static string FormatToggle(bool on) => on ? "true" : "false";
}
