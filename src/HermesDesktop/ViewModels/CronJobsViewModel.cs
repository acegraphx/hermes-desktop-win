using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class CronJobsViewModel : ObservableObject
{
    private readonly ICronBrowserService _service;
    private readonly MainViewModel _mainVm;
    private readonly ILogger<CronJobsViewModel> _logger;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<CronJob> _jobs = new();
    [ObservableProperty] private ObservableCollection<CronJob> _filteredJobs = new();
    [ObservableProperty] private CronJob? _selectedJob;

    public bool IsDetailVisible => SelectedJob is not null && !IsEditorVisible;
    public bool IsRightPanelVisible => IsDetailVisible || IsEditorVisible;

    [ObservableProperty] private bool _isEditorVisible;
    [ObservableProperty] private bool _isEditingExisting;
    [ObservableProperty] private string _editorTitle = "New cron job";
    [ObservableProperty] private string? _editorError;

    [ObservableProperty] private string _draftName = string.Empty;
    [ObservableProperty] private string _draftPrompt = string.Empty;
    [ObservableProperty] private string _draftSkills = string.Empty;
    [ObservableProperty] private string _draftModel = string.Empty;
    [ObservableProperty] private string _draftProvider = string.Empty;
    [ObservableProperty] private string _draftBaseUrl = string.Empty;
    [ObservableProperty] private string _draftCustomDelivery = string.Empty;
    [ObservableProperty] private string _draftTimezone = string.Empty;
    [ObservableProperty] private CronDeliveryPreset _draftDeliveryPreset = CronDeliveryPreset.Local;
    [ObservableProperty] private CronSchedulePreset _draftSchedulePreset = CronSchedulePreset.Daily;
    [ObservableProperty] private int _draftHour = 9;
    [ObservableProperty] private int _draftMinute = 0;
    [ObservableProperty] private int _draftWeekday = 1;
    [ObservableProperty] private int _draftDayOfMonth = 1;
    [ObservableProperty] private int _draftIntervalValue = 1;
    [ObservableProperty] private CronIntervalUnit _draftIntervalUnit = CronIntervalUnit.Hours;
    [ObservableProperty] private DateTime _draftOneTimeDate = DateTime.Now.AddHours(1);
    [ObservableProperty] private string _draftCustomExpression = string.Empty;
    [ObservableProperty] private string _draftSummary = string.Empty;

    private string? _editingJobId;

    public IReadOnlyList<CronSchedulePreset> SchedulePresets { get; } =
        Enum.GetValues<CronSchedulePreset>();

    public IReadOnlyList<CronIntervalUnit> IntervalUnits { get; } =
        Enum.GetValues<CronIntervalUnit>();

    public IReadOnlyList<CronDeliveryPreset> DeliveryPresets { get; } =
        Enum.GetValues<CronDeliveryPreset>();

    public IReadOnlyList<string> WeekdayLabels { get; } =
        CronScheduleFormatter.WeekdayPickerLabels;

    public CronJobsViewModel(
        ICronBrowserService service,
        MainViewModel mainVm,
        ILogger<CronJobsViewModel> logger)
    {
        _service = service;
        _mainVm = mainVm;
        _logger = logger;

        _ = RefreshAsync();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnJobsChanged(ObservableCollection<CronJob> value) => ApplyFilter();

    partial void OnSelectedJobChanged(CronJob? value)
    {
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(IsRightPanelVisible));
    }

    partial void OnIsEditorVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(IsRightPanelVisible));
    }

    partial void OnDraftSchedulePresetChanged(CronSchedulePreset value) => UpdateDraftSummary();
    partial void OnDraftHourChanged(int value) => UpdateDraftSummary();
    partial void OnDraftMinuteChanged(int value) => UpdateDraftSummary();
    partial void OnDraftWeekdayChanged(int value) => UpdateDraftSummary();
    partial void OnDraftDayOfMonthChanged(int value) => UpdateDraftSummary();
    partial void OnDraftIntervalValueChanged(int value) => UpdateDraftSummary();
    partial void OnDraftIntervalUnitChanged(CronIntervalUnit value) => UpdateDraftSummary();
    partial void OnDraftOneTimeDateChanged(DateTime value) => UpdateDraftSummary();
    partial void OnDraftCustomExpressionChanged(string value) => UpdateDraftSummary();

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredJobs = new ObservableCollection<CronJob>(Jobs);
            return;
        }
        FilteredJobs = new ObservableCollection<CronJob>(
            Jobs.Where(j => j.MatchesSearch(SearchQuery)));
    }

    private void UpdateDraftSummary()
    {
        var draft = BuildScheduleDraft();
        DraftSummary = draft.Summary;
    }

    private CronScheduleDraft BuildScheduleDraft() => new()
    {
        Preset = DraftSchedulePreset,
        Hour = DraftHour,
        Minute = DraftMinute,
        Weekday = DraftWeekday,
        DayOfMonth = DraftDayOfMonth,
        IntervalValue = DraftIntervalValue,
        IntervalUnit = DraftIntervalUnit,
        OneTimeDate = DraftOneTimeDate,
        CustomExpression = DraftCustomExpression
    };

    private CronJobDraft BuildJobDraft() => new()
    {
        Name = DraftName,
        Prompt = DraftPrompt,
        SkillsText = DraftSkills,
        Model = DraftModel,
        Provider = DraftProvider,
        BaseUrl = DraftBaseUrl,
        DeliveryPreset = DraftDeliveryPreset,
        CustomDeliveryTarget = DraftCustomDelivery,
        Timezone = DraftTimezone,
        Schedule = BuildScheduleDraft()
    };

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_mainVm.ActiveConnection is null) return;
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            // Preserve selection across refresh: Jobs gets a new collection with fresh
            // instances, so re-select by id to keep the detail panel stable.
            var previousId = SelectedJob?.Id ?? _editingJobId;
            var jobs = await _service.ListJobsAsync(_mainVm.ActiveConnection);
            Jobs = new ObservableCollection<CronJob>(jobs);
            if (previousId is not null)
            {
                SelectedJob = Jobs.FirstOrDefault(j => j.Id == previousId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list cron jobs");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void NewJob()
    {
        _editingJobId = null;
        SelectedJob = null;
        IsEditingExisting = false;
        EditorTitle = "New cron job";
        EditorError = null;
        ResetDraftToDefaults();
        IsEditorVisible = true;
    }

    [RelayCommand]
    private void EditJob(CronJob? job)
    {
        var target = job ?? SelectedJob;
        if (target is null) return;
        SelectedJob = target;
        LoadDraftFromJob(target);
        _editingJobId = target.Id;
        IsEditingExisting = true;
        EditorTitle = $"Edit: {target.ResolvedName}";
        EditorError = null;
        IsEditorVisible = true;
    }

    [RelayCommand]
    private void CloseDetail()
    {
        SelectedJob = null;
    }

    [RelayCommand]
    private void CancelEditor()
    {
        IsEditorVisible = false;
        EditorError = null;
        _editingJobId = null;
    }

    [RelayCommand]
    private async Task SaveJobAsync()
    {
        if (_mainVm.ActiveConnection is null) return;
        var draft = BuildJobDraft();
        if (draft.ValidationError is { } err)
        {
            EditorError = err;
            return;
        }

        try
        {
            EditorError = null;
            if (_editingJobId is null)
            {
                var newId = await _service.CreateJobAsync(_mainVm.ActiveConnection, draft);
                _editingJobId = newId;
                StatusMessage = "Created cron job.";
            }
            else
            {
                await _service.UpdateJobAsync(_mainVm.ActiveConnection, _editingJobId, draft);
                StatusMessage = "Saved cron job.";
            }
            IsEditorVisible = false;
            await RefreshAsync();
            _editingJobId = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cron job");
            EditorError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task PauseJobAsync(CronJob? job)
    {
        if (job is null || _mainVm.ActiveConnection is null) return;
        try
        {
            await _service.PauseJobAsync(_mainVm.ActiveConnection, job.Id);
            StatusMessage = $"Paused {job.ResolvedName}.";
            await RefreshAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task ResumeJobAsync(CronJob? job)
    {
        if (job is null || _mainVm.ActiveConnection is null) return;
        try
        {
            await _service.ResumeJobAsync(_mainVm.ActiveConnection, job.Id);
            StatusMessage = $"Resumed {job.ResolvedName}.";
            await RefreshAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task RunJobNowAsync(CronJob? job)
    {
        if (job is null || _mainVm.ActiveConnection is null) return;
        try
        {
            await _service.RunJobNowAsync(_mainVm.ActiveConnection, job.Id);
            StatusMessage = $"Triggered {job.ResolvedName}.";
            await RefreshAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteJobAsync(CronJob? job)
    {
        var target = job ?? SelectedJob;
        if (target is null || _mainVm.ActiveConnection is null) return;
        try
        {
            await _service.RemoveJobAsync(_mainVm.ActiveConnection, target.Id);
            StatusMessage = $"Deleted {target.ResolvedName}.";
            if (SelectedJob?.Id == target.Id) SelectedJob = null;
            await RefreshAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private void ResetDraftToDefaults()
    {
        DraftName = string.Empty;
        DraftPrompt = string.Empty;
        DraftSkills = string.Empty;
        DraftModel = string.Empty;
        DraftProvider = string.Empty;
        DraftBaseUrl = string.Empty;
        DraftCustomDelivery = string.Empty;
        DraftTimezone = string.Empty;
        DraftDeliveryPreset = CronDeliveryPreset.Local;
        DraftSchedulePreset = CronSchedulePreset.Daily;
        DraftHour = 9;
        DraftMinute = 0;
        DraftWeekday = 1;
        DraftDayOfMonth = 1;
        DraftIntervalValue = 1;
        DraftIntervalUnit = CronIntervalUnit.Hours;
        DraftOneTimeDate = DateTime.Now.AddHours(1);
        DraftCustomExpression = string.Empty;
        UpdateDraftSummary();
    }

    private void LoadDraftFromJob(CronJob job)
    {
        var d = CronJobDraft.FromJob(job);
        DraftName = d.Name;
        DraftPrompt = d.Prompt;
        DraftSkills = d.SkillsText;
        DraftModel = d.Model;
        DraftProvider = d.Provider;
        DraftBaseUrl = d.BaseUrl;
        DraftCustomDelivery = d.CustomDeliveryTarget;
        DraftTimezone = d.Timezone;
        DraftDeliveryPreset = d.DeliveryPreset;
        DraftSchedulePreset = d.Schedule.Preset;
        DraftHour = d.Schedule.Hour;
        DraftMinute = d.Schedule.Minute;
        DraftWeekday = d.Schedule.Weekday;
        DraftDayOfMonth = d.Schedule.DayOfMonth;
        DraftIntervalValue = d.Schedule.IntervalValue;
        DraftIntervalUnit = d.Schedule.IntervalUnit;
        DraftOneTimeDate = d.Schedule.OneTimeDate;
        DraftCustomExpression = d.Schedule.CustomExpression;
        UpdateDraftSummary();
    }
}
