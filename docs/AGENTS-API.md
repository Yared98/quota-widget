# QuotaWidget for agents and orchestrators

QuotaWidget keeps every provider's quota in one machine-readable file and ships a small CLI, so an
agent that hands work to other agents/models can pick the one with room left, and harness hooks can
ask for a fresh read when a task ends.

## Projects
Split the code into three projects (one solution):
- `src/QuotaWidget.Core/` (class library, `net8.0-windows`, no WPF): Model, Providers, Config,
  Persistence, Export, Ipc, Scheduling, Localization. Everything that is not UI.
- `src/QuotaWidget/` (WPF app, `WinExe`): UI + TaskbarHost; references Core; hosts the pipe server.
- `src/QuotaWidget.Cli/` (console, `Exe`, AssemblyName `qw`): references Core.
- `tests/QuotaWidget.Tests/` references Core (move tests accordingly).
`dotnet publish` of the app must also place `qw.exe` in the same folder (publish both into one
output in CI; README shows `dotnet publish src/QuotaWidget -o out` + `dotnet publish src/QuotaWidget.Cli -o out`).

## quotas.json (export contract, version 1)
Path: `%LOCALAPPDATA%\QuotaWidget\quotas.json` (config `exportPath` overrides). Written atomically
(write `.tmp`, then `File.Replace`/move) after every refresh, and once at startup from the persisted
snapshot. Writers take the named mutex `Local\QuotaWidget.Export` while writing.
```json
{
  "version": 1,
  "generated_at": "2026-11-19T22:40:00-03:00",
  "providers": [
    {
      "id": "g2", "type": "antigravity", "label": "G2",
      "read_at": "2026-11-19T22:38:10-03:00",
      "stale": false,
      "error": null,
      "workers": ["agy-alt:gemini-*", "agy-alt:claude-*"],
      "main": { "family": "Gemini Models", "window": "Week", "used_percent": 62, "resets_at": "..." },
      "families": [
        { "name": "Gemini Models",
          "windows": [ { "name": "Week", "used_percent": 62, "resets_at": "2026-11-26T21:25:26Z", "estimated": false } ] }
      ]
    }
  ]
}
```
- `stale`: same rule as the bar (read older than 2 × refreshMinutes, or main window estimated).
- `main`: the window the bar shows as the big number (null when no data).
- Only enabled providers; providers with a config error appear with `error` set and no families.
- Times are ISO-8601 with offset; `used_percent` rounded to 1 decimal.

## Provider config additions
- `workers` (string array, optional): free-form tags saying what this quota pays for, e.g. model ids
  (`claude-haiku-5-5`), CLI+model (`agy:gemini-3.8-flash-high`) or tool names. Copied to the export.
  Matching (`pick`) is case-insensitive with `*` wildcards on both sides allowed in the tag.
- `workerFamilies` (object, optional): tag pattern → family name, to say which family limits that worker,
  e.g. `{ "agy-alt:claude-*": "Claude and GPT models" }`. Without it, `pick` uses the main family.
- `minRefreshSeconds` (int, optional): minimum time between two reads of this provider triggered by
  refresh requests (timer refreshes ignore it). Default 120 for `claude-code` (each read costs tokens),
  30 for the others.

## Refresh requests (IPC)
The running app hosts a named pipe server `QuotaWidget.<current user name>` (PipeSecurity: current
user only; `PipeOptions.CurrentUserOnly`). Protocol: one UTF-8 JSON line per connection, max 4 KB:
`{"cmd":"refresh","ids":["g2"],"wait":false,"force":false}` → reply one JSON line
`{"ok":true,"queued":["g2"],"skipped":[{"id":"claude","reason":"min_interval"}]}`;
with `"wait":true` the reply is sent after the refresh finishes and also contains `"quotas": <export>`.
`{"cmd":"ping"}` → `{"ok":true,"version":"x.y.z"}`. Anything else → `{"ok":false,"error":"..."}`.
Empty `ids` = all enabled providers.

`RefreshScheduler` (pure, clock injected, unit tested): coalesces requests arriving within 2 s into one
run; drops ids refreshed less than `minRefreshSeconds` ago unless `force`; never runs the same provider
twice in parallel (a request during a run is queued for right after it).

## CLI: `qw`
```
qw json                      print quotas.json (if missing: read all providers once, then print)
qw refresh [ids...] [--wait] [--force] [--timeout <s>]
qw pick <worker> [--max <percent>] [--json]
qw status                    is the widget running? (ping)
```
- `refresh`: if the widget answers on the pipe, forward and return (exit 0 immediately unless --wait).
  If not running: load the config, read the requested providers itself (honouring minRefreshSeconds
  against `read_at` in quotas.json unless --force), merge into quotas.json + snapshot, exit.
  Must return within ~1 s when not waiting, so hooks never block a harness.
- `pick <worker>`: among providers whose `workers` match `<worker>` and have no error, choose the one
  with the lowest used % in the limiting family (from `workerFamilies`, else main family), considering
  every window of that family (a window ≥ `--max`, default 95, disqualifies). Prints the provider id
  (or with `--json`: `{"id","label","used_percent","window","resets_at"}`); exit 3 when none qualifies.
  Stale data is allowed but reported in `--json` (`"stale": true`).
- Exit codes: 0 ok, 1 error, 3 nothing to pick. Errors go to stderr. Output is stable for scripts.

## Hook examples (`examples/hooks/`)
- `claude-code-settings.json`: a `Stop` and a `SubagentStop` hook running
  `qw refresh claude` (with a comment: add other ids if subagents use other accounts).
- `opencode-plugin.js`: an OpenCode plugin that runs `qw refresh <id>` on `session.idle`.
- `worker-wrapper.ps1`: runs any worker command, then `qw refresh <id>`; plus how to pick first:
  `$id = qw pick "agy:gemini-*"`.
- `docs/AGENTS.md` (user-facing): explains quotas.json, the CLI, `pick`, `workers`, hooks, and a short
  "orchestrator recipe" (read → pick → delegate → refresh on finish).
