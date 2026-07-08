using System.IO;
using System.IO.Pipes;

namespace FileShare.Services;

/// <summary>
/// Ensures only one FileShare instance runs at a time. A second launch (e.g. from the
/// Explorer "FileShareで共有" context menu) forwards its path argument to the first
/// instance over a named pipe and then exits.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "FileShare-SingleInstance-9F1C2E7A";
    private const string PipeName = "FileShare-IPC-9F1C2E7A";

    private Mutex? _mutex;
    private CancellationTokenSource? _serverCts;

    public event EventHandler<string>? PathReceived;

    /// <summary>
    /// Returns true if this process becomes the primary instance. Returns false if another
    /// instance is already running (the given path, if any, has been forwarded to it).
    /// </summary>
    public bool AcquireOrForward(string? path)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            StartServer();
            return true;
        }

        // Always notify the running instance so it comes to the foreground, even when
        // launched with no path (e.g. re-clicking the Start Menu / taskbar icon).
        TryForward(path ?? string.Empty);

        return false;
    }

    private static void TryForward(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(path);
        }
        catch
        {
            // Best-effort; if forwarding fails there's nothing more we can do here.
        }
    }

    private void StartServer()
    {
        _serverCts = new CancellationTokenSource();
        _ = ServerLoopAsync(_serverCts.Token);
    }

    private async Task ServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(ct);
                // Raised even for an empty line: that's a "just come to the foreground" signal
                // from a relaunch with no file/folder argument.
                PathReceived?.Invoke(this, line ?? string.Empty);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep listening even if a single connection attempt failed.
            }
        }
    }

    public void Dispose()
    {
        _serverCts?.Cancel();
        try { _mutex?.ReleaseMutex(); } catch { /* not owned, ignore */ }
        _mutex?.Dispose();
    }
}
