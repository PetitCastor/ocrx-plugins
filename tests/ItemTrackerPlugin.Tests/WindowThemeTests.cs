using ItemTrackerPlugin.App;
using ItemTrackerPlugin.Window;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.Tests;

public sealed class WindowThemeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"itrk-theme-{Guid.NewGuid():N}");

    public WindowThemeTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Theme_follows_windows_in_the_shipped_spec_and_for_a_fresh_install()
    {
        Assert.True(SettingsSpecFile.TryRead(Path.Combine(AppContext.BaseDirectory, SettingsSpecFile.FileName), out var shipped));
        var field = shipped.Fields.Single(f => f.Id == ItemTracker.ThemeSettingId);

        Assert.Equal(SettingsFieldType.Select, field.Type);
        Assert.Equal("system", field.Value);
        Assert.Equal("Window", field.Group);
        Assert.Equal(["system", "light", "dark"], field.Options!.Select(o => o.Value));
        Assert.Equal(WindowTheme.System, TrackerOptions.Load(Path.Combine(_dir, "settings.json")).Theme);
    }

    [Fact]
    public void A_settings_file_from_before_the_theme_follows_windows()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "LogDir": "E:\Games\StarCitizen\LIVE", "OpenAtStartup": false }""");

        Assert.Equal(WindowTheme.System, TrackerOptions.Load(path).Theme);
    }

    [Fact]
    public void An_unknown_stored_theme_follows_windows()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "LogDir": "x", "Theme": "purple" }""");

        Assert.Equal(WindowTheme.System, TrackerOptions.Load(path).Theme);
    }

    [Fact]
    public void Choosing_dark_is_saved_and_leaves_the_other_settings_alone()
    {
        var path = Path.Combine(_dir, "settings.json");
        var options = TrackerOptions.Load(path);
        options.SaveOpenAtStartup(false, path);
        var logDir = options.LogDir;

        options.SaveTheme(WindowTheme.Dark, path);

        Assert.Equal(WindowTheme.Dark, options.Theme);
        var reloaded = TrackerOptions.Load(path);
        Assert.Equal(WindowTheme.Dark, reloaded.Theme);
        Assert.False(reloaded.OpenAtStartup);
        Assert.Equal(logDir, reloaded.LogDir);
        Assert.Equal("dark", ItemTracker.BuildSpec(reloaded).Fields.Single(f => f.Id == ItemTracker.ThemeSettingId).Value);
    }

    [Theory]
    [InlineData("system", true, "System")]
    [InlineData("light", true, "Light")]
    [InlineData(" Dark ", true, "Dark")]
    [InlineData("auto", false, "System")]
    [InlineData("", false, "System")]
    [InlineData(null, false, "System")]
    public void The_theme_travels_as_one_of_its_option_values(string? value, bool accepted, string expected)
    {
        Assert.Equal(accepted, WindowThemes.TryParse(value, out var theme));
        Assert.Equal(Enum.Parse<WindowTheme>(expected), theme);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Every_theme_round_trips_through_an_option(string name)
    {
        var theme = Enum.Parse<WindowTheme>(name);
        var value = WindowThemes.Format(theme);

        Assert.Contains(WindowThemes.Options, o => o.Value == value);
        Assert.True(WindowThemes.TryParse(value, out var parsed));
        Assert.Equal(theme, parsed);
    }

    [Fact]
    public void Light_and_dark_ignore_the_windows_mode()
    {
        Assert.False(WindowThemes.IsDark(WindowTheme.Light));
        Assert.True(WindowThemes.IsDark(WindowTheme.Dark));
    }
}
