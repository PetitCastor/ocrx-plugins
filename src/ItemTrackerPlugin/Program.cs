using ItemTrackerPlugin;
using Ocrx.Sdk;

// The host owns connect, reconnect and shutdown; ItemTracker starts the log ingestion on the first
// connect and stops it on dispose, once the host has returned. No config file: the tracker's own
// settings live in its data root, and the pipe name is the SDK default unless --pipe says otherwise.
await using var tracker = new ItemTracker();
return await OcrxPluginHost.RunAsync(tracker, args, new PluginHostOptions { ConfigFileName = null });
