using ItemTrackerPlugin.App;

namespace ItemTrackerPlugin.Window;

/// <summary>
/// Hands the window a fresh message each time the snapshot is rebuilt, and once at the start when a
/// snapshot already exists. Before the first rebuild there is nothing to send: an empty document would
/// show "no items" for a ledger that is still loading. Never throws: a failed build costs one update.
/// </summary>
internal sealed class ViewFeed : IDisposable
{
    private readonly TrackerState _state;
    private readonly Action<string> _post;
    private readonly Action<string>? _log;
    private readonly Lock _gate = new();

    public ViewFeed(TrackerState state, Action<string> post, Action<string>? log = null)
    {
        _state = state;
        _post = post;
        _log = log;
        _state.Changed += Send;
        Send();
    }

    /// <summary>Under a lock, so two rebuilds finishing together cannot post out of order.</summary>
    private void Send()
    {
        lock (_gate)
        {
            try
            {
                var snapshot = _state.Current;
                if (snapshot.BuiltAt == DateTimeOffset.MinValue) return;
                _post(ViewMessage.From(snapshot));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log?.Invoke($"ItemTrackerPlugin: could not update the window: {ex.Message}");
            }
        }
    }

    public void Dispose() => _state.Changed -= Send;
}
