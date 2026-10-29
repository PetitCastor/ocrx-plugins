using ItemTrackerPlugin.App;
using ItemTrackerPlugin.Window;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.Tests;

public sealed class WindowStartupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"itrk-window-{Guid.NewGuid():N}");

    public WindowStartupTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(false, true, "Open")]
    [InlineData(false, false, "TrayOnly")]
    [InlineData(true, true, "None")]
    [InlineData(true, false, "None")]
    public void Startup_shows_the_window_only_when_asked_and_never_in_a_replay(bool replay, bool openAtStartup, string expected)
    {
        Assert.Equal(Enum.Parse<WindowStart>(expected), WindowStartup.Decide(replay, openAtStartup));
    }

    [Fact]
    public void Open_at_startup_defaults_to_on_in_the_shipped_spec_and_for_a_fresh_install()
    {
        Assert.True(SettingsSpecFile.TryRead(Path.Combine(AppContext.BaseDirectory, SettingsSpecFile.FileName), out var shipped));
        var field = shipped.Fields.Single(f => f.Id == ItemTracker.OpenAtStartupSettingId);

        Assert.Equal(SettingsFieldType.Toggle, field.Type);
        Assert.Equal("true", field.Value);
        Assert.Equal("Window", field.Group);
        Assert.True(TrackerOptions.Load(Path.Combine(_dir, "settings.json")).OpenAtStartup);
    }

    [Fact]
    public void A_settings_file_from_before_the_toggle_reads_as_on()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "LogDir": "E:\Games\StarCitizen\LIVE" }""");

        Assert.True(TrackerOptions.Load(path).OpenAtStartup);
    }

    [Fact]
    public void Turning_it_off_is_saved_and_leaves_the_log_source_alone()
    {
        var path = Path.Combine(_dir, "settings.json");
        var options = TrackerOptions.Load(path);
        var logDir = options.LogDir;

        options.SaveOpenAtStartup(false, path);

        Assert.False(options.OpenAtStartup);
        var reloaded = TrackerOptions.Load(path);
        Assert.False(reloaded.OpenAtStartup);
        Assert.Equal(logDir, reloaded.LogDir);
        Assert.Equal("false", ItemTracker.BuildSpec(reloaded).Fields.Single(f => f.Id == ItemTracker.OpenAtStartupSettingId).Value);
    }

    [Theory]
    [InlineData("true", true, true)]
    [InlineData("False", true, false)]
    [InlineData(" true ", true, true)]
    [InlineData("yes", false, false)]
    [InlineData("", false, false)]
    public void The_toggle_travels_as_true_or_false(string value, bool accepted, bool expected)
    {
        Assert.Equal(accepted, WindowStartup.TryParseToggle(value, out var on));
        Assert.Equal(expected, on);
    }
}
