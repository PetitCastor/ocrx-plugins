using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Screen;

namespace ItemTrackerPlugin.Tests;

public sealed class ItemClassResolverTests
{
    private static readonly ItemClassResolver Resolver = new(GameLocalization.Parse(
    [
        "item_NameBEHR_LaserCannon_S2=M4A Cannon Defense Division",
        "item_NameARCL_Pistol_Glimmer=Arclight \"Glimmer\" Pistol",
        "item_NameKLWE_LaserRepeater_S1=CF-227 Badger Repeater",
        "item_NameKLWE_LaserRepeater_S2=CF-227 Badger Repeater",
        "item_NameTWIN_A=Twin",
        "item_NameTWIN_B=Twin",
        "Frontend_Unrelated=Nope",
    ]));

    private static TooltipReading Tooltip(string name, int? size = null) =>
        new(name, 1, null, null, null, size, null, null, false);

    [Fact]
    public void Unique_name_gives_its_class() =>
        Assert.Equal("BEHR_LaserCannon_S2", Resolver.Resolve(Tooltip("M4A Cannon Defense Division")));

    [Fact]
    public void Case_spacing_and_curly_quotes_do_not_matter() =>
        Assert.Equal("ARCL_Pistol_Glimmer", Resolver.Resolve(Tooltip("arclight  “Glimmer” pistol")));

    [Fact]
    public void Size_breaks_a_tie() =>
        Assert.Equal("KLWE_LaserRepeater_S2", Resolver.Resolve(Tooltip("CF-227 Badger Repeater", size: 2)));

    [Fact]
    public void Ambiguous_name_resolves_to_nothing()
    {
        Assert.Null(Resolver.Resolve(Tooltip("Twin")));
        Assert.Null(Resolver.Resolve(Tooltip("CF-227 Badger Repeater")));
    }

    [Fact]
    public void Unknown_name_resolves_to_nothing() => Assert.Null(Resolver.Resolve(Tooltip("Not in the game")));
}
