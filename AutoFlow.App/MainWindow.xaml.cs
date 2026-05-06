using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using AutomationHost.App.Models;
using AutomationHost.App.Services;

namespace AutomationHost.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ScriptCatalogService _catalogService;
    private readonly ScriptRunnerService _runnerService;
    private readonly FileSystemWatcher _fileSystemWatcher;
    private ScriptDefinition? _selectedScript;
    private string _logOutput = string.Empty;
    private string _runStatusText = "空闲";
    private string _statusMessage = "就绪";

    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;
        ScriptsDirectory = PathService.ResolveScriptsDirectory();
        PathService.EnsureDirectory(ScriptsDirectory);

        _catalogService = new ScriptCatalogService();
        _runnerService = new ScriptRunnerService(new LuaAutomationRuntime(new AutomationInputService()));
        _runnerService.LogGenerated += RunnerService_OnLogGenerated;
        _runnerService.ScriptStateChanged += RunnerService_OnScriptStateChanged;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void RunButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedScript is null)
        {
            MessageBox.Show(this, "请先选择一个脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void StopButton_OnClick(object sender, RoutedEventArgs e)
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
            MessageBox.Show(this, "请先选择一个脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedScript.FilePath,
            UseShellExecute = true,
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _runnerService.Stop();
        _fileSystemWatcher.Dispose();
        base.OnClosed(e);
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
}
