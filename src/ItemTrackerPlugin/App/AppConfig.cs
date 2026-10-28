using System.Text.Json;
using System.Text.Json.Serialization;
using ItemTrackerPlugin.Data;
using ItemTrackerPlugin.Ingest;

namespace ItemTrackerPlugin.App;

/// <summary>
/// The tracker's two settings, edited through the engine's Settings panel (see
/// <see cref="ItemTracker"/>) and kept in settings.json.
/// <para>
/// Not config.json: that name is the SDK's per-plugin <c>PluginConfig</c> file (ConfigSeed),
/// which the overlay outputs will need in the same folder.
/// </para>
/// </summary>
public sealed class AppConfig
{
    private static readonly object PublishLock = new();

    public string? LogDir { get; set; }

    /// <summary>
    /// The wipe date: everything logged before it is ignored, items included. Null — the
    /// default — reads the whole history. Only worth setting after a persistence wipe, since
    /// the tracker learns about an item only from a line that moves it. The JSON key keeps
    /// its original name so the standalone tracker's config.json still loads on import.
    /// </summary>
    public DateTimeOffset? InceptionDate { get; set; }

    /// <summary>Same directory as the database: both are this install's private state.</summary>
    public static string DefaultPath => Path.Combine(
        Path.GetDirectoryName(TrackerDb.DefaultPath) ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OCRX", "ItemTracker"),
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Reads settings.json, filling and persisting any unset field with its default so the
    /// file exists and is fully populated after the very first run — there is nowhere
    /// else left to look these values up.
    /// </summary>
    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        var existed = File.Exists(path);
        var config = existed ? ReadOrDefault(path) : new AppConfig();

        var changed = !existed;
        if (config.LogDir is null) { config.LogDir = LogFileLocator.DefaultLogDir; changed = true; }

        if (changed) config.Save(path);
        return config;
    }

    /// <summary>
    /// Reads the config, or returns a blank one after moving an unreadable file aside.
    /// <para>
    /// The file is never overwritten in place on a parse failure. Load fills the blank
    /// config's defaults and saves it, so overwriting here would destroy InceptionDate with
    /// no way to recover what was lost. Keeping the original under .corrupt makes that
    /// repairable.
    /// </para>
    /// </summary>
    private static AppConfig ReadOrDefault(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.Error.WriteLine($"Could not read config at {path}, using defaults: {ex.Message}");
            TryPreserveCorrupt(path);
            return new AppConfig();
        }
    }

    private static void TryPreserveCorrupt(string path)
    {
        try
        {
            var kept = path + ".corrupt";
            File.Delete(kept);
            File.Move(path, kept);
            Console.Error.WriteLine($"Original kept at {kept}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort. Losing the copy is bad; failing to start over it would be worse.
        }
    }

    /// <summary>
    /// Writes the config atomically. A direct WriteAllText truncates before it writes, so a
    /// crash or power cut mid-write leaves a half-written file that fails to parse on the
    /// next launch — the exact input <see cref="ReadOrDefault"/> has to recover from.
    /// </summary>
    public void Save(string? path = null)
        => Save(path, afterTempWritten: null);

    internal void Save(string? path, Action<string>? afterTempWritten)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, JsonOptions);
        var temp = path + $".{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(temp, json);
            afterTempWritten?.Invoke(temp);
            lock (PublishLock)
                File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
