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

    /// <summary>
    /// Each size the tray, the title bar and the taskbar ask for at 100-200 % display scaling is drawn for
    /// that size, so none is scaled from another — a scaled one is what came out blurry. The 256 px frame
    /// is a PNG for Explorer's view of the executable; System.Drawing skips PNG frames, so it is not here.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(48)]
    [InlineData(64)]
    public void The_icon_ships_a_frame_drawn_for_each_size(int size)
    {
        using var icon = TrackerWindow.LoadIcon(new Size(size, size));
        Assert.Equal(size, icon.Width);
        Assert.Equal(size, icon.Height);
    }

    [Fact]
    public void The_window_gets_the_icon_with_every_size_not_the_tray_one()
    {
        using var all = TrackerWindow.LoadIcon(size: null);

        // The window's icon keeps its large frames for the taskbar, so a 48 px copy is not scaled up from 16.
        using var large = new Icon(all, 48, 48);
        Assert.Equal(48, large.Width);
    }
}
