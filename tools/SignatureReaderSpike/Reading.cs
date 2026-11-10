namespace SignatureReaderSpike;

/// <summary>
/// What one band read as. <see cref="LayoutValid"/> says the glyphs and commas are arranged like a
/// number; <see cref="Value"/> is that number once templates have named every digit, else null.
/// </summary>
internal sealed record Reading(
    Badge Badge, List<Cell> Cells, string Text, bool LayoutValid, int? Value, string Font, double MeanScore, double MinScore, double MinMargin, int Threshold,
    char Channel);
