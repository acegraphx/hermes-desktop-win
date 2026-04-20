# Hermes Desktop for Windows

A native Windows desktop client for [Hermes Agent](https://github.com/dodo-reach/hermes-desktop) hosts. Connects directly over SSH &mdash; no browser wrapper, no gateway API, no helper service installed on the remote host.

> **Windows port of [hermes-desktop](https://github.com/dodo-reach/hermes-desktop)** &mdash; the original native macOS (SwiftUI) workspace by [dodo-reach](https://github.com/dodo-reach). Same philosophy: real SSH, real terminal, remote host as the single source of truth. This port is built from scratch with .NET 8 WPF and SSH.NET to bring the same experience to Windows.

---

## Download

Grab the latest `HermesDesktop.exe` from [**Releases**](https://github.com/acegraphx/hermes-desktop-win/releases). It's a self-contained single file (~72 MB) &mdash; no installer, no .NET runtime needed. Just run it.

---

## Features

- **Terminal** &mdash; Embedded SSH terminal with tabs, powered by xterm.js in WebView2. Full color, resize, scrollback. **Customizable themes** (System, Graphite, Evergreen, Dusk, Paper) with live preview and persisted preference.
- **Sessions** &mdash; Browse, search, and delete sessions from the remote session store. View full message transcripts.
- **Files** &mdash; Edit `USER.md`, `MEMORY.md`, and `SOUL.md` with conflict detection. SHA-256 hash-based optimistic locking prevents blind overwrites.
- **Usage** &mdash; Token usage dashboard with totals (input, output, cache-read, cache-write, reasoning), per-model breakdown, provider attribution, cost estimates, and a bar chart of recent sessions.
- **Skills** &mdash; Recursive skill discovery from `~/.hermes/skills/`. YAML frontmatter parsing, tag display, and rendered markdown content.
- **Cron Jobs** &mdash; Browse, create, edit, pause/resume, run-now, and delete remote Hermes cron jobs. Supports hourly, daily, weekday, weekly, monthly, every-N-interval, after-delay, at-datetime, and custom cron expressions. Read-only detail panel with full metadata (schedule, prompt, skills, next/last run, delivery target); switch to editor mode with a single click.
- **Overview** &mdash; Confirms remote home, Hermes root, tracked files, session source, Python version, and active Hermes profile. Lists other available profiles on the host with a one-click Switch action.
- **Hermes profile awareness** &mdash; Each connection can target a named Hermes profile under `~/.hermes/profiles/<name>` or the default `~/.hermes`. Every section (overview, sessions, usage, skills, files, cron, terminal) runs against the selected profile. Profile badge shown on the connection list.
- **Connection Management** &mdash; Add, edit, test, and delete SSH profiles. Import hosts from `~/.ssh/config`.
- **Dark Mode** &mdash; Automatically detects the Windows system theme.

---

## Screenshots

| Overview | Sessions |
|----------|----------|
| ![Overview](screenshots/overview.png) | ![Sessions](screenshots/sessions.png) |

| Usage | Skills |
|-------|--------|
| ![Usage](screenshots/usage.png) | ![Skills](screenshots/skills.png) |

| Terminal |
|----------|
| ![Terminal](screenshots/terminal.png) |

---

## Requirements

**Your machine (Windows):**
- Windows 10 or later (x64)
- WebView2 runtime (pre-installed on Windows 10/11 with Edge)

**Remote host:**
- SSH access with key-based authentication (no password prompts)
- `python3` available in the SSH environment
- Hermes data under `~/.hermes`

---

## Installation

### From release

1. Download `HermesDesktop.zip` from Releases
2. Extract anywhere
3. Run `HermesDesktop.exe`

### From source

```bash
# Prerequisites: .NET 8 SDK
dotnet build src/HermesDesktop/HermesDesktop.csproj

# Run
dotnet run --project src/HermesDesktop
```

---

## Quick start

1. Launch the app. You land on the **Connections** screen.
2. Click **New Connection** and enter:
   - **Label** &mdash; a name you'll recognize (e.g. "Pi", "VPS")
   - **Host** &mdash; hostname or IP address
   - **Username** &mdash; your SSH user
   - **Port** &mdash; typically 22
   - **SSH Key Path** &mdash; optional, auto-discovers `~/.ssh/id_ed25519`, `id_rsa`, `id_ecdsa`
3. Click **Test** to verify SSH connectivity and Python availability.
4. Click **Save**, then **Connect** on the new entry.
5. The sidebar unlocks all sections. Browse sessions, edit files, or open a terminal.

Alternatively, click **Import SSH Config** to pull hosts from your `~/.ssh/config`.

---

## Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+S` | Save the current file (when in Files view) |

---

## Architecture

```
WPF (.NET 8) + CommunityToolkit.Mvvm
       |
  MVVM: Views (XAML) <-> ViewModels (C#) <-> Services (C#)
       |
  SSH.NET (connection pool, command execution, ShellStream)
       |
  Remote Python scripts (base64-encoded, piped to python3 via SSH)
       |
  xterm.js in WebView2 (terminal)    marked.js in WebView2 (markdown)
```

### How remote commands work

The app does **not** install anything on the remote host. Every operation follows this pattern:

1. A Python script is loaded from an embedded resource.
2. Parameters are injected as a `payload` dictionary at the top of the script.
3. The script is base64-encoded and sent as a single SSH command:
   ```
   printf '%s' '<base64>' | base64 -d | python3 -
   ```
4. Python runs on the remote host, queries `~/.hermes/state.db` or reads files, and prints JSON to stdout.
5. The app parses the JSON response.

This keeps the remote host stateless. No helper services, no daemons, no file mirroring.

### SSH connection pooling

Unlike the macOS app (which spawns a fresh `ssh` process per command), the Windows app maintains a persistent SSH connection per profile via SSH.NET. A `SshConnectionPool` with double-checked locking manages connections. Terminal sessions get dedicated connections since `ShellStream` ties up the channel.

### Terminal implementation

The terminal uses xterm.js (the same engine as VS Code's terminal) running inside WebView2. Data flows bidirectionally:

- **Output:** `ShellStream.ReadAsync()` &rarr; base64 &rarr; `ExecuteScriptAsync("terminalWrite('...')")` &rarr; xterm.js
- **Input:** xterm.js `onData` &rarr; `postMessage` &rarr; C# `WebMessageReceived` &rarr; `ShellStream.WriteAsync()`
- **Resize:** `ResizeObserver` &rarr; `postMessage` &rarr; `SendWindowChangeRequest` on SSH channel

---

## Project structure

```
hermes-desktop-win/
  HermesDesktop.sln
  src/HermesDesktop/
    App.xaml.cs                 DI container, Serilog, theme, startup
    MainWindow.xaml             Sidebar + ContentControl shell
    Models/                     Data models (ConnectionProfile, Session, etc.)
    Services/                   SSH transport, script executor, data services
    ViewModels/                 MVVM ViewModels for each section
    Views/                      XAML views for each section
    Controls/                   TerminalControl, MarkdownControl, SimpleBarChart
    Scripts/                    Embedded Python scripts (12 files)
    Assets/Terminal/            xterm.js, terminal-bridge.js, HTML host
    Assets/Markdown/            marked.js, markdown.html
    Resources/                  DarkTheme.xaml, LightTheme.xaml, Icons/
    Helpers/                    AppPaths, ThemeManager, RelativeDateHelper
    Converters/                 WPF value converters
  build/
    publish.ps1                 Build & publish script
```

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [SSH.NET](https://github.com/sshnet/SSH.NET) | 2025.1.0 | SSH connections, commands, shell streams |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MVVM framework with source generators |
| [Microsoft.Web.WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) | 1.0.3912 | Terminal (xterm.js) and markdown (marked.js) rendering |
| [Microsoft.Extensions.Hosting](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) | 10.0.6 | DI, logging, host lifecycle |
| [Serilog](https://serilog.net/) | 10.0.0 | Structured logging to rolling daily files |

Vendored client-side libraries (in `Assets/`):
- [xterm.js](https://xtermjs.org/) 5.5.0 &mdash; terminal emulator
- [xterm-addon-fit](https://www.npmjs.com/package/@xterm/addon-fit) 0.10.0 &mdash; auto-fit terminal to container
- [xterm-addon-webgl](https://www.npmjs.com/package/@xterm/addon-webgl) 0.18.0 &mdash; GPU-accelerated rendering
- [marked.js](https://marked.js.org/) 15.0.7 &mdash; markdown to HTML

---

## Building

### Debug build

```bash
dotnet build src/HermesDesktop/HermesDesktop.csproj
dotnet run --project src/HermesDesktop
```

### Release publish (self-contained single file)

```powershell
# PowerShell
.\build\publish.ps1

# Or manually:
dotnet publish src/HermesDesktop/HermesDesktop.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o ./publish
```

Output: `publish/HermesDesktop.exe` (~73 MB, includes .NET runtime)

---

## Local data

The app stores configuration in `%APPDATA%\HermesDesktop\`:

| File | Contents |
|------|----------|
| `connections.json` | SSH connection profiles (label, host, user, port, key path, Hermes profile name) |
| `preferences.json` | Last active connection ID, terminal theme preference |
| `logs/hermes-YYYYMMDD.log` | Debug logs (7-day rolling retention) |

All writes use atomic temp-file-then-rename to prevent corruption.

---

## Dark mode

The app reads the Windows registry key `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` at startup and applies the matching theme (`DarkTheme.xaml` or `LightTheme.xaml`). The sidebar, dialogs, and overlays all respect the active theme via `DynamicResource` bindings.

---

## How it compares to the macOS app

| Aspect | macOS (SwiftUI) | Windows (WPF) |
|--------|-----------------|---------------|
| SSH | Shells out to `/usr/bin/ssh` | SSH.NET library (pure C#) |
| Terminal | Vendored SwiftTerm | xterm.js in WebView2 |
| Markdown | Native SwiftUI text | marked.js in WebView2 |
| Connection pooling | OS `ControlMaster` | In-app `SshConnectionPool` |
| SSH config | Used for aliases | Parsed + import button |
| Auth | System SSH agent | Direct key file loading |
| Theme | System macOS appearance | Windows registry detection |
| Distribution | `.app` bundle | Single-file `.exe` |

The remote Python scripts are functionally equivalent &mdash; same `payload`/`ok` JSON protocol, same SQLite queries, same file operations.

---

## Acknowledgments

This project is a Windows port of [**hermes-desktop**](https://github.com/dodo-reach/hermes-desktop) by [dodo-reach](https://github.com/dodo-reach) &mdash; a native macOS SwiftUI workspace for Hermes Agent. The original app introduced the SSH-only, zero-install approach to managing Hermes hosts from a native desktop app. The remote Python scripts, JSON protocol, and overall feature set in this Windows port are functionally equivalent to the macOS original.

---

## License

MIT &mdash; same as the original [hermes-desktop](https://github.com/dodo-reach/hermes-desktop).
