using Xunit;

namespace SignatureOcrSweep.Tests;

public class AccuracyPivotTests
{
    private static SweepRow Row(string config, string knob, Verdict verdict) =>
        new("offline", "f.png", config, null, "2560x1440", knob, "0,0,1,1", 6, "", null, 2000, verdict);

    [Fact]
    public void Cells_count_correct_wrong_and_unreadable_per_configuration_and_knob_set()
    {
        var rows = new[]
        {
            Row("A", "baseline", Verdict.Correct), Row("A", "baseline", Verdict.WrongNumber),
            Row("A", "baseline", Verdict.Unreadable), Row("A", "baseline", Verdict.Blank),
            Row("A", "scale=8", Verdict.Correct), Row("B", "baseline", Verdict.Blank),
        };

        var (columns, body) = AccuracyPivot.Build(rows, r => r.Configuration);

        Assert.Equal(["baseline", "scale=8"], columns);
        Assert.Equal("A", body[0].Row);
        Assert.Equal(["1/4 w1 u1", "1/1"], body[0].Cells);
        Assert.Equal("B", body[1].Row);
        Assert.Equal(["0/1", "-"], body[1].Cells);
    }

    [Fact]
    public void Csv_output_quotes_raw_text_containing_commas_and_quotes()
    {
        var csv = CsvFile.Rows([Row("A", "baseline", Verdict.Correct) with { RawText = "2,\"000\"\n" }]);

        Assert.Contains("\"2,\"\"000\"\"\\n\"", csv);
    }
}
