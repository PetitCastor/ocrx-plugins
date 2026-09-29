namespace SignatureOcrSweep;

/// <summary>One CSV row: a frame read under one knob set by one mode. In engine mode
/// <see cref="FrameRect"/> and <see cref="EffectiveScale"/> are what the engine reported applying (via a
/// Detailed ROI), not the plan; in offline mode they are the plan the read used.</summary>
public sealed record SweepRow(
    string Mode,
    string Frame,
    string Configuration,
    double? VerticalFov,
    string FrameSize,
    string KnobSet,
    string FrameRect,
    double EffectiveScale,
    string RawText,
    double? Parsed,
    double? Expected,
    Verdict Verdict,
    string Note = "")
{
    public const string CropMismatchPrefix = "crop mismatch";

    /// <summary>True when the engine measured a different crop or scale than the plan, which means it
    /// mapped the ROI differently (typically fit mode because the games catalog did not resolve).</summary>
    public bool CropMismatch => Note.StartsWith(CropMismatchPrefix, StringComparison.Ordinal);
}
