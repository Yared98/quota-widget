using System.IO;
using System.IO.Pipes;
using System.Text;

namespace QuotaWidget.Ipc;

public class PipeServerHost : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<PipeRequest, Task<PipeResponse>> _requestHandler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;

    public PipeServerHost(Func<PipeRequest, Task<PipeResponse>> requestHandler, string? pipeName = null)
    {
        _requestHandler = requestHandler;
        _pipeName = pipeName ?? PipeProtocol.GetPipeName();
    }

    public void Start()
    {
        _listenerTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var serverStream = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await serverStream.WaitForConnectionAsync(_cts.Token);

                // Handle client in background task
                _ = Task.Run(() => HandleClientAsync(serverStream, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Delay briefly before retrying to avoid hot loop on error
                try
                {
                    await Task.Delay(100, _cts.Token);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken token)
    {
        using (stream)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                byte[] buffer = new byte[512];
                bool lineFound = false;

                while (!lineFound && memoryStream.Length <= PipeProtocol.MaxMessageBytes + 1)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead == 0)
                        break;

                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (buffer[i] == (byte)'\n')
                        {
                            memoryStream.Write(buffer, 0, i);
                            lineFound = true;
                            break;
                        }
                    }

                    if (!lineFound)
                    {
                        memoryStream.Write(buffer, 0, bytesRead);
                    }
                }

                byte[] requestBytes = memoryStream.ToArray();
                var (request, error) = PipeMessageParser.ParseRequest(requestBytes);

                PipeResponse response;
                if (error != null)
                {
                    response = PipeMessageParser.CreateErrorResponse(error);
                }
                else
                {
                    response = await _requestHandler(request!);
                }

                string responseJson = PipeMessageParser.SerializeResponse(response) + "\n";
                byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);

                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                await stream.FlushAsync(token);
            }
            catch
            {
                // Client connection aborted
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
