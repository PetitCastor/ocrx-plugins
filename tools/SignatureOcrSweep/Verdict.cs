namespace SignatureOcrSweep;

public enum Verdict
{
    /// <summary>The frame has no expected signature in the manifest.</summary>
    Unlabelled,
    Correct,
    /// <summary>The text parsed to a number that is not the expected signature. The failure that matters.</summary>
    WrongNumber,
    /// <summary>Text came back but does not parse (for example <c>tooo</c>). The plugin treats this like a blank.</summary>
    Unreadable,
    /// <summary>Empty or whitespace-only text.</summary>
    Blank,
    /// <summary>The read itself failed (engine ROI error, exception, timeout). Never folded into <see cref="Blank"/>.</summary>
    Error,
}
