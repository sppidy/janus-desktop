using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Janus.Desktop.Models;
using Janus.Desktop.Services;
using Janus.Desktop.Views;
using Windows.UI;

namespace Janus.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly AppServices _services = App.Services;
    private readonly DispatcherTimer _statusTimer = new();

    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
        ConfigureTitleBar();
        RefreshBackendCombo();
        Nav.SelectedItem = Nav.MenuItems[0];
        NavigateTo("dash");

        _statusTimer.Interval = TimeSpan.FromSeconds(30);
        _statusTimer.Tick += async (_, _) => await RefreshConnectionAsync();
        _statusTimer.Start();
        _ = RefreshConnectionAsync();
    }

    /// <summary>Sets the OS title-bar / Alt-Tab icon to the bundled neon.ico.</summary>
    private void SetWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "neon.ico");
            if (File.Exists(iconPath))
                AppWindow?.SetIcon(iconPath);
        }
        catch { /* non-fatal — falls back to the default Windows icon */ }
    }

    /// <summary>
    /// Extend our content into the OS title-bar area and tell the OS that the
    /// AppTitleBar Grid is the draggable region. Window controls (min/max/close)
    /// continue to be rendered by the OS in the top-right; we just colour them
    /// to match the neon theme.
    /// </summary>
    private void ConfigureTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            Title = "NEON.TRADER";

            if (AppWindow?.TitleBar is { } tb)
            {
                var bg     = (Color)Application.Current.Resources["Bg1Color"];
                var bgHot  = (Color)Application.Current.Resources["Bg2Color"];
                var text   = (Color)Application.Current.Resources["TextColor"];
                var muted  = (Color)Application.Current.Resources["TextMuteColor"];

                tb.BackgroundColor               = bg;
                tb.InactiveBackgroundColor       = bg;
                tb.ButtonBackgroundColor         = Colors.Transparent;
                tb.ButtonInactiveBackgroundColor = Colors.Transparent;
                tb.ButtonForegroundColor         = text;
                tb.ButtonInactiveForegroundColor = muted;
                tb.ButtonHoverBackgroundColor    = bgHot;
                tb.ButtonHoverForegroundColor    = text;
                tb.ButtonPressedBackgroundColor  = bgHot;
                tb.ButtonPressedForegroundColor  = text;
            }
        }
        catch { /* old windows builds may not support it — fall back silently */ }
    }

    private void RefreshBackendCombo()
    {
        BackendCombo.Items.Clear();
        foreach (var p in _services.Settings.Profiles)
            BackendCombo.Items.Add(p);
        BackendCombo.DisplayMemberPath = nameof(BackendProfile.Name);
        BackendCombo.SelectedItem = _services.Settings.ActiveProfile;
    }

    private void BackendCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackendCombo.SelectedItem is BackendProfile p)
        {
            _services.Settings.SetActive(p.Id);
            _ = RefreshConnectionAsync();
            // re-navigate to force page reload with new profile
            if (Nav.SelectedItem is NavigationViewItem nvi && nvi.Tag is string tag)
                NavigateTo(tag);
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            NavigateTo(tag);
    }

    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
            NavigateTo(tag);
    }

    private void NavigateTo(string tag)
    {
        Type? page = tag switch
        {
            "dash"  => typeof(DashboardPage),
            "watch" => typeof(WatchlistPage),
            "port"  => typeof(PortfolioPage),
            "chart" => typeof(ChartsPage),
            "strat" => typeof(StrategyPage),
            "scan"  => typeof(ScannerPage),
            "agent" => typeof(AgentPage),
            "logs"  => typeof(LogsPage),
            "set"   => typeof(SettingsPage),
            _ => null,
        };
        if (page is not null)
            ContentFrame.Navigate(page);
    }

    private async Task RefreshConnectionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var status = await _services.Api.GetStatusAsync(cts.Token);
            SetConnection(ok: status.Status == "ok");
            try
            {
                var regime = await _services.Api.GetRegimeAsync(cts.Token);
                RegimeText.Text = regime.Regime ?? "—";
            }
            catch { RegimeText.Text = "—"; }
        }
        catch
        {
            SetConnection(ok: false);
            RegimeText.Text = "—";
        }
    }

    private void SetConnection(bool ok)
    {
        ConnDot.Fill = (Brush)(ok
            ? Application.Current.Resources["NeonBrush"]
            : Application.Current.Resources["DownBrush"]);
        ConnText.Text = ok ? "ONLINE" : "OFFLINE";
    }

    public void Toast(string message, bool error = false)
    {
        ToastBar.Title = error ? "Error" : "";
        ToastBar.Message = message;
        ToastBar.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        ToastBar.IsOpen = true;
    }
}
