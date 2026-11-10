namespace SignatureReaderSpike;

/// <summary>An ink image with the components found in it at one threshold.</summary>
internal sealed record InkMap(byte[] Ink, int Width, int Height, int[] Labels, List<Comp> Comps, int Threshold);
