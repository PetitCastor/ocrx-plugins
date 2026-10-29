using System.Text.RegularExpressions;

namespace ItemTrackerPlugin.Screen;

/// <summary>
/// Reads the lines OCR returns for a tooltip region. Windows OCR is right about the words and
/// wrong about the separators, so every field tolerates what the corpus showed: <c>pscu</c> for
/// µSCU, <c>Klaus 6 Werner</c> for <c>Klaus &amp; Werner</c>, and a label glued to its value
/// (<c>Item TypesLight Armor</c>, <c>Item Type}Racing Helmet</c>).
/// </summary>
public static partial class TooltipParser
{
    /// <summary>Item types seen so far. A glued value is cut at the longest of these it ends with;
    /// one that is not here is still read, with leading punctuation stripped.</summary>
    private static readonly string[] KnownItemTypes =
    [
        "Light Armor", "Medium Armor", "Heavy Armor", "Racing Helmet", "Medical Device", "Laser Repeater",
        "Laser Cannon", "Sniper Rifle", "Assault Rifle", "Pistol", "Helmet", "Undersuit", "Backpack",
        "Shotgun", "Knife", "Gadget", "Grenade", "Food", "Drink", "Magazine", "Attachment", "Multitool",
        "Torso", "Arms", "Legs", "Hat", "Gloves", "Jacket", "Shirt", "Pants", "Boots", "Rifle", "SMG",
    ];

    /// <summary>Null when the lines are not an item tooltip: no <c>Volume:</c> line, as in the
    /// Freight Manager's junk or a button label.</summary>
    public static TooltipReading? Parse(IReadOnlyList<string> lines)
    {
        var block = FirstBlock(lines.Select(l => OcrDigits.Normalise(l).Trim()).Where(l => l.Length > 0).ToList());
        var volumeAt = block.FindIndex(l => VolumeLine().IsMatch(l));
        if (volumeAt < 1) return null;

        var volumeText = VolumeLine().Match(block[volumeAt]).Groups["v"].Value;
        if (!long.TryParse(volumeText, out var volume)) return null;

        // Containers print their Capacity before the Volume; neither belongs to the name.
        var nameEnd = block.FindIndex(l => CapacityLine().IsMatch(l));
        var name = string.Join(' ', block.Take(nameEnd is >= 1 && nameEnd < volumeAt ? nameEnd : volumeAt)).Trim();
        if (name.Length == 0) return null;

        string? manufacturer = null, itemType = null, cls = null, ammo = null, contents = null;
        int? size = null;
        var registered = false;

        foreach (var line in block.Skip(volumeAt + 1))
        {
            if (line.StartsWith("Registered To You", StringComparison.OrdinalIgnoreCase)) registered = true;
            else if (Value(line, "Manufacturer") is { } m) manufacturer = FixAmpersand(m);
            else if (Value(line, "Item Type") is { } t) itemType = ResolveItemType(t);
            else if (Value(line, "Class") is { } c) cls = c;
            else if (Value(line, "Ammo Count") is { } a) ammo = a;
            else if (Value(line, "Contents Remaining") is { } r) contents = r;
            else if (Value(line, "Size") is { } s && int.TryParse(s.Trim(), out var n)) size = n;
        }

        return new TooltipReading(name, volume, manufacturer, itemType, cls, size, ammo, contents, registered);
    }

    /// <summary>
    /// A worn item makes the game stack a second tooltip, the comparison, under the first. It
    /// starts at the name line above the second <c>Volume:</c>, so cut there.
    /// </summary>
    private static List<string> FirstBlock(List<string> lines)
    {
        var volumes = Enumerable.Range(0, lines.Count).Where(i => VolumeLine().IsMatch(lines[i])).ToList();
        return volumes.Count >= 2 && volumes[1] - 1 > volumes[0] ? lines[..(volumes[1] - 1)] : lines;
    }

    private static string? Value(string line, string label)
    {
        if (!line.StartsWith(label, StringComparison.OrdinalIgnoreCase)) return null;

        var rest = line[label.Length..];
        // A label is followed by a separator, except Item Type, whose value OCR often glues on.
        if (!label.Equals("Item Type", StringComparison.OrdinalIgnoreCase) && !SeparatorFirst().IsMatch(rest)) return null;

        return rest.TrimStart(':', ';', ' ').Trim();
    }

    private static string ResolveItemType(string value)
    {
        foreach (var known in KnownItemTypes.OrderByDescending(k => k.Length))
            if (value.EndsWith(known, StringComparison.OrdinalIgnoreCase) && value.Length - known.Length <= 3)
                return known;

        return LeadingJunk().Replace(value, "").Trim();
    }

    /// <summary>OCR reads the ampersand of <c>Klaus &amp; Werner</c> as a 6 or an 8.</summary>
    private static string FixAmpersand(string text) => Ampersand().Replace(text, " & ");

    [GeneratedRegex(@"^Volume\s*[:;]?\s*(?<v>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolumeLine();

    [GeneratedRegex(@"^Capacity\s*[:;]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CapacityLine();

    [GeneratedRegex(@"^\s*[:;]", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorFirst();

    [GeneratedRegex(@"^[^A-Za-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingJunk();

    [GeneratedRegex(@"(?<=[A-Za-z]) [68] (?=[A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex Ampersand();
}
