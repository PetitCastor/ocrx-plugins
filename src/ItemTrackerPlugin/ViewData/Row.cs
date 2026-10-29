using System.Text.Json.Nodes;

namespace ItemTrackerPlugin.ViewData;

/// <summary>One item of the view with the place it is grouped under.</summary>
/// <param name="PlaceId">A location id, or a ship class.</param>
internal sealed record Row(RowKind Kind, string PlaceId, string PlaceLabel, JsonObject Json);
