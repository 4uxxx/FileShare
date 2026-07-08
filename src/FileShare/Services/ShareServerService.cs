using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FileShare.Models;

namespace FileShare.Services;

/// <summary>
/// Embedded Kestrel HTTP server that publishes the configured <see cref="ShareItem"/>s
/// for download. Binds to loopback only; a <see cref="TunnelService"/> is responsible
/// for exposing it to the internet.
/// </summary>
public sealed class ShareServerService : IDisposable
{
    private readonly ConfigService _configService;
    private readonly FileExtensionContentTypeProvider _contentTypes = new();
    private WebApplication? _app;

    public event EventHandler<AccessLogEntry>? RequestLogged;

    public bool IsRunning => _app is not null;

    public int Port { get; private set; }

    public ShareServerService(ConfigService configService)
    {
        _configService = configService;
    }

    public async Task StartAsync()
    {
        if (_app is not null) return;

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{_configService.Config.Port}");
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Large, slow, or tunnel-relayed downloads can dip below Kestrel's default
            // minimum throughput watchdog and get killed mid-transfer. This is a personal
            // file-sharing tool behind auth, not a public API, so disable that guard.
            options.Limits.MinResponseDataRate = null;
            options.Limits.MinRequestBodyDataRate = null;

            // ZipArchive.Dispose() writes the central directory synchronously, which Kestrel
            // blocks by default (AllowSynchronousIO = false) and throws mid-download for the
            // /zip endpoint. Safe to allow here: this is a small local app, not a high-scale API.
            options.AllowSynchronousIO = true;
        });

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            if (!TryAuthorize(context))
            {
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"FileShare\"";
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("認証が必要です。");
                LogRequest(context, StatusCodes.Status401Unauthorized);
                return;
            }

            await next();
            LogRequest(context, context.Response.StatusCode);
        });

        app.MapGet("/", () => Results.Content(BuildIndexHtml(), "text/html; charset=utf-8"));

        app.MapGet("/dl/{id}", IResult (string id) =>
        {
            var item = FindRootItem(id, ShareItemKind.File);
            if (item is null || !File.Exists(item.Path)) return Results.NotFound();

            var contentType = ResolveContentType(item.Path);
            return Results.File(item.Path, contentType, item.Name, enableRangeProcessing: true);
        });

        app.MapGet("/browse/{id}/{**subpath}", IResult (string id, string? subpath) =>
        {
            var item = FindRootItem(id, ShareItemKind.Folder);
            if (item is null || !Directory.Exists(item.Path)) return Results.NotFound();
            if (!TryResolveSafePath(item.Path, subpath, out var fullPath) || !Directory.Exists(fullPath))
                return Results.NotFound();

            return Results.Content(BuildBrowseHtml(item, subpath ?? string.Empty, fullPath), "text/html; charset=utf-8");
        });

        app.MapGet("/file/{id}/{**subpath}", IResult (string id, string? subpath) =>
        {
            var item = FindRootItem(id, ShareItemKind.Folder);
            if (item is null) return Results.NotFound();
            if (!TryResolveSafePath(item.Path, subpath, out var fullPath) || !File.Exists(fullPath))
                return Results.NotFound();

            var contentType = ResolveContentType(fullPath);
            return Results.File(fullPath, contentType, Path.GetFileName(fullPath), enableRangeProcessing: true);
        });

        app.MapGet("/zip/{id}/{**subpath}", async (HttpContext ctx, string id, string? subpath) =>
        {
            var item = FindRootItem(id, ShareItemKind.Folder);
            if (item is null) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            if (!TryResolveSafePath(item.Path, subpath, out var fullPath) || !Directory.Exists(fullPath))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var zipName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{zipName}.zip\"";

            using var archive = new ZipArchive(ctx.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(fullPath, file).Replace('\\', '/');
                // Stored (no compression): most shared files are already-compressed media/binaries,
                // so compressing wastes CPU and creates multi-second gaps with no bytes flushed to
                // the client, which can look like a stall over a slower relay (e.g. Tailscale Funnel).
                var entry = archive.CreateEntry(relative, CompressionLevel.NoCompression);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream, ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        });

        _app = app;
        await app.StartAsync();

        var address = app.Urls.FirstOrDefault()
            ?? app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?
                .Addresses.FirstOrDefault();

        Port = address is not null ? new Uri(address).Port : _configService.Config.Port;
    }

    public async Task StopAsync()
    {
        if (_app is null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
        Port = 0;
    }

    public void Dispose()
    {
        if (_app is not null)
            StopAsync().GetAwaiter().GetResult();
    }

    // ===== Auth =====

    private bool TryAuthorize(HttpContext context)
    {
        var cfg = _configService.Config;
        if (!cfg.AuthEnabled) return true;

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
            var parts = decoded.Split(':', 2);
            if (parts.Length != 2) return false;

            var userOk = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(parts[0]), Encoding.UTF8.GetBytes(cfg.AuthUsername));
            var passOk = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(parts[1]), Encoding.UTF8.GetBytes(cfg.AuthPassword));
            return userOk && passOk;
        }
        catch
        {
            return false;
        }
    }

    // ===== Helpers =====

    private ShareItem? FindRootItem(string id, ShareItemKind kind) =>
        _configService.Config.Items.FirstOrDefault(i => i.Id == id && i.Kind == kind);

    private static bool TryResolveSafePath(string root, string? subpath, out string fullPath)
    {
        var rootFull = Path.GetFullPath(root);
        var combined = string.IsNullOrEmpty(subpath)
            ? rootFull
            : Path.GetFullPath(Path.Combine(rootFull, subpath.Replace('/', Path.DirectorySeparatorChar)));

        if (combined.Equals(rootFull, StringComparison.OrdinalIgnoreCase) ||
            combined.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            fullPath = combined;
            return true;
        }

        fullPath = string.Empty;
        return false;
    }

    private string ResolveContentType(string path) =>
        _contentTypes.TryGetContentType(path, out var contentType) ? contentType : "application/octet-stream";

    private void LogRequest(HttpContext context, int statusCode)
    {
        RequestLogged?.Invoke(this, new AccessLogEntry
        {
            RemoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "?",
            Path = context.Request.Path,
            StatusCode = statusCode,
        });
    }

    // ===== HTML rendering =====

    private const string BaseStyle = """
        <style>
          * { box-sizing: border-box; }
          body {
            margin: 0; padding: 48px 24px; background: #FFFFFF; color: #1D1D1F;
            font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
          }
          .wrap { max-width: 760px; margin: 0 auto; }
          h1 { font-size: 28px; font-weight: 600; letter-spacing: -0.3px; margin: 0 0 6px; }
          p.sub { color: #7A7A7A; margin: 0 0 32px; font-size: 14px; }
          .row {
            display: flex; align-items: center; justify-content: space-between;
            padding: 14px 18px; border: 1px solid #E0E0E0; border-radius: 14px; margin-bottom: 10px;
            text-decoration: none; color: inherit;
          }
          .row:hover { background: #F5F5F7; }
          .name { font-size: 15px; font-weight: 600; }
          .meta { font-size: 12px; color: #7A7A7A; margin-top: 2px; }
          .pill {
            display: inline-block; padding: 8px 16px; border-radius: 999px; font-size: 13px;
            font-weight: 600; text-decoration: none; background: #0066CC; color: #fff; margin-left: 8px;
          }
          .pill.secondary { background: #F5F5F7; color: #0066CC; }
          a.crumb { color: #0066CC; text-decoration: none; font-size: 13px; }
          .actions { display:flex; align-items:center; }
        </style>
        """;

    private string BuildIndexHtml()
    {
        var items = _configService.Config.Items;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"ja\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>FileShare</title>").Append(BaseStyle).Append("</head><body><div class=\"wrap\">");
        sb.Append("<h1>共有ファイル</h1><p class=\"sub\">ダウンロードするファイルまたはフォルダを選んでください。</p>");

        if (items.Count == 0)
            sb.Append("<p class=\"sub\">現在共有されているファイルはありません。</p>");

        foreach (var item in items)
        {
            var name = WebUtility.HtmlEncode(item.Name);
            if (item.Kind == ShareItemKind.File)
            {
                sb.Append($"<a class=\"row\" href=\"/dl/{item.Id}\">");
                sb.Append($"<div><div class=\"name\">{name}</div><div class=\"meta\">{FormatSize(item.SizeBytes)}</div></div>");
                sb.Append("<span class=\"pill\">ダウンロード</span></a>");
            }
            else
            {
                sb.Append($"<div class=\"row\"><a href=\"/browse/{item.Id}/\" style=\"text-decoration:none;color:inherit;flex:1\">");
                sb.Append($"<div class=\"name\">{name}</div><div class=\"meta\">フォルダ</div></a>");
                sb.Append("<div class=\"actions\">");
                sb.Append($"<a class=\"pill secondary\" href=\"/browse/{item.Id}/\">開く</a>");
                sb.Append($"<a class=\"pill\" href=\"/zip/{item.Id}/\">ZIP</a>");
                sb.Append("</div></div>");
            }
        }

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private string BuildBrowseHtml(ShareItem item, string subpath, string fullPath)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"ja\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append($"<title>{WebUtility.HtmlEncode(item.Name)}</title>").Append(BaseStyle).Append("</head><body><div class=\"wrap\">");

        var crumbPath = string.IsNullOrEmpty(subpath) ? item.Name : $"{item.Name}/{subpath}";
        sb.Append($"<h1>{WebUtility.HtmlEncode(Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : item.Name)}</h1>");
        sb.Append($"<p class=\"sub\"><a class=\"crumb\" href=\"/\">共有ファイル</a> / {WebUtility.HtmlEncode(crumbPath)}");
        sb.Append($" &nbsp;<a class=\"pill\" href=\"/zip/{item.Id}/{Uri.EscapeDataString(subpath)}\">このフォルダをZIPでダウンロード</a></p>");

        if (!string.IsNullOrEmpty(subpath))
        {
            var parent = subpath.Contains('/') ? subpath[..subpath.LastIndexOf('/')] : string.Empty;
            sb.Append($"<a class=\"row\" href=\"/browse/{item.Id}/{Uri.EscapeDataString(parent)}\"><div class=\"name\">.. (上へ)</div></a>");
        }

        foreach (var dir in Directory.EnumerateDirectories(fullPath).OrderBy(Path.GetFileName))
        {
            var dirName = Path.GetFileName(dir);
            var childPath = string.IsNullOrEmpty(subpath) ? dirName : $"{subpath}/{dirName}";
            sb.Append($"<a class=\"row\" href=\"/browse/{item.Id}/{Uri.EscapeDataString(childPath)}\">");
            sb.Append($"<div class=\"name\">{WebUtility.HtmlEncode(dirName)}</div><span class=\"pill secondary\">フォルダ</span></a>");
        }

        foreach (var file in Directory.EnumerateFiles(fullPath).OrderBy(Path.GetFileName))
        {
            var fileName = Path.GetFileName(file);
            var childPath = string.IsNullOrEmpty(subpath) ? fileName : $"{subpath}/{fileName}";
            long size = 0;
            try { size = new FileInfo(file).Length; } catch { /* best effort */ }
            sb.Append($"<a class=\"row\" href=\"/file/{item.Id}/{Uri.EscapeDataString(childPath)}\">");
            sb.Append($"<div><div class=\"name\">{WebUtility.HtmlEncode(fileName)}</div><div class=\"meta\">{FormatSize(size)}</div></div>");
            sb.Append("<span class=\"pill\">ダウンロード</span></a>");
        }

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }
}
