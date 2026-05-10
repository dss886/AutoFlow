using MoonSharp.Interpreter;

namespace AutoFlow.App.Services;

public sealed class LuaAutomationRuntime
{
    private readonly AutomationInputService _inputService;

    public LuaAutomationRuntime(AutomationInputService inputService)
    {
        _inputService = inputService;
    }

    public Task ExecuteAsync(string scriptPath, Action<string> log, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var script = new Script(CoreModules.Preset_Complete);
            RegisterHostApi(script, log, cancellationToken);
            script.DoFile(scriptPath);
        }, cancellationToken);
    }

    private void RegisterHostApi(Script script, Action<string> log, CancellationToken cancellationToken)
    {
        script.Globals["host"] = BuildHostTable(script, log, cancellationToken);
        script.Globals["mouse"] = BuildMouseTable(script, cancellationToken);
        script.Globals["keyboard"] = BuildKeyboardTable(script, cancellationToken);
    }

    private Table BuildHostTable(Script script, Action<string> log, CancellationToken cancellationToken)
    {
        var table = new Table(script);
        table["log"] = DynValue.NewCallback((_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = args.Count > 0 ? args[0].CastToString() ?? string.Empty : string.Empty;
            log(message);
            return DynValue.Nil;
        });

        table["sleep"] = DynValue.NewCallback((_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var milliseconds = RequireInt(args, 0, "host.sleep");

            var remaining = milliseconds;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = Math.Min(remaining, 100);
                Thread.Sleep(step);
                remaining -= step;
            }

            return DynValue.Nil;
        });

        table["stop_requested"] = DynValue.NewCallback((_, _) =>
        {
            return DynValue.NewBoolean(cancellationToken.IsCancellationRequested);
        });

        return table;
    }

    private Table BuildMouseTable(Script script, CancellationToken cancellationToken)
    {
        var table = new Table(script);
        table["move"] = DynValue.NewCallback((_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inputService.MoveMouse(RequireInt(args, 0, "mouse.move"), RequireInt(args, 1, "mouse.move"));
            return DynValue.Nil;
        });

        table["click"] = DynValue.NewCallback((_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var button = args.Count > 0 ? args[0].CastToString() ?? "left" : "left";
            _inputService.Click(button);
            return DynValue.Nil;
        });

        table["down"] = DynValue.NewCallback((_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inputService.MouseDown(RequireString(args, 0, "mouse.down"));
            return DynValue.Nil;
        });

        table["up"] = DynValue.NewCallback((_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inputService.MouseUp(RequireString(args, 0, "mouse.up"));
            return DynValue.Nil;
        });

        return table;
    }

    private Table BuildKeyboardTable(Script script, CancellationToken cancellationToken)
    {
        var table = new Table(script);
        table["press"] = DynValue.NewCallback((_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inputService.PressKey(RequireString(args, 0, "keyboard.press"));
            return DynValue.Nil;
        });

        table["down"] = DynValue.NewCallback((_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inputService.KeyDown(RequireString(args, 0, "keyboard.down"));
            return DynValue.Nil;
        });

        table["up"] = DynValue.NewCallback((_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inputService.KeyUp(RequireString(args, 0, "keyboard.up"));
            return DynValue.Nil;
        });

        return table;
    }

    private static int RequireInt(CallbackArguments args, int index, string functionName)
    {
        if (args.Count <= index || args[index].Type != DataType.Number)
        {
            throw new ScriptRuntimeException($"{functionName} 需要数字参数。");
        }

        return (int)args[index].Number;
    }

    private static string RequireString(CallbackArguments args, int index, string functionName)
    {
        if (args.Count <= index || args[index].Type != DataType.String)
        {
            throw new ScriptRuntimeException($"{functionName} 需要字符串参数。");
        }

        return args[index].String;
    }
}
