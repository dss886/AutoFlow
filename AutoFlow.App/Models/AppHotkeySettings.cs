using System.ComponentModel;
using System.Windows.Input;

namespace AutoFlow.App.Models;

public sealed class AppHotkeySettings
{
    public ShortcutGesture Run { get; set; } = ShortcutGesture.FromKey(Key.F10);

    public ShortcutGesture Stop { get; set; } = ShortcutGesture.FromKey(Key.F12);

    public ShortcutGesture Record { get; set; } = ShortcutGesture.FromGesture(Key.R, ModifierKeys.Control | ModifierKeys.Alt);

    public ShortcutGesture ScreenTool { get; set; } = ShortcutGesture.FromGesture(Key.S, ModifierKeys.Control | ModifierKeys.Alt);

    public static AppHotkeySettings CreateDefault()
    {
        return new AppHotkeySettings();
    }

    public AppHotkeySettings Clone()
    {
        return new AppHotkeySettings
        {
            Run = Run,
            Stop = Stop,
            Record = Record,
            ScreenTool = ScreenTool,
        };
    }
}

public readonly record struct ShortcutGesture(Key Key, ModifierKeys Modifiers)
{
    private static readonly KeyGestureConverter Converter = new();

    public bool IsEmpty => Key == Key.None;

    public string DisplayText => IsEmpty ? "未设置" : ToStorageString(this);

    public static ShortcutGesture FromKey(Key key)
    {
        return new ShortcutGesture(key, ModifierKeys.None);
    }

    public static ShortcutGesture FromGesture(Key key, ModifierKeys modifiers)
    {
        return new ShortcutGesture(key, modifiers);
    }

    public string Serialize()
    {
        return ToStorageString(this);
    }

    public static ShortcutGesture ParseOrDefault(string? value, ShortcutGesture fallback)
    {
        return TryParse(value, out var gesture) ? gesture : fallback;
    }

    public static bool TryParse(string? value, out ShortcutGesture gesture)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            gesture = default;
            return false;
        }

        try
        {
            if (Converter.ConvertFromInvariantString(value) is KeyGesture keyGesture)
            {
                gesture = new ShortcutGesture(keyGesture.Key, keyGesture.Modifiers);
                return gesture.Key != Key.None;
            }
        }
        catch
        {
            // Ignore invalid persisted shortcuts and fall back to defaults.
        }

        gesture = default;
        return false;
    }

    private static string ToStorageString(ShortcutGesture gesture)
    {
        if (gesture.IsEmpty)
        {
            return string.Empty;
        }

        var keyGesture = new KeyGesture(gesture.Key, gesture.Modifiers);
        return Converter.ConvertToInvariantString(keyGesture) ?? string.Empty;
    }
}
