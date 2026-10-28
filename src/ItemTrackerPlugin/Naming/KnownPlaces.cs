using System.Text.RegularExpressions;

namespace ItemTrackerPlugin.Naming;

/// <summary>
/// The places a local inventory's internal string names outright, and where they sit.
/// <para>
/// Three shapes turn up in the logs: a landing zone ("Stanton3_Area18", Area18 on the third
/// planet out from Stanton), a station on a planet's low orbit or one of its Lagrange points
/// ("RR_ARC_LEO", "RR_CRU_L4"), and a few one-offs ("Nyx_Levski"). Station names are not in
/// the string at all, so they come from this table, checked against starcitizen.tools.
/// Anything missing from it is left to the name votes, or shown as logged, rather than
/// guessed — which includes the jump-point gateways, whose internal strings are legacy
/// asset names ("RR_JP_NyxCastra" is Stanton Gateway).
/// </para>
/// </summary>
public static partial class KnownPlaces
{
    [GeneratedRegex(@"^(?<system>[A-Za-z]+)(?<orbit>[0-9]+)_(?<place>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex LandingZone();

    [GeneratedRegex(@"^R[RS]_(?<body>[A-Za-z]+[0-9]?)_(?<slot>LEO|L[1-5])$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Station();

    /// <summary>Planets by system and orbit, counted out from the star.</summary>
    private static readonly Dictionary<(string System, int Orbit), string> Planets = new()
    {
        [("Stanton", 1)] = "Hurston",
        [("Stanton", 2)] = "Crusader",
        [("Stanton", 3)] = "ArcCorp",
        [("Stanton", 4)] = "microTech",
        [("Pyro", 1)] = "Pyro I",
        [("Pyro", 2)] = "Monox",
        [("Pyro", 3)] = "Bloom",
        [("Pyro", 4)] = "Pyro IV",
        [("Pyro", 5)] = "Pyro V",
        [("Pyro", 6)] = "Terminus",
        [("Nyx", 1)] = "Nyx I",
        [("Nyx", 2)] = "Nyx II",
        [("Nyx", 3)] = "Nyx III",
    };

    /// <summary>How station strings abbreviate their planet: Stanton's by name, Pyro's by orbit.</summary>
    private static readonly Dictionary<string, (string System, int Orbit)> Bodies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HUR"] = ("Stanton", 1),
        ["CRU"] = ("Stanton", 2),
        ["ARC"] = ("Stanton", 3),
        ["MIC"] = ("Stanton", 4),
        ["P1"] = ("Pyro", 1),
        ["P2"] = ("Pyro", 2),
        ["P3"] = ("Pyro", 3),
        ["P4"] = ("Pyro", 4),
        ["P5"] = ("Pyro", 5),
        ["P6"] = ("Pyro", 6),
    };

    /// <summary>Stations by planet abbreviation and slot: its low orbit, or a Lagrange point.</summary>
    private static readonly Dictionary<string, string> Stations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HUR_LEO"] = "Everus Harbor",
        ["HUR_L1"] = "HUR-L1 Green Glade Station",
        ["HUR_L2"] = "HUR-L2 Faithful Dream Station",
        ["HUR_L3"] = "HUR-L3 Thundering Express Station",
        ["HUR_L4"] = "HUR-L4 Melodic Fields Station",
        ["HUR_L5"] = "HUR-L5 High Course Station",

        ["CRU_LEO"] = "Seraphim Station",
        ["CRU_L1"] = "CRU-L1 Ambitious Dream Station",
        ["CRU_L4"] = "CRU-L4 Shallow Fields Station",
        ["CRU_L5"] = "CRU-L5 Beautiful Glen Station",

        ["ARC_LEO"] = "Baijini Point",
        ["ARC_L1"] = "ARC-L1 Wide Forest Station",
        ["ARC_L2"] = "ARC-L2 Lively Pathway Station",
        ["ARC_L3"] = "ARC-L3 Modern Express Station",
        ["ARC_L4"] = "ARC-L4 Faint Glen Station",
        ["ARC_L5"] = "ARC-L5 Yellow Core Station",

        ["MIC_LEO"] = "Port Tressler",
        ["MIC_L1"] = "MIC-L1 Shallow Frontier Station",
        ["MIC_L2"] = "MIC-L2 Long Forest Station",
        ["MIC_L3"] = "MIC-L3 Endless Odyssey Station",
        ["MIC_L4"] = "MIC-L4 Red Crossroads Station",
        ["MIC_L5"] = "MIC-L5 Modern Icarus Station",

        ["P2_L4"] = "Checkmate Station",
        ["P3_LEO"] = "Orbituary",
        ["P3_L1"] = "Starlight Service Station",
        ["P3_L3"] = "Patch City",
        ["P5_L2"] = "Gaslight",
        ["P5_L4"] = "Rod's Fuel 'N Supplies",
        ["P5_L5"] = "Rat's Nest",
        ["P6_LEO"] = "Ruin Station",
        ["P6_L3"] = "Endgame",
        ["P6_L4"] = "Dudley & Daughters",
        ["P6_L5"] = "Megumi Refueling",
    };

    /// <summary>Internal strings that fit no pattern, and the body the place is on.</summary>
    private static readonly Dictionary<string, (string Place, string Planet)> OneOffs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nyx_Levski"] = ("Levski", "Delamar"),
    };

    /// <summary>
    /// The place an internal string names and where it sits: the planet for a landing zone
    /// or an orbital station, the planet's Lagrange point for a rest stop ("Crusader L4" —
    /// those orbit the star, not the planet). Null when the string names nothing on record.
    /// </summary>
    public static (string Place, string Planet)? FromRaw(string raw)
    {
        if (OneOffs.TryGetValue(raw, out var oneOff)) return oneOff;

        var zone = LandingZone().Match(raw);
        if (zone.Success
            && Systems.IsSystem(zone.Groups["system"].Value)
            && int.TryParse(zone.Groups["orbit"].Value, out var orbit)
            && Planets.TryGetValue((Systems.Canonical(zone.Groups["system"].Value), orbit), out var landedOn))
        {
            return (zone.Groups["place"].Value.Replace('_', ' '), landedOn);
        }

        var station = Station().Match(raw);
        if (station.Success
            && Bodies.TryGetValue(station.Groups["body"].Value, out var body)
            && Stations.TryGetValue($"{station.Groups["body"].Value}_{station.Groups["slot"].Value}", out var name)
            && Planets.TryGetValue(body, out var planet))
        {
            var slot = station.Groups["slot"].Value.ToUpperInvariant();
            return (name, slot == "LEO" ? planet : $"{planet} {slot}");
        }

        return null;
    }
}
