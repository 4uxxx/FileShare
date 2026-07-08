namespace FileShare.Models;

public enum TunnelMode
{
    /// <summary>Cloudflare quick tunnel — no account needed, URL changes every restart.</summary>
    CloudflareQuick,

    /// <summary>Tailscale Funnel — requires a (free) Tailscale account, URL stays fixed.</summary>
    TailscaleFunnel,
}
