using System.Text.Json;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// A spawned engine owns a named pipe and a Windows OCR instance, so two of these must never run
/// at once. This is what actually serializes them: the <c>[Collection]</c> attribute on
/// <see cref="ReplayParityTests"/> alone only groups; without <c>DisableParallelization</c> the
/// group still runs beside every other collection in the assembly. Keep this even with only one
/// test below: it is the thing that stops the second test you add here from racing the first one
/// for the same pipe.
/// </summary>
[Collection("ReplayParity")]
public class ReplayParityTests
{
    private const string Corpus = "Fixtures/Replay/scan-signature";
    private const string Manifest = "manifest.json";

    /// <summary>
    /// Parity smoke test: spawns a real Ocrx.Engine.exe replaying a PNG corpus and drives
    /// this plugin through its real OcrxPluginHost path. Needs OCRX_ENGINE_PATH
    /// pointed at a built or unpacked Ocrx.Engine.exe and a Windows OCR language pack.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ScanSignature_corpus_matches_manifest_records()
    {
        var corpusDir = ReplayCorpus.Resolve(Corpus);
        Assert.True(Directory.Exists(corpusDir), $"corpus not copied to the test output: {corpusDir}");

        var expectedFrames = ReadManifest(Path.Combine(corpusDir, Manifest));
        Assert.NotEmpty(expectedFrames);

        AssertManifestLabelsEveryPng(corpusDir, expectedFrames);

        foreach (var expected in expectedFrames)
        {
            var oneFrameCorpus = CreateOneFrameCorpus(corpusDir, expected.File);
            try
            {
                if (expected.Name is null)
                {
                    await AssertCounterReadsSignature(oneFrameCorpus, expected.File, expected.Signature!.Value);
                    continue;
                }

                var result = await ReplayHarness.RunAsync(new ReplayOptions
                {
                    EnginePath = EngineLocator.Resolve(),
                    CorpusDir = oneFrameCorpus,
                    Plugin = new SignaturePlugin(SignatureTable.LoadEmbedded()),
                });

                Assert.Equal(0, result.ExitCode);
                Assert.Equal(StreamEndReason.ReplayCompleted, result.Reason);

                var record = Assert.Single(result.Records);
                Assert.Equal("SignaturePlugin", record.Plugin);
                Assert.Equal(TriggerKind.Auto, record.Trigger);
                Assert.Equal(RecordKind.Observation, record.Kind);

                using var json = JsonDocument.Parse(record.RawText);
                Assert.Equal(expected.Name, json.RootElement.GetProperty("name").GetString());
                Assert.Equal(expected.Kind, json.RootElement.GetProperty("kind").GetString());

                // Optional: name/kind alone let a misread that lands on a different cluster count
                // of the same ore (e.g. a doubled signature) pass unnoticed, since TryMatch
                // searches unit signature x count 1-6. Frames whose manifest entry pins the exact
                // reading close that gap; older or hand-labelled entries that only know the ore
                // name still work without them.
                if (expected.Signature is { } signature)
                    Assert.Equal(signature, json.RootElement.GetProperty("signature").GetDouble());
                if (expected.Count is { } count)
                    Assert.Equal(count, json.RootElement.GetProperty("count").GetInt32());
            }
            finally
            {
                Directory.Delete(oneFrameCorpus, recursive: true);
            }
        }
    }

    /// <summary>
    /// A frame whose value names no ore — the 2026-09-28 resolution captures read 2,000 — cannot
    /// produce a plugin observation, so it pins the step before matching instead: the counter ROI,
    /// placed by the engine for this frame's size and the game's scale mode, has to OCR to text the
    /// plugin's own parser reads as the manifest's signature. Needs the published games.json
    /// (star-citizen, scaleMode "height") for the frames taller than 16:9.
    /// </summary>
    private static async Task AssertCounterReadsSignature(string corpus, string file, double signature)
    {
        var result = await ReplayHarness.RunAsync(new ReplayOptions
        {
            EnginePath = EngineLocator.Resolve(),
            CorpusDir = corpus,
            Plugin = new CounterReadingProbe(),
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StreamEndReason.ReplayCompleted, result.Reason);

        var raw = Assert.Single(result.Records).RawText;
        Assert.True(SignatureParser.TryParse(raw, out var read), $"{file}: counter OCR '{raw}' is not a number");
        Assert.True(signature == read, $"{file}: counter OCR '{raw}' parsed as {read}, expected {signature}");
    }

    private static IReadOnlyList<(string File, string? Name, string? Kind, double? Signature, int? Count)> ReadManifest(string path)
    {
        Assert.True(File.Exists(path), $"manifest not copied to the test output: {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(
            document.RootElement.TryGetProperty("frames", out var frames) &&
            frames.ValueKind == JsonValueKind.Array,
            "manifest must contain a 'frames' array");

        var expected = new List<(string File, string? Name, string? Kind, double? Signature, int? Count)>();
        foreach (var frame in frames.EnumerateArray())
        {
            var file = frame.GetProperty("file").GetString() ?? string.Empty;
            var name = frame.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var kind = frame.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : null;
            double? signature = frame.TryGetProperty("signature", out var signatureElement)
                ? signatureElement.GetDouble() : null;
            int? count = frame.TryGetProperty("count", out var countElement)
                ? countElement.GetInt32() : null;

            Assert.False(string.IsNullOrWhiteSpace(file), "manifest frame file is required");
            // Either an ore observation (name and kind) or a bare reading (signature alone).
            if (name is null && kind is null)
            {
                Assert.True(signature is not null, $"{file}: a frame without name/kind needs a signature");
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(name), $"{file}: manifest frame name is required with kind");
                Assert.False(string.IsNullOrWhiteSpace(kind), $"{file}: manifest frame kind is required with name");
            }

            expected.Add((file, name, kind, signature, count));
        }

        return expected;
    }

    private static void AssertManifestLabelsEveryPng(
        string corpusDir,
        IReadOnlyList<(string File, string? Name, string? Kind, double? Signature, int? Count)> frames)
    {
        var manifestFiles = frames
            .Select(f => NormalizeManifestPath(f.File))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(frames.Count, manifestFiles.Count);

        var pngFiles = Directory.EnumerateFiles(corpusDir, "*.png", SearchOption.AllDirectories)
            .Select(path => NormalizeManifestPath(Path.GetRelativePath(corpusDir, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            manifestFiles.OrderBy(file => file, StringComparer.OrdinalIgnoreCase),
            pngFiles.OrderBy(file => file, StringComparer.OrdinalIgnoreCase));
    }

    private static string CreateOneFrameCorpus(string sourceCorpusDir, string manifestFile)
    {
        var source = Path.Combine(sourceCorpusDir, manifestFile);
        var tempCorpus = Path.Combine(Path.GetTempPath(), $"signature-parity-{Guid.NewGuid():N}");
        var target = Path.Combine(tempCorpus, Path.GetFileName(manifestFile));

        Directory.CreateDirectory(tempCorpus);
        File.Copy(source, target);
        return tempCorpus;
    }

    private static string NormalizeManifestPath(string path) => path.Replace('\\', '/');
}
