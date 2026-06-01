using System.IO;

namespace AutoFlow.App.Services;

public sealed class PathService
{
    public string ResolveAppDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoFlow");
    }

    public string ResolveLogsDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "logs");
    }

    public string ResolveScriptsDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "scripts");
    }

    public string ResolveTessDataDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "tessdata");
    }

    public void EnsureDirectory(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
    }
}
