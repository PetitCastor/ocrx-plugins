namespace SignatureOcrSweep;

/// <summary>Parsed <c>--name value</c> command-line options; a name may repeat.</summary>
internal sealed class Options(Dictionary<string, List<string>> map)
{
    public static Options Parse(string[] tokens)
    {
        var map = new Dictionary<string, List<string>>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!tokens[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= tokens.Length)
                throw new FormatException($"expected --name value, got '{tokens[i]}'");
            var name = tokens[i][2..];
            if (!map.TryGetValue(name, out var values))
                map[name] = values = [];
            values.Add(tokens[++i]);
        }

        return new Options(map);
    }

    public string Single(string name) =>
        Optional(name) ?? throw new FormatException($"--{name} is required");

    public string? Optional(string name) => map.TryGetValue(name, out var v) ? v[^1] : null;

    public IEnumerable<string> All(string name) => map.TryGetValue(name, out var v) ? v : [];
}
