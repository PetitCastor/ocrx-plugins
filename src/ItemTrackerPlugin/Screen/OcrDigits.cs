namespace ItemTrackerPlugin.Screen;

/// <summary>The game draws zero with a slash, which Windows OCR returns as <c>ø</c>.</summary>
internal static class OcrDigits
{
    public static string Normalise(string text) =>
        text.Replace('ø', '0').Replace('Ø', '0').Replace('∅', '0');
}
