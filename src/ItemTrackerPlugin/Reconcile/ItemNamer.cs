using System.Text.RegularExpressions;
using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Screen;

namespace ItemTrackerPlugin.Reconcile;

/// <summary>
/// The screen never shows a class and the ledger never a name, so the two meet on the display name.
/// A ledger class is named by the game's own <c>global.ini</c>, where a variant often has no key of
/// its own (<c>rsi_odyssey_undersuit_helmet_01_01_01</c> is under <c>_rsi_odyssey_undersuit_helmet_01</c>),
/// so trailing numeric segments are dropped until a key matches; then by the fallback (the wiki).
/// </summary>
public sealed partial class ItemNamer(GameLocalization text, Func<string, string?>? fallback = null)
{
    private readonly Dictionary<string, string?> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The display name the game gives a class, or null when nothing names it.</summary>
    public string? Name(string itemClass)
    {
        for (var stem = itemClass; stem.Length > 0; stem = TrimNumericSegment().Replace(stem, ""))
        {
            if ((text.Get("item_Name" + stem) ?? text.Get("item_Name_" + stem)) is { } name) return name;
            if (!TrimNumericSegment().IsMatch(stem)) break;
        }

        return fallback?.Invoke(itemClass);
    }

    /// <summary>The match key of a class: <see cref="Key(string)"/> of its name, or null when unnamed.</summary>
    public string? KeyOf(string itemClass)
    {
        if (_keys.TryGetValue(itemClass, out var key)) return key;
        return _keys[itemClass] = Name(itemClass) is { } name ? Key(name) : null;
    }

    /// <summary>
    /// What a name is compared by. OCR swaps I, l and 1 depending on the language pack build ("Odyssey
    /// II" or "Odyssey 11"), so they are one letter here; two real names that differ only by those are
    /// not worth the misses the swap would cause.
    /// </summary>
    public static string Key(string name) =>
        ItemClassResolver.Normalise(name).Replace('L', '1').Replace('I', '1');

    [GeneratedRegex(@"_\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrimNumericSegment();
}
