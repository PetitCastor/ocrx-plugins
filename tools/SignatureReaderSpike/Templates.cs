namespace SignatureReaderSpike;

/// <summary>Per-font digit templates: the mean normalised cell of every labelled sample.</summary>
internal sealed class Templates
{
    public const int W = 12, H = 16;
    private readonly Dictionary<(string Font, int Digit), (float[] Sum, int Count)> _sums = new();
    private Dictionary<(string Font, int Digit), float[]>? _means;

    public IEnumerable<string> Fonts => _sums.Keys.Select(k => k.Font).Distinct();

    public string Coverage => string.Join(" ", Fonts.OrderBy(f => f).Select(f =>
        f + ":" + string.Concat(_sums.Keys.Where(k => k.Font == f).Select(k => k.Digit).OrderBy(d => d))));

    public void Add(string font, int digit, float[] feature)
    {
        if (!_sums.TryGetValue((font, digit), out var entry))
            entry = (new float[W * H], 0);
        for (var i = 0; i < feature.Length; i++)
            entry.Sum[i] += feature[i];
        _sums[(font, digit)] = (entry.Sum, entry.Count + 1);
        _means = null;
    }

    public float[]? Mean(string font, int digit)
    {
        _means ??= _sums.ToDictionary(kv => kv.Key, kv => Reader.Normalise(kv.Value.Sum.Select(v => v / kv.Value.Count).ToArray()));
        return _means.GetValueOrDefault((font, digit));
    }
}
