namespace FileShare.Models;

public sealed class AccessLogEntry
{
    public DateTimeOffset Time { get; init; } = DateTimeOffset.Now;
    public string RemoteAddress { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public int StatusCode { get; init; }
}
