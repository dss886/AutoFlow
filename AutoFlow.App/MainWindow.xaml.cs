using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AutoFlow.App.Models;
using AutoFlow.App.Services;

namespace AutoFlow.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int WhMouseLl = 14;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const ushort XButton1 = 0x0001;

    private readonly ScriptCatalogService _catalogService;
    private readonly ScriptRunnerService _runnerService;
    private readonly FileSystemWatcher _fileSystemWatcher;
    private readonly DispatcherTimer _mousePositionTimer;
    private readonly LowLevelMouseProc _mouseHookProc;
    private ScriptDefinition? _selectedScript;
    private string _logOutput = string.Empty;
    private string _runStatusText = "空闲";
    private string _statusMessage = "就绪";
    private string _mousePositionText = "鼠标位置: X 0, Y 0";
    private bool _allowExit;
    private bool _isMousePositionVisible;
    private bool _isToggleMouseButtonPressed;
    private IntPtr _mouseHookHandle;

    public MainWindow()
    {
        InitializeComponent();

        Style = (Style)FindResource(typeof(Window));
        SourceInitialized += MainWindow_OnSourceInitialized;

        DataContext = this;
        ScriptsDirectory = PathService.ResolveScriptsDirectory();
        PathService.EnsureDirectory(ScriptsDirectory);

        _catalogService = new ScriptCatalogService();
        _runnerService = new ScriptRunnerService(new LuaAutomationRuntime(new AutomationInputService()));
        _runnerService.LogGenerated += RunnerService_OnLogGenerated;
        _runnerService.ScriptStateChanged += RunnerService_OnScriptStateChanged;
        _mouseHookProc = MouseHookCallback;
        _mousePositionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _mousePositionTimer.Tick += MousePositionTimer_OnTick;

        _fileSystemWatcher = new FileSystemWatcher(ScriptsDirectory, "*.lua")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };

        _fileSystemWatcher.Created += FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Changed += FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Deleted += FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Renamed += FileSystemWatcher_OnChanged;

        LoadScripts();
        AppendLog("应用已启动。");
        AppendLog($"脚本目录: {ScriptsDirectory}");
    }

    public ObservableCollection<ScriptDefinition> Scripts { get; } = new();

    public string ScriptsDirectory { get; }

    public ScriptDefinition? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (_selectedScript == value)
            {
                return;
            }

            _selectedScript = value;
            OnPropertyChanged();
        }
    }

    public string RunStatusText
    {
        get => _runStatusText;
        set
        {
            if (_runStatusText == value)
            {
                return;
            }

            _runStatusText = value;
            OnPropertyChanged();
        }
    }

    public string LogOutput
    {
        get => _logOutput;
        set
        {
            if (_logOutput == value)
            {
                return;
            }

            _logOutput = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsScriptRunning => _runnerService.IsRunning;

    public string RunControlButtonText => IsScriptRunning ? "停止脚本（鼠标后退键）" : "运行脚本（鼠标后退键）";

    public bool IsMousePositionVisible
    {
        get => _isMousePositionVisible;
        private set
        {
            if (_isMousePositionVisible == value)
            {
                return;
            }

            _isMousePositionVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MousePositionToggleButtonText));
        }
    }

    public string MousePositionText
    {
        get => _mousePositionText;
        private set
        {
            if (_mousePositionText == value)
            {
                return;
            }

            _mousePositionText = value;
            OnPropertyChanged();
        }
    }

    public string MousePositionToggleButtonText => IsMousePositionVisible ? "隐藏鼠标位置" : "显示鼠标位置";

    public event PropertyChangedEventHandler? PropertyChanged;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

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

    private async void RunButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ToggleRunStateAsync();
    }

    private async Task RunSelectedScriptAsync()
    {
        if (SelectedScript is null)
        {
            System.Windows.MessageBox.Show(this, "请先选择一个脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            await _runnerService.StartAsync(SelectedScript);
        }
        catch (Exception ex)
        {
            AppendLog($"启动脚本失败: {ex.Message}");
            RunStatusText = "启动失败";
            StatusMessage = "启动失败";
        }
    }

    private async Task ToggleRunStateAsync()
    {
        if (IsScriptRunning)
        {
            StopRunningScript();
            return;
        }

        await RunSelectedScriptAsync();
    }

    private void StopRunningScript()
    {
        _runnerService.Stop();
    }

    private void OpenScriptsFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = ScriptsDirectory,
            UseShellExecute = true,
        });
    }

    private void OpenInEditorButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedScript is null)
        {
            System.Windows.MessageBox.Show(this, "请先选择一个脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedScript.FilePath,
            UseShellExecute = true,
        });
    }

    private void ToggleMousePositionButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetMousePositionTracking(!IsMousePositionVisible);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowPlacementService.Apply(this);
        InstallMouseHook();
    }

    protected override void OnClosed(EventArgs e)
    {
        _mousePositionTimer.Stop();
        RemoveMouseHook();
        _runnerService.Stop();
        _fileSystemWatcher.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        WindowPlacementService.Save(this);
        base.OnClosing(e);
    }

    private void InstallMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
        var moduleHandle = GetModuleHandle(moduleName);
        _mouseHookHandle = SetWindowsHookEx(WhMouseLl, _mouseHookProc, moduleHandle, 0);
        if (_mouseHookHandle != IntPtr.Zero)
        {
            AppendLog("已启用鼠标后退键监听。");
            return;
        }

        var errorCode = Marshal.GetLastWin32Error();
        AppendLog($"鼠标后退键监听启用失败，错误代码: {errorCode}");
    }

    private void SetMousePositionTracking(bool isEnabled)
    {
        IsMousePositionVisible = isEnabled;

        if (isEnabled)
        {
            UpdateMousePosition();
            _mousePositionTimer.Start();
            return;
        }

        _mousePositionTimer.Stop();
    }

    private void MousePositionTimer_OnTick(object? sender, EventArgs e)
    {
        UpdateMousePosition();
    }

    private void UpdateMousePosition()
    {
        if (!GetCursorPos(out var point))
        {
            return;
        }

        MousePositionText = $"鼠标位置: X {point.X}, Y {point.Y}";
    }

    private void RemoveMouseHook()
    {
        if (_mouseHookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var mouseData = Marshal.PtrToStructure<MouseHookData>(lParam);
        var sideButton = (ushort)(mouseData.MouseData >> 16);

        if (sideButton == XButton1)
        {
            if (message == WmXButtonDown && !_isToggleMouseButtonPressed)
            {
                _isToggleMouseButtonPressed = true;
                Dispatcher.BeginInvoke(new Action(() => _ = ToggleRunStateAsync()));
            }
            else if (message == WmXButtonUp)
            {
                _isToggleMouseButtonPressed = false;
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void LoadScripts()
    {
        var selectedPath = SelectedScript?.FilePath;
        var scripts = _catalogService.LoadScripts(ScriptsDirectory);

        Scripts.Clear();
        foreach (var script in scripts)
        {
            script.IsRunning = string.Equals(script.FilePath, _runnerService.RunningScript?.FilePath, StringComparison.OrdinalIgnoreCase);
            Scripts.Add(script);
        }

        SelectedScript = Scripts.FirstOrDefault(item =>
            string.Equals(item.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? Scripts.FirstOrDefault();

        StatusMessage = Scripts.Count == 0 ? "脚本目录为空" : $"已加载 {Scripts.Count} 个脚本";
    }

    private void AppendLog(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogOutput = string.IsNullOrWhiteSpace(LogOutput)
            ? timestamped
            : $"{LogOutput}{Environment.NewLine}{timestamped}";
    }

    private void RunnerService_OnLogGenerated(string message)
    {
        Dispatcher.Invoke(() => AppendLog(message));
    }

    private void RunnerService_OnScriptStateChanged(ScriptDefinition? script, bool isRunning)
    {
        Dispatcher.Invoke(() =>
        {
            if (script is not null)
            {
                var item = Scripts.FirstOrDefault(candidate =>
                    string.Equals(candidate.FilePath, script.FilePath, StringComparison.OrdinalIgnoreCase));

                if (item is not null)
                {
                    item.IsRunning = isRunning;
                }
            }

            RunStatusText = isRunning ? "运行中" : "空闲";
            StatusMessage = isRunning ? "脚本运行中" : "就绪";
            OnPropertyChanged(nameof(IsScriptRunning));
            OnPropertyChanged(nameof(RunControlButtonText));
        });
    }

    private void FileSystemWatcher_OnChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            LoadScripts();
            AppendLog("检测到脚本目录变化，已自动刷新。");
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void PrepareForExit()
    {
        _allowExit = true;
    }
}
