using System.Text.Json.Serialization;

namespace Janus.Desktop.Models;

public enum BackendKind { NseMain, NseEval, Forex }

public sealed class BackendProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public BackendKind Kind { get; set; }
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";

    /// <summary>"main" or "eval" for NSE; ignored for Forex.</summary>
    public string? Portfolio { get; set; }

    [JsonIgnore]
    public bool IsNse => Kind is BackendKind.NseMain or BackendKind.NseEval;

    [JsonIgnore]
    public bool IsForex => Kind == BackendKind.Forex;

    public static BackendProfile[] DefaultProfiles() => new[]
    {
        new BackendProfile
        {
            Id = "nse-main", Name = "NSE // main",
            Kind = BackendKind.NseMain,
            BaseUrl = "https://BACKEND_HOST:8443",
            Portfolio = "main",
        },
        new BackendProfile
        {
            Id = "nse-eval", Name = "NSE // eval",
            Kind = BackendKind.NseEval,
            BaseUrl = "https://BACKEND_HOST:8443",
            Portfolio = "eval",
        },
        new BackendProfile
        {
            Id = "forex",   Name = "FOREX",
            Kind = BackendKind.Forex,
            BaseUrl = "https://BACKEND_HOST:8444",
            Portfolio = null,
        },
    };
}
