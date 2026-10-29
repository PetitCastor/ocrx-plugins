namespace ItemTrackerPlugin.Screen;

/// <summary>Turns the two header reads into a <see cref="ScreenHeader"/>. The right panel decides
/// first: the left one reads <c>PERSONAL / ALL</c> in every context that has a left panel.</summary>
public static class HeaderParser
{
    public static ScreenHeader Parse(string rightText, string leftText)
    {
        var right = Squash(rightText);
        if (right.Contains("PERSONALSTORAGE", StringComparison.Ordinal))
        {
            var slash = rightText.IndexOf('/');
            var model = slash > 0 ? rightText[..slash].Trim() : null;
            return new ScreenHeader(ScreenContext.ShipStorage, string.IsNullOrEmpty(model) ? null : model);
        }

        if (right.StartsWith("LOCAL", StringComparison.Ordinal))
            return new ScreenHeader(ScreenContext.Kiosk, null);

        if (right.Length == 0 && Squash(leftText).StartsWith("PERSONAL", StringComparison.Ordinal))
            return new ScreenHeader(ScreenContext.Personal, null);

        return ScreenHeader.Unknown;
    }

    private static string Squash(string text) =>
        new(text.Where(c => !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());
}
