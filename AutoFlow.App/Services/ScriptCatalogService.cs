using System.IO;
using AutoFlow.App.Models;

namespace AutoFlow.App.Services;

public sealed class ScriptCatalogService
{
    public IReadOnlyList<ScriptDefinition> LoadScripts(string scriptsDirectory)
    {
        PathService.EnsureDirectory(scriptsDirectory);

        return Directory
            .EnumerateFiles(scriptsDirectory, "*.lua", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(ReadScriptMetadata)
            .ToList();
    }

    private static ScriptDefinition ReadScriptMetadata(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var description = "未提供描述";

        foreach (var line in File.ReadLines(filePath).Take(20))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            trimmed = trimmed[2..].Trim();
            if (trimmed.StartsWith("@name:", StringComparison.OrdinalIgnoreCase))
            {
                name = trimmed[6..].Trim();
            }
            else if (trimmed.StartsWith("@description:", StringComparison.OrdinalIgnoreCase))
            {
                description = trimmed[13..].Trim();
            }
        }

        return new ScriptDefinition
        {
            Name = name,
            Description = description,
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
        };
    }
}
