using QuotaWidget.Providers.Antigravity;
using Xunit;

namespace QuotaWidget.Tests;

public class AgyParserTests
{
    private const string SampleOutput =
        "Gemini Models\tWeekly Limit Remaining\t79%\t2026-11-24T23:42:49Z\r\n" +
        "Gemini Models\tFive Hour Limit Remaining\t83%\t2026-11-20T01:45:49Z\r\n" +
        "Claude and GPT models\tWeekly Limit Remaining\t47%\t2026-11-25T22:14:03Z\r\n" +
        "Claude and GPT models\tFive Hour Limit Remaining\t0%\t2026-11-20T01:52:34Z\r\n" +
        "Gemini 4 Argon\tGemini 4 Argon Weekly Limit\t100%\t2026-11-26T21:31:07Z\r\n";

    [Fact]
    public void Parse_RemainingConvertedToUsed()
    {
        var fixedNow = DateTimeOffset.Parse("2026-11-19T18:00:00Z");
        var snapshot = AgyParser.Parse(SampleOutput, fixedNow, "gemini", "Gemini");

        Assert.Equal(3, snapshot.Families.Count);

        // Gemini Models: 79% remaining -> 21% used; 83% remaining -> 17% used
        var gemini = snapshot.Families[0];
        Assert.Equal("Gemini Models", gemini.Name);

        var fiveHour = gemini.Windows[0]; // 5h ordered first
        Assert.Equal("5h", fiveHour.Name);
        Assert.Equal(17.0, fiveHour.UsedPercent); // 100 - 83 = 17

        var week = gemini.Windows[1];
        Assert.Equal("Week", week.Name);
        Assert.Equal(21.0, week.UsedPercent); // 100 - 79 = 21

        // Claude and GPT: 0% remaining -> 100% used; 47% remaining -> 53% used
        var claudeGpt = snapshot.Families[1];
        Assert.Equal("5h", claudeGpt.Windows[0].Name);
        Assert.Equal(100.0, claudeGpt.Windows[0].UsedPercent); // 100 - 0 = 100
        Assert.Equal("Week", claudeGpt.Windows[1].Name);
        Assert.Equal(53.0, claudeGpt.Windows[1].UsedPercent); // 100 - 47 = 53

        // Gemini 4 Argon: 100% remaining -> 0% used
        var argon = snapshot.Families[2];
        Assert.Equal(0.0, argon.Windows[0].UsedPercent); // 100 - 100 = 0
    }

    [Fact]
    public void Parse_Ordering_ShortestFirst_5hThenWeekThenOthers()
    {
        var fixedNow = DateTimeOffset.Parse("2026-11-19T18:00:00Z");
        string tsv =
            "Custom Family\tOther Limit\t50%\t2026-11-25T12:00:00Z\r\n" +
            "Custom Family\tWeekly Limit Remaining\t70%\t2026-11-24T12:00:00Z\r\n" +
            "Custom Family\tFive Hour Limit Remaining\t90%\t2026-11-20T12:00:00Z\r\n";

        var snapshot = AgyParser.Parse(tsv, fixedNow, "gemini", "Gemini");

        Assert.Single(snapshot.Families);
        var windows = snapshot.Families[0].Windows;
        Assert.Equal(3, windows.Count);

        Assert.Equal("5h", windows[0].Name);
        Assert.Equal("Week", windows[1].Name);
        Assert.Equal("Other Limit", windows[2].Name);
    }

    [Fact]
    public void Parse_WeekOnly_CorrectlyParsed()
    {
        var fixedNow = DateTimeOffset.Parse("2026-11-19T18:00:00Z");
        string weekOnlyInput =
            "Gemini Models\tWeekly Limit Remaining\t65%\t2026-11-25T12:00:00Z\r\n" +
            "Gemini 4 Argon\tGemini 4 Argon Weekly Limit\t90%\t2026-11-26T15:00:00Z\r\n";

        var snapshot = AgyParser.Parse(weekOnlyInput, fixedNow, "g2", "G2");

        Assert.Equal(2, snapshot.Families.Count);

        var fam1 = snapshot.Families[0];
        Assert.Single(fam1.Windows);
        Assert.Equal("Week", fam1.Windows[0].Name);
        Assert.Equal(35.0, fam1.Windows[0].UsedPercent); // 100 - 65

        var fam2 = snapshot.Families[1];
        Assert.Single(fam2.Windows);
        Assert.Equal("Week", fam2.Windows[0].Name);
        Assert.Equal(10.0, fam2.Windows[0].UsedPercent); // 100 - 90
    }
}
