using ItemTrackerPlugin.Naming;

namespace ItemTrackerPlugin.Tests;

public class KnownPlacesTests
{
    [Theory]
    [InlineData("Stanton1_Lorville", "Lorville", "Hurston")]
    [InlineData("Stanton2_Orison", "Orison", "Crusader")]
    [InlineData("Stanton3_Area18", "Area18", "ArcCorp")]
    [InlineData("Stanton4_New_Babbage", "New Babbage", "microTech")]
    [InlineData("Pyro5_Some_Outpost", "Some Outpost", "Pyro V")]
    public void Reads_a_landing_zone_and_the_planet_its_orbit_number_names(string raw, string place, string planet)
    {
        Assert.Equal<(string, string)?>((place, planet), KnownPlaces.FromRaw(raw));
    }

    [Theory]
    [InlineData("RR_HUR_LEO", "Everus Harbor", "Hurston")]
    [InlineData("RR_ARC_LEO", "Baijini Point", "ArcCorp")]
    [InlineData("RR_CRU_LEO", "Seraphim Station", "Crusader")]
    [InlineData("RR_P6_LEO", "Ruin Station", "Terminus")]
    public void Names_a_station_in_a_planets_low_orbit(string raw, string place, string planet)
    {
        Assert.Equal<(string, string)?>((place, planet), KnownPlaces.FromRaw(raw));
    }

    [Theory]
    // Rest stops orbit the star at a planet's Lagrange point, not the planet itself.
    [InlineData("RR_CRU_L4", "CRU-L4 Shallow Fields Station", "Crusader L4")]
    [InlineData("RS_HUR_L1", "HUR-L1 Green Glade Station", "Hurston L1")]
    [InlineData("RR_P5_L2", "Gaslight", "Pyro V L2")]
    public void Names_a_rest_stop_at_a_planets_lagrange_point(string raw, string place, string planet)
    {
        Assert.Equal<(string, string)?>((place, planet), KnownPlaces.FromRaw(raw));
    }

    [Fact]
    public void Names_a_one_off_on_its_body()
    {
        Assert.Equal<(string, string)?>(("Levski", "Delamar"), KnownPlaces.FromRaw("Nyx_Levski"));
    }

    [Theory]
    [InlineData("Stanton_Kaboos")]                            // no orbit
    [InlineData("Stanton9_Nowhere")]                          // an orbit with no planet on record
    [InlineData("Sml3_Outpost")]                              // not a system
    [InlineData("RR_CRU_L2")]                                 // a Lagrange point with no station
    [InlineData("RR_JP_NyxCastra")]                           // a gateway: its string is a legacy name
    [InlineData("AsteroidClusterBase_Nyx_Social_Keeger_002")] // not on record
    public void Leaves_anything_not_on_record_to_the_name_votes(string raw)
    {
        Assert.Null(KnownPlaces.FromRaw(raw));
    }
}
