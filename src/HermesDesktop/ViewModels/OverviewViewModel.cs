using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class OverviewViewModel : ObservableObject
{
    private readonly IRemoteHermesService _hermesService;
    private readonly IConnectionStore _connectionStore;
    private readonly MainViewModel _mainVm;
    private readonly ILogger<OverviewViewModel> _logger;

    [ObservableProperty]
    private HermesOverview? _overview;

    public bool HasMultipleProfiles =>
        Overview?.AvailableProfiles is { Count: > 1 };

    public string ChatReadinessTitle =>
        Overview?.HermesCliAvailable == true ? "Hermes TUI ready" : "Hermes TUI needs attention";

    public string ChatReadinessDetail =>
        Overview?.HermesCliAvailable == true
            ? "Chat runs inside the real Hermes TUI; transcripts are read back from the selected host."
            : "The remote hermes CLI was not found on the prepared SSH PATH.";

    public string ChatReadinessBadge =>
        Overview?.HermesCliAvailable == true ? "TUI ready" : "Check host";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public OverviewViewModel(
        IRemoteHermesService hermesService,
        IConnectionStore connectionStore,
        MainViewModel mainVm,
        ILogger<OverviewViewModel> logger)
    {
        _hermesService = hermesService;
        _connectionStore = connectionStore;
        _mainVm = mainVm;
        _logger = logger;

        _ = LoadOverviewAsync();
    }

    partial void OnOverviewChanged(HermesOverview? value)
    {
        OnPropertyChanged(nameof(HasMultipleProfiles));
        OnPropertyChanged(nameof(ChatReadinessTitle));
        OnPropertyChanged(nameof(ChatReadinessDetail));
        OnPropertyChanged(nameof(ChatReadinessBadge));
    }

    [RelayCommand]
    private async Task SwitchProfileAsync(string? profileName)
    {
        if (_mainVm.ActiveConnection is null || profileName is null) return;
        var updated = _mainVm.ActiveConnection.WithHermesProfile(profileName);
        await _connectionStore.SaveConnectionAsync(updated);
        _mainVm.RefreshConnections();
        _mainVm.ActiveConnection = updated;
        await LoadOverviewAsync();
    }

    [RelayCommand]
    private async Task LoadOverviewAsync()
    {
        if (_mainVm.ActiveConnection == null)
        {
            ErrorMessage = "No active connection. Select a connection first.";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            Overview = await _hermesService.GetOverviewAsync(_mainVm.ActiveConnection);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to load overview");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
