using System.Runtime.InteropServices;

namespace ItemTrackerPlugin.Window;

/// <summary>
/// The window's title bar, which WinForms always draws light. Windows 10 20H1 and later draw it dark
/// on request; older builds ignore the request and keep it light, which is harmless. A window already
/// shown keeps its old title bar until its frame is redrawn, so the change asks for that too.
/// </summary>
internal static class TitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const uint SwpFrameUnchangedElsewhere = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020; // NOSIZE NOMOVE NOZORDER NOACTIVATE FRAMECHANGED

    internal static void Apply(IntPtr handle, bool dark)
    {
        var on = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
        _ = SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpFrameUnchangedElsewhere);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
