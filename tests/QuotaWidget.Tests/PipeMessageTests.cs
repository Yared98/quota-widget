using QuotaWidget.Ipc;

namespace QuotaWidget.Tests;

public class PipeMessageTests
{
    [Fact]
    public void ParseRequest_ValidPing_Success()
    {
        string json = "{\"cmd\":\"ping\"}";
        var (req, error) = PipeMessageParser.ParseRequest(json);

        Assert.Null(error);
        Assert.NotNull(req);
        Assert.Equal("ping", req.Cmd);
    }

    [Fact]
    public void ParseRequest_ValidRefresh_WithAllFields()
    {
        string json = "{\"cmd\":\"refresh\",\"ids\":[\"g2\",\"claude\"],\"wait\":true,\"force\":true}";
        var (req, error) = PipeMessageParser.ParseRequest(json);

        Assert.Null(error);
        Assert.NotNull(req);
        Assert.Equal("refresh", req.Cmd);
        Assert.Equal(new[] { "g2", "claude" }, req.Ids);
        Assert.True(req.Wait);
        Assert.True(req.Force);
    }

    [Fact]
    public void ParseRequest_UnknownCommand_ReturnsError()
    {
        string json = "{\"cmd\":\"destroy\"}";
        var (req, error) = PipeMessageParser.ParseRequest(json);

        Assert.Null(req);
        Assert.NotNull(error);
        Assert.Contains("Unknown command", error);
    }

    [Fact]
    public void ParseRequest_MissingCmd_ReturnsError()
    {
        string json = "{\"ids\":[\"g2\"]}";
        var (req, error) = PipeMessageParser.ParseRequest(json);

        Assert.Null(req);
        Assert.NotNull(error);
        Assert.Contains("Missing 'cmd'", error);
    }

    [Fact]
    public void ParseRequest_Exceeds4KbLimit_ReturnsError()
    {
        byte[] oversizedBytes = new byte[PipeProtocol.MaxMessageBytes + 1];
        Array.Fill(oversizedBytes, (byte)' ');

        var (req, error) = PipeMessageParser.ParseRequest(oversizedBytes);

        Assert.Null(req);
        Assert.NotNull(error);
        Assert.Contains("exceeds 4096 bytes limit", error);
    }

    [Fact]
    public void ParseRequest_InvalidJson_ReturnsError()
    {
        string broken = "{\"cmd\": \"refresh\"";
        var (req, error) = PipeMessageParser.ParseRequest(broken);

        Assert.Null(req);
        Assert.NotNull(error);
        Assert.Contains("Invalid JSON", error);
    }

    [Fact]
    public void ParseRequest_Empty_ReturnsError()
    {
        var (req, error) = PipeMessageParser.ParseRequest("");

        Assert.Null(req);
        Assert.NotNull(error);
        Assert.Equal("Empty request", error);
    }
}
