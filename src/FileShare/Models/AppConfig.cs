namespace FileShare.Models;

/// <summary>Persisted application state: shared items, server + auth settings.</summary>
public sealed class AppConfig
{
    public List<ShareItem> Items { get; set; } = new();

    public int Port { get; set; } = 0; // 0 = pick a free port automatically

    public bool AuthEnabled { get; set; } = true;

    public TunnelMode TunnelMode { get; set; } = TunnelMode.CloudflareQuick;

    public string AuthUsername { get; set; } = "share";

    public string AuthPassword { get; set; } = string.Empty;

    /// <summary>Pinned quick-add folder paths shown as one-click chips in the GUI.</summary>
    public List<string> PinnedPaths { get; set; } = new()
    {
        @"E:\hxt\season file",
        @"D:\Project Reboot\dll",
        @"D:\Project Reboot\soft",
        @"D:\clip",
    };
}
