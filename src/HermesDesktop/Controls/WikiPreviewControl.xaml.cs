using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HermesDesktop.Helpers;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Web.WebView2.Core;

namespace HermesDesktop.Controls;

public partial class WikiPreviewControl : UserControl
{
    private bool _isReady;
    private bool _navigationStarted;
    private string? _pendingMarkdown;

    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.Register(nameof(MarkdownText), typeof(string), typeof(WikiPreviewControl),
            new PropertyMetadata(null, OnRenderInputChanged));

    public static readonly DependencyProperty CurrentDirProperty =
        DependencyProperty.Register(nameof(CurrentDir), typeof(string), typeof(WikiPreviewControl),
            new PropertyMetadata(string.Empty, OnRenderInputChanged));

    public static readonly DependencyProperty ProfileProperty =
        DependencyProperty.Register(nameof(Profile), typeof(ConnectionProfile), typeof(WikiPreviewControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AssetResolverProperty =
        DependencyProperty.Register(nameof(AssetResolver), typeof(WikiAssetResolver), typeof(WikiPreviewControl),
            new PropertyMetadata(null));

    public string? MarkdownText
    {
        get => (string?)GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    public string CurrentDir
    {
        get => (string)GetValue(CurrentDirProperty);
        set => SetValue(CurrentDirProperty, value);
    }

    public ConnectionProfile? Profile
    {
        get => (ConnectionProfile?)GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public WikiAssetResolver? AssetResolver
    {
        get => (WikiAssetResolver?)GetValue(AssetResolverProperty);
        set => SetValue(AssetResolverProperty, value);
    }

    public event EventHandler<string>? WikilinkClicked;
    public event EventHandler<string>? ExternalLinkClicked;

    public WikiPreviewControl()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_navigationStarted) return;
        _navigationStarted = true;

        try
        {
            await PreviewWebView.EnsureCoreWebView2Async();
            PreviewWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

            var assetsPath = AppAssets.ResolveAssetFolder("Wiki", "preview.html");
            if (assetsPath != null)
            {
                PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "hermes.wikipreview", assetsPath,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            // Intercept all requests to the asset host so we can stream them via SFTP.
            PreviewWebView.CoreWebView2.AddWebResourceRequestedFilter(
                "https://hermes.wikiassets/*", CoreWebView2WebResourceContext.All);
            PreviewWebView.CoreWebView2.WebResourceRequested += OnAssetRequested;

            PreviewWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            PreviewWebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _isReady = true;
                if (_pendingMarkdown != null)
                {
                    Render(_pendingMarkdown, CurrentDir);
                    _pendingMarkdown = null;
                }
            };

            PreviewWebView.CoreWebView2.Navigate("https://hermes.wikipreview/preview.html");
        }
        catch
        {
            // WebView2 not available — fall back silently.
        }
    }

    private async void OnAssetRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var profile = Profile;
            var resolver = AssetResolver;
            if (profile == null || resolver == null)
            {
                e.Response = PreviewWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                    null, 404, "Not Found", "Content-Type: text/plain");
                return;
            }

            var uri = new Uri(e.Request.Uri);
            var relative = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

            var (bytes, mime) = await resolver.GetAsync(profile, relative);
            if (bytes == null)
            {
                e.Response = PreviewWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                    null, 404, "Not Found", "Content-Type: text/plain");
                return;
            }

            var stream = new MemoryStream(bytes);
            e.Response = PreviewWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                stream, 200, "OK",
                $"Content-Type: {mime}\r\nCache-Control: max-age=300");
        }
        catch
        {
            e.Response = PreviewWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                null, 500, "Server Error", "Content-Type: text/plain");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            if (string.IsNullOrEmpty(json)) return;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;
            switch (typeProp.GetString())
            {
                case "wikilink":
                    if (root.TryGetProperty("target", out var t))
                        WikilinkClicked?.Invoke(this, t.GetString() ?? string.Empty);
                    break;
                case "externalLink":
                    if (root.TryGetProperty("href", out var h))
                        ExternalLinkClicked?.Invoke(this, h.GetString() ?? string.Empty);
                    break;
            }
        }
        catch { /* ignore malformed bridge messages */ }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (PreviewWebView.CoreWebView2 == null) return;
        var mod = Keyboard.Modifiers;
        if (mod == ModifierKeys.Control)
        {
            if (e.Key == Key.C)
            {
                _ = PreviewWebView.CoreWebView2.ExecuteScriptAsync("document.execCommand('copy')");
                e.Handled = true;
            }
            else if (e.Key == Key.A)
            {
                _ = PreviewWebView.CoreWebView2.ExecuteScriptAsync("document.execCommand('selectAll')");
                e.Handled = true;
            }
        }
    }

    private static void OnRenderInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WikiPreviewControl control) return;
        var md = control.MarkdownText;
        var dir = control.CurrentDir ?? string.Empty;
        if (control._isReady)
            control.Render(md, dir);
        else
            control._pendingMarkdown = md;
    }

    private void Render(string? content, string currentDir)
    {
        if (PreviewWebView.CoreWebView2 == null) return;
        var text = content ?? string.Empty;
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        var dirJson = JsonSerializer.Serialize(currentDir ?? string.Empty);
        var isDark = Helpers.ThemeManager.IsSystemDarkMode() ? "true" : "false";
        _ = PreviewWebView.CoreWebView2.ExecuteScriptAsync(
            $"renderMarkdown('{base64}', {dirJson}, {isDark})");
    }

}
