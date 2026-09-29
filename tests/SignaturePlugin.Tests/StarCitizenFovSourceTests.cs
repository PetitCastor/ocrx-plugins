using Xunit;

namespace SignaturePlugin.Tests;

public sealed class StarCitizenFovSourceTests : IDisposable
{
    private static readonly DateTime Written = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sc-fov-{Guid.NewGuid():N}");
    private readonly string _file;
    private TimeSpan _now = TimeSpan.FromHours(1);

    public StarCitizenFovSourceTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "attributes.xml");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteFov(string value, DateTime writtenUtc, string? file = null)
        => WriteRaw($"""
            <Attributes Version="1">
             <Attr name="AutoZoomOnSelectedTargetStrength" value="1"/>
             <Attr name="FOV" value="{value}"/>
             <Attr name="Height" value="1440"/>
            </Attributes>
            """, writtenUtc, file);

    private void WriteRaw(string content, DateTime writtenUtc, string? file = null)
    {
        File.WriteAllText(file ?? _file, content);
        File.SetLastWriteTimeUtc(file ?? _file, writtenUtc);
    }

    private StarCitizenFovSource Source(Func<IReadOnlyList<string>>? locate = null)
        => new(locate ?? (() => [_file]), () => _now);

    private void NextPoll() => _now += StarCitizenFovSource.PollInterval;

    [Fact]
    public void Reads_the_stored_vertical_fov()
    {
        WriteFov("67.6728", Written);
        var source = Source();

        Assert.True(source.Poll());
        Assert.Equal(67.6728, source.Current);
    }

    [Fact]
    public void A_rewrite_is_picked_up_on_the_next_poll()
    {
        WriteFov("54.5364", Written);
        var source = Source();
        source.Poll();

        WriteFov("83.9863", Written.AddSeconds(5));
        NextPoll();

        Assert.True(source.Poll());
        Assert.Equal(83.9863, source.Current);
    }

    [Fact]
    public void Polls_are_throttled()
    {
        WriteFov("54.5364", Written);
        var source = Source();
        source.Poll();

        WriteFov("83.9863", Written.AddSeconds(5));

        Assert.False(source.Poll());
        Assert.Equal(54.5364, source.Current);
    }

    /// <summary>An unrelated option rewrites the file with the same FOV: no change to report.</summary>
    [Fact]
    public void A_rewrite_with_the_same_fov_reports_no_change()
    {
        WriteFov("63.0883", Written);
        var source = Source();
        source.Poll();

        WriteFov("63.0883", Written.AddSeconds(5));
        NextPoll();

        Assert.False(source.Poll());
    }

    [Fact]
    public void A_torn_file_is_retried_rather_than_skipped()
    {
        WriteRaw("<Attributes><Attr name=\"FOV\" value=\"77.5521\"/><Attr na", Written);
        var source = Source();

        Assert.False(source.Poll());
        Assert.Null(source.Current);

        // Same write time as the torn read: only a retry, not a new write, can pick this up.
        WriteFov("77.5521", Written);
        NextPoll();

        Assert.True(source.Poll());
        Assert.Equal(77.5521, source.Current);
    }

    /// <summary>A complete file without FOV means "no FOV", which zooms nothing — not a stale
    /// value kept from before.</summary>
    [Fact]
    public void A_complete_file_without_fov_clears_the_value()
    {
        WriteFov("83.9863", Written);
        var source = Source();
        source.Poll();

        WriteRaw("<Attributes><Attr name=\"Height\" value=\"1440\"/></Attributes>", Written.AddSeconds(5));
        NextPoll();

        Assert.True(source.Poll());
        Assert.Null(source.Current);
    }

    [Fact]
    public void No_game_means_no_fov()
    {
        var source = Source(() => []);

        Assert.False(source.Poll());
        Assert.Null(source.Current);
    }

    /// <summary>Process enumeration is the expensive part: once per locate interval, found or not.</summary>
    [Fact]
    public void The_game_is_looked_for_once_per_locate_interval()
    {
        var locates = 0;
        var source = Source(() => { locates++; return []; });

        for (var i = 0; i < 29; i++)
        {
            source.Poll();
            NextPoll();
        }
        Assert.Equal(1, locates);

        _now += StarCitizenFovSource.LocateInterval;
        source.Poll();
        Assert.Equal(2, locates);
    }

    [Fact]
    public void A_failed_lookup_keeps_the_file_already_followed()
    {
        WriteFov("67.6728", Written);
        var fail = false;
        var source = Source(() => fail ? throw new InvalidOperationException("process exited") : [_file]);
        source.Poll();

        fail = true;
        WriteFov("77.5521", Written.AddSeconds(5));
        _now += StarCitizenFovSource.LocateInterval;

        Assert.True(source.Poll());
        Assert.Equal(77.5521, source.Current);
    }

    /// <summary>LIVE and PTU running together must not make the source flip between them.</summary>
    [Fact]
    public void The_followed_channel_is_kept_while_its_game_still_runs()
    {
        var other = Path.Combine(_dir, "ptu-attributes.xml");
        WriteFov("67.6728", Written);
        WriteFov("83.9863", Written, other);
        IReadOnlyList<string> running = [_file];
        var source = Source(() => running);
        source.Poll();

        running = [other, _file];
        _now += StarCitizenFovSource.LocateInterval;

        Assert.False(source.Poll());
        Assert.Equal(67.6728, source.Current);
    }

    [Fact]
    public void The_file_is_found_from_the_game_executable()
        => Assert.Equal(
            Path.Combine(@"E:\Games\StarCitizen\LIVE", "user", "client", "0", "Profiles", "default", "attributes.xml"),
            StarCitizenInstall.AttributesFileFor(@"E:\Games\StarCitizen\LIVE\Bin64\StarCitizen.exe"));
}
