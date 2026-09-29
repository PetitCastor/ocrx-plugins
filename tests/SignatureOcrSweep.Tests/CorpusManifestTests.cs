using Xunit;

namespace SignatureOcrSweep.Tests;

public class CorpusManifestTests
{
    [Fact]
    public void Reads_the_parity_corpus_shape_and_the_optional_additions()
    {
        var frames = CorpusManifest.Parse("""
            { "frames": [
              { "file": "a.png", "name": "Lindinium", "kind": "ore", "signature": 3400, "count": 1 },
              { "file": "b.png", "signature": 17020, "configuration": "borderless-2560x1440", "verticalFov": 67.6728, "lossy": true, "note": "x" }
            ] }
            """);

        Assert.Equal(3400, frames[0].Signature);
        Assert.Equal("unlabelled", frames[0].Configuration);
        Assert.Null(frames[0].VerticalFov);
        Assert.False(frames[0].Lossy);
        Assert.Equal(67.6728, frames[1].VerticalFov);
        Assert.Equal("borderless-2560x1440 @vfov67.67 [lossy]", frames[1].ConfigurationLabel);
    }

    [Fact]
    public void A_manifest_without_frames_is_rejected() =>
        Assert.Throws<FormatException>(() => CorpusManifest.Parse("{}"));

    [Fact]
    public void A_duplicate_file_is_a_clear_error()
    {
        var ex = Assert.Throws<FormatException>(() => CorpusManifest.Parse(
            """{ "frames": [ { "file": "a.png", "signature": 1 }, { "file": "A.png", "signature": 2 } ] }"""));

        Assert.Contains("more than once", ex.Message);
    }
}
