using System.Diagnostics;
using System.Runtime.InteropServices;
using AutoFlow.App.Infrastructure;
using AutoFlow.App.Models;

namespace AutoFlow.App.Services;

public sealed class GlobalMouseHookService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseMove = 0x0200;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const ushort XButton1 = 0x0001;
    private const ushort XButton2 = 0x0002;

    private readonly AppLoggerService _logger;
    private readonly IEventBus _eventBus;
    private readonly HookProc _mouseHookProc;
    private bool _isDisposed;
    private IntPtr _mouseHookHandle;

    public GlobalMouseHookService(IEventBus eventBus, AppLoggerService logger)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mouseHookProc = MouseHookCallback;
    }

    public event Func<ShortcutMouseButton, bool>? ShortcutMouseButtonDown;

    public event Func<ShortcutMouseButton, bool>? ShortcutMouseButtonUp;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
        var moduleHandle = GetModuleHandle(moduleName);
        _mouseHookHandle = SetWindowsHookEx(WhMouseLl, _mouseHookProc, moduleHandle, 0);
        if (_mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        var errorCode = Marshal.GetLastWin32Error();
        _logger.E($"全局鼠标位置监听注册失败，请检查当前环境。错误代码: {errorCode}");
    }

    public void Stop()
    {
        if (_mouseHookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var mouseData = Marshal.PtrToStructure<MouseHookData>(lParam);
        if (message == WmMouseMove)
        {
            _eventBus.Publish(new MouseMovedMessage(mouseData.X, mouseData.Y));
        }

        var shortcutMouseButton = ResolveShortcutMouseButton(message, mouseData.MouseData);
        if (message is WmMButtonDown or WmXButtonDown
            && shortcutMouseButton != ShortcutMouseButton.None
            && DispatchConsumable(ShortcutMouseButtonDown, shortcutMouseButton))
        {
            return new IntPtr(1);
        }

        if (message is WmMButtonUp or WmXButtonUp
            && shortcutMouseButton != ShortcutMouseButton.None
            && DispatchConsumable(ShortcutMouseButtonUp, shortcutMouseButton))
        {
            return new IntPtr(1);
        }

        var observedButton = ResolveObservedMouseButton(message);
        if (observedButton is not null)
        {
            _eventBus.Publish(new MouseButtonObservedMessage(
                observedButton.Button,
                observedButton.IsButtonDown,
                mouseData.X,
                mouseData.Y));
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private static bool DispatchConsumable(Func<ShortcutMouseButton, bool>? callback, ShortcutMouseButton mouseButton)
    {
        if (callback is null)
        {
            return false;
        }

        foreach (var handler in callback.GetInvocationList().Cast<Func<ShortcutMouseButton, bool>>())
        {
            if (handler(mouseButton))
            {
                return true;
            }
        }

        return false;
    }

    private static ObservedMouseButton? ResolveObservedMouseButton(int message)
    {
        return message switch
        {
            WmLButtonDown => new ObservedMouseButton("left", IsButtonDown: true),
            WmLButtonUp => new ObservedMouseButton("left", IsButtonDown: false),
            WmRButtonDown => new ObservedMouseButton("right", IsButtonDown: true),
            WmRButtonUp => new ObservedMouseButton("right", IsButtonDown: false),
            WmMButtonDown => new ObservedMouseButton("middle", IsButtonDown: true),
            WmMButtonUp => new ObservedMouseButton("middle", IsButtonDown: false),
            _ => null,
        };
    }

    private static ShortcutMouseButton ResolveShortcutMouseButton(int message, uint mouseData)
    {
        return message switch
        {
            WmMButtonDown or WmMButtonUp => ShortcutMouseButton.Middle,
            WmXButtonDown or WmXButtonUp => (ushort)(mouseData >> 16) switch
            {
                XButton1 => ShortcutMouseButton.XButton1,
                XButton2 => ShortcutMouseButton.XButton2,
                _ => ShortcutMouseButton.None,
            },
            _ => ShortcutMouseButton.None,
        };
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private sealed record ObservedMouseButton(string Button, bool IsButtonDown);
}
