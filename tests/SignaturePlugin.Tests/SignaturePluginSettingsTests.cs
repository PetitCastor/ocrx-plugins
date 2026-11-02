using Ocrx.Contracts;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;
using Xunit;

namespace SignaturePlugin.Tests;

// SignaturePlugin as the first consumer of the generic settings SDK: it projects its overlay theme
// and position as SELECT fields and applies an edit to either entirely in-process. These assert the
// projection, the live rebuild-and-persist, and that a bad or unowned value changes nothing.
public class SignaturePluginSettingsTests
{
    [Fact]
    public async Task Connect_projects_theme_and_position_as_selects_carrying_the_current_values()
    {
        var (config, path) = TempConfig(OverlayThemes.Citizen);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnConnectedAsync(services, default);

        var fields = Assert.Single(services.Published).Fields;
        Assert.Equal(4, fields.Count);

        var theme = fields.Single(field => field.Id == "overlayTheme");
        Assert.Equal(SettingsFieldType.Select, theme.Type);
        Assert.Equal(OverlayThemes.Citizen, theme.Value);
        Assert.NotNull(theme.Options);
        // Options come straight from OverlayThemes, so the panel can never offer a theme Apply rejects.
        Assert.Equal(new[] { "default", "citizen", "retro" }, theme.Options.Select(option => option.Value));

        var position = fields.Single(field => field.Id == "position");
        Assert.Equal(SettingsFieldType.Select, position.Type);
        Assert.Equal(OverlayPositions.TopCenter, position.Value);
        Assert.NotNull(position.Options);
        Assert.Equal(9, position.Options.Count);

        // The size is a bounded INT — what the engine draws as a slider — carrying the current percentage.
        var size = fields.Single(field => field.Id == "overlayScale");
        Assert.Equal(SettingsFieldType.Int, size.Type);
        Assert.Equal("100", size.Value);
        Assert.Equal(50, size.Min);
        Assert.Equal(100, size.Max);

        var scanArea = fields.Single(field => field.Id == "roiScale");
        Assert.Equal(SettingsFieldType.Int, scanArea.Type);
        Assert.Equal("100", scanArea.Value);
        Assert.Equal(100, scanArea.Min);
        Assert.Equal(200, scanArea.Max);
    }

    [Fact]
    public async Task Scan_area_edit_grows_the_counter_rect_pushes_it_and_leaves_the_overlay_alone()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyValue("roiScale", "150"), services, default);

        var pushed = Assert.Single(Assert.Single(services.UpdatedRois));
        Assert.Equal(new RoiRect(1264, 443, 105, 66), pushed.Rect);
        Assert.Equal(Rois.Counter.Scale, pushed.Scale);
        Assert.Same(services.UpdatedRois[0], plugin.Rois);
        Assert.Empty(services.Rebuilt); // the overlay window is not torn down for a crop change
        Assert.Equal("150", Assert.Single(services.Published).Fields.Single(f => f.Id == "roiScale").Value);
        Assert.Equal(150, PluginConfig.Load<SignaturePluginConfig>(path).RoiScale);

        await plugin.OnApplySettings(ApplyValue("roiScale", "100"), services, default);
        Assert.Equal(Rois.Counter, Assert.Single(services.UpdatedRois[1]));
    }

    [Fact]
    public void A_saved_scan_area_is_in_the_rect_the_host_subscribes_at_connect()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        config.RoiScale = 125;

        var plugin = new SignaturePlugin(null, config, path);

        Assert.Equal(RoiScales.Apply(Rois.Counter, 125), Assert.Single(plugin.Rois));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("201")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task Scan_area_outside_the_slider_range_is_rejected(string value)
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyValue("roiScale", value), services, default);

        Assert.Empty(services.UpdatedRois);
        Assert.Empty(services.Published);
        Assert.Equal(100, PluginConfig.Load<SignaturePluginConfig>(path).RoiScale);
    }

    [Fact]
    public async Task Size_edit_shrinks_the_live_overlay_and_persists_only_the_percentage()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyScale("50"), services, default);

        var rebuilt = Overlay(Assert.IsType<SignaturePluginConfig>(Assert.Single(services.Rebuilt)));
        Assert.Equal(210, rebuilt.Width);
        Assert.Equal(44, rebuilt.Height);
        Assert.Equal(12, rebuilt.FontSize);
        Assert.Equal("50", ScaleField(services).Value);

        // The base overlay on disk keeps its full size, so moving the slider back up restores it.
        var reloaded = PluginConfig.Load<SignaturePluginConfig>(path);
        Assert.Equal(50, reloaded.OverlayScale);
        Assert.Equal(560, Overlay(reloaded).Width);
        Assert.Equal(88, Overlay(reloaded).Height);

        await plugin.OnApplySettings(ApplyScale("100"), services, default);
        Assert.Equal(420, Overlay((SignaturePluginConfig)services.Rebuilt[1]).Width);
        Assert.Equal(88, Overlay((SignaturePluginConfig)services.Rebuilt[1]).Height);
    }

    [Theory]
    [InlineData("49")]
    [InlineData("101")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("75.5")]
    public async Task Size_outside_the_slider_range_is_rejected(string value)
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyScale(value), services, default);

        Assert.Empty(services.Rebuilt);
        Assert.Empty(services.Published);
        Assert.Equal(100, PluginConfig.Load<SignaturePluginConfig>(path).OverlayScale);
    }

    [Fact]
    public async Task Size_and_theme_compose_on_the_themed_dimensions()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyScale("50"), services, default);
        await plugin.OnApplySettings(Apply(OverlayThemes.Citizen), services, default);

        var rebuilt = Overlay((SignaturePluginConfig)services.Rebuilt[1]);
        Assert.Equal(225, rebuilt.Width); // citizen's 450 at 50%, not the default pill's 210
        Assert.Equal(14, rebuilt.FontSize);
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
        Assert.Equal(OverlayThemes.Citizen, ThemeField(services).Value);

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
        Assert.Equal(OverlayThemes.Citizen, ThemeField(services).Value);
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

    [Fact]
    public async Task Valid_position_apply_rebuilds_republishes_and_persists_the_position()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyPosition("bottomright"), services, default);

        var rebuilt = Assert.IsType<SignaturePluginConfig>(Assert.Single(services.Rebuilt));
        Assert.Equal(OverlayAnchor.BottomRight, Overlay(rebuilt).Anchor);
        Assert.Equal("bottomright", PositionField(services).Value);

        var reloaded = PluginConfig.Load<SignaturePluginConfig>(path);
        Assert.Equal("bottomright", reloaded.Position);
    }

    [Fact]
    public async Task Invalid_position_preserves_the_current_position_and_touches_nothing()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyPosition("offscreen"), services, default);

        Assert.Empty(services.Rebuilt);
        Assert.Empty(services.Published);
        Assert.Equal(OverlayPositions.TopCenter, config.Position);
        Assert.Contains(services.Logs, line => line.Contains("offscreen"));
    }

    [Fact]
    public async Task Both_fields_valid_in_one_batch_apply_together_and_rebuild_once()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyBoth(OverlayThemes.Citizen, "bottomright"), services, default);

        var rebuilt = Assert.IsType<SignaturePluginConfig>(Assert.Single(services.Rebuilt));
        Assert.Equal("Rajdhani SemiBold", Overlay(rebuilt).FontFamily);
        Assert.Equal(OverlayAnchor.BottomRight, Overlay(rebuilt).Anchor);

        Assert.Equal(OverlayThemes.Citizen, ThemeField(services).Value);
        Assert.Equal("bottomright", PositionField(services).Value);

        var reloaded = PluginConfig.Load<SignaturePluginConfig>(path);
        Assert.Equal(OverlayThemes.Citizen, reloaded.OverlayTheme);
        Assert.Equal("bottomright", reloaded.Position);
    }

    [Fact]
    public async Task An_invalid_theme_alongside_a_valid_position_aborts_both_edits()
    {
        var (config, path) = TempConfig(OverlayThemes.Citizen);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyBoth("neon", "bottomright"), services, default);

        Assert.Empty(services.Rebuilt);
        Assert.Empty(services.Published);
        Assert.Equal(OverlayThemes.Citizen, config.OverlayTheme);
        Assert.Equal(OverlayPositions.TopCenter, config.Position);
        Assert.Contains(services.Logs, line => line.Contains("neon"));
    }

    [Fact]
    public async Task A_valid_theme_alongside_an_invalid_position_aborts_both_edits()
    {
        var (config, path) = TempConfig(OverlayThemes.Citizen);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyBoth(OverlayThemes.Retro, "offscreen"), services, default);

        Assert.Empty(services.Rebuilt);
        Assert.Empty(services.Published);
        Assert.Equal(OverlayThemes.Citizen, config.OverlayTheme);
        Assert.Equal(OverlayPositions.TopCenter, config.Position);
        Assert.Contains(services.Logs, line => line.Contains("offscreen"));
    }

    [Fact]
    public async Task Position_only_apply_preserves_the_current_nondefault_theme_preset()
    {
        var (config, path) = TempConfig(OverlayThemes.Citizen);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnApplySettings(ApplyPosition("bottomright"), services, default);

        var rebuilt = Assert.IsType<SignaturePluginConfig>(Assert.Single(services.Rebuilt));
        // The theme preset (font/size/colours) survives a position-only change...
        Assert.Equal("Rajdhani SemiBold", Overlay(rebuilt).FontFamily);
        Assert.Equal(76, Overlay(rebuilt).Height);
        // ...while the anchor moves to the newly applied position.
        Assert.Equal(OverlayAnchor.BottomRight, Overlay(rebuilt).Anchor);

        var reloaded = PluginConfig.Load<SignaturePluginConfig>(path);
        Assert.Equal(OverlayThemes.Citizen, reloaded.OverlayTheme);
        Assert.Equal("bottomright", reloaded.Position);
    }

    [Fact]
    public async Task Apply_forces_the_overlay_to_redraw_even_when_the_reading_is_unchanged()
    {
        var (config, path) = TempConfig(OverlayThemes.Default);
        var plugin = new SignaturePlugin(null, config, path);
        var services = new FakePluginServices();

        await plugin.OnTickAsync(Tick("3600", services), default);
        Assert.Single(services.Emitted);

        await plugin.OnApplySettings(Apply(OverlayThemes.Citizen), services, default);

        // Same signature as before the apply. Without clearing the dedupe cache after the rebuild,
        // EmitObservation would see it as unchanged and skip emitting — leaving the freshly rebuilt
        // overlay window blank, since it never received its first Show() call.
        await plugin.OnTickAsync(Tick("3600", services), default);

        Assert.Equal(2, services.Emitted.Count);
    }

    private static TickContext Tick(string counterText, FakePluginServices services) =>
        TickContext.ForTesting(new TickDataBuilder().Text("counter", counterText).Build(), services);

    private static ApplySettings Apply(string theme) => new([new SettingsValue("overlayTheme", theme)]);

    private static ApplySettings ApplyValue(string id, string value) => new([new SettingsValue(id, value)]);

    private static ApplySettings ApplyScale(string percent) => new([new SettingsValue("overlayScale", percent)]);

    private static ApplySettings ApplyPosition(string position) => new([new SettingsValue("position", position)]);

    private static ApplySettings ApplyBoth(string theme, string position) =>
        new([new SettingsValue("overlayTheme", theme), new SettingsValue("position", position)]);

    private static SettingsField ThemeField(FakePluginServices services) =>
        Assert.Single(services.Published).Fields.Single(field => field.Id == "overlayTheme");

    private static SettingsField ScaleField(FakePluginServices services) =>
        Assert.Single(services.Published).Fields.Single(field => field.Id == "overlayScale");

    private static SettingsField PositionField(FakePluginServices services) =>
        Assert.Single(services.Published).Fields.Single(field => field.Id == "position");

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
