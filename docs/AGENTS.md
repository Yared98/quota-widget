# Agent & Orchestrator API

QuotaWidget provides a machine-readable export (`quotas.json`), a dedicated CLI tool (`qw`), and an IPC refresh protocol. These tools allow autonomous agents, subagent runners, and multi-model orchestrators to make intelligent routing decisions based on live capacity and trigger updates when tasks complete.

---

## 1. The Machine-Readable Export (`quotas.json`)

Whenever QuotaWidget refreshes any provider—or when the application first starts—it writes the current quota state to:

```
%LOCALAPPDATA%\QuotaWidget\quotas.json
```

You can customize this path by setting `"exportPath"` in `%APPDATA%\QuotaWidget\config.json`.

### Atomic Writing & Mutex Synchronization

To prevent torn reads by external tools or agents:
1. QuotaWidget acquires the named Windows mutex `Local\QuotaWidget.Export`.
2. It writes the serialized export to a temporary file (`quotas.json.tmp`).
3. It atomically replaces the target file via `File.Replace` (or atomic move).
4. External readers or writers can optionally acquire `Local\QuotaWidget.Export` when performing synchronized filesystem operations.

### JSON Schema (Version 1)

```json
{
  "version": 1,
  "generated_at": "2026-11-19T22:40:00-03:00",
  "providers": [
    {
      "id": "gemini",
      "type": "antigravity",
      "label": "Gemini",
      "read_at": "2026-11-19T22:38:10-03:00",
      "stale": false,
      "error": null,
      "workers": ["agy:gemini-*", "agy:claude-*"],
      "main": {
        "family": "Gemini Models",
        "window": "Week",
        "used_percent": 62.0,
        "resets_at": "2026-11-26T21:25:26-03:00"
      },
      "families": [
        {
          "name": "Gemini Models",
          "windows": [
            {
              "name": "Week",
              "used_percent": 62.0,
              "resets_at": "2026-11-26T21:25:26-03:00",
              "estimated": false
            }
          ]
        }
      ]
    }
  ]
}
```

### Key Field Semantics

- **`stale`**: Follows the taskbar display rules. A snapshot is considered stale if its `read_at` timestamp is older than `2 × refreshMinutes` or if the primary window is estimated.
- **`main`**: The primary quota window displayed as the primary percentage on the taskbar overlay. Set to `null` if no quota window is available.
- **`used_percent`**: Rounded to 1 decimal place.
- **`error`**: Contains the error message if the provider encountered a configuration or runtime error; `null` on success. Providers with configuration validation errors omit families.
- **Only enabled providers** appear in the export list.

---

## 2. Provider Configuration Additions

In `config.json`, each provider can be tagged with operational metadata for agent routing and throttling:

```json
{
  "id": "gemini-secondary",
  "type": "antigravity",
  "label": "Gemini Sec",
  "enabled": true,
  "workers": ["agy:gemini-*", "agy:claude-*"],
  "workerFamilies": {
    "agy:claude-*": "Claude and GPT models"
  },
  "minRefreshSeconds": 30
}
```

- **`workers`** *(array of strings, optional)*: Capability tags indicating which models or CLI tools this account pays for. Tags support case-insensitive wildcard matching (`*`) at the start, middle, or end (e.g., `agy:gemini-*`, `*haiku*`).
- **`workerFamilies`** *(map of string to string, optional)*: Maps worker tag patterns to specific quota family names within the provider. For instance, if an account provides both Gemini and Claude quotas, this points `agy:claude-*` workers to the `"Claude and GPT models"` family instead of the default main family.
- **`minRefreshSeconds`** *(integer, optional)*: Minimum interval required between consecutive reads of this provider triggered by refresh requests. Prevents spamming expensive CLI probes (e.g. Claude Code token costs). Defaults to `120` seconds for `claude-code` and `30` seconds for all other provider types. Regular timer refreshes ignore this limit.

---

## 3. The `qw` CLI

QuotaWidget includes a companion console executable, `qw.exe`.

### Commands

#### `qw json`
Outputs the current contents of `quotas.json` to standard output. If `quotas.json` does not exist on disk, the CLI loads your configuration, queries all enabled providers once, writes `quotas.json`, and outputs the result.

#### `qw refresh [ids...] [--wait] [--force] [--timeout <s>]`
Triggers an immediate refresh for specified providers (or all enabled providers if no `ids` are supplied).
- If the QuotaWidget app is currently running, the command forwards the request over the local named pipe IPC.
- If QuotaWidget is not running, the CLI executes the refresh directly in-process, respects `minRefreshSeconds` against cached timestamps (unless `--force`), and writes the updated export to `quotas.json`.
- **`--wait`**: Blocks until the refresh finishes. Without `--wait`, the command returns immediately (~under 1 second), ensuring harness and agent hooks never stall your tasks.
- **`--force`**: Bypasses `minRefreshSeconds` checks.
- **`--timeout <s>`**: Maximum wait time in seconds when `--wait` is specified (default: 30s).

#### `qw pick <worker> [--max <percent>] [--json]`
Selects the best provider matching `<worker>`.
1. Filters providers whose `workers` match `<worker>` and have no active errors.
2. Identifies the limiting family (via `workerFamilies` if matched, else the main family).
3. Evaluates all windows within that family: if any window has `used_percent >= --max` (default `95`), the provider is disqualified.
4. From all qualified candidates, selects the one with the lowest utilized percentage in the limiting window. Ties are broken in favor of the provider configured earliest in `config.json`.
5. Prints the provider ID (e.g. `gemini`), or with `--json`:
   ```json
   {
     "id": "gemini",
     "label": "Gemini",
     "used_percent": 42.5,
     "window": "5h",
     "resets_at": "2026-11-20T01:45:49-03:00",
     "stale": false
   }
   ```

#### `qw status`
Checks whether the QuotaWidget desktop application is currently running by pinging its named pipe.
- Outputs `running (vX.Y.Z)` and exits with code `0` if running.
- Exits with code `1` if the application is not running.

### Exit Codes
- `0`: Success.
- `1`: Error (details written to `stderr`).
- `3`: No qualifying provider found for `pick`.

---

## 4. Orchestrator Recipe

Multi-agent frameworks can implement a simple four-step pattern: **Read / Pick &rarr; Delegate &rarr; Refresh**.

```
   ┌────────────────────────────────────────┐
   │ 1. Pick Provider                       │
   │    $id = qw pick "agy:gemini" │
   └───────────────────┬────────────────────┘
                       │
                       ▼
   ┌────────────────────────────────────────┐
   │ 2. Delegate Subagent / Run Task        │
   │    agy -p "Perform complex analysis"   │
   └───────────────────┬────────────────────┘
                       │
                       ▼
   ┌────────────────────────────────────────┐
   │ 3. Hook / Refresh                      │
   │    qw refresh $id             │
   └────────────────────────────────────────┘
```

### PowerShell Example

```powershell
# Pick the least utilized provider for Gemini tasks
$provider = qw pick "agy:gemini-*"
if ($LASTEXITCODE -ne 0) {
    Write-Error "All Gemini providers are exhausted or unavailable!"
    exit 1
}

Write-Host "Routing task to provider: $provider"
try {
    # Run the worker workload
    agy -p "Summarize daily release logs"
}
finally {
    # Request non-blocking refresh so status updates immediately
    qw refresh $provider
}
```

---

## 5. Hook Examples

Ready-to-use hooks are provided in `examples/hooks/`:

- **`claude-code-settings.json`**: Configures Claude Code's `Stop` and `SubagentStop` lifecycle events to refresh Claude's quota upon turn completion.
- **`opencode-plugin.js`**: An OpenCode extension that triggers a non-blocking `qw refresh opencode` when a session goes idle.
- **`worker-wrapper.ps1`**: A general-purpose PowerShell script wrapping any CLI process with automated provider picking and post-execution refresh.
