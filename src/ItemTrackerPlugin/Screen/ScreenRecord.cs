using System.Globalization;
using System.Text.Json;
using Ocrx.Sdk;

namespace ItemTrackerPlugin.Screen;

/// <summary>The <see cref="CaptureRecord"/> an observation travels as: JSON in the raw text for a
/// sink that wants all of it, and flat fields for one that wants columns.</summary>
public static class ScreenRecord
{
    public static CaptureRecord From(ScreenObservation o, DateTime timestamp, ulong frameSeq, string plugin)
    {
        var fields = new Dictionary<string, string>
        {
            ["panel"] = o.Panel.ToString(),
            ["name"] = o.Tooltip.Name,
            ["source"] = o.SourceRoi,
            ["frameSeq"] = frameSeq.ToString(CultureInfo.InvariantCulture),
        };
        Set(fields, "ship", o.ShipModel);
        Set(fields, "class", o.ItemClass);
        Set(fields, "itemType", o.Tooltip.ItemType);
        Set(fields, "quantity", o.Quantity?.ToString(CultureInfo.InvariantCulture));
        if (o.Tile is { } tile)
        {
            fields["row"] = tile.Row.ToString(CultureInfo.InvariantCulture);
            fields["column"] = tile.Column.ToString(CultureInfo.InvariantCulture);
        }

        if (o.Page is { } page)
        {
            fields["page"] = page.Page.ToString(CultureInfo.InvariantCulture);
            fields["pages"] = page.Of.ToString(CultureInfo.InvariantCulture);
        }

        return new CaptureRecord(timestamp, plugin, TriggerKind.Auto, JsonSerializer.Serialize(o))
        {
            Kind = RecordKind.Observation,
            Fields = fields,
        };
    }

    private static void Set(Dictionary<string, string> fields, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value)) fields[key] = value;
    }
}
