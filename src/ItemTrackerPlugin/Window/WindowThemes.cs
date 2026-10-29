using Microsoft.Win32;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.Window;

/// <summary>
/// The "Window theme" setting: how a <see cref="WindowTheme"/> travels as a Select value and is kept
/// in settings.json, and whether it comes out dark right now.
/// </summary>
internal static class WindowThemes
{
    private const string SystemValue = "system";
    private const string LightValue = "light";
    private const string DarkValue = "dark";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    internal static IReadOnlyList<SettingsOption> Options { get; } =
    [
        new(SystemValue, "Follow Windows"),
        new(LightValue, "Light"),
        new(DarkValue, "Dark"),
    ];

    internal static string Format(WindowTheme theme) => theme switch
    {
        WindowTheme.Light => LightValue,
        WindowTheme.Dark => DarkValue,
        _ => SystemValue,
    };

    /// <summary>One of the option values, in any case; anything else is not an edit the plugin can use.</summary>
    internal static bool TryParse(string? value, out WindowTheme theme)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case SystemValue: theme = WindowTheme.System; return true;
            case LightValue: theme = WindowTheme.Light; return true;
            case DarkValue: theme = WindowTheme.Dark; return true;
            default: theme = WindowTheme.System; return false;
        }
    }

    internal static bool IsDark(WindowTheme theme) => theme switch
    {
        WindowTheme.Light => false,
        WindowTheme.Dark => true,
        _ => SystemAppsAreDark(),
    };

    /// <summary>
    /// The Windows app mode. The value is absent on installs that never changed it, which means light.
    /// </summary>
    private static bool SystemAppsAreDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
