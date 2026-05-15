using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using AutoFlow.App.Models;

namespace AutoFlow.App.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HcAction = 0;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint VkR = 0x52;
    private const uint VkLShift = 0xA0;
    private const uint VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly Action<string> _logMessage;
    private readonly HookProc _mainKeyboardHookProc;
    private readonly HookProc _screenToolKeyboardHookProc;
    private AppHotkeySettings _hotkeySettings = AppHotkeySettings.CreateDefault();
    private bool _isDisposed;
    private bool _isScreenToolShortcutsEnabled;
    private bool _isRunHotkeyPressed;
    private bool _isStopHotkeyPressed;
    private bool _isRecordHotkeyPressed;
    private bool _isScreenToolHotkeyPressed;
    private bool _isScreenToolRecordKeyPressed;
    private bool _isScreenToolShiftKeyPressed;
    private IntPtr _mainKeyboardHookHandle;
    private IntPtr _screenToolKeyboardHookHandle;
    private Window? _owner;

    public GlobalHotkeyService(Action<string> logMessage)
    {
        _logMessage = logMessage ?? throw new ArgumentNullException(nameof(logMessage));
        _mainKeyboardHookProc = MainKeyboardHookCallback;
        _screenToolKeyboardHookProc = ScreenToolKeyboardHookCallback;
    }

    public event Action? RunRequested;

    public event Action? StopRequested;

    public event Action? RecordRequested;

    public event Action? ScreenToolToggleRequested;

    public event Action? ScreenToolRecordRequested;

    public event Action? ScreenToolColorDisplayToggleRequested;

    public void Initialize(Window owner)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(owner);

        if (_owner is not null)
        {
            return;
        }

        _owner = owner;
        ReloadConfiguredHotkeys(showFailureMessage: false);
        InstallMainKeyboardHook();
    }

    public void ReloadConfiguredHotkeys(bool showFailureMessage)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        _hotkeySettings = LocalSettingsService.LoadHotkeySettings().Clone();
        ResetAllShortcutStates();

        if (showFailureMessage)
        {
            _logMessage("快捷键设置已刷新。");
        }
    }

    public bool HandlePreviewKeyDown(Key key)
    {
        return _isScreenToolShortcutsEnabled
            && TryHandleScreenToolShortcut(NormalizeKey(key), isKeyDown: true);
    }

    public void HandlePreviewKeyUp(Key key)
    {
        if (_isScreenToolShortcutsEnabled)
        {
            ReleaseScreenToolShortcutState(NormalizeKey(key));
        }
    }

    public bool HandleGlobalMouseButtonDown(ShortcutMouseButton mouseButton)
    {
        return TryHandleConfiguredHotkeys(mouseButton, GetCurrentModifiers());
    }

    public bool HandleGlobalMouseButtonUp(ShortcutMouseButton mouseButton)
    {
        return ReleaseConfiguredHotkeyStates(mouseButton);
    }

    public void SetScreenToolShortcutsEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isScreenToolShortcutsEnabled == enabled)
        {
            return;
        }

        _isScreenToolShortcutsEnabled = enabled;
        ResetScreenToolShortcutStates();

        if (enabled)
        {
            InstallScreenToolKeyboardHook();
            return;
        }

        RemoveScreenToolKeyboardHook();
    }

    public void CleanupAllRegistrations()
    {
        ResetAllShortcutStates();
        RemoveScreenToolKeyboardHook();
        RemoveMainKeyboardHook();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        CleanupAllRegistrations();
        _owner = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private void InstallMainKeyboardHook()
    {
        if (_mainKeyboardHookHandle != IntPtr.Zero)
        {
            return;
        }

        var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
        var moduleHandle = GetModuleHandle(moduleName);
        _mainKeyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _mainKeyboardHookProc, moduleHandle, 0);
        if (_mainKeyboardHookHandle != IntPtr.Zero)
        {
            _logMessage("已启用主功能全局快捷键监听。");
            return;
        }

        var errorCode = Marshal.GetLastWin32Error();
        _logMessage($"主功能全局快捷键监听启用失败，错误代码: {errorCode}");
    }

    private void RemoveMainKeyboardHook()
    {
        if (_mainKeyboardHookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_mainKeyboardHookHandle);
        _mainKeyboardHookHandle = IntPtr.Zero;
    }

    private void InstallScreenToolKeyboardHook()
    {
        if (_screenToolKeyboardHookHandle != IntPtr.Zero)
        {
            return;
        }

        var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
        var moduleHandle = GetModuleHandle(moduleName);
        _screenToolKeyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _screenToolKeyboardHookProc, moduleHandle, 0);
        if (_screenToolKeyboardHookHandle != IntPtr.Zero)
        {
            _logMessage("已启用屏幕工具专用快捷键监听。");
            return;
        }

        var errorCode = Marshal.GetLastWin32Error();
        _logMessage($"屏幕工具专用快捷键监听启用失败，错误代码: {errorCode}");
    }

    private void RemoveScreenToolKeyboardHook()
    {
        if (_screenToolKeyboardHookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_screenToolKeyboardHookHandle);
        _screenToolKeyboardHookHandle = IntPtr.Zero;
        _logMessage("已取消屏幕工具专用快捷键监听。");
    }

    private IntPtr MainKeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != HcAction)
        {
            return CallNextHookEx(_mainKeyboardHookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var keyboardData = Marshal.PtrToStructure<KeyboardHookData>(lParam);
        var isKeyDown = message is WmKeyDown or WmSysKeyDown;
        var isKeyUp = message is WmKeyUp or WmSysKeyUp;

        if (!isKeyDown && !isKeyUp)
        {
            return CallNextHookEx(_mainKeyboardHookHandle, nCode, wParam, lParam);
        }

        var key = NormalizeKey(KeyInterop.KeyFromVirtualKey((int)keyboardData.VkCode));
        if (isKeyDown && TryHandleConfiguredHotkeys(key, GetCurrentModifiers()))
        {
            return new IntPtr(1);
        }

        if (isKeyUp)
        {
            return ReleaseConfiguredHotkeyStates(key)
                ? new IntPtr(1)
                : CallNextHookEx(_mainKeyboardHookHandle, nCode, wParam, lParam);
        }

        return CallNextHookEx(_mainKeyboardHookHandle, nCode, wParam, lParam);
    }

    private IntPtr ScreenToolKeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != HcAction)
        {
            return CallNextHookEx(_screenToolKeyboardHookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var keyboardData = Marshal.PtrToStructure<KeyboardHookData>(lParam);
        var isKeyDown = message is WmKeyDown or WmSysKeyDown;
        var isKeyUp = message is WmKeyUp or WmSysKeyUp;

        if (!isKeyDown && !isKeyUp)
        {
            return CallNextHookEx(_screenToolKeyboardHookHandle, nCode, wParam, lParam);
        }

        var key = NormalizeKey(KeyInterop.KeyFromVirtualKey((int)keyboardData.VkCode));
        if (isKeyDown && TryHandleScreenToolShortcut(key, isKeyDown: true))
        {
            return new IntPtr(1);
        }

        if (isKeyUp)
        {
            return ReleaseScreenToolShortcutState(key)
                ? new IntPtr(1)
                : CallNextHookEx(_screenToolKeyboardHookHandle, nCode, wParam, lParam);
        }

        return CallNextHookEx(_screenToolKeyboardHookHandle, nCode, wParam, lParam);
    }

    private bool TryHandleConfiguredHotkeys(Key key, ModifierKeys modifiers)
    {
        return TryHandleConfiguredHotkey(_hotkeySettings.Run, key, modifiers, ref _isRunHotkeyPressed, RunRequested)
            || TryHandleConfiguredHotkey(_hotkeySettings.Stop, key, modifiers, ref _isStopHotkeyPressed, StopRequested)
            || TryHandleConfiguredHotkey(_hotkeySettings.Record, key, modifiers, ref _isRecordHotkeyPressed, RecordRequested)
            || TryHandleConfiguredHotkey(_hotkeySettings.ScreenTool, key, modifiers, ref _isScreenToolHotkeyPressed, ScreenToolToggleRequested);
    }

    private bool TryHandleConfiguredHotkeys(ShortcutMouseButton mouseButton, ModifierKeys modifiers)
    {
        return TryHandleConfiguredHotkey(_hotkeySettings.Run, mouseButton, modifiers, ref _isRunHotkeyPressed, RunRequested)
            || TryHandleConfiguredHotkey(_hotkeySettings.Stop, mouseButton, modifiers, ref _isStopHotkeyPressed, StopRequested)
            || TryHandleConfiguredHotkey(_hotkeySettings.Record, mouseButton, modifiers, ref _isRecordHotkeyPressed, RecordRequested)
            || TryHandleConfiguredHotkey(_hotkeySettings.ScreenTool, mouseButton, modifiers, ref _isScreenToolHotkeyPressed, ScreenToolToggleRequested);
    }

    private bool TryHandleConfiguredHotkey(
        ShortcutGesture gesture,
        Key key,
        ModifierKeys modifiers,
        ref bool pressedState,
        Action? callback)
    {
        if (!IsConfiguredHotkeyMatch(gesture, key, modifiers))
        {
            return false;
        }

        if (pressedState)
        {
            return true;
        }

        pressedState = true;
        Dispatch(callback);
        return true;
    }

    private bool TryHandleConfiguredHotkey(
        ShortcutGesture gesture,
        ShortcutMouseButton mouseButton,
        ModifierKeys modifiers,
        ref bool pressedState,
        Action? callback)
    {
        if (!IsConfiguredHotkeyMatch(gesture, mouseButton, modifiers))
        {
            return false;
        }

        if (pressedState)
        {
            return true;
        }

        pressedState = true;
        Dispatch(callback);
        return true;
    }

    private bool TryHandleScreenToolShortcut(Key key, bool isKeyDown)
    {
        return key switch
        {
            Key.R => HandleScreenToolRecordShortcut(isKeyDown),
            Key.LeftShift or Key.RightShift => HandleScreenToolShiftShortcut(isKeyDown),
            _ => false,
        };
    }

    private bool HandleScreenToolRecordShortcut(bool isKeyDown)
    {
        if (!isKeyDown)
        {
            _isScreenToolRecordKeyPressed = false;
            return true;
        }

        if (_isScreenToolRecordKeyPressed)
        {
            return true;
        }

        _isScreenToolRecordKeyPressed = true;
        Dispatch(ScreenToolRecordRequested);
        return true;
    }

    private bool HandleScreenToolShiftShortcut(bool isKeyDown)
    {
        if (!isKeyDown)
        {
            _isScreenToolShiftKeyPressed = false;
            return true;
        }

        if (_isScreenToolShiftKeyPressed)
        {
            return true;
        }

        _isScreenToolShiftKeyPressed = true;
        Dispatch(ScreenToolColorDisplayToggleRequested);
        return true;
    }

    private bool ReleaseConfiguredHotkeyStates(Key key)
    {
        var released = false;
        released |= ReleaseConfiguredHotkeyState(key, _hotkeySettings.Run, ref _isRunHotkeyPressed);
        released |= ReleaseConfiguredHotkeyState(key, _hotkeySettings.Stop, ref _isStopHotkeyPressed);
        released |= ReleaseConfiguredHotkeyState(key, _hotkeySettings.Record, ref _isRecordHotkeyPressed);
        released |= ReleaseConfiguredHotkeyState(key, _hotkeySettings.ScreenTool, ref _isScreenToolHotkeyPressed);
        return released;
    }

    private bool ReleaseConfiguredHotkeyStates(ShortcutMouseButton mouseButton)
    {
        var released = false;
        released |= ReleaseConfiguredHotkeyState(mouseButton, _hotkeySettings.Run, ref _isRunHotkeyPressed);
        released |= ReleaseConfiguredHotkeyState(mouseButton, _hotkeySettings.Stop, ref _isStopHotkeyPressed);
        released |= ReleaseConfiguredHotkeyState(mouseButton, _hotkeySettings.Record, ref _isRecordHotkeyPressed);
        released |= ReleaseConfiguredHotkeyState(mouseButton, _hotkeySettings.ScreenTool, ref _isScreenToolHotkeyPressed);
        return released;
    }

    private static bool ReleaseConfiguredHotkeyState(Key key, ShortcutGesture gesture, ref bool pressedState)
    {
        if (!pressedState || gesture.IsEmpty)
        {
            return false;
        }

        if (NormalizeKey(gesture.Key) == key || IsMatchingModifierKeyRelease(key, gesture.Modifiers))
        {
            pressedState = false;
            return true;
        }

        return false;
    }

    private static bool ReleaseConfiguredHotkeyState(ShortcutMouseButton mouseButton, ShortcutGesture gesture, ref bool pressedState)
    {
        if (!pressedState || !gesture.IsMouse)
        {
            return false;
        }

        if (gesture.MouseButton == mouseButton)
        {
            pressedState = false;
            return true;
        }

        return false;
    }

    private bool ReleaseScreenToolShortcutState(Key key)
    {
        switch (key)
        {
            case Key.R:
                _isScreenToolRecordKeyPressed = false;
                return true;
            case Key.LeftShift:
            case Key.RightShift:
                _isScreenToolShiftKeyPressed = false;
                return true;
            default:
                return false;
        }
    }

    private void ResetAllShortcutStates()
    {
        _isRunHotkeyPressed = false;
        _isStopHotkeyPressed = false;
        _isRecordHotkeyPressed = false;
        _isScreenToolHotkeyPressed = false;
        ResetScreenToolShortcutStates();
    }

    private void ResetScreenToolShortcutStates()
    {
        _isScreenToolRecordKeyPressed = false;
        _isScreenToolShiftKeyPressed = false;
    }

    private void Dispatch(Action? callback)
    {
        if (callback is null || _owner is null)
        {
            return;
        }

        _owner.Dispatcher.BeginInvoke(callback);
    }

    private static bool IsConfiguredHotkeyMatch(ShortcutGesture gesture, Key key, ModifierKeys modifiers)
    {
        if (!gesture.IsKeyboard)
        {
            return false;
        }

        return NormalizeKey(gesture.Key) == key
            && NormalizeModifiers(gesture.Modifiers) == NormalizeModifiers(modifiers);
    }

    private static bool IsConfiguredHotkeyMatch(ShortcutGesture gesture, ShortcutMouseButton mouseButton, ModifierKeys modifiers)
    {
        if (!gesture.IsMouse)
        {
            return false;
        }

        return gesture.MouseButton == mouseButton
            && NormalizeModifiers(gesture.Modifiers) == NormalizeModifiers(modifiers);
    }

    private static bool IsMatchingModifierKeyRelease(Key key, ModifierKeys modifiers)
    {
        return (key is Key.LeftCtrl or Key.RightCtrl && modifiers.HasFlag(ModifierKeys.Control))
            || (key is Key.LeftAlt or Key.RightAlt && modifiers.HasFlag(ModifierKeys.Alt))
            || (key is Key.LeftShift or Key.RightShift && modifiers.HasFlag(ModifierKeys.Shift))
            || (key is Key.LWin or Key.RWin && modifiers.HasFlag(ModifierKeys.Windows));
    }

    private static ModifierKeys GetCurrentModifiers()
    {
        var modifiers = ModifierKeys.None;

        if (IsKeyDown(VkLControl) || IsKeyDown(VkRControl))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (IsKeyDown(VkLMenu) || IsKeyDown(VkRMenu))
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (IsKeyDown((int)VkLShift) || IsKeyDown((int)VkRShift))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (IsKeyDown(VkLWin) || IsKeyDown(VkRWin))
        {
            modifiers |= ModifierKeys.Windows;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static Key NormalizeKey(Key key)
    {
        return key == Key.System ? Key.None : key;
    }

    private static ModifierKeys NormalizeModifiers(ModifierKeys modifiers)
    {
        return modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows);
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
