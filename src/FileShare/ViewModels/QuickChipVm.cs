using FileShare.Helpers;

namespace FileShare.ViewModels;

/// <summary>A one-click "add this pinned folder" chip shown in the GUI.</summary>
public sealed class QuickChipVm : ViewModelBase
{
    public string Path { get; }
    public string Name { get; }
    public bool Exists { get; }

    private bool _isAdded;
    public bool IsAdded
    {
        get => _isAdded;
        set => Set(ref _isAdded, value);
    }

    public QuickChipVm(string path, bool exists, bool isAdded)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(Name)) Name = path;
        Exists = exists;
        _isAdded = isAdded;
    }
}
