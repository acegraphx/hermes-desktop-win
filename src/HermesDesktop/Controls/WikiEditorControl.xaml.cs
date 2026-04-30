using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace HermesDesktop.Controls;

public partial class WikiEditorControl : UserControl
{
    private bool _bridgeReady;
    private bool _editorInitialized;
    private bool _suppressNextChange;
    private string? _pendingContent;

    public static readonly DependencyProperty EditorContentProperty =
        DependencyProperty.Register(nameof(EditorContent), typeof(string), typeof(WikiEditorControl),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditorContentChanged));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(WikiEditorControl),
            new PropertyMetadata(false, OnReadOnlyChanged));

    public string? EditorContent
    {
        get => (string?)GetValue(EditorContentProperty);
        set => SetValue(EditorContentProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public event EventHandler<string>? WikilinkClicked;
    public event EventHandler? SaveRequested;

    public WikiEditorControl()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_editorInitialized) return;

        try
        {
            await EditorWebView.EnsureCoreWebView2Async();

            var assetsPath = FindAssetsPath("Wiki");
            if (assetsPath != null)
            {
                EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "hermes.wikieditor", assetsPath,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            EditorWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            EditorWebView.CoreWebView2.Navigate("https://hermes.wikieditor/editor.html");
        }
        catch
        {
            // WebView2 not available — silently degrade.
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;
            switch (typeProp.GetString())
            {
                case "bridge-ready":
                    _bridgeReady = true;
                    InitializeEditor();
                    break;
                case "ready":
                    _editorInitialized = true;
                    if (_pendingContent != null)
                    {
                        ApplyContent(_pendingContent);
                        _pendingContent = null;
                    }
                    break;
                case "change":
                    if (root.TryGetProperty("text", out var textProp))
                    {
                        var text = textProp.GetString() ?? string.Empty;
                        _suppressNextChange = true;
                        try { EditorContent = text; }
                        finally { _suppressNextChange = false; }
                    }
                    break;
                case "wikilink":
                    if (root.TryGetProperty("target", out var t))
                        WikilinkClicked?.Invoke(this, t.GetString() ?? string.Empty);
                    break;
                case "save":
                    SaveRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch { /* ignore malformed bridge messages */ }
    }

    private void InitializeEditor()
    {
        if (!_bridgeReady || EditorWebView.CoreWebView2 == null) return;

        var initial = EditorContent ?? string.Empty;
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(initial));
        var isDark = Helpers.ThemeManager.IsSystemDarkMode() ? "true" : "false";
        var ro = IsReadOnly ? "true" : "false";
        _ = EditorWebView.CoreWebView2.ExecuteScriptAsync(
            $"window.editorInit('{base64}', {isDark}, {ro})");
    }

    private void ApplyContent(string content)
    {
        if (EditorWebView.CoreWebView2 == null) return;
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content ?? string.Empty));
        _ = EditorWebView.CoreWebView2.ExecuteScriptAsync(
            $"window.editorSetContent('{base64}')");
    }

    public void SetTheme(bool isDark)
    {
        if (!_editorInitialized || EditorWebView.CoreWebView2 == null) return;
        var arg = isDark ? "true" : "false";
        _ = EditorWebView.CoreWebView2.ExecuteScriptAsync($"window.editorSetTheme({arg})");
    }

    private static void OnEditorContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WikiEditorControl c) return;
        if (c._suppressNextChange) return;
        var text = e.NewValue as string ?? string.Empty;
        if (c._editorInitialized)
            c.ApplyContent(text);
        else
            c._pendingContent = text;
    }

    private static void OnReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WikiEditorControl c || !c._editorInitialized || c.EditorWebView.CoreWebView2 == null) return;
        var arg = (bool)e.NewValue ? "true" : "false";
        _ = c.EditorWebView.CoreWebView2.ExecuteScriptAsync($"window.editorSetReadOnly({arg})");
    }

    private static string? FindAssetsPath(string subfolder)
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var candidate = Path.Combine(dir, "Assets", subfolder);
            if (Directory.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }
}
