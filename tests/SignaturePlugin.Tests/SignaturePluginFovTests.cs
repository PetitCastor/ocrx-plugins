using Ocrx.Sdk;
using Ocrx.Sdk.Testing;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>The plugin moves its counter ROI when the user changes Star Citizen's FOV.</summary>
public sealed class SignaturePluginFovTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sc-fov-plugin-{Guid.NewGuid():N}");
    private readonly string _file;
    private DateTime _now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    public SignaturePluginFovTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "attributes.xml");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteFov(double fov, DateTime writtenUtc)
    {
        File.WriteAllText(_file, $"<Attributes><Attr name=\"FOV\" value=\"{fov.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"/></Attributes>");
        File.SetLastWriteTimeUtc(_file, writtenUtc);
    }

    private SignaturePlugin Plugin() => new() { FovSource = new StarCitizenFovSource(() => _file, () => _now) };

    private static Task Tick(SignaturePlugin plugin, FakePluginServices services)
        => plugin.OnTickAsync(TickContext.ForTesting(new TickDataBuilder().Text("counter", "").Build(), services), default);

    [Fact]
    public void The_first_subscription_is_already_zoomed()
    {
        WriteFov(83.9863, _now);

        var plugin = Plugin();

        var counter = Assert.Single(plugin.Rois);
        Assert.Equal(StarCitizenFovZoom.Apply(Rois.Counter, StarCitizenFovZoom.Factor(83.9863)), counter);
    }

    [Fact]
    public async Task A_fov_change_moves_the_roi_and_tells_the_engine()
    {
        WriteFov(58.7155, _now);
        var plugin = Plugin();
        var services = new FakePluginServices();

        WriteFov(67.6728, _now.AddSeconds(5));
        _now += StarCitizenFovSource.PollInterval;
        await Tick(plugin, services);

        var pushed = Assert.Single(services.UpdatedRois);
        var expected = StarCitizenFovZoom.Apply(Rois.Counter, StarCitizenFovZoom.Factor(67.6728));
        Assert.Equal(expected, Assert.Single(pushed));
        Assert.Same(pushed, plugin.Rois);
        Assert.Contains(services.Logs, line => line.Contains("Star Citizen FOV 67.6728"));
    }

    /// <summary>Moving within the floor changes nothing on screen, so nothing is sent.</summary>
    [Fact]
    public async Task A_change_below_the_floor_sends_nothing()
    {
        WriteFov(54.5364, _now);
        var plugin = Plugin();
        var services = new FakePluginServices();

        WriteFov(58.7155, _now.AddSeconds(5));
        _now += StarCitizenFovSource.PollInterval;
        await Tick(plugin, services);

        Assert.Empty(services.UpdatedRois);
        Assert.Same(Rois.Counter, Assert.Single(plugin.Rois));
    }

    [Fact]
    public async Task Without_a_fov_source_the_rect_stays_calibrated()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Tick(plugin, services);

        Assert.Empty(services.UpdatedRois);
        Assert.Same(Rois.All, plugin.Rois);
    }
}
