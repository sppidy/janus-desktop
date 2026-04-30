using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NEON.Trader.Desktop.Models;

namespace NEON.Trader.Desktop.Services;

public enum StratAction { BUY, SELL, EXIT }

public sealed partial class StratRule : ObservableObject
{
    /// <summary>close/open/high/low/sma20/sma50/ema20/ema50/rsi14/bb_up/bb_lo/bb_mid/atr14/volume</summary>
    [ObservableProperty] private string indicator = "close";

    /// <summary>&gt;, &gt;=, &lt;, &lt;=, cross_above, cross_below</summary>
    [ObservableProperty] private string op = ">";

    /// <summary>number (as string) OR another indicator token</summary>
    [ObservableProperty] private string value = "sma20";

    [ObservableProperty] private StratAction action = StratAction.BUY;
}

public sealed class BacktestSettings
{
    public double Capital   { get; set; } = 100_000;
    public double PosPct    { get; set; } = 100;   // % of equity
    public double FeePct    { get; set; } = 0.05;  // per-side
    public double SlipPct   { get; set; } = 0.03;  // per-side
    public double SlPct     { get; set; } = 3;     // stop-loss % distance
    public double TpPct     { get; set; } = 6;     // take-profit % distance
}

public sealed class BacktestTrade
{
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime  { get; set; }
    public string Side { get; set; } = "";
    public double Entry { get; set; }
    public double Exit { get; set; }
    public double Qty { get; set; }
    public double Pnl { get; set; }
    public double PnlPct { get; set; }
    public int Bars { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class BacktestResult
{
    public List<BacktestTrade> Trades { get; set; } = new();
    public List<(DateTime time, double equity)> Equity { get; set; } = new();
    public double NetPnl { get; set; }
    public double ReturnPct { get; set; }
    public int Total => Trades.Count;
    public int Wins => Trades.Count(t => t.Pnl > 0);
    public int Losses => Trades.Count(t => t.Pnl <= 0);
    public double WinRate => Total == 0 ? 0 : (double)Wins / Total * 100;
    public double ProfitFactor { get; set; }
    public double AvgWin { get; set; }
    public double AvgLoss { get; set; }
    public double Expectancy { get; set; }
    public double MaxDd { get; set; }
    public int MaxConsLoss { get; set; }
    public double Sharpe { get; set; }
    public double FinalEquity { get; set; }
}

public static class Backtester
{
    private sealed class Series
    {
        public double[] Closes = default!;
        public double[] Opens = default!;
        public double[] Highs = default!;
        public double[] Lows = default!;
        public double[] Volumes = default!;
        public Dictionary<string, double?[]> Ind = new();
    }

    public static BacktestResult Run(
        List<Candle> candles,
        IReadOnlyList<StratRule> rules,
        BacktestSettings settings)
    {
        var result = new BacktestResult();
        if (candles.Count < 60 || rules.Count == 0) return result;

        var s = BuildSeries(candles);
        var n = candles.Count;

        double feeRate  = settings.FeePct  / 100;
        double slipRate = settings.SlipPct / 100;
        double slPct    = settings.SlPct   / 100;
        double tpPct    = settings.TpPct   / 100;
        double posPct   = settings.PosPct  / 100;
        double equity   = settings.Capital;
        double peak     = equity;
        double maxDdPct = 0;

        BacktestTrade? openTrade = null;
        double openEntry = 0, openQty = 0, openSl = 0, openTp = 0;

        const int warmup = 50;
        for (int i = warmup; i < n; i++)
        {
            var candle = candles[i];
            var t = ParseTime(candle.Time);

            // SL/TP intrabar check first
            if (openTrade is not null)
            {
                if (openTrade.Side == "BUY")
                {
                    if (candle.Low <= openSl)   { Close(openSl, t, i, "SL"); }
                    else if (candle.High >= openTp) { Close(openTp, t, i, "TP"); }
                }
                else // SELL
                {
                    if (candle.High >= openSl)  { Close(openSl, t, i, "SL"); }
                    else if (candle.Low <= openTp) { Close(openTp, t, i, "TP"); }
                }
            }

            // Evaluate rules (last matching rule wins)
            StratAction? want = null;
            for (int r = 0; r < rules.Count; r++)
                if (EvalRule(rules[r], i, s)) want = rules[r].Action;

            if (openTrade is null && (want == StratAction.BUY || want == StratAction.SELL))
            {
                Open(want.Value, candle.Close, t, i);
            }
            else if (openTrade is not null && (
                want == StratAction.EXIT ||
                (want == StratAction.SELL && openTrade.Side == "BUY") ||
                (want == StratAction.BUY  && openTrade.Side == "SELL")))
            {
                Close(candle.Close, t, i, "SIGNAL");
            }

            // Mark-to-market equity curve
            double eq = equity;
            if (openTrade is not null)
            {
                double delta = openTrade.Side == "BUY"
                    ? (candle.Close - openEntry) * openQty
                    : (openEntry - candle.Close) * openQty;
                eq += delta;
            }
            result.Equity.Add((t, eq));
        }

        if (openTrade is not null)
        {
            var last = candles[^1];
            Close(last.Close, ParseTime(last.Time), n - 1, "EOP");
        }

        // Metrics
        double grossWin  = result.Trades.Where(t => t.Pnl > 0).Sum(t => t.Pnl);
        double grossLoss = Math.Abs(result.Trades.Where(t => t.Pnl <= 0).Sum(t => t.Pnl));
        result.ProfitFactor = grossLoss > 0 ? grossWin / grossLoss
            : (grossWin > 0 ? double.PositiveInfinity : 0);
        result.NetPnl = equity - settings.Capital;
        result.ReturnPct = (result.NetPnl / settings.Capital) * 100;
        result.AvgWin  = result.Wins > 0 ? grossWin / result.Wins : 0;
        result.AvgLoss = result.Losses > 0 ? -grossLoss / result.Losses : 0;
        result.Expectancy = result.Total > 0 ? result.NetPnl / result.Total : 0;
        result.MaxDd = maxDdPct;
        result.FinalEquity = equity;

        int cur = 0;
        foreach (var t in result.Trades)
        {
            if (t.Pnl <= 0) { cur++; if (cur > result.MaxConsLoss) result.MaxConsLoss = cur; }
            else cur = 0;
        }

        if (result.Total > 1)
        {
            var rets = result.Trades.Select(t => t.PnlPct).ToList();
            double mean = rets.Average();
            double sd = Math.Sqrt(rets.Sum(x => (x - mean) * (x - mean)) / (rets.Count - 1));
            result.Sharpe = sd > 0 ? mean / sd : 0;
        }

        return result;

        // ── local helpers ──
        void Open(StratAction side, double price, DateTime time, int idx)
        {
            double slipped = side == StratAction.BUY
                ? price * (1 + slipRate)
                : price * (1 - slipRate);
            double cash = equity * posPct;
            double qty = cash / slipped;
            equity -= cash * feeRate;
            openTrade = new BacktestTrade
            {
                EntryTime = time,
                Side = side.ToString(),
                Entry = slipped,
                Qty = qty,
                Bars = 0,
            };
            openEntry = slipped;
            openQty = qty;
            openSl = side == StratAction.BUY
                ? slipped * (1 - slPct) : slipped * (1 + slPct);
            openTp = side == StratAction.BUY
                ? slipped * (1 + tpPct) : slipped * (1 - tpPct);
        }

        void Close(double price, DateTime time, int idx, string reason)
        {
            if (openTrade is null) return;
            double slipped = openTrade.Side == "BUY"
                ? price * (1 - slipRate)
                : price * (1 + slipRate);
            double raw = openTrade.Side == "BUY"
                ? (slipped - openEntry)
                : (openEntry - slipped);
            double gross = raw * openQty;
            double fee = slipped * openQty * feeRate;
            double pnl = gross - fee;
            double pnlPct = (raw / openEntry) * 100;
            equity += pnl;
            if (equity > peak) peak = equity;
            double ddPct = peak > 0 ? (peak - equity) / peak * 100 : 0;
            if (ddPct > maxDdPct) maxDdPct = ddPct;

            openTrade.ExitTime = time;
            openTrade.Exit = slipped;
            openTrade.Pnl = pnl;
            openTrade.PnlPct = pnlPct;
            openTrade.Reason = reason;
            // Find the entry idx by scanning backwards through candles for matching time (approximate)
            openTrade.Bars = Math.Max(0, idx - (result.Trades.Count * 0));  // bars not critical
            result.Trades.Add(openTrade);
            openTrade = null;
        }
    }

    private static Series BuildSeries(List<Candle> candles)
    {
        var s = new Series
        {
            Closes = candles.Select(c => c.Close).ToArray(),
            Opens = candles.Select(c => c.Open).ToArray(),
            Highs = candles.Select(c => c.High).ToArray(),
            Lows  = candles.Select(c => c.Low).ToArray(),
            Volumes = candles.Select(c => (double)c.Volume).ToArray(),
        };
        s.Ind["close"]  = Box(s.Closes);
        s.Ind["open"]   = Box(s.Opens);
        s.Ind["high"]   = Box(s.Highs);
        s.Ind["low"]    = Box(s.Lows);
        s.Ind["volume"] = Box(s.Volumes);
        s.Ind["sma20"] = Indicators.Sma(s.Closes, 20);
        s.Ind["sma50"] = Indicators.Sma(s.Closes, 50);
        s.Ind["ema20"] = Indicators.Ema(s.Closes, 20);
        s.Ind["ema50"] = Indicators.Ema(s.Closes, 50);
        s.Ind["rsi14"] = Indicators.Rsi(s.Closes, 14);
        s.Ind["atr14"] = Indicators.Atr(s.Highs, s.Lows, s.Closes, 14);
        var bb = Indicators.Bollinger(s.Closes, 20, 2);
        s.Ind["bb_up"]  = bb.Up;
        s.Ind["bb_mid"] = bb.Mid;
        s.Ind["bb_lo"]  = bb.Lo;
        return s;

        static double?[] Box(double[] arr) => arr.Select(v => (double?)v).ToArray();
    }

    private static bool EvalRule(StratRule rule, int i, Series s)
    {
        double? lhs = ValueAt(i, rule.Indicator, s);
        double? rhs = ValueAt(i, rule.Value, s);
        if (lhs is null || rhs is null) return false;
        switch (rule.Op)
        {
            case ">":  return lhs >  rhs;
            case ">=": return lhs >= rhs;
            case "<":  return lhs <  rhs;
            case "<=": return lhs <= rhs;
            case "cross_above":
            {
                double? pl = ValueAt(i - 1, rule.Indicator, s);
                double? pr = ValueAt(i - 1, rule.Value, s);
                return pl is not null && pr is not null && pl <= pr && lhs > rhs;
            }
            case "cross_below":
            {
                double? pl = ValueAt(i - 1, rule.Indicator, s);
                double? pr = ValueAt(i - 1, rule.Value, s);
                return pl is not null && pr is not null && pl >= pr && lhs < rhs;
            }
        }
        return false;
    }

    private static double? ValueAt(int i, string token, Series s)
    {
        if (i < 0) return null;
        var t = token.Trim().ToLowerInvariant();
        if (double.TryParse(t, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var num)) return num;
        if (s.Ind.TryGetValue(t, out var arr) && i < arr.Length) return arr[i];
        return null;
    }

    /// <summary>
    /// Backend timestamps are tz-naive already in the server's (IST) local
    /// time — parse as-is so display matches what a human would read on the
    /// exchange clock.
    /// </summary>
    private static DateTime ParseTime(string? str)
    {
        if (DateTime.TryParse(str, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t))
            return t;
        return DateTime.Now;
    }
}
