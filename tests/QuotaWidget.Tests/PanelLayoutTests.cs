using QuotaWidget.Model;
using Xunit;

namespace QuotaWidget.Tests;

public class PanelLayoutTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void LayoutDecision_Single_WhenOneOrZeroFamilies()
    {
        var emptySnapshot = new QuotaSnapshot("p1", "Claude", FixedNow, Array.Empty<QuotaFamily>());
        Assert.Equal(ProviderLayoutMode.Single, PanelLayout.GetLayoutMode(emptySnapshot));

        var singleFamOneWin = new QuotaSnapshot("p1", "Claude", FixedNow, new[]
        {
            new QuotaFamily("Claude", new[] { new QuotaWindow("5h", 50.0) })
        });
        Assert.Equal(ProviderLayoutMode.Single, PanelLayout.GetLayoutMode(singleFamOneWin));

        var singleFamTwoWin = new QuotaSnapshot("p1", "Claude", FixedNow, new[]
        {
            new QuotaFamily("Claude", new[]
            {
                new QuotaWindow("5h", 56.0),
                new QuotaWindow("Week", 56.0)
            })
        });
        Assert.Equal(ProviderLayoutMode.Single, PanelLayout.GetLayoutMode(singleFamTwoWin));
    }

    [Fact]
    public void LayoutDecision_Compact_WhenEveryFamilyHasExactlyOneWindow()
    {
        var compactSnapshot = new QuotaSnapshot("g2", "G2", FixedNow, new[]
        {
            new QuotaFamily("Gemini Models", new[] { new QuotaWindow("5h", 100.0) }),
            new QuotaFamily("Claude and GPT models", new[] { new QuotaWindow("5h", 100.0) })
        });
        Assert.Equal(ProviderLayoutMode.Compact, PanelLayout.GetLayoutMode(compactSnapshot));

        var openCodeSnapshot = new QuotaSnapshot("oc", "OC", FixedNow, new[]
        {
            new QuotaFamily("Haiku", new[] { new QuotaWindow("Month", 0.0) }),
            new QuotaFamily("Luna", new[] { new QuotaWindow("Month", 0.0) })
        });
        Assert.Equal(ProviderLayoutMode.Compact, PanelLayout.GetLayoutMode(openCodeSnapshot));
    }

    [Fact]
    public void LayoutDecision_Grouped_WhenMultipleFamiliesAndAnyHasMultipleWindows()
    {
        var groupedSnapshot = new QuotaSnapshot("g1", "G1", FixedNow, new[]
        {
            new QuotaFamily("Gemini Models", new[]
            {
                new QuotaWindow("5h", 24.0),
                new QuotaWindow("Week", 22.0)
            }),
            new QuotaFamily("Claude & GPT Models", new[]
            {
                new QuotaWindow("5h", 100.0),
                new QuotaWindow("Week", 53.0)
            }),
            new QuotaFamily("Gemini 4 Argon", new[]
            {
                new QuotaWindow("Week", 0.0)
            })
        });
        Assert.Equal(ProviderLayoutMode.Grouped, PanelLayout.GetLayoutMode(groupedSnapshot));
    }

    [Theory]
    [InlineData("Claude and GPT models", "Cl/GPT")]
    [InlineData("Claude & GPT Models", "Cl/GPT")]
    [InlineData("Gemini Models", "Gemini")]
    [InlineData("Gemini 4 Argon", "Gemini")]
    [InlineData("Haiku", "Haiku")]
    [InlineData("Luna", "Luna")]
    [InlineData("Supercalifragilistic", "Superca")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void PeriodShortNames_ShortensFamilyNames(string? familyName, string expected)
    {
        Assert.Equal(expected, PanelLayout.ShortenFamilyName(familyName));
    }

    [Fact]
    public void ExhaustedDetection_TrueWhenAllWindowsInAllFamiliesAtOrAbove100()
    {
        var exhaustedSnapshot = new QuotaSnapshot("g2", "G2", FixedNow, new[]
        {
            new QuotaFamily("Gemini Models", new[] { new QuotaWindow("5h", 100.0) }),
            new QuotaFamily("Claude & GPT Models", new[] { new QuotaWindow("5h", 100.0) })
        });
        Assert.True(PanelLayout.IsExhausted(exhaustedSnapshot));

        var partialSnapshot = new QuotaSnapshot("g1", "G1", FixedNow, new[]
        {
            new QuotaFamily("Gemini Models", new[] { new QuotaWindow("5h", 100.0) }),
            new QuotaFamily("Claude & GPT Models", new[] { new QuotaWindow("5h", 80.0) })
        });
        Assert.False(PanelLayout.IsExhausted(partialSnapshot));

        var emptySnapshot = new QuotaSnapshot("p", "P", FixedNow, Array.Empty<QuotaFamily>());
        Assert.False(PanelLayout.IsExhausted(emptySnapshot));
    }

    [Fact]
    public void StatusDotColor_FreshAndErrorFree_IsGreen()
    {
        var s1 = new QuotaSnapshot("p1", "P1", FixedNow, new[]
        {
            new QuotaFamily("Fam", new[] { new QuotaWindow("5h", 10.0) })
        });
        var s2 = new QuotaSnapshot("p2", "P2", FixedNow, new[]
        {
            new QuotaFamily("Fam", new[] { new QuotaWindow("5h", 20.0) })
        });

        string dot = PanelLayout.GetStatusDotColor(new[] { s1, s2 }, FixedNow, refreshMinutes: 5);
        Assert.Equal(ColorThresholds.GreenHex, dot);
    }

    [Fact]
    public void StatusDotColor_AnyHasError_IsRed()
    {
        var s1 = new QuotaSnapshot("p1", "P1", FixedNow, new[]
        {
            new QuotaFamily("Fam", new[] { new QuotaWindow("5h", 10.0) })
        });
        var s2 = new QuotaSnapshot("p2", "P2", FixedNow, error: "Network timeout");

        string dot = PanelLayout.GetStatusDotColor(new[] { s1, s2 }, FixedNow, refreshMinutes: 5);
        Assert.Equal(ColorThresholds.RedHex, dot);
    }

    [Fact]
    public void StatusDotColor_AnyStaleAndNoError_IsAmber()
    {
        var s1 = new QuotaSnapshot("p1", "P1", FixedNow, new[]
        {
            new QuotaFamily("Fam", new[] { new QuotaWindow("5h", 10.0) })
        });
        // 15 min old when refresh is 5 min -> stale (threshold is 2x = 10 min)
        var s2 = new QuotaSnapshot("p2", "P2", FixedNow.AddMinutes(-15), new[]
        {
            new QuotaFamily("Fam", new[] { new QuotaWindow("5h", 20.0) })
        });

        string dot = PanelLayout.GetStatusDotColor(new[] { s1, s2 }, FixedNow, refreshMinutes: 5);
        Assert.Equal(ColorThresholds.AmberHex, dot);
    }
}
