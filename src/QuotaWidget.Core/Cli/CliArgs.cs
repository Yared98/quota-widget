using System.Globalization;

namespace QuotaWidget.Cli;

public enum CliCommand
{
    Status,
    Json,
    Refresh,
    Pick,
    Help,
    Unknown
}

public class CliOptions
{
    public CliCommand Command { get; set; } = CliCommand.Help;
    public List<string> Ids { get; set; } = new();
    public bool Wait { get; set; }
    public bool Force { get; set; }
    /// <summary>Internal: read providers in this process, never via the widget (used by the detached refresh).</summary>
    public bool Standalone { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? Worker { get; set; }
    public double MaxPercent { get; set; } = 95.0;
    public bool Json { get; set; }
    public string? Error { get; set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        if (args == null || args.Length == 0)
        {
            options.Command = CliCommand.Help;
            return options;
        }

        string cmd = args[0].ToLowerInvariant();

        switch (cmd)
        {
            case "-h":
            case "--help":
            case "help":
                options.Command = CliCommand.Help;
                return options;

            case "status":
                options.Command = CliCommand.Status;
                return options;

            case "json":
                options.Command = CliCommand.Json;
                return options;

            case "refresh":
                options.Command = CliCommand.Refresh;
                for (int i = 1; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg.Equals("--wait", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Wait = true;
                    }
                    else if (arg.Equals("--force", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Force = true;
                    }
                    else if (arg.Equals("--standalone", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Standalone = true;
                    }
                    else if (arg.Equals("--timeout", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int t))
                        {
                            options.TimeoutSeconds = t;
                            i++;
                        }
                        else
                        {
                            options.Error = "Missing or invalid integer value for --timeout";
                            return options;
                        }
                    }
                    else if (arg.StartsWith("--timeout=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(arg["--timeout=".Length..], out int t))
                        {
                            options.TimeoutSeconds = t;
                        }
                        else
                        {
                            options.Error = "Invalid integer value for --timeout";
                            return options;
                        }
                    }
                    else if (arg.StartsWith("-"))
                    {
                        options.Error = $"Unknown flag for refresh: {arg}";
                        return options;
                    }
                    else
                    {
                        options.Ids.Add(arg);
                    }
                }
                return options;

            case "pick":
                options.Command = CliCommand.Pick;
                for (int i = 1; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Json = true;
                    }
                    else if (arg.Equals("--max", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 < args.Length && double.TryParse(args[i + 1], CultureInfo.InvariantCulture, out double m))
                        {
                            options.MaxPercent = m;
                            i++;
                        }
                        else
                        {
                            options.Error = "Missing or invalid value for --max";
                            return options;
                        }
                    }
                    else if (arg.StartsWith("--max=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (double.TryParse(arg["--max=".Length..], CultureInfo.InvariantCulture, out double m))
                        {
                            options.MaxPercent = m;
                        }
                        else
                        {
                            options.Error = "Invalid value for --max";
                            return options;
                        }
                    }
                    else if (arg.StartsWith("-"))
                    {
                        options.Error = $"Unknown flag for pick: {arg}";
                        return options;
                    }
                    else
                    {
                        if (options.Worker == null)
                        {
                            options.Worker = arg;
                        }
                        else
                        {
                            options.Error = $"Unexpected argument: {arg}";
                            return options;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(options.Worker))
                {
                    options.Error = "Missing required <worker> argument for pick";
                }

                return options;

            default:
                options.Command = CliCommand.Unknown;
                options.Error = $"Unknown command: {args[0]}";
                return options;
        }
    }
}
