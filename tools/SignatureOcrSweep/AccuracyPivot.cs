using System.Text;

namespace SignatureOcrSweep;

/// <summary>Accuracy per configuration x knob set. A cell reads <c>correct/total</c>, followed by
/// <c>wN</c> for N wrong-number reads and <c>uN</c> for N unreadable ones and <c>eN</c> for N failed reads when there are any (blanks are the
/// remainder). Wrong numbers are the number to drive to zero, so they are never folded into a percentage.</summary>
public static class AccuracyPivot
{
    public static string Cell(IReadOnlyCollection<SweepRow> cell)
    {
        var correct = cell.Count(r => r.Verdict == Verdict.Correct);
        var wrong = cell.Count(r => r.Verdict == Verdict.WrongNumber);
        var unreadable = cell.Count(r => r.Verdict == Verdict.Unreadable);
        var errors = cell.Count(r => r.Verdict == Verdict.Error);
        return $"{correct}/{cell.Count}" + (wrong > 0 ? $" w{wrong}" : "") + (unreadable > 0 ? $" u{unreadable}" : "")
            + (errors > 0 ? $" e{errors}" : "");
    }

    /// <summary>Row labels are the configuration labels (FOV and lossy caveat included), columns the knob sets in first-seen order.</summary>
    public static (IReadOnlyList<string> Columns, IReadOnlyList<(string Row, IReadOnlyList<string> Cells)> Body) Build(
        IReadOnlyList<SweepRow> rows, Func<SweepRow, string> rowLabel)
    {
        var columns = rows.Select(r => r.KnobSet).Distinct().ToList();
        var body = rows.GroupBy(rowLabel)
            .Select(g => (Row: g.Key, Cells: (IReadOnlyList<string>)columns
                .Select(k => g.Where(r => r.KnobSet == k).ToList())
                .Select(cell => cell.Count == 0 ? "-" : Cell(cell))
                .ToList()))
            .ToList();
        return (columns, body);
    }

    public static string Csv(IReadOnlyList<SweepRow> rows, Func<SweepRow, string> rowLabel)
    {
        var (columns, body) = Build(rows, rowLabel);
        var sb = new StringBuilder(CsvFile.Field("configuration"));
        foreach (var c in columns)
            sb.Append(',').Append(CsvFile.Field(c));
        sb.Append('\n');
        foreach (var (row, cells) in body)
        {
            sb.Append(CsvFile.Field(row));
            foreach (var cell in cells)
                sb.Append(',').Append(CsvFile.Field(cell));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>A fixed-width table for the console.</summary>
    public static string Text(IReadOnlyList<SweepRow> rows, Func<SweepRow, string> rowLabel)
    {
        var (columns, body) = Build(rows, rowLabel);
        var labelWidth = Math.Max("configuration".Length, body.Count == 0 ? 0 : body.Max(b => b.Row.Length));
        var widths = columns.Select((c, i) => Math.Max(c.Length, body.Count == 0 ? 0 : body.Max(b => b.Cells[i].Length))).ToList();

        var sb = new StringBuilder("configuration".PadRight(labelWidth));
        for (var i = 0; i < columns.Count; i++)
            sb.Append("  ").Append(columns[i].PadRight(widths[i]));
        sb.AppendLine();
        foreach (var (row, cells) in body)
        {
            sb.Append(row.PadRight(labelWidth));
            for (var i = 0; i < cells.Count; i++)
                sb.Append("  ").Append(cells[i].PadRight(widths[i]));
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
