using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Janus.Desktop.Models;
using Janus.Desktop.Services;

namespace Janus.Desktop.Views;

public sealed partial class StrategyPage : Page
{
    private readonly AppServices _services = App.Services;
    private readonly ObservableCollection<StratRule> _rules = new();

    // Static so XAML x:Bind can see them on ComboBox ItemsSource.
    public static IReadOnlyList<string> IndicatorList { get; } = new[]
    {
        "close", "open", "high", "low",
        "sma20", "sma50", "ema20", "ema50",
        "rsi14", "bb_up", "bb_lo", "bb_mid",
        "atr14", "volume",
    };

    public static IReadOnlyList<string> OpList { get; } = new[]
    {
        ">", ">=", "<", "<=", "cross_above", "cross_below",
    };

    public static IReadOnlyList<StratAction> ActionList { get; } =
        (StratAction[])Enum.GetValues(typeof(StratAction));

    private static readonly Dictionary<string, (string Name, StratRule[] Rules)> Templates = new()
    {
        ["ema_cross"] = ("EMA Cross", new[]
        {
            new StratRule { Indicator = "ema20", Op = "cross_above", Value = "ema50", Action = StratAction.BUY },
            new StratRule { Indicator = "ema20", Op = "cross_below", Value = "ema50", Action = StratAction.EXIT },
        }),
        ["rsi_mr"] = ("RSI Mean Reversion", new[]
        {
            new StratRule { Indicator = "rsi14", Op = "<", Value = "30", Action = StratAction.BUY },
            new StratRule { Indicator = "rsi14", Op = ">", Value = "70", Action = StratAction.EXIT },
        }),
        ["bb_breakout"] = ("Bollinger Breakout", new[]
        {
            new StratRule { Indicator = "close", Op = "cross_above", Value = "bb_up",  Action = StratAction.BUY },
            new StratRule { Indicator = "close", Op = "cross_below", Value = "bb_mid", Action = StratAction.EXIT },
        }),
        ["sma_trend"] = ("SMA Trend", new[]
        {
            new StratRule { Indicator = "close", Op = ">", Value = "sma50", Action = StratAction.BUY },
            new StratRule { Indicator = "close", Op = "<", Value = "sma50", Action = StratAction.EXIT },
        }),
    };

    public StrategyPage()
    {
        InitializeComponent();
        RulesList.ItemsSource = _rules;   // ItemsControl + TwoWay bindings on each row

        Loaded += async (_, _) =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                var s = await _services.Api.GetStatusAsync(cts.Token);
                if (string.IsNullOrEmpty(SymbolBox.Text) && s.Watchlist?.Count > 0)
                    SymbolBox.Text = s.Watchlist[0];
            }
            catch { }
        };
    }

    private void AddRule_Click(object sender, object e) =>
        _rules.Add(new StratRule { Indicator = "close", Op = ">", Value = "sma20", Action = StratAction.BUY });

    private void ClearRules_Click(object sender, object e) => _rules.Clear();

    private void RemoveRule_Click(object sender, object e)
    {
        if (sender is Button b && b.Tag is StratRule rule)
            _rules.Remove(rule);
    }

    private void Template_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not ComboBoxItem item) return;
        var key = (item.Tag as string) ?? "";
        if (!Templates.TryGetValue(key, out var tpl)) return;
        NameBox.Text = tpl.Name;
        _rules.Clear();
        foreach (var r in tpl.Rules) _rules.Add(new StratRule
        {
            Indicator = r.Indicator, Op = r.Op, Value = r.Value, Action = r.Action,
        });
    }

    private async void RunBacktest_Click(object sender, object e)
    {
        ErrorText.Text = "";
        if (_rules.Count == 0)
        {
            (App.MainWindow as MainWindow)?.Toast("add at least one rule", error: true);
            return;
        }
        var sym = SymbolBox.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(sym))
        {
            (App.MainWindow as MainWindow)?.Toast("pick a symbol", error: true);
            return;
        }
        var tf = (TfBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "1h";

        double ParseBox(TextBox b, double fallback) =>
            double.TryParse(b.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        var settings = new BacktestSettings
        {
            Capital = ParseBox(CapitalBox, 100_000),
            PosPct  = ParseBox(PosBox,     100),
            FeePct  = ParseBox(FeeBox,     0.05),
            SlPct   = ParseBox(SlBox,      3),
            TpPct   = ParseBox(TpBox,      6),
            SlipPct = ParseBox(SlipBox,    0.03),
        };

        EquityState.Text = "loading candles...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var resp = await _services.Api.GetCandlesAsync(sym, tf, 500, cts.Token);
            var candles = resp.Candles ?? new();
            var result = Backtester.Run(candles, _rules.ToList(), settings);
            RenderResult(result, sym, tf);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            EquityState.Text = "error";
        }
    }

    private void RenderResult(BacktestResult result, string sym, string tf)
    {
        EquityState.Text = $"{result.Total} trades · {sym} {tf}";

        // Equity curve
        var pts = new ObservableCollection<DateTimePoint>();
        foreach (var e in result.Equity) pts.Add(new DateTimePoint(e.time, e.equity));
        EquityChart.Series = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Values = pts,
                Stroke = new SolidColorPaint(SKColor.Parse("b4ff00")) { StrokeThickness = 1.5f },
                GeometrySize = 0,
                Fill = new SolidColorPaint(SKColor.Parse("b4ff00").WithAlpha(35)),
                Name = "Equity",
            }
        };
        EquityChart.XAxes = new[]
        {
            new Axis
            {
                Labeler = v => { try { return new DateTime((long)v).ToString("MMM dd"); } catch { return ""; } },
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(SKColor.Parse("7a8a7e")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("1e2730")) { StrokeThickness = 1 },
            }
        };
        EquityChart.YAxes = new[]
        {
            new Axis
            {
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(SKColor.Parse("7a8a7e")),
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("1e2730")) { StrokeThickness = 1 },
            }
        };

        // KPIs
        KpiRepeater.ItemsSource = BuildKpis(result);

        // Trade list
        TradeCount.Text = $"{result.Total} trades";
        TradesList.ItemsSource = result.Trades.AsEnumerable().Reverse().ToList();
    }

    private static IEnumerable<UIElement> BuildKpis(BacktestResult r)
    {
        Microsoft.UI.Xaml.Media.Brush Up() => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["UpBrush"];
        Microsoft.UI.Xaml.Media.Brush Down() => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DownBrush"];
        Microsoft.UI.Xaml.Media.Brush Text() => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"];

        UIElement Kpi(string label, string value, Microsoft.UI.Xaml.Media.Brush color)
        {
            var border = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["Bg2Brush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                Width = 130,
            };
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily((string)Application.Current.Resources["MonoFont"]),
                FontSize = 9,
                CharacterSpacing = 250,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextMuteBrush"],
            });
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontFamily = new FontFamily((string)Application.Current.Resources["SansFont"]),
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = color,
            });
            border.Child = stack;
            return border;
        }

        string Money(double v) => v.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
        string Pct(double v) => (v >= 0 ? "+" : "") + v.ToString("F2") + "%";

        yield return Kpi("NET P&L", "₹" + Money(r.NetPnl),
            r.NetPnl >= 0 ? Up() : Down());
        yield return Kpi("RETURN", Pct(r.ReturnPct),
            r.ReturnPct >= 0 ? Up() : Down());
        yield return Kpi("TRADES", r.Total.ToString(), Text());
        yield return Kpi("WIN RATE", r.WinRate.ToString("F1") + "%",
            r.WinRate >= 50 ? Up() : Down());
        yield return Kpi("PROFIT FACTOR",
            double.IsPositiveInfinity(r.ProfitFactor) ? "∞" : r.ProfitFactor.ToString("F2"),
            r.ProfitFactor >= 1 ? Up() : Down());
        yield return Kpi("AVG WIN", "₹" + Money(r.AvgWin), Up());
        yield return Kpi("AVG LOSS", "₹" + Money(r.AvgLoss), Down());
        yield return Kpi("EXPECTANCY", "₹" + Money(r.Expectancy),
            r.Expectancy >= 0 ? Up() : Down());
        yield return Kpi("MAX DD", r.MaxDd.ToString("F2") + "%", Down());
        yield return Kpi("MAX CONS LOSS", r.MaxConsLoss.ToString(), Text());
        yield return Kpi("SHARPE", r.Sharpe.ToString("F2"),
            r.Sharpe >= 0 ? Up() : Down());
        yield return Kpi("FINAL EQUITY", "₹" + Money(r.FinalEquity),
            r.NetPnl >= 0 ? Up() : Down());
    }

    // ── AI strategy generator ──

    private async void GenerateAi_Click(object sender, object e)
    {
        var prompt = AiPrompt.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            (App.MainWindow as MainWindow)?.Toast("describe what you want", error: true);
            return;
        }

        AiState.Text = "generating...";
        AiOutput.Text = "";
        AiOutput.Visibility = Visibility.Collapsed;

        string indicators = string.Join(", ", IndicatorList.Select(s => $"\"{s}\""));
        string ops        = string.Join(", ", OpList.Select(s => $"\"{s}\""));
        string actions    = string.Join(", ", ActionList.Select(s => $"\"{s}\""));

        string system =
            "You are a quant strategy generator. Given a natural-language request, " +
            "return ONE JSON object and NOTHING else. Schema:\n" +
            "{\n" +
            "  \"name\": string,\n" +
            "  \"rules\": [\n" +
            "    { \"indicator\": one of [" + indicators + "],\n" +
            "      \"op\": one of [" + ops + "],\n" +
            "      \"value\": number as string OR one of the indicators,\n" +
            "      \"action\": one of [" + actions + "] }\n" +
            "  ]\n" +
            "}\n" +
            "Only output the JSON. No prose, no markdown, no code fences.";

        string fullPrompt = $"{system}\n\nREQUEST: {prompt}";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var start = await _services.Api.StartChatAsync(
                new ChatRequest { Message = fullPrompt, History = new() },
                cts.Token);
            if (string.IsNullOrEmpty(start.JobId))
                throw new InvalidOperationException("chat backend returned no job id");

            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
            while (DateTime.UtcNow < deadline)
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
                var job = await _services.Api.GetChatJobAsync(start.JobId!, cts.Token);
                if (job.Status == "completed")
                {
                    string reply = (job.Reply ?? "").Trim();
                    AiOutput.Text = reply;
                    AiOutput.Visibility = Visibility.Visible;
                    if (TryApplyStrategy(reply, out int applied))
                    {
                        AiState.Text = $"loaded {applied} rules";
                        (App.MainWindow as MainWindow)?.Toast("Strategy generated");
                    }
                    else
                    {
                        AiState.Text = "parse failed — see raw output below";
                    }
                    return;
                }
                if (job.Status == "failed")
                {
                    AiState.Text = "ai failed";
                    AiOutput.Text = job.Error ?? "error";
                    AiOutput.Visibility = Visibility.Visible;
                    return;
                }
            }
            AiState.Text = "timeout";
        }
        catch (Exception ex)
        {
            AiState.Text = "error";
            AiOutput.Text = ex.Message;
            AiOutput.Visibility = Visibility.Visible;
        }
    }

    private bool TryApplyStrategy(string reply, out int applied)
    {
        applied = 0;
        var json = ExtractJson(reply);
        if (string.IsNullOrEmpty(json)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                NameBox.Text = nameEl.GetString() ?? NameBox.Text;

            if (!root.TryGetProperty("rules", out var rulesEl) || rulesEl.ValueKind != JsonValueKind.Array)
                return false;

            var newRules = new List<StratRule>();
            foreach (var item in rulesEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string ind = item.TryGetProperty("indicator", out var i) ? (i.GetString() ?? "close") : "close";
                string op  = item.TryGetProperty("op",        out var o) ? (o.GetString() ?? ">")      : ">";
                string val = item.TryGetProperty("value",     out var v)
                    ? (v.ValueKind == JsonValueKind.Number ? v.GetRawText() : (v.GetString() ?? ""))
                    : "";
                string actS = item.TryGetProperty("action", out var a) ? (a.GetString() ?? "BUY") : "BUY";
                var act = Enum.TryParse<StratAction>(actS.Trim(), ignoreCase: true, out var parsed)
                    ? parsed : StratAction.BUY;
                newRules.Add(new StratRule { Indicator = ind, Op = op, Value = val, Action = act });
            }

            if (newRules.Count == 0) return false;
            _rules.Clear();
            foreach (var r in newRules) _rules.Add(r);
            applied = newRules.Count;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractJson(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        // Strip ```json ... ``` fences if present.
        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var after = text.IndexOf('\n', fenceStart);
            var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);
            if (after > 0 && fenceEnd > after)
                text = text.Substring(after + 1, fenceEnd - after - 1);
        }
        int first = text.IndexOf('{');
        int last  = text.LastIndexOf('}');
        if (first < 0 || last < 0 || last <= first) return null;
        return text.Substring(first, last - first + 1);
    }
}
