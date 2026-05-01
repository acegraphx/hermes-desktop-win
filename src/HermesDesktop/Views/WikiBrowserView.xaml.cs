using System.Windows.Controls;
using HermesDesktop.Models;
using HermesDesktop.ViewModels;

namespace HermesDesktop.Views;

public partial class WikiBrowserView : UserControl
{
    public WikiBrowserView()
    {
        InitializeComponent();
    }

    private void OnWikilinkClicked(object? sender, string target)
    {
        if (DataContext is WikiBrowserViewModel vm)
            vm.TryNavigateToBasename(target);
    }

    private void OnTreeNodeSelected(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: WikiNode node } &&
            DataContext is WikiBrowserViewModel vm)
        {
            vm.SelectedNode = node;
        }
    }

    private void OnSearchHitDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: WikiSearchResult hit } &&
            DataContext is WikiBrowserViewModel vm)
        {
            vm.OpenSearchHitCommand.Execute(hit);
        }
    }

    private void OnEditorSaveRequested(object? sender, EventArgs e)
    {
        if (DataContext is WikiBrowserViewModel vm && vm.SaveCommand.CanExecute(null))
            vm.SaveCommand.Execute(null);
    }

    private void OnSplitDragCompleted(object? sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DataContext is not WikiBrowserViewModel vm) return;
        var editor = EditorRow.ActualHeight;
        var preview = PreviewRow.ActualHeight;
        var total = editor + preview;
        if (total <= 0) return;
        vm.UpdateSplitRatio(editor / total);
    }
}
