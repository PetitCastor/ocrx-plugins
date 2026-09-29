using System.Globalization;
using System.Xml;

namespace SignaturePlugin;

/// <summary>
/// Follows the vertical FOV Star Citizen stores in its profile's attributes.xml.
/// </summary>
/// <remarks>
/// The game rewrites the file the moment a changed option is applied, while it keeps running, so
/// watching its write time is enough to follow the user's FOV slider live. The stored value is the
/// VERTICAL FOV in degrees (slider 90 stores 58.7155 at 16:9), which is what
/// <see cref="StarCitizenFovZoom"/> takes. Polled from the tick loop rather than a file watcher: the
/// ROI update then happens on the plugin's own sequential thread, with nothing to lock. Deadlines run
/// on a monotonic clock so a wall-clock step backwards cannot stall polling.
/// </remarks>
internal sealed class StarCitizenFovSource
{
    /// <summary>How often the file's write time is checked.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>How often the game is looked for — process enumeration is the expensive part. Also
    /// bounds how long a switch to another channel (LIVE to PTU) keeps reading the old one's file.</summary>
    public static readonly TimeSpan LocateInterval = TimeSpan.FromSeconds(30);

    private readonly Func<IReadOnlyList<string>> _locate;
    private readonly Func<TimeSpan> _monotonicNow;
    private TimeSpan? _nextPoll;
    private TimeSpan? _nextLocate;
    private string? _path;
    private DateTime _lastWriteUtc;

    public StarCitizenFovSource()
        : this(StarCitizenInstall.FindAttributesFiles, () => TimeSpan.FromMilliseconds(Environment.TickCount64))
    {
    }

    internal StarCitizenFovSource(Func<IReadOnlyList<string>> locate, Func<TimeSpan> monotonicNow)
    {
        _locate = locate;
        _monotonicNow = monotonicNow;
    }

    /// <summary>The last FOV read, or null before the file has been found and read once, or when the
    /// file holds no FOV at all.</summary>
    public double? Current { get; private set; }

    /// <summary>
    /// Re-reads the file when its write time moved, at most once per <see cref="PollInterval"/>.
    /// True when <see cref="Current"/> changed.
    /// </summary>
    public bool Poll()
    {
        var now = _monotonicNow();
        if (now < _nextPoll)
            return false;
        _nextPoll = now + PollInterval;

        if (!(now < _nextLocate))
        {
            _nextLocate = now + LocateInterval;
            Relocate();
        }

        if (_path is null)
            return false;

        // A missing file reads as 1601-01-01 rather than throwing, and TryReadFov then fails it.
        var written = File.GetLastWriteTimeUtc(_path);
        if (written == _lastWriteUtc)
            return false;

        // A file caught mid-rewrite fails to parse; leaving _lastWriteUtc alone retries it next poll.
        if (!TryReadFov(_path, out var fov))
            return false;

        _lastWriteUtc = written;
        if (fov == Current)
            return false;

        Current = fov;
        return true;
    }

    /// <summary>
    /// Keeps the current file while the game still runs from it, so two running channels (LIVE and
    /// PTU) cannot make the source flip between them on every locate; otherwise takes the first.
    /// </summary>
    private void Relocate()
    {
        IReadOnlyList<string> candidates;
        try
        {
            candidates = _locate();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
            or IOException or UnauthorizedAccessException)
        {
            // Process enumeration is best-effort: a failure keeps whatever file was already followed.
            return;
        }

        if (_path is not null && candidates.Contains(_path, StringComparer.OrdinalIgnoreCase))
            return;

        var located = candidates.Count > 0 ? candidates[0] : null;
        if (located is null)
            return;

        _path = located;
        _lastWriteUtc = default;
    }

    /// <summary>
    /// Reads the <c>FOV</c> attribute. False when the file is missing, locked or torn — worth a
    /// retry. True with a null <paramref name="fov"/> when the file parsed completely but holds no
    /// usable FOV — an answer, not a failure.
    /// </summary>
    internal static bool TryReadFov(string path, out double? fov)
    {
        fov = null;
        try
        {
            // ReadWrite|Delete sharing: the game may be rewriting the file right now.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = XmlReader.Create(stream);
            while (reader.Read())
            {
                if (fov is null
                    && reader.NodeType == XmlNodeType.Element
                    && reader.Name == "Attr"
                    && reader.GetAttribute("name") == "FOV"
                    && double.TryParse(reader.GetAttribute("value"), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var value))
                {
                    fov = value;
                }
            }

            // Read to the end on purpose: a torn file with a complete FOV element still throws here
            // rather than being taken at face value.
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            fov = null;
            return false;
        }
    }
}
