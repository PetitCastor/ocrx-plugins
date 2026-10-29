using System.Globalization;
using ItemTrackerPlugin.Model;
using ItemTrackerPlugin.Resolve;
using ItemTrackerPlugin.Screen;

namespace ItemTrackerPlugin.Reconcile;

/// <summary>
/// Gives every ledger placement one state with a reason, from what the kiosk showed.
/// <para>
/// The screen never shows a geid, so the match is per (station, kind of item): how many the ledger
/// had there at the time of the frame against how many the visit's tiles showed. Equal verifies every
/// one of them; different is a conflict on that kind at that station, never on a guessed geid.
/// Identical items are interchangeable to the player, which is why that is enough.
/// </para>
/// <para>
/// Only the kiosk is reconciled. The ship locker and the personal inventory are stored as
/// observations but have no ledger key to meet: a ship's header names the model, not the ship, and
/// the personal inventory is the player's body, whose ledger side is the worn sightings.
/// </para>
/// </summary>
public sealed class Reconciler(ItemNamer namer, TimeZoneInfo? zone = null)
{
    private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Local;

    public Reconciliation Run(ReconcileInput input)
    {
        var rows = input.Holdings
            .Select(h => new ReconcileRow(h, StationOf(h), namer.KeyOf(h.ItemClass)))
            .ToList();

        var (byVisit, unplaced) = PlaceAtVisits(input);
        var sightings = new Dictionary<(string Station, string Key), Sighting>();
        var coverage = new List<StationCoverage>();

        foreach (var (visit, seen) in byVisit.OrderBy(kv => kv.Key.OpenedAt))
        {
            var station = visit.LocationId!;
            var kinds = seen.GroupBy(o => ItemNamer.Key(o.Name)).Select(g => Sum(input, station, g.Key, g.ToList())).ToList();

            var covered = new StationCoverage(
                station,
                seen[^1].At,
                seen.Where(o => o.Page is not null).Select(o => o.Page).Distinct().Count(),
                seen.Max(o => o.Pages ?? 0),
                kinds.Sum(k => k.Observed),
                input.FreightTotals.TryGetValue(station, out var total) ? total : null);
            coverage.Add(covered);
            foreach (var s in kinds) Keep(sightings, s with { Whole = covered.Complete });
            if (!covered.Complete) continue;

            // A view of the whole station shows everything there: what the ledger has and no tile
            // showed is a sighting of none.
            foreach (var key in rows.Where(r => r.Station == station && r.Key is not null).Select(r => r.Key!).Distinct())
            {
                if (kinds.All(k => k.Key != key))
                    Keep(sightings, new Sighting(station, key, "", null, covered.At, null, 0, Exact: true, Whole: true));
            }
        }

        var ledgerAt = sightings.Values.ToDictionary(s => s, s => LedgerAt(input, rows, s));
        var holdings = rows.Select(r => Judge(input, r, sightings, ledgerAt)).ToList();
        var arrivals = sightings.Values
            .Where(s => s.Observed > 0 && ledgerAt[s] == 0)
            .Select(s => Arrival(input, s))
            .ToList();

        return new Reconciliation(holdings, arrivals, coverage, unplaced)
        {
            Pages = PageStatuses(sightings.Values, rows.Zip(holdings).ToList(), arrivals),
        };
    }

    /// <summary>
    /// The kinds each page showed, and how many of them are verified. A kind sits on the page of its
    /// last sighting, and is verified when every placement of it at the station is, or when the screen
    /// showed it with no ledger side at all.
    /// </summary>
    private static List<PageStatus> PageStatuses(
        IEnumerable<Sighting> sightings, List<(ReconcileRow Row, ReconciledHolding Held)> judged, List<UnknownArrival> arrivals) =>
        sightings
            .Where(s => s is { Observed: > 0, Page: not null })
            .GroupBy(s => (s.LocationId, Page: s.Page!.Value))
            .Select(g => new PageStatus(g.Key.LocationId, g.Key.Page, g.Count(), g.Count(s => KindVerified(s, judged, arrivals))))
            .OrderBy(p => p.LocationId, StringComparer.Ordinal).ThenBy(p => p.Page)
            .ToList();

    private static bool KindVerified(
        Sighting s, List<(ReconcileRow Row, ReconciledHolding Held)> judged, List<UnknownArrival> arrivals)
    {
        var held = judged.Where(j => j.Row.Station == s.LocationId && j.Row.Key == s.Key).Select(j => j.Held.State).ToList();
        var came = arrivals.Where(a => a.LocationId == s.LocationId && ItemNamer.Key(a.Name) == s.Key).Select(a => a.State).ToList();
        return held.Count + came.Count > 0 && held.Concat(came).All(state => state == PlacementState.Verified);
    }

    /// <summary>The station an item sits at directly; one inside a container there is not on the kiosk's grid.</summary>
    private static string? StationOf(ItemHolding h) =>
        h.Chain is [{ Kind: InventoryKind.Location } direct, ..] ? direct.Key : null;

    /// <summary>
    /// Kiosk observations by the station-open line before them. The kiosk header says <c>LOCAL</c>, not
    /// which station; the log says which one the player opened.
    /// </summary>
    private static (Dictionary<StationVisit, List<StoredObservation>> ByVisit, int Unplaced) PlaceAtVisits(ReconcileInput input)
    {
        var visits = input.Visits.OrderBy(v => v.OpenedAt).ToList();
        var byVisit = new Dictionary<StationVisit, List<StoredObservation>>();
        var unplaced = 0;

        foreach (var o in input.Observations.Where(o => o.Panel == ScreenContext.Kiosk).OrderBy(o => o.At))
        {
            // An open the log could not tie to an id still ends the visit before it.
            if (visits.LastOrDefault(v => v.OpenedAt <= o.At) is not { LocationId: not null } visit)
            {
                unplaced++;
                continue;
            }

            if (!byVisit.TryGetValue(visit, out var seen)) byVisit[visit] = seen = [];
            seen.Add(o);
        }

        return (byVisit, unplaced);
    }

    /// <summary>
    /// One kind of item in one visit: the sum over its distinct tiles, each at its last read badge. A
    /// frame whose hovered tile was not found is the same tile as one that was, when there is one. A
    /// move of that kind in or out of the station re-sorts the grid, so only frames after the last one
    /// count: a tile seen before it may since show something else, and its item another tile.
    /// </summary>
    private Sighting Sum(ReconcileInput input, string station, string key, List<StoredObservation> seen)
    {
        var lastAt = seen[^1].At;
        var resorted = input.Moves
            .Where(m => m.Timestamp < lastAt && !m.Failed && !m.Lost && Touches(m, station) && namer.KeyOf(m.ItemClass) == key)
            .Select(m => (DateTimeOffset?)m.Timestamp)
            .Max();
        if (resorted is { } cut) seen = seen.Where(o => o.At > cut).ToList();

        var tiles = seen.GroupBy(o => (o.Page, o.Row, o.Column)).ToList();
        if (tiles.Any(t => t.Key.Row is not null)) tiles = tiles.Where(t => t.Key.Row is not null).ToList();

        var badges = tiles.Select(t => t.LastOrDefault(o => o.Quantity is not null)?.Quantity).ToList();
        var last = seen[^1];
        return new Sighting(
            station, key, last.Name, seen.LastOrDefault(o => o.ItemClass is not null)?.ItemClass, last.At, last.Page,
            badges.Sum(b => b ?? 1), badges.All(b => b is not null), Whole: false);
    }

    private static bool Touches(MoveRecord m, string station) =>
        (m.SourceKind == InventoryKind.Location && m.SourceKey == station)
        || (m.TargetKind == InventoryKind.Location && m.TargetKey == station);

    private static void Keep(Dictionary<(string, string), Sighting> sightings, Sighting s)
    {
        if (!sightings.TryGetValue((s.LocationId, s.Key), out var held) || held.At <= s.At)
            sightings[(s.LocationId, s.Key)] = s;
    }

    /// <summary>
    /// How many of a kind the ledger had at a station at the time of a sighting: what is there now, plus
    /// what left after it, less what arrived after it. The log writes some stores on two lines, a
    /// request and an item-stored line, both naming the geid, so a named arrival counts once.
    /// </summary>
    private int LedgerAt(ReconcileInput input, List<ReconcileRow> rows, Sighting s)
    {
        var now = rows.Where(r => r.Station == s.LocationId && r.Key == s.Key).Sum(r => r.Holding.Quantity);

        var after = input.Moves
            .Where(m => m.Timestamp > s.At && !m.Failed && !m.Lost && namer.KeyOf(m.ItemClass) == s.Key)
            .ToList();

        var left = after
            .Where(m => m.SourceKind == InventoryKind.Location && m.SourceKey == s.LocationId)
            .Sum(m => m.Units);

        var arrived = after.Where(m => m.TargetKind == InventoryKind.Location && m.TargetKey == s.LocationId).ToList();
        var came = arrived.Where(m => m.ItemGeid is null).Sum(m => m.Units)
                   + arrived.Where(m => m.ItemGeid is not null).Select(m => m.ItemGeid).Distinct().Count();

        return Math.Max(now + left - came, 0);
    }

    private ReconciledHolding Judge(
        ReconcileInput input, ReconcileRow row, Dictionary<(string, string), Sighting> sightings, Dictionary<Sighting, int> ledgerAt)
    {
        var h = row.Holding;
        var evidence = h.LastMove;
        var state = PlacementState.Deduced;
        var reason = $"{Arrived(h.LastMoveType)} {Local(h.LastMove).ToString("d MMM", CultureInfo.InvariantCulture)}, not seen on screen since";

        // Any sighting of the kind dates a look at it, even one before the item's last move.
        var sighting = row is { Station: { } station, Key: { } key } && sightings.TryGetValue((station, key), out var found)
            ? found
            : null;

        if (sighting is { } s && s.At > h.LastMove)
        {
            var had = ledgerAt[s];
            if (s.Observed == had)
            {
                (state, reason, evidence) = (PlacementState.Verified, Seen(input, s), s.At);
            }
            else if (s.Observed > had || (s.Exact && s.Whole))
            {
                // More than the ledger had is a conflict on any view. Fewer is one only on a view of
                // the whole station with every badge read: a filter, an unvisited page, a tile not
                // hovered or an unread badge each hide some.
                (state, reason, evidence) = (PlacementState.Conflict, $"Ledger says {had}, kiosk showed {s.Observed}", s.At);
            }
        }

        if (input.Updates.LastOrDefault(u => u.At > evidence) is { Build: > 0 } update)
            (state, reason) = (PlacementState.Stale, $"Game updated to build {update.Build} since");

        return new ReconciledHolding(h, state, reason, sighting?.At);
    }

    private UnknownArrival Arrival(ReconcileInput input, Sighting s)
    {
        var stale = input.Updates.LastOrDefault(u => u.At > s.At);
        return new UnknownArrival(
            s.LocationId, s.Name, s.ItemClass, s.Observed, s.At, s.Page,
            stale.Build > 0 ? PlacementState.Stale : PlacementState.Verified,
            stale.Build > 0 ? $"Game updated to build {stale.Build} since" : $"{Seen(input, s)}, origin unknown");
    }

    private string Seen(ReconcileInput input, Sighting s) =>
        $"Seen at {input.PlaceLabel(s.LocationId)} kiosk, {Local(s.At).ToString("d MMM HH:mm", CultureInfo.InvariantCulture)}"
        + (s.Page is { } page ? $", page {page}" : "");

    private DateTimeOffset Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, _zone);

    private static string Arrived(string moveType) => moveType switch
    {
        "Store" or "StoreItem" => "Stored",
        "Move" or "Swap" => "Moved here",
        "Drop" => "Dropped",
        "Interaction" => "Equipped",
        "Carry" => "Carried",
        _ => "Placed",
    };
}
