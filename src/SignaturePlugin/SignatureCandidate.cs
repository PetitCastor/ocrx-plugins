namespace SignaturePlugin;

/// <summary>
/// A word (or a few adjacent words) OCR read in a search tile that looks like an RS signature value.
/// </summary>
/// <param name="Text">What OCR read.</param>
/// <param name="Value">The number it parses to, after the folds in <see cref="SignatureCandidates"/>.</param>
/// <param name="Known">The value lands on the signature table (the caller's judgement, not this type's):
/// strong evidence that it is a real signature rather than some other grouped number on the HUD.</param>
/// <param name="Box">Where the text is, in frame pixels.</param>
/// <param name="Tile">The frame rectangle of the tile it was read in, for deciding whether the text was
/// cut by the tile's edge.</param>
internal sealed record SignatureCandidate(string Text, double Value, bool Known, NumberBox Box, NumberBox Tile);
