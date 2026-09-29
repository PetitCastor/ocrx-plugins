namespace SignatureOcrSweep;

/// <summary>Grey-level remapping applied after the channel collapse.</summary>
public enum ContrastMode
{
    None,
    /// <summary>Linear min-max stretch to the full 0-255 range.</summary>
    Stretch,
    /// <summary>Power-law remap; the exponent is <see cref="KnobSet.Gamma"/>.</summary>
    Gamma,
}
