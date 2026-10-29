using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace ItemTrackerPlugin.Window;

/// <summary>
/// The Item Tracker window: the read-only page of <c>view/</c> in a WebView2, fed by
/// <see cref="Post"/>. The page is served from a virtual host mapped to the folder beside the exe,
/// because ES modules do not load from <c>file://</c>; anything that would leave that host is refused.
/// Closing it by hand hides it (<see cref="HiddenByUser"/>); only <see cref="CloseForGood"/> ends it.
/// <para>
/// The page picks its colours from <c>prefers-color-scheme</c>, which WebView2 answers from the
/// <see cref="WindowTheme"/> (Auto follows Windows by itself); the form paints the background behind
/// the page while it loads and the title bar in the same colours.
/// </para>
/// </summary>
internal sealed class ViewForm : Form
{
    /// <summary>A reserved name (RFC 2606), so it can never be a real site.</summary>
    private const string Host = "item-tracker.example";
    private const string Origin = "https://" + Host + "/";

    internal const string RuntimeMissing =
        "Item Tracker needs the Microsoft Edge WebView2 Runtime to show its window. "
        + "Install it from Microsoft's website, then restart OCRX.";

    private readonly string _viewDir;
    private readonly string _userDataDir;
    private readonly Action<string> _log;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private bool _started;
    private bool _pageReady;
    private bool _closing;
    private string? _latest;
    private WindowTheme _theme;

    /// <summary>The page's own background colours (<c>--bg</c> in view.css), so loading shows no flash.</summary>
    private static readonly Color LightBack = Color.FromArgb(0xf8, 0xfa, 0xfc);
    private static readonly Color DarkBack = Color.FromArgb(0x0f, 0x17, 0x2a);

    public ViewForm(string viewDir, string userDataDir, Icon icon, WindowTheme theme, Action<string> log)
    {
        _viewDir = viewDir;
        _userDataDir = userDataDir;
        _log = log;

        Text = "Item Tracker";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1100, 720);
        MinimumSize = new Size(640, 400);
        Controls.Add(_web);

        _theme = theme;
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Recolours the window and its page now; no reload.</summary>
    public void SetTheme(WindowTheme theme)
    {
        _theme = theme;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var dark = WindowThemes.IsDark(_theme);
        BackColor = dark ? DarkBack : LightBack;
        ForeColor = dark ? Color.FromArgb(0xe2, 0xe8, 0xf0) : Color.FromArgb(0x0f, 0x17, 0x2a);
        _web.DefaultBackgroundColor = BackColor;
        if (IsHandleCreated) TitleBar.Apply(Handle, dark);
        if (_web.CoreWebView2 is { } core) core.Profile.PreferredColorScheme = ColorScheme(_theme);
    }

    private static CoreWebView2PreferredColorScheme ColorScheme(WindowTheme theme) => theme switch
    {
        WindowTheme.Light => CoreWebView2PreferredColorScheme.Light,
        WindowTheme.Dark => CoreWebView2PreferredColorScheme.Dark,
        _ => CoreWebView2PreferredColorScheme.Auto,
    };

    /// <summary>
    /// Windows switching its app mode. The page follows on its own; the title bar and the background
    /// behind the page are the form's to repaint. Raised on this thread, which subscribed.
    /// </summary>
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_theme == WindowTheme.System && e.Category == UserPreferenceCategory.General && !IsDisposed) ApplyTheme();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TitleBar.Apply(Handle, WindowThemes.IsDark(_theme));
    }

    /// <summary>Raised when the user closed the window, which only hid it.</summary>
    public event Action? HiddenByUser;

    /// <summary>Shows the window, starting the page on the first show, and brings it to the front.</summary>
    public void Open()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();

        if (!_started)
        {
            _started = true;
            _ = StartPageAsync();
        }
        else if (!_pageReady && _web.CoreWebView2 is { } core)
        {
            // The last load failed or the page's process died: reopening the window tries again.
            core.Reload();
        }
    }

    /// <summary>Keeps the latest message and sends it now if the page is up, or once it is.</summary>
    public void Post(string message)
    {
        _latest = message;
        if (!_pageReady) return;

        try
        {
            _web.CoreWebView2.PostWebMessageAsJson(message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // The page's process went away; the message is kept for the reload on the next open.
            _pageReady = false;
            _log($"ItemTrackerPlugin: the window's page did not take an update: {ex.Message}");
        }
    }

    /// <summary>Closes for real, for the end of the run.</summary>
    public void CloseForGood()
    {
        _closing = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Windows shutting down or the run ending close it for real; the user's close only hides it.
        if (!_closing && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            HiddenByUser?.Invoke();
            return;
        }

        base.OnFormClosing(e);
    }

    private async Task StartPageAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, _userDataDir);
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowPlainMessage(RuntimeMissing);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log($"ItemTrackerPlugin: the window could not start its page: {ex.Message}");
            ShowPlainMessage($"Item Tracker could not show its window: {ex.Message}");
            return;
        }

        try
        {
            Navigate(_web.CoreWebView2);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log($"ItemTrackerPlugin: the window could not load its page: {ex.Message}");
            ShowPlainMessage($"Item Tracker could not show its window: {ex.Message}");
        }
    }

    private void Navigate(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Profile.PreferredColorScheme = ColorScheme(_theme);
        core.SetVirtualHostNameToFolderMapping(Host, _viewDir, CoreWebView2HostResourceAccessKind.Deny);
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.ProcessFailed += (_, e) =>
        {
            _pageReady = false;
            _log($"ItemTrackerPlugin: the window's page process failed ({e.ProcessFailedKind}); reopening the window reloads it.");
        };
        core.NavigationCompleted += (_, e) =>
        {
            _pageReady = e.IsSuccess;
            if (!e.IsSuccess)
            {
                _log($"ItemTrackerPlugin: the window's page did not load ({e.WebErrorStatus}).");
                return;
            }

            if (_latest is { } message) core.PostWebMessageAsJson(message);
        };
        core.Navigate(Origin + "index.html");
    }

    private void ShowPlainMessage(string text)
    {
        Controls.Remove(_web);
        Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(24),
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A static event: left subscribed, it would keep the form alive for the rest of the run.
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}
