using ItemTrackerPlugin.Screen;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;

namespace ItemTrackerPlugin.Tests;

public sealed class ItemTrackerScreenTests
{
    private static FakePluginServices Services() => new()
    {
        Engine = new FakePluginServices().Engine with { ReplayMode = true },
    };

    private static TickContext Tick(FakePluginServices services, string right, string left = "PERSONAL/ ALL") =>
        TickContext.ForTesting(
            new TickDataBuilder().Text(ScreenRois.HeaderRight, right).Text(ScreenRois.HeaderLeft, left).Build(), services);

    [Fact]
    public async Task Starts_with_only_the_headers()
    {
        await using var tracker = new ItemTracker();

        Assert.Same(ScreenRois.Idle, tracker.Rois);
        Assert.Equal(RoiErrorPolicy.SkipErrored, tracker.ErrorPolicy);
    }

    [Fact]
    public async Task A_kiosk_header_switches_to_the_full_set_once()
    {
        var services = Services();
        await using var tracker = new ItemTracker();

        await tracker.OnTickAsync(Tick(services, "LOCAL/ LOCAL"), default);
        await tracker.OnTickAsync(Tick(services, "LOCAL/ LOCAL"), default);

        Assert.Same(ScreenRois.For(ScreenContext.Kiosk), tracker.Rois);
        Assert.Single(services.UpdatedRois);
        Assert.Same(ScreenRois.For(ScreenContext.Kiosk), services.UpdatedRois[0]);
    }

    [Fact]
    public async Task Leaving_the_screen_goes_back_to_the_headers()
    {
        var services = Services();
        await using var tracker = new ItemTracker();

        await tracker.OnTickAsync(Tick(services, "LOCAL/ LOCAL"), default);
        await tracker.OnTickAsync(Tick(services, "", ""), default);

        Assert.Same(ScreenRois.Idle, tracker.Rois);
        Assert.Equal(2, services.UpdatedRois.Count);
    }

    [Fact]
    public async Task A_failed_header_changes_nothing()
    {
        var services = Services();
        await using var tracker = new ItemTracker();
        await tracker.OnTickAsync(Tick(services, "LOCAL/ LOCAL"), default);

        var failed = new TickDataBuilder().Errored(ScreenRois.HeaderRight, "boom").Text(ScreenRois.HeaderLeft, "").Build();
        await tracker.OnTickAsync(TickContext.ForTesting(failed, services), default);

        Assert.Same(ScreenRois.For(ScreenContext.Kiosk), tracker.Rois);
        Assert.Single(services.UpdatedRois);
    }

    [Fact]
    public async Task A_fixed_set_never_switches()
    {
        var services = Services();
        await using var tracker = new ItemTracker(fixedSet: true);

        await tracker.OnTickAsync(Tick(services, ""), default);

        Assert.Same(ScreenRois.For(ScreenContext.Kiosk), tracker.Rois);
        Assert.Empty(services.UpdatedRois);
    }

    [Fact]
    public async Task A_hovered_item_is_emitted_as_an_observation_record()
    {
        var services = Services();
        await using var tracker = new ItemTracker(fixedSet: true);
        var tick = new TickDataBuilder()
            .Text(ScreenRois.HeaderRight, "LOCAL/ LOCAL").Text(ScreenRois.HeaderLeft, "PERSONAL/ ALL")
            .Detailed(ScreenRois.TooltipRight, "CRUZ Dark", "Volume: 1000 pscu", "Contents Remaining: 100%")
            .FrameSeq(7)
            .Build();

        await tracker.OnTickAsync(TickContext.ForTesting(tick, services), default);

        var record = Assert.Single(services.Emitted);
        Assert.Equal("ItemTrackerPlugin", record.Plugin);
        Assert.Equal(RecordKind.Observation, record.Kind);
        Assert.Equal("CRUZ Dark", record.Fields!["name"]);
        Assert.Equal("Kiosk", record.Fields["panel"]);
        Assert.Equal("7", record.Fields["frameSeq"]);
    }
}
