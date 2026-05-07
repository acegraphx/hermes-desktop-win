using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class SessionBrowserViewModel : ObservableObject
{
    private readonly IRemoteScriptExecutor _executor;
    private readonly ISessionBrowserService _sessionService;
    private readonly IHermesChatService _chatService;
    private readonly TerminalViewModel _terminalViewModel;
    private readonly MainViewModel _mainVm;
    private readonly ILogger<SessionBrowserViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<SessionItem> _sessions = new();

    [ObservableProperty]
    private SessionItem? _selectedSession;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingDetail;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _currentOffset;

    [ObservableProperty]
    private List<TranscriptMessage>? _transcriptMessages;

    [ObservableProperty]
    private string _chatPrompt = string.Empty;

    [ObservableProperty]
    private bool _autoApproveCommands;

    [ObservableProperty]
    private PendingSessionTurn? _pendingTurn;

    [ObservableProperty]
    private string? _chatError;

    private const int PageSize = 50;

    public bool HasMore => CurrentOffset + PageSize < TotalCount;

    public SessionBrowserViewModel(
        IRemoteScriptExecutor executor,
        ISessionBrowserService sessionService,
        IHermesChatService chatService,
        TerminalViewModel terminalViewModel,
        MainViewModel mainVm,
        ILogger<SessionBrowserViewModel> logger)
    {
        _executor = executor;
        _sessionService = sessionService;
        _chatService = chatService;
        _terminalViewModel = terminalViewModel;
        _mainVm = mainVm;
        _logger = logger;

        _ = LoadSessionsAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        CurrentOffset = 0;
        _ = LoadSessionsAsync();
    }

    partial void OnSelectedSessionChanged(SessionItem? value)
    {
        if (value != null)
            _ = LoadDetailAsync(value);
    }

    [RelayCommand]
    private async Task LoadSessionsAsync()
    {
        if (_mainVm.ActiveConnection == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var json = await _executor.ExecuteRawAsync(
                _mainVm.ActiveConnection, "query_sessions.py",
                new()
                {
                    ["offset"] = CurrentOffset,
                    ["limit"] = PageSize,
                    ["query"] = SearchQuery ?? ""
                });

            var result = System.Text.Json.JsonSerializer.Deserialize<SessionListResponse>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null || !result.Ok)
            {
                ErrorMessage = result?.Error ?? "Failed to load sessions";
                return;
            }

            Sessions = new ObservableCollection<SessionItem>(result.Items ?? new());
            TotalCount = result.TotalCount;
            OnPropertyChanged(nameof(HasMore));
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

    private async Task LoadDetailAsync(SessionItem session)
    {
        if (_mainVm.ActiveConnection == null) return;

        try
        {
            IsLoadingDetail = true;
            TranscriptMessages = null;

            var json = await _executor.ExecuteRawAsync(
                _mainVm.ActiveConnection, "query_session_detail.py",
                new() { ["session_id"] = session.Id });

            var result = System.Text.Json.JsonSerializer.Deserialize<SessionDetailResponse>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Ok == true)
                TranscriptMessages = result.Items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load session detail");
        }
        finally
        {
            IsLoadingDetail = false;
        }
    }

    [ObservableProperty]
    private SessionItem? _pendingDeleteSession;

    [ObservableProperty]
    private bool _showDeleteConfirmation;

    [RelayCommand]
    private void RequestDeleteSession(SessionItem session)
    {
        PendingDeleteSession = session;
        ShowDeleteConfirmation = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteSessionAsync()
    {
        if (_mainVm.ActiveConnection == null || PendingDeleteSession == null) return;

        try
        {
            await _sessionService.DeleteSessionAsync(_mainVm.ActiveConnection, PendingDeleteSession.Id);
            Sessions.Remove(PendingDeleteSession);
            TotalCount--;
            if (SelectedSession == PendingDeleteSession)
            {
                SelectedSession = null;
                TranscriptMessages = null;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            ShowDeleteConfirmation = false;
            PendingDeleteSession = null;
        }
    }

    [RelayCommand]
    private void CancelDeleteSession()
    {
        ShowDeleteConfirmation = false;
        PendingDeleteSession = null;
    }

    [RelayCommand]
    private async Task LoadNextPageAsync()
    {
        CurrentOffset += PageSize;
        await LoadSessionsAsync();
    }

    [RelayCommand]
    private async Task SendChatAsync()
    {
        if (_mainVm.ActiveConnection is null) return;
        var prompt = (ChatPrompt ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(prompt)) return;

        var sessionId = SelectedSession?.Id;
        PendingTurn = new PendingSessionTurn
        {
            SessionId = sessionId,
            Prompt = prompt,
            AutoApproveCommands = AutoApproveCommands
        };
        ChatError = null;
        try
        {
            await _chatService.SendMessageAsync(_mainVm.ActiveConnection, prompt, sessionId, AutoApproveCommands);
            ChatPrompt = string.Empty;
            await LoadSessionsAsync();
            if (sessionId is not null)
            {
                var refreshed = Sessions.FirstOrDefault(s => s.Id == sessionId);
                if (refreshed is not null)
                {
                    SelectedSession = refreshed;
                    await LoadDetailAsync(refreshed);
                }
            }
        }
        catch (Exception ex)
        {
            ChatError = ex.Message;
        }
        finally
        {
            PendingTurn = null;
        }
    }

    [RelayCommand]
    private async Task StartNewChatAsync()
    {
        SelectedSession = null;
        TranscriptMessages = null;
        await SendChatAsync();
    }

    [RelayCommand]
    private async Task ResumeInTerminalAsync()
    {
        if (_mainVm.ActiveConnection is null || SelectedSession is null) return;
        var invocation = new HermesSessionResumeInvocation(SelectedSession.Id, _mainVm.ActiveConnection);
        _mainVm.SelectedSection = NavigationSection.Terminal;
        await _terminalViewModel.OpenTabWithStartupCommandAsync(invocation.CommandLine);
    }
}

public class SessionItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("started_at")]
    public object? StartedAt { get; set; }

    [JsonPropertyName("last_active")]
    public object? LastActive { get; set; }

    [JsonPropertyName("message_count")]
    public int? MessageCount { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    public string DisplayTitle => Title ?? Id;
}

public class TranscriptMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("timestamp")]
    public object? Timestamp { get; set; }
}

public class SessionListResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("items")]
    public List<SessionItem>? Items { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class SessionDetailResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("items")]
    public List<TranscriptMessage>? Items { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
