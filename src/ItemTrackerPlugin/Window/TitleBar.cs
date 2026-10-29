using System.Runtime.InteropServices;

namespace ItemTrackerPlugin.Window;

/// <summary>
/// The window's title bar, which WinForms always draws light. Windows 10 20H1 and later draw it dark
/// on request; older builds ignore the request and keep it light, which is harmless.
/// </summary>
internal static class TitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    internal static void Apply(IntPtr handle, bool dark)
    {
        var on = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
