using QuotaWidget.Cli;

namespace QuotaWidget.Tests;

public class CliParsingTests
{
    [Fact]
    public void Parse_Status_Command()
    {
        var opts = CliOptions.Parse(new[] { "status" });
        Assert.Equal(CliCommand.Status, opts.Command);
        Assert.Null(opts.Error);
    }

    [Fact]
    public void Parse_Json_Command()
    {
        var opts = CliOptions.Parse(new[] { "json" });
        Assert.Equal(CliCommand.Json, opts.Command);
        Assert.Null(opts.Error);
    }

    [Fact]
    public void Parse_Refresh_DefaultsAndFlags()
    {
        var opts = CliOptions.Parse(new[] { "refresh" });
        Assert.Equal(CliCommand.Refresh, opts.Command);
        Assert.Empty(opts.Ids);
        Assert.False(opts.Wait);
        Assert.False(opts.Force);
        Assert.Null(opts.TimeoutSeconds);

        var optsWithArgs = CliOptions.Parse(new[] { "refresh", "claude", "gemini", "--wait", "--force", "--timeout", "45" });
        Assert.Equal(CliCommand.Refresh, optsWithArgs.Command);
        Assert.Equal(new[] { "claude", "gemini" }, optsWithArgs.Ids);
        Assert.True(optsWithArgs.Wait);
        Assert.True(optsWithArgs.Force);
        Assert.Equal(45, optsWithArgs.TimeoutSeconds);

        var optsTimeoutEquals = CliOptions.Parse(new[] { "refresh", "--timeout=10" });
        Assert.Equal(10, optsTimeoutEquals.TimeoutSeconds);
    }

    [Fact]
    public void Parse_Pick_WorkerAndOptions()
    {
        var opts = CliOptions.Parse(new[] { "pick", "agy:gemini-*" });
        Assert.Equal(CliCommand.Pick, opts.Command);
        Assert.Equal("agy:gemini-*", opts.Worker);
        Assert.Equal(95.0, opts.MaxPercent);
        Assert.False(opts.Json);

        var optsWithFlags = CliOptions.Parse(new[] { "pick", "claude-haiku", "--max", "80.5", "--json" });
        Assert.Equal(CliCommand.Pick, optsWithFlags.Command);
        Assert.Equal("claude-haiku", optsWithFlags.Worker);
        Assert.Equal(80.5, optsWithFlags.MaxPercent);
        Assert.True(optsWithFlags.Json);

        var optsMissingWorker = CliOptions.Parse(new[] { "pick", "--json" });
        Assert.NotNull(optsMissingWorker.Error);
    }

    [Fact]
    public void Parse_UnknownCommand_ReturnsError()
    {
        var opts = CliOptions.Parse(new[] { "nonexistent" });
        Assert.Equal(CliCommand.Unknown, opts.Command);
        Assert.NotNull(opts.Error);
    }
}
