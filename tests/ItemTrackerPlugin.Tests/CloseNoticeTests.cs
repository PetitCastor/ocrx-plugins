using ItemTrackerPlugin.Window;

namespace ItemTrackerPlugin.Tests;

public sealed class CloseNoticeTests
{
    [Fact]
    public void Only_the_first_close_of_a_run_shows_the_balloon()
    {
        var notice = new CloseNotice();

        Assert.True(notice.TakeFirst());
        Assert.False(notice.TakeFirst());
        Assert.False(notice.TakeFirst());
    }

    [Fact]
    public void Each_run_starts_with_its_own_first_close()
    {
        new CloseNotice().TakeFirst();

        Assert.True(new CloseNotice().TakeFirst());
    }

    [Fact]
    public void Two_closes_racing_show_one_balloon()
    {
        var notice = new CloseNotice();

        var shown = Enumerable.Range(0, 64).AsParallel().Count(_ => notice.TakeFirst());

        Assert.Equal(1, shown);
    }

    /// <summary>The wording the user confirmed; a change is a decision, not a refactor.</summary>
    [Fact]
    public void The_balloon_says_where_the_window_went()
    {
        Assert.Equal("Item Tracker", CloseNotice.Title);
        Assert.Equal("Item Tracker is still running. Click its icon in the notification area to open it again.", CloseNotice.Text);
    }
}
