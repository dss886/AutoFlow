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
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "logs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            if (current.EnumerateFiles("*.sln").Any())
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "logs");
    }

    public string ResolveScriptsDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "scripts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            if (current.EnumerateFiles("*.sln").Any())
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "scripts");
    }

    public void EnsureDirectory(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
    }
}
