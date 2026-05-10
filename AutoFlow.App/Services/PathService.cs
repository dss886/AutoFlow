using System.IO;

namespace AutoFlow.App.Services;

public static class PathService
{
    public static string ResolveLogsDirectory()
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

    public static string ResolveScriptsDirectory()
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

    public static void EnsureDirectory(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
    }
}
