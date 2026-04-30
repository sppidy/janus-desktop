using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NEON.Trader.Desktop.Services;

namespace NEON.Trader.Desktop.Views;

public sealed partial class DashboardPage : Page
{
    private readonly AppServices _services = App.Services;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };

    public DashboardPage()
    {
        InitializeComponent();
        // With NavigationCacheMode=Required, the page instance is kept alive
        // across nav so we only want the timer running while it's actually
        // on screen. Start on Loaded, stop on Unloaded, and fire an immediate
        // refresh each time the user comes back.
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var status = await _services.Api.GetStatusAsync(cts.Token);

            var sum = status.Summary;
            TotalValueText.Text = Money(sum?.TotalValue);
            CashText.Text       = Money(sum?.Cash);
            ReturnText.Text     = Pct(sum?.TotalReturnPct);
            ReturnText.Foreground = (sum?.TotalReturnPct ?? 0) >= 0
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["UpBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DownBrush"];
            RealizedText.Text   = Money(sum?.RealizedPnl);
            PosCountText.Text   = (status.Positions?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
            PortfolioText.Text  = status.Portfolio ?? "—";
            AutoStateText.Text  = status.Autopilot?.Running == true ? "RUNNING" : "STOPPED";
            AutoStateText.Foreground = status.Autopilot?.Running == true
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["UpBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextMuteBrush"];

            PositionsList.ItemsSource = status.Positions;

            // Reverse trades so newest are first
            var trades = status.RecentTrades ?? new();
            trades.Reverse();
            TradesList.ItemsSource = trades;

            ErrorText.Text = "";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private static string Money(double? v) =>
        v is null ? "—" : "₹" + v.Value.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));

    private static string Pct(double? v) =>
        v is null ? "—" : (v.Value >= 0 ? "+" : "") + v.Value.ToString("F2") + "%";

    private async void AutoStart_Click(object sender, object e)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _services.Api.StartAutopilotAsync(cts.Token);
            (App.MainWindow as MainWindow)?.Toast("Autopilot started");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            (App.MainWindow as MainWindow)?.Toast(ex.Message, error: true);
        }
    }

    private async void AutoStop_Click(object sender, object e)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _services.Api.StopAutopilotAsync(cts.Token);
            (App.MainWindow as MainWindow)?.Toast("Autopilot stopped");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            (App.MainWindow as MainWindow)?.Toast(ex.Message, error: true);
        }
    }
}
