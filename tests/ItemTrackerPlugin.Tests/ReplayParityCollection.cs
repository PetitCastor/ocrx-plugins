namespace ItemTrackerPlugin.Tests;

/// <summary>
/// A spawned engine owns a named pipe and a Windows OCR instance, so two replay tests must never
/// run at once. Without <c>DisableParallelization</c> the collection would still run beside every
/// other collection in the assembly.
/// </summary>
[CollectionDefinition("ReplayParity", DisableParallelization = true)]
public sealed class ReplayParityCollection;
