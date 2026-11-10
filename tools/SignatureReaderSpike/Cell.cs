namespace SignatureReaderSpike;

/// <summary>One glyph position in a located badge: its box in the band and, once classified, its scores.</summary>
internal sealed class Cell
{
    public required Box Box { get; init; }

    public float[] Feature { get; set; } = [];

    /// <summary>Correlation with each digit's template, -1 where the font has no template for that digit.</summary>
    public double[] Scores { get; set; } = new double[10];

    public int Digit { get; set; } = -1;

    public double Best { get; set; }

    /// <summary>Best score minus the runner-up: how clearly this glyph is one digit and not another.</summary>
    public double Margin { get; set; }
}
