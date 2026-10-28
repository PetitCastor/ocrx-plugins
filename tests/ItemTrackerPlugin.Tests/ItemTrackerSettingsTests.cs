using ItemTrackerPlugin.App;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.Tests;

public sealed class ItemTrackerSettingsTests
{
    [Theory]
    [InlineData("", true, null)]
    [InlineData("   ", true, null)]
    [InlineData("2026-10-10", true, "2026-10-10")]
    [InlineData(" 2026-10-10 ", true, "2026-10-10")]
    [InlineData("10/10/2026", false, null)]
    [InlineData("2026-13-01", false, null)]
    [InlineData("yesterday", false, null)]
    public void Wipe_date_is_blank_or_iso(string value, bool accepted, string? expected)
    {
        Assert.Equal(accepted, ItemTracker.TryParseWipeDate(value, out var date));
        Assert.Equal(expected is null ? null : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), date);
    }

    [Fact]
    public void Live_spec_carries_the_current_values()
    {
        var options = new TrackerOptions
        {
            LogDir = @"E:\Games\StarCitizen\LIVE",
            InceptionDate = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero),
        };

        var spec = ItemTracker.BuildSpec(options);

        Assert.Equal(@"E:\Games\StarCitizen\LIVE", Field(spec, ItemTracker.LogDirSettingId).Value);
        Assert.Equal("2026-10-10", Field(spec, ItemTracker.WipeDateSettingId).Value);
        Assert.Equal("", Field(ItemTracker.BuildSpec(new TrackerOptions { LogDir = "x" }), ItemTracker.WipeDateSettingId).Value);
    }

    /// <summary>
    /// settings-spec.json is what the engine shows before the plugin connects; once connected it
    /// shows the live spec. Anything but the values differing between the two flips in the panel the
    /// moment the plugin starts.
    /// </summary>
    [Fact]
    public void Shipped_spec_matches_the_live_spec_except_for_values()
    {
        Assert.True(SettingsSpecFile.TryRead(Path.Combine(AppContext.BaseDirectory, SettingsSpecFile.FileName), out var shipped));
        var live = ItemTracker.BuildSpec(new TrackerOptions { LogDir = "x" });

        Assert.Equal(
            live.Fields.Select(f => f with { Value = "" }),
            shipped.Fields.Select(f => f with { Value = "" }));
    }

    private static SettingsField Field(SettingsSpec spec, string id) => spec.Fields.Single(f => f.Id == id);
}
