using System.ComponentModel;
using System.Linq;
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
        // Just track the VM here — actual subscription/unsubscription happens in
        // Loaded/Unloaded so we don't end up double-subscribed across re-mount cycles.
        _vm = DataContext as TerminalViewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        _vm.PropertyChanged += OnVmPropertyChanged;

        // The singleton VM may already have tabs whose TerminalControl instances were
        // detached from a prior TerminalView's grid. Re-attach them to this grid.
        foreach (var tab in _vm.Tabs)
        {
            if (tab.TerminalControl is { } control && !TerminalHostGrid.Children.Contains(control))
                TerminalHostGrid.Children.Add(control);
        }

        SwitchToActiveTab();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Stop receiving ActiveTab change notifications — the singleton VM outlives
        // this view, and our handler references this view's TerminalHostGrid.
        if (_vm != null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        // Detach (but don't dispose) terminal controls so the singleton VM can re-attach
        // them to the next TerminalView instance. Leave EmptyState and any non-terminal
        // children in place.
        var toRemove = TerminalHostGrid.Children
            .OfType<TerminalControl>()
            .ToList();
        foreach (var control in toRemove)
            TerminalHostGrid.Children.Remove(control);
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
