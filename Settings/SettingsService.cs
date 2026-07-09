using System.IO;
using System.Text.Json;

namespace TouhouScalePad.Settings;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsPath { get; } = Path.Combine(AppContext.BaseDirectory, "TouhouScalePad.settings.json");
    private string LegacySettingsPath { get; } = Path.Combine(AppContext.BaseDirectory, "ScalePad.settings.json");

    public AppSettings Load()
    {
        try
        {
            var sourcePath = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
            if (!File.Exists(sourcePath))
            {
                return AppSettings.CreateDefault();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(sourcePath), SerializerOptions);
            if (settings is null)
            {
                return AppSettings.CreateDefault();
            }

            MergeBuiltInPresets(settings);
            settings.PollingIntervalMilliseconds = Math.Clamp(settings.PollingIntervalMilliseconds, 1, 4);
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, SettingsPath, true);
    }

    private static void MergeBuiltInPresets(AppSettings settings)
    {
        settings.SizePresets ??= [];
        settings.GameProfiles ??= [];

        foreach (var preset in AppSettings.CreateDefault().SizePresets)
        {
            if (settings.SizePresets.Any(item => item.Width == preset.Width && item.Height == preset.Height))
            {
                continue;
            }

            settings.SizePresets.Add(preset);
        }
    }
}
