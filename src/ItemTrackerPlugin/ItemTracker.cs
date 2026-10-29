using System.Globalization;
using ItemTrackerPlugin.App;
using ItemTrackerPlugin.Ingest;
using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Screen;
using Ocrx.Sdk;

namespace ItemTrackerPlugin;

/// <summary>
/// The OCRX side of the tracker. The ledger comes from Game.log, followed by
/// <see cref="LogWatchService"/> on its own loop; this class starts that loop, carries its two
/// settings to and from the engine's Settings panel, and turns the inventory screens into
/// observations (<see cref="ScreenReader"/>). It does not reconcile the two.
/// </summary>
public sealed class ItemTracker : IOcrxPlugin, IAsyncDisposable
{
    /// <summary>Opaque to the engine, which forwards each back untouched on a user edit.</summary>
    internal const string LogDirSettingId = "logDir";
    internal const string WipeDateSettingId = "wipeDate";

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

    /// <summary>Ticks in a row that must want the headers alone before an inventory screen's set is
    /// dropped, so one misread header does not cost two subscription round trips.</summary>
    private const int LeaveAfterTicks = 3;

    public ItemTracker() : this(fixedSet: false)
    {
    }

    /// <param name="fixedSet">Subscribe every region from the first tick and never switch. A replay
    /// feeds frames faster than a changed set can take effect (seven ticks in the 11-frame kiosk
    /// corpus), so its expectations need the full set from the start. It costs about 150 ms per
    /// scan against 50 ms for the headers alone, which is why live use switches.</param>
    /// <param name="startHost">False keeps the ledger from starting, for tests of the screen side.</param>
    internal ItemTracker(bool fixedSet, bool startHost = true)
    {
        _fixedSet = fixedSet;
        _startHost = startHost;
        _rois = fixedSet ? ScreenRois.For(ScreenContext.Kiosk) : ScreenRois.Idle;
        _screen = new ScreenReader(CurrentClasses);
    }

    /// <summary>
    /// The current set: the two headers until an inventory screen is up, then what that screen
    /// needs. The SDK reads it on every connect, so a reconnect resumes with the set in effect.
    /// </summary>
    public IReadOnlyList<RoiSubscription> Rois => _rois;

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

        await services.PublishSettingsAsync(BuildSpec(_host.Options), ct);
    }

    /// <summary>
    /// Applies an edit to the log folder or the wipe date through the tracker's own "Save and
    /// rebuild" path, which persists, wipes and re-reads the logs. An edit the tracker cannot use is
    /// logged and dropped; the republished spec then shows the values still in effect.
    /// </summary>
    public async Task OnApplySettings(ApplySettings apply, IPluginServices services, CancellationToken ct)
    {
        if (_host is null) return;

        var logDirEdit = apply.Values.FirstOrDefault(v => v.Id == LogDirSettingId);
        var wipeDateEdit = apply.Values.FirstOrDefault(v => v.Id == WipeDateSettingId);
        if (logDirEdit is null && wipeDateEdit is null) return;

        _classes = null;
        var options = _host.Options;
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
        ]);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }
}
