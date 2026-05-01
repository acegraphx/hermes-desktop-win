using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class UsageBrowserViewModel : ObservableObject
{
    private readonly IRemoteScriptExecutor _executor;
    private readonly IRemoteHermesService _hermes;
    private readonly MainViewModel _mainVm;
    private readonly ILogger<UsageBrowserViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _sessionCount;

    [ObservableProperty]
    private long _inputTokens;

    [ObservableProperty]
    private long _outputTokens;

    [ObservableProperty]
    private long _cacheReadTokens;

    [ObservableProperty]
    private long _cacheWriteTokens;

    [ObservableProperty]
    private long _reasoningTokens;

    public long TotalTokens => InputTokens + OutputTokens;

    public long AllTokenCategoriesTotal =>
        InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens + ReasoningTokens;

    public string AveragePerSession => SessionCount > 0
        ? $"{(InputTokens + OutputTokens) / SessionCount:N0}"
        : "0";

    [ObservableProperty]
    private ObservableCollection<UsageTopSession> _topSessions = new();

    [ObservableProperty]
    private ObservableCollection<UsageTopModel> _topModels = new();

    [ObservableProperty]
    private List<Controls.BarDataPoint> _recentSessionBars = new();

    [ObservableProperty]
    private bool _isHostWide;

    [ObservableProperty]
    private HostWideUsageSummary? _hostWideSummary;

    public bool HasMultipleProfiles => HostWideSummary != null && HostWideSummary.Profiles.Count > 1;

    public UsageBrowserViewModel(
        IRemoteScriptExecutor executor,
        IRemoteHermesService hermes,
        MainViewModel mainVm,
        ILogger<UsageBrowserViewModel> logger)
    {
        _executor = executor;
        _hermes = hermes;
        _mainVm = mainVm;
        _logger = logger;

        _ = LoadUsageAsync();
    }

    partial void OnIsHostWideChanged(bool value)
    {
        if (value)
            _ = LoadHostWideAsync();
    }

    [RelayCommand]
    private async Task LoadUsageAsync()
    {
        if (_mainVm.ActiveConnection == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            if (IsHostWide)
            {
                await LoadHostWideAsync();
                return;
            }

            var json = await _executor.ExecuteRawAsync(
                _mainVm.ActiveConnection, "query_usage.py");

            var result = System.Text.Json.JsonSerializer.Deserialize<UsageResponse>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null || !result.Ok)
            {
                ErrorMessage = result?.Error ?? "Failed to load usage data";
                return;
            }

            ApplyActiveTotals(result);

            TopSessions = new ObservableCollection<UsageTopSession>(result.TopSessions ?? new());
            TopModels = new ObservableCollection<UsageTopModel>(result.TopModels ?? new());
            OnPropertyChanged(nameof(AveragePerSession));

            RecentSessionBars = (result.RecentSessions ?? new())
                .Select(s => new Controls.BarDataPoint
                {
                    Label = s.Title ?? s.Id,
                    Value = s.TotalTokens
                })
                .ToList();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadHostWideAsync()
    {
        if (_mainVm.ActiveConnection == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var overview = await _hermes.GetOverviewAsync(_mainVm.ActiveConnection);
            var profiles = overview.AvailableProfiles?
                .Where(p => p.Exists)
                .ToList() ?? new();

            if (profiles.Count == 0)
            {
                HostWideSummary = new HostWideUsageSummary();
                OnPropertyChanged(nameof(HasMultipleProfiles));
                return;
            }

            var rows = new List<ProfileUsageRow>();
            var activeName = _mainVm.ActiveConnection.ResolvedHermesProfileName;

            foreach (var prof in profiles)
            {
                var row = new ProfileUsageRow
                {
                    ProfileName = prof.DisplayName,
                    ProfilePath = prof.Path,
                    IsActive = string.Equals(prof.IsDefault ? "default" : prof.Name, activeName, StringComparison.OrdinalIgnoreCase),
                };

                try
                {
                    var args = new Dictionary<string, object>
                    {
                        ["hermes_home"] = prof.Path
                    };
                    var json = await _executor.ExecuteRawAsync(
                        _mainVm.ActiveConnection, "query_usage.py", args);
                    var result = System.Text.Json.JsonSerializer.Deserialize<UsageResponse>(json,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (result?.Ok == true)
                    {
                        row.IsAvailable = true;
                        row.SessionCount = result.SessionCount;
                        row.InputTokens = result.InputTokens;
                        row.OutputTokens = result.OutputTokens;
                        row.CacheReadTokens = result.CacheReadTokens;
                        row.CacheWriteTokens = result.CacheWriteTokens;
                        row.ReasoningTokens = result.ReasoningTokens;
                    }
                    else
                    {
                        row.IsAvailable = false;
                        row.UnavailableReason = result?.Error ?? "Usage unavailable";
                    }
                }
                catch (Exception ex)
                {
                    row.IsAvailable = false;
                    row.UnavailableReason = ex.Message;
                    _logger.LogDebug(ex, "Host-wide usage skipped profile {Name}", prof.Name);
                }

                rows.Add(row);
            }

            var summary = new HostWideUsageSummary { Profiles = rows };
            HostWideSummary = summary;

            // Mirror aggregated totals into the existing display fields so the top cards work too.
            SessionCount = summary.TotalSessions;
            InputTokens = summary.TotalInputTokens;
            OutputTokens = summary.TotalOutputTokens;
            CacheReadTokens = summary.TotalCacheReadTokens;
            CacheWriteTokens = summary.TotalCacheWriteTokens;
            ReasoningTokens = summary.TotalReasoningTokens;
            OnPropertyChanged(nameof(TotalTokens));
            OnPropertyChanged(nameof(AllTokenCategoriesTotal));
            OnPropertyChanged(nameof(AveragePerSession));
            OnPropertyChanged(nameof(HasMultipleProfiles));

            // Top-sessions / top-models / bar chart aren't aggregated for host-wide;
            // clear them to avoid showing a stale single-profile view.
            TopSessions = new ObservableCollection<UsageTopSession>();
            TopModels = new ObservableCollection<UsageTopModel>();
            RecentSessionBars = new List<Controls.BarDataPoint>();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyActiveTotals(UsageResponse result)
    {
        SessionCount = result.SessionCount;
        InputTokens = result.InputTokens;
        OutputTokens = result.OutputTokens;
        CacheReadTokens = result.CacheReadTokens;
        CacheWriteTokens = result.CacheWriteTokens;
        ReasoningTokens = result.ReasoningTokens;
        OnPropertyChanged(nameof(TotalTokens));
        OnPropertyChanged(nameof(AllTokenCategoriesTotal));
    }
}

public class UsageResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("session_count")]
    public int SessionCount { get; set; }

    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("cache_read_tokens")]
    public long CacheReadTokens { get; set; }

    [JsonPropertyName("cache_write_tokens")]
    public long CacheWriteTokens { get; set; }

    [JsonPropertyName("reasoning_tokens")]
    public long ReasoningTokens { get; set; }

    [JsonPropertyName("top_sessions")]
    public List<UsageTopSession>? TopSessions { get; set; }

    [JsonPropertyName("top_models")]
    public List<UsageTopModel>? TopModels { get; set; }

    [JsonPropertyName("recent_sessions")]
    public List<UsageRecentSession>? RecentSessions { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class UsageTopSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }

    public string Display => $"{Title ?? Id} — {TotalTokens:N0} tokens";
}

public class UsageTopModel
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("billing_provider")]
    public string? BillingProvider { get; set; }

    [JsonPropertyName("session_count")]
    public int SessionCount { get; set; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }

    [JsonPropertyName("estimated_cost_usd")]
    public double EstimatedCostUsd { get; set; }

    public string Display
    {
        get
        {
            var cost = EstimatedCostUsd > 0 ? $", ~${EstimatedCostUsd:F2}" : "";
            var provider = !string.IsNullOrEmpty(BillingProvider) ? $" ({BillingProvider})" : "";
            return $"{Model}{provider} — {SessionCount} sessions, {TotalTokens:N0} tokens{cost}";
        }
    }
}

public class UsageRecentSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }
}
