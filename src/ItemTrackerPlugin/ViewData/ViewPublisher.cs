using System.Text;
using System.Text.Json.Nodes;
using ItemTrackerPlugin.App;
using Microsoft.Extensions.Logging;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.ViewData;

/// <summary>
/// Writes the view's data document each time the snapshot is rebuilt. A document over the SDK's limit
/// is written again with fewer moves per item, then with none, so a big ledger still gets a view
/// instead of a stale one. A replay writes nothing: a corpus run never touches the real ledger's files.
/// </summary>
internal sealed class ViewPublisher : IDisposable
{
    /// <summary>The engine looks the data up under the plugin's install folder, which is its catalog id.</summary>
    internal const string DataFolder = "item-tracker";

    private static readonly int[] HistorySteps = [ViewDocument.FullHistory, 3, 0];

    private readonly TrackerState _state;
    private readonly string _path;
    private readonly ILogger? _log;
    private readonly Lock _gate = new();

    public ViewPublisher(TrackerState state, string path, ILogger? log = null)
    {
        _state = state;
        _path = path;
        _log = log;
        _state.Changed += Publish;
    }

    public static string DefaultPath => PluginView.DataPath(DataFolder);

    /// <summary>Writes the current snapshot now. Never throws: a failed write costs one update of the view.</summary>
    public void Publish()
    {
        lock (_gate)
        {
            try
            {
                WriteSmallestFit(_state.Current, _path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _log?.LogWarning(ex, "Could not publish the view data to {Path}", _path);
            }
        }
    }

    internal static void WriteSmallestFit(TrackerState.Snapshot snapshot, string path) =>
        PluginView.WriteData(path, Fit(history => ViewDocument.Build(snapshot, history))
            ?? throw new InvalidOperationException("The view data is over the size limit even without history."));

    /// <summary>The first document of the ladder, fullest first, that fits the limit; null when none does.</summary>
    internal static JsonObject? Fit(Func<int, JsonObject> build)
    {
        foreach (var history in HistorySteps)
        {
            var document = build(history);
            if (Encoding.UTF8.GetByteCount(document.ToJsonString()) <= PluginView.MaxDataBytes) return document;
        }

        return null;
    }

    public void Dispose() => _state.Changed -= Publish;
}
