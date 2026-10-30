using System.Globalization;
using ItemTrackerPlugin.Data;
using ItemTrackerPlugin.Naming;
using ItemTrackerPlugin.Reconcile;
using ItemTrackerPlugin.Resolve;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ItemTrackerPlugin.App;

/// <summary>What the UI shows for one item: its placement plus its human-readable name.</summary>
public sealed record NamedHolding(ItemHolding Holding, ResolvedName Name)
{
    public string Display => Name.Display;
    public string ClassName => Holding.ItemClass;
}

/// <summary>
/// The current view of the world, shared by every connected page. Rebuilt after each
/// ingest pass; pages subscribe to <see cref="Changed"/> so tailing the live log updates
/// an open browser without a refresh.
/// </summary>
/// <param name="log">Optional so tests can build one bare; the host always supplies it.</param>
public sealed class TrackerState(
    TrackerDb db, WikiNameService names, TrackerOptions options, ILogger<TrackerState>? log = null)
{
    private readonly Lock _gate = new();

    /// <summary>
    /// One rebuild at a time. Rebuild is called from the ingest pass, the wiki refreshes, the
    /// tray and the pages, and each reads the store then publishes what it read. Two at once
    /// let the one that read first publish last, replacing a snapshot that had the newest
    /// moves with one that did not — and nothing rebuilds again until something else moves.
    /// Run in turn, the last to publish is always the last to read.
    /// </summary>
    private readonly Lock _rebuildGate = new();

    private Snapshot _current = Snapshot.Empty;

    public event Action? Changed;

    public sealed record Snapshot(
        IReadOnlyList<NamedHolding> Holdings,
        HoldingResolver? Resolver,
        DateTimeOffset BuiltAt,
        IngestHealth Health,
        Reconciliation? Reconciliation = null)
    {
        public static readonly Snapshot Empty = new([], null, DateTimeOffset.MinValue, IngestHealth.Empty);
    }

    /// <summary>The game text the reconciliation names ledger classes by, read once per log folder.</summary>
    private (string LogDir, GameLocalization Text)? _text;

    public Snapshot Current
    {
        get { lock (_gate) return _current; }
    }

    public WikiNameService Names => names;

    /// <summary>Re-reads the store and re-resolves every item, then notifies subscribers.</summary>
    public void Rebuild() => Rebuild(afterOptionsCaptured: null, afterResolverLoaded: null, beforePublish: null);

    internal void Rebuild(
        Action? afterOptionsCaptured, Action? afterResolverLoaded, Action? beforePublish,
        Action? afterLogDirCaptured = null)
    {
        lock (_rebuildGate)
        {
            // Keep the diagnostic values tied to this rebuild even if settings are changed
            // while the database snapshot and in-memory replay are being built.
            var (logDir, inceptionDate) = options.CaptureLogSource(afterLogDirCaptured);
            afterOptionsCaptured?.Invoke();

            IngestHealth? health = null;
            var resolver = HoldingResolver.Load(
                db,
                asOf: null,
                afterMovesRead: null,
                readAdditionalSnapshot: cn => health = LoadHealthSnapshot(cn, logDir, inceptionDate));
            afterResolverLoaded?.Invoke();
            var resolved = resolver.ResolveAll();
            var holdings = resolved
                .Select(h => new NamedHolding(h, names.Resolve(h.ItemClass)))
                .ToList();

            var reconciliation = Reconcile(logDir, resolver, resolved);
            var snapshot = new Snapshot(holdings, resolver, DateTimeOffset.UtcNow, health!, reconciliation);
            beforePublish?.Invoke();

            lock (_gate)
            {
                _current = snapshot;
            }
        }

        // Outside the gate: subscribers only queue a re-render, and read Current when it runs.
        // One at a time, each shielded: the snapshot is already published, so a page that
        // throws must neither cost the pages after it their update nor fail the caller — an
        // ingest pass would record a failure, keep the rebuild owed and run another.
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception ex)
            {
                log?.LogWarning(ex, "A snapshot subscriber failed");
            }
        }
    }

    /// <summary>
    /// The states, or none when they cannot be worked out: the ledger worked before the screen was
    /// read and must still publish when reconciling fails.
    /// </summary>
    private Reconciliation Reconcile(string logDir, HoldingResolver resolver, IReadOnlyList<ItemHolding> holdings)
    {
        try
        {
            return new Reconciler(Namer(logDir)).Run(ReconcileSources.Load(db, resolver, holdings));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or FormatException or UnauthorizedAccessException)
        {
            log?.LogWarning(ex, "Reconciling with the screen failed; publishing the ledger alone");
            return Reconciliation.Empty;
        }
    }

    /// <summary>
    /// Names a class by the game's own text, else by the wiki when it knows the class. A folder with no
    /// <c>global.ini</c> is read again next time: the game may write one.
    /// </summary>
    private ItemNamer Namer(string logDir)
    {
        if (_text is not { } cached || cached.LogDir != logDir || cached.Text.Count == 0)
            _text = cached = (logDir, GameLocalization.Load(logDir));
        return new ItemNamer(cached.Text, c => names.Resolve(c) is { Match: not NameMatch.None } n ? n.Display : null);
    }

    private static IngestHealth LoadHealthSnapshot(
        SqliteConnection cn, string logDir, DateTimeOffset? inceptionDate)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COUNT(*),
                SUM(CASE WHEN last_ts IS NOT NULL THEN 1 ELSE 0 END),
                COALESCE(SUM(unrecognised), 0),
                MAX(last_line_ts)
            FROM session
            """;

        using var r = cmd.ExecuteReader();
        r.Read();

        var logFiles = r.GetInt32(0);
        var filesWithLines = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture);
        var unrecognised = r.GetInt64(2);
        var newestLogLine = r.IsDBNull(3)
            ? (DateTimeOffset?)null
            : DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture);

        using var movesCmd = cn.CreateCommand();
        movesCmd.CommandText = "SELECT COUNT(*) FROM move";
        var moves = Convert.ToInt64(movesCmd.ExecuteScalar(), CultureInfo.InvariantCulture);

        // Each file keeps where the player stood at its watermark; the newest file's is the latest.
        using var hereCmd = cn.CreateCommand();
        hereCmd.CommandText = """
            SELECT cur_loc_id FROM session
            WHERE cur_loc_id IS NOT NULL
            ORDER BY last_line_ts DESC, id DESC
            LIMIT 1
            """;
        var here = hereCmd.ExecuteScalar() as string;

        return new IngestHealth(
            logDir, inceptionDate, logFiles, filesWithLines, moves, unrecognised, newestLogLine)
        {
            CurrentLocationId = here,
        };
    }
}
