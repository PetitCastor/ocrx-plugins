using System.Text.Json.Nodes;
using ItemTrackerPlugin.App;
using ItemTrackerPlugin.ViewData;
using ItemTrackerPlugin.Window;

namespace ItemTrackerPlugin.Tests;

public sealed class ViewMessageTests
{
    [Fact]
    public void The_window_gets_the_view_document_wrapped_the_way_the_page_reads_it()
    {
        var message = JsonNode.Parse(ViewMessage.From(TrackerState.Snapshot.Empty))!.AsObject();

        Assert.Equal(["data", "type"], message.Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Equal("ocrx:view-data", message["type"]!.GetValue<string>());
        Assert.Equal(ViewDocument.Schema, message["data"]!["schema"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(ViewDocument.Build(TrackerState.Snapshot.Empty), message["data"]));
    }

    /// <summary>view/main.js matches the same string; a rename on one side only would leave the page blank.</summary>
    [Fact]
    public void The_page_listens_for_the_type_the_window_sends()
    {
        var main = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "view", "main.js"));

        Assert.Contains($"\"{ViewMessage.Type}\"", main, StringComparison.Ordinal);
        Assert.Contains("chrome?.webview?.addEventListener(\"message\"", main, StringComparison.Ordinal);
    }

}
