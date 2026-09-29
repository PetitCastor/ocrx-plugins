namespace SignatureOcrSweep;

/// <summary>How the scaled crop is collapsed to one grey value before OCR.</summary>
public enum ChannelMode
{
    /// <summary>The shipped engine behaviour: B and G are overwritten with R.</summary>
    Red,
    /// <summary>Rec. 601 luminance.</summary>
    Luminance,
    /// <summary>max(R, G, B).</summary>
    MaxRgb,
    /// <summary>HSV value, which for RGB is the same as <see cref="MaxRgb"/>; kept as its own axis value so
    /// the sweep table reads the way the task file lists it.</summary>
    HsvValue,
}
