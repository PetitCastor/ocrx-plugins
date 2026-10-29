using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Reconcile;

namespace ItemTrackerPlugin.Tests;

public sealed class ItemNamerTests
{
    /// <summary>Keys as the 4.x <c>global.ini</c> writes them.</summary>
    private static readonly ItemNamer Namer = new(GameLocalization.Parse(
    [
        "item_Name_rsi_odyssey_undersuit_helmet_01=Odyssey II Racing Helmet Alpha",
        "item_Name_thp_light_arms_01_01_01=Aztalan Arms",
        "item_Nameklwe_pistol_energy_01_chromic01=Arclight \"Glimmer\" Pistol",
    ]), c => c == "wiki_only_01" ? "From The Wiki" : null);

    [Theory]
    [InlineData("rsi_odyssey_undersuit_helmet_01_01_01", "Odyssey II Racing Helmet Alpha")]
    [InlineData("thp_light_arms_01_01_01", "Aztalan Arms")]
    [InlineData("klwe_pistol_energy_01_chromic01", "Arclight \"Glimmer\" Pistol")]
    [InlineData("wiki_only_01", "From The Wiki")]
    [InlineData("klwe_pistol_energy_01_mag", null)]
    public void A_class_is_named_by_the_game_text_then_the_wiki(string itemClass, string? name) =>
        Assert.Equal(name, Namer.Name(itemClass));

    [Fact]
    public void Ocr_swaps_of_I_l_and_1_meet_the_same_key()
    {
        Assert.Equal(ItemNamer.Key("Odyssey II Racing Helmet Alpha"), ItemNamer.Key("Odyssey 11 Racing  Helmet alpha"));
        Assert.Equal(Namer.KeyOf("rsi_odyssey_undersuit_helmet_01_01_01"), ItemNamer.Key("Odyssey 11 Racing Helmet Alpha"));
        Assert.Null(Namer.KeyOf("unknown_class"));
    }
}
