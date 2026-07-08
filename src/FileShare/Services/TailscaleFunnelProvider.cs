using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FileShare.Services;

/// <summary>
/// Exposes the local HTTP server via Tailscale Funnel. Requires a (free) Tailscale
/// account and the Funnel feature enabled once for the tailnet, but the resulting
/// public URL (https://&lt;device&gt;.&lt;tailnet&gt;.ts.net) stays fixed across restarts.
/// </summary>
public sealed partial class TailscaleFunnelProvider : ITunnelProvider
{
    private static readonly string[] CandidatePaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tailscale", "tailscale.exe"),
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public event EventHandler<string>? StatusChanged;

    public string? PublicUrl { get; private set; }
    public int? BoundPort { get; private set; }

    private static string? FindBinary() => CandidatePaths.FirstOrDefault(File.Exists);

    public async Task<string?> StartAsync(int localPort, CancellationToken ct = default)
    {
        var exe = FindBinary();
        if (exe is null)
        {
            StatusChanged?.Invoke(this,
                "Tailscale がインストールされていません。https://tailscale.com/download/windows からインストールしてください。");
            return null;
        }

        var status = await GetStatusAsync(exe, ct);
        if (status?.BackendState != "Running")
        {
            if (!await LoginAsync(exe, ct)) return null;
            status = await GetStatusAsync(exe, ct);
        }

        if (status?.BackendState != "Running")
        {
            StatusChanged?.Invoke(this, "Tailscale に接続できませんでした。");
            return null;
        }

        StatusChanged?.Invoke(this, "Tailscale Funnel を有効にしています…");
        var funnelResult = await RunFunnelEnableAsync(exe, localPort, ct);
        if (!funnelResult.Ok)
        {
            var hint = funnelResult.EnableUrl is not null ? $" 次のURLで有効化してください: {funnelResult.EnableUrl}" : string.Empty;
            StatusChanged?.Invoke(this, $"Tailscale Funnel を有効化できませんでした。{hint}");
            if (funnelResult.EnableUrl is not null)
            {
                try { Process.Start(new ProcessStartInfo(funnelResult.EnableUrl) { UseShellExecute = true }); } catch { /* best effort */ }
            }
            return null;
        }

        var dnsName = status.Self?.DNSName?.TrimEnd('.');
        if (string.IsNullOrEmpty(dnsName))
        {
            StatusChanged?.Invoke(this, "Tailscale のデバイス名を取得できませんでした。");
            return null;
        }

        BoundPort = localPort;
        PublicUrl = $"https://{dnsName}/";
        StatusChanged?.Invoke(this, "固定URLで公開しました。");
        return PublicUrl;
    }

    public async Task StopAsync()
    {
        var exe = FindBinary();
        if (exe is not null && BoundPort is not null)
        {
            await RunAsync(exe, "funnel reset", CancellationToken.None);
        }
        BoundPort = null;
        PublicUrl = null;
    }

    private async Task<(bool Ok, string? EnableUrl)> RunFunnelEnableAsync(string exe, int localPort, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"funnel --bg {localPort}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        string? enableUrl = null;

        void OnLine(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            var match = UrlRegex().Match(e.Data);
            if (match.Success) enableUrl = match.Value;
        }

        process.OutputDataReceived += OnLine;
        process.ErrorDataReceived += OnLine;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // `tailscale funnel --bg` keeps running/retrying when Funnel isn't enabled for the
        // tailnet yet, rather than failing fast — bound how long we wait for it.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return (false, enableUrl);
        }

        return (process.ExitCode == 0, enableUrl);
    }

    private async Task<bool> LoginAsync(string exe, CancellationToken ct)
    {
        StatusChanged?.Invoke(this, "Tailscale に接続しています…");

        var result = await RunUpOnceAsync(exe, "up", ct);

        // The device already has non-default settings (e.g. a custom hostname or
        // unattended mode); tailscale refuses a bare "up" and instead prints the exact
        // command to re-run. Retry with that so we don't silently reset the user's config.
        if (!result.Ok && result.SuggestedArgs is not null)
        {
            StatusChanged?.Invoke(this, "既存のTailscale設定を維持して再接続しています…");
            result = await RunUpOnceAsync(exe, result.SuggestedArgs, ct);
        }

        if (!result.Ok && !result.SawLoginUrl)
        {
            var detail = string.IsNullOrWhiteSpace(result.ErrorText) ? string.Empty : $": {result.ErrorText.Trim()}";
            StatusChanged?.Invoke(this, $"Tailscale への接続に失敗しました{detail}");
        }

        return result.Ok;
    }

    private async Task<UpResult> RunUpOnceAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sawLoginUrl = false;
        string? suggestedArgs = null;
        var outputLines = new List<string>();

        void OnLine(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            outputLines.Add(e.Data);

            var urlMatch = LoginUrlRegex().Match(e.Data);
            if (urlMatch.Success && !sawLoginUrl)
            {
                sawLoginUrl = true;
                var url = urlMatch.Value;
                StatusChanged?.Invoke(this, $"ブラウザで次のURLを開いてログインしてください: {url}");
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* best effort */ }
            }

            var trimmed = e.Data.TrimStart();
            if (trimmed.StartsWith("tailscale up", StringComparison.OrdinalIgnoreCase))
                suggestedArgs = trimmed["tailscale ".Length..].Trim();
        }

        process.OutputDataReceived += OnLine;
        process.ErrorDataReceived += OnLine;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return new UpResult(false, suggestedArgs, sawLoginUrl, "タイムアウトしました。");
        }

        return new UpResult(process.ExitCode == 0, suggestedArgs, sawLoginUrl, string.Join('\n', outputLines));
    }

    private sealed record UpResult(bool Ok, string? SuggestedArgs, bool SawLoginUrl, string ErrorText);

    private static async Task<TailscaleStatus?> GetStatusAsync(string exe, CancellationToken ct)
    {
        var (exitCode, stdOut, _) = await RunAsync(exe, "status --json", ct);
        if (exitCode is not (0 or 1) || string.IsNullOrWhiteSpace(stdOut)) return null;
        try
        {
            return JsonSerializer.Deserialize<TailscaleStatus>(stdOut, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, await stdOutTask, await stdErrTask);
    }

    [GeneratedRegex(@"https://login\.tailscale\.com/\S+")]
    private static partial Regex LoginUrlRegex();

    [GeneratedRegex(@"https://\S+")]
    private static partial Regex UrlRegex();

    private sealed class TailscaleStatus
    {
        public string? BackendState { get; set; }
        public string? AuthURL { get; set; }
        public TailscaleSelf? Self { get; set; }
    }

    private sealed class TailscaleSelf
    {
        public string? DNSName { get; set; }
    }
}
