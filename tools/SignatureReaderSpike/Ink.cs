namespace SignatureReaderSpike;

/// <summary>Turns a BGRA crop into an "ink" map: bright thin strokes kept, everything broad removed.</summary>
internal static class Ink
{
    /// <summary>One byte per pixel from the chosen channel: g, r, b, x (max of the three), n (min).</summary>
    public static byte[] Channel(Bgra image, char channel)
    {
        var gray = new byte[image.Width * image.Height];
        var p = image.Pixels;
        for (var i = 0; i < gray.Length; i++)
        {
            byte b = p[4 * i], g = p[4 * i + 1], r = p[4 * i + 2];
            gray[i] = channel switch
            {
                'r' => r,
                'b' => b,
                'x' => Math.Max(b, Math.Max(g, r)),
                'n' => Math.Min(b, Math.Min(g, r)),
                _ => g,
            };
        }

        return gray;
    }

    /// <summary>
    /// White top-hat: the image minus its morphological opening by a k x k square. Anything wider
    /// than k in both directions (the badge panel, rocks, sky) is background and cancels; strokes
    /// thinner than k survive at their contrast above that background.
    /// </summary>
    public static byte[] TopHat(byte[] gray, int width, int height, int k)
    {
        var opened = Filter(Filter(gray, width, height, k, min: true), width, height, k, min: false);
        var result = new byte[gray.Length];
        for (var i = 0; i < gray.Length; i++)
            result[i] = (byte)(gray[i] - opened[i]); // an opening never exceeds its source, so this cannot go negative
        return result;
    }

    private static byte[] Filter(byte[] source, int width, int height, int k, bool min)
    {
        var half = k / 2;
        var rows = new byte[source.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = source[y * width + x];
                for (var d = -half; d <= half; d++)
                {
                    var xx = Math.Clamp(x + d, 0, width - 1);
                    var v = source[y * width + xx];
                    value = min ? Math.Min(value, v) : Math.Max(value, v);
                }

                rows[y * width + x] = value;
            }
        }

        var result = new byte[source.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = rows[y * width + x];
                for (var d = -half; d <= half; d++)
                {
                    var yy = Math.Clamp(y + d, 0, height - 1);
                    var v = rows[yy * width + x];
                    value = min ? Math.Min(value, v) : Math.Max(value, v);
                }

                result[y * width + x] = value;
            }
        }

        return result;
    }
}
