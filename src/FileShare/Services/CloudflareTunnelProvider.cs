using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace FileShare.Services;

/// <summary>
/// Exposes the local HTTP server via a Cloudflare "quick tunnel" (cloudflared.exe).
/// No account or port forwarding needed, but the public URL changes every time
/// a new tunnel is started.
/// </summary>
public sealed partial class CloudflareTunnelProvider : ITunnelProvider, IDisposable
{
    private const string DownloadUrl =
        "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";

    private static readonly string ToolsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileShare", "tools");

    private static readonly string CloudflaredPath = Path.Combine(ToolsDir, "cloudflared.exe");

    private Process? _process;

    public event EventHandler<string>? StatusChanged;

    public string? PublicUrl { get; private set; }

    /// <summary>Ensures cloudflared.exe is present locally, downloading it if necessary.</summary>
    public async Task<bool> EnsureBinaryAsync(CancellationToken ct = default)
    {
        if (File.Exists(CloudflaredPath)) return true;

        try
        {
            Directory.CreateDirectory(ToolsDir);
            StatusChanged?.Invoke(this, "cloudflared をダウンロードしています…");

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(3);
            await using var stream = await http.GetStreamAsync(DownloadUrl, ct);
            await using var file = File.Create(CloudflaredPath);
            await stream.CopyToAsync(file, ct);
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"cloudflared のダウンロードに失敗しました: {ex.Message}");
            if (File.Exists(CloudflaredPath)) File.Delete(CloudflaredPath);
            return false;
        }
    }

    public async Task<string?> StartAsync(int localPort, CancellationToken ct = default)
    {
        if (!await EnsureBinaryAsync(ct)) return null;

        var tcs = new TaskCompletionSource<string?>();
        ct.Register(() => tcs.TrySetResult(null));

        var psi = new ProcessStartInfo
        {
            FileName = CloudflaredPath,
            Arguments = $"tunnel --url http://127.0.0.1:{localPort} --no-autoupdate",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        void OnLine(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            var match = UrlRegex().Match(e.Data);
            if (match.Success && PublicUrl is null)
            {
                PublicUrl = match.Value;
                tcs.TrySetResult(PublicUrl);
                StatusChanged?.Invoke(this, "トンネルに接続しました。");
            }
        }

        _process.OutputDataReceived += OnLine;
        _process.ErrorDataReceived += OnLine;
        _process.Exited += (_, _) =>
        {
            StatusChanged?.Invoke(this, "トンネルが終了しました。");
            tcs.TrySetResult(null);
        };

        StatusChanged?.Invoke(this, "cloudflare トンネルを起動しています…");
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var linkedRegistration = timeout.Token.Register(() => tcs.TrySetResult(null));

        return await tcs.Task;
    }

    public async Task StopAsync()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            // Best-effort shutdown.
        }
        finally
        {
            _process.Dispose();
            _process = null;
            PublicUrl = null;
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    [GeneratedRegex(@"https://[a-zA-Z0-9-]+\.trycloudflare\.com")]
    private static partial Regex UrlRegex();
}
