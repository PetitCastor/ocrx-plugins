using System.Globalization;
using ItemTrackerPlugin.Data;
using ItemTrackerPlugin.Resolve;

namespace ItemTrackerPlugin.Reconcile;

/// <summary>Reads what <see cref="Reconciler"/> needs from the store, beside a resolver already loaded from it.</summary>
public static class ReconcileSources
{
    public static ReconcileInput Load(TrackerDb db, HoldingResolver resolver, IReadOnlyList<ItemHolding> holdings)
    {
        using var cn = db.Open();
        using var transaction = cn.BeginTransaction(deferred: true);

        var observations = ObservationStore.Load(cn);

        var visits = new List<StationVisit>();
        using (var cmd = cn.CreateCommand())
        {
            // A null ref is an open the log could not tie to an id; it places nothing, but it still
            // ends the visit before it.
            cmd.CommandText = "SELECT ref, ts FROM log_signal WHERE kind = 'opened' ORDER BY ts";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                visits.Add(new StationVisit(
                    r.IsDBNull(0) ? null : r.GetString(0), DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture)));
            }
        }

        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT location_id, total FROM freight_total WHERE total IS NOT NULL";
            using var r = cmd.ExecuteReader();
            while (r.Read()) totals[r.GetString(0)] = r.GetInt32(1);
        }

        transaction.Commit();
        return new ReconcileInput(
            holdings, resolver.Moves, resolver.Updates, observations, visits, totals, resolver.PlaceLabel);
    }
}
