using ItemTrackerPlugin.Data;
using ItemTrackerPlugin.Ingest;

namespace ItemTrackerPlugin.App;

/// <summary>Where the tracker reads from and writes to.</summary>
public sealed class TrackerOptions
{
    private readonly object _logSourceGate = new();
    private readonly SemaphoreSlim _applyLogSourceGate = new(1, 1);
    private string _logDir = LogFileLocator.DefaultLogDir;
    private DateTimeOffset? _inceptionDate;

    public string LogDir
    {
        get { lock (_logSourceGate) return _logDir; }
        set { lock (_logSourceGate) _logDir = value; }
    }
    public string DatabasePath { get; set; } = TrackerDb.DefaultPath;

    /// <summary>Whether the window shows when the plugin starts; see <see cref="AppConfig.OpenAtStartup"/>.</summary>
    public bool OpenAtStartup { get; set; } = true;

    /// <summary>The wipe date; see <see cref="AppConfig.InceptionDate"/>. Null reads the whole history.</summary>
    public DateTimeOffset? InceptionDate
    {
        get { lock (_logSourceGate) return _inceptionDate; }
        set { lock (_logSourceGate) _inceptionDate = value; }
    }

    internal (string LogDir, DateTimeOffset? InceptionDate) CaptureLogSource(Action? afterLogDirCaptured = null)
    {
        lock (_logSourceGate)
        {
            var logDir = _logDir;
            afterLogDirCaptured?.Invoke();
            return (logDir, _inceptionDate);
        }
    }

    internal void SetLogSource(
        string logDir, DateTimeOffset? inceptionDate, Action? afterContention = null)
    {
        if (!Monitor.TryEnter(_logSourceGate))
        {
            afterContention?.Invoke();
            lock (_logSourceGate)
            {
                _logDir = logDir;
                _inceptionDate = inceptionDate;
            }

            return;
        }

        try
        {
            _logDir = logDir;
            _inceptionDate = inceptionDate;
        }
        finally
        {
            Monitor.Exit(_logSourceGate);
        }
    }

    /// <summary>
    /// Reads settings.json (see <see cref="AppConfig"/>) — the only place these settings
    /// come from. The database path is not one of them: it is fixed to the plugin's data root.
    /// </summary>
    public static TrackerOptions Load(string? configPath = null)
    {
        var config = AppConfig.Load(configPath);

        return new TrackerOptions
        {
            LogDir = config.LogDir ?? LogFileLocator.DefaultLogDir,
            InceptionDate = config.InceptionDate,
            OpenAtStartup = config.OpenAtStartup ?? true,
        };
    }

    /// <summary>
    /// Persists "open window at startup" to settings.json and takes it here. It only decides the next
    /// start, so nothing is restarted or rebuilt.
    /// </summary>
    public void SaveOpenAtStartup(bool open, string? configPath = null)
    {
        var config = AppConfig.Load(configPath);
        config.OpenAtStartup = open;
        config.Save(configPath);
        OpenAtStartup = open;
    }

    /// <summary>
    /// Persists a new log directory and wipe date to settings.json, applies them here,
    /// restarts the watcher if the directory changed, then wipes and re-ingests under its
    /// lock — the standalone tracker's "Save and rebuild", now driven by the engine's
    /// Settings panel.
    /// </summary>
    public async Task ApplyLogSourceAsync(
        string logDir, DateOnly? inceptionDate, LogWatchService watcher,
        string? configPath = null, CancellationToken cancellationToken = default)
        => await ApplyLogSourceAsync(logDir, inceptionDate, watcher, configPath,
            hooks: null, cancellationToken: cancellationToken);

    internal async Task ApplyLogSourceAsync(
        string logDir, DateOnly? inceptionDate, LogWatchService watcher,
        string? configPath, TrackerOptionsApplyHooks? hooks, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        hooks?.BeforeGate?.Invoke();
        if (!_applyLogSourceGate.Wait(0))
        {
            hooks?.OnApplyContended?.Invoke();
            await _applyLogSourceGate.WaitAsync(cancellationToken);
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(logDir) || !Directory.Exists(logDir))
                throw new DirectoryNotFoundException($"Log directory does not exist: {logDir}");

            var config = AppConfig.Load(configPath);
            var newDate = inceptionDate is { } d
                ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : (DateTimeOffset?)null;

            // Against what the running watcher has been using, read before it is overwritten — not
            // against settings.json, whose LogDir can still be unset on a fresh install and would
            // restart the watcher for a folder that never changed.
            var currentSource = CaptureLogSource();
            var logDirChanged = !string.Equals(currentSource.LogDir, logDir, StringComparison.OrdinalIgnoreCase);

            config.LogDir = logDir;
            config.InceptionDate = newDate;
            config.Save(configPath);
            hooks?.AfterConfigurationSaved?.Invoke();

            SetLogSource(config.LogDir ?? logDir, config.InceptionDate);
            hooks?.AfterSourceApplied?.Invoke();

            if (logDirChanged) watcher.RestartWatching();

            // The wipe and the re-ingest go together under the watcher's ingest lock; doing the
            // reset here would race a background pass already in flight. The config and source
            // are already committed, so finish this critical operation before reporting shutdown.
            if (hooks?.BeforeResetAsync is { } beforeReset) await beforeReset();
            await watcher.ResetAndRebuildAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            _applyLogSourceGate.Release();
        }
    }
}

internal sealed class TrackerOptionsApplyHooks
{
    public Action? BeforeGate { get; init; }
    public Action? OnApplyContended { get; init; }
    public Action? AfterConfigurationSaved { get; init; }
    public Action? AfterSourceApplied { get; init; }
    public Func<Task>? BeforeResetAsync { get; init; }
}
