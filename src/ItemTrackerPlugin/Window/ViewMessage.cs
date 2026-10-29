using System.Text.Json.Nodes;
using ItemTrackerPlugin.App;
using ItemTrackerPlugin.ViewData;

namespace ItemTrackerPlugin.Window;

/// <summary>
/// What the window's page receives: the view document wrapped as <c>{ "type": "ocrx:view-data",
/// "data": ... }</c>, the shape <c>view/main.js</c> reads. It goes in-process to the WebView, so
/// there is no file and no size limit; every move kept per item is sent.
/// </summary>
internal static class ViewMessage
{
    internal const string Type = "ocrx:view-data";

    public static string From(TrackerState.Snapshot snapshot) => Wrap(ViewDocument.Build(snapshot));

    internal static string Wrap(JsonObject document) =>
        new JsonObject { ["type"] = Type, ["data"] = document }.ToJsonString();
}
