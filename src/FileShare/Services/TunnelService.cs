using FileShare.Models;

namespace FileShare.Services;

/// <summary>
/// Facade that starts/stops the tunnel provider selected in <see cref="AppConfig.TunnelMode"/>,
/// so the rest of the app (MainViewModel) doesn't need to know which provider is active.
/// </summary>
public sealed class TunnelService : IDisposable
{
    private readonly ConfigService _configService;
    private readonly CloudflareTunnelProvider _cloudflare = new();
    private readonly TailscaleFunnelProvider _tailscale = new();
    private ITunnelProvider? _active;

    public event EventHandler<string>? StatusChanged;

    public string? PublicUrl => _active?.PublicUrl;

    public TunnelService(ConfigService configService)
    {
        _configService = configService;
        _cloudflare.StatusChanged += (_, msg) => StatusChanged?.Invoke(this, msg);
        _tailscale.StatusChanged += (_, msg) => StatusChanged?.Invoke(this, msg);
    }

    public Task<string?> StartAsync(int localPort, CancellationToken ct = default)
    {
        _active = _configService.Config.TunnelMode switch
        {
            TunnelMode.TailscaleFunnel => _tailscale,
            _ => _cloudflare,
        };
        return _active.StartAsync(localPort, ct);
    }

    public async Task StopAsync()
    {
        if (_active is null) return;
        await _active.StopAsync();
        _active = null;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        (_cloudflare as IDisposable)?.Dispose();
    }
}
