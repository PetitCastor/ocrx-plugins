namespace SignatureOcrSweep;

/// <summary>The four <c>BitmapInterpolationMode</c> values, kept as our own enum so the pure parts of
/// the tool (knob parsing, tests) do not need the WinRT projection.</summary>
public enum InterpolationKind
{
    /// <summary>The shipped engine behaviour.</summary>
    Cubic,
    Fant,
    Linear,
    NearestNeighbor,
}
