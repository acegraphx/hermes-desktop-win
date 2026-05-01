# Hermes Desktop for Windows

A native Windows app for working with [Hermes Agent](https://github.com/dodo-reach/hermes-desktop) hosts over SSH. Browse sessions, edit your `USER.md` / `MEMORY.md` / `SOUL.md`, watch token usage, manage cron jobs, write markdown notes in a built-in wiki, and pop open a real terminal &mdash; all without installing anything on the remote machine.

> **Windows port of [hermes-desktop](https://github.com/dodo-reach/hermes-desktop)** &mdash; the original native macOS (SwiftUI) workspace by [dodo-reach](https://github.com/dodo-reach). Same philosophy: real SSH, real terminal, the remote host stays the single source of truth. Built from scratch in .NET 8 WPF + SSH.NET to bring the same experience to Windows.

---

## Download

Grab `HermesDesktop.exe` from [**Releases**](https://github.com/acegraphx/hermes-desktop-win/releases). One self-contained file (~72 MB) &mdash; no installer, no .NET runtime needed. Just run it.

---

## Why use it

If you keep your Hermes data on a Pi, a VPS, or any remote box, you'd normally either:
- SSH in and grep through SQLite by hand, or
- run a browser-based dashboard somewhere and remember a URL.

This app gives you a clean native desktop window into all of that. It connects over your existing SSH key, pulls the data it needs, and shows it in one place. Nothing is installed on the server &mdash; the app sends short Python snippets over your SSH session whenever it needs data.

---

## What you can do

### Your conversations & memory
- **Sessions** &mdash; Browse every conversation Hermes has stored, search across them, read full transcripts, and delete the ones you no longer need.
- **Files** &mdash; Edit `USER.md`, `MEMORY.md`, and `SOUL.md` directly. Built-in conflict detection catches the case where the file changed on the server while you were editing.
- **Wiki** &mdash; A markdown knowledge base under `~/.hermes/wiki`. Three-column layout: file tree, editor + live preview, metadata sidebar with frontmatter, tags, and backlinks. Wikilinks (`[[Page]]`), full-text search across all your notes, and **autosave** so you never lose work. The default view is a clean preview; switch to Edit or Split with one click.

### Terminal
- A real SSH terminal &mdash; same engine as VS Code's terminal &mdash; with tabs, full color, scrollback, and mouse selection.
- **Pick your font.** Browse system monospace fonts, switch between five bundled programming fonts (Cascadia Code, Fira Code, Hack, IBM Plex Mono, JetBrains Mono), or download more from the built-in catalog.
- **Five themes** &mdash; System, Graphite, Evergreen, Dusk, Paper &mdash; with live preview.

### Automation & insight
- **Cron Jobs** &mdash; Browse and create scheduled jobs through a friendly UI: pick hourly / daily / weekly / monthly / "every N hours" / one-shot at a date, or drop in a raw cron expression. Pause, resume, run-now, edit, or delete each job without touching the server.
- **Usage** &mdash; Token-usage dashboard. See input / output / cache-read / cache-write / reasoning totals, per-model breakdown, provider attribution, cost estimates, and a bar chart of your recent sessions. Tick **"Aggregate across all profiles on this host"** to roll up the totals across every readable Hermes profile, with a per-profile breakdown table.
- **Skills** &mdash; Recursive view of every `SKILL.md` in `~/.hermes/skills/` plus any external directories listed in `skills.external_dirs` in `~/.hermes/config.yaml` (with local skills taking precedence). Edit local skills inline (atomic save with conflict detection) or create new ones. External skills show an "External" badge and are read-only.

### Multiple hosts & profiles
- **Connection management** &mdash; Add, edit, test, and delete SSH profiles. Click **Import SSH Config** to pull hosts from your `~/.ssh/config`.
- **Hermes profile awareness** &mdash; Each connection can target either the default `~/.hermes` or a named profile under `~/.hermes/profiles/<name>`. Every section &mdash; sessions, usage, files, cron, terminal &mdash; runs against the chosen profile. The Overview page lists other available profiles on the host with a one-click switch.

### Quality-of-life touches
- **Dark mode** that follows your Windows theme.
- **Collapsible sidebar** &mdash; rail mode shows just the icons; full mode shows the labels. Your choice persists.
- **Stays out of your way** &mdash; no background services, no auto-updates, no telemetry. Configuration lives in `%APPDATA%\HermesDesktop\` and that's it.

---

## Screenshots

| Overview | Sessions |
|----------|----------|
| ![Overview](screenshots/overview.png) | ![Sessions](screenshots/sessions.png) |

| Wiki | Files |
|------|-------|
| ![Wiki](screenshots/wiki.png) | ![Files](screenshots/files.png) |

| Usage | Skills |
|-------|--------|
| ![Usage](screenshots/usage.png) | ![Skills](screenshots/skills.png) |

| Terminal |
|----------|
| ![Terminal](screenshots/terminal.png) |

---

## Requirements

**Your Windows machine:**
- Windows 10 or later (x64)
- WebView2 runtime (pre-installed on Windows 10/11 with Edge)

**The remote host you're connecting to:**
- SSH access with key-based authentication (no password prompts)
- `python3` available in the SSH environment
- Hermes data under `~/.hermes` (or `~/.hermes/profiles/<name>` for non-default profiles)

---

## Quick start

1. Launch the app. You land on the **Connections** screen.
2. Click **New Connection** and fill in:
   - **Label** &mdash; a name you'll recognize (e.g. "Pi", "VPS")
   - **Host** &mdash; hostname or IP address
   - **Username** &mdash; your SSH user
   - **Port** &mdash; usually 22
   - **SSH Key Path** &mdash; optional; the app auto-discovers `~/.ssh/id_ed25519`, `id_rsa`, `id_ecdsa`
   - **Hermes Profile** &mdash; leave blank for the default profile, or type the profile name
3. Click **Test** to verify SSH connectivity and Python availability.
4. Click **Save**, then **Connect** on the new entry.
5. The sidebar unlocks all sections. Browse sessions, edit files, write wiki notes, or open a terminal.

Or click **Import SSH Config** to bring in hosts from your `~/.ssh/config` in one go.

---

## Common questions

**Does the app install anything on the remote host?**
No. Every operation sends a short Python script over your SSH session. The script runs once, prints JSON, and exits. Nothing is left behind.

**Does the wiki auto-save my edits?**
Yes &mdash; 1.5 seconds after you stop typing. The status indicator next to the **Save** button shows `Unsaved (autosaving…)` &rarr; `Saving…` &rarr; `Saved HH:mm:ss`. `Ctrl+S` still works for an immediate save.

**Where does my data live?**
On the remote host. The Windows app stores only your connection profiles and preferences locally in `%APPDATA%\HermesDesktop\`.

**Can I use multiple Hermes profiles on the same machine?**
Yes. Create one connection per profile (each one points at `~/.hermes/profiles/<name>`). Switch between them with one click on the Overview page.

**Is it compatible with the macOS hermes-desktop app?**
Yes &mdash; both speak the same protocol to the remote host. You can use them side-by-side against the same Hermes installation.

---

## Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+S` | Save the current file (Files view or Wiki) |

---

<br>

---

# Technical details

Everything below is for developers, packagers, and the curious. Normal users can stop here.

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
  xterm.js in WebView2 (terminal)    marked.js + CodeMirror 6 in WebView2 (markdown / wiki)
```

### How remote commands work

The app does **not** install anything on the remote host. Every operation follows this pattern:

1. A Python script is loaded from an embedded resource.
2. Parameters are injected as a `payload` dictionary at the top of the script (including `hermes_home` and `profile_name` resolved from the active connection).
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
- **Resize:** `ResizeObserver` &rarr; `postMessage` &rarr; `SendWindowChangeRequest` on the SSH channel

### Wiki implementation

CodeMirror 6 editor (vendored bundle) + marked.js preview running in WebView2. Wiki images are streamed over SFTP through a virtual host (`hermes.wikiassets`) with a 64 MB / 256-entry LRU cache. Saves use the same SHA-256 optimistic-lock protocol as `USER.md` editing. Autosave is a 1.5 s debounce on the editor's change event.

### Terminal fonts

Three sources unified through a `FontRegistry`: System monospace fonts, five bundled TTFs, and downloadable fonts from a manifest at `Resources/font-catalog.json`. Downloaded fonts land in `%APPDATA%\HermesDesktop\fonts\` and are SHA-256-verified before activation. xterm.js renders all three via WebView2 `@font-face` rules pointing at virtual hosts (`hermes.fonts.bundled`, `hermes.fonts.user`).

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
    Controls/                   TerminalControl, MarkdownControl, WikiEditorControl, ...
    Scripts/                    Embedded Python scripts
    Assets/Terminal/            xterm.js, terminal-bridge.js, HTML host
    Assets/Markdown/            marked.js, markdown.html
    Assets/Wiki/                CodeMirror 6 bundle, preview.html
    Assets/Fonts/               Bundled programming fonts (TTF) + LICENSES.md
    Resources/                  Themes, icons, font-catalog.json
    Helpers/                    AppPaths, ThemeManager, FontRegistry, ...
    Converters/                 WPF value converters
  build/
    publish.ps1                 Build & publish script
  docs/
    ARCHITECTURE.md, DEVELOPMENT.md
```

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [SSH.NET](https://github.com/sshnet/SSH.NET) | 2025.1.0 | SSH connections, commands, shell streams, SFTP |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MVVM framework with source generators |
| [Microsoft.Web.WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) | 1.0.3912 | Terminal (xterm.js), markdown (marked.js), wiki editor (CodeMirror 6) |
| [Microsoft.Extensions.Hosting](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) | 10.0.6 | DI, logging, host lifecycle |
| [Serilog](https://serilog.net/) | 10.0.0 | Structured logging to rolling daily files |

Vendored client-side libraries (in `Assets/`):
- [xterm.js](https://xtermjs.org/) 5.5.0 &mdash; terminal emulator
- [xterm-addon-fit](https://www.npmjs.com/package/@xterm/addon-fit) 0.10.0 &mdash; auto-fit terminal to container
- [xterm-addon-webgl](https://www.npmjs.com/package/@xterm/addon-webgl) 0.18.0 &mdash; GPU-accelerated rendering
- [marked.js](https://marked.js.org/) 15.0.7 &mdash; markdown to HTML
- [CodeMirror 6](https://codemirror.net/) &mdash; wiki editor (vendored as a single IIFE bundle)

---

## Building from source

### Debug build

```bash
# Prerequisites: .NET 8 SDK
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

Output: `publish/HermesDesktop.exe` (~73 MB, includes the .NET runtime).

---

## Local data

The app stores configuration in `%APPDATA%\HermesDesktop\`:

| File | Contents |
|------|----------|
| `connections.json` | SSH connection profiles (label, host, user, port, key path, Hermes profile, optional wiki path) |
| `preferences.json` | Last active connection, terminal theme/font/size, sidebar collapsed state, wiki view mode + split ratio + autosave, last-opened wiki page per connection |
| `fonts/<id>.ttf` | Runtime-downloaded catalog fonts |
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
| Wiki editor | Native | CodeMirror 6 in WebView2 |
| Connection pooling | OS `ControlMaster` | In-app `SshConnectionPool` |
| SSH config | Used for aliases | Parsed + import button |
| Auth | System SSH agent | Direct key file loading |
| Theme | System macOS appearance | Windows registry detection |
| Distribution | `.app` bundle | Single-file `.exe` |

The remote Python scripts are functionally equivalent &mdash; same `payload`/`ok` JSON protocol, same SQLite queries, same file operations.

For deeper internals, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

---

## Acknowledgments

This project is a Windows port of [**hermes-desktop**](https://github.com/dodo-reach/hermes-desktop) by [dodo-reach](https://github.com/dodo-reach) &mdash; a native macOS SwiftUI workspace for Hermes Agent. The original app introduced the SSH-only, zero-install approach to managing Hermes hosts from a native desktop app. The remote Python scripts, JSON protocol, and overall feature set in this Windows port are functionally equivalent to the macOS original.

---

## License

MIT &mdash; same as the original [hermes-desktop](https://github.com/dodo-reach/hermes-desktop).
