using ItemTrackerPlugin.Screen;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;

namespace ItemTrackerPlugin.Tests;

public sealed class ScreenReaderTests
{
    private static readonly string[] BadgerTooltip =
    [
        "CF-227 Badger Repeater", "Volume: 252000 pscu", "Ammo Count: 0/0", "Manufacturer: Klaus 6 Werner",
        "Item Type: Laser Repeater", "Size: 2", "Registered To You",
    ];

    private static readonly ScreenReader Reader = new(() => null);

    private static TickDataBuilder Headers(string right, string left = "PERSONAL/ ALL") =>
        new TickDataBuilder().Text(ScreenRois.HeaderRight, right).Text(ScreenRois.HeaderLeft, left);

    private static TickDataBuilder WithTooltip(TickDataBuilder b, RoiId id, params string[] lines) =>
        b.Detailed(id, lines.Select(l => (OcrLineSpec)l).ToArray());

    [Fact]
    public void Header_read_gives_the_context()
    {
        var header = ScreenReader.ReadHeader(Headers("LOCAL/ LOCAL").Build());

        Assert.Equal(ScreenContext.Kiosk, header?.Context);
    }

    [Fact]
    public void A_header_that_failed_to_read_is_no_header()
    {
        var tick = new TickDataBuilder().Errored(ScreenRois.HeaderRight, "boom").Text(ScreenRois.HeaderLeft, "PERSONAL/ ALL").Build();

        Assert.Null(ScreenReader.ReadHeader(tick));
    }

    [Fact]
    public void Kiosk_tooltip_page_and_badge_make_one_observation()
    {
        var b = WithTooltip(Headers("LOCAL/ LOCAL"), ScreenRois.TooltipRight, BadgerTooltip);
        b.Text(ScreenRois.Page(2), "1/4");
        b.Detailed(ScreenRois.Capacity, "CAPACITY:", "662M/5G", "SCU");
        // A wide tile at column 0 draws its badge in cell (0, 1).
        b.Text(ScreenRois.Badge(0, 1, 4), "").Text(ScreenRois.Badge(0, 1, 6), "2x").Text(ScreenRois.Badge(0, 1, 8), "");

        var observation = new ScreenReader(() => null, _ => new GridTile(0, 0, 2)).Read(b.Build(), new ScreenHeader(ScreenContext.Kiosk, null));

        Assert.NotNull(observation);
        Assert.Equal(ScreenContext.Kiosk, observation.Panel);
        Assert.Equal("CF-227 Badger Repeater", observation.Tooltip.Name);
        Assert.Equal("tooltip-right", observation.SourceRoi);
        Assert.Equal(new PageCounter(1, 4), observation.Page);
        Assert.Equal(new CapacityReading(662_000_000, 5_000_000_000), observation.Capacity);
        Assert.Equal(2, observation.Quantity);
        Assert.Equal(new GridTile(0, 0, 2), observation.Tile);
    }

    [Fact]
    public void Ship_storage_carries_the_ship_model()
    {
        var b = WithTooltip(Headers("SHIV / PERSONAL STORAGE"), ScreenRois.TooltipRight, BadgerTooltip);

        var observation = Reader.Read(b.Build(), new ScreenHeader(ScreenContext.ShipStorage, "SHIV"));

        Assert.Equal(ScreenContext.ShipStorage, observation?.Panel);
        Assert.Equal("SHIV", observation?.ShipModel);
        Assert.Null(observation?.Tile);
        Assert.Null(observation?.Quantity);
    }

    [Fact]
    public void A_left_tooltip_is_the_personal_inventory()
    {
        var b = WithTooltip(Headers(""), ScreenRois.TooltipLeft, "Aztalan Legs", "Volume: 36000 pscu", "Item Type: Light Armor");

        var observation = Reader.Read(b.Build(), new ScreenHeader(ScreenContext.Personal, null));

        Assert.Equal(ScreenContext.Personal, observation?.Panel);
        Assert.Equal("tooltip-left", observation?.SourceRoi);
        Assert.Null(observation?.Page);
    }

    [Fact]
    public void Unknown_screen_reads_nothing_even_with_a_tooltip_in_the_tick()
    {
        var b = WithTooltip(Headers(""), ScreenRois.TooltipRight, BadgerTooltip);

        Assert.Null(Reader.Read(b.Build(), ScreenHeader.Unknown));
    }

    [Fact]
    public void Regions_of_a_set_that_is_not_subscribed_yet_read_nothing()
    {
        Assert.Null(Reader.Read(Headers("LOCAL/ LOCAL").Build(), new ScreenHeader(ScreenContext.Kiosk, null)));
    }

    [Fact]
    public void Freight_manager_junk_in_a_tooltip_region_is_not_an_observation()
    {
        var b = WithTooltip(Headers("LOCAL/ LOCAL"), ScreenRois.TooltipRight, "MOVE TO ELEVATOR");

        Assert.Null(Reader.Read(b.Build(), new ScreenHeader(ScreenContext.Kiosk, null)));
    }

    [Fact]
    public void Item_class_comes_from_the_resolver()
    {
        var resolver = new ItemClassResolver(ItemTrackerPlugin.Naming.GameLocalization.Parse(["item_NameKLWE_LaserRepeater_S2=CF-227 Badger Repeater"]));
        var b = WithTooltip(Headers("LOCAL/ LOCAL"), ScreenRois.TooltipRight, BadgerTooltip);

        var observation = new ScreenReader(() => resolver).Read(b.Build(), new ScreenHeader(ScreenContext.Kiosk, null));

        Assert.Equal("KLWE_LaserRepeater_S2", observation?.ItemClass);
    }
}
