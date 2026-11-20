using System.IO;

namespace QuotaWidget.Providers;

public static class PathResolver
{
    public static string Resolve(
        string command,
        string? pathEnv = null,
        string? pathextEnv = null,
        Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            return command;

        command = Environment.ExpandEnvironmentVariables(command.Trim());

        // If the command already has directory separators or is rooted, do not search PATH
        if (command.Contains(Path.DirectorySeparatorChar) ||
            command.Contains(Path.AltDirectorySeparatorChar) ||
            Path.IsPathRooted(command))
        {
            return command;
        }

        fileExists ??= System.IO.File.Exists;
        pathEnv ??= Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        pathextEnv ??= Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";

        var dirs = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var exts = pathextEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool hasExtension = !string.IsNullOrEmpty(Path.GetExtension(command));

        foreach (var dir in dirs)
        {
            if (hasExtension)
            {
                string fullPath = Path.Combine(dir, command);
                if (fileExists(fullPath))
                    return fullPath;
            }
            else
            {
                // An extensionless file (e.g. npm's bash shim next to claude.cmd) cannot be started by
                // CreateProcess, so only PATHEXT candidates count.
                // Match with extensions from PATHEXT
                foreach (var ext in exts)
                {
                    string candidateExt = ext.StartsWith('.') ? ext : "." + ext;
                    string candidate = Path.Combine(dir, command + candidateExt);
                    if (fileExists(candidate))
                        return candidate;
                }
            }
        }

        return command;
    }
}
