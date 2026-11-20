using QuotaWidget.Export;
using QuotaWidget.Selection;

namespace QuotaWidget.Tests;

public class WorkerPickerTests
{
    [Theory]
    [InlineData("agy-alt:gemini-*", "agy-alt:gemini-1.5-flash", true)]
    [InlineData("agy-alt:gemini-*", "AGY-ALT:GEMINI-PRO", true)]
    [InlineData("*gemini*", "foo-gemini-bar", true)]
    [InlineData("*gemini*", "gemini", true)]
    [InlineData("claude-haiku-5-5", "*haiku*", true)]
    [InlineData("agy:gemini-flash", "*flash*", true)]
    [InlineData("claude", "gemini", false)]
    public void MatchesWildcard_BothSides_Works(string pattern, string input, bool expected)
    {
        bool actual = WorkerPicker.MatchesWildcard(pattern, input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Pick_RespectsWorkerFamilies_OverMainFamily()
    {
        var export = new QuotaExport
        {
            Version = 1,
            Providers = new List<QuotaExportProvider>
            {
                new()
                {
                    Id = "p1",
                    Label = "Dual",
                    Workers = new List<string> { "agy:*" },
                    WorkerFamilies = new Dictionary<string, string>
                    {
                        ["agy:claude-*"] = "Claude and GPT models"
                    },
                    Main = new QuotaExportMainWindow { Family = "Gemini Models", Window = "5h", UsedPercent = 20.0 },
                    Families = new List<QuotaExportFamily>
                    {
                        new("Gemini Models", new List<QuotaExportWindow>
                        {
                            new("5h", 20.0, null, false)
                        }),
                        new("Claude and GPT models", new List<QuotaExportWindow>
                        {
                            new("5h", 75.0, null, false)
                        })
                    }
                }
            }
        };

        // Querying agy:claude-3.5 should use limiting family "Claude and GPT models" (75%)
        var pickClaude = WorkerPicker.Pick("agy:claude-3.5", export);
        Assert.NotNull(pickClaude);
        Assert.Equal("p1", pickClaude.Id);
        Assert.Equal(75.0, pickClaude.UsedPercent);

        // Querying agy:gemini-flash has no workerFamilies match, so uses main family "Gemini Models" (20%)
        var pickGemini = WorkerPicker.Pick("agy:gemini-flash", export);
        Assert.NotNull(pickGemini);
        Assert.Equal("p1", pickGemini.Id);
        Assert.Equal(20.0, pickGemini.UsedPercent);
    }

    [Fact]
    public void Pick_Disqualifies_WhenAnyWindowInLimitingFamilyExceedsMax()
    {
        var export = new QuotaExport
        {
            Version = 1,
            Providers = new List<QuotaExportProvider>
            {
                new()
                {
                    Id = "p1",
                    Label = "P1",
                    Workers = new List<string> { "worker-a" },
                    Main = new QuotaExportMainWindow { Family = "Main", Window = "5h", UsedPercent = 30.0 },
                    Families = new List<QuotaExportFamily>
                    {
                        new("Main", new List<QuotaExportWindow>
                        {
                            new("5h", 30.0, null, false),
                            new("Week", 96.0, null, false) // Exceeds default max 95%
                        })
                    }
                }
            }
        };

        // Default max = 95 -> disqualified
        var pickDefault = WorkerPicker.Pick("worker-a", export, maxPercent: 95.0);
        Assert.Null(pickDefault);

        // Max = 97 -> qualifies, used % is 96.0 (limiting window)
        var pickHigherMax = WorkerPicker.Pick("worker-a", export, maxPercent: 97.0);
        Assert.NotNull(pickHigherMax);
        Assert.Equal("p1", pickHigherMax.Id);
        Assert.Equal(96.0, pickHigherMax.UsedPercent);
        Assert.Equal("Week", pickHigherMax.Window);
    }

    [Fact]
    public void Pick_Ties_LowerUsedThenConfigOrder()
    {
        var export = new QuotaExport
        {
            Version = 1,
            Providers = new List<QuotaExportProvider>
            {
                new()
                {
                    Id = "p1",
                    Label = "P1",
                    Workers = new List<string> { "model-x" },
                    Families = new List<QuotaExportFamily>
                    {
                        new("Default", new List<QuotaExportWindow> { new("5h", 50.0, null, false) })
                    }
                },
                new()
                {
                    Id = "p2",
                    Label = "P2",
                    Workers = new List<string> { "model-x" },
                    Families = new List<QuotaExportFamily>
                    {
                        new("Default", new List<QuotaExportWindow> { new("5h", 30.0, null, false) })
                    }
                },
                new()
                {
                    Id = "p3",
                    Label = "P3",
                    Workers = new List<string> { "model-x" },
                    Families = new List<QuotaExportFamily>
                    {
                        new("Default", new List<QuotaExportWindow> { new("5h", 30.0, null, false) })
                    }
                }
            }
        };

        // Lower used wins: p2 and p3 (30%) beat p1 (50%)
        // Between p2 and p3 (both 30%), ties broken by config order:
        var configOrderP2First = new[] { "p1", "p2", "p3" };
        var pick1 = WorkerPicker.Pick("model-x", export, configOrder: configOrderP2First);
        Assert.NotNull(pick1);
        Assert.Equal("p2", pick1.Id);

        // If config order puts p3 before p2:
        var configOrderP3First = new[] { "p1", "p3", "p2" };
        var pick2 = WorkerPicker.Pick("model-x", export, configOrder: configOrderP3First);
        Assert.NotNull(pick2);
        Assert.Equal("p3", pick2.Id);
    }

    [Fact]
    public void Pick_ExcludesProvidersWithErrors_AndReturnsNullWhenNoneQualify()
    {
        var export = new QuotaExport
        {
            Version = 1,
            Providers = new List<QuotaExportProvider>
            {
                new()
                {
                    Id = "broken",
                    Label = "Broken",
                    Workers = new List<string> { "model-z" },
                    Error = "Connection refused",
                    Families = new List<QuotaExportFamily>
                    {
                        new("Default", new List<QuotaExportWindow> { new("5h", 10.0, null, false) })
                    }
                }
            }
        };

        var pickError = WorkerPicker.Pick("model-z", export);
        Assert.Null(pickError);

        var pickUnknownWorker = WorkerPicker.Pick("unknown-model", export);
        Assert.Null(pickUnknownWorker);
    }
}
