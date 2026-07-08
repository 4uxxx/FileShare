namespace FileShare.Services;

/// <summary>Root service container, created once at app startup.</summary>
public sealed class AppServices
{
    public ConfigService Config { get; } = new();
    public ShareServerService Server { get; }
    public TunnelService Tunnel { get; }

    public AppServices()
    {
        Server = new ShareServerService(Config);
        Tunnel = new TunnelService(Config);
    }

    public void Load() => Config.Load();

    public void Shutdown()
    {
        try { Tunnel.Dispose(); } catch { /* best-effort */ }
        try { Server.Dispose(); } catch { /* best-effort */ }
        Config.Save();
    }
}
