using System.IO;
using System.Text.Json;

namespace AutoFlow.App.Services;

public static class LocalSettingsService
{
    private const string SettingsFileName = "config.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static ScreenToolColorDisplayFormat LoadScreenToolColorDisplayFormat()
    {
        try
        {
            var settings = LoadSettings();
            return settings.ScreenTool?.ColorDisplayFormat ?? ScreenToolColorDisplayFormat.Hex;
        }
        catch
        {
            return ScreenToolColorDisplayFormat.Hex;
        }
    }

    public static void SaveScreenToolColorDisplayFormat(ScreenToolColorDisplayFormat colorDisplayFormat)
    {
        try
        {
            var settings = LoadSettings();
            settings.ScreenTool ??= new ScreenToolSettings();
            settings.ScreenTool.ColorDisplayFormat = colorDisplayFormat;
            SaveSettings(settings);
        }
        catch
        {
            // Keep the screen tool usable even if local settings cannot be written.
        }
    }

    public static LocalWindowPlacement? LoadWindowPlacement()
    {
        try
        {
            var settings = LoadSettings();
            return settings.WindowPlacement;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveWindowPlacement(LocalWindowPlacement windowPlacement)
    {
        try
        {
            var settings = LoadSettings();
            settings.WindowPlacement = windowPlacement;
            SaveSettings(settings);
        }
        catch
        {
            // Keep shutdown resilient even when local settings cannot be written.
        }
    }

    private static LocalSettings LoadSettings()
    {
        var settingsFilePath = GetSettingsFilePath();
        if (!File.Exists(settingsFilePath))
        {
            return new LocalSettings();
        }

        var json = File.ReadAllText(settingsFilePath);
        return JsonSerializer.Deserialize<LocalSettings>(json) ?? new LocalSettings();
    }

    private static void SaveSettings(LocalSettings settings)
    {
        var configDirectory = PathService.ResolveAppDataDirectory();
        PathService.EnsureDirectory(configDirectory);
        File.WriteAllText(GetSettingsFilePath(), JsonSerializer.Serialize(settings, SerializerOptions));
    }

    private static string GetSettingsFilePath()
    {
        return Path.Combine(PathService.ResolveAppDataDirectory(), SettingsFileName);
    }

    private sealed class LocalSettings
    {
        public ScreenToolSettings? ScreenTool { get; set; }

        public LocalWindowPlacement? WindowPlacement { get; set; }
    }

    private sealed class ScreenToolSettings
    {
        public ScreenToolColorDisplayFormat? ColorDisplayFormat { get; set; }
    }
}

public sealed record LocalWindowPlacement(int Left, int Top, int Width, int Height);

public enum ScreenToolColorDisplayFormat
{
    Hex,
    Rgb,
}
