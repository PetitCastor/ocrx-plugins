namespace SignatureOcrSweep;

/// <summary>
/// The post-scale (and pre-sharpen) pixel knobs, as pure transforms on packed Bgra8 buffers so they can
/// be tested without an OCR engine. <see cref="Collapse"/> with <see cref="ChannelMode.Red"/> is a line
/// for line copy of the engine's <c>OcrPipeline.ApplyRedChannelGrayscale</c>.
/// </summary>
public static class PixelOps
{
    /// <summary>Collapses to grey in place. Red keeps the engine's exact behaviour (B and G take R's value);
    /// the other modes write their grey value to B, G and R.</summary>
    public static void Collapse(byte[] bgra, ChannelMode mode)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            byte grey = mode switch
            {
                ChannelMode.Red => r,
                ChannelMode.Luminance => (byte)Math.Clamp((int)Math.Round(0.299 * r + 0.587 * g + 0.114 * b), 0, 255),
                ChannelMode.MaxRgb or ChannelMode.HsvValue => Math.Max(r, Math.Max(g, b)),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            bgra[i] = grey;
            bgra[i + 1] = grey;
            bgra[i + 2] = grey;
        }
    }

    /// <summary>Linear min-max stretch of the grey (R) channel to 0-255. A flat image is left alone.</summary>
    public static void Stretch(byte[] bgra)
    {
        byte lo = 255, hi = 0;
        for (var i = 2; i < bgra.Length; i += 4)
        {
            lo = Math.Min(lo, bgra[i]);
            hi = Math.Max(hi, bgra[i]);
        }

        if (hi <= lo)
            return;

        var range = hi - lo;
        MapGrey(bgra, v => (byte)Math.Clamp((int)Math.Round((v - lo) * 255.0 / range), 0, 255));
    }

    /// <summary>Power-law remap of the grey (R) channel: <c>255 * (v / 255) ^ gamma</c>.</summary>
    public static void ApplyGamma(byte[] bgra, double gamma)
    {
        var table = new byte[256];
        for (var v = 0; v < 256; v++)
            table[v] = (byte)Math.Clamp((int)Math.Round(255.0 * Math.Pow(v / 255.0, gamma)), 0, 255);
        MapGrey(bgra, v => table[v]);
    }

    /// <summary>Grey values at or above the threshold become 255, the rest 0.</summary>
    public static void Threshold(byte[] bgra, int threshold) =>
        MapGrey(bgra, v => v >= threshold ? (byte)255 : (byte)0);

    /// <summary>Otsu's threshold over the grey (R) channel: the cut that maximises between-class variance.</summary>
    public static int OtsuThreshold(byte[] bgra)
    {
        var histogram = new long[256];
        for (var i = 2; i < bgra.Length; i += 4)
            histogram[bgra[i]]++;

        long total = bgra.Length / 4;
        double sumAll = 0;
        for (var v = 0; v < 256; v++)
            sumAll += v * (double)histogram[v];

        double sumBackground = 0, best = -1;
        long weightBackground = 0;
        var threshold = 0;
        for (var v = 0; v < 256; v++)
        {
            weightBackground += histogram[v];
            if (weightBackground == 0)
                continue;
            var weightForeground = total - weightBackground;
            if (weightForeground == 0)
                break;

            sumBackground += v * (double)histogram[v];
            var meanBackground = sumBackground / weightBackground;
            var meanForeground = (sumAll - sumBackground) / weightForeground;
            var between = (double)weightBackground * weightForeground
                * (meanBackground - meanForeground) * (meanBackground - meanForeground);
            if (between > best)
            {
                best = between;
                threshold = v + 1; // pixels at or above v + 1 are the foreground class
            }
        }

        return threshold;
    }

    /// <summary>
    /// Unsharp mask on colour, before any scaling: <c>out = in + amount * (in - blur3x3(in))</c> per
    /// colour channel, edges clamped. Alpha is untouched.
    /// </summary>
    public static byte[] UnsharpMask(byte[] bgra, int width, int height, double amount)
    {
        var result = new byte[bgra.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = 4 * (y * width + x);
                for (var c = 0; c < 3; c++)
                {
                    var sum = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var yy = Math.Clamp(y + dy, 0, height - 1);
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var xx = Math.Clamp(x + dx, 0, width - 1);
                            sum += bgra[4 * (yy * width + xx) + c];
                        }
                    }

                    var blur = sum / 9.0;
                    var original = bgra[offset + c];
                    result[offset + c] = (byte)Math.Clamp((int)Math.Round(original + amount * (original - blur)), 0, 255);
                }

                result[offset + 3] = bgra[offset + 3];
            }
        }

        return result;
    }

    /// <summary>Applies every post-scale knob of <paramref name="knobs"/> in order: channel, contrast, binarization.</summary>
    public static void ApplyPostScale(byte[] bgra, KnobSet knobs)
    {
        Collapse(bgra, knobs.Channel);
        switch (knobs.Contrast)
        {
            case ContrastMode.Stretch: Stretch(bgra); break;
            case ContrastMode.Gamma: ApplyGamma(bgra, knobs.Gamma); break;
        }

        switch (knobs.Binarize)
        {
            case BinarizeMode.Otsu: Threshold(bgra, OtsuThreshold(bgra)); break;
            case BinarizeMode.Fixed: Threshold(bgra, knobs.Threshold); break;
        }
    }

    /// <summary>Rewrites the grey value on B, G and R from the current R.</summary>
    private static void MapGrey(byte[] bgra, Func<byte, byte> map)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var v = map(bgra[i + 2]);
            bgra[i] = v;
            bgra[i + 1] = v;
            bgra[i + 2] = v;
        }
    }
}
