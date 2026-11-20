using QuotaWidget.Model;
using QuotaWidget.Providers;
using Xunit;

namespace QuotaWidget.Tests;

public class GenericQuotaParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_ValidJson_ReturnsFamiliesAndWindows()
    {
        string json = """
        {
          "version": 1,
          "families": [
            {
              "name": "Pro plan",
              "windows": [
                { "name": "5h", "used_percent": 42.0, "resets_at": "2026-11-20T14:45:00Z", "estimated": false },
                { "name": "Week", "used_percent": 61.5, "resets_at": null, "estimated": true }
              ]
            }
          ],
          "error": null
        }
        """;

        var snapshot = GenericQuotaParser.Parse(json, Now, "my-provider", "My Label");

        Assert.Equal("my-provider", snapshot.ProviderId);
        Assert.Equal("My Label", snapshot.Label);
        Assert.Equal(Now, snapshot.ReadAt);
        Assert.Null(snapshot.Error);
        Assert.False(snapshot.HasError);
        Assert.Single(snapshot.Families);

        var family = snapshot.Families[0];
        Assert.Equal("Pro plan", family.Name);
        Assert.Equal(2, family.Windows.Count);

        var w1 = family.Windows[0];
        Assert.Equal("5h", w1.Name);
        Assert.Equal(42.0, w1.UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T14:45:00Z"), w1.ResetsAt);
        Assert.False(w1.IsEstimated);

        var w2 = family.Windows[1];
        Assert.Equal("Week", w2.Name);
        Assert.Equal(61.5, w2.UsedPercent);
        Assert.Null(w2.ResetsAt);
        Assert.True(w2.IsEstimated);
    }

    [Fact]
    public void Parse_MissingFields_HandledGracefully()
    {
        // Window without resets_at and estimated; window missing required used_percent is skipped
        string json = """
        {
          "families": [
            {
              "name": "Default",
              "windows": [
                { "name": "Hour", "used_percent": 25.0 },
                { "name": "BrokenWindow" }
              ]
            }
          ]
        }
        """;

        var snapshot = GenericQuotaParser.Parse(json, Now, "p1", "Label1");

        Assert.Single(snapshot.Families);
        var family = snapshot.Families[0];
        Assert.Single(family.Windows);

        var w = family.Windows[0];
        Assert.Equal("Hour", w.Name);
        Assert.Equal(25.0, w.UsedPercent);
        Assert.Null(w.ResetsAt);
        Assert.False(w.IsEstimated);
        Assert.Null(snapshot.Error);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsErrorWithFirst120Chars()
    {
        string invalidOutput = "This is not valid JSON content at all. " + new string('X', 150);

        var snapshot = GenericQuotaParser.Parse(invalidOutput, Now, "p1", "Label1");

        Assert.NotNull(snapshot.Error);
        Assert.StartsWith("Invalid JSON:", snapshot.Error);
        // Error contains first 120 chars
        Assert.Contains(invalidOutput[..120], snapshot.Error);
        Assert.Empty(snapshot.Families);
    }

    [Fact]
    public void Parse_ErrorField_PopulatesSnapshotError()
    {
        string json = """
        {
          "version": 1,
          "families": [
            {
              "name": "Partial",
              "windows": [
                { "name": "5h", "used_percent": 90.0 }
              ]
            }
          ],
          "error": "Rate limit exceeded on upstream service"
        }
        """;

        var snapshot = GenericQuotaParser.Parse(json, Now, "p1", "Label1");

        Assert.Equal("Rate limit exceeded on upstream service", snapshot.Error);
        Assert.True(snapshot.HasError);
        Assert.Single(snapshot.Families);
        Assert.Equal(90.0, snapshot.Families[0].Windows[0].UsedPercent);
    }
}
