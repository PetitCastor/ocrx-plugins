using System.Text.RegularExpressions;
using ItemTrackerPlugin.Naming;

namespace ItemTrackerPlugin.Screen;

/// <summary>
/// Maps a tooltip's display name back to the game's item class (<c>item_NameBEHR_LaserCannon_S2</c>
/// gives <c>BEHR_LaserCannon_S2</c>), through the same <c>global.ini</c> the place names use. Two
/// classes can share a display name; the tooltip's <c>Size</c> breaks the tie when the class names
/// carry a <c>_S&lt;n&gt;</c> suffix, and anything still ambiguous resolves to nothing rather than
/// to a guess.
/// </summary>
public sealed partial class ItemClassResolver
{
    private const string NameKeyPrefix = "item_Name";

    private readonly Dictionary<string, List<string>> _byName = [];

    public ItemClassResolver(GameLocalization text)
    {
        foreach (var (key, value) in text.Entries)
        {
            if (!key.StartsWith(NameKeyPrefix, StringComparison.OrdinalIgnoreCase) || key.Length == NameKeyPrefix.Length) continue;

            var display = Normalise(value);
            if (display.Length == 0) continue;

            if (!_byName.TryGetValue(display, out var classes)) _byName[display] = classes = [];
            // Some keys put an underscore between the prefix and the class: item_Name_thp_light_arms_01_01_01.
            classes.Add(key[NameKeyPrefix.Length..].TrimStart('_'));
        }
    }

    public string? Resolve(TooltipReading tooltip)
    {
        if (!_byName.TryGetValue(Normalise(tooltip.Name), out var classes)) return null;
        if (classes.Count == 1) return classes[0];

        if (tooltip.Size is { } size)
        {
            var sized = classes.Where(c => c.EndsWith($"_S{size}", StringComparison.OrdinalIgnoreCase)).ToList();
            if (sized.Count == 1) return sized[0];
        }

        return null;
    }

    /// <summary>Case, spacing and the typographic quotes are what OCR and the localization disagree on.</summary>
    internal static string Normalise(string name) =>
        Spaces().Replace(name.Replace('“', '"').Replace('”', '"').Replace('’', '\'').Trim(), " ").ToUpperInvariant();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
