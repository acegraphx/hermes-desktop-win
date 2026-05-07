using System.Text;
using System.Windows;
using System.Windows.Controls;
using HermesDesktop.Helpers;

namespace HermesDesktop.Controls;

public partial class MarkdownControl : UserControl
{
    private bool _isReady;
    private string? _pendingContent;

    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.Register(nameof(MarkdownText), typeof(string), typeof(MarkdownControl),
            new PropertyMetadata(null, OnMarkdownTextChanged));

    public string? MarkdownText
    {
        get => (string?)GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    public MarkdownControl()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isReady) return;

        try
        {
            await MarkdownWebView.EnsureCoreWebView2Async();

            var assetsPath = AppAssets.ResolveAssetFolder("Markdown", "markdown.html");
            if (assetsPath != null)
            {
                MarkdownWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "hermes.markdown", assetsPath,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            }

            MarkdownWebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _isReady = true;
                if (_pendingContent != null)
                    RenderContent(_pendingContent);
            };

            MarkdownWebView.CoreWebView2.Navigate("https://hermes.markdown/markdown.html");
        }
        catch
        {
            // WebView2 not available - fall back silently
        }
    }

    private static void OnMarkdownTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownControl control)
        {
            var text = e.NewValue as string;
            if (control._isReady)
                control.RenderContent(text);
            else
                control._pendingContent = text;
        }
    }

    private void RenderContent(string? content)
    {
        if (string.IsNullOrEmpty(content) || MarkdownWebView.CoreWebView2 == null)
            return;

        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        var isDark = Helpers.ThemeManager.IsSystemDarkMode() ? "true" : "false";
        _ = MarkdownWebView.CoreWebView2.ExecuteScriptAsync(
            $"renderMarkdown('{base64}', {isDark})");
    }

}
