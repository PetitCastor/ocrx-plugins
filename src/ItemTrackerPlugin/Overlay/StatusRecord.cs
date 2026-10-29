using Ocrx.Sdk;

namespace ItemTrackerPlugin.Overlay;

/// <summary>The record that carries one status line to the overlay.</summary>
public static class StatusRecord
{
    /// <summary>The field the overlay template reads, and the mark of a status record among the plugin's records.</summary>
    public const string StatusField = "status";

    public static CaptureRecord Show(string line, DateTime timestamp, string plugin) =>
        new(timestamp, plugin, TriggerKind.Auto, line)
        {
            Kind = RecordKind.Observation,
            Fields = new Dictionary<string, string> { [StatusField] = line },
        };

    public static bool IsStatus(CaptureRecord record) =>
        record.Kind == RecordKind.Observation && record.Fields?.ContainsKey(StatusField) == true;
}
