using Ocrx.Sdk;

namespace SignaturePlugin;

/// <summary>Applies the SignaturePlugin-owned visual presets to its optional overlay sink.</summary>
internal static class OverlayThemes
{
    public const string Default = "default";
    public const string Citizen = "citizen";
    public const string Retro = "retro";

    /// <summary>
    /// The selectable themes and their display labels — the single source the settings spec is
    /// generated from, so the projected option list can never drift from what <see cref="Apply"/>
    /// understands. The value strings are the SignaturePlugin-owned ids that travel the wire; the
    /// labels are plugin presentation the engine shows verbatim.
    /// </summary>
    public static readonly IReadOnlyList<SettingsOption> Options =
    [
        new SettingsOption(Default, "Default"),
        new SettingsOption(Citizen, "Star Citizen"),
        new SettingsOption(Retro, "Retro"),
    ];

    /// <summary>The theme value in the canonical form <see cref="Apply"/> and <see cref="Options"/>
    /// compare against: trimmed and lower-cased, defaulting a null/blank to <see cref="Default"/>.</summary>
    public static string Normalize(string? theme) =>
        string.IsNullOrWhiteSpace(theme) ? Default : theme.Trim().ToLowerInvariant();

    /// <summary>Whether <paramref name="theme"/> (already normalized) is one this plugin can apply.</summary>
    public static bool IsKnown(string theme) =>
        Options.Any(option => string.Equals(option.Value, theme, StringComparison.Ordinal));

    public static void Apply(SignaturePluginConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var theme = Normalize(config.OverlayTheme);
        var overlay = config.Outputs
            .FirstOrDefault(output => string.Equals(output.Type, "overlay", StringComparison.OrdinalIgnoreCase))
            ?.Overlay;

        switch (theme)
        {
            case Default:
                return;
            case Citizen:
                if (overlay is not null)
                    ApplyCitizen(overlay);
                return;
            case Retro:
                if (overlay is not null)
                    ApplyRetro(overlay);
                return;
            default:
                throw new ArgumentException(
                    $"Unsupported overlayTheme '{config.OverlayTheme}'. Use '{Default}', '{Citizen}', or '{Retro}'.",
                    nameof(config.OverlayTheme));
        }
    }

    private static void ApplyCitizen(OverlaySpec overlay)
    {
        overlay.Width = 560;
        overlay.Height = 76;
        overlay.FontFamily = "Rajdhani SemiBold";
        overlay.FontFile = FontPath("Rajdhani-SemiBold.ttf");
        overlay.FontSize = 28;
        overlay.UsePixelText = false;
        overlay.ForegroundColor = "#B8F2FF";
        overlay.BackgroundColor = "#071D2B";
        overlay.BackgroundAlpha = 235;
        overlay.BorderColor = "#32BDE6";
        overlay.BorderWidth = 2;
        overlay.CornerRadius = 4;
        overlay.CornerAccentLength = 18;
        overlay.Padding = 16;
    }

    private static void ApplyRetro(OverlaySpec overlay)
    {
        overlay.Width = 560;
        overlay.Height = 88;
        overlay.FontFamily = "Press Start 2P";
        overlay.FontFile = FontPath("PressStart2P-Regular.ttf");
        overlay.FontSize = 24;
        overlay.UsePixelText = true;
        overlay.ForegroundColor = "#FFEF5A";
        overlay.BackgroundColor = "#1A1435";
        overlay.BackgroundAlpha = 240;
        overlay.BorderColor = "#6B5BA8";
        overlay.BorderWidth = 1;
        overlay.CornerRadius = 0;
        overlay.CornerAccentLength = 0;
        overlay.Padding = 16;
    }

    private static string FontPath(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", fileName);
        if (!File.Exists(path))
            throw new InvalidOperationException($"The selected overlay theme requires packaged font '{fileName}', but it was not found at '{path}'.");
        return path;
    }
}
