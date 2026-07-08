using Microsoft.Win32;

namespace FileShare.Services;

/// <summary>
/// Registers/unregisters the Explorer right-click "FileShareで共有" menu entry for
/// files, folders, and a folder's empty background (per-user, HKCU — no admin needed).
/// </summary>
public static class ShellIntegrationService
{
    private const string MenuText = "FileShareで共有";
    private const string KeyName = "FileShare.Share";

    private static readonly (string KeyPath, string ArgTemplate)[] Targets =
    {
        (@"Software\Classes\*\shell\" + KeyName, "\"%1\""),
        (@"Software\Classes\Directory\shell\" + KeyName, "\"%1\""),
        (@"Software\Classes\Directory\Background\shell\" + KeyName, "\"%V\""),
    };

    public static void Register()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            foreach (var (keyPath, argTemplate) in Targets)
            {
                using var key = Registry.CurrentUser.CreateSubKey(keyPath);
                key.SetValue(null, MenuText);
                key.SetValue("Icon", $"\"{exePath}\"");
                using var cmdKey = key.CreateSubKey("command");
                cmdKey.SetValue(null, $"\"{exePath}\" {argTemplate}");
            }
        }
        catch
        {
            // Best-effort; the app still works fine without the context menu entry.
        }
    }

    public static void Unregister()
    {
        foreach (var (keyPath, _) in Targets)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false); }
            catch { /* best effort */ }
        }
    }
}
