# QuotaWidget

A lightweight, unobtrusive Windows 11 taskbar overlay (.NET 8 WPF) that displays your AI subscription and plan quota usage in real time. Placed neatly in the bottom-left corner of your taskbar, QuotaWidget gives you a glanceable bar showing consumed capacity, with detailed reset times and breakdown windows available on hover.

![QuotaWidget Preview](docs/screenshot.png)

---

## Features

- **Glanceable taskbar overlay**: Shows current percentage used and visual progress bars (thick bar for primary short window, thin bar for weekly or secondary window).
- **Color-coded thresholds**: Green (< 50%), amber (50%–80%), and red (> 80%).
- **Rich hover panel**: Hover over the widget to view exact reset timestamps, multi-model families, and detailed quota status.
- **Auto-theming**: Automatically detects light or dark Windows taskbar modes.
- **Provider-based architecture**: Built-in support for major AI CLI tools, plus extensible generic JSON support for scripts and files.

---

## Installation

### Option 1: Download Pre-built Release
1. Download the latest release `.zip` from the [Releases](../../releases) page.
2. Extract the archive into a folder of your choice (e.g., `C:\Tools\QuotaWidget` or `%LOCALAPPDATA%\Programs\QuotaWidget`).
3. Run `QuotaWidget.exe`.

### Option 2: Build from Source
Ensure you have the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed.

```bash
git clone https://github.com/username/quota-widget.git
cd quota-widget
dotnet publish src/QuotaWidget -o out
dotnet publish src/QuotaWidget.Cli -o out
```

Both programs land in the same folder: `QuotaWidget.exe` (the bar) and `qw.exe` (the command line for
scripts and agents, see [docs/AGENTS.md](docs/AGENTS.md)). Add that folder to your `PATH` so hooks can
call `qw`.

The compiled binaries (`QuotaWidget.exe`, `qw.exe`) and dependencies will be located in the `./out` directory.

---

## First Run & Configuration

On first launch, if no configuration is found, QuotaWidget automatically creates a default config file at:

```
%APPDATA%\QuotaWidget\config.json
```

It seeds this file from `config.example.json`.

### Managing QuotaWidget

Right-click the widget on your taskbar to open the context menu:
- **Refresh now**: Manually triggers an immediate poll of all enabled providers.
- **Open config**: Opens `%APPDATA%\QuotaWidget\config.json` in your default text editor.
- **Reload config**: Re-reads your configuration and rebuilds the taskbar widget immediately without restarting the application.
- **Exit**: Closes the application.

Snapshots and cached data are preserved across restarts in `%LOCALAPPDATA%\QuotaWidget\snapshot.json`.

### General Configuration Options

The root configuration supports:

| Property | Type | Default | Description |
|---|---|---|---|
| `refreshMinutes` | number | `10` | Interval in minutes between automatic provider quota updates. |
| `language` | string | `"auto"` | UI language: `"auto"` (detects system language; Portuguese falls back to pt-BR, otherwise English), `"en"`, or `"pt-BR"`. |
| `offsetX` | number | `8` | Horizontal pixel offset from the left edge of the taskbar. |
| `providers` | array | `[...]` | List of provider configuration objects. |

Comments (`//`) and trailing commas are permitted in `config.json`.

---

## Built-in Providers

### 1. Claude Code (`claude-code`)

Reads usage and rate limit data from Anthropic's Claude Code CLI.

- **Requirements**: [Claude Code](https://docs.anthropic.com/en/docs/agents-and-tools/claude-code/overview) CLI installed and authenticated (`claude` must be in your `PATH` or configured via `command`).
- **How it works**: Executes `claude` with a dummy prompt using Haiku (`-p ok --model haiku --output-format stream-json --verbose ...`) in a temporary directory and extracts the `rate_limit_event` payload (`five_hour` and `seven_day` unified windows).
- **Token Cost Warning**: Each poll consumes **~1,000 Claude Haiku tokens** to probe rate-limit metrics. Keep `refreshMinutes` at a reasonable interval (e.g., 10–15 minutes).

```json
{
  "id": "claude",
  "type": "claude-code",
  "label": "Claude",
  "enabled": true,
  "command": "claude",
  "timeoutSeconds": 90
}
```

---

### 2. Antigravity (`antigravity`)

Polls Google Antigravity quota and rate limits.

- **Requirements**: Antigravity CLI (`agy` in `PATH` or configured via `command`).
- **How it works**: Runs `agy -p /usage` and parses quota lines (`5h`, `Week`, etc.). The CLI reports remaining percentage, which QuotaWidget converts to used percentage (`100 - remaining`).
- **Main Family**: You can specify `mainFamily` (default: `"Gemini Models"`) to select which family provides the bars shown directly on the taskbar.

```json
{
  "id": "gemini",
  "type": "antigravity",
  "label": "Gemini",
  "enabled": true,
  "command": "agy",
  "mainFamily": "Gemini Models"
}
```

#### Multi-Account Setup (`runAsUser`)

If you maintain a second Google account under another local Windows user profile, QuotaWidget can query it using Windows `runas.exe`:

```json
{
  "id": "gemini-work",
  "type": "antigravity",
  "label": "Work",
  "enabled": true,
  "command": "agy",
  "runAsUser": "OtherUser",
  "exchangeDir": "C:\\Users\\Public\\QuotaExchange"
}
```

- **One-time Setup**: You must cache the user's password once in Windows Credential Manager. Open a command prompt or terminal and execute:
  ```cmd
  runas /savecred /user:OtherUser cmd.exe
  ```
  Enter the user's password when prompted.
- **Credential Storage**: The `/savecred` flag stores the credential securely in the standard Windows Credential Manager.
- **Exchange Directory**: `exchangeDir` must point to a directory readable and writable by both Windows users (defaults to `%TEMP%` if omitted).

---

### 3. OpenCode (`opencode`)

Tracks consumption against monthly budgets in OpenCode.

- **Requirements**: [OpenCode](https://opencode.ai) CLI installed (`opencode` in `PATH` or configured via `command`).
- **How it works**: Runs `opencode stats --days <N> --models --cost --json`, where `N` is the number of days elapsed since the start of the current billing cycle.
- **Cost-Based Estimate vs Budget**: OpenCode reports accumulated costs and usage for model requests. QuotaWidget calculates consumption percentage against your budget limit (`usd` for pay-per-use models, or `requests` / `tokens` for subscription models that report `cost: 0`). Because OpenCode metrics are cost-based estimates derived from session stats rather than live provider rate-limit headers, they are marked as **estimated** (dimmed display on the bar and labeled "estimated" in the panel).
- **Billing Period**: Configure `resetDay` (1–28, default `1`) to set the monthly day when your budget resets.

```json
{
  "id": "opencode",
  "type": "opencode",
  "label": "OC",
  "enabled": true,
  "command": "opencode",
  "resetDay": 1,
  "budgets": [
    {
      "name": "Go",
      "models": ["opencode-go/*"],
      "requests": 20000
    },
    {
      "name": "Claude",
      "models": ["anthropic/*", "claude-*"],
      "usd": 50.0
    }
  ]
}
```

---

## Custom Providers (`command` & `file`)

You can easily feed quota metrics from any script, background agent, or custom API without writing C# code:

- **`command` provider**: Runs any executable or script (PowerShell, Python, Bash, Node.js) and parses stdout.
- **`file` provider**: Monitors a local JSON file on disk updated by external jobs or status scripts.

Both providers communicate via the simple **Generic Quota JSON Schema (v1)**.

For full schema documentation and script examples, see [docs/PROVIDERS.md](docs/PROVIDERS.md).

Quick example:

```json
{
  "id": "custom-script",
  "type": "command",
  "label": "Custom",
  "enabled": true,
  "command": "powershell",
  "args": ["-NoProfile", "-File", "examples/providers/sample.ps1"],
  "timeoutSeconds": 90
}
```

---

## Agent & Orchestrator API

QuotaWidget provides a machine-readable export (`quotas.json`), a dedicated CLI tool (`qw`), and an IPC protocol designed for autonomous agents, subagent runners, and harness hooks:

- **Centralized Quota Export (`quotas.json`)**: Written atomically to `%LOCALAPPDATA%\QuotaWidget\quotas.json` with mutex synchronization (`Local\QuotaWidget.Export`) after every refresh and at startup.
- **`qw` CLI**:
  - `qw json`: Outputs the complete quota state as JSON (reads providers if missing).
  - `qw refresh [ids...] [--wait] [--force]`: Triggers an immediate refresh via IPC or standalone.
  - `qw pick <worker> [--max <percent>] [--json]`: Picks the qualifying provider with lowest utilization matching worker tags.
  - `qw status`: Pings the running desktop widget.
- **Routing & Throttling Metadata**: Configure `workers`, `workerFamilies`, and `minRefreshSeconds` per provider to intelligently route agent tasks and protect expensive probing budgets.
- **Harness & Tool Hooks**: Ready-to-use integrations in `examples/hooks/` for Claude Code (`Stop`/`SubagentStop`), OpenCode (`session.idle`), and PowerShell task wrappers.

For complete integration instructions and orchestrator recipes, see [docs/AGENTS.md](docs/AGENTS.md).

---

## Start with Windows

To have QuotaWidget start automatically when you log into Windows:

1. Press <kbd>Win</kbd> + <kbd>R</kbd> to open the **Run** dialog.
2. Type `shell:startup` and press <kbd>Enter</kbd> (this opens your user's Startup folder).
3. Right-click inside the folder, select **New** &rarr; **Shortcut**.
4. Browse to and select your `QuotaWidget.exe` executable, then click **Finish**.

---

## Privacy & Security

- **100% Local**: QuotaWidget runs entirely on your local machine. It does not contain telemetry, external trackers, or remote cloud reporting.
- **No Extra Credentials**: QuotaWidget does not ask for or store your API keys or passwords. It exclusively queries the official CLIs you already have installed and authenticated locally.
- **Non-Invasive Execution**: All process runners execute hidden background processes without disturbing active windows.

---

## Known Limitations

- **Overlay placement**: QuotaWidget operates as a borderless topmost tool window positioned over the taskbar rather than injecting into Windows shell processes (which is blocked by modern Windows 11 XAML taskbar protections). It automatically hides when a foreground window enters fullscreen mode.
- **Primary monitor only**: The widget currently positions itself exclusively on the primary monitor's taskbar (`Shell_TrayWnd`).

---

## License

This project is licensed under the [MIT License](LICENSE).
