namespace SignatureOcrSweep;

/// <summary>One labelled frame. <see cref="Configuration"/>, <see cref="VerticalFov"/> and
/// <see cref="Lossy"/> are additions to the parity corpus manifest shape; the parity reader looks
/// fields up by name and ignores the rest, so a manifest carrying them still reads there.</summary>
/// <param name="File">PNG file name inside the corpus directory.</param>
/// <param name="Signature">The number the badge shows, when the frame is labelled with one.</param>
/// <param name="Configuration">Free-text label such as <c>windowed-1600x1200</c>; the pivot groups by it.</param>
/// <param name="VerticalFov">Star Citizen's stored (vertical) FOV in degrees, when known. The sweep applies
/// <c>StarCitizenFovZoom</c> for it in both modes. Null means the calibrated rect, no zoom.</param>
/// <param name="Lossy">True for frames that are not the engine's exact pixels (for example a JPEG
/// screenshot converted to PNG). Reported next to the configuration, never mixed in silently.</param>
public sealed record ManifestFrame(
    string File,
    double? Signature,
    string Configuration,
    double? VerticalFov,
    bool Lossy,
    string? Note)
{
    /// <summary>The label the pivot groups by: configuration plus FOV and the lossy caveat when present.</summary>
    public string ConfigurationLabel =>
        Configuration
        + (VerticalFov is { } fov ? $" @vfov{fov:0.##}" : "")
        + (Lossy ? " [lossy]" : "");
}
