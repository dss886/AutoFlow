using System.ComponentModel;
using System.Windows.Input;

namespace AutoFlow.App.Models;

public sealed class AppHotkeySettings
{
    public ShortcutGesture Run { get; set; } = ShortcutGesture.FromKey(Key.F10);

    public ShortcutGesture Stop { get; set; } = ShortcutGesture.FromKey(Key.F11);

    public ShortcutGesture Record { get; set; } = ShortcutGesture.FromKey(Key.F12);

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

public enum ShortcutMouseButton
{
    None,
    Middle,
    XButton1,
    XButton2,
}

public readonly record struct ShortcutGesture(Key Key, ModifierKeys Modifiers, ShortcutMouseButton MouseButton = ShortcutMouseButton.None)
{
    private static readonly KeyGestureConverter Converter = new();

    public bool IsEmpty => Key == Key.None && MouseButton == ShortcutMouseButton.None;

    public bool IsKeyboard => Key != Key.None;

    public bool IsMouse => MouseButton != ShortcutMouseButton.None;

    public string DisplayText => IsEmpty ? "未设置" : ToDisplayString(this);

    public static ShortcutGesture FromKey(Key key)
    {
        return new ShortcutGesture(key, ModifierKeys.None);
    }

    public static ShortcutGesture FromGesture(Key key, ModifierKeys modifiers)
    {
        return new ShortcutGesture(key, modifiers);
    }

    public static ShortcutGesture FromMouseGesture(ShortcutMouseButton mouseButton, ModifierKeys modifiers = ModifierKeys.None)
    {
        return new ShortcutGesture(Key.None, modifiers, mouseButton);
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
            if (value.StartsWith("Mouse:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var buttonValue = parts[0]["Mouse:".Length..];
                if (!Enum.TryParse<ShortcutMouseButton>(buttonValue, ignoreCase: true, out var mouseButton)
                    || mouseButton == ShortcutMouseButton.None)
                {
                    gesture = default;
                    return false;
                }

                var modifiers = ModifierKeys.None;
                if (parts.Length > 1 && parts[1].StartsWith("Modifiers:", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers = ParseModifiers(parts[1]["Modifiers:".Length..]);
                }

                gesture = new ShortcutGesture(Key.None, modifiers, mouseButton);
                return true;
            }

            if (Converter.ConvertFromInvariantString(value) is KeyGesture keyGesture)
            {
                gesture = new ShortcutGesture(keyGesture.Key, keyGesture.Modifiers, ShortcutMouseButton.None);
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

        if (gesture.IsMouse)
        {
            return $"Mouse:{gesture.MouseButton};Modifiers:{SerializeModifiers(gesture.Modifiers)}";
        }

        var keyGesture = new KeyGesture(gesture.Key, gesture.Modifiers);
        return Converter.ConvertToInvariantString(keyGesture) ?? string.Empty;
    }

    private static string ToDisplayString(ShortcutGesture gesture)
    {
        if (gesture.IsMouse)
        {
            var parts = GetModifierDisplayParts(gesture.Modifiers);
            parts.Add(GetMouseButtonDisplayText(gesture.MouseButton));
            return string.Join("+", parts);
        }

        var keyGesture = new KeyGesture(gesture.Key, gesture.Modifiers);
        return Converter.ConvertToInvariantString(keyGesture) ?? string.Empty;
    }

    private static string SerializeModifiers(ModifierKeys modifiers)
    {
        var normalized = NormalizeModifiers(modifiers);
        if (normalized == ModifierKeys.None)
        {
            return "None";
        }

        return normalized.ToString();
    }

    private static ModifierKeys ParseModifiers(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            return ModifierKeys.None;
        }

        var modifiers = ModifierKeys.None;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<ModifierKeys>(part, ignoreCase: true, out var modifier))
            {
                modifiers |= modifier;
            }
        }

        return NormalizeModifiers(modifiers);
    }

    private static ModifierKeys NormalizeModifiers(ModifierKeys modifiers)
    {
        return modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows);
    }

    private static List<string> GetModifierDisplayParts(ModifierKeys modifiers)
    {
        var normalized = NormalizeModifiers(modifiers);
        var parts = new List<string>();

        if (normalized.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (normalized.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (normalized.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (normalized.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        return parts;
    }

    private static string GetMouseButtonDisplayText(ShortcutMouseButton mouseButton)
    {
        return mouseButton switch
        {
            ShortcutMouseButton.Middle => "鼠标中键",
            ShortcutMouseButton.XButton1 => "鼠标侧键1",
            ShortcutMouseButton.XButton2 => "鼠标侧键2",
            _ => "鼠标按键",
        };
    }
}
