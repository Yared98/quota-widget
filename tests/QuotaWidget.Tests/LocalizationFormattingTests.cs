using System.Globalization;
using QuotaWidget.Localization;
using Xunit;

namespace QuotaWidget.Tests;

public class LocalizationFormattingTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ResolveLanguage_HandlesAutoAndSpecificCultures()
    {
        var ptCulture = new CultureInfo("pt-BR");
        var enCulture = new CultureInfo("en-US");

        Assert.Equal(Strings.PtBr, Strings.ResolveLanguage("auto", ptCulture));
        Assert.Equal(Strings.En, Strings.ResolveLanguage("auto", enCulture));
        Assert.Equal(Strings.PtBr, Strings.ResolveLanguage("pt-BR", enCulture));
        Assert.Equal(Strings.En, Strings.ResolveLanguage("en", ptCulture));
    }

    [Fact]
    public void TitleAndHeaderUpdate_BothLanguages()
    {
        Assert.Equal("AI quotas", Strings.GetTitle(Strings.En));
        Assert.Equal("Cotas de IA", Strings.GetTitle(Strings.PtBr));

        string enHeader = Strings.FormatHeaderUpdate(FixedNow, Strings.En);
        string ptHeader = Strings.FormatHeaderUpdate(FixedNow, Strings.PtBr);

        Assert.Equal($"updated {FixedNow.ToLocalTime():HH:mm}", enHeader);
        Assert.Equal($"atualizado {FixedNow.ToLocalTime():HH:mm}", ptHeader);
    }

    [Theory]
    [InlineData(0, "read 0 min ago", "lido há 0 min")]
    [InlineData(5, "read 5 min ago", "lido há 5 min")]
    [InlineData(45, "read 45 min ago", "lido há 45 min")]
    [InlineData(60, "read 1 h ago", "lido há 1 h")]
    [InlineData(120, "read 2 h ago", "lido há 2 h")]
    [InlineData(1440, "read 1 day ago", "lido há 1 dia")]
    [InlineData(2880, "read 2 days ago", "lido há 2 dias")]
    [InlineData(4320, "read 3 days ago", "lido há 3 dias")]
    public void ReadAgo_BothLanguages(int minutesAgo, string expectedEn, string expectedPt)
    {
        var readAt = FixedNow.AddMinutes(-minutesAgo);

        Assert.Equal(expectedEn, Strings.FormatReadAgo(readAt, FixedNow, Strings.En));
        Assert.Equal(expectedPt, Strings.FormatReadAgo(readAt, FixedNow, Strings.PtBr));
    }

    [Fact]
    public void FormatReset_Today_ResetsHHmm()
    {
        var reset = FixedNow.AddHours(2);

        string enReset = Strings.FormatReset(reset, isEstimated: false, FixedNow, Strings.En);
        string ptReset = Strings.FormatReset(reset, isEstimated: false, FixedNow, Strings.PtBr);

        Assert.Equal($"↻ {reset.ToLocalTime():HH:mm}", enReset);
        Assert.Equal($"↻ {reset.ToLocalTime():HH:mm}", ptReset);
    }

    [Fact]
    public void FormatReset_FutureDay_LocalizedDateFormats()
    {
        // Future date: 4 days later at 19:00
        var reset = FixedNow.AddDays(4);

        string enReset = Strings.FormatReset(reset, isEstimated: false, FixedNow, Strings.En);
        string ptReset = Strings.FormatReset(reset, isEstimated: false, FixedNow, Strings.PtBr);

        Assert.Equal($"↻ {reset.ToLocalTime():MM/dd} {reset.ToLocalTime():HH}h", enReset);
        Assert.Equal($"↻ {reset.ToLocalTime():dd/MM} {reset.ToLocalTime():HH}h", ptReset);
    }

    [Fact]
    public void FormatReset_Midnight_LocalizedDateFormats()
    {
        var localNow = FixedNow.ToLocalTime();
        var reset = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset).AddDays(5);

        string enReset = Strings.FormatReset(reset, isEstimated: false, FixedNow, Strings.En);
        string ptReset = Strings.FormatReset(reset, isEstimated: false, FixedNow, Strings.PtBr);

        Assert.Equal($"↻ {reset.ToLocalTime():MM/dd}", enReset);
        Assert.Equal($"↻ {reset.ToLocalTime():dd/MM}", ptReset);
    }

    [Fact]
    public void WindowName_Standard_BothLanguages()
    {
        Assert.Equal("5h", Strings.WindowName("5h", Strings.PtBr));
        Assert.Equal("5h", Strings.WindowName("5h", Strings.En));
        Assert.Equal("Semana", Strings.WindowName("Week", Strings.PtBr));
        Assert.Equal("Week", Strings.WindowName("Week", Strings.En));
        Assert.Equal("Mês", Strings.WindowName("Month", Strings.PtBr));
        Assert.Equal("Month", Strings.WindowName("Month", Strings.En));
    }

    [Fact]
    public void FormatReset_EstimatedWithoutReset_BothLanguages()
    {
        string enReset = Strings.FormatReset(null, isEstimated: true, FixedNow, Strings.En);
        string ptReset = Strings.FormatReset(null, isEstimated: true, FixedNow, Strings.PtBr);

        Assert.Equal("estimated", enReset);
        Assert.Equal("estimado", ptReset);
    }

    [Fact]
    public void FormatReset_NullResetAndNotEstimated_ReturnsEmptyString()
    {
        string enReset = Strings.FormatReset(null, isEstimated: false, FixedNow, Strings.En);
        string ptReset = Strings.FormatReset(null, isEstimated: false, FixedNow, Strings.PtBr);

        Assert.Equal(string.Empty, enReset);
        Assert.Equal(string.Empty, ptReset);
    }
}
