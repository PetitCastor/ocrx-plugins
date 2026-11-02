namespace SignaturePlugin;

/// <summary>
/// What one read of the counter ROI says about the signature number, as <see cref="CounterReader"/> decides it.
/// </summary>
/// <param name="Text">The text to take the reading from, or empty when nothing in the read is a credible
/// signature — which is not the same as the read being empty: a distance line ("15.9km") is text, and is not
/// a reading.</param>
/// <param name="Box">The number's box in frame pixels: of the whole number when <paramref name="Cut"/> is
/// <see cref="CounterCut.None"/>, of the piece that was read when it is not. Null when there is none.</param>
/// <param name="Cut">The rect's edge has sliced into the number: the text is a fragment, and must not become
/// an observation however valid it looks.</param>
/// <param name="Raw">What OCR returned, trimmed, for diagnostics.</param>
internal sealed record CounterReading(string Text, NumberBox? Box, CounterCut Cut, string Raw)
{
    public bool IsCut => Cut != CounterCut.None;

    /// <summary>A whole number was read: there is text to observe, and a box to follow.</summary>
    public bool HasNumber => !IsCut && Text.Length > 0 && Box is not null;
}
