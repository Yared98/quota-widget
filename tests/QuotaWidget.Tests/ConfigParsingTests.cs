using QuotaWidget.Config;
using Xunit;

namespace QuotaWidget.Tests;

public class ConfigParsingTests
{
    [Fact]
    public void Parse_CommentsAndTrailingCommas_Accepted()
    {
        string jsonWithCommentsAndCommas = """
        {
          // Refresh interval in minutes
          "refreshMinutes": 15,
          /* UI Language */
          "language": "en",
          "offsetX": 12,
          "providers": [
            {
              "id": "claude",
              "type": "claude-code",
              "label": "Claude",
              "enabled": true,
            },
          ],
        }
        """;

        var config = ConfigStore.Parse(jsonWithCommentsAndCommas);

        Assert.Equal(15, config.RefreshMinutes);
        Assert.Equal("en", config.Language);
        Assert.Equal(12, config.OffsetX);
        Assert.Single(config.Providers);

        var p = config.Providers[0];
        Assert.Equal("claude", p.Id);
        Assert.Equal("claude-code", p.Type);
        Assert.Equal("Claude", p.Label);
        Assert.True(p.Enabled);
        Assert.Null(p.ValidationError);
    }

    [Fact]
    public void Parse_UnknownProviderType_FlagsValidationError()
    {
        string json = """
        {
          "providers": [
            { "id": "valid1", "type": "claude-code", "label": "Claude" },
            { "id": "invalid1", "type": "unknown-future-ai", "label": "Alien" }
          ]
        }
        """;

        var config = ConfigStore.Parse(json);

        Assert.Equal(2, config.Providers.Count);
        Assert.Null(config.Providers[0].ValidationError);
        Assert.NotNull(config.Providers[1].ValidationError);
        Assert.Contains("Unknown provider type: unknown-future-ai", config.Providers[1].ValidationError);
    }

    [Fact]
    public void Parse_DuplicateId_FlagsValidationErrorOnDuplicate()
    {
        string json = """
        {
          "providers": [
            { "id": "claude", "type": "claude-code", "label": "Claude 1" },
            { "id": "claude", "type": "claude-code", "label": "Claude 2" }
          ]
        }
        """;

        var config = ConfigStore.Parse(json);

        Assert.Equal(2, config.Providers.Count);
        Assert.Null(config.Providers[0].ValidationError);
        Assert.NotNull(config.Providers[1].ValidationError);
        Assert.Contains("Duplicate provider id: claude", config.Providers[1].ValidationError);
    }

    [Fact]
    public void Parse_MissingRequiredFields_FlagsValidationError()
    {
        string json = """
        {
          "providers": [
            { "type": "claude-code", "label": "No Id" },
            { "id": "p2", "label": "No Type" },
            { "id": "file1", "type": "file", "label": "Missing Path" },
            { "id": "cmd1", "type": "command", "label": "Missing Command" }
          ]
        }
        """;

        var config = ConfigStore.Parse(json);

        Assert.Equal(4, config.Providers.Count);
        Assert.Equal("Missing required field: id", config.Providers[0].ValidationError);
        Assert.Equal("Missing required field: type", config.Providers[1].ValidationError);
        Assert.Equal("Missing required field: path", config.Providers[2].ValidationError);
        Assert.Equal("Missing required field: command", config.Providers[3].ValidationError);
    }
}
