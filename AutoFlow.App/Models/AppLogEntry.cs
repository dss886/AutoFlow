namespace AutoFlow.App.Models;

public enum LogSource
{
    System,
    Script,
}

public enum LogLevel
{
    Verbose,
    Debug,
    Info,
    Warning,
    Error,
}

public sealed record AppLogEntry(DateTime Timestamp, LogSource Source, LogLevel Level, string Message)
{
    public static AppLogEntry CreateSystem(string message, LogLevel level = LogLevel.Info)
    {
        return new AppLogEntry(DateTime.Now, LogSource.System, level, message);
    }

    public static AppLogEntry CreateScript(string message, LogLevel level = LogLevel.Info)
    {
        return new AppLogEntry(DateTime.Now, LogSource.Script, level, message);
    }

    public string TimestampText => Timestamp.ToString("HH:mm:ss.fff");

    public string SourceLabel => GetSourceLabel(Source);

    public string LevelLabel => GetLevelLabel(Level);

    public string Format()
    {
        return $"[{TimestampText}][{LevelLabel}][{SourceLabel}] {Message}";
    }

    private static string GetSourceLabel(LogSource source)
    {
        return source switch
        {
            LogSource.System => "系统",
            LogSource.Script => "脚本",
            _ => "系统",
        };
    }

    private static string GetLevelLabel(LogLevel level)
    {
        return level switch
        {
            LogLevel.Verbose => "V",
            LogLevel.Debug => "D",
            LogLevel.Info => "I",
            LogLevel.Warning => "W",
            LogLevel.Error => "E",
            _ => "I",
        };
    }
}
