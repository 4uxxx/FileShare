using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FileShare.Models;

namespace FileShare.Services;

/// <summary>Loads and saves <see cref="AppConfig"/> to %AppData%\FileShare\config.json.</summary>
public sealed class ConfigService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileShare");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppConfig Config { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded is not null) Config = loaded;
            }
        }
        catch
        {
            Config = new AppConfig();
        }

        if (string.IsNullOrEmpty(Config.AuthPassword))
            Config.AuthPassword = GenerateRandomPassword();

        // Drop items whose backing path no longer exists.
        Config.Items.RemoveAll(i => !File.Exists(i.Path) && !Directory.Exists(i.Path));

        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(Config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Best-effort persistence; a failed save should never crash the app.
        }
    }

    public static string GenerateRandomPassword(int length = 10)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> buffer = stackalloc byte[length];
        RandomNumberGenerator.Fill(buffer);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = alphabet[buffer[i] % alphabet.Length];
        return new string(chars);
    }
}
