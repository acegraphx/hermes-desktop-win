using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class ConnectionManagerViewModel : ObservableObject
{
    private readonly IConnectionStore _connectionStore;
    private readonly IRemoteHermesService _hermesService;
    private readonly SshConfigParser _sshConfigParser;
    private readonly MainViewModel _mainVm;
    private readonly ILogger<ConnectionManagerViewModel> _logger;

    [ObservableProperty]
    private ConnectionProfile? _selectedConnection;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string? _testResult;

    // Editor fields
    [ObservableProperty]
    private string _editLabel = string.Empty;

    [ObservableProperty]
    private string _editHost = string.Empty;

    [ObservableProperty]
    private string _editUser = string.Empty;

    [ObservableProperty]
    private int _editPort = 22;

    [ObservableProperty]
    private string _editKeyPath = string.Empty;

    [ObservableProperty]
    private string _editHermesProfile = string.Empty;

    [ObservableProperty]
    private string _editCustomHermesHomePath = string.Empty;

    [ObservableProperty]
    private string _editWikiPath = string.Empty;

    private Guid? _editingId;

    public ObservableCollection<ConnectionProfile> Connections => _mainVm.Connections;

    [ObservableProperty]
    private ObservableCollection<SshConfigEntry> _sshConfigEntries = new();

    [ObservableProperty]
    private bool _showSshConfigImport;

    public ConnectionManagerViewModel(
        IConnectionStore connectionStore,
        IRemoteHermesService hermesService,
        SshConfigParser sshConfigParser,
        MainViewModel mainVm,
        ILogger<ConnectionManagerViewModel> logger)
    {
        _connectionStore = connectionStore;
        _hermesService = hermesService;
        _sshConfigParser = sshConfigParser;
        _mainVm = mainVm;
        _logger = logger;
    }

    [RelayCommand]
    private void NewConnection()
    {
        _editingId = null;
        EditLabel = "";
        EditHost = "";
        EditUser = "";
        EditPort = 22;
        EditKeyPath = "";
        EditHermesProfile = "";
        EditCustomHermesHomePath = "";
        EditWikiPath = "";
        TestResult = null;
        IsEditing = true;
    }

    [RelayCommand]
    private void EditConnection(ConnectionProfile profile)
    {
        _editingId = profile.Id;
        EditLabel = profile.Label;
        EditHost = profile.SshHost;
        EditUser = profile.SshUser;
        EditPort = profile.SshPort;
        EditKeyPath = profile.SshKeyPath ?? "";
        EditHermesProfile = profile.HermesProfile ?? "";
        EditCustomHermesHomePath = profile.CustomHermesHomePath ?? "";
        EditWikiPath = profile.WikiPath ?? "";
        TestResult = null;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveConnectionAsync()
    {
        var profile = new ConnectionProfile
        {
            Id = _editingId ?? Guid.NewGuid(),
            Label = EditLabel.Trim(),
            SshHost = EditHost.Trim(),
            SshUser = EditUser.Trim(),
            SshPort = EditPort,
            SshKeyPath = string.IsNullOrWhiteSpace(EditKeyPath) ? null : EditKeyPath.Trim(),
            HermesProfile = string.IsNullOrWhiteSpace(EditHermesProfile) ||
                            string.Equals(EditHermesProfile.Trim(), "default", StringComparison.OrdinalIgnoreCase)
                ? null
                : EditHermesProfile.Trim(),
            CustomHermesHomePath = string.IsNullOrWhiteSpace(EditCustomHermesHomePath)
                ? null
                : EditCustomHermesHomePath.Trim(),
            WikiPath = string.IsNullOrWhiteSpace(EditWikiPath) ? null : EditWikiPath.Trim(),
        };

        if (!profile.IsValid)
        {
            TestResult = profile.ValidationError;
            return;
        }

        await _connectionStore.SaveConnectionAsync(profile);
        _mainVm.RefreshConnections();
        IsEditing = false;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private void OpenSshConfigImport()
    {
        var entries = _sshConfigParser.Parse();
        SshConfigEntries = new ObservableCollection<SshConfigEntry>(entries);
        ShowSshConfigImport = entries.Count > 0;
        if (entries.Count == 0)
            TestResult = "No SSH config entries found in ~/.ssh/config";
    }

    [RelayCommand]
    private async Task ImportSshConfigEntryAsync(SshConfigEntry entry)
    {
        var profile = entry.ToConnectionProfile();
        await _connectionStore.SaveConnectionAsync(profile);
        _mainVm.RefreshConnections();
        ShowSshConfigImport = false;
    }

    [RelayCommand]
    private void CloseSshConfigImport()
    {
        ShowSshConfigImport = false;
    }

    [ObservableProperty]
    private ConnectionProfile? _pendingDeleteConnection;

    [ObservableProperty]
    private bool _showDeleteConfirmation;

    [RelayCommand]
    private void RequestDeleteConnection(ConnectionProfile profile)
    {
        PendingDeleteConnection = profile;
        ShowDeleteConfirmation = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteConnectionAsync()
    {
        if (PendingDeleteConnection == null) return;
        await _connectionStore.DeleteConnectionAsync(PendingDeleteConnection.Id);
        _mainVm.RefreshConnections();
        if (_mainVm.ActiveConnection?.Id == PendingDeleteConnection.Id)
            _mainVm.ActiveConnection = null;
        ShowDeleteConfirmation = false;
        PendingDeleteConnection = null;
    }

    [RelayCommand]
    private void CancelDeleteConnection()
    {
        ShowDeleteConfirmation = false;
        PendingDeleteConnection = null;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var profile = new ConnectionProfile
        {
            Label = EditLabel.Trim(),
            SshHost = EditHost.Trim(),
            SshUser = EditUser.Trim(),
            SshPort = EditPort,
            SshKeyPath = string.IsNullOrWhiteSpace(EditKeyPath) ? null : EditKeyPath.Trim(),
            HermesProfile = string.IsNullOrWhiteSpace(EditHermesProfile) ||
                            string.Equals(EditHermesProfile.Trim(), "default", StringComparison.OrdinalIgnoreCase)
                ? null
                : EditHermesProfile.Trim(),
            CustomHermesHomePath = string.IsNullOrWhiteSpace(EditCustomHermesHomePath)
                ? null
                : EditCustomHermesHomePath.Trim(),
        };

        if (!profile.IsValid)
        {
            TestResult = "Please fill in all required fields.";
            return;
        }

        IsTesting = true;
        TestResult = null;
        try
        {
            var result = await _hermesService.TestConnectionAsync(profile);
            TestResult = result.ExitCode == 0
                ? $"Success! {result.StandardOutput.Trim()}"
                : $"Failed (exit {result.ExitCode}): {result.StandardError.Trim()}";
        }
        catch (Exception ex)
        {
            TestResult = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void ConnectTo(ConnectionProfile profile)
    {
        _mainVm.ActiveConnection = profile;
        _mainVm.SelectedSection = NavigationSection.Overview;
    }
}
