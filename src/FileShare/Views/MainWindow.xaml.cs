using System.ComponentModel;
using System.Windows;
using FileShare.Services;
using FileShare.ViewModels;

namespace FileShare.Views;

public partial class MainWindow : Window
{
    private readonly TrayIconService _tray = new();
    private bool _isExiting;
    private bool _hasShownTrayBalloon;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        _tray.ShowRequested += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        _tray.ExitRequested += (_, _) => Dispatcher.Invoke(ExitApplication);
    }

    /// <summary>
    /// Called when a relaunch is detected: either with a file/folder path from the Explorer
    /// context menu, or empty (e.g. re-clicking the Start Menu icon), which just re-shows the window.
    /// </summary>
    public void HandleExternalPath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && DataContext is MainViewModel vm)
            vm.AddExternalPath(path);
        RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        _tray.Hide();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_isExiting) return;
        if (DataContext is not MainViewModel { CloseToTray: true }) return;

        e.Cancel = true;
        Hide();
        _tray.Show(_hasShownTrayBalloon ? null : "FileShareはバックグラウンドで動作しています。共有は継続されます。");
        _hasShownTrayBalloon = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Dispose();
        base.OnClosed(e);
    }
}
