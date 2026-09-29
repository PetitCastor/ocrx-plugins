using System.Globalization;
using System.Text;

namespace SignatureOcrSweep;

/// <summary>Minimal CSV output. Raw OCR text can hold commas, quotes and newlines, so every text field is quoted.</summary>
public static class CsvFile
{
    public static string Field(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    private static string Number(double? value) =>
        value is { } v ? v.ToString("0.###", CultureInfo.InvariantCulture) : "";

    public static string Rows(IEnumerable<SweepRow> rows)
    {
        var sb = new StringBuilder("mode,frame,configuration,vertical_fov,frame_size,knob_set,frame_rect,effective_scale,raw_text,parsed,expected,verdict,note\n");
        foreach (var r in rows)
        {
            sb.Append(string.Join(',',
                    Field(r.Mode), Field(r.Frame), Field(r.Configuration), Number(r.VerticalFov), Field(r.FrameSize),
                    Field(r.KnobSet), Field(r.FrameRect), Number(r.EffectiveScale), Field(r.RawText),
                    Number(r.Parsed), Number(r.Expected), r.Verdict, Field(r.Note)))
                .Append('\n');
        }

        return sb.ToString();
    }
}
