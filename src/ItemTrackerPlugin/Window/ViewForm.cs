using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ItemTrackerPlugin.Window;

/// <summary>
/// The Item Tracker window: the read-only page of <c>view/</c> in a WebView2, fed by
/// <see cref="Post"/>. The page is served from a virtual host mapped to the folder beside the exe,
/// because ES modules do not load from <c>file://</c>; anything that would leave that host is refused.
/// Closing it by hand hides it (<see cref="HiddenByUser"/>); only <see cref="CloseForGood"/> ends it.
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

    public ViewForm(string viewDir, string userDataDir, Icon icon, Action<string> log)
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
    }

    /// <summary>Raised when the user closed the window, which only hid it.</summary>
    public event Action? HiddenByUser;

    /// <summary>Shows the window, starting the page on the first show, and brings it to the front.</summary>
    public void Open()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();

        if (_started) return;
        _started = true;
        _ = StartPageAsync();
    }

    /// <summary>Keeps the latest message and sends it now if the page is up, or once it is.</summary>
    public void Post(string message)
    {
        _latest = message;
        if (_pageReady) _web.CoreWebView2.PostWebMessageAsJson(message);
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

        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.SetVirtualHostNameToFolderMapping(Host, _viewDir, CoreWebView2HostResourceAccessKind.Deny);
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
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
        if (disposing) _web.Dispose();
        base.Dispose(disposing);
    }
}
