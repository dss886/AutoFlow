using System.Runtime.InteropServices;
using System.Windows.Input;

namespace AutoFlow.App.Services;

public sealed class AutomationInputService
{
    private const int DefaultClickHoldMilliseconds = 30;
    private readonly ScreenColorService _screenColorService;
    private readonly object _syncRoot = new();
    private readonly HashSet<string> _pressedMouseButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ushort> _pressedKeys = new();

    public AutomationInputService(ScreenColorService screenColorService)
    {
        _screenColorService = screenColorService ?? throw new ArgumentNullException(nameof(screenColorService));
    }

    public void MoveMouse(int x, int y)
    {
        SetCursorPos(x, y);
    }

    public IReadOnlyList<(int R, int G, int B, string Hex)> GetScreenColors(IReadOnlyList<(int X, int Y)> points)
    {
        return _screenColorService.GetScreenColors(points);
    }

    public void MouseDown(string button)
    {
        var normalizedButton = NormalizeMouseButton(button);
        SendMouseInput(normalizedButton, isDown: true);
        UpdatePressedMouseButton(normalizedButton, isPressed: true);
    }

    public void MouseUp(string button)
    {
        var normalizedButton = NormalizeMouseButton(button);
        SendMouseInput(normalizedButton, isDown: false);
        UpdatePressedMouseButton(normalizedButton, isPressed: false);
    }

    public void Click(string button)
    {
        var normalizedButton = NormalizeMouseButton(button);
        SendMouseInput(normalizedButton, isDown: true);

        // A tiny hold makes the synthetic click more reliable for some targets.
        System.Threading.Thread.Sleep(DefaultClickHoldMilliseconds);

        SendMouseInput(normalizedButton, isDown: false);
    }

    public void PressKey(string keyExpression)
    {
        var keys = NormalizeKeys(keyExpression);
        if (keys.Count == 0)
        {
            throw new InvalidOperationException("未提供有效按键。");
        }

        for (var index = 0; index < keys.Count; index++)
        {
            KeyDown(keys[index]);
        }

        for (var index = keys.Count - 1; index >= 0; index--)
        {
            KeyUp(keys[index]);
        }
    }

    public void KeyDown(string keyExpression)
    {
        var virtualKey = (ushort)ToVirtualKey(keyExpression);
        SendKeyboardInput(virtualKey, isKeyUp: false);
        UpdatePressedKey(virtualKey, isPressed: true);
    }

    public void KeyUp(string keyExpression)
    {
        var virtualKey = (ushort)ToVirtualKey(keyExpression);
        SendKeyboardInput(virtualKey, isKeyUp: true);
        UpdatePressedKey(virtualKey, isPressed: false);
    }

    public void ReleasePressedInputs()
    {
        List<string> pressedMouseButtons;
        List<ushort> pressedKeys;

        lock (_syncRoot)
        {
            pressedMouseButtons = _pressedMouseButtons.ToList();
            pressedKeys = _pressedKeys.ToList();
            _pressedMouseButtons.Clear();
            _pressedKeys.Clear();
        }

        foreach (var button in pressedMouseButtons)
        {
            TryRelease(() => SendMouseInput(button, isDown: false));
        }

        foreach (var key in pressedKeys)
        {
            TryRelease(() => SendKeyboardInput(key, isKeyUp: true));
        }
    }

    private static IReadOnlyList<string> NormalizeKeys(string keyExpression)
    {
        return keyExpression
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
    }

    private static void SendMouseInput(string button, bool isDown)
    {
        var flags = ToMouseEventFlag(button, isDown);

        var input = new INPUT
        {
            type = InputType.Mouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flags,
                },
            },
        };

        SendInputs(new[] { input });
    }

    private void UpdatePressedMouseButton(string button, bool isPressed)
    {
        lock (_syncRoot)
        {
            if (isPressed)
            {
                _pressedMouseButtons.Add(button);
            }
            else
            {
                _pressedMouseButtons.Remove(button);
            }
        }
    }

    private void UpdatePressedKey(ushort virtualKey, bool isPressed)
    {
        lock (_syncRoot)
        {
            if (isPressed)
            {
                _pressedKeys.Add(virtualKey);
            }
            else
            {
                _pressedKeys.Remove(virtualKey);
            }
        }
    }

    private static string NormalizeMouseButton(string button)
    {
        if (string.IsNullOrWhiteSpace(button))
        {
            throw new InvalidOperationException("未提供有效鼠标按键。");
        }

        var normalized = button.Trim().ToLowerInvariant();
        return normalized switch
        {
            "left" or "right" or "middle" => normalized,
            _ => throw new InvalidOperationException($"不支持的鼠标按键: {button}"),
        };
    }

    private static MouseEventFlags ToMouseEventFlag(string button, bool isDown)
    {
        return button switch
        {
            "left" => isDown ? MouseEventFlags.LeftDown : MouseEventFlags.LeftUp,
            "right" => isDown ? MouseEventFlags.RightDown : MouseEventFlags.RightUp,
            "middle" => isDown ? MouseEventFlags.MiddleDown : MouseEventFlags.MiddleUp,
            _ => throw new InvalidOperationException($"不支持的鼠标按键: {button}"),
        };
    }

    private static void SendKeyboardInput(ushort virtualKey, bool isKeyUp)
    {
        var input = new INPUT
        {
            type = InputType.Keyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = isKeyUp ? KeyboardEventFlags.KeyUp : 0,
                },
            },
        };

        SendInputs(new[] { input });
    }

    private static int ToVirtualKey(string keyExpression)
    {
        var normalized = keyExpression.Trim().ToUpperInvariant() switch
        {
            "CTRL" => "LeftCtrl",
            "CONTROL" => "LeftCtrl",
            "ALT" => "LeftAlt",
            "SHIFT" => "LeftShift",
            "WIN" => "LWin",
            "WINDOWS" => "LWin",
            "ENTER" => "Return",
            "ESC" => "Escape",
            "DEL" => "Delete",
            "INS" => "Insert",
            "PGUP" => "PageUp",
            "PGDN" => "PageDown",
            "SPACE" => "Space",
            "TAB" => "Tab",
            "UP" => "Up",
            "DOWN" => "Down",
            "LEFT" => "Left",
            "RIGHT" => "Right",
            _ => keyExpression.Trim(),
        };

        if (!Enum.TryParse<Key>(normalized, ignoreCase: true, out var key))
        {
            throw new InvalidOperationException($"不支持的键盘按键: {keyExpression}");
        }

        return KeyInterop.VirtualKeyFromKey(key);
    }

    private static void SendInputs(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException("发送输入失败。");
        }
    }

    private static void TryRelease(Action releaseAction)
    {
        try
        {
            releaseAction();
        }
        catch
        {
            // 安全收尾阶段尽量释放剩余输入状态，不让单次失败中断后续释放。
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    private enum InputType : uint
    {
        Mouse = 0,
        Keyboard = 1,
    }

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010,
        MiddleDown = 0x0020,
        MiddleUp = 0x0040,
    }

    [Flags]
    private enum KeyboardEventFlags : uint
    {
        KeyUp = 0x0002,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public InputType type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public MouseEventFlags dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public KeyboardEventFlags dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}
