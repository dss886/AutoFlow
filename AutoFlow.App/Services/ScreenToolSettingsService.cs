using System.IO;
using System.Text.Json;

namespace AutoFlow.App.Services;

public static class ScreenToolSettingsService
{
    private const string SettingsFileName = "screen-tool-settings.json";

    public static ScreenToolColorDisplayFormat LoadColorDisplayFormat()
    {
        try
        {
            var settingsFilePath = GetSettingsFilePath();
            if (!File.Exists(settingsFilePath))
            {
                return ScreenToolColorDisplayFormat.Hex;
            }

            var json = File.ReadAllText(settingsFilePath);
            var settings = JsonSerializer.Deserialize<ScreenToolSettings>(json);
            return settings?.ColorDisplayFormat ?? ScreenToolColorDisplayFormat.Hex;
        }
        catch
        {
            return ScreenToolColorDisplayFormat.Hex;
        }
    }

    public static void SaveColorDisplayFormat(ScreenToolColorDisplayFormat colorDisplayFormat)
    {
        try
        {
            var configDirectory = PathService.ResolveAppDataDirectory();
            PathService.EnsureDirectory(configDirectory);

            var settings = new ScreenToolSettings(colorDisplayFormat);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            File.WriteAllText(GetSettingsFilePath(), json);
        }
        catch
        {
            // Keep the screen tool usable even if local settings cannot be written.
        }
    }

    private static string GetSettingsFilePath()
    {
        return Path.Combine(PathService.ResolveAppDataDirectory(), SettingsFileName);
    }

    private sealed record ScreenToolSettings(ScreenToolColorDisplayFormat ColorDisplayFormat);
}

public enum ScreenToolColorDisplayFormat
{
    Hex,
    Rgb,
}
