using QuotaWidget.Providers;
using Xunit;

namespace QuotaWidget.Tests;

public class PathResolverTests
{
    [Fact]
    public void Resolve_FindsExecutableInPath_WithExtension()
    {
        string pathEnv = @"C:\Windows;C:\Tools\Bin;C:\Program Files\Node";
        string pathextEnv = ".COM;.EXE;.BAT;.CMD";

        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Tools\Bin\claude.cmd"
        };

        string resolved = PathResolver.Resolve(
            "claude",
            pathEnv: pathEnv,
            pathextEnv: pathextEnv,
            fileExists: path => existingFiles.Contains(path));

        Assert.Equal(@"C:\Tools\Bin\claude.cmd", resolved, ignoreCase: true);
    }

    [Fact]
    public void Resolve_SkipsExtensionlessShimNextToCmd()
    {
        // npm installs both "claude" (a bash script) and "claude.cmd" in the same folder.
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\nvm\nodejs\claude", @"C:\nvm\nodejs\claude.cmd"
        };
        string resolved = PathResolver.Resolve("claude", @"C:\nvm\nodejs", ".EXE;.CMD", existing.Contains);
        Assert.Equal(@"C:\nvm\nodejs\claude.cmd", resolved, ignoreCase: true);
    }

    [Fact]
    public void Resolve_CommandAlreadyHasDirectory_BypassesPathSearch()
    {
        string pathEnv = @"C:\Windows;C:\Tools\Bin";
        string pathextEnv = ".EXE;.CMD";

        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Tools\Bin\mytool.exe"
        };

        // Command has directory separator
        string resolved = PathResolver.Resolve(
            @"C:\Custom\mytool.exe",
            pathEnv: pathEnv,
            pathextEnv: pathextEnv,
            fileExists: path => existingFiles.Contains(path));

        Assert.Equal(@"C:\Custom\mytool.exe", resolved);
    }

    [Fact]
    public void Resolve_CommandAlreadyHasExtension_FindsDirectly()
    {
        string pathEnv = @"C:\Windows;C:\Tools\Bin";
        string pathextEnv = ".EXE;.CMD";

        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Tools\Bin\agy.exe"
        };

        string resolved = PathResolver.Resolve(
            "agy.exe",
            pathEnv: pathEnv,
            pathextEnv: pathextEnv,
            fileExists: path => existingFiles.Contains(path));

        Assert.Equal(@"C:\Tools\Bin\agy.exe", resolved);
    }

    [Fact]
    public void Resolve_NotFoundInPath_ReturnsOriginalCommand()
    {
        string pathEnv = @"C:\Windows;C:\Tools\Bin";
        string pathextEnv = ".EXE;.CMD";

        string resolved = PathResolver.Resolve(
            "nonexistent_cli",
            pathEnv: pathEnv,
            pathextEnv: pathextEnv,
            fileExists: _ => false);

        Assert.Equal("nonexistent_cli", resolved);
    }
}
