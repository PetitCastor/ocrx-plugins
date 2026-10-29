using ItemTrackerPlugin.Overlay;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Screen;
using Ocrx.Sdk;
using Ocrx.Sdk.Overlay;
using Ocrx.Sdk.Testing;

namespace ItemTrackerPlugin.Tests;

public sealed class StatusOverlayTests
{
    private sealed class Capture : IRecordSink
    {
        public List<CaptureRecord> Got { get; } = [];

        public ValueTask EmitAsync(CaptureRecord record, CancellationToken ct)
        {
            Got.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SilentOutput : IPluginOutput
    {
        public void WriteLine(string message = "") { }

        public void UpdateStatus(string statusText) { }
    }

    private static FakePluginServices Services() => new()
    {
        Engine = new FakePluginServices().Engine with { ReplayMode = true },
    };

    private static TickContext Tick(IPluginServices services, string right, string page = "") =>
        TickContext.ForTesting(
            new TickDataBuilder().Text(ScreenRois.HeaderRight, right).Text(ScreenRois.HeaderLeft, "PERSONAL/ ALL")
                .Text(ScreenRois.Page(2), page).Build(),
            services);

    [Fact]
    public async Task A_kiosk_tick_emits_the_status_line_for_the_open_station()
    {
        var services = Services();
        await using var tracker = new ItemTracker(fixedSet: false, startHost: false)
        {
            StationSource = () => new StationStatus("1", "Keeger", new StateTally(14, 0, 0, 1), []),
        };

        await tracker.OnTickAsync(Tick(services, "LOCAL/ LOCAL", "2/5"), default);

        var record = Assert.Single(services.Emitted, StatusRecord.IsStatus);
        Assert.Equal("Keeger kiosk · p.2/5 · 14 ✓ · 1 ⚠", record.Fields![StatusRecord.StatusField]);
        Assert.Equal("ItemTrackerPlugin", record.Plugin);
    }

    [Fact]
    public async Task Leaving_the_inventory_clears_the_overlay_and_nothing_shows_before_it()
    {
        var services = Services();
        await using var tracker = new ItemTracker(fixedSet: false, startHost: false);

        await tracker.OnTickAsync(Tick(services, ""), default);
        Assert.Empty(services.Emitted);
        Assert.Empty(services.Cleared);

        await tracker.OnTickAsync(Tick(services, "LOCAL/ LOCAL"), default);
        for (var i = 0; i < 3; i++) await tracker.OnTickAsync(Tick(services, ""), default);

        Assert.Single(services.Emitted);
        Assert.Single(services.Cleared);
    }

    [Fact]
    public async Task A_ship_locker_status_names_the_model()
    {
        var services = Services();
        await using var tracker = new ItemTracker(fixedSet: false, startHost: false);

        await tracker.OnTickAsync(Tick(services, "SHIV / PERSONAL STORAGE", "1/2"), default);

        Assert.Equal("SHIV locker · p.1/2", Assert.Single(services.Emitted).Fields![StatusRecord.StatusField]);
    }

    [Fact]
    public async Task The_overlay_gets_status_lines_and_clears_and_nothing_else()
    {
        var capture = new Capture();
        await using var sink = new StatusOnlySink(capture);
        var screen = new CaptureRecord(DateTime.UtcNow, "ItemTrackerPlugin", TriggerKind.Auto, "{\"panel\":\"Kiosk\"}")
        {
            Fields = new Dictionary<string, string> { ["panel"] = "Kiosk" },
        };

        await sink.EmitAsync(screen, default);
        await sink.EmitAsync(StatusRecord.Show("Kiosk", DateTime.UtcNow, "ItemTrackerPlugin"), default);
        await sink.EmitAsync(new CaptureRecord(DateTime.UtcNow, "ItemTrackerPlugin", TriggerKind.Auto, "") { Kind = RecordKind.Cleared }, default);

        Assert.Equal([RecordKind.Observation, RecordKind.Cleared], capture.Got.Select(r => r.Kind));
        Assert.True(StatusRecord.IsStatus(capture.Got[0]));
    }

    [Fact]
    public void The_only_output_is_one_overlay_that_reads_the_status_field_with_the_sdk_look()
    {
        var output = Assert.Single(TrackerConfig.Create().Outputs);
        var overlay = output.Overlay!;
        var sdk = new OverlaySpec();

        Assert.Equal("overlay", output.Type);
        Assert.Equal("{status}", overlay.Template);
        Assert.Equal(0, overlay.LingerMs);

        // Only the template and the linger differ from the SDK's defaults: no per-plugin styling.
        Assert.Equal(
            (sdk.Anchor, sdk.OffsetX, sdk.OffsetY, sdk.Width, sdk.Height, sdk.FontFamily, sdk.FontSize, sdk.ForegroundColor,
                sdk.BackgroundColor, sdk.BackgroundAlpha, sdk.CornerRadius, sdk.BorderWidth, sdk.Padding),
            (overlay.Anchor, overlay.OffsetX, overlay.OffsetY, overlay.Width, overlay.Height, overlay.FontFamily, overlay.FontSize,
                overlay.ForegroundColor, overlay.BackgroundColor, overlay.BackgroundAlpha, overlay.CornerRadius, overlay.BorderWidth,
                overlay.Padding));
    }

    [Fact]
    public void The_factory_wraps_the_sdk_overlay_so_screen_records_stay_off_it()
    {
        var created = new StatusOverlayFactory(new OverlaySinkFactory()).Create(new OverlaySpec(), new SilentOutput());

        Assert.IsType<StatusOnlySink>(created);
    }
}
