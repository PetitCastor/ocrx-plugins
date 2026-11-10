namespace SignatureReaderSpike;

/// <summary>8-connected component labelling over ink above a threshold.</summary>
internal static class Components
{
    public static (List<Comp> Comps, int[] Labels) Find(byte[] ink, int width, int height, int threshold)
    {
        var labels = new int[ink.Length];
        var comps = new List<Comp>();
        var stack = new Stack<int>();
        var next = 0;
        for (var start = 0; start < ink.Length; start++)
        {
            if (ink[start] <= threshold || labels[start] != 0)
                continue;
            next++;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, area = 0;
            labels[start] = next;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                int x = i % width, y = i / width;
                area++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= width || yy >= height)
                            continue;
                        var j = yy * width + xx;
                        if (ink[j] > threshold && labels[j] == 0)
                        {
                            labels[j] = next;
                            stack.Push(j);
                        }
                    }
                }
            }

            comps.Add(new Comp(next, minX, minY, maxX - minX + 1, maxY - minY + 1, area));
        }

        return (comps, labels);
    }
}
