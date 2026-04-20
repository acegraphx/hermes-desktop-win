# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build
dotnet build src/HermesDesktop/HermesDesktop.csproj

# Run (debug)
dotnet run --project src/HermesDesktop

# Publish self-contained single-file release
dotnet publish src/HermesDesktop/HermesDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ./publish
```

There are no tests. No linter is configured.

## What This Is

A native Windows WPF port of [hermes-desktop](https://github.com/dodo-reach/hermes-desktop) (macOS SwiftUI). It connects to remote Hermes Agent hosts over SSH to manage sessions, edit config files, view usage analytics, browse skills, browse/create/edit cron jobs, and provide an interactive terminal. Profile-aware — every section runs against the Hermes profile selected on the connection (default `~/.hermes` or `~/.hermes/profiles/<name>`).

## Architecture

**Stack:** .NET 8 WPF, CommunityToolkit.Mvvm, SSH.NET, WebView2 (xterm.js + marked.js)

**MVVM with DI:** `App.xaml.cs` configures all services and ViewModels via `Microsoft.Extensions.DependencyInjection`. Services are singletons; content ViewModels are transient (recreated on each navigation). `MainViewModel` is the navigation hub — it resolves the active ViewModel and WPF's implicit `DataTemplate` matching in `App.xaml` renders the correct View.

**SSH transport:** `SshConnectionPool` maintains one persistent `SshClient` per connection profile (unlike the macOS app which spawns a fresh `ssh` process per command). Terminal sessions get their own dedicated connections because `ShellStream` ties up the channel.

**Remote script execution:** Python scripts in `Scripts/` are embedded as assembly resources. `RemotePythonScriptExecutor` base64-encodes the payload (auto-including `hermes_home` and `profile_name` from the active connection), wraps the script body with a set of shared helpers (`fail`, `normalize_text`, `choose_table`, `choose_column`, `resolved_hermes_home`, `iter_session_store_candidates`, `tilde`, `quote_ident`, `quote_text`, `expand_remote_path`), and sends it as `printf '%s' '<b64>' | base64 -d | python3 -` over SSH. Scripts print JSON with `"ok": true/false` to stdout. Nothing is installed on the remote host. Scripts should use `resolved_hermes_home()` instead of hardcoded `~/.hermes` so they respect the selected Hermes profile.

**Cron jobs:** `CronBrowserService` wraps three scripts — `cron_list.py` (reads `~/.hermes/cron/jobs.json`), `cron_mutate.py` (create/update, writes the JSON atomically), and `cron_command.py` (pause/resume/run/remove via the remote `hermes` CLI with `-p <profile>` when non-default). `CronJobsViewModel` drives a split UI: cards on the left (display-only), right pane swaps between a read-only detail panel and the editor form based on `IsDetailVisible` / `IsEditorVisible`.

**Terminal:** `TerminalControl` hosts WebView2 running xterm.js. Bidirectional data bridge: SSH.NET `ShellStream` bytes are base64-encoded and passed to JS via `ExecuteScriptAsync("terminalWrite('...')")`. User input goes back via `window.chrome.webview.postMessage()` → C# `WebMessageReceived` → `ShellStream.WriteAsync()`. Resize uses reflection to call `SendWindowChangeRequest` on the SSH channel (SSH.NET 2025 moved this method off `ShellStream`). Theme is applied via `terminalSetTheme(jsonTheme)` on the JS bridge; presets live in `Models/TerminalTheme.cs` (System, Graphite, Evergreen, Dusk, Paper) and the selected style is persisted to `preferences.json`. When a tab opens for a non-default Hermes profile, the VM writes `export HERMES_HOME=...; exec $SHELL -l` into the shell stream so the terminal is scoped to that profile.

**Theming:** `ThemeManager` reads `HKCU\...\Personalize\AppsUseLightTheme` at startup and loads `DarkTheme.xaml` or `LightTheme.xaml`. MainWindow uses `DynamicResource` bindings for all theme-aware colors.

## Key Patterns

- All remote data fetching goes through `IRemoteScriptExecutor` → embedded `.py` script → JSON response. To add a new remote operation: write the Python script in `Scripts/` (use `resolved_hermes_home()` / `iter_session_store_candidates()` from shared helpers — do not re-declare `fail`, `stringify`, etc.), call it from a service via `ExecuteAsync<T>(profile, "script.py", params)`. `hermes_home` and `profile_name` are auto-injected into the payload by the executor.
- File editing uses SHA-256 hash-based optimistic locking. The `write_file.py` script checks `expected_content_hash` before writing.
- Navigation guards: `MainViewModel.RequestSectionNavigation()` checks `IsDirty` on the file editor before allowing section switches.
- Delete operations (connections, sessions) go through a confirmation flow: `RequestDelete*` shows overlay, `ConfirmDelete*` executes. Cron delete is direct (no confirmation yet).
- `SshConfigParser` reads `~/.ssh/config` for the "Import SSH Config" feature in connection management.
- `ConnectionProfile.RemoteHermesHomePath` derives `~/.hermes` or `~/.hermes/profiles/<name>` from the optional `HermesProfile` field. Do not hardcode paths in services or XAML.
- Scrollbars (WPF and `::-webkit-scrollbar` in `markdown.html`) are intentionally thin (6px) with hidden repeat buttons — see `Resources/ControlStyles.xaml`.

## Local Storage

- `%APPDATA%\HermesDesktop\connections.json` — connection profiles (includes `hermesProfile` per entry)
- `%APPDATA%\HermesDesktop\preferences.json` — last connection ID, terminal theme preference
- `%APPDATA%\HermesDesktop\logs\hermes-*.log` — Serilog rolling daily, 7-day retention

## Vendored Assets

`Assets/Terminal/` contains xterm.js 5.5.0 + addons + `terminal-bridge.js`. `Assets/Markdown/` contains marked.js 15.0.7 + `markdown.html`. Both are copied to output via `<Content>` items in the csproj.
