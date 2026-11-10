namespace SignatureReaderSpike;

/// <summary>Band crop in, reading out: ink map, badge search, glyph cells, template scores.</summary>
internal static class Reader
{
    public static readonly int[] Thresholds = [28, 40, 52, 64];

    /// <summary>
    /// Reads a band crop. Several ink thresholds are tried because no single one separates the glyphs
    /// on every frame; with templates the most confident reading wins, without them the most plausible layout.
    /// </summary>
    public static Reading? Read(Bgra band, double scale, Templates? templates, string channels = "r")
    {
        Reading? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var channel in channels)
        {
            var gray = Ink.Channel(band, channel);
            var k = Math.Max(5, (int)Math.Round(9 * scale)) | 1;
            var ink = Ink.TopHat(gray, band.Width, band.Height, k);
            foreach (var threshold in Thresholds)
            {
                var (comps, labels) = Components.Find(ink, band.Width, band.Height, threshold);
                var map = new InkMap(ink, band.Width, band.Height, labels, comps, threshold);
                if (BadgeFinder.Find(map, scale) is not { } badge)
                    continue;
                var reading = Classify(map, badge, templates, channel);
                var score = Plausibility(reading) + (templates is null ? 0 : 2 * reading.MeanScore + 4 * reading.MinScore);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = reading;
                }
            }
        }

        return best;
    }

    private static double Plausibility(Reading r)
    {
        var digits = r.Cells.Count;
        var score = r.LayoutValid ? 3.0 : 0.0;
        if (digits is >= 4 and <= 6)
            score += 1.0;
        // Evenly sized cells mean the glyphs separated cleanly instead of being cut out of a merged blob.
        var widths = r.Cells.Select(c => (double)c.Box.Width).ToList();
        if (widths.Count > 1)
            score -= (widths.Max() - widths.Min()) / Math.Max(1.0, r.Badge.DigitHeight);
        return score;
    }

    private static Reading Classify(InkMap map, Badge badge, Templates? templates, char channel)
    {
        var top = badge.Digits.Min(d => d.Y);
        var bottom = badge.Digits.Max(d => d.Bottom);
        var cells = Segment(map, badge).Select(box => new Cell { Box = box }).ToList();
        foreach (var cell in cells)
            cell.Feature = Feature(map, cell.Box, top, bottom - top);

        var font = "";
        double mean = 0, min = 0, minMargin = 0;
        if (templates is not null)
        {
            var bestFont = double.NegativeInfinity;
            foreach (var candidate in templates.Fonts)
            {
                var total = 0.0;
                foreach (var cell in cells)
                    total += Score(cell.Feature, templates, candidate).Max();
                if (total > bestFont)
                {
                    bestFont = total;
                    font = candidate;
                }
            }

            foreach (var cell in cells)
            {
                cell.Scores = Score(cell.Feature, templates, font);
                var order = Enumerable.Range(0, 10).OrderByDescending(d => cell.Scores[d]).ToArray();
                cell.Digit = order[0];
                cell.Best = cell.Scores[order[0]];
                cell.Margin = cell.Scores[order[0]] - cell.Scores[order[1]];
            }

            if (cells.Count > 0)
            {
                mean = cells.Average(c => c.Best);
                min = cells.Min(c => c.Best);
                minMargin = cells.Min(c => c.Margin);
            }
        }

        // Layout: a comma sits between the two cells it lies between on the x axis.
        var text = new System.Text.StringBuilder();
        var commaAfter = new HashSet<int>();
        foreach (var comma in badge.Commas)
        {
            var cx = comma.X + comma.Width / 2.0;
            var index = cells.FindLastIndex(c => c.Box.X + c.Box.Width / 2.0 < cx);
            if (index >= 0 && index < cells.Count - 1)
                commaAfter.Add(index);
        }

        for (var i = 0; i < cells.Count; i++)
        {
            text.Append(cells[i].Digit >= 0 ? (char)('0' + cells[i].Digit) : '#');
            if (commaAfter.Contains(i))
                text.Append(',');
        }

        var layout = text.ToString();
        var layoutValid = LayoutOk(layout);
        var value = layoutValid && templates is not null && int.TryParse(layout.Replace(",", ""), out var parsed) ? parsed : (int?)null;
        return new Reading(badge, cells, layout, layoutValid, value, font, mean, min, minMargin, map.Threshold, channel);
    }

    /// <summary>Digits in groups of three after an optional comma; a missing comma is tolerated.</summary>
    private static bool LayoutOk(string text)
    {
        var parts = text.Split(',');
        if (parts.Length == 1)
            return text.Length is >= 4 and <= 6;
        if (parts[0].Length is < 1 or > 3)
            return false;
        return parts.Skip(1).All(p => p.Length == 3);
    }

    private static double[] Score(float[] feature, Templates templates, string font)
    {
        var scores = new double[10];
        for (var d = 0; d < 10; d++)
        {
            var template = templates.Mean(font, d);
            if (template is null)
            {
                // No sample of this digit in this font. With no template at all every score ties at -1
                // and the glyph reads as 0; the gate rejects that reading on its mean score.
                scores[d] = -1;
                continue;
            }

            var dot = 0.0;
            for (var i = 0; i < feature.Length; i++)
                dot += feature[i] * template[i];
            scores[d] = dot;
        }

        return scores;
    }

    /// <summary>Cuts merged blobs into glyph boxes at the emptiest columns near each expected boundary.</summary>
    public static List<Box> Segment(InkMap map, Badge badge)
    {
        var h = badge.DigitHeight;
        var narrow = badge.Digits.Where(d => d.Width <= 0.95 * h).Select(d => d.Width).OrderBy(w => w).ToList();
        var unit = narrow.Count > 0 ? narrow[narrow.Count / 2] : 0.62 * h;
        var boxes = new List<Box>();
        foreach (var comp in badge.Digits)
        {
            var n = (int)Math.Round((comp.Width + 1) / (unit + 1.0));
            if (comp.Width <= 1.5 * unit || n < 2)
            {
                boxes.Add(new Box(comp.X, comp.Y, comp.Width, comp.Height));
                continue;
            }

            var columns = new int[comp.Width];
            for (var y = comp.Y; y < comp.Bottom; y++)
                for (var x = 0; x < comp.Width; x++)
                    if (map.Labels[y * map.Width + comp.X + x] == comp.Label)
                        columns[x] += map.Ink[y * map.Width + comp.X + x];
            var cuts = new List<int> { -1 };
            for (var i = 1; i < n; i++)
            {
                var at = i * (double)comp.Width / n;
                var lo = Math.Max(cuts[^1] + 2, (int)Math.Floor(at - 0.3 * comp.Width / n));
                var hi = Math.Min(comp.Width - 2, (int)Math.Ceiling(at + 0.3 * comp.Width / n));
                if (lo > hi)
                    continue;
                var cut = lo;
                for (var x = lo; x <= hi; x++)
                    if (columns[x] < columns[cut])
                        cut = x;
                cuts.Add(cut);
            }

            cuts.Add(comp.Width);
            for (var i = 0; i + 1 < cuts.Count; i++)
            {
                var from = cuts[i] + 1;
                var to = cuts[i + 1];
                if (to > from)
                    boxes.Add(new Box(comp.X + from, comp.Y, to - from, comp.Height));
            }
        }

        return boxes.OrderBy(b => b.X).ToList();
    }

    /// <summary>
    /// The glyph in a fixed-aspect cell centred on its box, resampled to the template size and
    /// normalised to zero mean and unit length, so a dot product with a template is a correlation.
    /// </summary>
    public static float[] Feature(InkMap map, Box box, int top, int height)
    {
        var cellWidth = Math.Max(box.Width, (int)Math.Round(0.75 * height));
        var x0 = box.X + box.Width / 2.0 - cellWidth / 2.0;
        var result = new float[Templates.W * Templates.H];
        const int sub = 3;
        for (var ty = 0; ty < Templates.H; ty++)
        {
            for (var tx = 0; tx < Templates.W; tx++)
            {
                var sum = 0.0;
                for (var sy = 0; sy < sub; sy++)
                {
                    for (var sx = 0; sx < sub; sx++)
                    {
                        var fx = x0 + (tx + (sx + 0.5) / sub) * cellWidth / Templates.W - 0.5;
                        var fy = top + (ty + (sy + 0.5) / sub) * height / Templates.H - 0.5;
                        // Only this glyph's own columns count: a neighbour leaking into the cell is not evidence.
                        sum += fx < box.X - 0.5 || fx > box.Right - 0.5 ? 0.0 : Sample(map, fx, fy);
                    }
                }

                result[ty * Templates.W + tx] = (float)(sum / (sub * sub));
            }
        }

        return Normalise(result);
    }

    private static double Sample(InkMap map, double x, double y)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var ax = x - x0;
        var ay = y - y0;
        double At(int xx, int yy) => xx < 0 || yy < 0 || xx >= map.Width || yy >= map.Height ? 0.0 : map.Ink[yy * map.Width + xx];
        return (1 - ay) * ((1 - ax) * At(x0, y0) + ax * At(x0 + 1, y0)) + ay * ((1 - ax) * At(x0, y0 + 1) + ax * At(x0 + 1, y0 + 1));
    }

    public static float[] Normalise(float[] values)
    {
        var mean = values.Average();
        var result = new float[values.Length];
        var norm = 0.0;
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = values[i] - mean;
            norm += result[i] * result[i];
        }

        norm = Math.Sqrt(norm);
        if (norm < 1e-6)
            return result;
        for (var i = 0; i < values.Length; i++)
            result[i] = (float)(result[i] / norm);
        return result;
    }
}
