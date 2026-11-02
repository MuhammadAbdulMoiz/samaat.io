using System.Text.Json;
using System.Text.Json.Serialization;

namespace Samaat.Core.Settings;

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON. Nothing here throws; failures land in <see cref="LastError"/>.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Samaat", "settings.json");

    public string Path { get; } = path;

    /// <summary>One line describing the last failed Load or Save; null after a success.</summary>
    public string? LastError { get; private set; }

    public AppSettings Load()
    {
        LastError = null;
        if (Directory.Exists(Path))
        {
            LastError = "Settings could not be read: a folder is in the way. Using defaults.";
            return new AppSettings();
        }

        if (!File.Exists(Path))
        {
            return new AppSettings();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Json)
                ?? throw new JsonException("empty settings");
            return loaded.Normalize();
        }
        catch (JsonException)
        {
            LastError = TryKeepCopy()
                ? "Settings file was unreadable; using defaults (the old file was kept as settings.json.bad)."
                : "Settings file was unreadable and could not be set aside; using defaults.";
        }
        catch (Exception e)
        {
            LastError = $"Settings could not be read: {e.Message} Using defaults.";
        }

        return new AppSettings();
    }

    public bool Save(AppSettings settings)
    {
        LastError = null;
        var temp = Path + ".tmp";
        try
        {
            if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
            File.Move(temp, Path, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            LastError = $"Settings could not be saved: {e.Message}";
            try
            {
                File.Delete(temp);
            }
            catch (Exception)
            {
                // Best effort; the save already failed.
            }

            return false;
        }
    }

    private bool TryKeepCopy()
    {
        try
        {
            File.Move(Path, Path + ".bad", overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
