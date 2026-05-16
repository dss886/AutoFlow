using AutoFlow.App.Models;

namespace AutoFlow.App.Services;

public sealed class ScriptRunnerService
{
    private readonly AppLoggerService _logger;
    private readonly LuaRuntimeService _runtime;
    private CancellationTokenSource? _currentRunCts;

    public ScriptRunnerService(LuaRuntimeService runtime, AppLoggerService logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsRunning => RunningScript is not null;

    public ScriptDefinition? RunningScript { get; private set; }

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
        _logger.V($"开始执行脚本: 「{script.Name}」");

        try
        {
            await _runtime.ExecuteAsync(script.FilePath, _currentRunCts.Token);

            if (_currentRunCts.Token.IsCancellationRequested)
            {
                _logger.V($"脚本已停止: 「{script.Name}」");
            }
            else
            {
                _logger.V($"脚本执行完成: 「{script.Name}」");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.V($"脚本已停止: 「{script.Name}」");
        }
        catch (Exception ex)
        {
            _logger.E($"脚本执行失败: 「{script.Name}」，错误信息: {ex.Message}");
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
}
