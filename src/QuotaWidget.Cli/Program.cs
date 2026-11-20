namespace QuotaWidget.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await CliRunner.RunAsync(args);
    }
}
