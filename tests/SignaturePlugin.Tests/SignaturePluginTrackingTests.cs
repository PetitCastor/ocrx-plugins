using System.Globalization;
using Ocrx.Contracts;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// The plugin wired to a <see cref="BadgeTracker"/>: the ROI set it holds and pushes, replay, FOV, the text it
/// takes from the counter, the absence debounce, the manual-tick dumps and the performance summary. Ticks come
/// from an <see cref="OcrScene"/> scanned through whatever set the plugin currently holds.
/// </summary>
public sealed class SignaturePluginTrackingTests : IDisposable
{
    private static readonly DateTime Written = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sc-track-plugin-{Guid.NewGuid():N}");
    private readonly OcrScene _scene = new();
    private TimeSpan _clock = TimeSpan.Zero;

    public SignaturePluginTrackingTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SignaturePlugin Plugin(StarCitizenFovSource? fov = null)
        => new() { Tracker = new BadgeTracker(Rois.Counter), FovSource = fov, Clock = () => _clock };

    private StarCitizenFovSource Fov(double fov)
    {
        var file = Path.Combine(_dir, "attributes.xml");
        File.WriteAllText(file, $"<Attributes><Attr name=\"FOV\" value=\"{fov.ToString(CultureInfo.InvariantCulture)}\"/></Attributes>");
        File.SetLastWriteTimeUtc(file, Written);
        return new StarCitizenFovSource(() => [file], () => TimeSpan.FromHours(1));
    }

    private Task Tick(SignaturePlugin plugin, IPluginServices services)
        => plugin.OnTickAsync(TickContext.ForTesting(_scene.Scan(plugin.Rois), services), default);

    private static FakePluginServices Replaying()
    {
        var services = new FakePluginServices();
        services.Engine = services.Engine with { ReplayMode = true };
        return services;
    }

    private static NumberBox Number(int x, int y, int width = 46) => new(x, y, width, 15);

    /// <summary>"2,000" where the calibrated rect has it.</summary>
    private static readonly NumberBox Calibrated = Number(1273, 464);

    [Fact]
    public async Task Connecting_to_a_live_engine_pushes_nothing_and_idle_ticks_push_nothing()
    {
        var plugin = Plugin();
        var services = new FakePluginServices();

        await plugin.OnConnectedAsync(services, default);
        for (var i = 0; i < 20; i++)
            await Tick(plugin, services);

        Assert.Empty(services.UpdatedRois);
        Assert.Same(Rois.All, plugin.Rois);
        Assert.Equal(RoiKind.Detailed, plugin.Rois[0].Kind);
    }

    /// <summary>Replay parity is measured at the calibrated rect, like the FOV: no tracking, no moves — and the text is
    /// what the engine read through the Detailed counter, taken by the plain text path.</summary>
    [Fact]
    public async Task A_replaying_engine_keeps_the_calibrated_rect_and_emits_the_text_the_engine_read()
    {
        var plugin = Plugin();
        var services = Replaying();
        _scene.Add("3,600", Number(1273, 464)); // inside the calibrated rect

        await plugin.OnConnectedAsync(services, default);
        for (var i = 0; i < 12; i++)
            await Tick(plugin, services);

        Assert.Empty(services.UpdatedRois);
        Assert.Same(Rois.All, plugin.Rois);
        Assert.Equal(RoiKind.Detailed, plugin.Rois[0].Kind);
        Assert.Contains("\"name\":\"Bexalite\"", Assert.Single(services.Emitted).RawText);
        Assert.DoesNotContain(services.VerboseLogs, l => l.Contains("acquired")); // the tracker never ran
    }

    [Fact]
    public async Task The_number_is_read_from_the_counter_and_a_standing_badge_costs_no_push()
    {
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);
        _scene.Add("3,600", Number(1273, 464));

        for (var i = 0; i < 5; i++)
            await Tick(plugin, services);

        Assert.Contains("\"name\":\"Bexalite\"", Assert.Single(services.Emitted).RawText);
        Assert.Empty(services.UpdatedRois);
    }

    /// <summary>A counter widened to follow the badge takes in the pin icon, which OCR reads as a stray "9": the
    /// text of the whole read does not parse, the number alone does, and that is what is observed.</summary>
    [Fact]
    public async Task A_pin_icon_in_the_widened_counter_does_not_spoil_the_reading()
    {
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);
        _scene.AddLine(("9", new NumberBox(1266, 464, 8, 15)), ("3,600", Number(1290, 464, 46)));

        for (var i = 0; i < 3; i++)
            await Tick(plugin, services);

        Assert.Contains("\"name\":\"Bexalite\"", Assert.Single(services.Emitted).RawText);
    }

    /// <summary>A number that moves is followed by pushes, and the counter keeps its id, scale and kind.</summary>
    [Fact]
    public async Task A_moving_number_moves_the_counter_through_pushes()
    {
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);
        var x = 1273;
        for (var tick = 0; tick < 60; tick++)
        {
            if (tick >= 10) x -= 10;
            _scene.Remove(_ => true).Add("3,600", Number(x, 464, 46));
            await Tick(plugin, services);
        }

        Assert.NotEmpty(services.UpdatedRois);
        Assert.Same(services.UpdatedRois[^1], plugin.Rois);
        Assert.Equal(Rois.Counter.Id, plugin.Rois[0].Id);
        Assert.Equal(Rois.Counter.Scale, plugin.Rois[0].Scale);
        Assert.Equal(Rois.Counter.Kind, plugin.Rois[0].Kind);
        Assert.True(plugin.Rois[0].Rect.X < Rois.Counter.Rect.X);
        Assert.InRange(services.UpdatedRois.Count / 5.0, 0, 3.0); // pushes a second over the 5 s of motion
        Assert.Single(services.Emitted);
    }

    /// <summary>
    /// Pushes — a lost number's search tile coming and going, the counter returning — must not reset the absence
    /// streaks: only a FOV change moves the crop onto the same badge. A badge that has really gone clears after
    /// the usual run of blank reads whatever the tracker pushes meanwhile.
    /// </summary>
    [Fact]
    public async Task Pushes_do_not_stop_a_vanished_badge_from_clearing()
    {
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);
        _scene.Add("3,600", Number(1273, 464));
        for (var i = 0; i < 3; i++)
            await Tick(plugin, services);
        Assert.Single(services.Emitted);

        _scene.Remove(_ => true);
        for (var i = 0; i < SignatureAbsenceDebouncer.ConfirmTicks; i++)
            await Tick(plugin, services);

        Assert.NotEmpty(services.UpdatedRois); // the search tile went out and came back during those ticks
        Assert.Single(services.Cleared);
    }

    [Fact]
    public async Task Under_fov_zoom_the_counter_keeps_the_zoomed_size_and_scale_and_falls_back_to_the_zoomed_rect()
    {
        var factor = StarCitizenFovZoom.Factor(83.9863);
        var zoomed = StarCitizenFovZoom.Apply(Rois.Counter, factor);
        var plugin = Plugin(Fov(83.9863));
        var services = new FakePluginServices();

        await plugin.OnConnectedAsync(services, default);
        Assert.Equal(zoomed, Assert.Single(services.UpdatedRois)[0]); // nothing seen yet: the zoomed calibrated rect

        // The number as the zoomed-out HUD draws it, a few pixels right of where the zoomed rect has it: it fits, but
        // with less margin on the right than a read needs.
        var left = (int)Math.Round(Rois.DigitsMarginLeft * factor);
        var right = (int)Math.Round(Rois.DigitsMarginRight * factor);
        var zoomedBox = Number((int)zoomed.Rect.X + left + 4, (int)zoomed.Rect.Y + 8, (int)Math.Round(46 * factor));
        _scene.Add("17,200", zoomedBox);
        for (var i = 0; i < 12; i++)
            await Tick(plugin, services);

        var placed = plugin.Rois[0];
        Assert.True(zoomed.Rect.Height <= placed.Rect.Height);
        Assert.Equal(zoomed.Scale, placed.Scale);
        Assert.Equal(Rois.Counter.Kind, placed.Kind);

        // The rect moved onto the shifted number: it is a different rect, with the zoomed margins on both sides.
        Assert.NotEqual(zoomed.Rect, placed.Rect);
        Assert.True(zoomedBox.X - placed.Rect.X >= left, "left margin");
        Assert.True(placed.Rect.X + placed.Rect.Width - zoomedBox.Right >= right, "right margin");
        Assert.Equal(1, services.VerboseLogs.Count(l => l.Contains("acquired")));
    }

    [Fact]
    public async Task A_failed_push_of_the_tracked_set_is_retried_on_the_next_tick()
    {
        var plugin = Plugin();
        var services = new FlakyRoiServices(new FakePluginServices(), failures: 1);
        await plugin.OnConnectedAsync(services, default);
        _scene.Add("3,600", Number(1273, 464, 62));

        for (var i = 0; i < 8; i++)
            await Tick(plugin, services);

        Assert.Equal(plugin.Rois, Assert.Single(services.Inner.UpdatedRois));
    }

    private (SignaturePlugin Plugin, List<RoiRect?> Dumped, FakePluginServices Services) ManualRig(bool fullFrame)
    {
        var dumped = new List<RoiRect?>();
        var services = new FakePluginServices
        {
            DumpFrameHandler = (roi, prefix, _) => { dumped.Add(roi); return Task.FromResult<string?>($"{prefix}.png"); },
        };
        var config = new SignaturePluginConfig { DumpFullFrameOnManual = fullFrame };
        var plugin = new SignaturePlugin(null, config, Path.Combine(_dir, "config.json")) { Tracker = new BadgeTracker(Rois.Counter) };
        return (plugin, dumped, services);
    }

    /// <summary>The counter crop is dumped as before; the whole frame — about 4 MB a press — only on its own opt-in.</summary>
    [Fact]
    public async Task A_manual_tick_dumps_only_the_counter_crop_unless_the_full_frame_is_asked_for()
    {
        var (plugin, dumped, services) = ManualRig(fullFrame: false);
        await plugin.OnConnectedAsync(services, default);
        _scene.Add("3,600", Number(1273, 464));

        await plugin.OnManualTickAsync(TickContext.ForTesting(_scene.Scan(plugin.Rois), services), default);

        Assert.Equal([(RoiRect?)Rois.Counter.Rect], dumped);
        Assert.DoesNotContain(services.VerboseLogs, l => l.Contains("full frame"));
    }

    [Fact]
    public async Task A_manual_tick_with_the_option_dumps_the_whole_frame_first_and_the_rect_it_was_read_through()
    {
        var (plugin, dumped, services) = ManualRig(fullFrame: true);
        await plugin.OnConnectedAsync(services, default);
        _scene.Add("3,600", Number(1273, 464));

        await plugin.OnManualTickAsync(TickContext.ForTesting(_scene.Scan(plugin.Rois), services), default);

        Assert.Equal([(RoiRect?)null, Rois.Counter.Rect], dumped);
        Assert.Contains(services.VerboseLogs, l => l.Contains("full frame dumped to signature-frame.png"));
    }

    /// <summary>One line per five seconds, never one per tick — and it carries what the live run is judged by.</summary>
    [Fact]
    public async Task The_performance_summary_is_one_line_per_window_and_never_per_tick()
    {
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);
        _scene.Add("3,600", Number(1273, 464));

        for (var i = 0; i < 50; i++)
        {
            _clock += TimeSpan.FromMilliseconds(100);
            await Tick(plugin, services);
        }

        Assert.DoesNotContain(services.VerboseLogs, l => l.StartsWith("signature perf"));

        _clock += TimeSpan.FromMilliseconds(100); // the window opened on the first tick
        await Tick(plugin, services);

        var line = Assert.Single(services.VerboseLogs, l => l.StartsWith("signature perf"));
        Assert.Contains("ticks=51", line);
        Assert.Contains("state idle/tracking/lost=2/98/0%", line);
        Assert.Contains("search-tiles=0", line);
        Assert.Contains("pushes=0", line);
        Assert.Contains("handler avg=", line);
    }

    // ---- the live failures of 2026-11-01 ----

    /// <summary>
    /// The false capture, through the plugin: "17,140" is on screen, the counter's left edge slices its first
    /// digit and OCR reads "7,140" — Borase x2 in the table. The real value, Aluminum x4, is what stays shown.
    /// </summary>
    [Fact]
    public async Task A_number_cut_by_the_counters_edge_is_never_emitted()
    {
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);
        _scene.Add("17,140", Number(1273, 464, 56));
        for (var i = 0; i < 6; i++)
            await Tick(plugin, services);
        Assert.Contains("\"name\":\"Aluminum\"", Assert.Single(services.Emitted).RawText); // 17,140: Aluminum x4

        for (var i = 0; i < 8; i++)
        {
            var rect = plugin.Rois[0].Rect;
            _scene.Remove(_ => true).Add("17,140", Number((int)rect.X - 9, 464, 56)); // first digit outside the rect
            await Tick(plugin, services);
        }

        Assert.Single(services.Emitted); // still just the Aluminum: the fragment was never an observation
        Assert.DoesNotContain(services.Emitted, r => r.RawText.Contains("Borase"));
        Assert.Contains(services.VerboseLogs, l => l.Contains("raw='7,140'") && l.Contains("cut by the counter's left edge"));
        Assert.DoesNotContain(services.VerboseLogs, l => l.Contains("read=7140"));
    }

    /// <summary>
    /// A cut read says the badge is still there, so it must not confirm an absence — a run of them does not clear the
    /// overlay — and it is not an observation, so it neither shows nor changes the value. "17,140" is followed whole
    /// and shown (Aluminum x4); then the counter's left edge keeps slicing its first digit, which reads "7,140" —
    /// Borase x2 — every tick, for longer than an absence takes to be confirmed.
    /// </summary>
    [Fact]
    public async Task A_cut_read_is_presence_not_absence_and_not_an_observation()
    {
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);
        _scene.Add("17,140", Number(1273, 464, 56));
        for (var i = 0; i < 6; i++)
            await Tick(plugin, services);
        Assert.Contains("\"name\":\"Aluminum\"", Assert.Single(services.Emitted).RawText);

        for (var i = 0; i < SignatureAbsenceDebouncer.ConfirmTicks + 4; i++)
        {
            var rect = plugin.Rois[0].Rect;
            _scene.Remove(_ => true).Add("17,140", Number((int)rect.X - 9, 464, 56));
            await Tick(plugin, services);
        }

        Assert.Contains(services.VerboseLogs, l => l.Contains("raw='7,140'") && l.Contains("cut by the counter's left edge"));
        Assert.Single(services.Emitted); // the fragment was never an observation
        Assert.Empty(services.Cleared);  // and never a blank: the badge is there
    }

    /// <summary>The distance line under the number is no reading: with the number gone it says nothing is there, so
    /// the overlay clears on the usual run of blank reads instead of being held by "15.9km" as a number.</summary>
    [Fact]
    public async Task A_distance_line_left_behind_does_not_hold_the_overlay()
    {
        var plugin = Plugin();
        var services = new FakePluginServices();
        await plugin.OnConnectedAsync(services, default);
        _scene.Add("3,600", Number(1273, 464, 46)).Add("15.9km", new NumberBox(1273, 491, 50, 13));
        for (var i = 0; i < 4; i++)
            await Tick(plugin, services);
        Assert.Contains("\"name\":\"Bexalite\"", Assert.Single(services.Emitted).RawText);

        _scene.Remove(w => w.Text == "3,600");
        for (var i = 0; i < SignatureAbsenceDebouncer.ConfirmTicks; i++)
            await Tick(plugin, services);

        Assert.Single(services.Emitted);
        Assert.Single(services.Cleared);
        Assert.DoesNotContain(services.VerboseLogs, l => l.Contains("read=15.9"));
        Assert.Contains(services.VerboseLogs, l => l.Contains("raw='15.9km'") && l.Contains("no signature in it"));
    }
}
