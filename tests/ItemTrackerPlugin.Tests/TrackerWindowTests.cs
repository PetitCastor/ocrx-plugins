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
    /// The file carries a frame of its own for each size the tray, the title bar and the taskbar ask for at
    /// 100-200 % display scaling, and 256 px for Explorer: none has to be scaled from another, which is
    /// what came out blurry. Read from the icon directory, since System.Drawing reports a requested size
    /// whether or not a frame of that size exists.
    /// </summary>
    [Fact]
    public void The_icon_file_has_a_frame_for_each_size()
    {
        using var stream = typeof(ItemTracker).Assembly.GetManifestResourceStream(TrackerWindow.IconResource)!;
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16();
        Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16();

        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            int width = reader.ReadByte();
            reader.ReadBytes(15);
            sizes.Add(width == 0 ? 256 : width);
        }

        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 256], sizes);
    }

    /// <summary>Each small frame loads at its size. The 256 px one is a PNG, which System.Drawing skips.</summary>
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
    public void Loading_with_no_size_keeps_the_large_frames()
    {
        using var all = TrackerWindow.LoadIcon(size: null);

        // The window's icon keeps its large frames for the taskbar, so a 48 px copy is not scaled up from 16.
        using var large = new Icon(all, 48, 48);
        Assert.Equal(48, large.Width);
    }
}
