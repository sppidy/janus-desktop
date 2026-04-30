using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NEON.Trader.Desktop.Services;

namespace NEON.Trader.Desktop.Views;

public sealed partial class LogsPage : Page
{
    private readonly AppServices _services = App.Services;
    private readonly DispatcherQueue _ui;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private const int MaxChars = 200_000;

    public LogsPage()
    {
        InitializeComponent();
        _ui = DispatcherQueue.GetForCurrentThread();
        Loaded   += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private static string FormatNow() =>
        DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    private void Start()
    {
        if (_loopTask is not null) return;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    private void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        _loopTask = null;
        SetStatus(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        Append($"[{FormatNow()}] [logs] starting receive loop\r\n");
        while (!ct.IsCancellationRequested)
        {
            var uri = _services.Api.BuildLogsWsUri();
            if (uri is null)
            {
                Append($"[{FormatNow()}] [logs] no active profile — open Settings first\r\n");
                await DelayOrCancel(TimeSpan.FromSeconds(5), ct);
                continue;
            }
            // Strip the token from the logged URL.
            var sanitized = uri.GetLeftPart(UriPartial.Path);
            Append($"[{FormatNow()}] [logs] connecting {sanitized}\r\n");

            using var ws = new ClientWebSocket();
            ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            // Also set the API key as a header in case the query-string token is stripped upstream.
            var key = _services.Settings.ActiveProfile?.ApiKey;
            if (!string.IsNullOrEmpty(key))
            {
                try { ws.Options.SetRequestHeader("X-API-Key", key); } catch { }
            }

            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(10));
                await ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
                SetStatus(true);
                Append($"[{FormatNow()}] [logs] connected — waiting for events\r\n");

                var buf = new byte[8192];
                var sb = new StringBuilder();
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var res = await ws.ReceiveAsync(buf, ct).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    if (res.Count > 0)
                    {
                        sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                        if (res.EndOfMessage)
                        {
                            var text = sb.ToString();
                            sb.Clear();
                            if (!text.EndsWith("\n")) text += "\r\n";
                            Append(text);
                        }
                    }
                }
                Append($"[{FormatNow()}] [logs] connection closed (state={ws.State})\r\n");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Append($"[{FormatNow()}] [logs] {ex.GetType().Name}: {ex.Message}\r\n");
            }
            finally
            {
                SetStatus(false);
                try { if (ws.State == WebSocketState.Open)
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch { }
            }

            if (ct.IsCancellationRequested) break;
            Append($"[{FormatNow()}] [logs] reconnecting in 3s...\r\n");
            await DelayOrCancel(TimeSpan.FromSeconds(3), ct);
        }
        Append($"[{FormatNow()}] [logs] receive loop exited\r\n");
    }

    private static async Task DelayOrCancel(TimeSpan d, CancellationToken ct)
    {
        try { await Task.Delay(d, ct).ConfigureAwait(false); } catch { }
    }

    private void Append(string text)
    {
        _ui.TryEnqueue(() =>
        {
            var cur = LogBox.Text ?? "";
            var next = cur + text;
            if (next.Length > MaxChars)
                next = next.Substring(next.Length - MaxChars);
            LogBox.Text = next;
            LogBox.SelectionStart = LogBox.Text.Length;
            LogScroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);
        });
    }

    private void SetStatus(bool connected)
    {
        _ui.TryEnqueue(() =>
        {
            if (connected)
            {
                var neon = (Brush)Application.Current.Resources["UpBrush"];
                StatusPillText.Text = "LIVE";
                StatusPillText.Foreground = neon;
                StatusPill.BorderBrush = neon;
            }
            else
            {
                var down = (Brush)Application.Current.Resources["DownBrush"];
                StatusPillText.Text = "DISCONNECTED";
                StatusPillText.Foreground = down;
                StatusPill.BorderBrush = down;
            }
        });
    }

    private void Clear_Click(object sender, object e)
    {
        LogBox.Text = "";
    }
}
