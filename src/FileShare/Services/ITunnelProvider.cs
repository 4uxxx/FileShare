namespace FileShare.Services;

/// <summary>A mechanism for exposing the local HTTP server to the public internet.</summary>
public interface ITunnelProvider
{
    event EventHandler<string>? StatusChanged;

    string? PublicUrl { get; }

    Task<string?> StartAsync(int localPort, CancellationToken ct = default);

    Task StopAsync();
}
