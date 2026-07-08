using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using FileShare.Helpers;
using FileShare.Models;
using FileShare.Services;

namespace FileShare.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ConfigService _config = App.Services.Config;
    private readonly ShareServerService _server = App.Services.Server;
    private readonly TunnelService _tunnel = App.Services.Tunnel;

    public ObservableCollection<ShareItem> Items { get; } = new();
    public ObservableCollection<QuickChipVm> QuickChips { get; } = new();
    public ObservableCollection<AccessLogEntry> Logs { get; } = new();

    private bool _isSharing;
    public bool IsSharing
    {
        get => _isSharing;
        private set => Set(ref _isSharing, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    private string _statusText = "共有は停止中です。";
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    private string? _publicUrl;
    public string? PublicUrl
    {
        get => _publicUrl;
        private set => Set(ref _publicUrl, value);
    }

    private BitmapImage? _qrImage;
    public BitmapImage? QrImage
    {
        get => _qrImage;
        private set => Set(ref _qrImage, value);
    }

    public bool AuthEnabled
    {
        get => _config.Config.AuthEnabled;
        set
        {
            _config.Config.AuthEnabled = value;
            _config.Save();
            Raise();
        }
    }

    public string AuthUsername
    {
        get => _config.Config.AuthUsername;
        set
        {
            _config.Config.AuthUsername = string.IsNullOrWhiteSpace(value) ? "share" : value.Trim();
            _config.Save();
            Raise();
        }
    }

    public string AuthPassword
    {
        get => _config.Config.AuthPassword;
        set
        {
            _config.Config.AuthPassword = value;
            _config.Save();
            Raise();
        }
    }

    public bool IsCloudflareMode
    {
        get => _config.Config.TunnelMode == TunnelMode.CloudflareQuick;
        set
        {
            if (!value) return;
            _config.Config.TunnelMode = TunnelMode.CloudflareQuick;
            _config.Save();
            Raise();
            Raise(nameof(IsTailscaleMode));
        }
    }

    public bool IsTailscaleMode
    {
        get => _config.Config.TunnelMode == TunnelMode.TailscaleFunnel;
        set
        {
            if (!value) return;
            _config.Config.TunnelMode = TunnelMode.TailscaleFunnel;
            _config.Save();
            Raise();
            Raise(nameof(IsCloudflareMode));
        }
    }

    public bool CloseToTray
    {
        get => _config.Config.CloseToTray;
        set
        {
            _config.Config.CloseToTray = value;
            _config.Save();
            Raise();
        }
    }

    public RelayCommand ToggleShareCommand { get; }
    public RelayCommand AddFileCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand<ShareItem> RemoveItemCommand { get; }
    public RelayCommand<QuickChipVm> QuickAddCommand { get; }
    public RelayCommand<ShareItem> CopyItemLinkCommand { get; }
    public RelayCommand CopyUrlCommand { get; }
    public RelayCommand RegeneratePasswordCommand { get; }

    public MainViewModel()
    {
        foreach (var item in _config.Config.Items) Items.Add(item);
        RefreshQuickChips();

        ToggleShareCommand = new RelayCommand(async () => await ToggleShareAsync(), () => !IsBusy);
        AddFileCommand = new RelayCommand(AddFile, () => !IsSharing);
        AddFolderCommand = new RelayCommand(AddFolder, () => !IsSharing);
        RemoveItemCommand = new RelayCommand<ShareItem>(RemoveItem, _ => !IsSharing);
        QuickAddCommand = new RelayCommand<QuickChipVm>(QuickAdd, c => c?.Exists == true && !IsSharing);
        CopyItemLinkCommand = new RelayCommand<ShareItem>(CopyItemLink, _ => IsSharing);
        CopyUrlCommand = new RelayCommand(() => { if (PublicUrl is not null) Clipboard.SetText(PublicUrl); });
        RegeneratePasswordCommand = new RelayCommand(() =>
        {
            AuthPassword = ConfigService.GenerateRandomPassword();
        });

        _server.RequestLogged += (_, entry) => Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Insert(0, entry);
            while (Logs.Count > 50) Logs.RemoveAt(Logs.Count - 1);
        });

        _tunnel.StatusChanged += (_, message) => Application.Current.Dispatcher.Invoke(() => StatusText = message);
    }

    private async Task ToggleShareAsync()
    {
        if (IsSharing)
        {
            IsBusy = true;
            StatusText = "共有を停止しています…";
            try
            {
                await _tunnel.StopAsync();
                await _server.StopAsync();
            }
            finally
            {
                IsSharing = false;
                IsBusy = false;
                PublicUrl = null;
                QrImage = null;
                StatusText = "共有は停止中です。";
                RaiseCommandStates();
            }
            return;
        }

        if (Items.Count == 0)
        {
            MessageBox.Show("共有するファイルまたはフォルダを追加してください。", "FileShare",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsBusy = true;
        StatusText = "サーバーを起動しています…";
        RaiseCommandStates();
        try
        {
            _config.Save();
            await _server.StartAsync();

            StatusText = "インターネット公開用のトンネルに接続しています…";
            var url = await _tunnel.StartAsync(_server.Port);
            if (url is null)
            {
                var reason = StatusText;
                MessageBox.Show(
                    $"インターネット公開用のトンネルに接続できませんでした。\n\n{reason}",
                    "FileShare", MessageBoxButton.OK, MessageBoxImage.Error);
                await _server.StopAsync();
                StatusText = "共有は停止中です。";
                return;
            }

            PublicUrl = url;
            QrImage = QrCodeService.Generate(url);
            IsSharing = true;
            StatusText = "共有中です。";
        }
        finally
        {
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private void AddFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "共有するファイルを選択",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
            AddItem(path, ShareItemKind.File);

        RefreshQuickChips();
    }

    private void AddFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "共有するフォルダを選択",
        };
        if (dlg.ShowDialog() != true) return;

        AddItem(dlg.FolderName, ShareItemKind.Folder);
        RefreshQuickChips();
    }

    private void QuickAdd(QuickChipVm? chip)
    {
        if (chip is null) return;

        var existing = Items.FirstOrDefault(i => string.Equals(i.Path, chip.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RemoveItem(existing);
            return;
        }

        var kind = Directory.Exists(chip.Path) ? ShareItemKind.Folder : ShareItemKind.File;
        AddItem(chip.Path, kind);
        RefreshQuickChips();
    }

    /// <summary>Adds a path received from the Explorer "FileShareで共有" context menu.</summary>
    public void AddExternalPath(string path)
    {
        if (IsSharing)
        {
            MessageBox.Show("共有中はアイテムを追加できません。先に共有を停止してください。", "FileShare",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Directory.Exists(path)) AddItem(path, ShareItemKind.Folder);
        else if (File.Exists(path)) AddItem(path, ShareItemKind.File);
        else return;

        RefreshQuickChips();
    }

    private void AddItem(string path, ShareItemKind kind)
    {
        if (Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) return;

        long size = 0;
        if (kind == ShareItemKind.File)
        {
            try { size = new FileInfo(path).Length; } catch { /* best effort */ }
        }

        var item = new ShareItem
        {
            Path = path,
            Name = kind == ShareItemKind.Folder
                ? Path.GetFileName(path.TrimEnd('\\', '/'))
                : Path.GetFileName(path),
            Kind = kind,
            SizeBytes = size,
        };
        if (string.IsNullOrEmpty(item.Name)) item.Name = path;

        Items.Add(item);
        _config.Config.Items.Add(item);
        _config.Save();
    }

    private void CopyItemLink(ShareItem? item)
    {
        if (item is null || PublicUrl is null) return;
        var baseUrl = PublicUrl.TrimEnd('/');
        var link = item.Kind == ShareItemKind.File ? $"{baseUrl}/dl/{item.Id}" : $"{baseUrl}/browse/{item.Id}/";
        Clipboard.SetText(link);
    }

    private void RemoveItem(ShareItem? item)
    {
        if (item is null) return;
        Items.Remove(item);
        _config.Config.Items.RemoveAll(i => i.Id == item.Id);
        _config.Save();
        RefreshQuickChips();
    }

    private void RefreshQuickChips()
    {
        QuickChips.Clear();
        foreach (var path in _config.Config.PinnedPaths)
        {
            var exists = File.Exists(path) || Directory.Exists(path);
            var isAdded = Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            QuickChips.Add(new QuickChipVm(path, exists, isAdded));
        }
    }

    private void RaiseCommandStates()
    {
        ToggleShareCommand.RaiseCanExecuteChanged();
        AddFileCommand.RaiseCanExecuteChanged();
        AddFolderCommand.RaiseCanExecuteChanged();
        RemoveItemCommand.RaiseCanExecuteChanged();
        QuickAddCommand.RaiseCanExecuteChanged();
        CopyItemLinkCommand.RaiseCanExecuteChanged();
    }
}
