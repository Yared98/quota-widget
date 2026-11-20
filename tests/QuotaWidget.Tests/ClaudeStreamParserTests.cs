using QuotaWidget.Providers.ClaudeCode;
using Xunit;

namespace QuotaWidget.Tests;

public class ClaudeStreamParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 22, 0, 0, TimeSpan.Zero);

    private const string Sample =
        "{\"type\":\"system\",\"subtype\":\"init\"}\n" +
        "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}}\n" +
        "{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"resetsAt\":1795137600," +
        "\"rateLimitType\":\"five_hour\",\"unifiedWindows\":{\"five_hour\":{\"utilization\":0.4,\"resetsAt\":1795137600," +
        "\"status\":\"allowed\"},\"seven_day\":{\"utilization\":0.54,\"resetsAt\":1795154400,\"status\":\"allowed\"}}}}\r\n" +
        "{\"type\":\"result\"}\n";

    [Fact]
    public void Parses_both_windows_as_used_percent()
    {
        var q = ClaudeStreamParser.Parse(Sample, Now);
        Assert.NotNull(q);
        Assert.Equal(Now, q!.ReadAt);
        var fam = Assert.Single(q.Families);
        Assert.Equal("Claude", fam.Name);
        Assert.Equal(2, fam.Windows.Count);

        var five = fam.Windows[0];
        Assert.Equal("5h", five.Name);
        Assert.Equal(40.0, five.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795137600), five.ResetsAt);
        Assert.False(five.IsEstimated);

        var week = fam.Windows[1];
        Assert.Equal("Week", week.Name);
        Assert.Equal(54.0, week.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795154400), week.ResetsAt);
        Assert.False(week.IsEstimated);
    }

    [Fact]
    public void Returns_null_without_event()
    {
        Assert.Null(ClaudeStreamParser.Parse("{\"type\":\"result\"}\nnot json\n", Now));
        Assert.Null(ClaudeStreamParser.Parse("", Now));
    }

    [Fact]
    public void Missing_week_keeps_five_hour()
    {
        const string line = "{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"unifiedWindows\":{\"five_hour\":{\"utilization\":1.0,\"resetsAt\":1795137600}}}}";
        var q = ClaudeStreamParser.Parse(line, Now);
        Assert.NotNull(q);
        var w = Assert.Single(q!.Families[0].Windows);
        Assert.Equal("5h", w.Name);
        Assert.Equal(100.0, w.UsedPercent);
    }
}
