# QuotaWidget design

QuotaWidget is a small Windows 11 app (.NET 8, WPF) that shows how much of your AI plan quotas you
have **used**, in the bottom-left corner of the taskbar. Each data source is a **provider**. A few
providers are built in; anyone can add their own without touching C# through the `command` or `file`
providers, or contribute a new built-in provider.

## Repo layout
```
QuotaWidget.sln
src/QuotaWidget/                  WPF app
  Model/                          QuotaSnapshot, QuotaFamily, QuotaWindow, colors, formatting
  Providers/                      IQuotaProvider, ProviderRegistry, one folder/file per built-in type
  Config/                         WidgetConfig (+ ProviderConfig), ConfigStore
  Host/                           TaskbarHost (overlay placement), theme, native calls
  Localization/                   Strings (en, pt-BR)
  config.example.json             copied to output; seed for the user config
tests/QuotaWidget.Tests/          xUnit, pure logic only (no processes, no UI)
examples/providers/               sample scripts for the `command` provider
docs/DESIGN.md (this), docs/PROVIDERS.md (how to add a quota source)
README.md, CONTRIBUTING.md, LICENSE (MIT), .github/workflows/ci.yml
```

## Data model
- `QuotaWindow`: `Name` (display, e.g. "5h", "Week", "Month"), `UsedPercent` (0..100, double, clamp),
  `ResetsAt` (DateTimeOffset?, null = unknown), `IsEstimated` (bool).
- `QuotaFamily`: `Name`, `Windows` (ordered as the provider returns them).
- `QuotaSnapshot`: `ProviderId`, `Label`, `ReadAt`, `Families`, `Error` (string?).
- **Bar selection** for a provider: main family = the family whose name equals the provider's
  `mainFamily` config (case-insensitive), else the first family. Thick bar + number = first window of
  the main family; thin bar = second window of the main family (absent if there is only one).
  Providers return windows shortest first.
- Colors by used %: < 50 green `#5DCAA5`, 50–80 amber `#EF9F27`, > 80 red `#E24B4A` (boundaries:
  50 → amber, 80 → amber, 80.01 → red).

## Generic quota JSON (schema version 1)
Used by the `command` and `file` providers, and documented in docs/PROVIDERS.md:
```json
{
  "version": 1,
  "families": [
    { "name": "Pro plan",
      "windows": [
        { "name": "5h",   "used_percent": 42,   "resets_at": "2026-11-20T01:45:49Z" },
        { "name": "Week", "used_percent": 61.5, "resets_at": null, "estimated": true }
      ] }
  ],
  "error": null
}
```
`used_percent` required; `resets_at` ISO-8601 or null/absent; `estimated` optional; `error` optional
string (shown in the panel; families may still be present). Unknown fields ignored. Invalid JSON →
provider error with the first 120 chars of the output.

## Providers
Interface: `Task<QuotaSnapshot> ReadAsync(ProviderConfig cfg, DateTimeOffset now, CancellationToken ct)`.
`ProviderRegistry` maps `type` string → provider. Process-running code lives in a small
`ProcessRunner` (hidden window, stdout/stderr capture, timeout, kill tree) so parsers stay pure and testable.
Executables are resolved through PATH + PATHEXT when the configured command has no directory
(so `"claude"` finds `claude.cmd`). Environment variables in paths are expanded.

| type | what it does | specific config |
|---|---|---|
| `claude-code` | runs `claude -p ok --model haiku --output-format stream-json --verbose --max-turns 1 --system-prompt "Reply ok." --tools "" --strict-mcp-config --setting-sources "" --no-session-persistence` in the temp folder and reads the `rate_limit_event` line (`rate_limit_info.unifiedWindows.five_hour/seven_day`: `utilization` 0..1, `resetsAt` unix s). Windows "5h", "Week". Costs ~1k Haiku tokens per read; documented in README. | `command` (default `claude`) |
| `antigravity` | runs `agy -p /usage` and parses TSV lines `family \t label \t NN% \t ISO reset` (NN is **remaining** → used = 100 − NN). Label containing "Five Hour" → "5h", "Weekly" → "Week", other → label text. Windows sorted 5h, Week, others. | `command` (default `agy`), `mainFamily` (default "Gemini Models"), optional `runAsUser` + `exchangeDir` (see below) |
| `opencode` | runs `opencode stats --days N --models --cost --json` where N = days since the current period start (0 = today). Sums `cost` of `models[]` whose `providerID/id` matches each budget's `models` patterns (`*` wildcard). One family per budget, one window "Month" with used % = cost / usd × 100, `ResetsAt` = next period start, `IsEstimated` = true (cost is OpenCode's estimate). | `command` (default `opencode`), `resetDay` (1..28, default 1), `budgets`: `[{ "name", "models": ["opencode-go/claude-haiku-5-5"], "usd": 15 }]` |
| `command` | runs any command; stdout must be the generic JSON. | `command`, `args` (array), `timeoutSeconds` (default 90) |
| `file` | reads a generic JSON file written by anything (a script, a status line, a cron job). `ReadAt` = file LastWriteTime; older than `maxAgeMinutes` (default 60) → stale. | `path`, `maxAgeMinutes` |

`antigravity` with `runAsUser` (second Google account kept under another local Windows user): writes
`<exchangeDir>\<id>-usage.cmd`, starts `runas.exe /savecred /user:<user> "conhost.exe --headless cmd /c \"<cmd>\""`
hidden, waits (1 s poll, timeout) for `<exchangeDir>\<id>-usage.txt` newer than the start, parses it.
The cmd writes to a .tmp and `move /y`s it. `exchangeDir` must be writable by both users. README
explains the one-time `runas /savecred` setup and that it stores the credential in Windows.

Failures: a provider that throws or returns an error keeps the last good families, with the error shown.
Providers run in parallel, off the UI thread, every `refreshMinutes` and on "Refresh now".
Last good snapshots persist to `%LOCALAPPDATA%\QuotaWidget\snapshot.json` (keyed by provider id).

## Config
User config: `%APPDATA%\QuotaWidget\config.json`. If missing at startup, copy `config.example.json`
there. Menu item "Open config" opens it in the default editor; "Reload config" re-reads it and rebuilds
the bar without restarting.
```json
{
  "refreshMinutes": 10,
  "language": "auto",
  "offsetX": 8,
  "providers": [
    { "id": "claude", "type": "claude-code", "label": "Claude", "enabled": true },
    { "id": "gemini", "type": "antigravity", "label": "Gemini", "enabled": false },
    { "id": "opencode", "type": "opencode", "label": "OC", "enabled": false,
      "budgets": [ { "name": "Go", "models": ["opencode-go/*"], "usd": 15 } ] },
    { "id": "custom", "type": "command", "label": "Mine", "enabled": false,
      "command": "powershell", "args": ["-NoProfile", "-File", "examples/providers/sample.ps1"] }
  ]
}
```
Validation: unknown type / duplicate id / missing required field → that provider shows an error block;
others still work. JSON with comments and trailing commas accepted.
Bar shows every enabled provider in order (each block 64 px; window width follows the count).
`hideOnBar: true` keeps a provider only in the panel.

## UI
Bar block: label left, used % right (rounded integer), thick bar (6 px) and thin bar (3 px) filled by
used %. Stale (ReadAt older than 2 × refresh, or estimated main window) → number dimmed.
Hover panel: title "AI quotas · % used" (localized), "updated HH:mm"; per provider: label, "read X ago",
error line if any; per family (family name shown only if the provider has more than one family):
rows `name | bar | NN% | resets HH:mm` (or `dd/MM HH:mm` when not today; "estimated" when estimated
and no reset). Panel closes when the cursor has been outside widget and panel for ~300 ms (poll).
Right-click menu: Refresh now, Open config, Reload config, Exit.
Theme: text white on dark taskbar, near-black on light (SystemUsesLightTheme).

## Placement
Overlay only: tool window, no taskbar button, no activation, topmost, placed over the left end of the
primary taskbar (`Shell_TrayWnd` rect + offsetX, vertically centered), re-positioned every 1 s; hidden
while the foreground window covers its whole monitor (except desktop/shell classes). Single instance
(named mutex). (Embedding as a child of `Shell_TrayWnd` was tried: on current Windows 11 the
taskbar's XAML layer draws over child windows, so it is not used.)

## Localization
`Strings` with `en` and `pt-BR`; `language: "auto"` uses the UI culture (pt* → pt-BR, else en).
All user-visible text goes through it. Date formats: pt-BR `dd/MM HH:mm`, en `MM/dd HH:mm`.
