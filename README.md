# aitrader-desktop

Windows desktop client for the AI Trading Agent. Part of [`trading-agent`](https://github.com/sppidy/trading-agent).

- WinUI 3 + .NET 8 (`net8.0-windows10.0.19041.0`)
- Windows App SDK 2.0-preview
- LiveCharts2 (SkiaSharp), CommunityToolkit.Mvvm
- Bundled JetBrains Mono TTF
- Platforms: x86 / x64 / arm64

Same NEON aesthetic as the web dashboard: dark grid + neon-lime accent, JetBrains Mono type.

## Build

Open `NEON.Trader.Desktop.sln` in Visual Studio 18 (Community is fine), select platform, hit F5.

Or from the command line (requires the .NET 8 SDK installed):

```bash
dotnet build -c Release -p:Platform=x64
```

The `WindowsPackageType=None` setting means the build output is a plain `.exe` you can copy and run.

CI builds Release x64 on a Windows runner via `.github/workflows/desktop-build.yml` in the super-repo.

## Architecture highlights

- `Services/ApiClient.cs` — typed `HttpClient`, self-signed cert bypass, X-API-Key header
- `Services/SettingsService.cs` — multi-profile JSON persistence under `%LocalAppData%\NEON.Trader\settings.json`
- `Services/Indicators.cs` + `Services/Backtester.cs` — pure-C# SMA/EMA/RSI/Bollinger/ATR + intrabar SL/TP backtest engine, byte-for-byte matching the web JS implementation
- `Models/BackendProfile.cs` — NSE main / NSE eval / Forex profile presets
- `App.xaml.cs` — global crash logger to `%LocalAppData%\NEON.Trader\crash.log`

## Views

Dashboard · Watchlist · Portfolio · Charts · Strategy · Scanner · Agent · Logs · Settings.

## License

[Apache-2.0](LICENSE). Contributing guidelines and security policy live in the [super-repo](https://github.com/sppidy/trading-agent).
