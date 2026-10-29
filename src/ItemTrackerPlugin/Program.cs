using ItemTrackerPlugin;
using ItemTrackerPlugin.Overlay;
using Ocrx.Sdk;
using Ocrx.Sdk.Overlay;

// The host owns connect, reconnect and shutdown; ItemTracker starts the log ingestion on the first
// connect and stops it on dispose, once the host has returned. No config file: the tracker's own
// settings live in its data root, and the pipe name is the SDK default unless --pipe says otherwise.
// The one output is the in-game status line, through the SDK overlay with its own look and position.
await using var tracker = new ItemTracker();
return await OcrxPluginHost.RunAsync(tracker, args, new PluginHostOptions
{
    Config = TrackerConfig.Create(),
    OverlayFactory = new StatusOverlayFactory(new OverlaySinkFactory()),
});
