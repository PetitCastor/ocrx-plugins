using Xunit;

namespace SignatureOcrSweep.Tests;

public class GateReportTests
{
    private static SweepRow Row(string mode, string frame, string knob, string raw, Verdict verdict = Verdict.Correct, string note = "") =>
        new(mode, frame, "cfg", null, "2560x1440", knob, "0,0,1,1", 6, raw, null, 2000, verdict, note);

    [Fact]
    public void Matching_reads_pass_and_a_differing_read_is_reported_verbatim()
    {
        var engine = new[] { Row("engine", "a.png", "baseline", "2,000"), Row("engine", "b.png", "baseline", "tooo") };
        var offline = new[] { Row("offline", "a.png", "baseline", "2,000"), Row("offline", "b.png", "baseline", "tooc") };

        var lines = GateReport.Compare(engine, offline);
        var text = GateReport.Text(lines);

        Assert.True(lines[0].Match);
        Assert.False(lines[1].Match);
        Assert.Contains("'tooo'", text);
        Assert.Contains("'tooc'", text);
        Assert.Contains("GATE FAILED: 1 of 2", text);
    }

    [Fact]
    public void A_frame_missing_from_the_offline_rows_is_a_mismatch()
    {
        var lines = GateReport.Compare([Row("engine", "a.png", "baseline", "")], []);

        Assert.False(lines[0].Match);
        Assert.Contains("<no offline row>", lines[0].Offline);
    }

    [Fact]
    public void All_matching_reads_print_the_pass_line()
    {
        var lines = GateReport.Compare([Row("engine", "a.png", "baseline", "x")], [Row("offline", "a.png", "baseline", "x")]);

        Assert.Contains("GATE PASSED", GateReport.Text(lines));
    }

    [Fact]
    public void Crop_mismatches_and_errors_are_engine_problems_even_when_the_text_matches()
    {
        var rows = new[]
        {
            Row("engine", "ok.png", "baseline", "2,000"),
            Row("engine", "fit.png", "baseline", "", note: $"{SweepRow.CropMismatchPrefix}: engine applied 1,2,3,4"),
            Row("engine", "bad.png", "baseline", "", Verdict.Error, "ROI failed"),
        };

        var problems = GateReport.EngineProblems(rows);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("fit.png"));
        Assert.Contains(problems, p => p.StartsWith("bad.png"));
    }
}
