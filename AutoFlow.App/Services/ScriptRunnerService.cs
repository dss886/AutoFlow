using AutoFlow.App.Models;

namespace AutoFlow.App.Services;

public sealed class ScriptRunnerService
{
    private readonly LuaAutomationRuntime _runtime;
    private CancellationTokenSource? _currentRunCts;

    public ScriptRunnerService(LuaAutomationRuntime runtime)
    {
        _runtime = runtime;
    }

    public bool IsRunning => RunningScript is not null;

    public ScriptDefinition? RunningScript { get; private set; }

    public event Action<string>? LogGenerated;

    public event Action<ScriptDefinition?, bool>? ScriptStateChanged;

    public async Task StartAsync(ScriptDefinition script)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("当前已有脚本正在运行，请先停止它。");
        }

        _currentRunCts = new CancellationTokenSource();
        RunningScript = script;
        ScriptStateChanged?.Invoke(script, true);
        LogGenerated?.Invoke($"开始执行脚本: {script.Name}");

        try
        {
            await _runtime.ExecuteAsync(script.FilePath, LogGeneratedMessage, _currentRunCts.Token);
            LogGenerated?.Invoke($"脚本执行完成: {script.Name}");
        }
        catch (ScriptExecutionCanceledException)
        {
            LogGenerated?.Invoke($"脚本已停止: {script.Name}");
        }
        catch (OperationCanceledException)
        {
            LogGenerated?.Invoke($"脚本已停止: {script.Name}");
        }
        catch (Exception ex)
        {
            LogGenerated?.Invoke($"脚本执行失败: {ex.Message}");
        }
        finally
        {
            var completedScript = RunningScript;
            RunningScript = null;
            _runtime.ReleasePressedInputs();
            _currentRunCts.Dispose();
            _currentRunCts = null;
            ScriptStateChanged?.Invoke(completedScript, false);
        }
    }

    public void Stop()
    {
        _currentRunCts?.Cancel();
    }

    private void LogGeneratedMessage(string message)
    {
        LogGenerated?.Invoke(message);
    }
}
