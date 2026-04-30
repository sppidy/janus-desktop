using NEON.Trader.Desktop.Services;

namespace NEON.Trader.Desktop.Services;

/// <summary>
/// Cheap service locator. A real DI container is overkill for a 1-window app.
/// Access via <c>App.Services</c>.
/// </summary>
public sealed class AppServices
{
    public SettingsService Settings { get; }
    public ApiClient Api { get; }

    public AppServices()
    {
        Settings = new SettingsService();
        Settings.Load();
        Api = new ApiClient(Settings);
    }
}
