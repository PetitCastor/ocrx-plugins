using ItemTrackerPlugin.Ingest;

namespace ItemTrackerPlugin.Tests;

public class FreightCursorTests
{
    [Theory]
    [InlineData("MA==", 0)]
    [InlineData("MTI=", 12)]
    [InlineData("MjUw", 250)]
    [InlineData("NTAw", 500)]
    public void Reads_the_position_a_cursor_encodes(string cursor, long expected)
    {
        Assert.True(FreightCursor.TryDecode(cursor, out var position));
        Assert.Equal(expected, position);
    }

    [Theory]
    [InlineData("MA", 0)]
    [InlineData("MTI", 12)]
    [InlineData(" MjUw ", 250)]
    public void Accepts_a_cursor_whose_padding_was_trimmed(string cursor, long expected)
    {
        Assert.True(FreightCursor.TryDecode(cursor, out var position));
        Assert.Equal(expected, position);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!!")]
    [InlineData("bm90YW51bWJlcg==")] // "notanumber"
    [InlineData("LTU=")] // "-5"
    [InlineData("MS41")] // "1.5"
    [InlineData("OTk5OTk5OTk5OTk5OTk5OTk5OTk5")] // 20 digits: overflows long
    public void Rejects_a_cursor_that_is_not_a_position(string cursor)
    {
        Assert.False(FreightCursor.TryDecode(cursor, out _));
    }

    [Fact]
    public void Rejects_a_missing_cursor()
    {
        Assert.False(FreightCursor.TryDecode(null, out _));
    }
}
