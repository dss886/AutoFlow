using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AutoFlow.App.Infrastructure;
using AutoFlow.App.Models;
using AutoFlow.App.Services;

namespace AutoFlow.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaxLogLineCount = 3000;

    private readonly Action _closeWindow;
    private readonly Action _openSettings;
    private readonly ScriptCatalogService _catalogService;
    private readonly ScriptRunnerService _runnerService;
    private readonly InputRecordingSession _recordingSession = new();
    private readonly FileSystemWatcher _fileSystemWatcher;
    private readonly DispatcherTimer _scriptRefreshTimer;
    private readonly Queue<string> _logEntries = new();
    private ScriptDefinition? _selectedScript;
    private string _logOutput = string.Empty;
    private bool _isScreenToolVisible;
    private bool _suppressNextScriptDirectoryRefreshLog;
    private CancellationTokenSource? _recordCountdownCancellation;
    private bool _isRecording;
    private bool _isDisposed;
    private int _recordCountdownSeconds;

    public MainWindowViewModel(Action closeWindow, Action openSettings)
    {
        _closeWindow = closeWindow ?? throw new ArgumentNullException(nameof(closeWindow));
        _openSettings = openSettings ?? throw new ArgumentNullException(nameof(openSettings));

        ScriptsDirectory = PathService.ResolveScriptsDirectory();
        PathService.EnsureDirectory(ScriptsDirectory);

        _catalogService = new ScriptCatalogService();
        _runnerService = new ScriptRunnerService(new LuaAutomationRuntime(new AutomationInputService()));
        _runnerService.LogGenerated += RunnerService_OnLogGenerated;
        _runnerService.ScriptStateChanged += RunnerService_OnScriptStateChanged;

        _scriptRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _scriptRefreshTimer.Tick += ScriptRefreshTimer_OnTick;

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

        ToggleRunStateCommand = new RelayCommand(ToggleRunState);
        OpenScriptsFolderCommand = new RelayCommand(OpenScriptsFolder);
        OpenScriptCommand = new RelayCommand<ScriptDefinition>(OpenScript);
        DeleteScriptCommand = new RelayCommand<ScriptDefinition>(DeleteScript);
        RecordCommand = new RelayCommand(Record);
        ToggleScreenToolCommand = new RelayCommand(ToggleScreenTool);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        CloseWindowCommand = new RelayCommand(_closeWindow);

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

    public string LogOutput
    {
        get => _logOutput;
        private set
        {
            if (_logOutput == value)
            {
                return;
            }

            _logOutput = value;
            OnPropertyChanged();
        }
    }

    public bool IsScriptRunning => _runnerService.IsRunning;

    public string RunControlButtonText => IsScriptRunning ? "停止" : "运行";

    public string RecordButtonText => _isRecording
        ? "结束"
        : _recordCountdownSeconds > 0
            ? $"{_recordCountdownSeconds} 秒"
            : "录制";

    public bool IsScreenToolVisible
    {
        get => _isScreenToolVisible;
        private set
        {
            if (_isScreenToolVisible == value)
            {
                return;
            }

            _isScreenToolVisible = value;
            OnPropertyChanged();
        }
    }

    public bool IsRecording => _isRecording;

    public ICommand ToggleRunStateCommand { get; }

    public ICommand OpenScriptsFolderCommand { get; }

    public ICommand OpenScriptCommand { get; }

    public ICommand DeleteScriptCommand { get; }

    public ICommand RecordCommand { get; }

    public ICommand ToggleScreenToolCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand CloseWindowCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ExecuteRunCommand()
    {
        if (!IsScriptRunning)
        {
            if (IsRecordingOrCountdownActive())
            {
                AppendLog("录制进行中，无法启动脚本。");
                return;
            }

            _ = RunSelectedScriptAsync();
        }
    }

    public void ExecuteStopCommand()
    {
        if (IsScriptRunning)
        {
            _runnerService.Stop();
        }
    }

    public void ExecuteRecordCommand()
    {
        if (RecordCommand.CanExecute(null))
        {
            RecordCommand.Execute(null);
        }
    }

    public void ExecuteToggleScreenToolCommand()
    {
        if (ToggleScreenToolCommand.CanExecute(null))
        {
            ToggleScreenToolCommand.Execute(null);
        }
    }

    public void AppendLogMessage(string message)
    {
        AppendLog(message);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _scriptRefreshTimer.Stop();
        _scriptRefreshTimer.Tick -= ScriptRefreshTimer_OnTick;

        _fileSystemWatcher.Created -= FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Changed -= FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Deleted -= FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Renamed -= FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Dispose();

        _runnerService.LogGenerated -= RunnerService_OnLogGenerated;
        _runnerService.ScriptStateChanged -= RunnerService_OnScriptStateChanged;
        _runnerService.Stop();
        CancelRecordCountdownCore();
        _recordCountdownCancellation?.Dispose();
        _recordCountdownCancellation = null;
        _isRecording = false;
    }

    private void ToggleRunState()
    {
        if (IsScriptRunning)
        {
            _runnerService.Stop();
            return;
        }

        if (IsRecordingOrCountdownActive())
        {
            AppendLog("录制进行中，无法启动脚本。");
            return;
        }

        _ = RunSelectedScriptAsync();
    }

    private void OpenSettings()
    {
        _openSettings();
    }

    private async Task RunSelectedScriptAsync()
    {
        if (SelectedScript is null)
        {
            System.Windows.MessageBox.Show("请先选择一个脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!EnsureSelectedScriptExists())
        {
            return;
        }

        try
        {
            await _runnerService.StartAsync(SelectedScript);
        }
        catch (Exception ex)
        {
            AppendLog($"启动脚本失败: {ex.Message}");
        }
    }

    private void OpenScriptsFolder()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = ScriptsDirectory,
            UseShellExecute = true,
        });
    }

    private void OpenScript(ScriptDefinition? script)
    {
        var targetScript = ResolveScriptForAction(script);
        if (targetScript is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = targetScript.FilePath,
            UseShellExecute = true,
        });
    }

    private void DeleteScript(ScriptDefinition? script)
    {
        var targetScript = ResolveScriptForAction(script);
        if (targetScript is null)
        {
            return;
        }

        if (string.Equals(_runnerService.RunningScript?.FilePath, targetScript.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show("脚本正在运行，请先停止后再删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"确定要删除脚本“{targetScript.Name}”吗？\n删除后不可恢复。",
            "删除脚本",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _suppressNextScriptDirectoryRefreshLog = true;
            File.Delete(targetScript.FilePath);
            AppendLog($"已删除脚本: {targetScript.FileName}");
            LoadScripts();
        }
        catch (Exception ex)
        {
            AppendLog($"删除脚本失败: {targetScript.FileName}，{ex.Message}");
            System.Windows.MessageBox.Show($"删除脚本失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleScreenTool()
    {
        IsScreenToolVisible = !IsScreenToolVisible;
        AppendLog(IsScreenToolVisible ? "屏幕工具已启动。" : "屏幕工具已关闭。");
    }

    private void Record()
    {
        if (_isRecording)
        {
            StopRecording();
            return;
        }

        if (_recordCountdownCancellation is not null)
        {
            CancelRecordCountdown();
            return;
        }

        if (IsScriptRunning)
        {
            System.Windows.MessageBox.Show("脚本运行中无法开始录制，请先停止脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _ = StartRecordCountdownAsync();
    }

    public void HandleObservedKeyboardInput(Key key, bool isKeyDown)
    {
        if (!_isRecording || !_recordingSession.RecordKeyboardEvent(key, isKeyDown))
        {
            return;
        }

        var reading = InputRecordingSession.CaptureCursorReading();
        AppendLog($"{FormatKeyboardLogPrefix(key, isKeyDown)}，{reading.ToLogMessage()}");
    }

    public void HandleObservedMouseInput(string button, bool isButtonDown, int x, int y)
    {
        if (!_isRecording || !_recordingSession.RecordMouseButtonEvent(button, isButtonDown, x, y))
        {
            return;
        }

        AppendLog($"{FormatMouseLogPrefix(button, isButtonDown)}，{InputRecordingSession.CreateReadingLogMessage(x, y)}");
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
    }

    private bool EnsureSelectedScriptExists()
    {
        return EnsureScriptExists(SelectedScript);
    }

    private ScriptDefinition? ResolveScriptForAction(ScriptDefinition? script)
    {
        var targetScript = script ?? SelectedScript;
        if (targetScript is null)
        {
            System.Windows.MessageBox.Show("请先选择一个脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        SelectedScript = Scripts.FirstOrDefault(item =>
            string.Equals(item.FilePath, targetScript.FilePath, StringComparison.OrdinalIgnoreCase))
            ?? targetScript;

        return EnsureScriptExists(targetScript) ? targetScript : null;
    }

    private bool EnsureScriptExists(ScriptDefinition? selectedScript)
    {
        if (selectedScript is null)
        {
            return false;
        }

        if (File.Exists(selectedScript.FilePath))
        {
            return true;
        }

        LoadScripts();
        AppendLog($"脚本文件不存在，已跳过操作: {selectedScript.FileName}");
        System.Windows.MessageBox.Show("所选脚本文件已不存在，列表已自动刷新。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void AppendLog(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logEntries.Enqueue(timestamped);

        while (_logEntries.Count > MaxLogLineCount)
        {
            _logEntries.Dequeue();
        }

        LogOutput = string.Join(Environment.NewLine, _logEntries);
    }

    private void RunnerService_OnLogGenerated(string message)
    {
        GetUiDispatcher().Invoke(() => AppendLog(message));
    }

    private void RunnerService_OnScriptStateChanged(ScriptDefinition? script, bool isRunning)
    {
        GetUiDispatcher().Invoke(() =>
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

            OnPropertyChanged(nameof(IsScriptRunning));
            OnPropertyChanged(nameof(RunControlButtonText));
        });
    }

    private void ScriptRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        _scriptRefreshTimer.Stop();
        LoadScripts();

        if (_suppressNextScriptDirectoryRefreshLog)
        {
            _suppressNextScriptDirectoryRefreshLog = false;
            return;
        }

        AppendLog("检测到脚本目录变化，已自动刷新。");
    }

    private void FileSystemWatcher_OnChanged(object sender, FileSystemEventArgs e)
    {
        GetUiDispatcher().InvokeAsync(() =>
        {
            _scriptRefreshTimer.Stop();
            _scriptRefreshTimer.Start();
        });
    }

    private static Dispatcher GetUiDispatcher()
    {
        return System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    private bool IsRecordingOrCountdownActive()
    {
        return _isRecording || _recordCountdownCancellation is not null;
    }

    private async Task StartRecordCountdownAsync()
    {
        var cancellation = new CancellationTokenSource();
        _recordCountdownCancellation = cancellation;
        AppendLog("录制将在 3 秒后开始，再次点击录制可取消。");

        try
        {
            for (var seconds = 3; seconds >= 1; seconds--)
            {
                _recordCountdownSeconds = seconds;
                OnPropertyChanged(nameof(RecordButtonText));
                await Task.Delay(1000, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_recordCountdownCancellation, cancellation))
            {
                _recordCountdownCancellation = null;
            }

            _recordCountdownSeconds = 0;
            OnPropertyChanged(nameof(RecordButtonText));
            cancellation.Dispose();
        }

        StartRecording();
    }

    private void CancelRecordCountdown()
    {
        if (_recordCountdownCancellation is null)
        {
            return;
        }

        CancelRecordCountdownCore();
        AppendLog("已取消录制倒计时。");
    }

    private void CancelRecordCountdownCore()
    {
        _recordCountdownCancellation?.Cancel();
        _recordCountdownSeconds = 0;
        OnPropertyChanged(nameof(RecordButtonText));
    }

    private void StartRecording()
    {
        _recordingSession.Start();
        _isRecording = true;
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(RecordButtonText));
        AppendLog("录制已开始。");
    }

    private void StopRecording()
    {
        var now = DateTime.Now;
        var scriptName = $"录制脚本 {now:yyyy-MM-dd HH:mm:ss}";
        var description = $"由 AutoFlow 于 {now:yyyy-MM-dd HH:mm:ss} 录制生成";
        var fileName = $"record_{now:yyyyMMdd_HHmmss}.lua";
        var filePath = Path.Combine(ScriptsDirectory, fileName);

        try
        {
            var scriptContent = _recordingSession.StopAndBuildScript(scriptName, description);
            _suppressNextScriptDirectoryRefreshLog = true;
            File.WriteAllText(filePath, scriptContent);

            LoadScripts();
            SelectedScript = Scripts.FirstOrDefault(item =>
                string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            AppendLog($"录制已完成，脚本已保存: {fileName}");
            TrayIconService.Current?.ShowInfo("AutoFlow 录制完成", $"脚本已保存到 Scripts：{fileName}");
        }
        catch (Exception ex)
        {
            AppendLog($"保存录制脚本失败: {ex.Message}");
            System.Windows.MessageBox.Show($"保存录制脚本失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRecording = false;
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(RecordButtonText));
        }
    }

    private static string FormatMouseLogPrefix(string button, bool isButtonDown)
    {
        var buttonText = button switch
        {
            "left" => "左键",
            "right" => "右键",
            "middle" => "中键",
            _ => button,
        };

        return isButtonDown ? $"录制鼠标{buttonText}按下" : $"录制鼠标{buttonText}抬起";
    }

    private static string FormatKeyboardLogPrefix(Key key, bool isKeyDown)
    {
        var keyName = key switch
        {
            Key.LeftCtrl or Key.RightCtrl => "Ctrl",
            Key.LeftAlt or Key.RightAlt => "Alt",
            Key.LeftShift or Key.RightShift => "Shift",
            Key.LWin or Key.RWin => "Win",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Delete => "Del",
            Key.Insert => "Ins",
            Key.PageUp => "PgUp",
            Key.PageDown => "PgDn",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            _ => key.ToString(),
        };

        return isKeyDown ? $"录制键盘按下 {keyName}" : $"录制键盘抬起 {keyName}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
