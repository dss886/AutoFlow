using MoonSharp.Interpreter;
using AutoFlow.App.Models;

namespace AutoFlow.App.Services;

public sealed class LuaRuntimeService
{
    private readonly AppLoggerService _logger;
    private readonly AutomationInputService _inputService;
    private readonly ScreenNumberRecognitionService _screenNumberRecognitionService;

    public LuaRuntimeService(
        AutomationInputService inputService,
        ScreenNumberRecognitionService screenNumberRecognitionService,
        AppLoggerService logger)
    {
        _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
        _screenNumberRecognitionService = screenNumberRecognitionService ?? throw new ArgumentNullException(nameof(screenNumberRecognitionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ExecuteAsync(string scriptPath, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var script = new Script(CoreModules.Preset_Complete);
            RegisterHostApi(script, cancellationToken);
            try
            {
                script.DoFile(scriptPath);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 停止脚本属于预期控制流，这里就地结束，避免调试时冒泡为“用户未处理异常”。
            }
        }, cancellationToken);
    }

    public void ReleasePressedInputs()
    {
        _inputService.ReleasePressedInputs();
    }

    private void RegisterHostApi(Script script, CancellationToken cancellationToken)
    {
        script.Globals["host"] = BuildHostTable(script, cancellationToken);
        script.Globals["mouse"] = BuildMouseTable(script, cancellationToken);
        script.Globals["keyboard"] = BuildKeyboardTable(script, cancellationToken);
        script.Globals["screen"] = BuildScreenTable(script, cancellationToken);
    }

    private Table BuildHostTable(Script script, CancellationToken cancellationToken)
    {
        var table = new Table(script);
        table["log"] = DynValue.NewCallback((_, args) =>
        {
            ThrowIfCancellationRequested(cancellationToken);
            var message = args.Count > 0 ? args[0].CastToString() ?? string.Empty : string.Empty;
            _logger.I(message, LogSource.Script);
            return DynValue.Nil;
        });

        table["sleep"] = DynValue.NewCallback((_, args) =>
        {
            ThrowIfCancellationRequested(cancellationToken);
            var milliseconds = RequireInt(args, 0, "host.sleep");

            var remaining = milliseconds;
            while (remaining > 0)
            {
                ThrowIfCancellationRequested(cancellationToken);
                var step = Math.Min(remaining, 100);
                Thread.Sleep(step);
                remaining -= step;
            }

            return DynValue.Nil;
        });

        return table;
    }

    private Table BuildMouseTable(Script script, CancellationToken cancellationToken)
    {
        var table = new Table(script);
        table["move"] = DynValue.NewCallback((_, args) =>
        {
            ThrowIfCancellationRequested(cancellationToken);
            _inputService.MoveMouse(RequireInt(args, 0, "mouse.move"), RequireInt(args, 1, "mouse.move"));
            return DynValue.Nil;
        });

        table["click"] = DynValue.NewCallback((_, args) =>
        {
            ThrowIfCancellationRequested(cancellationToken);
            var button = args.Count > 0 ? args[0].CastToString() ?? "left" : "left";
            _inputService.Click(button);
            return DynValue.Nil;
        });

        table["down"] = DynValue.NewCallback((_, args) =>
        {
            ThrowIfCancellationRequested(cancellationToken);
            _inputService.MouseDown(RequireString(args, 0, "mouse.down"));
            return DynValue.Nil;
        });

        table["up"] = DynValue.NewCallback((_, args) =>
        {
            ThrowIfCancellationRequested(cancellationToken);
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
            ThrowIfCancellationRequested(cancellationToken);
            _inputService.PressKey(RequireString(args, 0, "keyboard.press"));
            return DynValue.Nil;
        });

        table["down"] = DynValue.NewCallback((_, args) =>
        {
            ThrowIfCancellationRequested(cancellationToken);
            _inputService.KeyDown(RequireString(args, 0, "keyboard.down"));
            return DynValue.Nil;
        });

        table["up"] = DynValue.NewCallback((_, args) =>
        {
            ThrowIfCancellationRequested(cancellationToken);
            _inputService.KeyUp(RequireString(args, 0, "keyboard.up"));
            return DynValue.Nil;
        });

        return table;
    }

    private Table BuildScreenTable(Script script, CancellationToken cancellationToken)
    {
        var table = new Table(script);
        table["get_color"] = DynValue.NewCallback((_, args) =>
        {
            ThrowIfCancellationRequested(cancellationToken);
            var x = RequireInt(args, 0, "screen.get_color");
            var y = RequireInt(args, 1, "screen.get_color");
            return DynValue.NewString(_inputService.GetScreenColorHex(x, y));
        });

        table["read_number"] = DynValue.NewCallback((_, args) =>
        {
            ThrowIfCancellationRequested(cancellationToken);
            var x1 = RequireInt(args, 0, "screen.read_number");
            var y1 = RequireInt(args, 1, "screen.read_number");
            var x2 = RequireInt(args, 2, "screen.read_number");
            var y2 = RequireInt(args, 3, "screen.read_number");
            var options = ReadScreenNumberOptions(args, 4, "screen.read_number");
            var region = RequireScreenRegion(x1, y1, x2, y2, "screen.read_number");

            var success = _screenNumberRecognitionService.TryReadNumber(
                region.X,
                region.Y,
                region.Width,
                region.Height,
                options,
                out var value);

            return success ? DynValue.NewNumber(value) : DynValue.Nil;
        });

        return table;
    }

    private static void ThrowIfCancellationRequested(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

    private static ScreenNumberReadOptions ReadScreenNumberOptions(
        CallbackArguments args,
        int index,
        string functionName)
    {
        if (args.Count <= index || args[index].IsNil())
        {
            return new ScreenNumberReadOptions();
        }

        if (args[index].Type != DataType.Table)
        {
            throw new ScriptRuntimeException($"{functionName} 的 options 参数必须是 table。");
        }

        var table = args[index].Table;
        var mode = ReadMode(table, functionName);
        return new ScreenNumberReadOptions
        {
            Language = ReadOptionalString(table, "lang") ?? "eng",
            CharacterWhitelist = ReadOptionalString(table, "allow") ?? BuildDefaultWhitelist(mode),
            Scale = ReadOptionalInt(table, "scale") ?? 3,
            Threshold = ReadOptionalByte(table, "threshold"),
            Invert = ReadOptionalBool(table, "invert") ?? false,
            TrimResult = ReadOptionalBool(table, "trim") ?? true,
            Mode = mode,
            MaxCandidates = ReadOptionalInt(table, "max_candidates") ?? 3,
        };
    }

    private static ScreenNumberReadMode ReadMode(Table table, string functionName)
    {
        var rawMode = ReadOptionalString(table, "mode");
        if (string.IsNullOrWhiteSpace(rawMode))
        {
            return ScreenNumberReadMode.Integer;
        }

        return rawMode.Trim().ToLowerInvariant() switch
        {
            "int" or "integer" => ScreenNumberReadMode.Integer,
            "float" or "double" or "decimal" => ScreenNumberReadMode.Float,
            _ => throw new ScriptRuntimeException($"{functionName} 的 options.mode 只支持 integer 或 float。"),
        };
    }

    private static string BuildDefaultWhitelist(ScreenNumberReadMode mode)
    {
        return mode == ScreenNumberReadMode.Integer
            ? "0123456789"
            : "0123456789.,";
    }

    private static string? ReadOptionalString(Table table, string key)
    {
        var value = table.Get(key);
        return value.Type == DataType.String ? value.String : null;
    }

    private static int? ReadOptionalInt(Table table, string key)
    {
        var value = table.Get(key);
        return value.Type == DataType.Number ? (int)value.Number : null;
    }

    private static byte? ReadOptionalByte(Table table, string key)
    {
        var number = ReadOptionalInt(table, key);
        if (number is null)
        {
            return null;
        }

        return (byte)Math.Clamp(number.Value, byte.MinValue, byte.MaxValue);
    }

    private static bool? ReadOptionalBool(Table table, string key)
    {
        var value = table.Get(key);
        return value.Type == DataType.Boolean ? value.Boolean : null;
    }

    private static ScreenRegion RequireScreenRegion(int x1, int y1, int x2, int y2, string functionName)
    {
        if (x2 <= x1 || y2 <= y1)
        {
            throw new ScriptRuntimeException(
                $"{functionName} 要求右下角坐标严格大于左上角坐标。");
        }

        return new ScreenRegion(x1, y1, x2 - x1, y2 - y1);
    }

    private readonly record struct ScreenRegion(int X, int Y, int Width, int Height);
}
