using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using QuotaWidget.Config;
using QuotaWidget.Model;

namespace QuotaWidget.Providers.Antigravity;

public class AntigravityProvider : IQuotaProvider
{
    public async Task<QuotaSnapshot> ReadAsync(ProviderConfig cfg, DateTimeOffset now, CancellationToken ct)
    {
        string command = cfg.Command ?? "agy";
        string agyPath = PathResolver.Resolve(command);

        if (!string.IsNullOrEmpty(cfg.RunAsUser))
        {
            return await ReadRunAsUserAsync(cfg, agyPath, now, ct);
        }

        return await ReadDirectAsync(cfg, agyPath, now, ct);
    }

    private static async Task<QuotaSnapshot> ReadDirectAsync(ProviderConfig cfg, string agyPath, DateTimeOffset now, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds > 0 ? cfg.TimeoutSeconds.Value : 90);

        try
        {
            var result = await ProcessRunner.RunAsync(
                agyPath,
                new[] { "-p", "/usage" },
                timeout: timeout,
                cancellationToken: ct);

            if (!result.Success && string.IsNullOrEmpty(result.Stdout))
            {
                string err = !string.IsNullOrEmpty(result.Stderr) ? result.Stderr : (result.TimedOut ? $"Timeout ({timeout.TotalSeconds:F0}s)" : $"Exit code {result.ExitCode}");
                return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"agy: {err}");
            }

            var snapshot = AgyParser.Parse(result.Stdout, now, cfg.Id, cfg.Label);
            if (snapshot.Families.Count == 0)
            {
                string first = (result.Stdout ?? string.Empty).Trim().Split('\n')[0].Trim();
                if (first.Length > 120) first = first[..120];
                return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"agy: no quotas found ({first})");
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"agy: {ex.Message}");
        }
    }

    private static async Task<QuotaSnapshot> ReadRunAsUserAsync(ProviderConfig cfg, string agyPath, DateTimeOffset now, CancellationToken ct)
    {
        string user = cfg.RunAsUser!;
        string exchangeDir = Environment.ExpandEnvironmentVariables(
            !string.IsNullOrWhiteSpace(cfg.ExchangeDir) ? cfg.ExchangeDir : Path.GetTempPath());

        // These values are pasted into a runas command line and a .cmd file: refuse anything that
        // could break out of the quoting.
        const string unsafeChars = "\"&|<>^%!\r\n";
        if (user.IndexOfAny(unsafeChars.ToCharArray()) >= 0 || user.Contains(' ') ||
            agyPath.IndexOfAny(unsafeChars.ToCharArray()) >= 0 ||
            exchangeDir.IndexOfAny(unsafeChars.ToCharArray()) >= 0 ||
            cfg.Id.IndexOfAny(unsafeChars.ToCharArray()) >= 0 || cfg.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(),
                "runAsUser, command, exchangeDir and id must not contain quotes, spaces in the user name or \" & | < > ^ % !");
        }

        try
        {
            if (!Directory.Exists(exchangeDir))
            {
                Directory.CreateDirectory(exchangeDir);
            }

            string cmdPath = Path.Combine(exchangeDir, $"{cfg.Id}-usage.cmd");
            string tmpPath = Path.Combine(exchangeDir, $"{cfg.Id}-usage.tmp");
            string txtPath = Path.Combine(exchangeDir, $"{cfg.Id}-usage.txt");

            string cmdContent = $"@echo off\r\n\"{agyPath}\" -p /usage > \"{tmpPath}\" 2>&1\r\nmove /y \"{tmpPath}\" \"{txtPath}\" >nul\r\n";

            Encoding oemEncoding = Encoding.ASCII;
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                oemEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            }
            catch
            {
                oemEncoding = Encoding.ASCII;
            }

            await System.IO.File.WriteAllTextAsync(cmdPath, cmdContent, oemEncoding, ct);

            DateTime startUtc = DateTime.UtcNow;

            var psi = new ProcessStartInfo
            {
                FileName = "runas.exe",
                Arguments = $"/savecred /user:{user} \"conhost.exe --headless cmd /c \\\"{cmdPath}\\\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), "Failed to start runas.exe");
            }

            int timeoutSec = cfg.TimeoutSeconds > 0 ? cfg.TimeoutSeconds.Value : 90;
            var timeoutAt = DateTime.UtcNow.AddSeconds(timeoutSec);
            bool updated = false;

            while (DateTime.UtcNow < timeoutAt && !ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);

                if (System.IO.File.Exists(txtPath))
                {
                    var lastWrite = System.IO.File.GetLastWriteTimeUtc(txtPath);
                    if (lastWrite >= startUtc)
                    {
                        updated = true;
                        break;
                    }
                }
            }

            if (!updated)
            {
                return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"Timeout waiting for {cfg.Id}-usage.txt ({timeoutSec}s)");
            }

            string txtContent = string.Empty;
            for (int r = 0; r < 5; r++)
            {
                try
                {
                    txtContent = await System.IO.File.ReadAllTextAsync(txtPath, ct);
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(300, ct);
                }
            }

            var snapshot = AgyParser.Parse(txtContent, now, cfg.Id, cfg.Label);
            if (snapshot.Families.Count == 0)
            {
                string first = (txtContent ?? string.Empty).Trim().Split('\n')[0].Trim();
                if (first.Length > 120) first = first[..120];
                return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"agy: no quotas found ({first})");
            }

            return snapshot;
        }
        catch (OperationCanceledException)
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), "Operation canceled");
        }
        catch (Exception ex)
        {
            return new QuotaSnapshot(cfg.Id, cfg.Label, now, Array.Empty<QuotaFamily>(), $"agy runas: {ex.Message}");
        }
    }
}
