using ItemTrackerPlugin.App;

namespace ItemTrackerPlugin.Tests;

public sealed class StandaloneImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"itrk-import-{Guid.NewGuid():N}");
    private readonly string _source;
    private readonly string _root;

    public StandaloneImportTests()
    {
        _source = Path.Combine(_dir, "InventoryTracker");
        _root = Path.Combine(_dir, "OCRX", "ItemTracker");
        Directory.CreateDirectory(_source);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Copies_the_store_its_wal_and_both_settings_without_touching_the_source()
    {
        File.WriteAllText(Path.Combine(_source, "tracker.db"), "store");
        File.WriteAllText(Path.Combine(_source, "tracker.db-wal"), "wal");
        const string oldConfig = """
            {
              "LogDir": "E:\\Games\\StarCitizen\\LIVE",
              "DatabasePath": "C:\\old\\tracker.db",
              "Port": 5730,
              "InceptionDate": "2026-10-10T00:00:00+00:00",
              "SetupComplete": true
            }
            """;
        File.WriteAllText(Path.Combine(_source, "config.json"), oldConfig);
        var before = Snapshot(_source);

        Assert.True(StandaloneImport.Run(_source, _root));

        Assert.Equal("store", File.ReadAllText(Path.Combine(_root, "tracker.db")));
        Assert.Equal("wal", File.ReadAllText(Path.Combine(_root, "tracker.db-wal")));
        Assert.False(File.Exists(Path.Combine(_root, "tracker.db.import")));
        var settings = AppConfig.Load(Path.Combine(_root, "settings.json"));
        Assert.Equal(@"E:\Games\StarCitizen\LIVE", settings.LogDir);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero), settings.InceptionDate);
        Assert.DoesNotContain("DatabasePath", File.ReadAllText(Path.Combine(_root, "settings.json")));
        Assert.Equal(before, Snapshot(_source));
    }

    [Fact]
    public void Leaves_an_existing_plugin_store_and_its_settings_alone()
    {
        File.WriteAllText(Path.Combine(_source, "tracker.db"), "old store");
        File.WriteAllText(Path.Combine(_source, "config.json"), """{ "LogDir": "D:\\old" }""");
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "tracker.db"), "plugin store");

        Assert.False(StandaloneImport.Run(_source, _root));

        Assert.Equal("plugin store", File.ReadAllText(Path.Combine(_root, "tracker.db")));
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")));
    }

    [Fact]
    public void Does_nothing_without_a_standalone_store()
    {
        Assert.False(StandaloneImport.Run(_source, _root));

        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void An_unreadable_old_config_still_imports_the_store()
    {
        File.WriteAllText(Path.Combine(_source, "tracker.db"), "store");
        File.WriteAllText(Path.Combine(_source, "config.json"), "{ not json");

        Assert.True(StandaloneImport.Run(_source, _root));

        Assert.Equal("store", File.ReadAllText(Path.Combine(_root, "tracker.db")));
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")));
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(_source, "config.json")));
    }

    /// <summary>
    /// The standalone app still running holds its store open read-write, as SQLite does: nothing is
    /// copied, and a later start, once that app has closed, imports.
    /// </summary>
    [Fact]
    public void A_store_held_open_for_writing_is_left_for_a_later_start()
    {
        var sourceDb = Path.Combine(_source, "tracker.db");
        File.WriteAllText(sourceDb, "store");
        File.WriteAllText(sourceDb + "-wal", "wal");

        using (new FileStream(sourceDb, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            Assert.ThrowsAny<IOException>(() => StandaloneImport.Run(_source, _root));

        Assert.False(File.Exists(Path.Combine(_root, "tracker.db")));
        Assert.False(File.Exists(Path.Combine(_root, "tracker.db-wal")));

        Assert.True(StandaloneImport.Run(_source, _root));
        Assert.Equal("store", File.ReadAllText(Path.Combine(_root, "tracker.db")));
    }

    /// <summary>A WAL left by an interrupted import is older than the store copied after it.</summary>
    [Fact]
    public void A_wal_left_by_an_interrupted_import_is_dropped()
    {
        File.WriteAllText(Path.Combine(_source, "tracker.db"), "store");
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "tracker.db-wal"), "stale wal");
        File.WriteAllText(Path.Combine(_root, "tracker.db-shm"), "stale shm");
        File.WriteAllText(Path.Combine(_root, "tracker.db.import"), "partial");

        Assert.True(StandaloneImport.Run(_source, _root));

        Assert.Equal("store", File.ReadAllText(Path.Combine(_root, "tracker.db")));
        Assert.False(File.Exists(Path.Combine(_root, "tracker.db-wal")));
        Assert.False(File.Exists(Path.Combine(_root, "tracker.db-shm")));
        Assert.False(File.Exists(Path.Combine(_root, "tracker.db.import")));
    }

    /// <summary>Every file in the folder with its content and write time.</summary>
    private static List<(string Name, string Content, DateTime Written)> Snapshot(string dir) =>
        Directory.GetFiles(dir)
            .Order(StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f), File.GetLastWriteTimeUtc(f)))
            .ToList();
}
