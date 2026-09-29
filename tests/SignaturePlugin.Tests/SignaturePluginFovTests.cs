using System.Globalization;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>The plugin moves its counter ROI when the user changes Star Citizen's FOV.</summary>
public sealed class SignaturePluginFovTests : IDisposable
{
    private static readonly DateTime Written = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sc-fov-plugin-{Guid.NewGuid():N}");
    private readonly string _file;
    private TimeSpan _now = TimeSpan.FromHours(1);

    public SignaturePluginFovTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "attributes.xml");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteFov(double fov, DateTime writtenUtc)
    {
        File.WriteAllText(_file, $"<Attributes><Attr name=\"FOV\" value=\"{fov.ToString(CultureInfo.InvariantCulture)}\"/></Attributes>");
        File.SetLastWriteTimeUtc(_file, writtenUtc);
    }

    private void ChangeFov(double fov)
    {
        WriteFov(fov, Written.AddSeconds(_now.TotalSeconds));
        _now += StarCitizenFovSource.PollInterval;
    }

    private SignaturePlugin Plugin() => new() { FovSource = new StarCitizenFovSource(() => [_file], () => _now) };

    private static RoiSubscription Zoomed(double fov)
        => StarCitizenFovZoom.Apply(Rois.Counter, StarCitizenFovZoom.Factor(fov));

    private static Task Tick(SignaturePlugin plugin, IPluginServices services, string text = "")
        => plugin.OnTickAsync(TickContext.ForTesting(new TickDataBuilder().Text("counter", text).Build(), services), default);

    /// <summary>Nothing is read before a session says whether it is live or a replay.</summary>
    [Fact]
    public void Before_a_session_the_rect_is_the_calibrated_one()
    {
        WriteFov(83.9863, Written);

        Assert.Same(Rois.All, Plugin().Rois);
    }

    [Fact]
    public async Task Connecting_to_a_live_engine_zooms_and_pushes_before_the_first_tick()
    {
        WriteFov(83.9863, Written);
        var plugin = Plugin();
        var services = new FakePluginServices();

        await plugin.OnConnectedAsync(services, default);

        var pushed = Assert.Single(services.UpdatedRois);
        Assert.Equal(Zoomed(83.9863), Assert.Single(pushed));
        Assert.Same(pushed, plugin.Rois);
    }

    /// <summary>Recorded frames were captured at whatever FOV they were captured at: the live
    /// game's setting must not move the rect over them.</summary>
    [Fact]
    public async Task A_replaying_engine_keeps_the_calibrated_rect()
    {
        WriteFov(83.9863, Written);
        var plugin = Plugin();
        var services = new FakePluginServices();
        services.Engine = services.Engine with { ReplayMode = true };

        await plugin.OnConnectedAsync(services, default);
        await Tick(plugin, services);

        Assert.Empty(services.UpdatedRois);
        Assert.Same(Rois.All, plugin.Rois);
    }

    [Fact]
    public async Task A_fov_change_moves_the_roi_and_tells_the_engine()
    {
        WriteFov(58.7155, Written);
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);

        ChangeFov(67.6728);
        await Tick(plugin, services);

        var pushed = Assert.Single(services.UpdatedRois);
        Assert.Equal(Zoomed(67.6728), Assert.Single(pushed));
        Assert.Same(pushed, plugin.Rois);
        Assert.Contains(services.Logs, line => line.Contains("Star Citizen FOV 67.6728"));
    }

    /// <summary>Moving within the floor changes nothing on screen, so nothing is sent.</summary>
    [Fact]
    public async Task A_change_below_the_floor_sends_nothing()
    {
        WriteFov(54.5364, Written);
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);

        ChangeFov(58.7155);
        await Tick(plugin, services);

        Assert.Empty(services.UpdatedRois);
        Assert.Same(Rois.Counter, Assert.Single(plugin.Rois));
    }

    /// <summary>A push the engine did not take is retried on the next tick, not forgotten until the
    /// next FOV change — and the failure is said once, not once per tick.</summary>
    [Fact]
    public async Task A_failed_push_is_retried_on_the_next_tick()
    {
        WriteFov(58.7155, Written);
        var plugin = Plugin();
        var services = new FlakyRoiServices(new FakePluginServices(), failures: 2);
        await plugin.OnConnectedAsync(services, default);

        ChangeFov(77.5521);
        await Tick(plugin, services);
        await Tick(plugin, services);
        Assert.Empty(services.Inner.UpdatedRois);

        await Tick(plugin, services);

        Assert.Equal(Zoomed(77.5521), Assert.Single(Assert.Single(services.Inner.UpdatedRois)));
        Assert.Single(services.Inner.Logs, line => line.Contains("counter ROI update failed"));
    }

    /// <summary>A reconnect subscribes Rois afresh, so it must not be pushed a second time.</summary>
    [Fact]
    public async Task A_reconnect_does_not_push_what_the_host_already_subscribed()
    {
        WriteFov(83.9863, Written);
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);

        await plugin.OnConnectedAsync(services, default);

        Assert.Single(services.UpdatedRois);
    }

    /// <summary>The dump shows the rect the manual tick's text was read through.</summary>
    [Fact]
    public async Task A_manual_tick_dumps_the_rect_it_was_read_through()
    {
        WriteFov(58.7155, Written);
        var plugin = Plugin();
        Ocrx.Contracts.RoiRect? dumped = null;
        var services = new FakePluginServices
        {
            DumpFrameHandler = (roi, _, _) => { dumped = roi; return Task.FromResult<string?>(null); },
        };
        await plugin.OnConnectedAsync(services, default);

        ChangeFov(83.9863);
        await plugin.OnManualTickAsync(
            TickContext.ForTesting(new TickDataBuilder().Text("counter", "3600").Build(), services), default);

        Assert.Equal(Rois.Counter.Rect, dumped);
        Assert.Equal(Zoomed(83.9863), Assert.Single(plugin.Rois));
    }

    [Fact]
    public async Task Without_a_fov_source_the_rect_stays_calibrated()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await plugin.OnConnectedAsync(services, default);
        await Tick(plugin, services);

        Assert.Empty(services.UpdatedRois);
        Assert.Same(Rois.All, plugin.Rois);
    }
}
