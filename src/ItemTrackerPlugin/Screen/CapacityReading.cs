namespace ItemTrackerPlugin.Screen;

/// <summary>The <c>&lt;used&gt;/&lt;max&gt; SCU</c> line, in µSCU: the game abbreviates with
/// K, M and G (<c>662M/5G</c> is 662 SCU of 5000); a figure with no suffix is whole SCU.</summary>
public readonly record struct CapacityReading(long UsedMicroScu, long MaxMicroScu);
