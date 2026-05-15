using System.IO;
using System.Text.Json;
using AutoFlow.App.Models;

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

    public static AppHotkeySettings LoadHotkeySettings()
    {
        var defaults = AppHotkeySettings.CreateDefault();

        try
        {
            var settings = LoadSettings();
            return new AppHotkeySettings
            {
                Run = ShortcutGesture.ParseOrDefault(settings.Hotkeys?.Run, defaults.Run),
                Stop = ShortcutGesture.ParseOrDefault(settings.Hotkeys?.Stop, defaults.Stop),
                Record = ShortcutGesture.ParseOrDefault(settings.Hotkeys?.Record, defaults.Record),
                ScreenTool = ShortcutGesture.ParseOrDefault(settings.Hotkeys?.ScreenTool, defaults.ScreenTool),
            };
        }
        catch
        {
            return defaults;
        }
    }

    public static void SaveHotkeySettings(AppHotkeySettings hotkeySettings)
    {
        ArgumentNullException.ThrowIfNull(hotkeySettings);

        try
        {
            var settings = LoadSettings();
            settings.Hotkeys ??= new HotkeySettings();
            settings.Hotkeys.Run = hotkeySettings.Run.Serialize();
            settings.Hotkeys.Stop = hotkeySettings.Stop.Serialize();
            settings.Hotkeys.Record = hotkeySettings.Record.Serialize();
            settings.Hotkeys.ScreenTool = hotkeySettings.ScreenTool.Serialize();
            SaveSettings(settings);
        }
        catch
        {
            // Keep hotkey customization usable even when local settings cannot be written.
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

        public HotkeySettings? Hotkeys { get; set; }
    }

    private sealed class ScreenToolSettings
    {
        public ScreenToolColorDisplayFormat? ColorDisplayFormat { get; set; }
    }

    private sealed class HotkeySettings
    {
        public string? Run { get; set; }

        public string? Stop { get; set; }

        public string? Record { get; set; }

        public string? ScreenTool { get; set; }
    }
}

public sealed record LocalWindowPlacement(int Left, int Top, int Width, int Height);

public enum ScreenToolColorDisplayFormat
{
    Hex,
    Rgb,
}
