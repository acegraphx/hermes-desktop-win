using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using HermesDesktop.ViewModels;

namespace HermesDesktop.Views;

public partial class FontManagerWindow : Window
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public FontManagerWindow()
    {
        InitializeComponent();
        LoadWindowIcon();
        StateChanged += OnStateChanged;
    }

    private void LoadWindowIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath == null) return;

            var hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
            if (hIcon != IntPtr.Zero && hIcon != (IntPtr)1)
            {
                Icon = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                DestroyIcon(hIcon);
            }
        }
        catch
        {
            // Gracefully fall back to default icon
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton != null)
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized
                ? ""
                : "";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized
                ? "Restore Down"
                : "Maximize";
        }

        // Compensate for maximized window oversizing (matches MainWindow behavior).
        RootGrid.Margin = WindowState == WindowState.Maximized
            ? new Thickness(7)
            : new Thickness(0);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is FontManagerViewModel vm)
        {
            vm.Dispose();
        }
        base.OnClosed(e);
    }
}
