using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Janus.Desktop.Models;

public sealed class StatusResponse
{
    [JsonPropertyName("status")]       public string? Status { get; set; }
    [JsonPropertyName("portfolio")]    public string? Portfolio { get; set; }
    [JsonPropertyName("portfolios_available")] public List<string>? PortfoliosAvailable { get; set; }
    [JsonPropertyName("summary")]      public Summary? Summary { get; set; }
    [JsonPropertyName("positions")]    public List<Position>? Positions { get; set; }
    [JsonPropertyName("recent_trades")] public List<Trade>? RecentTrades { get; set; }
    [JsonPropertyName("watchlist")]    public List<string>? Watchlist { get; set; }
    [JsonPropertyName("autopilot")]    public Autopilot? Autopilot { get; set; }
}

public sealed class Summary
{
    [JsonPropertyName("cash")]             public double Cash { get; set; }
    [JsonPropertyName("total_value")]      public double TotalValue { get; set; }
    [JsonPropertyName("total_return_pct")] public double TotalReturnPct { get; set; }
    [JsonPropertyName("realized_pnl")]     public double RealizedPnl { get; set; }
}

public sealed class Position
{
    [JsonPropertyName("symbol")]        public string Symbol { get; set; } = "";
    [JsonPropertyName("quantity")]      public double Quantity { get; set; }
    [JsonPropertyName("avg_price")]     public double AvgPrice { get; set; }
    [JsonPropertyName("current_price")] public double CurrentPrice { get; set; }
    [JsonPropertyName("pnl")]           public double Pnl { get; set; }
    [JsonPropertyName("pnl_pct")]       public double PnlPct { get; set; }
    [JsonPropertyName("highest_price")] public double HighestPrice { get; set; }
    [JsonPropertyName("entry_time")]    public string? EntryTime { get; set; }
}

public sealed class Trade
{
    [JsonPropertyName("symbol")]       public string Symbol { get; set; } = "";
    [JsonPropertyName("side")]         public string? Side { get; set; }
    [JsonPropertyName("action")]       public string? Action { get; set; }
    [JsonPropertyName("quantity")]     public double? Quantity { get; set; }
    [JsonPropertyName("entry_price")]  public double? EntryPrice { get; set; }
    [JsonPropertyName("exit_price")]   public double? ExitPrice { get; set; }
    [JsonPropertyName("price")]        public double? Price { get; set; }
    [JsonPropertyName("pnl")]          public double? Pnl { get; set; }
    [JsonPropertyName("pnl_pct")]      public double? PnlPct { get; set; }
    [JsonPropertyName("timestamp")]    public string? Timestamp { get; set; }

    public string ResolvedSide => (Side ?? Action ?? "").ToUpperInvariant();
    public double? ResolvedPrice => ExitPrice ?? EntryPrice ?? Price;

    // Display helpers for XAML x:Bind — keep source strings compact.
    public string FormattedTime
    {
        get
        {
            if (string.IsNullOrEmpty(Timestamp)) return "—";
            // Backend emits tz-naive strings already in server-local time —
            // parse verbatim, don't re-interpret as UTC (which would shift the
            // displayed value by the server↔client TZ offset).
            if (System.DateTime.TryParse(
                    Timestamp,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var dt))
            {
                return dt.ToString("MMM d HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            }
            return Timestamp;
        }
    }

    public string FormattedPrice =>
        ResolvedPrice is null ? "—" : ResolvedPrice.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    public string FormattedPnl
    {
        get
        {
            if (Pnl is null) return "";
            var v = Pnl.Value;
            var sign = v >= 0 ? "+" : "-";
            return sign + "₹" + System.Math.Abs(v).ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
        }
    }
}

public sealed class Autopilot
{
    [JsonPropertyName("running")]    public bool Running { get; set; }
    [JsonPropertyName("cycle")]      public int Cycle { get; set; }
    [JsonPropertyName("started_at")] public string? StartedAt { get; set; }
    [JsonPropertyName("interval")]   public int Interval { get; set; }
    [JsonPropertyName("pid")]        public int Pid { get; set; }
}

public sealed class PricesResponse
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("prices")] public Dictionary<string, double>? Prices { get; set; }
}

public sealed class RegimeResponse
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("regime")] public string? Regime { get; set; }
}

public sealed class Candle
{
    [JsonPropertyName("t")] public string? Time { get; set; }
    [JsonPropertyName("o")] public double Open { get; set; }
    [JsonPropertyName("h")] public double High { get; set; }
    [JsonPropertyName("l")] public double Low { get; set; }
    [JsonPropertyName("c")] public double Close { get; set; }
    [JsonPropertyName("v")] public long Volume { get; set; }
}

public sealed class CandlesResponse
{
    [JsonPropertyName("status")]    public string? Status { get; set; }
    [JsonPropertyName("symbol")]    public string? Symbol { get; set; }
    [JsonPropertyName("timeframe")] public string? Timeframe { get; set; }
    [JsonPropertyName("candles")]   public List<Candle>? Candles { get; set; }
}

public sealed class Signal
{
    [JsonPropertyName("symbol")]            public string Symbol { get; set; } = "";
    [JsonPropertyName("signal")]            public string? SignalDir { get; set; }
    [JsonPropertyName("price")]             public double? Price { get; set; }
    [JsonPropertyName("confidence")]        public double? Confidence { get; set; }
    [JsonPropertyName("reason")]            public string? Reason { get; set; }
    [JsonPropertyName("stop_loss")]         public double? StopLoss { get; set; }
    [JsonPropertyName("target")]            public double? Target { get; set; }
    [JsonPropertyName("position_size_pct")] public double? PositionSizePct { get; set; }
    [JsonPropertyName("entry_price")]       public double? EntryPrice { get; set; }
}

public sealed class ScanResponse
{
    [JsonPropertyName("status")]  public string? Status { get; set; }
    [JsonPropertyName("signals")] public List<Signal>? Signals { get; set; }
}

public sealed class JobAccepted
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("job_id")] public string? JobId { get; set; }
}

public sealed class ScanJob
{
    [JsonPropertyName("status")]  public string? Status { get; set; }
    [JsonPropertyName("signals")] public List<Signal>? Signals { get; set; }
    [JsonPropertyName("error")]   public string? Error { get; set; }
}

public sealed class ChatJob
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("reply")]  public string? Reply { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("error")]  public string? Error { get; set; }
}

public sealed class ChatHistoryItem
{
    [JsonPropertyName("role")]    public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

public sealed class ChatRequest
{
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("history")] public List<ChatHistoryItem> History { get; set; } = new();
}

public sealed class OrderRequest
{
    [JsonPropertyName("symbol")]   public string Symbol   { get; set; } = "";
    [JsonPropertyName("side")]     public string Side     { get; set; } = "BUY";
    [JsonPropertyName("quantity")] public int?   Quantity { get; set; }
    [JsonPropertyName("price")]    public double? Price   { get; set; }
    [JsonPropertyName("portfolio")] public string? Portfolio { get; set; }
}

public sealed class OrderResponse
{
    [JsonPropertyName("status")]   public string?  Status   { get; set; }
    [JsonPropertyName("action")]   public string?  Action   { get; set; }
    [JsonPropertyName("symbol")]   public string?  Symbol   { get; set; }
    [JsonPropertyName("quantity")] public int?     Quantity { get; set; }
    [JsonPropertyName("price")]    public double?  Price    { get; set; }
    [JsonPropertyName("summary")]  public Summary? Summary  { get; set; }
}
