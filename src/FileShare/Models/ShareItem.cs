namespace FileShare.Models;

public enum ShareItemKind
{
    File,
    Folder
}

/// <summary>A single file or folder the user has chosen to publish.</summary>
public sealed class ShareItem
{
    /// <summary>Stable short id used in public URLs instead of the real path.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];

    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ShareItemKind Kind { get; set; }

    /// <summary>File size in bytes, or total size of a folder (best-effort, computed lazily).</summary>
    public long SizeBytes { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}
