using System.Text.Json;
using QuotaWidget.Config;
using QuotaWidget.Export;
using QuotaWidget.Model;

namespace QuotaWidget.Tests;

public class ExportTests
{
    [Fact]
    public void Export_Builds_CorrectJsonShape_AndFields()
    {
        var now = new DateTimeOffset(2026, 11, 19, 22, 40, 0, TimeSpan.FromHours(-3));
        var config = new WidgetConfig
        {
            RefreshMinutes = 10,
            Providers = new List<ProviderConfig>
            {
                new()
                {
                    Id = "g2",
                    Type = "antigravity",
                    Label = "G2",
                    Enabled = true,
                    Workers = new List<string> { "agy-alt:gemini-*", "agy-alt:claude-*" },
                    WorkerFamilies = new Dictionary<string, string> { ["agy-alt:claude-*"] = "Claude and GPT models" }
                },
                new()
                {
                    Id = "disabled-p",
                    Type = "antigravity",
                    Label = "Disabled",
                    Enabled = false
                }
            }
        };

        var resetTime = new DateTimeOffset(2026, 11, 26, 21, 25, 26, TimeSpan.Zero);
        var snapshots = new Dictionary<string, QuotaSnapshot>
        {
            ["g2"] = new QuotaSnapshot(
                "g2",
                "G2",
                now.AddMinutes(-2),
                new List<QuotaFamily>
                {
                    new("Gemini Models", new List<QuotaWindow>
                    {
                        new("Week", 61.54, resetTime, isEstimated: false)
                    })
                }
            )
        };

        var export = QuotaExporter.Build(config, snapshots, now);

        Assert.Equal(1, export.Version);
        Assert.Equal(now.ToString("yyyy-MM-ddTHH:mm:sszzz"), export.GeneratedAt);
        Assert.Single(export.Providers); // Disabled provider excluded

        var p = export.Providers[0];
        Assert.Equal("g2", p.Id);
        Assert.Equal("antigravity", p.Type);
        Assert.Equal("G2", p.Label);
        Assert.Equal(now.AddMinutes(-2).ToString("yyyy-MM-ddTHH:mm:sszzz"), p.ReadAt);
        Assert.False(p.Stale);
        Assert.Null(p.Error);
        Assert.Equal(new[] { "agy-alt:gemini-*", "agy-alt:claude-*" }, p.Workers);

        // Main window check
        Assert.NotNull(p.Main);
        Assert.Equal("Gemini Models", p.Main!.Family);
        Assert.Equal("Week", p.Main.Window);
        Assert.Equal(61.5, p.Main.UsedPercent); // Rounded to 1 decimal
        Assert.Equal(resetTime.ToString("yyyy-MM-ddTHH:mm:sszzz"), p.Main.ResetsAt);

        // Families check
        Assert.Single(p.Families);
        Assert.Equal("Gemini Models", p.Families[0].Name);
        Assert.Single(p.Families[0].Windows);
        var win = p.Families[0].Windows[0];
        Assert.Equal("Week", win.Name);
        Assert.Equal(61.5, win.UsedPercent);
        Assert.Equal(resetTime.ToString("yyyy-MM-ddTHH:mm:sszzz"), win.ResetsAt);
        Assert.False(win.Estimated);

        // Check JSON serialization round-trip
        string json = JsonSerializer.Serialize(export, QuotaExporter.JsonOptions);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"generated_at\"", json);
        Assert.Contains("\"providers\"", json);
        Assert.Contains("\"used_percent\": 61.5", json);
    }

    [Fact]
    public void Export_Stale_WhenOlderThanTwoTimesRefreshOrEstimated()
    {
        var now = new DateTimeOffset(2026, 11, 19, 22, 0, 0, TimeSpan.Zero);
        var config = new WidgetConfig
        {
            RefreshMinutes = 10,
            Providers = new List<ProviderConfig>
            {
                new() { Id = "old", Type = "command", Label = "Old", Enabled = true },
                new() { Id = "est", Type = "command", Label = "Est", Enabled = true },
                new() { Id = "fresh", Type = "command", Label = "Fresh", Enabled = true }
            }
        };

        var snapshots = new Dictionary<string, QuotaSnapshot>
        {
            // 25 minutes ago > 2 * 10
            ["old"] = new QuotaSnapshot("old", "Old", now.AddMinutes(-25), new List<QuotaFamily>
            {
                new("Default", new List<QuotaWindow> { new("5h", 30.0, null, isEstimated: false) })
            }),
            // 5 minutes ago but estimated
            ["est"] = new QuotaSnapshot("est", "Est", now.AddMinutes(-5), new List<QuotaFamily>
            {
                new("Default", new List<QuotaWindow> { new("Month", 30.0, null, isEstimated: true) })
            }),
            // 5 minutes ago and not estimated
            ["fresh"] = new QuotaSnapshot("fresh", "Fresh", now.AddMinutes(-5), new List<QuotaFamily>
            {
                new("Default", new List<QuotaWindow> { new("5h", 30.0, null, isEstimated: false) })
            })
        };

        var export = QuotaExporter.Build(config, snapshots, now);

        var oldP = export.Providers.First(p => p.Id == "old");
        var estP = export.Providers.First(p => p.Id == "est");
        var freshP = export.Providers.First(p => p.Id == "fresh");

        Assert.True(oldP.Stale);
        Assert.True(estP.Stale);
        Assert.False(freshP.Stale);
    }

    [Fact]
    public void Export_ProviderWithConfigError_HasErrorAndNoFamilies()
    {
        var now = DateTimeOffset.UtcNow;
        var config = new WidgetConfig
        {
            Providers = new List<ProviderConfig>
            {
                new()
                {
                    Id = "broken",
                    Type = "unknown-type",
                    Label = "Broken",
                    Enabled = true,
                    ValidationError = "Unknown provider type: unknown-type"
                }
            }
        };

        var export = QuotaExporter.Build(config, new Dictionary<string, QuotaSnapshot>(), now);

        Assert.Single(export.Providers);
        var p = export.Providers[0];
        Assert.Equal("broken", p.Id);
        Assert.Equal("Unknown provider type: unknown-type", p.Error);
        Assert.True(p.Stale);
        Assert.Null(p.Main);
        Assert.Empty(p.Families);
    }
}
