# NEON.TRADER — Desktop (WinUI 3)

Native Windows desktop client for the NEON.TRADER trading backend(s). Same
aesthetic as the web dashboard: dark grid + neon-lime accent, JetBrains Mono
type, cyber chrome.

## Backends

Settings → Profiles lets you manage any number of backend connections:

- **NSE main** — single-user NSE backend, `portfolio=main`
- **NSE eval** — same backend, `portfolio=eval`
- **Forex** — multi-user Forex backend (Postgres-backed)

Each profile stores its own base URL + API key. Switching the title-bar
backend dropdown re-routes all API calls immediately.

Configuration is persisted to
`%LocalAppData%\NEON.Trader\settings.json`.

## Requirements

- Windows 10 1809+ or Windows 11 (ARM64 or x64)
- Visual Studio 2022+ with the **.NET desktop development** and
  **.NET Multi-platform App UI development** workloads, *or*
- .NET 8 SDK + Windows App SDK 1.6 + MSIX tooling:
  ```powershell
  winget install Microsoft.DotNet.SDK.8
  winget install Microsoft.WindowsAppSDK
  ```

## Build

```powershell
cd desktop-app
dotnet restore
dotnet build -c Debug -p:Platform=arm64   # or x64
```

Or open `NEON.Trader.Desktop.sln` in Visual Studio and press F5.

## Run unpackaged

The project is configured for `WindowsPackageType=None`, so it runs as a
plain `.exe` without MSIX deployment:

```powershell
dotnet run -p:Platform=arm64 --project NEON.Trader.Desktop
```

## Features

- **Dashboard** — portfolio KPIs, open positions, recent trades, autopilot
  controls. Polls `/api/status` every 15s.
- **Watchlist** — live price tiles with up/down tint based on delta.
- **Charts** — LiveCharts2 candlestick main pane + RSI sub-pane. Indicator
  overlays: MA20, EMA50, Bollinger Bands, volume, RSI. Symbol search
  autocompletes from the active profile's watchlist.
- **Strategy** — rule builder with indicator tokens (close, high, low,
  sma20/50, ema20/50, rsi14, bb_up/mid/lo, atr14, volume) and ops
  (`>`, `>=`, `<`, `<=`, `cross_above`, `cross_below`). Pick a template
  (EMA Cross, RSI MR, BB Breakout, SMA Trend) or build your own. Run a
  client-side backtest over `/api/candles` with SL/TP/commission/slippage
  modelling; renders equity curve, 12-KPI performance panel, full trade
  list.
- **Scanner** — rule-based or AI scan of the watchlist.
- **Agent** — conversational chat that talks to the backend's
  `/api/chat` LLM pipeline.
- **Logs** — live WebSocket stream from `/ws/logs`.
- **Settings** — manage multiple backend profiles + API keys.

## Layout

```
desktop-app/
├── NEON.Trader.Desktop.sln
└── NEON.Trader.Desktop/
    ├── NEON.Trader.Desktop.csproj
    ├── App.xaml(.cs)
    ├── MainWindow.xaml(.cs)
    ├── app.manifest
    ├── Themes/
    │   ├── Colors.xaml       – NEON palette
    │   └── Styles.xaml       – Card / button / text / input styles
    ├── Services/
    │   ├── ApiClient.cs      – HTTP + WebSocket + cert bypass
    │   ├── SettingsService.cs
    │   ├── AppServices.cs    – service locator
    │   ├── Indicators.cs     – SMA / EMA / RSI / BB / ATR
    │   └── Backtester.cs     – intrabar SL/TP engine
    ├── Models/
    │   ├── BackendProfile.cs
    │   └── TradingModels.cs
    └── Views/
        ├── DashboardPage.xaml(.cs)
        ├── WatchlistPage.xaml(.cs)
        ├── ChartsPage.xaml(.cs)
        ├── StrategyPage.xaml(.cs)
        ├── ScannerPage.xaml(.cs)
        ├── AgentPage.xaml(.cs)
        ├── LogsPage.xaml(.cs)
        └── SettingsPage.xaml(.cs)
```

## Notes

- The app trusts self-signed TLS certs — intended for private-network-only
  deployment mirroring the web dashboard.
- Backtest indicators and logic match the web strategy builder exactly
  (`frontend/app.js` ↔ `Services/Indicators.cs` + `Services/Backtester.cs`),
  so results on desktop line up with the browser.
