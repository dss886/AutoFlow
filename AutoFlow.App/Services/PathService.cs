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
        return Path.Combine(ResolveExecutableDirectory(), "logs");
    }

    public string ResolveScriptsDirectory()
    {
        return Path.Combine(ResolveExecutableDirectory(), "scripts");
    }

    public string ResolveTessDataDirectory()
    {
        return Path.Combine(ResolveExecutableDirectory(), "tessdata");
    }

    public string ResolveNativeLibraryDirectory()
    {
        return Path.Combine(ResolveExecutableDirectory(), "bin");
    }

    public string ResolveExecutableDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var directory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }

        return AppContext.BaseDirectory;
    }

    public void EnsureDirectory(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
    }
}
