using ItemTrackerPlugin.App;

namespace ItemTrackerPlugin.Tests;

public sealed class TrackerHostTests
{
    /// <summary>A corpus run must never write the real ledger: its root is a fresh temp folder.</summary>
    [Fact]
    public void Replay_gets_a_fresh_throwaway_root_under_temp()
    {
        var first = TrackerHost.DataRootFor(replay: true);
        var second = TrackerHost.DataRootFor(replay: true);

        Assert.StartsWith(Path.GetTempPath(), first, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(first, second);
        Assert.NotEqual(TrackerHost.DataRootFor(replay: false), first);
    }
}
