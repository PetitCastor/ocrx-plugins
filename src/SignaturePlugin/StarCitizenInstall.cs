using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SignaturePlugin;

/// <summary>
/// Finds the running game's profile <c>attributes.xml</c>, where it stores the FOV.
/// </summary>
/// <remarks>
/// The channel root (LIVE, PTU, ...) is the parent of <c>Bin64\StarCitizen.exe</c>, and the file sits
/// at <c>user\client\0\Profiles\default\attributes.xml</c> under it. <c>default</c> is the profile
/// Game.log reports activating (<c>ActivateProfile profileName default</c>); no other name has been
/// seen. The image path is read with <c>PROCESS_QUERY_LIMITED_INFORMATION</c>, the same right the
/// engine's window enumerator uses, because <see cref="Process.MainModule"/> needs a read handle an
/// anti-cheat protected process may refuse.
/// </remarks>
internal static class StarCitizenInstall
{
    private const string ProcessName = "StarCitizen";
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref int size);

    /// <summary>The running game's attributes.xml, or null when the game is not running or the file
    /// is not where it should be.</summary>
    public static string? FindAttributesFile()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                if (ImagePath((uint)process.Id) is { } exe && AttributesFileFor(exe) is { } file && File.Exists(file))
                    return file;
            }
        }

        return null;
    }

    /// <summary>Where attributes.xml lives for a given <c>StarCitizen.exe</c> path.</summary>
    internal static string? AttributesFileFor(string exePath)
    {
        var bin = Path.GetDirectoryName(exePath);
        var channel = bin is null ? null : Path.GetDirectoryName(bin);
        return channel is null
            ? null
            : Path.Combine(channel, "user", "client", "0", "Profiles", "default", "attributes.xml");
    }

    private static string? ImagePath(uint processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var size = 1024;
            var buffer = new StringBuilder(size);
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
