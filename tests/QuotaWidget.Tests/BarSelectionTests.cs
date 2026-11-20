using QuotaWidget.Model;
using Xunit;

namespace QuotaWidget.Tests;

public class BarSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MainFamilySelection_SelectsConfiguredFamily_CaseInsensitive()
    {
        var fam1 = new QuotaFamily("Claude and GPT models", new[] { new QuotaWindow("5h", 30.0) });
        var fam2 = new QuotaFamily("Gemini Models", new[] { new QuotaWindow("5h", 75.0), new QuotaWindow("Week", 40.0) });

        var snapshot = new QuotaSnapshot("g1", "G1", Now, new[] { fam1, fam2 });

        var main = QuotaLogic.GetMainFamily(snapshot, "gemini models");
        Assert.NotNull(main);
        Assert.Equal("Gemini Models", main.Name);

        var thick = QuotaLogic.GetThickWindow(snapshot, "gemini models");
        Assert.NotNull(thick);
        Assert.Equal("5h", thick.Name);
        Assert.Equal(75.0, thick.UsedPercent);

        var thin = QuotaLogic.GetThinWindow(snapshot, "gemini models");
        Assert.NotNull(thin);
        Assert.Equal("Week", thin.Name);
        Assert.Equal(40.0, thin.UsedPercent);
    }

    [Fact]
    public void MainFamilySelection_FallsBackToFirstFamily_WhenNotConfiguredOrNotFound()
    {
        var fam1 = new QuotaFamily("Pro Plan", new[] { new QuotaWindow("5h", 20.0), new QuotaWindow("Week", 60.0) });
        var fam2 = new QuotaFamily("Backup Plan", new[] { new QuotaWindow("Month", 10.0) });

        var snapshot = new QuotaSnapshot("p1", "P1", Now, new[] { fam1, fam2 });

        // Unconfigured
        var mainDefault = QuotaLogic.GetMainFamily(snapshot, null);
        Assert.NotNull(mainDefault);
        Assert.Equal("Pro Plan", mainDefault.Name);

        // Not found
        var mainNotFound = QuotaLogic.GetMainFamily(snapshot, "NonExistent");
        Assert.NotNull(mainNotFound);
        Assert.Equal("Pro Plan", mainNotFound.Name);
    }

    [Fact]
    public void BarSelection_OneWindow_ThickIsPresent_ThinIsAbsent()
    {
        var fam = new QuotaFamily("OpenCode", new[]
        {
            new QuotaWindow("Month", 65.0, isEstimated: true)
        });

        var snapshot = new QuotaSnapshot("oc", "OC", Now, new[] { fam });

        var thick = QuotaLogic.GetThickWindow(snapshot);
        Assert.NotNull(thick);
        Assert.Equal("Month", thick.Name);
        Assert.Equal(65.0, thick.UsedPercent);

        var thin = QuotaLogic.GetThinWindow(snapshot);
        Assert.Null(thin);
    }

    [Fact]
    public void BarSelection_NoWindows_ReturnsNull()
    {
        var fam = new QuotaFamily("Empty", Array.Empty<QuotaWindow>());
        var snapshot = new QuotaSnapshot("empty", "Empty", Now, new[] { fam });

        Assert.Null(QuotaLogic.GetThickWindow(snapshot));
        Assert.Null(QuotaLogic.GetThinWindow(snapshot));
    }
}
