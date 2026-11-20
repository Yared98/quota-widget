using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace QuotaWidget.Ipc;

/// <summary>Thrown when no widget answers on the pipe (not running, or another user's session).</summary>
public class WidgetNotRunningException : Exception
{
    public WidgetNotRunningException(string message, Exception? inner = null) : base(message, inner) { }
}

public static class PipeClient
{
    /// <param name="connectTimeoutMs">How long to wait for the widget to accept the connection.
    /// Failing here means "widget not running" (<see cref="WidgetNotRunningException"/>).</param>
    /// <param name="responseTimeoutMs">How long to wait for the reply once connected. Failing here is a
    /// real error (the widget is running), never a reason to read the providers a second time.</param>
    public static async Task<PipeResponse> SendRequestAsync(
        PipeRequest request,
        string? pipeName = null,
        int connectTimeoutMs = 500,
        int responseTimeoutMs = 2000,
        CancellationToken cancellationToken = default)
    {
        string name = pipeName ?? PipeProtocol.GetPipeName();
        // CurrentUserOnly also makes the client check that the server belongs to the current user.
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(connectTimeoutMs);
            await client.ConnectAsync(connectCts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            throw new WidgetNotRunningException("The widget is not running.", ex);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(responseTimeoutMs);

        byte[] reqBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request) + "\n");
        await client.WriteAsync(reqBytes, cts.Token);
        await client.FlushAsync(cts.Token);

        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        string? line = await reader.ReadLineAsync(cts.Token);
        if (string.IsNullOrWhiteSpace(line))
            return new PipeResponse { Ok = false, Error = "Empty response from widget" };

        try
        {
            var res = JsonSerializer.Deserialize<PipeResponse>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return res ?? new PipeResponse { Ok = false, Error = "Failed to deserialize response" };
        }
        catch (JsonException ex)
        {
            return new PipeResponse { Ok = false, Error = $"Invalid response JSON: {ex.Message}" };
        }
    }
}
