using System.Globalization;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;

namespace ItemTrackerPlugin.Tests;

/// <summary>
/// The screen reader against a real engine reading real frames. Needs OCRX_ENGINE_PATH pointed at
/// an Ocrx.Engine.exe and a Windows OCR language pack, like the other plugins' parity tests. The
/// tracker runs with its full region set from the first tick (<c>fixedSet</c>) because a replay
/// feeds frames faster than a changed set takes effect.
/// </summary>
[Collection("ReplayParity")]
public sealed class ReplayParityTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Kiosk_corpus_yields_the_manifests_observations()
    {
        var corpus = ScreenCorpus.Load("item-tracker-kiosk");
        AssertCorpusIsLabelledAndOrdered(corpus);

        var records = await Replay(corpus);

        foreach (var frame in corpus.Frames)
        {
            var seen = records.Where(r => r.Fields!["frameSeq"] == frame.Seq.ToString(CultureInfo.InvariantCulture)).ToList();
            if (frame.Observation is null)
            {
                Assert.True(seen.Count == 0, $"{frame.File}: expected nothing, read {seen.FirstOrDefault()?.RawText}");
                continue;
            }

            var record = Assert.Single(seen);
            AssertMatches(frame, record);
        }
    }

    /// <summary>
    /// The same kiosk page and the same tooltip at FOV 85, 100 and 116. The panels sit in the same
    /// place: every region reads the same item on the same tile, which is why the plugin carries no
    /// FOV zoom as SignaturePlugin does. Only the page counter differs, and that is the game's own
    /// content (the grid grew from six pages to seven between the first and second frame).
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Panels_do_not_move_with_the_field_of_view()
    {
        var corpus = ScreenCorpus.Load("item-tracker-fov");
        AssertCorpusIsLabelledAndOrdered(corpus);

        var records = await Replay(corpus);

        Assert.Equal(corpus.Frames.Count, records.Count);
        foreach (var frame in corpus.Frames)
            AssertMatches(frame, Assert.Single(records, r => r.Fields!["frameSeq"] == frame.Seq.ToString(CultureInfo.InvariantCulture)));

        Assert.Single(records.Select(r => r.Fields!["name"]).Distinct());
        Assert.Single(records.Select(r => (r.Fields!["row"], r.Fields["column"])).Distinct());
    }

    [Fact]
    public void Frame_file_names_give_their_capture_time()
    {
        Assert.Equal(new DateTime(2026, 10, 28, 15, 28, 7, 868), ScreenCorpus.TimeOf("frame_20261028_152807_868.png"));
        Assert.Throws<FormatException>(() => ScreenCorpus.TimeOf("Game_20261028.log"));
    }

    private static async Task<List<CaptureRecord>> Replay(ScreenCorpus corpus)
    {
        await using var plugin = new ItemTracker(fixedSet: true);
        var result = await ReplayHarness.RunAsync(new ReplayOptions
        {
            EnginePath = EngineLocator.Resolve(),
            CorpusDir = corpus.Directory,
            Plugin = plugin,
            Timeout = TimeSpan.FromMinutes(2),
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StreamEndReason.ReplayCompleted, result.Reason);
        return result.Records.Where(r => r.Kind == RecordKind.Observation).ToList();
    }

    private static void AssertMatches(ScreenCorpus.Frame frame, CaptureRecord record)
    {
        var want = frame.Observation!;
        var got = record.Fields!;
        var where = $"{frame.File} read {record.RawText}";

        Assert.True(want.Panel == got["panel"], $"panel: {where}");
        Assert.True(want.Name == got["name"], $"name: {where}");
        Assert.True(want.ItemType == Field(got, "itemType"), $"itemType: {where}");
        Assert.True(want.Row == Int(got, "row"), $"row: {where}");
        Assert.True(want.Column == Int(got, "column"), $"column: {where}");
        Assert.True(want.Quantity == Int(got, "quantity"), $"quantity: {where}");
        Assert.True(want.Page == Int(got, "page"), $"page: {where}");
        Assert.True(want.Pages == Int(got, "pages"), $"pages: {where}");
    }

    private static string? Field(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var v) ? v : null;

    private static int? Int(IReadOnlyDictionary<string, string> fields, string key) =>
        Field(fields, key) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : null;

    /// <summary>The engine replays in file-name order and the manifest is keyed by that order, so a
    /// frame added out of place or left unlabelled would shift every expectation after it.</summary>
    private static void AssertCorpusIsLabelledAndOrdered(ScreenCorpus corpus)
    {
        var onDisk = Directory.GetFiles(corpus.Directory, "*.png").Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(onDisk, corpus.Frames.Select(f => f.File).ToList());
        Assert.Equal(corpus.Frames.Select(f => f.CapturedAtLocal).Order().ToList(), corpus.Frames.Select(f => f.CapturedAtLocal).ToList());
    }
}
