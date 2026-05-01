using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Janus.Desktop.Models;
using Janus.Desktop.Services;

namespace Janus.Desktop.Views;

public sealed partial class ChartsPage : Page
{
    private readonly AppServices _services = App.Services;
    private string _symbol = "";
    private string _tf = "1h";
    private List<string> _watchlist = new();
    private List<Candle> _currentCandles = new();

    private readonly HashSet<string> _activeInd = new() { "vol", "rsi" };

    private static readonly SKColor Neon = SKColor.Parse("b4ff00");
    private static readonly SKColor Up = SKColor.Parse("3ddc97");
    private static readonly SKColor Down = SKColor.Parse("ff4d6d");
    private static readonly SKColor TextColor = SKColor.Parse("7a8a7e");
    private static readonly SKColor GridColor = SKColor.Parse("1e2730");

    private bool _loadedOnce;

    public ChartsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            // With NavigationCacheMode=Required the instance is retained.
            // Rebuilding the chart on every return would wipe the user's zoom
            // and scroll position — only do the first-time init here.
            if (_loadedOnce) return;
            _loadedOnce = true;
            try
            {
                await LoadWatchlistAsync();
                if (_watchlist.Count > 0 && string.IsNullOrEmpty(_symbol))
                {
                    _symbol = _watchlist[0];
                    SymbolBox.Text = _symbol;
                }
                await LoadChartAsync();
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Charts init failed: " + ex.Message;
            }
        };
    }

    private async System.Threading.Tasks.Task LoadWatchlistAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var status = await _services.Api.GetStatusAsync(cts.Token);
            _watchlist = status.Watchlist ?? new();
        }
        catch { _watchlist = new(); }
    }

    private void SymbolBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var q = sender.Text.Trim().ToUpperInvariant();
            sender.ItemsSource = _watchlist
                .Where(s => s.ToUpperInvariant().Contains(q))
                .Take(10)
                .ToList();
        }
    }

    private async void SymbolBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        _symbol = args.SelectedItem?.ToString()?.Trim().ToUpperInvariant() ?? _symbol;
        sender.Text = _symbol;
        await LoadChartAsync();
    }

    private async void SymbolBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var q = (args.QueryText ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(q)) return;
        _symbol = q;
        await LoadChartAsync();
    }

    private async void Tf_Click(object sender, object e)
    {
        if (sender is Button btn && btn.Tag is string tf)
        {
            _tf = tf;
            // Swap styles to visually mark active
            foreach (var child in ((StackPanel)btn.Parent).Children.OfType<Button>())
                child.Style = (Style)Application.Current.Resources["GhostButton"];
            btn.Style = (Style)Application.Current.Resources["NeonButton"];
            await LoadChartAsync();
        }
    }

    private async void Ind_Click(object sender, object e)
    {
        if (sender is ToggleButton t && t.Tag is string key)
        {
            if (t.IsChecked == true) _activeInd.Add(key);
            else _activeInd.Remove(key);
            if (key == "rsi") RsiBorder.Visibility = _activeInd.Contains("rsi") ? Visibility.Visible : Visibility.Collapsed;
            await LoadChartAsync();
        }
    }

    private async System.Threading.Tasks.Task LoadChartAsync()
    {
        if (string.IsNullOrWhiteSpace(_symbol)) return;
        LegendText.Text = $"{_symbol} · loading...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var resp = await _services.Api.GetCandlesAsync(_symbol, _tf, 500, cts.Token);
            var candles = resp.Candles ?? new();
            if (candles.Count == 0)
            {
                LegendText.Text = $"{_symbol} · no data";
                MainChart.Series = Array.Empty<ISeries>();
                RsiChart.Series  = Array.Empty<ISeries>();
                return;
            }

            RenderMain(candles);
            if (_activeInd.Contains("rsi")) RenderRsi(candles);
            UpdateLegend(candles);
            HookAxisSync();
            ErrorText.Text = "";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            LegendText.Text = $"{_symbol} · error";
        }
    }

    // When the user zooms/pans the main chart, mirror the X-axis range onto
    // the RSI sub-pane so the two always line up — TradingView does the same.
    private bool _syncingAxes;
    private System.ComponentModel.PropertyChangedEventHandler? _mainAxisChanged;
    private System.ComponentModel.PropertyChangedEventHandler? _rsiAxisChanged;
    private void HookAxisSync()
    {
        if (MainChart.XAxes is null || RsiChart.XAxes is null) return;
        var mainAxis = MainChart.XAxes.FirstOrDefault();
        var rsiAxis  = RsiChart.XAxes.FirstOrDefault();
        if (mainAxis is not System.ComponentModel.INotifyPropertyChanged mainInpc ||
            rsiAxis  is not System.ComponentModel.INotifyPropertyChanged rsiInpc) return;

        if (_mainAxisChanged is not null) mainInpc.PropertyChanged -= _mainAxisChanged;
        if (_rsiAxisChanged  is not null) rsiInpc.PropertyChanged  -= _rsiAxisChanged;

        _mainAxisChanged = (_, e) =>
        {
            if (_syncingAxes || (e.PropertyName != "MinLimit" && e.PropertyName != "MaxLimit")) return;
            _syncingAxes = true;
            try
            {
                var src = (Axis)mainAxis;
                var dst = (Axis)rsiAxis;
                dst.MinLimit = src.MinLimit;
                dst.MaxLimit = src.MaxLimit;
            }
            finally { _syncingAxes = false; }
        };
        _rsiAxisChanged = (_, e) =>
        {
            if (_syncingAxes || (e.PropertyName != "MinLimit" && e.PropertyName != "MaxLimit")) return;
            _syncingAxes = true;
            try
            {
                var src = (Axis)rsiAxis;
                var dst = (Axis)mainAxis;
                dst.MinLimit = src.MinLimit;
                dst.MaxLimit = src.MaxLimit;
            }
            finally { _syncingAxes = false; }
        };
        mainInpc.PropertyChanged += _mainAxisChanged;
        rsiInpc.PropertyChanged  += _rsiAxisChanged;
    }

    private void UpdateLegend(List<Candle> candles)
    {
        var first = candles[0];
        var last = candles[^1];
        var chg = ((last.Close - first.Close) / first.Close) * 100;
        LegendText.Text = $"{_symbol} · {_tf} · LAST {last.Close:F2} · {(chg >= 0 ? "+" : "")}{chg:F2}%";
    }

    private void RenderMain(List<Candle> candles)
    {
        _currentCandles = candles;

        // Index-based (FinancialPointI) so every bar is adjacent — no gaps for
        // overnight / weekend / holidays. TradingView renders the same way:
        // the axis is a bar index, labels are derived from each bar's timestamp.
        var financial = new ObservableCollection<FinancialPointI>();
        foreach (var c in candles)
            financial.Add(new FinancialPointI(c.High, c.Open, c.Close, c.Low));

        var series = new List<ISeries>
        {
            new CandlesticksSeries<FinancialPointI>
            {
                Values = financial,
                UpStroke = new SolidColorPaint(Up) { StrokeThickness = 1 },
                UpFill   = new SolidColorPaint(Up.WithAlpha(220)),
                DownStroke = new SolidColorPaint(Down) { StrokeThickness = 1 },
                DownFill   = new SolidColorPaint(Down.WithAlpha(220)),
                Name = _symbol,
            }
        };

        // Overlays: index-aligned line series.
        var closes = candles.Select(c => c.Close).ToList();

        if (_activeInd.Contains("ma20"))
            series.Add(IndexLine(Indicators.Sma(closes, 20), SKColor.Parse("00f0ff"), "MA20"));

        if (_activeInd.Contains("ema50"))
            series.Add(IndexLine(Indicators.Ema(closes, 50), SKColor.Parse("ffb300"), "EMA50"));

        if (_activeInd.Contains("bb"))
        {
            var (up, mid, lo) = Indicators.Bollinger(closes, 20, 2);
            series.Add(IndexLine(up,  Neon.WithAlpha(200), "BB up",  1));
            series.Add(IndexLine(mid, Neon.WithAlpha(120), "BB mid", 1));
            series.Add(IndexLine(lo,  Neon.WithAlpha(200), "BB lo",  1));
        }

        MainChart.Series = series;
        MainChart.XAxes = new[] { IndexTimeAxis(candles) };
        MainChart.YAxes = new[] { PriceAxis() };
    }

    private void RenderRsi(List<Candle> candles)
    {
        var closes = candles.Select(c => c.Close).ToList();
        var rsi = Indicators.Rsi(closes, 14);

        // Align to bar index so RSI scrolls in lockstep with the price pane.
        var pts = new ObservableCollection<ObservablePoint>();
        for (int i = 0; i < rsi.Length; i++)
            if (rsi[i] is double v) pts.Add(new ObservablePoint(i, v));

        RsiChart.Series = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Values = pts,
                Stroke = new SolidColorPaint(Neon) { StrokeThickness = 1 },
                GeometrySize = 0,
                Fill = null,
                Name = "RSI14",
            },
        };
        RsiChart.XAxes = new[] { IndexTimeAxis(candles) };
        RsiChart.YAxes = new[]
        {
            new Axis
            {
                MinLimit = 0, MaxLimit = 100,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(TextColor),
                SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 1 },
            }
        };
    }

    /// <summary>
    /// Build an index-based X axis whose labels are formatted dates looked up
    /// from the bar array — identical behaviour to TradingView's time axis
    /// (no gaps, labels still read like real dates).
    /// </summary>
    private static Axis IndexTimeAxis(List<Candle> candles) => new()
    {
        MinStep = 1,
        UnitWidth = 1,
        Labeler = value =>
        {
            int idx = (int)Math.Round(value);
            if (idx < 0 || idx >= candles.Count) return "";
            // Backend returns tz-naive strings already in the exchange's local
            // time. Don't re-interpret or convert — display verbatim.
            var t = ParseTime(candles[idx].Time);
            return t.ToString(t.Hour == 0 && t.Minute == 0 ? "MMM d" : "MMM d HH:mm");
        },
        TextSize = 10,
        LabelsPaint = new SolidColorPaint(TextColor),
        SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 1 },
    };

    private static Axis PriceAxis() => new()
    {
        TextSize = 10,
        LabelsPaint = new SolidColorPaint(TextColor),
        SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 1 },
    };

    /// <summary>Index-aligned indicator line (overlays onto the candle chart).</summary>
    private static LineSeries<ObservablePoint> IndexLine(
        double?[] values, SKColor color, string name, float thickness = 1.2f)
    {
        var pts = new ObservableCollection<ObservablePoint>();
        for (int i = 0; i < values.Length; i++)
            if (values[i] is double v) pts.Add(new ObservablePoint(i, v));
        return new LineSeries<ObservablePoint>
        {
            Values = pts,
            Stroke = new SolidColorPaint(color) { StrokeThickness = thickness },
            GeometrySize = 0,
            Fill = null,
            Name = name,
        };
    }

    /// <summary>
    /// Parse the backend's tz-naive ISO string as-is (no TZ assumption) so we
    /// don't introduce a phantom UTC↔IST shift. yfinance NSE candles are in
    /// exchange local time already; we just display them verbatim.
    /// </summary>
    private static DateTime ParseTime(string? s)
    {
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return t;
        return DateTime.Now;
    }
}
