using System.Text.Json;
using Ocrx.Sdk;
using Xunit;

namespace SignaturePlugin.Tests;

public class SettingsSpecFileTests
{
    [Fact]
    public void ShippedSpec_ThemeOptions_MatchTheLiveOptions() =>
        AssertOptionsMatch("overlayTheme", OverlayThemes.Options);

    [Fact]
    public void ShippedSpec_PositionOptions_MatchTheLiveOptions() =>
        AssertOptionsMatch("position", OverlayPositions.Options);

    // settings-spec.json is what the engine shows before the plugin connects; once connected it shows
    // the live spec built from the same Options lists. A label that differs between the two flips in
    // the UI the moment the plugin starts.
    private static void AssertOptionsMatch(string fieldId, IReadOnlyList<SettingsOption> live)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings-spec.json")));
        var field = doc.RootElement.GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("id").GetString() == fieldId);
        var shipped = field.GetProperty("options").EnumerateArray()
            .Select(o => (o.GetProperty("value").GetString()!, o.GetProperty("label").GetString()!))
            .ToList();

        Assert.Equal(live.Select(o => (o.Value, o.Label)).ToList(), shipped);
    }
}
