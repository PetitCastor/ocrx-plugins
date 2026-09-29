using SignaturePlugin;

namespace SignatureOcrSweep;

/// <summary>Turns OCR text into a verdict using the plugin's own <see cref="SignatureParser"/> (linked
/// source, not a re-implementation), so the <c>2.000</c> to 2,000 rule and every other fold apply exactly
/// as they do in the shipped plugin.</summary>
public static class VerdictClassifier
{
    public static Verdict Classify(string raw, double? expected, out double? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(raw))
            return Verdict.Blank;

        if (!SignatureParser.TryParse(raw, out var value))
            return Verdict.Unreadable;

        parsed = value;
        if (expected is not { } want)
            return Verdict.Unlabelled;
        return value == want ? Verdict.Correct : Verdict.WrongNumber;
    }
}
