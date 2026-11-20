using System.Diagnostics;

namespace QuotaWidget.Providers;

public record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut = false)
{
    public bool Success => !TimedOut && ExitCode == 0;
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string>? args = null,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDirectory ?? string.Empty
        };

        if (args != null)
        {
            foreach (var a in args)
                psi.ArgumentList.Add(a);
        }

        if (environment != null)
        {
            foreach (var kv in environment)
                psi.Environment[kv.Key] = kv.Value;
        }

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();

        var timeoutDuration = timeout ?? TimeSpan.FromSeconds(90);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeoutDuration);

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
            var waitTask = process.WaitForExitAsync(linkedCts.Token);

            await Task.WhenAll(stdoutTask, stderrTask, waitTask);

            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(-1, string.Empty, $"Timeout ({timeoutDuration.TotalSeconds:F0}s)", TimedOut: true);
        }
        catch (Exception ex)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(-1, string.Empty, ex.Message);
        }
    }
}
