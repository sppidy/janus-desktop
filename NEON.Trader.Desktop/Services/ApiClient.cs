using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NEON.Trader.Desktop.Models;

namespace NEON.Trader.Desktop.Services;

public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public ApiException(string message, int statusCode = 0) : base(message) { StatusCode = statusCode; }
}

public sealed class ApiClient
{
    private readonly SettingsService _settings;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ApiClient(SettingsService settings)
    {
        _settings = settings;
        var handler = new SocketsHttpHandler
        {
            // Self-hosted endpoint often uses a self-signed cert. Validation is
            // delegated to TlsTrust which (in order) accepts: chains that are
            // cleanly valid, or certs matching TRADER_BACKEND_CERT_THUMBPRINT,
            // or — only if explicitly opted in via TRADER_ALLOW_INSECURE_TLS=1
            // — anything. See Services/TlsTrust.cs for the full policy.
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = TlsTrust.Validate,
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    private BackendProfile RequireActive()
    {
        var p = _settings.ActiveProfile
            ?? throw new ApiException("No backend profile selected");
        if (string.IsNullOrWhiteSpace(p.BaseUrl))
            throw new ApiException("Active backend has no base URL");
        return p;
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var profile = RequireActive();
        var url = profile.BaseUrl.TrimEnd('/') + path;
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(profile.ApiKey))
            req.Headers.Add("X-API-Key", profile.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
    {
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new ApiException("Unauthorized — check API key", 401);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new ApiException(
                $"HTTP {(int)res.StatusCode}: {body}", (int)res.StatusCode);
        }
        var data = await res.Content.ReadFromJsonAsync<T>(JsonOpts, ct).ConfigureAwait(false);
        if (data is null) throw new ApiException("Empty response body");
        return data;
    }

    // ── Public endpoints ──

    public Task<StatusResponse> GetStatusAsync(CancellationToken ct = default)
    {
        var profile = RequireActive();
        var path = profile.IsNse
            ? $"/api/status?portfolio={Uri.EscapeDataString(profile.Portfolio ?? "main")}"
            : "/api/status";
        return SendAsync<StatusResponse>(BuildRequest(HttpMethod.Get, path), ct);
    }

    public Task<PricesResponse> GetPricesAsync(CancellationToken ct = default) =>
        SendAsync<PricesResponse>(BuildRequest(HttpMethod.Get, "/api/prices"), ct);

    public Task<RegimeResponse> GetRegimeAsync(CancellationToken ct = default) =>
        SendAsync<RegimeResponse>(BuildRequest(HttpMethod.Get, "/api/market-regime"), ct);

    public Task<CandlesResponse> GetCandlesAsync(
        string symbol, string timeframe = "1h", int limit = 500,
        CancellationToken ct = default)
    {
        var path =
            $"/api/candles?symbol={Uri.EscapeDataString(symbol)}" +
            $"&timeframe={Uri.EscapeDataString(timeframe)}&limit={limit}";
        return SendAsync<CandlesResponse>(BuildRequest(HttpMethod.Get, path), ct);
    }

    public Task<ScanResponse> RunRuleScanAsync(CancellationToken ct = default) =>
        SendAsync<ScanResponse>(BuildRequest(HttpMethod.Get, "/api/scan"), ct);

    public Task<JobAccepted> StartAiScanAsync(CancellationToken ct = default) =>
        SendAsync<JobAccepted>(BuildRequest(HttpMethod.Get, "/api/ai-scan"), ct);

    public Task<ScanJob> GetScanJobAsync(string jobId, CancellationToken ct = default) =>
        SendAsync<ScanJob>(BuildRequest(HttpMethod.Get, $"/api/scan/status/{jobId}"), ct);

    public async Task<JobAccepted> StartChatAsync(ChatRequest req, CancellationToken ct = default)
    {
        var msg = BuildRequest(HttpMethod.Post, "/api/chat");
        msg.Content = JsonContent.Create(req, options: JsonOpts);
        return await SendAsync<JobAccepted>(msg, ct).ConfigureAwait(false);
    }

    public Task<ChatJob> GetChatJobAsync(string jobId, CancellationToken ct = default) =>
        SendAsync<ChatJob>(BuildRequest(HttpMethod.Get, $"/api/chat/status/{jobId}"), ct);

    public async Task<OrderResponse> PlaceOrderAsync(OrderRequest order, CancellationToken ct = default)
    {
        // Carry the active NSE portfolio onto the request so 'main' vs 'eval'
        // is respected even if the caller didn't set it explicitly.
        var profile = RequireActive();
        if (profile.IsNse && string.IsNullOrEmpty(order.Portfolio))
            order.Portfolio = profile.Portfolio ?? "main";

        var msg = BuildRequest(HttpMethod.Post, "/api/order");
        msg.Content = JsonContent.Create(order, options: JsonOpts);
        return await SendAsync<OrderResponse>(msg, ct).ConfigureAwait(false);
    }

    public async Task StartAutopilotAsync(CancellationToken ct = default)
    {
        var msg = BuildRequest(HttpMethod.Post, "/api/autopilot/start");
        msg.Content = JsonContent.Create(new { }, options: JsonOpts);
        _ = await SendAsync<Dictionary<string, object>>(msg, ct).ConfigureAwait(false);
    }

    public async Task StopAutopilotAsync(CancellationToken ct = default)
    {
        var msg = BuildRequest(HttpMethod.Post, "/api/autopilot/stop");
        msg.Content = JsonContent.Create(new { }, options: JsonOpts);
        _ = await SendAsync<Dictionary<string, object>>(msg, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Build the /ws/logs URL for the active profile. The API key is NOT in
    /// the URL — caller must set the X-API-Key header on the ClientWebSocket
    /// (see <see cref="ConfigureLogsWs"/>) so the key never lands in reverse-
    /// proxy access logs / browser-history / tracing.
    /// </summary>
    public Uri? BuildLogsWsUri()
    {
        var profile = _settings.ActiveProfile;
        if (profile is null || string.IsNullOrEmpty(profile.BaseUrl)) return null;
        var baseUri = new Uri(profile.BaseUrl);
        var wsScheme = baseUri.Scheme == "https" ? "wss" : "ws";
        return new Uri($"{wsScheme}://{baseUri.Authority}/ws/logs");
    }

    /// <summary>
    /// Stamp the active profile's API key onto a ClientWebSocket via the
    /// X-API-Key header so the WS handshake authenticates without ever
    /// putting the key in a URL.
    /// </summary>
    public void ConfigureLogsWs(System.Net.WebSockets.ClientWebSocket ws)
    {
        var profile = _settings.ActiveProfile;
        if (profile is null || string.IsNullOrEmpty(profile.ApiKey)) return;
        try { ws.Options.SetRequestHeader("X-API-Key", profile.ApiKey); } catch { /* already-connected */ }
    }
}
