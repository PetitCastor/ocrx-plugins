using Ocrx.Sdk;
using Ocrx.Sdk.Testing;
using Xunit;

namespace SignaturePlugin.Tests;

// SignaturePlugin as the first consumer of the generic settings SDK: it projects its overlay theme
// as one SELECT field and applies an edit entirely in-process. These assert the projection, the
// live rebuild-and-persist, and that a bad or unowned value changes nothing.
public class SignaturePluginSettingsTests
{
    [Fact]
    public async Task Connect_projects_the_theme_as_one_select_carrying_the_current_value()
    {
        var (config, path) = TempConfig(OverlayThemes.Citizen);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnConnectedAsync(services, default);

        var field = Assert.Single(Assert.Single(services.Published).Fields);
        Assert.Equal("overlayTheme", field.Id);
        Assert.Equal(SettingsFieldType.Select, field.Type);
        Assert.Equal(OverlayThemes.Citizen, field.Value);
        Assert.NotNull(field.Options);
        // Options come straight from OverlayThemes, so the panel can never offer a theme Apply rejects.
        Assert.Equal(new[] { "default", "citizen", "retro" }, field.Options.Select(option => option.Value));
    }

    [Fact]
    public async Task Valid_apply_rebuilds_the_live_overlay_republishes_and_persists_the_theme()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(Apply(OverlayThemes.Citizen), services, default);

        // Live rebuild carries the themed overlay...
        var rebuilt = Assert.IsType<SignaturePluginConfig>(Assert.Single(services.Rebuilt));
        Assert.Equal("Rajdhani SemiBold", Overlay(rebuilt).FontFamily);
        Assert.Equal(76, Overlay(rebuilt).Height);

        // ...the panel gets the new value...
        Assert.Equal(OverlayThemes.Citizen, Assert.Single(Assert.Single(services.Published).Fields).Value);

        // ...and only the theme string persists: the base overlay preset is untouched on disk, so a
        // later switch has a clean preset to rebuild from.
        var reloaded = PluginConfig.Load<SignaturePluginConfig>(path);
        Assert.Equal(OverlayThemes.Citizen, reloaded.OverlayTheme);
        Assert.Equal("Segoe UI", Overlay(reloaded).FontFamily);
        Assert.Equal(88, Overlay(reloaded).Height);
    }

    [Fact]
    public async Task Switching_back_to_default_rebuilds_a_clean_overlay_not_the_previous_theme()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(Apply(OverlayThemes.Citizen), services, default);
        await plugin.OnApplySettings(Apply(OverlayThemes.Default), services, default);

        Assert.Equal(2, services.Rebuilt.Count);
        Assert.Equal("Rajdhani SemiBold", Overlay((SignaturePluginConfig)services.Rebuilt[0]).FontFamily);
        // The second rebuild resets to the base preset rather than keeping citizen's font — proof the
        // theme is derived from a pristine base every time, not mutated cumulatively.
        Assert.Equal("Segoe UI", Overlay((SignaturePluginConfig)services.Rebuilt[1]).FontFamily);
        Assert.Equal(OverlayThemes.Default, PluginConfig.Load<SignaturePluginConfig>(path).OverlayTheme);
    }

    [Fact]
    public async Task Re_applying_the_current_theme_republishes_without_rebuilding()
    {
        var (config, path) = TempConfig(OverlayThemes.Citizen);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(Apply(OverlayThemes.Citizen), services, default);

        // No rebuild — re-drawing the overlay for a no-op change would flicker it — but the panel is
        // still refreshed with the current value.
        Assert.Empty(services.Rebuilt);
        Assert.Equal(OverlayThemes.Citizen, Assert.Single(Assert.Single(services.Published).Fields).Value);
    }

    [Fact]
    public async Task Apply_preserves_unrelated_sinks_and_base_settings()
    {
        var dir = Directory.CreateTempSubdirectory("sigplugin-settings").FullName;
        var path = System.IO.Path.Combine(dir, "config.json");
        File.WriteAllText(path, """
            {
              "pipeName": "Custom.Pipe",
              "saveDebugFrames": true,
              "overlayTheme": "default",
              "outputs": [
                { "type": "json", "path": "captures/out.jsonl", "recordClears": true },
                { "type": "overlay", "overlay": { "template": "{cluster}", "backgroundColor": "#111827" } }
              ]
            }
            """);
        var config = PluginConfig.Load<SignaturePluginConfig>(path);
        var plugin = new SignaturePlugin(null, config, path);

        await plugin.OnApplySettings(Apply(OverlayThemes.Retro), new FakePluginServices(), default);

        var reloaded = PluginConfig.Load<SignaturePluginConfig>(path);
        Assert.Equal(OverlayThemes.Retro, reloaded.OverlayTheme);
        Assert.Equal("Custom.Pipe", reloaded.PipeName);
        Assert.True(reloaded.SaveDebugFrames);
        var json = reloaded.Outputs.Single(output => string.Equals(output.Type, "json", StringComparison.OrdinalIgnoreCase));
        Assert.True(json.RecordClears);
        Assert.EndsWith("out.jsonl", json.Path);
        // The relative spelling survives the round-trip rather than being rewritten absolute on disk.
        var rawPath = ReadRawPath(path, "json");
        Assert.NotNull(rawPath);
        Assert.False(System.IO.Path.IsPathRooted(rawPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_value_is_rejected_not_coerced_to_default(string blank)
    {
        var (config, path) = TempConfig(OverlayThemes.Citizen);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(Apply(blank), services, default);

        Assert.Empty(services.Rebuilt);
        Assert.Empty(services.Published);
        Assert.Equal(OverlayThemes.Citizen, config.OverlayTheme);
        Assert.Equal(OverlayThemes.Citizen, PluginConfig.Load<SignaturePluginConfig>(path).OverlayTheme);
    }

    [Fact]
    public async Task Invalid_value_preserves_the_current_theme_and_touches_nothing()
    {
        var (config, path) = TempConfig(OverlayThemes.Citizen);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(Apply("neon"), services, default);

        Assert.Empty(services.Rebuilt);
        Assert.Empty(services.Published);
        Assert.Equal(OverlayThemes.Citizen, config.OverlayTheme);
        Assert.Equal(OverlayThemes.Citizen, PluginConfig.Load<SignaturePluginConfig>(path).OverlayTheme);
        Assert.Contains(services.Logs, line => line.Contains("neon"));
    }

    [Fact]
    public async Task An_edit_for_an_unowned_id_is_a_silent_no_op()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(new ApplySettings([new SettingsValue("someOtherPlugin.knob", "on")]), services, default);

        Assert.Empty(services.Rebuilt);
        Assert.Empty(services.Published);
        Assert.Empty(services.Logs);
        Assert.Equal(OverlayThemes.Default, config.OverlayTheme);
    }

    private static ApplySettings Apply(string theme) => new([new SettingsValue("overlayTheme", theme)]);

    // The path string exactly as persisted on disk, before Load resolves it against the config dir —
    // so a test can prove Save wrote the relative spelling back rather than an absolute path.
    private static string? ReadRawPath(string configPath, string type)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        foreach (var output in document.RootElement.GetProperty("outputs").EnumerateArray())
            if (string.Equals(output.GetProperty("type").GetString(), type, StringComparison.OrdinalIgnoreCase))
                return output.TryGetProperty("path", out var path) ? path.GetString() : null;
        return null;
    }

    private static OverlaySpec Overlay(SignaturePluginConfig config) =>
        config.Outputs.First(output => string.Equals(output.Type, "overlay", StringComparison.OrdinalIgnoreCase)).Overlay!;

    private static (SignaturePluginConfig Config, string Path) TempConfig(string theme)
    {
        var dir = Directory.CreateTempSubdirectory("sigplugin-settings").FullName;
        var path = System.IO.Path.Combine(dir, "config.json");
        File.WriteAllText(path, $$"""
            {
              "pipeName": "OCRX.Engine",
              "overlayTheme": "{{theme}}",
              "outputs": [
                { "type": "overlay", "overlay": { "template": "{cluster}", "backgroundColor": "#111827" } }
              ]
            }
            """);
        return (PluginConfig.Load<SignaturePluginConfig>(path), path);
    }
}
