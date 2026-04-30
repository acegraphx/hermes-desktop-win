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

**Terminal fonts:** Three sources unified through `Helpers/FontRegistry.cs` → `FontEntry` records: System (monospace fonts on the OS, filtered by glyph-advance equality of `i`/`M`/`W`), Bundled (5 TTFs in `Assets/Fonts/` shipped with the app), Downloaded (TTFs the user installed at runtime under `%APPDATA%\HermesDesktop\fonts\`). Catalog of downloadable fonts lives in `Resources/font-catalog.json` (embedded resource); `Helpers/FontCatalog.cs` streams downloads to `<id>.ttf.partial`, SHA-256-verifies against the manifest, then atomic-renames into place. `FontRegistry.Invalidate()` clears the cache and fires `Changed`; `FontManagerViewModel` (the "Manage Fonts" dialog from the ⚙ button next to the terminal font dropdown) and `TerminalViewModel` both subscribe. WebView2 gets two virtual host mappings (`hermes.fonts.bundled` → install dir, `hermes.fonts.user` → `%APPDATA%`) set up in `TerminalControl.OnLoaded` after `EnsureCoreWebView2Async`. On the JS side, `terminalInstallFontFaces(css)` injects `<style id="hermes-fontfaces">` with `@font-face` rules pointing at those virtual hosts; the bridge runs in the `case "ready":` handshake before `ApplyFontAsync` so xterm.js can resolve the user's selected family. `font-display: block` and a `document.fonts.ready` re-fit ensure cell geometry uses the real glyphs.

**Wiki module:** Markdown knowledge-base browser+editor (inspired by [tolaria](https://github.com/refactoringhq/tolaria), no code copied). Reads `<RemoteHermesHomePath>/home/wiki` by default; `ConnectionProfile.WikiPath` can override per-connection. Three-column layout: file tree (or filtered ListView when `FilterQuery` is set) | CodeMirror 6 editor + marked.js preview (Edit/Split/Preview mode toggle) | metadata (frontmatter + tag chips + lazy backlinks Expander). Live preview re-renders 300 ms after editor changes (with frontmatter stripped via `StripFrontmatter`). Save uses the existing SHA-256 optimistic-lock pattern (`wiki_write.py` re-implements `write_file.py`'s tempfile + `os.replace` flow against the wiki root, with anti-traversal validation). Search has two modes: client-side filename/tag filter (`FilterQuery`, immediate) and remote full-text via `wiki_search.py` (`SearchQuery`, debounced 300 ms; uses `rg` if available, falls back to `grep`). Wikilinks `[[Page]]`/`[[Page|alias]]` resolve via heuristic (same dir → exact relative path → frontmatter title → bare basename); Ctrl+click in the editor follows them, click in the preview also navigates. Backlinks are computed on demand by `wiki_backlinks.py` when the panel is expanded.

**Wiki editor (CodeMirror 6):** Vendored bundle at `Assets/Wiki/codemirror.bundle.js` (~554 KB IIFE) built from `tools/codemirror/entry.mjs` via `node tools/codemirror/build.mjs` (esbuild). Re-build only when bumping CM6; commit the resulting bundle. The bundle exposes `window.editorInit/editorSetContent/editorSetTheme/editorSetReadOnly/editorFocus`. C#↔JS bridge via `WebMessageReceived`: `bridge-ready` (DOMContentLoaded) → C# calls `editorInit`; `ready` (after EditorView created) → C# applies pending content; `change` → `EditorContent` two-way DP; `wikilink` (Ctrl+click on `[[...]]` token); `save` (Ctrl+S keymap). Two `Compartment`s reconfigure theme (oneDark vs custom light) and read-only state at runtime. WebView2 virtual host `hermes.wikieditor` → `Assets/Wiki`.

**Wiki preview + image streaming:** `WikiPreviewControl` is a fork of `MarkdownControl` (separate to avoid breaking other markdown views). `Assets/Wiki/preview.html` extends marked.js with a `wikilink` extension + custom image renderer that rewrites relative `src` to `https://hermes.wikiassets/<currentDir>/<src>` (or `https://hermes.wikiassets/<src>` for absolute paths). The `hermes.wikiassets` host has no folder mapping — it's intercepted by `CoreWebView2.WebResourceRequested` and served by `WikiAssetResolver`, which streams files via SFTP through `SftpConnectionPool` (separate pool from `SshConnectionPool` because Renci.SshNet's `SftpClient` opens its own session) with a 64 MB / 256-entry LRU cache keyed by `profile.Id + relativePath`. `WikiBrowserViewModel.PrefetchImages` regex-scans the body on load and warms the cache in parallel.

**Theming:** `ThemeManager` reads `HKCU\...\Personalize\AppsUseLightTheme` at startup and loads `DarkTheme.xaml` or `LightTheme.xaml`. MainWindow uses `DynamicResource` bindings for all theme-aware colors.

## Key Patterns

- All remote data fetching goes through `IRemoteScriptExecutor` → embedded `.py` script → JSON response. To add a new remote operation: write the Python script in `Scripts/` (use `resolved_hermes_home()` / `iter_session_store_candidates()` from shared helpers — do not re-declare `fail`, `stringify`, etc.), call it from a service via `ExecuteAsync<T>(profile, "script.py", params)`. `hermes_home` and `profile_name` are auto-injected into the payload by the executor.
- File editing uses SHA-256 hash-based optimistic locking. The `write_file.py` script checks `expected_content_hash` before writing.
- Navigation guards: `MainViewModel.RequestSectionNavigation()` checks `IsDirty` on either the file editor or the wiki browser before allowing section switches; the editable section is reflected via `CurrentEditableSection`. The wiki browser also has its own internal dirty guard (`WikiBrowserViewModel.OnSelectedEntryChanged`) for switching between wiki pages with `_isReverting` re-entrancy protection.
- Delete operations (connections, sessions) go through a confirmation flow: `RequestDelete*` shows overlay, `ConfirmDelete*` executes. Cron delete is direct (no confirmation yet).
- `SshConfigParser` reads `~/.ssh/config` for the "Import SSH Config" feature in connection management.
- `ConnectionProfile.RemoteHermesHomePath` derives `~/.hermes` or `~/.hermes/profiles/<name>` from the optional `HermesProfile` field. Do not hardcode paths in services or XAML.
- Scrollbars (WPF and `::-webkit-scrollbar` in `markdown.html`) are intentionally thin (6px) with hidden repeat buttons — see `Resources/ControlStyles.xaml`.
- Downloaded font `FontEntry.WpfFamily` is a Consolas fallback, NOT a disk-backed `FontFamily(Uri, "./#Family")`. Binding a freshly-written .ttf to a TextBlock measure crashed the process when Defender briefly locked the file: `TryGetFontTable` threw `FileNotFoundException` out of `MeasureOverride`, bypassing normal binding-error handling. The terminal renders downloaded fonts via xterm.js + `@font-face` (using the WebView2 virtual hosts), so WPF preview rendering for them is intentionally skipped. Bundled fonts are stable and keep their disk-backed `WpfFamily` for proper preview.
- `App.OnStartup` wires `DispatcherUnhandledException`, `AppDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` as a last-resort safety net — exceptions log to Serilog and don't kill the process. Don't rely on this to mask bugs; fix root causes when they appear in the log.
- Custom WPF window chrome: any new `Window` should match `MainWindow`'s pattern — `WindowChrome` with `CaptionHeight="32"`, custom 32px title-bar row with hermes icon + title + `CaptionButtonStyle`/`CloseButtonStyle` buttons, and a `StateChanged` handler that toggles maximize/restore glyph and applies a 7px `RootGrid.Margin` when maximized to compensate for WPF's maximized-window oversizing. `Views/FontManagerWindow.xaml(.cs)` is the reference for non-MainWindow dialogs.

## Local Storage

- `%APPDATA%\HermesDesktop\connections.json` — connection profiles (includes `hermesProfile` per entry)
- `%APPDATA%\HermesDesktop\preferences.json` — last connection ID, terminal theme preference, terminal font family + size, last opened wiki page per connection (`lastWikiRelativePathByConnection`)
- `%APPDATA%\HermesDesktop\fonts\<id>.ttf` — runtime-downloaded catalog fonts; `.partial` files are mid-download artifacts that are cleaned up on failure
- `%APPDATA%\HermesDesktop\logs\hermes-*.log` — Serilog rolling daily, 7-day retention

## Vendored Assets

`Assets/Terminal/` contains xterm.js 5.5.0 + addons + `terminal-bridge.js`. `Assets/Markdown/` contains marked.js 15.0.7 + `markdown.html`. `Assets/Fonts/` contains 5 bundled programming TTFs (Cascadia Code, Fira Code, Hack, IBM Plex Mono, JetBrains Mono) + `LICENSES.md` with attribution per OFL/MIT. All three folders are copied to output via `<Content>` items in the csproj.

**csproj gotchas for `Assets/Fonts/`** — both must be present or bundled fonts won't survive a `PublishSingleFile` build:

- `<Content>` items carry `<ExcludeFromSingleFile>true</ExcludeFromSingleFile>` so the TTFs stay on disk next to the exe (the WebView2 virtual host mapping needs physical files; otherwise the bundler embeds them into the single-file exe and they disappear from `publish/Assets/Fonts/`).
- `<Resource Remove="Assets\Fonts\**\*.ttf" />` overrides WPF's implicit `<Resource>` discovery for `.ttf` (which would embed them into the assembly and prevent disk copy entirely).

The font catalog manifest is `<EmbeddedResource Include="Resources\font-catalog.json" />`. `FontCatalog` reads it via `Assembly.GetManifestResourceStream` at startup.
