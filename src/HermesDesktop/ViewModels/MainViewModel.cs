using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionStore _connectionStore;
    private readonly ISshTransport _sshTransport;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly ILogger<MainViewModel> _logger;
    private NavigationSection? _pendingSection;
    private Action? _pendingNavigationAction;
    private bool _suppressPrefsSave;

    [ObservableProperty]
    private ConnectionProfile? _activeConnection;

    [ObservableProperty]
    private NavigationSection _selectedSection = NavigationSection.Connections;

    [ObservableProperty]
    private ObservableObject? _currentContentViewModel;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _windowTitle = "Hermes Desktop";

    [ObservableProperty]
    private SshConnectionState _connectionState = SshConnectionState.Disconnected;

    [ObservableProperty]
    private bool _showDiscardChangesDialog;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private bool _showUpdateDialog;

    [ObservableProperty]
    private UpdateCheckResult? _availableUpdate;

    [ObservableProperty]
    private GridLength _sidebarColumnWidth = new(220);

    private double _lastExpandedWidth = 220;

    private const double CollapsedSidebarWidth = 64;
    private const double DefaultExpandedSidebarWidth = 220;
    private const double MinExpandedSidebarWidth = 180;

    public ObservableCollection<ConnectionProfile> Connections { get; } = new();

    public List<NavigationItem> NavigationItems { get; } = new()
    {
        new() { Section = NavigationSection.Connections, Label = "Connections", IconGlyph = "\uE774" },
        new() { Section = NavigationSection.Overview, Label = "Overview", IconGlyph = "\uE80F", RequiresConnection = true },
        new() { Section = NavigationSection.Files, Label = "Files", IconGlyph = "\uE8A5", RequiresConnection = true },
        new() { Section = NavigationSection.Sessions, Label = "Sessions", IconGlyph = "\uE8BD", RequiresConnection = true },
        new() { Section = NavigationSection.Kanban, Label = "Kanban", IconGlyph = "\uE8FD", RequiresConnection = true },
        new() { Section = NavigationSection.Usage, Label = "Usage", IconGlyph = "\uE9D2", RequiresConnection = true },
        new() { Section = NavigationSection.Skills, Label = "Skills", IconGlyph = "\uE82D", RequiresConnection = true },
        new() { Section = NavigationSection.CronJobs, Label = "Cron Jobs", IconGlyph = "\uE823", RequiresConnection = true },
        new() { Section = NavigationSection.Wiki, Label = "Wiki", IconGlyph = "\uE8F1", RequiresConnection = true },
        new() { Section = NavigationSection.Terminal, Label = "Terminal", IconGlyph = "\uE756", RequiresConnection = true },
    };

    /// <summary>Check whether any editing surface has unsaved changes.</summary>
    public bool IsDirty =>
        (CurrentContentViewModel is FileEditorViewModel fe && fe.IsDirty) ||
        (CurrentContentViewModel is WikiBrowserViewModel wv && wv.IsDirty);

    private NavigationSection? CurrentEditableSection => CurrentContentViewModel switch
    {
        FileEditorViewModel => NavigationSection.Files,
        WikiBrowserViewModel => NavigationSection.Wiki,
        _ => null,
    };

    public MainViewModel(
        IServiceProvider serviceProvider,
        IConnectionStore connectionStore,
        ISshTransport sshTransport,
        IUpdateCheckService updateCheckService,
        ILogger<MainViewModel> logger)
    {
        _serviceProvider = serviceProvider;
        _connectionStore = connectionStore;
        _sshTransport = sshTransport;
        _updateCheckService = updateCheckService;
        _logger = logger;

        _sshTransport.ConnectionStateChanged += OnConnectionStateChanged;
    }

    public async Task InitializeAsync()
    {
        await _connectionStore.LoadAsync();
        Connections.Clear();
        foreach (var conn in _connectionStore.Connections)
            Connections.Add(conn);

        // Restore sidebar state. Suppress the prefs round-trip that the partial
        // change handlers would otherwise trigger during load.
        _suppressPrefsSave = true;
        try
        {
            var savedWidth = _connectionStore.Preferences.SidebarExpandedWidth;
            if (savedWidth >= MinExpandedSidebarWidth) _lastExpandedWidth = savedWidth;
            IsSidebarCollapsed = _connectionStore.Preferences.SidebarCollapsed;
            SidebarColumnWidth = IsSidebarCollapsed
                ? new GridLength(CollapsedSidebarWidth)
                : new GridLength(_lastExpandedWidth);
        }
        finally { _suppressPrefsSave = false; }

        if (_connectionStore.Preferences.LastConnectionId is { } lastId)
        {
            var last = Connections.FirstOrDefault(c => c.Id == lastId);
            if (last != null)
                ActiveConnection = last;
        }

        if (_connectionStore.Preferences.AutomaticUpdateChecks)
            _ = CheckUpdatesIfDueAsync();
    }

    private async Task CheckUpdatesIfDueAsync()
    {
        var prefs = _connectionStore.Preferences;
        if (prefs.LastAutomaticUpdateCheckAt is { } last &&
            DateTime.UtcNow - last.ToUniversalTime() < TimeSpan.FromHours(24))
            return;

        var result = await _updateCheckService.CheckLatestReleaseAsync();
        prefs.LastAutomaticUpdateCheckAt = DateTime.UtcNow;
        await _connectionStore.SavePreferencesAsync(prefs);
        if (result.HasUpdate && result.LatestVersion != prefs.LastDismissedRelease)
        {
            AvailableUpdate = result;
            ShowUpdateDialog = true;
        }
    }

    partial void OnSelectedSectionChanged(NavigationSection value)
    {
        RequestSectionNavigation(value);
    }

    partial void OnActiveConnectionChanged(ConnectionProfile? value)
    {
        if (value != null)
        {
            var prefs = _connectionStore.Preferences;
            prefs.LastConnectionId = value.Id;
            _ = _connectionStore.SavePreferencesAsync(prefs);
        }
        UpdateWindowTitle();
        NavigateToSection(SelectedSection);
    }

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        if (value)
        {
            // Snapshot the current expanded width before we shrink, so the
            // user's drag-resized width survives a collapse round-trip.
            if (SidebarColumnWidth.IsAbsolute && SidebarColumnWidth.Value >= MinExpandedSidebarWidth)
                _lastExpandedWidth = SidebarColumnWidth.Value;
            SidebarColumnWidth = new GridLength(CollapsedSidebarWidth);
        }
        else
        {
            SidebarColumnWidth = new GridLength(_lastExpandedWidth > 0 ? _lastExpandedWidth : DefaultExpandedSidebarWidth);
        }

        if (_suppressPrefsSave) return;
        var prefs = _connectionStore.Preferences;
        prefs.SidebarCollapsed = value;
        _ = _connectionStore.SavePreferencesAsync(prefs);
    }

    partial void OnSidebarColumnWidthChanged(GridLength value)
    {
        if (_suppressPrefsSave) return;
        if (IsSidebarCollapsed) return;          // don't persist the 56px collapsed snapshot
        if (!value.IsAbsolute) return;           // ignore Star/Auto forms
        if (value.Value < MinExpandedSidebarWidth) return;
        if (Math.Abs(value.Value - _lastExpandedWidth) < 1) return;
        _lastExpandedWidth = value.Value;
        var prefs = _connectionStore.Preferences;
        prefs.SidebarExpandedWidth = value.Value;
        _ = _connectionStore.SavePreferencesAsync(prefs);
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    private void RequestSectionNavigation(NavigationSection section)
    {
        // Guard: don't lose unsaved edits when leaving the current editable section.
        if (IsDirty && section != CurrentEditableSection)
        {
            _pendingSection = section;
            ShowDiscardChangesDialog = true;
            return;
        }

        NavigateToSection(section);
    }

    [RelayCommand]
    private void DiscardChangesAndNavigate()
    {
        ShowDiscardChangesDialog = false;
        if (CurrentContentViewModel is FileEditorViewModel fe)
            fe.DiscardChangesCommand.Execute(null);
        else if (CurrentContentViewModel is WikiBrowserViewModel wv)
            wv.DiscardChangesCommand.Execute(null);

        if (_pendingSection.HasValue)
        {
            NavigateToSection(_pendingSection.Value);
            _pendingSection = null;
            _pendingNavigationAction?.Invoke();
            _pendingNavigationAction = null;
        }
    }

    [RelayCommand]
    private void CancelNavigation()
    {
        ShowDiscardChangesDialog = false;
        // Revert the sidebar selection back to Files
        _pendingSection = null;
        _pendingNavigationAction = null;
        OnPropertyChanged(nameof(SelectedSection));
    }

    private void NavigateToSection(NavigationSection section)
    {
        CurrentContentViewModel = section switch
        {
            NavigationSection.Connections => _serviceProvider.GetRequiredService<ConnectionManagerViewModel>(),
            NavigationSection.Overview => _serviceProvider.GetRequiredService<OverviewViewModel>(),
            NavigationSection.Files => _serviceProvider.GetRequiredService<FileEditorViewModel>(),
            NavigationSection.Sessions => _serviceProvider.GetRequiredService<SessionBrowserViewModel>(),
            NavigationSection.Kanban => _serviceProvider.GetRequiredService<KanbanViewModel>(),
            NavigationSection.Usage => _serviceProvider.GetRequiredService<UsageBrowserViewModel>(),
            NavigationSection.Skills => _serviceProvider.GetRequiredService<SkillBrowserViewModel>(),
            NavigationSection.CronJobs => _serviceProvider.GetRequiredService<CronJobsViewModel>(),
            NavigationSection.Wiki => _serviceProvider.GetRequiredService<WikiBrowserViewModel>(),
            NavigationSection.Terminal => _serviceProvider.GetRequiredService<TerminalViewModel>(),
            _ => null
        };
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        var section = SelectedSection.ToString();
        if (ActiveConnection != null)
            WindowTitle = $"{section} - {ActiveConnection.Label} - Hermes Desktop";
        else
            WindowTitle = $"{section} - Hermes Desktop";
    }

    public void RefreshConnections()
    {
        Connections.Clear();
        foreach (var conn in _connectionStore.Connections)
            Connections.Add(conn);
    }

    [RelayCommand]
    private void SaveFileShortcut()
    {
        if (CurrentContentViewModel is FileEditorViewModel fe && fe.IsDirty)
            fe.SaveFileCommand.Execute(null);
    }

    [RelayCommand]
    private void RefreshCurrentSection()
    {
        switch (CurrentContentViewModel)
        {
            case SessionBrowserViewModel s:
                s.LoadSessionsCommand.Execute(null);
                break;
            case KanbanViewModel k:
                k.RefreshCommand.Execute(null);
                break;
            case CronJobsViewModel c:
                c.RefreshCommand.Execute(null);
                break;
        }
    }

    [RelayCommand]
    private void FindCurrentSection()
    {
        ShowStatus("Use the search box in the active section.");
    }

    [RelayCommand]
    private void NewTerminalTab()
    {
        NavigateWithIntent(NavigationSection.Terminal, () =>
        {
            if (CurrentContentViewModel is TerminalViewModel terminal)
                terminal.NewTabCommand.Execute(null);
        });
    }

    [RelayCommand]
    private void NewChat()
    {
        NavigateWithIntent(NavigationSection.Sessions, () =>
        {
            if (CurrentContentViewModel is SessionBrowserViewModel session)
                session.ChatPrompt = string.Empty;
        });
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        ShowStatus("Checking for updates...");
        var result = await _updateCheckService.CheckLatestReleaseAsync();
        if (result.Error is not null)
        {
            ShowStatus($"Update check failed: {result.Error}");
            return;
        }

        if (!result.HasUpdate)
        {
            ShowStatus("Hermes Desktop is up to date.");
            return;
        }

        AvailableUpdate = result;
        ShowUpdateDialog = true;
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        if (AvailableUpdate?.LatestVersion is { } version)
        {
            var prefs = _connectionStore.Preferences;
            prefs.LastDismissedRelease = version;
            _ = _connectionStore.SavePreferencesAsync(prefs);
        }
        ShowUpdateDialog = false;
    }

    [RelayCommand]
    private void OpenUpdateRelease()
    {
        if (AvailableUpdate?.ReleaseUrl is not { } url) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowStatus($"Unable to open release page: {ex.Message}");
        }
    }

    private void NavigateWithIntent(NavigationSection section, Action action)
    {
        if (SelectedSection == section)
        {
            action();
            return;
        }

        _pendingNavigationAction = action;
        SelectedSection = section;
        if (!ShowDiscardChangesDialog)
        {
            _pendingNavigationAction?.Invoke();
            _pendingNavigationAction = null;
        }
    }

    public void ShowStatus(string message)
    {
        StatusMessage = message;
        _ = ClearStatusAfterDelay();
    }

    private async Task ClearStatusAfterDelay()
    {
        await Task.Delay(4000);
        StatusMessage = null;
    }

    private void OnConnectionStateChanged(object? sender, SshConnectionEventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ConnectionState = e.State;
            StatusMessage = e.State switch
            {
                SshConnectionState.Connecting => "Connecting...",
                SshConnectionState.Connected => "Connected",
                SshConnectionState.Error => $"Error: {e.ErrorMessage}",
                SshConnectionState.Disconnected => "Disconnected",
                _ => null
            };
        });
    }
}
