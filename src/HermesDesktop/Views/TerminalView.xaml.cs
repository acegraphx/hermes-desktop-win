using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HermesDesktop.Controls;
using HermesDesktop.ViewModels;

namespace HermesDesktop.Views;

public partial class TerminalView : UserControl
{
    private TerminalViewModel? _vm;

    public TerminalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as TerminalViewModel;

        if (_vm != null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalViewModel.ActiveTab))
        {
            Dispatcher.InvokeAsync(() => SwitchToActiveTab());
        }
    }

    private void SwitchToActiveTab()
    {
        if (_vm == null) return;

        var activeTab = _vm.ActiveTab;

        // Hide all existing terminal controls
        foreach (UIElement child in TerminalHostGrid.Children)
        {
            if (child is TerminalControl)
                child.Visibility = Visibility.Collapsed;
        }

        // Show empty state if no active tab
        EmptyState.Visibility = activeTab == null ? Visibility.Visible : Visibility.Collapsed;

        if (activeTab == null) return;

        // Create a TerminalControl for this tab if it doesn't have one yet
        if (activeTab.TerminalControl == null)
        {
            var control = new TerminalControl();
            activeTab.TerminalControl = control;

            TerminalHostGrid.Children.Add(control);

            // Attach the SSH session after the control is loaded
            control.Loaded += (_, _) =>
            {
                // Small delay to let WebView2 initialize, then attach session
                _ = AttachSessionDelayedAsync(control, activeTab);
            };
        }

        activeTab.TerminalControl.Visibility = Visibility.Visible;

        // Ensure the terminal gets keyboard focus
        FocusTerminalAsync(activeTab.TerminalControl);
    }

    private void FocusTerminalAsync(TerminalControl control)
    {
        // ContextIdle fires after layout/render — more reliable than an arbitrary delay
        Dispatcher.InvokeAsync(() => control.Focus(),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private async Task AttachSessionDelayedAsync(TerminalControl control, TerminalTabViewModel tab)
    {
        // Wait a moment for WebView2 to finish initializing
        await Task.Delay(500);
        control.AttachSession(tab.Session.Stream, tab.Session.Client);

        if (_vm is not null)
        {
            try
            {
                await control.ApplyThemeAsync(_vm.ResolveCurrentAppearance());
            }
            catch { /* best-effort theme application */ }
        }
    }

    private void TerminalHostGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Clicking in the terminal area should focus the active terminal
        if (_vm?.ActiveTab?.TerminalControl is { } control)
        {
            control.Focus();
        }
    }

    private void TabHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe
            && fe.DataContext is TerminalTabViewModel tab
            && DataContext is TerminalViewModel vm)
        {
            vm.ActiveTab = tab;
        }
    }
}
