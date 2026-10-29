using System.Globalization;
using System.Text.Json;
using ItemTrackerPlugin.Data;
using ItemTrackerPlugin.Screen;
using Microsoft.Data.Sqlite;

namespace ItemTrackerPlugin.Reconcile;

/// <summary>
/// Reads and writes <c>screen_observation</c>. What the screen showed cannot be rebuilt from the
/// logs, so the table survives a rebuild of everything the logs give.
/// </summary>
public static class ObservationStore
{
    public static void Add(TrackerDb db, ScreenObservation o, DateTimeOffset at) =>
        Add(db, new StoredObservation(
            at, o.Panel, o.ShipModel, o.Tooltip.Name, o.ItemClass, o.Quantity, o.Page?.Page, o.Page?.Of,
            o.Tile?.Row, o.Tile?.Column, o.SourceRoi, JsonSerializer.Serialize(o)));

    public static void Add(TrackerDb db, StoredObservation o)
    {
        using var cn = db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO screen_observation(ts, panel, ship, name, item_class, quantity, page, pages, tile_row, tile_col, roi, raw)
            VALUES($ts, $panel, $ship, $name, $class, $qty, $page, $pages, $row, $col, $roi, $raw)
            """;
        cmd.Parameters.AddWithValue("$ts", o.At.UtcDateTime.ToString("O"));
        cmd.Parameters.AddWithValue("$panel", o.Panel.ToString());
        cmd.Parameters.AddWithValue("$ship", (object?)o.ShipModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", o.Name);
        cmd.Parameters.AddWithValue("$class", (object?)o.ItemClass ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$qty", (object?)o.Quantity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$page", (object?)o.Page ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pages", (object?)o.Pages ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$row", (object?)o.Row ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$col", (object?)o.Column ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$roi", o.SourceRoi);
        cmd.Parameters.AddWithValue("$raw", o.RawText);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Every observation, oldest first.</summary>
    public static List<StoredObservation> Load(SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT ts, panel, ship, name, item_class, quantity, page, pages, tile_row, tile_col, roi, raw
            FROM screen_observation ORDER BY ts, id
            """;

        var rows = new List<StoredObservation>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new StoredObservation(
                DateTimeOffset.Parse(r.GetString(0), CultureInfo.InvariantCulture),
                Enum.TryParse<ScreenContext>(r.GetString(1), out var panel) ? panel : ScreenContext.Unknown,
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                Int(r, 5), Int(r, 6), Int(r, 7), Int(r, 8), Int(r, 9),
                r.GetString(10),
                r.GetString(11)));
        }

        return rows;
    }

    private static int? Int(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
}
