using ItemTrackerPlugin.Screen;

namespace ItemTrackerPlugin.Tests;

/// <summary>The raw reads are what Windows OCR returned on the 2026-10-28 and 10-29 kiosk frames,
/// noise included.</summary>
public sealed class TooltipParserTests
{
    [Fact]
    public void Pistol_with_a_comparison_tooltip_keeps_only_the_first_block()
    {
        var tooltip = TooltipParser.Parse(
        [
            "Arclight \"Glimmer\" Pistol", "Volume: 2500 pscu", "Manufacturer: Klaus 6 Werner",
            "Item Type: Pistol", "Class: Energy (Laser)", "Battery Size: 30", "Rate Of Fire: 300 rpm",
            "A handheld laser made famous as Kyle Fenris'", "it's viewed.", "Registered To You", "1",
            "ParaMed \"AA Support\" Medical Device", "Volume: 2500 pscu", "Manufacturer: CureLife",
            "Item Type: Medical Device",
        ]);

        Assert.NotNull(tooltip);
        Assert.Equal("Arclight \"Glimmer\" Pistol", tooltip.Name);
        Assert.Equal(2500, tooltip.VolumeMicroScu);
        Assert.Equal("Klaus & Werner", tooltip.Manufacturer);
        Assert.Equal("Pistol", tooltip.ItemType);
        Assert.Equal("Energy (Laser)", tooltip.Class);
        Assert.True(tooltip.RegisteredToYou);
    }

    [Fact]
    public void Armor_with_a_glued_item_type_and_a_worn_comparison()
    {
        var tooltip = TooltipParser.Parse(
        [
            "Aztalan Arms", "Volume: 8000 pscu", "Item TypesLight Armor", "Damage feduction: 20%",
            "Temp. Rating: -38 / 68 •C", "Radiation Protection: 26000 REM", "Registered To You",
            "ORC-mkV Arms Scorched", "Volume: 10000 pscu", "Item Type: Medium Armor",
        ]);

        Assert.NotNull(tooltip);
        Assert.Equal("Aztalan Arms", tooltip.Name);
        Assert.Equal(8000, tooltip.VolumeMicroScu);
        Assert.Equal("Light Armor", tooltip.ItemType);
        Assert.True(tooltip.RegisteredToYou);
    }

    [Theory]
    [InlineData("Item TypeSRacing Helmet")]
    [InlineData("Item Type}Racing Helmet")]
    [InlineData("Item Type9.Racing Helmet")]
    [InlineData("Item Type: Racing Helmet")]
    public void Glued_item_types_resolve(string line)
    {
        var tooltip = TooltipParser.Parse(["Odyssey II Racing Helmet Alpha", "Volume: 19000 pscu", line]);
        Assert.Equal("Racing Helmet", tooltip?.ItemType);
    }

    [Fact]
    public void Weapon_reads_size_and_ammo()
    {
        var tooltip = TooltipParser.Parse(
        [
            "CF-227 Badger Repeater", "Volume: 252000 pscu", "Ammo Count: 0/0", "Manufacturer: Klaus 6 Werner",
            "Item Type: Laser Repeater", "Size: 2", "Fire Rate: 750.00 RPM", "Registered To You",
        ]);

        Assert.NotNull(tooltip);
        Assert.Equal(252000, tooltip.VolumeMicroScu);
        Assert.Equal("0/0", tooltip.AmmoCount);
        Assert.Equal("Laser Repeater", tooltip.ItemType);
        Assert.Equal(2, tooltip.Size);
        Assert.Null(tooltip.ContentsRemaining);
    }

    [Fact]
    public void Slashed_zero_in_a_number_is_a_zero()
    {
        var tooltip = TooltipParser.Parse(["Aztalan Legs", "Capacity: 5000 pscl.J", "Volume: 36ø00 pscu", "Contents Remaining: ø/5000"]);

        Assert.Equal(36000, tooltip?.VolumeMicroScu);
        Assert.Equal("0/5000", tooltip?.ContentsRemaining);
    }

    [Fact]
    public void Consumable_has_no_registration_and_no_manufacturer()
    {
        var tooltip = TooltipParser.Parse(
        [
            "CRUZ Dark", "Volume: 1000 pscu", "Contents Remaining: 100%", "HEI: 38", "NOR: 30",
            "Effects: Hypertrophic, Hypo-Metabolic", "Restore. refuel. and recharge with CRUZ Optimal",
        ]);

        Assert.NotNull(tooltip);
        Assert.Equal("CRUZ Dark", tooltip.Name);
        Assert.Equal("100%", tooltip.ContentsRemaining);
        Assert.Null(tooltip.Manufacturer);
        Assert.False(tooltip.RegisteredToYou);
    }

    [Fact]
    public void Freight_manager_junk_is_not_a_tooltip()
    {
        Assert.Null(TooltipParser.Parse(["MOVE TO ELEVATOR"]));
        Assert.Null(TooltipParser.Parse(["CAPACITY: BI/SOOO scu", "CLEAR", "FILTERS"]));
        Assert.Null(TooltipParser.Parse([]));
    }
}
