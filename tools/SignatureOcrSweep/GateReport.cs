using System.Text;

namespace SignatureOcrSweep;

/// <summary>The offline-versus-engine comparison. Offline mode is only trusted for a sweep once every row
/// here matches, byte for byte, the engine's raw text for the same frame and knob set.</summary>
public static class GateReport
{
    public static List<GateLine> Compare(IReadOnlyList<SweepRow> engineRows, IReadOnlyList<SweepRow> offlineRows)
    {
        var offlineByKey = offlineRows.ToDictionary(r => (r.Frame, r.KnobSet));
        var lines = new List<GateLine>();
        foreach (var e in engineRows)
        {
            var offline = offlineByKey.TryGetValue((e.Frame, e.KnobSet), out var o) ? o.RawText : "<no offline row>";
            lines.Add(new GateLine(e.Frame, e.Configuration, e.KnobSet, e.RawText, offline));
        }

        return lines;
    }

    /// <summary>Engine rows that make the gate meaningless whatever the text says: the engine applied a
    /// different crop or scale than planned (probably fit mode), or the read failed outright.</summary>
    public static List<string> EngineProblems(IReadOnlyList<SweepRow> engineRows) =>
        engineRows.Where(r => r.CropMismatch || r.Verdict == Verdict.Error)
            .Select(r => $"{r.Frame} [{r.KnobSet}]: {r.Note}")
            .ToList();

    private static string Show(string raw) => "'" + raw.Replace("\r", "\\r").Replace("\n", "\\n") + "'";

    public static string Text(IReadOnlyList<GateLine> lines)
    {
        var sb = new StringBuilder();
        var frameWidth = lines.Count == 0 ? 5 : lines.Max(l => l.Frame.Length);
        var knobWidth = lines.Count == 0 ? 8 : lines.Max(l => l.KnobSet.Length);
        var engineWidth = lines.Count == 0 ? 6 : lines.Max(l => Show(l.Engine).Length);
        var offlineWidth = lines.Count == 0 ? 7 : lines.Max(l => Show(l.Offline).Length);
        sb.AppendLine($"{"frame".PadRight(frameWidth)}  {"knob set".PadRight(knobWidth)}  {"engine".PadRight(engineWidth)}  {"offline".PadRight(offlineWidth)}  match");
        foreach (var l in lines)
        {
            sb.AppendLine($"{l.Frame.PadRight(frameWidth)}  {l.KnobSet.PadRight(knobWidth)}  {Show(l.Engine).PadRight(engineWidth)}  {Show(l.Offline).PadRight(offlineWidth)}  {(l.Match ? "yes" : "NO")}");
        }

        var mismatches = lines.Count(l => !l.Match);
        sb.AppendLine(mismatches == 0
            ? $"GATE PASSED: offline reproduced the engine on all {lines.Count} reads."
            : $"GATE FAILED: {mismatches} of {lines.Count} reads differ.");
        return sb.ToString();
    }

    public static string Csv(IReadOnlyList<GateLine> lines)
    {
        var sb = new StringBuilder("frame,configuration,knob_set,engine_raw,offline_raw,match\n");
        foreach (var l in lines)
        {
            sb.Append(string.Join(',', CsvFile.Field(l.Frame), CsvFile.Field(l.Configuration), CsvFile.Field(l.KnobSet),
                CsvFile.Field(l.Engine), CsvFile.Field(l.Offline), l.Match ? "yes" : "no")).Append('\n');
        }

        return sb.ToString();
    }
}
