using Xunit;

namespace SignaturePlugin.Tests;

public sealed class StarCitizenFovSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sc-fov-{Guid.NewGuid():N}");
    private readonly string _file;
    private DateTime _now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    public StarCitizenFovSourceTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "attributes.xml");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteFov(string value, DateTime writtenUtc)
    {
        File.WriteAllText(_file, $"""
            <Attributes Version="1">
             <Attr name="AutoZoomOnSelectedTargetStrength" value="1"/>
             <Attr name="FOV" value="{value}"/>
             <Attr name="Height" value="1440"/>
            </Attributes>
            """);
        File.SetLastWriteTimeUtc(_file, writtenUtc);
    }

    private StarCitizenFovSource Source(Func<string?>? locate = null)
        => new(locate ?? (() => _file), () => _now);

    [Fact]
    public void Reads_the_stored_vertical_fov()
    {
        WriteFov("67.6728", _now);
        var source = Source();

        Assert.True(source.Poll());
        Assert.Equal(67.6728, source.Current);
    }

    [Fact]
    public void A_rewrite_is_picked_up_on_the_next_poll()
    {
        WriteFov("54.5364", _now);
        var source = Source();
        source.Poll();

        WriteFov("83.9863", _now.AddSeconds(5));
        _now += StarCitizenFovSource.PollInterval;

        Assert.True(source.Poll());
        Assert.Equal(83.9863, source.Current);
    }

    [Fact]
    public void Polls_are_throttled()
    {
        WriteFov("54.5364", _now);
        var source = Source();
        source.Poll();

        WriteFov("83.9863", _now.AddSeconds(5));

        Assert.False(source.Poll());
        Assert.Equal(54.5364, source.Current);
    }

    /// <summary>An unrelated option rewrites the file with the same FOV: no change to report.</summary>
    [Fact]
    public void A_rewrite_with_the_same_fov_reports_no_change()
    {
        WriteFov("63.0883", _now);
        var source = Source();
        source.Poll();

        WriteFov("63.0883", _now.AddSeconds(5));
        _now += StarCitizenFovSource.PollInterval;

        Assert.False(source.Poll());
    }

    [Fact]
    public void A_torn_file_is_retried_rather_than_skipped()
    {
        File.WriteAllText(_file, "<Attributes><Attr name=\"FO");
        File.SetLastWriteTimeUtc(_file, _now);
        var source = Source();

        Assert.False(source.Poll());
        Assert.Null(source.Current);

        // Same write time as the torn read: only a retry, not a new write, can pick this up.
        WriteFov("77.5521", _now);
        _now += StarCitizenFovSource.PollInterval;

        Assert.True(source.Poll());
        Assert.Equal(77.5521, source.Current);
    }

    [Fact]
    public void No_game_means_no_fov()
    {
        var source = Source(() => null);

        Assert.False(source.Poll());
        Assert.Null(source.Current);
    }

    [Fact]
    public void The_file_is_found_from_the_game_executable()
        => Assert.Equal(
            Path.Combine(@"E:\Games\StarCitizen\LIVE", "user", "client", "0", "Profiles", "default", "attributes.xml"),
            StarCitizenInstall.AttributesFileFor(@"E:\Games\StarCitizen\LIVE\Bin64\StarCitizen.exe"));
}
