using System.Globalization;
using ItemTrackerPlugin.App;
using ItemTrackerPlugin.Ingest;
using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Overlay;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Screen;
using ItemTrackerPlugin.Window;
using Microsoft.Data.Sqlite;
using Ocrx.Sdk;

namespace ItemTrackerPlugin;

/// <summary>
/// The OCRX side of the tracker. The ledger comes from Game.log, followed by
/// <see cref="LogWatchService"/> on its own loop; this class starts that loop, carries its two
/// settings to and from the engine's Settings panel, and turns the inventory screens into
/// observations (<see cref="ScreenReader"/>), which it stores for the reconciliation the ledger
/// snapshot runs (<see cref="Reconciler"/>). What the user sees comes from two places: the in-game
/// status line while an inventory screen is open (<see cref="InventoryStatus"/>, drawn by the SDK
/// overlay), and the plugin's own read-only window with its tray icon (<see cref="TrackerWindow"/>).
/// </summary>
public sealed class ItemTracker : IOcrxPlugin, IAsyncDisposable
{
    /// <summary>Opaque to the engine, which forwards each back untouched on a user edit.</summary>
    internal const string LogDirSettingId = "logDir";
    internal const string WipeDateSettingId = "wipeDate";
    internal const string OpenAtStartupSettingId = "openAtStartup";
    internal const string ThemeSettingId = "theme";

    private const string WipeDateFormat = "yyyy-MM-dd";

    private static readonly TimeSpan StartRetryGap = TimeSpan.FromSeconds(30);

    private TrackerHost? _host;
    private DateTime _nextStartAttempt;

    public string Name => "ItemTrackerPlugin";

    private readonly ScreenReader _screen;
    private readonly bool _fixedSet;
    private IReadOnlyList<RoiSubscription> _rois;
    private ItemClassResolver? _classes;
    private readonly bool _startHost;
    private int _leaveTicks;
    private string? _lastShown;
    private int? _lastQuantity;
    private readonly InventoryStatus _status = new();
    private readonly Func<string, TrackerWindow>? _startWindow;
    private TrackerWindow? _window;
    private ViewFeed? _feed;
    private bool _windowDecided;
    private (DateTimeOffset Built, string? Id, StationStatus? Value)? _stationCache;
    private string? _openedId;
    private DateTime _openedCheckedAt;
    private DateTime _nextRebuild;

    private static readonly TimeSpan OpenedLookupGap = TimeSpan.FromSeconds(1);

    /// <summary>The fewest seconds between rebuilds asked for by what the screen showed.</summary>
    private static readonly TimeSpan RebuildGap = TimeSpan.FromSeconds(3);

    /// <summary>Ticks in a row that must want the headers alone before an inventory screen's set is
    /// dropped, so one misread header does not cost two subscription round trips.</summary>
    private const int LeaveAfterTicks = 3;

    public ItemTracker() : this(fixedSet: false, startWindow: StartWindow)
    {
    }

    /// <param name="fixedSet">Subscribe every region from the first tick and never switch. A replay
    /// feeds frames faster than a changed set can take effect (seven ticks in the 11-frame kiosk
    /// corpus), so its expectations need the full set from the start. It costs about 150 ms per
    /// scan against 50 ms for the headers alone, which is why live use switches.</param>
    /// <param name="startHost">False keeps the ledger from starting, for tests of the screen side.</param>
    /// <param name="startWindow">Builds the window and tray icon from the tracker's data root; null,
    /// as in every test, runs without them.</param>
    internal ItemTracker(bool fixedSet, bool startHost = true, Func<string, TrackerWindow>? startWindow = null)
    {
        _fixedSet = fixedSet;
        _startHost = startHost;
        _startWindow = startWindow;
        _rois = fixedSet ? ScreenRois.For(ScreenContext.Kiosk) : ScreenRois.Idle;
        _screen = new ScreenReader(CurrentClasses);
    }

    /// <summary>
    /// The current set: the two headers until an inventory screen is up, then what that screen
    /// needs. The SDK reads it on every connect, so a reconnect resumes with the set in effect.
    /// </summary>
    public IReadOnlyList<RoiSubscription> Rois => _rois;

    /// <summary>Replaces the lookup of the open station, for tests of the status line.</summary>
    internal Func<StationStatus?>? StationSource { get; set; }

    /// <summary>A region that fails on one tick must not hide the rest; the reader checks each.</summary>
    public RoiErrorPolicy ErrorPolicy => RoiErrorPolicy.SkipErrored;

    /// <summary>
    /// Retries a failed start (see <see cref="StartAndPublishAsync"/>), then reads the screen. The
    /// header decides which regions are subscribed; a changed set applies from the next tick, so the
    /// tick that first shows a screen reads nothing of it.
    /// </summary>
    public async Task OnTickAsync(TickContext ctx, CancellationToken ct)
    {
        if (_startHost && _host is null && DateTime.UtcNow >= _nextStartAttempt)
            await StartAndPublishAsync(ctx.Services, ct);

        if (ScreenReader.ReadHeader(ctx.Tick) is not { } header) return;

        Observe(ctx, _screen.Read(ctx.Tick, header));
        ShowStatus(ctx, header);

        var wanted = ScreenRois.For(header.Context);
        if (_fixedSet || ReferenceEquals(wanted, _rois))
        {
            _leaveTicks = 0;
            return;
        }

        if (ReferenceEquals(wanted, ScreenRois.Idle) && ++_leaveTicks < LeaveAfterTicks) return;

        _leaveTicks = 0;
        var previous = _rois;
        _rois = wanted;
        try
        {
            await ctx.Services.UpdateRoisAsync(wanted, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The engine still has the old set; remember that so the next tick asks again. A
            // reconnect needs no help: the SDK re-reads Rois, which is the set wanted.
            _rois = previous;
            ctx.Services.Log($"ItemTrackerPlugin: could not switch regions, retrying: {ex.Message}");
        }
    }

    /// <summary>
    /// The status line on the overlay while a kiosk or a ship locker is up, cleared when it is not. Only
    /// a change is emitted: the overlay shows each record as it arrives and keeps it until the next.
    /// </summary>
    private void ShowStatus(TickContext ctx, ScreenHeader header)
    {
        var page = header.Context is ScreenContext.Kiosk or ScreenContext.ShipStorage ? ScreenReader.ReadPage(ctx.Tick) : null;
        if (!_status.Next(header, page, ctx.Tick.Timestamp, () => CurrentStation(ctx.Services), out var line)) return;

        if (line is null)
            ctx.Services.EmitCleared(ctx.Tick.Timestamp, Name);
        else
            ctx.Services.Emit(StatusRecord.Show(line, ctx.Tick.Timestamp, Name));
    }

    /// <summary>
    /// The station the log last said was opened, with its states from the latest snapshot. A tick comes
    /// many times a second, so the open-line lookup (a store read) runs at most once a second, and the
    /// states are rebuilt only when that id or the snapshot changed.
    /// </summary>
    private StationStatus? CurrentStation(IPluginServices services)
    {
        if (StationSource is { } injected) return injected();
        if (_host is null) return null;

        var now = DateTime.UtcNow;
        if (now - _openedCheckedAt >= OpenedLookupGap)
        {
            _openedCheckedAt = now;
            try
            {
                _openedId = ReconcileSources.LatestOpened(_host.Db);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                services.Log($"ItemTrackerPlugin: could not find the open station: {ex.Message}");
            }
        }

        var snapshot = _host.State.Current;
        if (_stationCache is { } cached && cached.Built == snapshot.BuiltAt && cached.Id == _openedId) return cached.Value;

        StationStatus? status = null;
        if (_openedId is { } id)
        {
            var reconciliation = snapshot.Reconciliation ?? Reconciliation.Empty;
            status = new StationStatus(
                id,
                snapshot.Resolver?.Places.ByLocationId(id)?.Name ?? snapshot.Resolver?.PlaceLabel(id) ?? id,
                reconciliation.At(id),
                reconciliation.Pages.Where(p => p.LocationId == id).ToList());
        }

        _stationCache = (snapshot.BuiltAt, _openedId, status);
        return status;
    }

    /// <summary>
    /// A hovered item stays hovered for many ticks, so live use emits when what is shown changes,
    /// not on every tick. A replay emits every frame: each is its own observation there.
    /// </summary>
    private void Observe(TickContext ctx, ScreenObservation? observation)
    {
        if (observation is null)
        {
            _lastShown = null;
            return;
        }

        var shown = $"{observation.Panel}|{observation.ShipModel}|{observation.Tooltip.Name}|{observation.Page}";
        var unchanged = shown == _lastShown && (observation.Quantity is null || observation.Quantity == _lastQuantity);
        if (!_fixedSet && unchanged) return;

        _lastQuantity = shown == _lastShown ? observation.Quantity ?? _lastQuantity : observation.Quantity;
        _lastShown = shown;
        ctx.Services.Emit(ScreenRecord.From(observation, ctx.Tick.Timestamp, ctx.Tick.FrameSeq, Name));
        Store(ctx, observation);
    }

    /// <summary>
    /// Keeps the observation for the next rebuild of the ledger snapshot. The time is the read's, not
    /// the tick's: a replay tick carries replay time, and a replay writes to its own throwaway store.
    /// A failed write costs one observation, never the tick.
    /// </summary>
    private void Store(TickContext ctx, ScreenObservation observation)
    {
        if (_host is null) return;

        try
        {
            ObservationStore.Add(_host.Db, observation, DateTimeOffset.UtcNow);
            RequestRebuild(_host, ctx.Services);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctx.Services.Log($"ItemTrackerPlugin: could not store an observation: {ex.Message}");
        }
    }

    /// <summary>
    /// What the screen showed changes the states, but no log line follows it, so nothing else would
    /// rebuild the snapshot the status line and the view read. A throttled rebuild off the tick thread; a
    /// replay has no use for one.
    /// </summary>
    private void RequestRebuild(TrackerHost host, IPluginServices services)
    {
        if (services.Engine.ReplayMode || DateTime.UtcNow < _nextRebuild) return;

        _nextRebuild = DateTime.UtcNow + RebuildGap;
        _ = Task.Run(() =>
        {
            try
            {
                host.State.Rebuild();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                services.Log($"ItemTrackerPlugin: could not rebuild after a screen read: {ex.Message}");
            }
        });
    }

    /// <summary>The game's item names, loaded once the log folder is known.</summary>
    private ItemClassResolver? CurrentClasses() =>
        _classes ??= _host is null ? null : new ItemClassResolver(GameLocalization.Load(_host.Options.LogDir));

    /// <summary>
    /// Starts ingestion on the first connect, then publishes the settings. The first connect follows
    /// process start at once — plugin start is not gated on the game, only frames are — and it is
    /// the earliest point at which the plugin knows whether the engine is replaying a corpus, which
    /// decides between the real store and a throwaway one. Later connects reuse what is running.
    /// </summary>
    public Task OnConnectedAsync(IPluginServices services, CancellationToken ct) => StartAndPublishAsync(services, ct);

    /// <summary>
    /// A start that fails — the standalone tracker still holding the store being imported, say — is
    /// logged and tried again from a later tick, so the ledger does not stay dead until the engine
    /// restarts. The SDK would only print the failure and carry on without it.
    /// </summary>
    private async Task StartAndPublishAsync(IPluginServices services, CancellationToken ct)
    {
        if (_host is null)
        {
            try
            {
                _host = await TrackerHost.StartAsync(services.Engine.ReplayMode, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _nextStartAttempt = DateTime.UtcNow + StartRetryGap;
                services.Log($"ItemTrackerPlugin: the tracker did not start, retrying in {StartRetryGap.TotalSeconds:0} s: {ex.Message}");
                return;
            }
        }

        StartWindowOnce(_host, services);
        await services.PublishSettingsAsync(BuildSpec(_host.Options), ct);
    }

    /// <summary>
    /// The window and its tray icon, once per run, after the first successful start: a reconnect
    /// reuses them, and a replay never has them.
    /// </summary>
    private void StartWindowOnce(TrackerHost host, IPluginServices services)
    {
        if (_windowDecided) return;
        _windowDecided = true;

        var start = WindowStartup.Decide(services.Engine.ReplayMode, host.Options.OpenAtStartup);
        if (start == WindowStart.None || _startWindow is null) return;

        var window = _startWindow(host.DataRoot);
        _window = window;
        window.SetTheme(host.Options.Theme);
        _feed = new ViewFeed(host.State, window.Post, services.Log);
        if (start == WindowStart.Open) window.Show();
    }

    /// <summary>The page ships beside the exe; WebView2 keeps its profile in the tracker's data root.</summary>
    private static TrackerWindow StartWindow(string dataRoot) => TrackerWindow.Start(
        Path.Combine(AppContext.BaseDirectory, "view"),
        Path.Combine(dataRoot, "WebView2"),
        Console.Error.WriteLine);

    /// <summary>
    /// Applies an edit to the log folder or the wipe date through the tracker's own "Save and
    /// rebuild" path, which persists, wipes and re-reads the logs. "Open window at startup" is only
    /// saved: it decides the next start and neither opens nor closes the window now. "Window theme" is
    /// saved and recolours the window at once. An edit the tracker cannot use is logged and dropped;
    /// the republished spec then shows the values still in effect.
    /// </summary>
    public async Task OnApplySettings(ApplySettings apply, IPluginServices services, CancellationToken ct)
    {
        if (_host is null) return;

        var options = _host.Options;
        var openEdit = apply.Values.FirstOrDefault(v => v.Id == OpenAtStartupSettingId);
        if (openEdit is not null)
        {
            if (WindowStartup.TryParseToggle(openEdit.Value, out var open))
            {
                try
                {
                    options.SaveOpenAtStartup(open, _host.SettingsPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The log edits of the same apply still go through; the republished spec shows the value kept.
                    services.Log($"ItemTrackerPlugin: could not save open at startup: {ex.Message}");
                }
            }
            else
                services.Log($"ItemTrackerPlugin: ignoring open at startup '{openEdit.Value}'; expected true or false.");
        }

        var themeEdit = apply.Values.FirstOrDefault(v => v.Id == ThemeSettingId);
        if (themeEdit is not null)
        {
            if (WindowThemes.TryParse(themeEdit.Value, out var theme))
            {
                try
                {
                    options.SaveTheme(theme, _host.SettingsPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Not saved, but the window still takes it for this run.
                    options.Theme = theme;
                    services.Log($"ItemTrackerPlugin: could not save the window theme: {ex.Message}");
                }

                _window?.SetTheme(theme);
            }
            else
                services.Log($"ItemTrackerPlugin: ignoring window theme '{themeEdit.Value}'; expected system, light or dark.");
        }

        var logDirEdit = apply.Values.FirstOrDefault(v => v.Id == LogDirSettingId);
        var wipeDateEdit = apply.Values.FirstOrDefault(v => v.Id == WipeDateSettingId);
        if (logDirEdit is null && wipeDateEdit is null)
        {
            if (openEdit is not null || themeEdit is not null) await services.PublishSettingsAsync(BuildSpec(options), ct);
            return;
        }

        _classes = null;
        var logDir = logDirEdit is null ? options.LogDir
            : string.IsNullOrWhiteSpace(logDirEdit.Value) ? LogFileLocator.DefaultLogDir
            : logDirEdit.Value.Trim();

        var wipeDate = options.InceptionDate is { } current ? DateOnly.FromDateTime(current.DateTime) : (DateOnly?)null;
        if (wipeDateEdit is not null)
        {
            if (!TryParseWipeDate(wipeDateEdit.Value, out wipeDate))
            {
                services.Log($"ItemTrackerPlugin: ignoring wipe date '{wipeDateEdit.Value}'; expected {WipeDateFormat} or blank.");
                await services.PublishSettingsAsync(BuildSpec(options), ct);
                return;
            }
        }

        try
        {
            await options.ApplyLogSourceAsync(logDir, wipeDate, _host.Watcher, _host.SettingsPath, ct);
        }
        catch (DirectoryNotFoundException ex)
        {
            services.Log($"ItemTrackerPlugin: {ex.Message}");
        }

        await services.PublishSettingsAsync(BuildSpec(options), ct);
    }

    internal static bool TryParseWipeDate(string value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!DateOnly.TryParseExact(value.Trim(), WipeDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;

        date = parsed;
        return true;
    }

    /// <summary>
    /// Field order, labels and help match the shipped settings-spec.json, which is what the panel
    /// shows before the plugin first connects; only the values here are live.
    /// </summary>
    internal static SettingsSpec BuildSpec(TrackerOptions options) =>
        new([
            new SettingsField(
                Id: LogDirSettingId,
                Label: "Game log folder",
                Type: SettingsFieldType.String,
                Value: options.LogDir,
                Help: "The Star Citizen channel folder that holds Game.log, such as the LIVE folder of the install. Blank finds it automatically. Changing it rebuilds the ledger from the logs.",
                Group: "Logs"),
            new SettingsField(
                Id: WipeDateSettingId,
                Label: "Wipe date",
                Type: SettingsFieldType.String,
                Value: options.InceptionDate?.ToString(WipeDateFormat, CultureInfo.InvariantCulture) ?? "",
                Help: "yyyy-MM-dd. Everything logged before it is ignored. Blank reads the whole history. Changing it rebuilds the ledger from the logs.",
                Group: "Logs"),
            new SettingsField(
                Id: OpenAtStartupSettingId,
                Label: "Open window at startup",
                Type: SettingsFieldType.Toggle,
                Value: WindowStartup.FormatToggle(options.OpenAtStartup),
                Help: "Shows the Item Tracker window when OCRX starts the plugin. Its icon in the notification area opens it at any time. Takes effect at the next start.",
                Group: "Window"),
            new SettingsField(
                Id: ThemeSettingId,
                Label: "Window theme",
                Type: SettingsFieldType.Select,
                Value: WindowThemes.Format(options.Theme),
                Help: "Light or dark colours for the Item Tracker window. Follow Windows matches the app mode chosen in Windows settings and changes with it. Takes effect at once.",
                Options: WindowThemes.Options,
                Group: "Window"),
        ]);

    public async ValueTask DisposeAsync()
    {
        _feed?.Dispose();
        _window?.Dispose();
        if (_host is not null) await _host.DisposeAsync();
    }
}
