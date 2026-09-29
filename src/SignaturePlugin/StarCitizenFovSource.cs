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
/// ROI update then happens on the plugin's own sequential thread, with nothing to lock.
/// </remarks>
internal sealed class StarCitizenFovSource
{
    /// <summary>How often the file's write time is checked.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>How often the game is looked for. Also bounds how long a switch to another channel
    /// (LIVE to PTU) keeps reading the old one's file.</summary>
    public static readonly TimeSpan LocateInterval = TimeSpan.FromSeconds(30);

    private readonly Func<string?> _locate;
    private readonly Func<DateTime> _utcNow;
    private DateTime _nextPoll = DateTime.MinValue;
    private DateTime _nextLocate = DateTime.MinValue;
    private string? _path;
    private DateTime _lastWriteUtc;

    public StarCitizenFovSource()
        : this(StarCitizenInstall.FindAttributesFile, () => DateTime.UtcNow)
    {
    }

    internal StarCitizenFovSource(Func<string?> locate, Func<DateTime> utcNow)
    {
        _locate = locate;
        _utcNow = utcNow;
    }

    /// <summary>The last FOV read, or null before the file has been found and read once.</summary>
    public double? Current { get; private set; }

    /// <summary>
    /// Re-reads the file when its write time moved, at most once per <see cref="PollInterval"/>.
    /// True when <see cref="Current"/> changed.
    /// </summary>
    public bool Poll()
    {
        var now = _utcNow();
        if (now < _nextPoll)
            return false;
        _nextPoll = now + PollInterval;

        if (_path is null || now >= _nextLocate)
        {
            _nextLocate = now + LocateInterval;
            var located = _locate();
            if (located is not null && !string.Equals(located, _path, StringComparison.OrdinalIgnoreCase))
            {
                _path = located;
                _lastWriteUtc = default;
            }
        }

        if (_path is null)
            return false;

        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (written == _lastWriteUtc)
            return false;

        // A file caught mid-rewrite fails to parse; leaving _lastWriteUtc alone retries it next poll.
        if (ReadFov(_path) is not { } fov)
            return false;

        _lastWriteUtc = written;
        if (fov == Current)
            return false;

        Current = fov;
        return true;
    }

    /// <summary>The <c>FOV</c> attribute's value, or null when the file is missing, locked, torn
    /// or has no parsable FOV.</summary>
    internal static double? ReadFov(string path)
    {
        try
        {
            // ReadWrite|Delete sharing: the game may be rewriting the file right now.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = XmlReader.Create(stream);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element
                    && reader.Name == "Attr"
                    && reader.GetAttribute("name") == "FOV")
                {
                    return double.TryParse(reader.GetAttribute("value"), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var value) ? value : null;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }
}
