using ItemTrackerPlugin.Screen;

namespace ItemTrackerPlugin.Tests;

public sealed class ScreenParserTests
{
    [Theory]
    [InlineData("1/3", 1, 3)]
    [InlineData("7/7", 7, 7)]
    [InlineData(" 1 / 5 ", 1, 5)]
    [InlineData("1/ø", null, null)]
    [InlineData("5/1", null, null)]
    [InlineData("", null, null)]
    public void Page_counter(string text, int? page, int? of)
    {
        var read = PageParser.Parse(text);
        Assert.Equal(page, read?.Page);
        Assert.Equal(of, read?.Of);
    }

    [Theory]
    [InlineData("662M/5G", 662_000_000L, 5_000_000_000L)]
    [InlineData("7K/750K", 7_000L, 750_000L)]
    [InlineData("CAPACITY: 61/5000 SCU", 61_000_000L, 5_000_000_000L)]
    [InlineData("0/117K", 0L, 117_000L)]
    [InlineData("6ø2M/5G", 602_000_000L, 5_000_000_000L)]
    public void Capacity_is_in_micro_scu(string text, long used, long max)
    {
        var read = CapacityParser.Parse(text);
        Assert.Equal(used, read?.UsedMicroScu);
        Assert.Equal(max, read?.MaxMicroScu);
    }

    [Theory]
    [InlineData("2x", 2)]
    [InlineData("5x", 5)]
    [InlineData("3X", 3)]
    [InlineData("X1", 1)]
    [InlineData("x12", 12)]
    [InlineData("0x", null)]
    [InlineData("27ø/27ø", null)]
    [InlineData("", null)]
    public void Quantity_badge(string text, int? expected) => Assert.Equal(expected, QuantityBadgeParser.Parse(text));

    [Theory]
    [InlineData("LOCAL/ LOCAL", "", ScreenContext.Kiosk, null)]
    [InlineData("LOCAL / LOCAL", "PERSONAL / ALL", ScreenContext.Kiosk, null)]
    [InlineData("SHIV / PERSONAL STORAGE", "PERSONAL / ALL", ScreenContext.ShipStorage, "SHIV")]
    [InlineData("", "PERSONAL / ALL", ScreenContext.Personal, null)]
    [InlineData("", "", ScreenContext.Unknown, null)]
    [InlineData("FREIGHT MANAGER", "", ScreenContext.Unknown, null)]
    public void Header_names_the_screen(string right, string left, ScreenContext context, string? detail)
    {
        var header = HeaderParser.Parse(right, left);
        Assert.Equal(context, header.Context);
        Assert.Equal(detail, header.Detail);
    }
}
