using System.IO;
using AutoFlow.App.Models;

namespace AutoFlow.App.Services;

public sealed class ScriptCatalogService
{
    public IReadOnlyList<ScriptDefinition> LoadScripts(string scriptsDirectory)
    {
        PathService.EnsureDirectory(scriptsDirectory);

        var scripts = new List<ScriptDefinition>();
        foreach (var filePath in Directory
                     .EnumerateFiles(scriptsDirectory, "*.lua", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var script = TryReadScriptMetadata(filePath);
            if (script is not null)
            {
                scripts.Add(script);
            }
        }

        return scripts;
    }

    private static ScriptDefinition? TryReadScriptMetadata(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var description = "未提供描述";

        try
        {
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
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException) when (!File.Exists(filePath))
        {
            return null;
        }
        catch (IOException)
        {
            // 文件正在被编辑器替换或短暂占用时，保留基础信息以避免刷新直接失败。
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
