namespace SignatureReaderSpike;

/// <summary>
/// Whether a reading is trusted enough to show. The two thresholds were picked on the same corpus
/// they are measured on, so they are a first guess and not a validated setting.
/// </summary>
internal static class Gate
{
    public const double MinMean = 0.75;
    public const double MinWorst = 0.60;

    public static bool Accepts(Reading? reading) =>
        reading is { Value: not null }
        && !reading.Text.StartsWith('0')
        && reading.MeanScore >= MinMean
        && reading.MinScore >= MinWorst;
}
