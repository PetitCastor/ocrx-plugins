using System.Globalization;
using System.Text.RegularExpressions;

namespace ItemTrackerPlugin.Screen;

public static partial class CapacityParser
{
    public static CapacityReading? Parse(string text)
    {
        var match = Pair().Match(OcrDigits.Normalise(text));
        if (!match.Success) return null;

        var used = ToMicroScu(match.Groups["u"].Value, match.Groups["us"].Value);
        var max = ToMicroScu(match.Groups["m"].Value, match.Groups["ms"].Value);
        return used is { } u && max is { } m && m > 0 && u <= m ? new CapacityReading(u, m) : null;
    }

    private static long? ToMicroScu(string number, string suffix)
    {
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
        // No suffix means the figure is in whole SCU, as the freight screens print it.
        var factor = suffix.ToUpperInvariant() switch { "K" => 1e3, "M" => 1e6, "G" => 1e9, _ => 1e6 };
        return (long)Math.Round(value * factor);
    }

    [GeneratedRegex(@"(?<u>\d+(?:\.\d+)?)\s*(?<us>[KMGkmg]?)\s*/\s*(?<m>\d+(?:\.\d+)?)\s*(?<ms>[KMGkmg]?)", RegexOptions.CultureInvariant)]
    private static partial Regex Pair();
}
