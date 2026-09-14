using Ocrx.Sdk;
using Xunit;

namespace SignaturePlugin.Tests;

public class OverlayThemesTests
{
    [Fact]
    public void Default_LeavesTheUsersOverlaySettingsUntouched()
    {
        var overlay = new OverlaySpec
        {
            Width = 321,
            Height = 65,
            FontFamily = "Custom Font",
            ForegroundColor = "#010203",
            BackgroundColor = "#040506",
            CornerRadius = 9,
            Padding = 3,
        };

        Apply(OverlayThemes.Default, overlay);

        Assert.Equal(321, overlay.Width);
        Assert.Equal(65, overlay.Height);
        Assert.Equal("Custom Font", overlay.FontFamily);
        Assert.Equal("#010203", overlay.ForegroundColor);
        Assert.Equal("#040506", overlay.BackgroundColor);
        Assert.Equal(9, overlay.CornerRadius);
        Assert.Equal(3, overlay.Padding);
    }

    [Fact]
    public void Citizen_AppliesTheOutlinedTechnicalPill()
    {
        var overlay = Apply(OverlayThemes.Citizen);

        Assert.Equal(560, overlay.Width);
        Assert.Equal(76, overlay.Height);
        Assert.Equal(28, overlay.FontSize);
        Assert.NotNull(overlay.FontFile);
        Assert.EndsWith(Path.Combine("Assets", "Fonts", "Rajdhani-SemiBold.ttf"), overlay.FontFile);
        Assert.False(overlay.UsePixelText);
        Assert.Equal("#B8F2FF", overlay.ForegroundColor);
        Assert.Equal("#071D2B", overlay.BackgroundColor);
        Assert.Equal(235, overlay.BackgroundAlpha);
        Assert.Equal("#32BDE6", overlay.BorderColor);
        Assert.Equal(2, overlay.BorderWidth);
        Assert.Equal(4, overlay.CornerRadius);
        Assert.Equal(18, overlay.CornerAccentLength);
    }

    [Fact]
    public void Retro_AppliesThePixelPill()
    {
        var overlay = Apply(OverlayThemes.Retro);

        Assert.Equal(560, overlay.Width);
        Assert.Equal(88, overlay.Height);
        Assert.Equal(24, overlay.FontSize);
        Assert.NotNull(overlay.FontFile);
        Assert.EndsWith(Path.Combine("Assets", "Fonts", "PressStart2P-Regular.ttf"), overlay.FontFile);
        Assert.True(overlay.UsePixelText);
        Assert.Equal("#FFEF5A", overlay.ForegroundColor);
        Assert.Equal("#1A1435", overlay.BackgroundColor);
        Assert.Equal(240, overlay.BackgroundAlpha);
        Assert.Equal("#6B5BA8", overlay.BorderColor);
        Assert.Equal(1, overlay.BorderWidth);
        Assert.Equal(0, overlay.CornerRadius);
        Assert.Equal(0, overlay.CornerAccentLength);
    }

    [Fact]
    public void UnknownTheme_ExplainsTheSupportedValues()
    {
        var error = Assert.Throws<ArgumentException>(() => Apply("neon"));

        Assert.Contains("Unsupported overlayTheme 'neon'", error.Message);
        Assert.Contains("default", error.Message);
        Assert.Contains("citizen", error.Message);
        Assert.Contains("retro", error.Message);
    }

    private static OverlaySpec Apply(string theme, OverlaySpec? overlay = null)
    {
        overlay ??= new OverlaySpec();
        var config = new SignaturePluginConfig
        {
            OverlayTheme = theme,
            Outputs = [new SinkSpec { Type = "overlay", Overlay = overlay }],
        };

        OverlayThemes.Apply(config);
        return overlay;
    }
}
