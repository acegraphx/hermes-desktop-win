(function () {
    'use strict';

    var terminal = new Terminal({
        fontFamily: 'Cascadia Code, Cascadia Mono, Consolas, monospace',
        fontSize: 14,
        lineHeight: 1.1,
        theme: {
            background: '#1e1e1e',
            foreground: '#cccccc',
            cursor: '#ffffff',
            cursorAccent: '#1e1e1e',
            selectionBackground: '#264f78',
            black: '#000000',
            red: '#cd3131',
            green: '#0dbc79',
            yellow: '#e5e510',
            blue: '#2472c8',
            magenta: '#bc3fbc',
            cyan: '#11a8cd',
            white: '#e5e5e5',
            brightBlack: '#666666',
            brightRed: '#f14c4c',
            brightGreen: '#23d18b',
            brightYellow: '#f5f543',
            brightBlue: '#3b8eea',
            brightMagenta: '#d670d6',
            brightCyan: '#29b8db',
            brightWhite: '#e5e5e5'
        },
        cursorBlink: true,
        allowProposedApi: true,
        scrollback: 10000
    });

    var fitAddon = new FitAddon.FitAddon();
    terminal.loadAddon(fitAddon);

    terminal.open(document.getElementById('terminal'));

    // Try WebGL renderer for performance, fall back to canvas
    try {
        var webglAddon = new WebglAddon.WebglAddon();
        webglAddon.onContextLoss(function () {
            webglAddon.dispose();
        });
        terminal.loadAddon(webglAddon);
    } catch (e) {
        console.warn('WebGL addon failed, using canvas renderer:', e);
    }

    fitAddon.fit();

    // === Input: xterm.js -> C# ===
    terminal.onData(function (data) {
        // Encode as base64 for safe transport through JS interop
        var bytes = new TextEncoder().encode(data);
        var base64 = btoa(String.fromCharCode.apply(null, bytes));
        window.chrome.webview.postMessage({
            type: 'input',
            data: base64
        });
    });

    terminal.onBinary(function (data) {
        var base64 = btoa(data);
        window.chrome.webview.postMessage({
            type: 'input',
            data: base64
        });
    });

    // === Resize: xterm.js -> C# ===
    // Guarded against:
    // 1. Window minimize — WebView2 host collapses to ~0px and the observer fires.
    //    fitAddon.fit() against a 0x0 box yields 1x1 dims; forwarding that to the
    //    SSH PTY reflows the remote shell buffer to garbage. So we skip when the
    //    container has no area.
    // 2. Redundant emits — only post a resize message when (cols, rows) actually
    //    changed since the last emit, so font changes / no-op layout passes don't
    //    trigger spurious PTY resize requests.
    var resizeTimer = null;
    var lastEmittedCols = terminal.cols;
    var lastEmittedRows = terminal.rows;
    var terminalEl = document.getElementById('terminal');

    function emitResizeIfChanged() {
        if (terminal.cols !== lastEmittedCols || terminal.rows !== lastEmittedRows) {
            lastEmittedCols = terminal.cols;
            lastEmittedRows = terminal.rows;
            window.chrome.webview.postMessage({
                type: 'resize',
                cols: terminal.cols,
                rows: terminal.rows
            });
        }
    }

    function fitAndEmitResize() {
        if (terminalEl.clientWidth <= 0 || terminalEl.clientHeight <= 0) return;
        try { fitAddon.fit(); } catch (e) { return; }
        emitResizeIfChanged();
    }

    var resizeObserver = new ResizeObserver(function () {
        // Debounce resize events
        if (resizeTimer) clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function () {
            fitAndEmitResize();
        }, 100);
    });
    resizeObserver.observe(terminalEl);
    window.addEventListener('resize', function () {
        if (resizeTimer) clearTimeout(resizeTimer);
        resizeTimer = setTimeout(fitAndEmitResize, 100);
    });

    // === Output: C# -> xterm.js ===
    // Called from C# via ExecuteScriptAsync
    window.terminalWrite = function (base64Data) {
        try {
            var binaryStr = atob(base64Data);
            var bytes = new Uint8Array(binaryStr.length);
            for (var i = 0; i < binaryStr.length; i++) {
                bytes[i] = binaryStr.charCodeAt(i);
            }
            terminal.write(bytes);
        } catch (e) {
            console.error('terminalWrite error:', e);
        }
    };

    // === Clear: C# -> xterm.js ===
    window.terminalClear = function () {
        terminal.clear();
    };

    // === Theme: C# -> xterm.js ===
    // Accepts an object matching xterm's ITheme. Partial updates supported.
    window.terminalSetTheme = function (themeJson) {
        try {
            var theme = typeof themeJson === 'string' ? JSON.parse(themeJson) : themeJson;
            terminal.options.theme = theme;
        } catch (e) {
            console.error('terminalSetTheme error:', e);
        }
    };

    // === Font: C# -> xterm.js ===
    // After updating font options the cell geometry changes, so we re-fit and
    // notify C# of the new (cols, rows) so the SSH PTY can be resized to match.
    window.terminalSetFont = function (fontFamily, fontSize) {
        try {
            if (typeof fontFamily === 'string' && fontFamily.length > 0) {
                terminal.options.fontFamily = fontFamily;
            }
            if (typeof fontSize === 'number' && fontSize > 0) {
                terminal.options.fontSize = fontSize;
            }
            if (terminalEl.clientWidth > 0 && terminalEl.clientHeight > 0) {
                try { fitAddon.fit(); } catch (e) { /* ignore */ }
            }
            emitResizeIfChanged();
        } catch (e) {
            console.error('terminalSetFont error:', e);
        }
    };

    window.terminalFit = function () {
        fitAndEmitResize();
    };

    // === Font faces: C# -> xterm.js ===
    // Installs/replaces a <style id="hermes-fontfaces"> block carrying @font-face
    // rules for bundled + downloaded fonts. After fonts finish loading we re-fit
    // so xterm.js measures cell geometry against the real glyphs (not fallbacks).
    window.terminalInstallFontFaces = function (cssText) {
        try {
            var styleId = 'hermes-fontfaces';
            var existing = document.getElementById(styleId);
            if (existing) existing.remove();
            if (cssText && cssText.length > 0) {
                var style = document.createElement('style');
                style.id = styleId;
                style.textContent = cssText;
                document.head.appendChild(style);
            }
            if (document.fonts && document.fonts.ready) {
                document.fonts.ready.then(function () {
                    if (terminalEl.clientWidth > 0 && terminalEl.clientHeight > 0) {
                        try { fitAddon.fit(); } catch (e) { /* ignore */ }
                    }
                    emitResizeIfChanged();
                });
            }
        } catch (e) {
            console.error('terminalInstallFontFaces error:', e);
        }
    };

    // === Ctrl+Shift+C: copy selected text to clipboard ===
    // WPF's PreviewKeyDown never fires once xterm.js owns Win32 focus, so the
    // shortcut must be intercepted here. attachCustomKeyEventHandler returning
    // false prevents xterm from processing the key further.
    terminal.attachCustomKeyEventHandler(function (e) {
        if (e.type === 'keydown' && e.ctrlKey && e.shiftKey && e.key === 'C') {
            var selection = terminal.getSelection();
            if (selection) {
                navigator.clipboard.writeText(selection).catch(function () {
                    // navigator.clipboard may be blocked; fall back to execCommand
                    document.execCommand('copy');
                });
            }
            return false;
        }
        return true;
    });

    // === Focus management ===
    window.terminalFocus = function () {
        terminal.focus();
    };

    // Ensure xterm.js has DOM focus whenever the browser receives interaction.
    // WebView2 in WPF gives this HWND Win32 focus on click, but xterm.js needs
    // its hidden textarea focused to capture keyboard events.
    document.addEventListener('mousedown', function () {
        setTimeout(function () { terminal.focus(); }, 0);
    });
    window.addEventListener('focus', function () {
        terminal.focus();
    });

    // Signal ready
    window.chrome.webview.postMessage({
        type: 'ready',
        cols: terminal.cols,
        rows: terminal.rows
    });
})();
