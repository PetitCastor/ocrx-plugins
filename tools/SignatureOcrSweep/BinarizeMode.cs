namespace SignatureOcrSweep;

public enum BinarizeMode
{
    None,
    /// <summary>Otsu's threshold, computed per crop.</summary>
    Otsu,
    /// <summary>A fixed 0-255 threshold, <see cref="KnobSet.Threshold"/>.</summary>
    Fixed,
}
