using Ocrx.Sdk;

namespace SignaturePlugin;

/// <summary>Applies the user's chosen overlay position to the optional overlay sink.</summary>
internal static class OverlayPositions
{
    public const string TopCenter = "topcenter";

    /// <summary>
    /// The selectable positions and their display labels — one per <see cref="OverlayAnchor"/> value
    /// except Custom, since this plugin never projects raw X/Y. The single source the settings spec
    /// is generated from, so the projected option list can never drift from what <see cref="Apply"/>
    /// understands.
    /// </summary>
    public static readonly IReadOnlyList<SettingsOption> Options =
    [
        new SettingsOption("topleft", "Top Left"),
        new SettingsOption(TopCenter, "Top Center"),
        new SettingsOption("topright", "Top Right"),
        new SettingsOption("middleleft", "Middle Left"),
        new SettingsOption("center", "Center"),
        new SettingsOption("middleright", "Middle Right"),
        new SettingsOption("bottomleft", "Bottom Left"),
        new SettingsOption("bottomcenter", "Bottom Center"),
        new SettingsOption("bottomright", "Bottom Right"),
    ];

    /// <summary>The position value in the canonical form <see cref="Apply"/> and <see cref="Options"/>
    /// compare against: trimmed and lower-cased, defaulting a null/blank to <see cref="TopCenter"/>.</summary>
    public static string Normalize(string? position) =>
        string.IsNullOrWhiteSpace(position) ? TopCenter : position.Trim().ToLowerInvariant();

    /// <summary>Whether <paramref name="position"/> (already normalized) is one this plugin can apply.</summary>
    public static bool IsKnown(string position) =>
        Options.Any(option => string.Equals(option.Value, position, StringComparison.Ordinal));

    public static void Apply(SignaturePluginConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var position = Normalize(config.Position);
        if (!IsKnown(position))
            throw new ArgumentException(
                $"Unsupported position '{config.Position}'. Use one of: {string.Join(", ", Options.Select(option => option.Value))}.",
                nameof(config.Position));

        var overlay = config.Outputs
            .FirstOrDefault(output => string.Equals(output.Type, "overlay", StringComparison.OrdinalIgnoreCase))
            ?.Overlay;
        if (overlay is null) return;

        overlay.Anchor = Enum.Parse<OverlayAnchor>(position, ignoreCase: true);
    }
}
