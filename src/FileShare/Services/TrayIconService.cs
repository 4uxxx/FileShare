using System.Windows.Forms;

namespace FileShare.Services;

/// <summary>Wraps a Windows notification-area icon shown while the window is hidden.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        _icon = new NotifyIcon
        {
            Text = "FileShare",
            Visible = false,
        };

        try
        {
            var exePath = Environment.ProcessPath;
            _icon.Icon = exePath is not null
                ? System.Drawing.Icon.ExtractAssociatedIcon(exePath)
                : System.Drawing.SystemIcons.Application;
        }
        catch
        {
            _icon.Icon = System.Drawing.SystemIcons.Application;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("開く", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Show(string? balloonText = null)
    {
        _icon.Visible = true;
        if (balloonText is not null)
            _icon.ShowBalloonTip(3000, "FileShare", balloonText, ToolTipIcon.Info);
    }

    public void Hide() => _icon.Visible = false;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
