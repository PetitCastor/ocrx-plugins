namespace SignatureOcrSweep;

/// <summary>What the probe saw for one plan: the Text ROI's raw text and, from a Detailed ROI with the
/// same rect and scale, the crop and scale the engine reports it actually applied. A missing or failed ROI
/// is <see cref="Error"/>, never empty text.</summary>
/// <param name="Crop">Engine-applied crop as <c>x,y,w,h</c> in frame pixels, or null when unavailable.</param>
public sealed record EngineRead(string Text, string? Error, string? Crop, double? Scale);
