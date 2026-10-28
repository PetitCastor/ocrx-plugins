using System.Globalization;
using System.Text;

namespace ItemTrackerPlugin.Ingest;

/// <summary>
/// Decodes the paging cursors of a Freight Inventory Grid page: base64 of a decimal item
/// position (<c>MA==</c> is 0, <c>MjUw</c> is 250). The padding is optional in what we accept
/// so a trimmed cursor still reads; anything that is not a non-negative integer does not.
/// </summary>
public static class FreightCursor
{
    public static bool TryDecode(string? cursor, out long position)
    {
        position = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return false;

        var text = cursor.Trim();
        var padded = text.Length % 4 == 0 ? text : text + new string('=', 4 - text.Length % 4);

        var buffer = new byte[padded.Length];
        if (!Convert.TryFromBase64String(padded, buffer, out var written)) return false;

        var digits = Encoding.ASCII.GetString(buffer, 0, written);
        return digits.Length > 0 && digits.All(char.IsAsciiDigit) &&
               long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out position);
    }
}
