using Ocrx.Sdk;

namespace SignaturePlugin;

internal sealed class SignaturePluginConfig : PluginConfig
{
    public string OverlayTheme { get; set; } = OverlayThemes.Default;
    public string Position { get; set; } = OverlayPositions.TopCenter;

    /// <summary>
    /// When <c>saveDebugFrames</c> is also on, a manual tick (the engine's hotkey) writes the whole frame as well
    /// as the counter crop. Off by default: a full 2560x1440 PNG is about 4 MB per press, and the crop is what
    /// calibration of the counter needs. Turn it on to record frames for the tracker's tests.
    /// </summary>
    public bool DumpFullFrameOnManual { get; set; }
}

internal static class UserConfig
{
    private const string ResourceName = "SignaturePlugin.config.json";

    public static string Ensure() =>
        ConfigSeed.EnsureInLocalAppData(typeof(UserConfig).Assembly, ResourceName, "SignaturePlugin");
}
