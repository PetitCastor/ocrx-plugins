using System.Drawing;
using ItemTrackerPlugin.Window;

namespace ItemTrackerPlugin.Tests;

/// <summary>The window itself is UI and checked by hand; what it loads at start is checked here.</summary>
public sealed class TrackerWindowTests
{
    [Fact]
    public void The_tray_icon_ships_inside_the_plugin_and_loads_at_tray_size()
    {
        using var stream = typeof(ItemTracker).Assembly.GetManifestResourceStream(TrackerWindow.IconResource);
        Assert.NotNull(stream);

        using var icon = new Icon(stream, 16, 16);
        Assert.Equal(16, icon.Width);
    }
}
