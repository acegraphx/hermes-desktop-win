using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using HermesDesktop.Helpers;
using HermesDesktop.Models;
using Microsoft.Web.WebView2.Core;
using Renci.SshNet;

namespace HermesDesktop.Controls;

public partial class TerminalControl : UserControl, IDisposable
{
    private ShellStream? _shellStream;
    private SshClient? _sshClient;
    private CancellationTokenSource? _readCts;
    private bool _isInitialized;
    private bool _isReady;
    private string? _pendingFontFamily;
    private int? _pendingFontSize;
    private TerminalThemeAppearance? _pendingTheme;
    private readonly ILogger? _logger;

    public TerminalControl()
    {
        InitializeComponent();
        Focusable = true;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
    }

    public TerminalControl(ILogger logger) : this()
    {
        _logger = logger;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;

        try
        {
            await TerminalWebView.EnsureCoreWebView2Async();

            // Map local assets folder to a virtual host
            var assetsPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Assets", "Terminal");

            // If running from source, assets might be in the project directory
            if (!Directory.Exists(assetsPath))
            {
                assetsPath = FindAssetsPath();
            }

            if (assetsPath != null && Directory.Exists(assetsPath))
            {
                TerminalWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "hermes.terminal",
                    assetsPath,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            // Map bundled + user font folders so @font-face URLs resolve. Bundled
            // ships with the app; user holds runtime-downloaded TTFs from the
            // catalog. Folder must exist before mapping or the mapping silently fails.
            try
            {
                var bundledFonts = FontRegistry.BundledFontsFolder;
                if (Directory.Exists(bundledFonts))
                {
                    TerminalWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        FontRegistry.BundledVirtualHost,
                        bundledFonts,
                        CoreWebView2HostResourceAccessKind.Allow);
                }
                var userFonts = FontRegistry.UserFontsFolder;
                Directory.CreateDirectory(userFonts);
                TerminalWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    FontRegistry.UserVirtualHost,
                    userFonts,
                    CoreWebView2HostResourceAccessKind.Allow);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to map font virtual hosts");
            }

            // Handle messages from xterm.js
            TerminalWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // Navigate to the terminal page
            TerminalWebView.CoreWebView2.Navigate("https://hermes.terminal/terminal.html");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize WebView2 for terminal");
        }
    }

    public void AttachSession(ShellStream shellStream, SshClient sshClient)
    {
        _shellStream = shellStream;
        _sshClient = sshClient;
        StartReading();
    }

    private void StartReading()
    {
        if (_shellStream == null) return;

        _readCts = new CancellationTokenSource();
        _ = ReadLoopAsync(_readCts.Token);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && _shellStream != null)
            {
                int bytesRead;
                try
                {
                    bytesRead = await _shellStream.ReadAsync(buffer, 0, buffer.Length, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Terminal read error");
                    break;
                }

                if (bytesRead <= 0)
                {
                    await Task.Delay(10, ct);
                    continue;
                }

                var data = new byte[bytesRead];
                Array.Copy(buffer, data, bytesRead);
                var base64 = Convert.ToBase64String(data);

                // Marshal to UI thread for WebView2 interop
                await Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        if (TerminalWebView.CoreWebView2 != null)
                        {
                            // Escape the base64 string for JS
                            var escaped = base64.Replace("\\", "\\\\").Replace("'", "\\'");
                            await TerminalWebView.CoreWebView2.ExecuteScriptAsync(
                                $"terminalWrite('{escaped}')");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed to write to terminal WebView");
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json!);
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "input":
                    var inputData = root.GetProperty("data").GetString();
                    if (inputData != null && _shellStream != null)
                    {
                        var bytes = Convert.FromBase64String(inputData);
                        await _shellStream.WriteAsync(bytes, 0, bytes.Length);
                        await _shellStream.FlushAsync();
                    }
                    break;

                case "resize":
                    var cols = root.GetProperty("cols").GetUInt32();
                    var rows = root.GetProperty("rows").GetUInt32();
                    SendWindowResize(cols, rows);
                    break;

                case "ready":
                    // Terminal is initialized — bridge functions exist on window now.
                    _isReady = true;
                    await InstallFontFacesAsync();
                    if (_pendingTheme is { } theme)
                    {
                        _pendingTheme = null;
                        await ApplyThemeAsync(theme);
                    }
                    if (_pendingFontFamily is { } font && _pendingFontSize is { } size)
                    {
                        _pendingFontFamily = null;
                        _pendingFontSize = null;
                        await ApplyFontAsync(font, size);
                    }
                    await TerminalWebView.CoreWebView2.ExecuteScriptAsync("terminalFocus()");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error handling terminal web message");
        }
    }

    private void SendWindowResize(uint cols, uint rows)
    {
        // SSH.NET 2025: SendWindowChangeRequest is on the Channel, not ShellStream.
        // Access it via reflection since there's no public API on ShellStream.
        try
        {
            if (_shellStream == null) return;
            var channelField = typeof(ShellStream).GetField("_channel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var channel = channelField?.GetValue(_shellStream);
            if (channel != null)
            {
                var method = channel.GetType().GetMethod("SendWindowChangeRequest");
                method?.Invoke(channel, new object[] { cols, rows, 0u, 0u });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to send window change request");
        }
    }

    public void QueueInitialAppearance(string fontFamily, int fontSize, TerminalThemeAppearance theme)
    {
        // Stash the desired font + theme so the "ready" handshake can apply them
        // once terminal-bridge.js has installed window.terminalSetFont/Theme. This
        // avoids a race where ApplyFontAsync would no-op because CoreWebView2 was
        // still initializing.
        _pendingFontFamily = fontFamily;
        _pendingFontSize = fontSize;
        _pendingTheme = theme;
    }

    public async Task InstallFontFacesAsync()
    {
        if (!_isReady || TerminalWebView.CoreWebView2 is null) return;
        try
        {
            var css = FontRegistry.CssFontFaceBlock();
            // JSON-serialize so quotes/backslashes inside the CSS are valid as a JS literal.
            var jsLiteral = JsonSerializer.Serialize(css);
            await TerminalWebView.CoreWebView2.ExecuteScriptAsync(
                $"terminalInstallFontFaces({jsLiteral})");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to install font faces");
        }
    }

    public async Task ApplyFontAsync(string fontFamily, int fontSize)
    {
        if (string.IsNullOrWhiteSpace(fontFamily)) return;

        if (!_isReady || TerminalWebView.CoreWebView2 is null)
        {
            _pendingFontFamily = fontFamily;
            _pendingFontSize = fontSize;
            return;
        }

        // Build a CSS-style font stack with sane fallbacks so the terminal still
        // renders even if the chosen face isn't available to WebView2 for some reason.
        var stack = $"{fontFamily}, Cascadia Mono, Consolas, monospace";
        var escaped = stack.Replace("\\", "\\\\").Replace("'", "\\'");
        try
        {
            await TerminalWebView.CoreWebView2.ExecuteScriptAsync(
                $"terminalSetFont('{escaped}', {fontSize})");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to apply terminal font");
        }
    }

    public async Task ApplyThemeAsync(TerminalThemeAppearance appearance)
    {
        if (!_isReady || TerminalWebView.CoreWebView2 is null)
        {
            _pendingTheme = appearance;
            return;
        }
        var p = appearance.AnsiPalette;
        if (p.Length < 16) return;
        var themeObj = new Dictionary<string, string>
        {
            ["background"] = appearance.Background.ToHex(),
            ["foreground"] = appearance.Foreground.ToHex(),
            ["cursor"] = appearance.Foreground.ToHex(),
            ["cursorAccent"] = appearance.Background.ToHex(),
            ["selectionBackground"] = p[4].ToHex(),
            ["black"] = p[0].ToHex(),
            ["red"] = p[1].ToHex(),
            ["green"] = p[2].ToHex(),
            ["yellow"] = p[3].ToHex(),
            ["blue"] = p[4].ToHex(),
            ["magenta"] = p[5].ToHex(),
            ["cyan"] = p[6].ToHex(),
            ["white"] = p[7].ToHex(),
            ["brightBlack"] = p[8].ToHex(),
            ["brightRed"] = p[9].ToHex(),
            ["brightGreen"] = p[10].ToHex(),
            ["brightYellow"] = p[11].ToHex(),
            ["brightBlue"] = p[12].ToHex(),
            ["brightMagenta"] = p[13].ToHex(),
            ["brightCyan"] = p[14].ToHex(),
            ["brightWhite"] = p[15].ToHex(),
        };
        var json = JsonSerializer.Serialize(themeObj);
        var escaped = json.Replace("\\", "\\\\").Replace("'", "\\'");
        try
        {
            await TerminalWebView.CoreWebView2.ExecuteScriptAsync(
                $"terminalSetTheme('{escaped}')");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to apply terminal theme");
        }
    }

    public new void Focus()
    {
        // Give this UserControl WPF keyboard focus so PreviewKeyDown/PreviewTextInput fire
        // as a fallback before the user clicks the terminal (which gives browser HWND Win32 focus).
        Keyboard.Focus(this);

        // Tell xterm.js to show a blinking cursor
        if (TerminalWebView.CoreWebView2 != null)
            _ = TerminalWebView.CoreWebView2.ExecuteScriptAsync("terminalFocus()");
    }

    // === WPF-level keyboard fallback ===
    // Before the user clicks the terminal, the WPF window has Win32 focus.
    // These handlers catch keyboard events and write directly to the shell stream.
    // Once the user clicks the terminal, browser HWND gets Win32 focus and
    // xterm.js handles keyboard natively via onData → postMessage.

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_shellStream == null || string.IsNullOrEmpty(e.Text)) return;

        var bytes = Encoding.UTF8.GetBytes(e.Text);
        _ = WriteToShellAsync(bytes);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_shellStream == null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mod = Keyboard.Modifiers;
        byte[]? bytes = null;

        // Ctrl+key → control characters (0x01–0x1A)
        if ((mod & ModifierKeys.Control) != 0 && key >= Key.A && key <= Key.Z)
        {
            bytes = [(byte)(key - Key.A + 1)];
        }
        // Alt+key → ESC prefix + character
        else if ((mod & ModifierKeys.Alt) != 0 && key >= Key.A && key <= Key.Z)
        {
            var ch = (mod & ModifierKeys.Shift) != 0
                ? (char)('A' + key - Key.A)
                : (char)('a' + key - Key.A);
            bytes = [(byte)'\x1B', (byte)ch];
        }
        // Special keys
        else if (mod == ModifierKeys.None || mod == ModifierKeys.Shift)
        {
            bytes = key switch
            {
                Key.Enter  => "\r"u8.ToArray(),
                Key.Back   => [0x7F],
                Key.Tab    => "\t"u8.ToArray(),
                Key.Escape => [0x1B],
                Key.Up     => "\x1B[A"u8.ToArray(),
                Key.Down   => "\x1B[B"u8.ToArray(),
                Key.Right  => "\x1B[C"u8.ToArray(),
                Key.Left   => "\x1B[D"u8.ToArray(),
                Key.Home   => "\x1B[H"u8.ToArray(),
                Key.End    => "\x1B[F"u8.ToArray(),
                Key.Delete => "\x1B[3~"u8.ToArray(),
                Key.Insert => "\x1B[2~"u8.ToArray(),
                Key.PageUp   => "\x1B[5~"u8.ToArray(),
                Key.PageDown => "\x1B[6~"u8.ToArray(),
                Key.F1  => "\x1BOP"u8.ToArray(),
                Key.F2  => "\x1BOQ"u8.ToArray(),
                Key.F3  => "\x1BOR"u8.ToArray(),
                Key.F4  => "\x1BOS"u8.ToArray(),
                Key.F5  => "\x1B[15~"u8.ToArray(),
                Key.F6  => "\x1B[17~"u8.ToArray(),
                Key.F7  => "\x1B[18~"u8.ToArray(),
                Key.F8  => "\x1B[19~"u8.ToArray(),
                Key.F9  => "\x1B[20~"u8.ToArray(),
                Key.F10 => "\x1B[21~"u8.ToArray(),
                Key.F11 => "\x1B[23~"u8.ToArray(),
                Key.F12 => "\x1B[24~"u8.ToArray(),
                Key.Space => " "u8.ToArray(),
                _ => null
            };
        }

        if (bytes != null)
        {
            _ = WriteToShellAsync(bytes);
            e.Handled = true;
        }
    }

    private async Task WriteToShellAsync(byte[] bytes)
    {
        try
        {
            if (_shellStream != null)
            {
                await _shellStream.WriteAsync(bytes, 0, bytes.Length);
                await _shellStream.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to write to shell stream");
        }
    }

    private static string? FindAssetsPath()
    {
        // Walk up from the current directory looking for the Assets/Terminal folder
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var candidate = Path.Combine(dir, "Assets", "Terminal");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "terminal.html")))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }

    public void Dispose()
    {
        _readCts?.Cancel();
        _readCts?.Dispose();

        try { _shellStream?.Dispose(); } catch { }
        try { _sshClient?.Dispose(); } catch { }

        _shellStream = null;
        _sshClient = null;
    }
}
