using System.Text.RegularExpressions;

namespace ItemTrackerPlugin.Screen;

/// <summary>The stack count on a tile's corner: <c>2x</c>, <c>5x</c>, or <c>X1</c>.</summary>
public static partial class QuantityBadgeParser
{
    public static int? Parse(string text)
    {
        var match = Badge().Match(OcrDigits.Normalise(text).Trim());
        if (!match.Success) return null;

        var digits = match.Groups["a"].Success ? match.Groups["a"].Value : match.Groups["b"].Value;
        return int.TryParse(digits, out var n) && n >= 1 ? n : null;
    }

    [GeneratedRegex(@"^(?:(?<a>\d{1,4})\s*[xX×]|[xX×]\s*(?<b>\d{1,4}))$", RegexOptions.CultureInvariant)]
    private static partial Regex Badge();
}
