using System.Windows;
using FileShare.Services;
using Velopack;
using Velopack.Sources;

namespace FileShare;

public partial class App : Application
{
    private const string UpdateRepoUrl = "https://github.com/4uxxx/FileShare";

    /// <summary>Global service container, created once at startup.</summary>
    public static AppServices Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services = new AppServices();
        Services.Load();

        _ = CheckForUpdatesAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.Shutdown();
        base.OnExit(e);
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(UpdateRepoUrl, accessToken: null, prerelease: false));
            if (!manager.IsInstalled) return;

            var updateInfo = await manager.CheckForUpdatesAsync();
            if (updateInfo is null) return;

            await manager.DownloadUpdatesAsync(updateInfo);

            var result = MessageBox.Show(
                $"新しいバージョン {updateInfo.TargetFullRelease.Version} が利用可能です。今すぐ更新して再起動しますか?",
                "FileShare", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
                manager.ApplyUpdatesAndRestart(updateInfo);
        }
        catch
        {
            // Offline, GitHub unreachable, or running outside of an installed build (dev/debug) — ignore.
        }
    }
}
