using System.Globalization;
using ItemTrackerPlugin.Overlay;
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

        var all = await ReplayAll(corpus);
        var records = Observations(all);

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

        AssertOverlayFollowsTheKiosk(all, corpus);
    }

    /// <summary>
    /// What the overlay was told while the corpus replayed: only status lines, each naming the kiosk or the ship locker, the page the frames showed in the line,
    /// and each a change from the line before.
    /// </summary>
    private static void AssertOverlayFollowsTheKiosk(List<CaptureRecord> all, ScreenCorpus corpus)
    {
        var lines = all.Where(StatusRecord.IsStatus).Select(r => r.Fields![StatusRecord.StatusField]).ToList();

        Assert.NotEmpty(lines);
        // With the dev machine's own logs the throwaway store may know the station; with none the line is just "Kiosk".
        Assert.All(lines, line => Assert.True(
            line.Contains("kiosk", StringComparison.OrdinalIgnoreCase) || line.Contains("locker", StringComparison.OrdinalIgnoreCase), line));
        for (var i = 1; i < lines.Count; i++) Assert.NotEqual(lines[i - 1], lines[i]);
        foreach (var page in corpus.Frames.Select(f => f.Observation).Where(o => o?.Page is not null && o.Panel == "Kiosk").Select(o => o!.Page!.Value).Distinct())
            Assert.Contains(lines, line => line.Contains($"p.{page}/", StringComparison.Ordinal));
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

        var records = Observations(await ReplayAll(corpus));

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

    /// <summary>The screen reader's records: the status lines for the overlay carry no frame sequence.</summary>
    private static List<CaptureRecord> Observations(List<CaptureRecord> all) =>
        all.Where(r => r.Kind == RecordKind.Observation && !StatusRecord.IsStatus(r)).ToList();

    private static async Task<List<CaptureRecord>> ReplayAll(ScreenCorpus corpus)
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
        return result.Records.ToList();
    }

    private static void AssertMatches(ScreenCorpus.Frame frame, CaptureRecord record)
    {
        var want = frame.Observation!;
        var got = record.Fields!;
        var where = $"{frame.File} read {record.RawText}";

        Assert.True(want.Panel == got["panel"], $"panel: {where}");
        Assert.True(SameName(want.Name, got["name"]), $"name: {where}");
        Assert.True(want.ItemType == Field(got, "itemType"), $"itemType: {where}");
        Assert.True(want.Row == Int(got, "row"), $"row: {where}");
        Assert.True(want.Column == Int(got, "column"), $"column: {where}");
        Assert.True(want.Quantity == Int(got, "quantity"), $"quantity: {where}");
        Assert.True(want.Page == Int(got, "page"), $"page: {where}");
        Assert.True(want.Pages == Int(got, "pages"), $"pages: {where}");
    }

    /// <summary>Windows OCR reads the roman numeral in "Odyssey II" as "II" or "11" depending on the
    /// language pack build, so I, l and 1 are one letter here.</summary>
    private static bool SameName(string? want, string? got) => Fold(want) == Fold(got);

    private static string? Fold(string? name) => name?.Replace('l', '1').Replace('I', '1');

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
