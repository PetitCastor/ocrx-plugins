namespace SignaturePlugin;

/// <summary>What <see cref="BadgeTracker"/> is doing, which is also what a tick costs.</summary>
internal enum TrackingState
{
    /// <summary>No number has been read, or it was given up on: the counter is at the calibrated rect, no
    /// search runs, nothing is pushed — the cost of the plugin before tracking existed.</summary>
    Idle,

    /// <summary>The counter ROI is reading the number and following it.</summary>
    Tracking,

    /// <summary>The number was read within the last few seconds and is not being read now: the counter holds
    /// its place and, every few ticks, one small search tile looks for it.</summary>
    Lost,
}
