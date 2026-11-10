namespace SignatureReaderSpike;

/// <summary>
/// Finds the signature badge in a band crop with no fixed position: a run of equal-height glyphs on
/// one baseline with a taller pin icon just left of it. Nothing here depends on colour.
/// </summary>
internal static class BadgeFinder
{
    public static Badge? Find(InkMap map, double scale)
    {
        var minHeight = Math.Max(5, (int)Math.Round(8 * scale));
        var maxHeight = (int)Math.Round(26 * scale);
        var comps = map.Comps.Where(c => c.Area >= 2).OrderBy(c => c.X).ToList();
        Badge? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var seed in comps)
        {
            if (seed.Height < minHeight || seed.Height > maxHeight)
                continue;
            var h = seed.Height;
            var top = seed.Y;
            var digits = new List<Comp> { seed };
            var commas = new List<Comp>();
            var right = seed.Right;
            foreach (var c in comps)
            {
                if (c.X < right - 1 || ReferenceEquals(c, seed))
                    continue;
                if (c.X - right > 1.1 * h)
                    break;
                var aligned = Math.Abs(c.Y - top) <= 0.25 * h + 1 && Math.Abs(c.Height - h) <= 0.25 * h + 1;
                var comma = c.Height >= 0.15 * h && c.Height <= 0.65 * h && c.Width <= 0.6 * h
                    && c.Bottom >= top + 0.75 * h && c.Bottom <= top + 1.45 * h;
                if (aligned)
                    digits.Add(c);
                else if (comma)
                    commas.Add(c);
                else
                    continue;
                right = c.Right;
            }

            var span = digits[^1].Right - digits[0].X;
            if (span < 1.8 * h)
                continue;
            if (Icon(comps, digits[0].X, top, h) is not { } icon)
                continue;
            // Prefer the widest run; a seed further right in the same run is a strict subset of it.
            var score = span + 0.01 * digits.Sum(d => d.Area);
            if (score > bestScore)
            {
                bestScore = score;
                commas.RemoveAll(c => c.X > digits[^1].Right);
                best = new Badge(digits, commas, icon, top, h);
            }
        }

        return best;
    }

    private static Box? Icon(List<Comp> comps, int runLeft, int top, int h)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, area = 0;
        foreach (var c in comps)
        {
            if (c.Right > runLeft - 0.2 * h || c.X < runLeft - 3.4 * h)
                continue;
            if (c.Bottom < top - 0.9 * h || c.Y > top + 1.9 * h)
                continue;
            minX = Math.Min(minX, c.X);
            minY = Math.Min(minY, c.Y);
            maxX = Math.Max(maxX, c.Right);
            maxY = Math.Max(maxY, c.Bottom);
            area += c.Area;
        }

        if (maxX < 0)
            return null;
        var box = new Box(minX, minY, maxX - minX, maxY - minY);
        return box.Height >= 1.15 * h && box.Width >= 0.5 * h && area >= 0.3 * h * h ? box : null;
    }
}
