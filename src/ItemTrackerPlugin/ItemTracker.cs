using System.Globalization;
using ItemTrackerPlugin.App;
using ItemTrackerPlugin.Ingest;
using Ocrx.Sdk;

namespace ItemTrackerPlugin;

/// <summary>
/// The OCRX side of the tracker. It reads nothing off the screen yet: the ledger comes from
/// Game.log alone, followed by <see cref="LogWatchService"/> on its own loop, and this class only
/// starts that loop and carries its two settings to and from the engine's Settings panel.
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

    /// <summary>
    /// None. The engine treats an empty set as a deliberate heartbeat-only subscription, and the
    /// ledger needs no frames until screen reads exist.
    /// </summary>
    public IReadOnlyList<RoiSubscription> Rois { get; } = [];

    /// <summary>Retries a failed start; see <see cref="StartAndPublishAsync"/>.</summary>
    public Task OnTickAsync(TickContext ctx, CancellationToken ct) =>
        _host is null && DateTime.UtcNow >= _nextStartAttempt
            ? StartAndPublishAsync(ctx.Services, ct)
            : Task.CompletedTask;

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
