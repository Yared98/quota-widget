using QuotaWidget.Config;
using QuotaWidget.Providers.OpenCode;
using Xunit;

namespace QuotaWidget.Tests;

public class OpenCodeParserTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("opencode-go/*", "opencode-go/claude-haiku-5-5", true)]
    [InlineData("opencode-go/*", "opencode-go/gpt-4o", true)]
    [InlineData("opencode-go/*", "anthropic/claude-3-5-sonnet", false)]
    [InlineData("*", "anything/at-all", true)]
    [InlineData("*haiku*", "anthropic/claude-3-5-haiku-20241022", true)]
    [InlineData("*-haiku-*", "opencode-go/claude-haiku-5-5", true)]
    [InlineData("openai/gpt-4*", "openai/gpt-4o", true)]
    [InlineData("openai/gpt-4*", "openai/o1-preview", false)]
    public void BudgetPatterns_WithWildcard(string pattern, string input, bool expectedMatch)
    {
        bool actual = OpenCodeParser.MatchesPattern(pattern, input);
        Assert.Equal(expectedMatch, actual);
    }

    [Fact]
    public void CalculatePeriod_SameMonth_WhenNowOnOrAfterResetDay()
    {
        // 2026-11-20, resetDay = 15 -> periodStart = 2026-11-15, nextReset = 2026-12-15, days = 5
        var now = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        var (periodStart, nextReset, days) = OpenCodeParser.CalculatePeriod(now, 15);

        Assert.Equal(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), periodStart);
        Assert.Equal(new DateTimeOffset(2026, 12, 15, 0, 0, 0, TimeSpan.Zero), nextReset);
        Assert.Equal(5, days);
    }

    [Fact]
    public void CalculatePeriod_TodayIsResetDay_DaysIsZero()
    {
        var now = new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);
        var (periodStart, nextReset, days) = OpenCodeParser.CalculatePeriod(now, 1);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), periodStart);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), nextReset);
        Assert.Equal(0, days);
    }

    [Fact]
    public void CalculatePeriod_PreviousMonth_WhenNowBeforeResetDay()
    {
        // 2026-11-10, resetDay = 15 -> periodStart = 2026-10-15, nextReset = 2026-11-15, days = 26
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var (periodStart, nextReset, days) = OpenCodeParser.CalculatePeriod(now, 15);

        Assert.Equal(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero), periodStart);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), nextReset);
        Assert.Equal(26, days);
    }

    [Fact]
    public void CalculatePeriod_YearBoundary_JanuaryRollingBackToDecember()
    {
        // 2026-01-05, resetDay = 15 -> periodStart = 2025-12-15, nextReset = 2026-01-15, days = 21
        var now = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);
        var (periodStart, nextReset, days) = OpenCodeParser.CalculatePeriod(now, 15);

        Assert.Equal(new DateTimeOffset(2025, 12, 15, 0, 0, 0, TimeSpan.Zero), periodStart);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), nextReset);
        Assert.Equal(21, days);
    }

    [Fact]
    public void CalculatePeriod_YearBoundary_DecemberRollingForwardToJanuary()
    {
        // 2026-12-20, resetDay = 15 -> periodStart = 2026-12-15, nextReset = 2027-01-15, days = 5
        var now = new DateTimeOffset(2026, 12, 20, 12, 0, 0, TimeSpan.Zero);
        var (periodStart, nextReset, days) = OpenCodeParser.CalculatePeriod(now, 15);

        Assert.Equal(new DateTimeOffset(2026, 12, 15, 0, 0, 0, TimeSpan.Zero), periodStart);
        Assert.Equal(new DateTimeOffset(2027, 1, 15, 0, 0, 0, TimeSpan.Zero), nextReset);
        Assert.Equal(5, days);
    }

    [Fact]
    public void Parse_SumsMatchingModels_CalculatesUsedPercentAndClamp()
    {
        string json = """
        {
          "models": [
            { "id": "claude-haiku-5-5", "providerID": "opencode-go", "cost": 4.50 },
            { "id": "claude-sonnet-4", "providerID": "opencode-go", "cost": 3.00 },
            { "id": "gpt-4o", "providerID": "openai", "cost": 10.00 }
          ]
        }
        """;

        var budgets = new List<BudgetConfig>
        {
            new("Go", new[] { "opencode-go/*" }, usd: 15.0),
            new("All", new[] { "*" }, usd: 10.0) // 17.50 cost > 10.0 usd -> clamp to 100%
        };

        var nextReset = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var snapshot = OpenCodeParser.Parse(json, FixedNow, "oc", "OC", budgets, nextReset);

        Assert.Equal(2, snapshot.Families.Count);

        // Budget "Go": 4.50 + 3.00 = 7.50 / 15.0 = 50.0%
        var fam1 = snapshot.Families[0];
        Assert.Equal("Go", fam1.Name);
        Assert.Single(fam1.Windows);
        var w1 = fam1.Windows[0];
        Assert.Equal("Month", w1.Name);
        Assert.Equal(50.0, w1.UsedPercent);
        Assert.Equal(nextReset, w1.ResetsAt);
        Assert.True(w1.IsEstimated);

        // Budget "All": 4.50 + 3.00 + 10.00 = 17.50 / 10.0 = 175% -> clamped to 100.0%
        var fam2 = snapshot.Families[1];
        Assert.Equal("All", fam2.Name);
        var w2 = fam2.Windows[0];
        Assert.Equal("Month", w2.Name);
        Assert.Equal(100.0, w2.UsedPercent);
        Assert.True(w2.IsEstimated);
    }

    [Fact]
    public void Parse_RealNestedFormat_RequestAndTokenBudgets()
    {
        // Shape of `opencode stats --models --cost --json`; subscription models report cost 0.
        string json = """
        { "sessions": 3, "cost": 1.5,
          "models": [
            { "model": { "id": "claude-haiku-5-5", "providerID": "opencode-go", "variant": "default" },
              "steps": 190, "tokens": { "input": 9000, "output": 500, "reasoning": 500, "cache": { "read": 99999, "write": 0 } },
              "cost": 0 },
            { "model": { "id": "gpt-6-luna", "providerID": "opencode-go", "variant": "default" },
              "steps": 10, "tokens": { "input": 100, "output": 0, "reasoning": 0, "cache": { "read": 0, "write": 0 } },
              "cost": 0 },
            { "model": { "id": "gemini-3.6-flash", "providerID": "google", "variant": "default" },
              "steps": 50, "tokens": { "input": 1, "output": 1, "reasoning": 0 }, "cost": 1.5 }
          ] }
        """;
        var budgets = new List<BudgetConfig>
        {
            new("Haiku", new[] { "opencode-go/claude-haiku-5-5" }, usd: 15.0) { Requests = 1900 },
            new("Luna", new[] { "opencode-go/gpt-6-luna" }, usd: 0) { Tokens = 1000 },
            new("Google", new[] { "google/*" }, usd: 3.0),
        };
        var snapshot = OpenCodeParser.Parse(json, FixedNow, "oc", "OC", budgets, FixedNow.AddDays(10));

        Assert.Null(snapshot.Error);
        Assert.Equal(10.0, snapshot.Families[0].Windows[0].UsedPercent, 3); // 190 / 1900 requests (cost 0 ignored)
        Assert.Equal(10.0, snapshot.Families[1].Windows[0].UsedPercent, 3); // 100 / 1000 tokens, cache excluded
        Assert.Equal(50.0, snapshot.Families[2].Windows[0].UsedPercent, 3); // 1.5 / 3 usd
    }

    [Fact]
    public void Parse_FlatFormatStillSupported_SecondBudget()
    {
        string json = """{ "models": [ { "id": "x", "providerID": "p", "cost": 5 } ] }""";
        var snapshot = OpenCodeParser.Parse(json, FixedNow, "oc", "OC",
            new[] { new BudgetConfig("All", new[] { "*" }, 10.0) }, FixedNow);
        var fam2 = snapshot.Families[0];
        Assert.Equal(50.0, fam2.Windows[0].UsedPercent, 3);
    }
}
