using ItemTrackerPlugin.Data;
using ItemTrackerPlugin.Ingest;
using ItemTrackerPlugin.Naming;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ItemTrackerPlugin.App;

/// <summary>
/// The standalone tracker's ingest stack — store, settings, wiki names, snapshot, log watcher —
/// wired the way its tray host wired them, minus the UI. Everything it writes lives under one data
/// root: the real one, or a throwaway one for a replay, which is deleted on dispose.
/// </summary>
internal sealed class TrackerHost : IAsyncDisposable
{
    /// <summary>star-citizen.wiki asks API clients to say who they are.</summary>
    private const string WikiUserAgent = "OCRX-ItemTracker/1.0 (Star Citizen inventory tracker)";

    private readonly ILoggerFactory _loggers;
    private readonly HttpClient _wiki;
    private readonly bool _throwaway;

    private TrackerHost(string dataRoot, bool throwaway)
    {
        DataRoot = dataRoot;
        _throwaway = throwaway;
        Directory.CreateDirectory(dataRoot);

        _loggers = LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new FileLoggerProvider(Path.Combine(dataRoot, "tracker.log"))));
        var log = _loggers.CreateLogger<TrackerHost>();

        if (!throwaway && StandaloneImport.Run(StandaloneImport.DefaultSourceDir, dataRoot))
            log.LogInformation("Imported the standalone tracker's store from {Dir}", StandaloneImport.DefaultSourceDir);

        Options = TrackerOptions.Load(SettingsPath);
        Options.DatabasePath = Path.Combine(dataRoot, "tracker.db");
        Db = new TrackerDb(Options.DatabasePath);
        Db.Initialize();

        _wiki = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _wiki.DefaultRequestHeaders.UserAgent.ParseAdd(WikiUserAgent);
        var names = new WikiNameService(Db, _wiki);
        var places = new PlaceNameService(Db, Options, _wiki);
        var state = new TrackerState(Db, names, Options, _loggers.CreateLogger<TrackerState>());
        Watcher = new LogWatchService(Options, Db, state, names, places, _loggers.CreateLogger<LogWatchService>());
    }

    public string DataRoot { get; }
    public string SettingsPath => Path.Combine(DataRoot, "settings.json");
    public TrackerOptions Options { get; }
    public TrackerDb Db { get; }
    public LogWatchService Watcher { get; }

    /// <summary>
    /// Builds the stack and starts following the logs. A replay gets a throwaway root under the temp
    /// folder: a corpus run must never touch the real ledger, nor import into it.
    /// </summary>
    public static async Task<TrackerHost> StartAsync(bool replay, CancellationToken ct)
    {
        var host = new TrackerHost(DataRootFor(replay), throwaway: replay);
        await host.Watcher.StartAsync(ct);
        return host;
    }

    internal static string DataRootFor(bool replay) => replay
        ? Path.Combine(Path.GetTempPath(), $"ItemTracker-replay-{Guid.NewGuid():N}")
        : Path.GetDirectoryName(TrackerDb.DefaultPath)!;

    /// <summary>Stops the watcher after the pass in flight, as the tray app did on exit.</summary>
    public async ValueTask DisposeAsync()
    {
        await Watcher.StopAsync(CancellationToken.None);
        Watcher.Dispose();
        _wiki.Dispose();

        if (_throwaway)
        {
            // Pooled connections hold the store's files open; the folder cannot go while they do.
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(DataRoot, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover under the temp folder is harmless; failing the plugin's exit over it is not.
                _loggers.CreateLogger<TrackerHost>().LogWarning(ex, "Could not delete the replay store at {Dir}", DataRoot);
            }
        }

        _loggers.Dispose();
    }
}
