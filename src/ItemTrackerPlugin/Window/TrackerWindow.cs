using System.Drawing;
using System.Windows.Forms;

namespace ItemTrackerPlugin.Window;

/// <summary>
/// The plugin's own window and tray icon, on a UI thread of their own: the SDK host loop never waits
/// on them and they never wait on it. The tray icon (tooltip "Item Tracker") opens the window on a
/// click or through its "Open" item; it has no "Quit", because the engine owns the plugin's lifetime.
/// Closing the window hides it, with a balloon the first time (<see cref="CloseNotice"/>).
/// <see cref="Dispose"/>, called when the host returns, removes both and ends the thread.
/// </summary>
internal sealed class TrackerWindow : IDisposable
{
    internal const string IconResource = "ItemTrackerPlugin.Window.ItemTracker.ico";

    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(5);

    private readonly string _viewDir;
    private readonly string _userDataDir;
    private readonly Action<string> _log;
    private readonly CloseNotice _notice = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private Control? _ui;
    private ViewForm? _form;
    private NotifyIcon? _tray;
    private Icon? _icon;
    private string? _latest;

    private TrackerWindow(string viewDir, string userDataDir, Action<string> log)
    {
        _viewDir = viewDir;
        _userDataDir = userDataDir;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "Item Tracker window" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>
    /// Starts the UI thread and puts the tray icon up; the window itself is built on the first
    /// <see cref="Show"/>. Returns once the icon is up, or the thread failed.
    /// </summary>
    public static TrackerWindow Start(string viewDir, string userDataDir, Action<string> log)
    {
        var window = new TrackerWindow(viewDir, userDataDir, log);
        window._thread.Start();
        window._ready.Task.Wait();
        return window;
    }

    /// <summary>Shows the window and brings it to the front. Safe from any thread.</summary>
    public void Show() => OnUi(OpenForm);

    /// <summary>Gives the page its next message, kept for a window not built yet. Safe from any thread.</summary>
    public void Post(string message) => OnUi(() =>
    {
        _latest = message;
        _form?.Post(message);
    });

    private void Run()
    {
        Control ui;
        Icon icon;
        NotifyIcon tray;
        try
        {
            // A fault on this thread is logged, never the WinForms dialog: nobody watches a plugin's
            // background window, and a modal box would freeze the tray icon until dismissed.
            // Thread scope: the application-wide form throws once any window exists, and the SDK
            // overlay may already have made one.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException, threadScope: true);
            Application.ThreadException += (_, e) => _log($"ItemTrackerPlugin: the window failed: {e.Exception.Message}");
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // An invisible control whose handle marshals calls onto this thread, window built or not.
            // Reading Handle is what creates it; a parentless control never shown would not otherwise.
            ui = new Control();
            _ = ui.Handle;

            icon = LoadIcon();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, (_, _) => OpenForm());
            tray = new NotifyIcon { Icon = icon, Text = "Item Tracker", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) OpenForm();
            };
            tray.BalloonTipClicked += (_, _) => OpenForm();
        }
        catch (Exception ex)
        {
            // No icon and no window, but the tracker and the in-game line carry on without them.
            _log($"ItemTrackerPlugin: the window could not start: {ex.Message}");
            _ready.TrySetResult();
            return;
        }

        (_ui, _icon, _tray) = (ui, icon, tray);
        _ready.TrySetResult();
        Application.Run();

        _form?.Dispose();
        tray.Visible = false;
        tray.Dispose();
        icon.Dispose();
        ui.Dispose();
    }

    private void OpenForm()
    {
        if (_form is null || _form.IsDisposed)
        {
            _form = new ViewForm(_viewDir, _userDataDir, _icon!, _log);
            _form.HiddenByUser += OnHidden;
            if (_latest is { } message) _form.Post(message);
        }

        _form.Open();
    }

    private void OnHidden()
    {
        if (_notice.TakeFirst()) _tray?.ShowBalloonTip(10_000, CloseNotice.Title, CloseNotice.Text, ToolTipIcon.Info);
    }

    /// <summary>Runs on the UI thread; dropped when the thread is gone or never started.</summary>
    private void OnUi(Action action)
    {
        var ui = _ui;
        if (ui is null || !ui.IsHandleCreated) return;

        try
        {
            ui.BeginInvoke(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The thread ended between the check and the call; a window going away has nothing left to show.
            _log($"ItemTrackerPlugin: the window is closed, a call to it was dropped: {ex.Message}");
        }
    }

    private static Icon LoadIcon()
    {
        using var stream = typeof(TrackerWindow).Assembly.GetManifestResourceStream(IconResource)
            ?? throw new InvalidOperationException($"The icon resource {IconResource} is missing.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        OnUi(() =>
        {
            _form?.CloseForGood();
            Application.ExitThread();
        });

        if (_thread.IsAlive && !_thread.Join(StopWait))
            _log("ItemTrackerPlugin: the window thread did not stop in time.");
    }
}
