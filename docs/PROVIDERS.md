# Quota Providers Guide

QuotaWidget supports custom data sources via `command` and `file` providers, as well as native C# providers.

## Generic Quota JSON Schema (v1)

```json
{
  "version": 1,
  "families": [
    {
      "name": "Pro plan",
      "windows": [
        {
          "name": "5h",
          "used_percent": 42.0,
          "resets_at": "2026-11-20T01:45:49Z",
          "estimated": false
        },
        {
          "name": "Week",
          "used_percent": 61.5,
          "resets_at": null,
          "estimated": true
        }
      ]
    }
  ],
  "error": null
}
```

### Schema Rules

- `used_percent` (number, required): Quota percentage consumed (0.0 to 100.0). Clamped automatically.
- `resets_at` (string or null, optional): ISO-8601 UTC timestamp. If absent or null, reset is treated as unknown.
- `estimated` (boolean, optional): Set to `true` when the usage or cost is estimated.
- `error` (string or null, optional): Non-fatal warning or error message shown in the hover panel.
- Invalid JSON output results in a provider error displaying the first 120 characters of the output.

## Command Provider

Executes any executable or script and parses stdout as Generic Quota JSON.

```json
{
  "id": "custom",
  "type": "command",
  "label": "Mine",
  "enabled": true,
  "command": "powershell",
  "args": ["-NoProfile", "-File", "examples/providers/sample.ps1"],
  "timeoutSeconds": 90
}
```

- When `command` contains no directory separator, it is resolved via `PATH` and `PATHEXT`.
- Environment variables in command strings are expanded.
- Process runs hidden and times out after `timeoutSeconds` (default 90).

## File Provider

Reads a Generic Quota JSON file from disk.

```json
{
  "id": "local-file",
  "type": "file",
  "label": "Cache",
  "enabled": true,
  "path": "%LOCALAPPDATA%\\Quota\\status.json",
  "maxAgeMinutes": 60
}
```

- Environment variables in `path` are expanded.
- `ReadAt` timestamp is taken from the file's LastWriteTime.
- If older than `maxAgeMinutes` (default 60), the data is treated as stale.

## Adding a Built-in Provider in C#

1. Create a class implementing `QuotaWidget.Providers.IQuotaProvider`:
   ```csharp
   Task<QuotaSnapshot> ReadAsync(ProviderConfig cfg, DateTimeOffset now, CancellationToken ct);
   ```
2. Use `ProcessRunner` and `PathResolver` to run external commands. Keep parsers separate and pure.
3. Register the provider in `ProviderRegistry`:
   ```csharp
   Register("my-provider", () => new MyProvider());
   ```
4. Add unit tests for the parser under `tests/QuotaWidget.Tests/`.
