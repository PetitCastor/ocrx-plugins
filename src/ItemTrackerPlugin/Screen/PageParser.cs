using System.Text.RegularExpressions;

namespace ItemTrackerPlugin.Screen;

public static partial class PageParser
{
    public static PageCounter? Parse(string text)
    {
        var match = Counter().Match(OcrDigits.Normalise(text));
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups["n"].ValueSpan, out var page) ||
            !int.TryParse(match.Groups["m"].ValueSpan, out var of)) return null;

        return page >= 1 && page <= of ? new PageCounter(page, of) : null;
    }

    [GeneratedRegex(@"(?<n>\d{1,3})\s*/\s*(?<m>\d{1,3})", RegexOptions.CultureInvariant)]
    private static partial Regex Counter();
}
