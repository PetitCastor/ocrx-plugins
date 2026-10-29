using Ocrx.Sdk;

namespace ItemTrackerPlugin.Overlay;

/// <summary>Builds the SDK overlay, behind <see cref="StatusOnlySink"/>.</summary>
public sealed class StatusOverlayFactory(IOverlaySinkFactory inner) : IOverlaySinkFactory
{
    public IRecordSink Create(OverlaySpec spec, IPluginOutput log) => new StatusOnlySink(inner.Create(spec, log));
}
