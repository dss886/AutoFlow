using System.IO;
using System.Text;

namespace AutomationHost.App.Services;

public static class ExceptionLogService
{
    private static readonly object SyncRoot = new();

    public static string LogException(string source, Exception exception)
    {
        var logsDirectory = PathService.ResolveLogsDirectory();
        PathService.EnsureDirectory(logsDirectory);

        var logFilePath = Path.Combine(logsDirectory, $"automation-host-{DateTime.Now:yyyyMMdd}.log");
        var builder = new StringBuilder();
        builder.AppendLine(new string('=', 80));
        builder.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        builder.AppendLine($"来源: {source}");
        builder.AppendLine($"应用目录: {AppContext.BaseDirectory}");
        builder.AppendLine($"系统版本: {Environment.OSVersion}");
        builder.AppendLine($"进程位数: {(Environment.Is64BitProcess ? "x64" : "x86")}");
        builder.AppendLine("异常详情:");
        builder.AppendLine(exception.ToString());
        builder.AppendLine();

        lock (SyncRoot)
        {
            File.AppendAllText(logFilePath, builder.ToString(), Encoding.UTF8);
        }

        return logFilePath;
    }
}
