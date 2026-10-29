using Ocrx.Sdk;

namespace ItemTrackerPlugin.Overlay;

/// <summary>
/// Lets only status lines and clears through to the overlay. Every record the plugin emits fans out
/// to every sink, and the overlay writes a record that lacks the template's field as its raw text:
/// without this, each screen observation would flash its JSON in game.
/// </summary>
internal sealed class StatusOnlySink(IRecordSink overlay) : IRecordSink
{
    public ValueTask EmitAsync(CaptureRecord record, CancellationToken ct) =>
        record.Kind == RecordKind.Cleared || StatusRecord.IsStatus(record)
            ? overlay.EmitAsync(record, ct)
            : ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => overlay.DisposeAsync();
}
