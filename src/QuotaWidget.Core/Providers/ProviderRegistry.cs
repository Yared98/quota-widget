namespace QuotaWidget.Providers;

public static class ProviderRegistry
{
    private static readonly Dictionary<string, Func<IQuotaProvider>> _providers = new(StringComparer.OrdinalIgnoreCase);

    static ProviderRegistry()
    {
        Register("claude-code", () => new ClaudeCode.ClaudeCodeProvider());
        Register("antigravity", () => new Antigravity.AntigravityProvider());
        Register("opencode", () => new OpenCode.OpenCodeProvider());
        Register("command", () => new Command.CommandProvider());
        Register("file", () => new File.FileProvider());
    }

    public static void Register(string type, Func<IQuotaProvider> factory)
    {
        _providers[type] = factory;
    }

    public static bool IsKnownType(string type)
    {
        return !string.IsNullOrWhiteSpace(type) && _providers.ContainsKey(type);
    }

    public static IQuotaProvider? Create(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return null;

        if (_providers.TryGetValue(type, out var factory))
        {
            return factory();
        }

        return null;
    }
}
