using System.Text.Json;

namespace ItemTrackerPlugin.App;

/// <summary>
/// Brings the standalone tracker's store over once, the first time the plugin runs without one of
/// its own. The old files are only ever read: the standalone app may still be installed, and its
/// data stays its own.
/// </summary>
internal static class StandaloneImport
{
    /// <summary>Where the standalone tracker kept tracker.db and config.json.</summary>
    public static string DefaultSourceDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventoryTracker");

    /// <summary>
    /// Copies tracker.db (and its WAL, if the standalone app left one) into
    /// <paramref name="dataRoot"/> when the plugin has no store yet, so the provenance the store
    /// carries survives the move. The log folder and wipe date come along too when the plugin has no
    /// settings yet: a store built under one wipe date and resumed under another would mix two
    /// histories. Returns true when a store was imported.
    /// </summary>
    public static bool Run(string sourceDir, string dataRoot)
    {
        var db = Path.Combine(dataRoot, "tracker.db");
        var sourceDb = Path.Combine(sourceDir, "tracker.db");
        if (File.Exists(db) || !File.Exists(sourceDb)) return false;

        Directory.CreateDirectory(dataRoot);
        ImportSettings(Path.Combine(sourceDir, "config.json"), Path.Combine(dataRoot, "settings.json"));

        // Opened denying writers. SQLite holds a store read-write for as long as a connection is open,
        // pooled ones included, so while the standalone app runs this throws and the import waits for
        // a later start; while it is held, that app cannot change the store half-way through the copy.
        // File.Copy would share with a writer and could take a torn copy.
        var partial = db + ".import";
        using (var source = new FileStream(sourceDb, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // The WAL first and the database last, through a temporary name: tracker.db existing is
            // what marks the import done, so a crash part-way leaves no store and the next start
            // imports again. A WAL or shared-memory file left by such an attempt belongs to an older
            // state of the store: replayed over a newer copy it would put old pages back.
            File.Delete(db + "-wal");
            File.Delete(db + "-shm");
            var sourceWal = sourceDb + "-wal";
            if (File.Exists(sourceWal)) File.Copy(sourceWal, db + "-wal");

            using var copy = File.Create(partial);
            source.CopyTo(copy);
        }

        File.Move(partial, db);
        return true;
    }

    private static void ImportSettings(string sourceConfig, string settings)
    {
        if (File.Exists(settings) || !File.Exists(sourceConfig)) return;

        // Deserialised here rather than through AppConfig.Load, which writes defaults back to the
        // file it reads and moves an unreadable one aside — neither may happen to the old file.
        AppConfig? old;
        try
        {
            old = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(sourceConfig));
        }
        catch (JsonException)
        {
            // An unreadable old config costs its two settings, not the store: the plugin starts
            // on defaults, as the standalone app itself would have.
            return;
        }

        if (old is null) return;

        new AppConfig { LogDir = old.LogDir, InceptionDate = old.InceptionDate }.Save(settings);
    }
}
