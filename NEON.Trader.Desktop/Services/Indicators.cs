using System;
using System.Collections.Generic;
using System.Linq;

namespace NEON.Trader.Desktop.Services;

/// <summary>
/// Pure-C# technical indicators mirroring the web frontend's JS implementations,
/// so a strategy backtest on desktop lines up with the browser version.
/// </summary>
public static class Indicators
{
    public static double?[] Sma(IReadOnlyList<double> closes, int n)
    {
        var out_ = new double?[closes.Count];
        if (n <= 0 || closes.Count == 0) return out_;
        double sum = 0;
        for (int i = 0; i < closes.Count; i++)
        {
            sum += closes[i];
            if (i >= n) sum -= closes[i - n];
            if (i >= n - 1) out_[i] = sum / n;
        }
        return out_;
    }

    public static double?[] Ema(IReadOnlyList<double> closes, int n)
    {
        var out_ = new double?[closes.Count];
        if (n <= 0) return out_;
        double k = 2.0 / (n + 1);
        double? ema = null;
        for (int i = 0; i < closes.Count; i++)
        {
            if (i < n - 1) continue;
            if (ema is null)
            {
                double s = 0;
                for (int j = 0; j < n; j++) s += closes[i - j];
                ema = s / n;
            }
            else
            {
                ema = closes[i] * k + ema.Value * (1 - k);
            }
            out_[i] = ema;
        }
        return out_;
    }

    public static double?[] Rsi(IReadOnlyList<double> closes, int n)
    {
        var out_ = new double?[closes.Count];
        if (closes.Count < 2 || n <= 0) return out_;
        double avgGain = 0, avgLoss = 0;
        for (int i = 1; i < closes.Count; i++)
        {
            double diff = closes[i] - closes[i - 1];
            double g = Math.Max(0, diff);
            double l = Math.Max(0, -diff);
            if (i <= n)
            {
                avgGain += g; avgLoss += l;
                if (i == n)
                {
                    avgGain /= n; avgLoss /= n;
                    out_[i] = 100 - 100.0 / (1 + (avgLoss == 0 ? 100 : avgGain / avgLoss));
                }
            }
            else
            {
                avgGain = (avgGain * (n - 1) + g) / n;
                avgLoss = (avgLoss * (n - 1) + l) / n;
                out_[i] = 100 - 100.0 / (1 + (avgLoss == 0 ? 100 : avgGain / avgLoss));
            }
        }
        return out_;
    }

    public static (double?[] Up, double?[] Mid, double?[] Lo) Bollinger(
        IReadOnlyList<double> closes, int n, double mult)
    {
        var mid = Sma(closes, n);
        var up = new double?[closes.Count];
        var lo = new double?[closes.Count];
        for (int i = n - 1; i < closes.Count; i++)
        {
            if (mid[i] is null) continue;
            double m = mid[i]!.Value;
            double s = 0;
            for (int j = 0; j < n; j++) { double d = closes[i - j] - m; s += d * d; }
            double sd = Math.Sqrt(s / n);
            up[i] = m + mult * sd;
            lo[i] = m - mult * sd;
        }
        return (up, mid, lo);
    }

    public static double?[] Atr(
        IReadOnlyList<double> highs, IReadOnlyList<double> lows, IReadOnlyList<double> closes, int n)
    {
        int len = closes.Count;
        var trs = new double[len];
        for (int i = 0; i < len; i++)
        {
            if (i == 0) { trs[i] = highs[i] - lows[i]; continue; }
            trs[i] = Math.Max(
                highs[i] - lows[i],
                Math.Max(
                    Math.Abs(highs[i] - closes[i - 1]),
                    Math.Abs(lows[i]  - closes[i - 1])));
        }
        var out_ = new double?[len];
        double? atr = null;
        for (int i = 0; i < len; i++)
        {
            if (i < n - 1) continue;
            if (atr is null)
            {
                double s = 0;
                for (int j = 0; j < n; j++) s += trs[i - j];
                atr = s / n;
            }
            else
            {
                atr = (atr.Value * (n - 1) + trs[i]) / n;
            }
            out_[i] = atr;
        }
        return out_;
    }
}
