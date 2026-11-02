using Ocrx.Sdk;

namespace SignaturePlugin;

internal sealed class SignaturePluginConfig : PluginConfig
{
    public string OverlayTheme { get; set; } = OverlayThemes.Default;
    public string Position { get; set; } = OverlayPositions.TopCenter;

    /// <summary>The overlay's size as a percentage of its full size, 50 to 100; see <see cref="OverlayScales"/>.</summary>
    public int OverlayScale { get; set; } = OverlayScales.Default;
}

internal static class UserConfig
{
    private const string ResourceName = "SignaturePlugin.config.json";

    public static string Ensure() =>
        ConfigSeed.EnsureInLocalAppData(typeof(UserConfig).Assembly, ResourceName, "SignaturePlugin");
}
