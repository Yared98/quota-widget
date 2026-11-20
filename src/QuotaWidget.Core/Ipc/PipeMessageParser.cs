using System.Text;
using System.Text.Json;

namespace QuotaWidget.Ipc;

public static class PipeMessageParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static (PipeRequest? Request, string? Error) ParseRequest(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return (null, "Empty request");

        if (bytes.Length > PipeProtocol.MaxMessageBytes)
            return (null, $"Message exceeds {PipeProtocol.MaxMessageBytes} bytes limit");

        string text;
        try
        {
            text = Encoding.UTF8.GetString(bytes).Trim();
        }
        catch (Exception ex)
        {
            return (null, $"Encoding error: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(text))
            return (null, "Empty request");

        try
        {
            var req = JsonSerializer.Deserialize<PipeRequest>(text, JsonOptions);
            if (req == null || string.IsNullOrWhiteSpace(req.Cmd))
                return (null, "Missing 'cmd' field");

            if (!req.Cmd.Equals("refresh", StringComparison.OrdinalIgnoreCase) &&
                !req.Cmd.Equals("ping", StringComparison.OrdinalIgnoreCase))
            {
                return (null, $"Unknown command: '{req.Cmd}'");
            }

            return (req, null);
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid JSON: {ex.Message}");
        }
    }

    public static (PipeRequest? Request, string? Error) ParseRequest(string text)
    {
        if (text == null)
            return (null, "Empty request");

        var bytes = Encoding.UTF8.GetBytes(text);
        return ParseRequest(bytes);
    }

    public static string SerializeResponse(PipeResponse response)
    {
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    public static PipeResponse CreateErrorResponse(string error)
    {
        return new PipeResponse
        {
            Ok = false,
            Error = error
        };
    }
}
