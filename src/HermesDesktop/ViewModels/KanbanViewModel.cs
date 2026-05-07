using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class KanbanViewModel : ObservableObject
{
    private static readonly string[] StatusOrder = ["triage", "todo", "ready", "running", "blocked", "done", "archived"];

    private readonly IKanbanBrowserService _service;
    private readonly MainViewModel _mainVm;
    private readonly ILogger<KanbanViewModel> _logger;
    private bool _suppressBoardLoad;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoadingDetail;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _includeArchived;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<KanbanProject> _boards = new();
    [ObservableProperty] private KanbanProject? _selectedBoard;
    [ObservableProperty] private KanbanBoard _board = KanbanBoard.Empty;
    [ObservableProperty] private ObservableCollection<KanbanColumn> _columns = new();
    [ObservableProperty] private KanbanTask? _selectedTask;
    [ObservableProperty] private KanbanTaskDetail? _selectedDetail;
    [ObservableProperty] private bool _showTaskDialog;
    [ObservableProperty] private bool _showBoardDialog;
    [ObservableProperty] private string? _dialogError;

    [ObservableProperty] private string _taskTitle = string.Empty;
    [ObservableProperty] private string _taskBody = string.Empty;
    [ObservableProperty] private string _taskAssignee = string.Empty;
    [ObservableProperty] private int _taskPriority;
    [ObservableProperty] private string _taskTenant = string.Empty;
    [ObservableProperty] private string _taskSkills = string.Empty;
    [ObservableProperty] private string _taskParents = string.Empty;
    [ObservableProperty] private bool _taskStartsInTriage;

    [ObservableProperty] private string _boardSlug = string.Empty;
    [ObservableProperty] private string _boardName = string.Empty;
    [ObservableProperty] private string _boardDescription = string.Empty;
    [ObservableProperty] private string _boardIcon = string.Empty;
    [ObservableProperty] private string _boardColor = string.Empty;
    [ObservableProperty] private bool _boardSwitchAfterCreate = true;

    [ObservableProperty] private string _commentText = string.Empty;
    [ObservableProperty] private string _assignAssignee = string.Empty;
    [ObservableProperty] private string _blockReason = string.Empty;
    [ObservableProperty] private string _completeResult = string.Empty;
    [ObservableProperty] private string _recoveryReason = string.Empty;
    [ObservableProperty] private string _recoverySummary = string.Empty;
    [ObservableProperty] private string _recoveryMetadataJson = string.Empty;
    [ObservableProperty] private string _editBody = string.Empty;
    [ObservableProperty] private string _editTenant = string.Empty;
    [ObservableProperty] private int _editPriority;
    [ObservableProperty] private string _editSkills = string.Empty;
    [ObservableProperty] private string _editParents = string.Empty;
    [ObservableProperty] private string _editChildren = string.Empty;
    [ObservableProperty] private bool _reclaimBeforeReassign;

    public bool HasSelectedTask => SelectedTask is not null;
    public bool SupportsBoardManagement => Boards.Count > 0;

    public KanbanViewModel(
        IKanbanBrowserService service,
        MainViewModel mainVm,
        ILogger<KanbanViewModel> logger)
    {
        _service = service;
        _mainVm = mainVm;
        _logger = logger;
        _ = RefreshAsync();
    }

    partial void OnIncludeArchivedChanged(bool value) => _ = RefreshAsync();
    partial void OnSearchQueryChanged(string value) => RebuildColumns();
    partial void OnBoardChanged(KanbanBoard value) => RebuildColumns();

    partial void OnSelectedBoardChanged(KanbanProject? value)
    {
        if (_suppressBoardLoad || value is null) return;
        _ = LoadBoardAsync(value.Slug);
    }

    partial void OnSelectedTaskChanged(KanbanTask? value)
    {
        OnPropertyChanged(nameof(HasSelectedTask));
        if (value is null)
        {
            SelectedDetail = null;
            return;
        }
        LoadActionDrafts(value);
        _ = LoadDetailAsync(value.Id);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_mainVm.ActiveConnection is null) return;
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            var previousSlug = SelectedBoard?.Slug;
            var response = await _service.ListBoardsAsync(_mainVm.ActiveConnection, IncludeArchived);
            _suppressBoardLoad = true;
            Boards = new ObservableCollection<KanbanProject>(response.Boards);
            SelectedBoard = Boards.FirstOrDefault(b => b.Slug == previousSlug)
                ?? Boards.FirstOrDefault(b => b.Slug == response.Current)
                ?? Boards.FirstOrDefault();
            _suppressBoardLoad = false;
            OnPropertyChanged(nameof(SupportsBoardManagement));
            if (SelectedBoard is { } board)
                await LoadBoardAsync(board.Slug);
        }
        catch (Exception ex)
        {
            _suppressBoardLoad = false;
            _logger.LogError(ex, "Failed to refresh Kanban");
            ErrorMessage = ex.Message;
        }
        finally { IsLoading = false; }
    }

    private async Task LoadBoardAsync(string slug)
    {
        if (_mainVm.ActiveConnection is null) return;
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            var previousTask = SelectedTask?.Id;
            Board = await _service.LoadBoardAsync(_mainVm.ActiveConnection, slug, IncludeArchived);
            SelectedTask = Board.Tasks.FirstOrDefault(t => t.Id == previousTask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Kanban board");
            ErrorMessage = ex.Message;
        }
        finally { IsLoading = false; }
    }

    private async Task LoadDetailAsync(string taskId)
    {
        if (_mainVm.ActiveConnection is null || SelectedBoard is null) return;
        try
        {
            IsLoadingDetail = true;
            SelectedDetail = await _service.LoadTaskDetailAsync(_mainVm.ActiveConnection, SelectedBoard.Slug, taskId);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoadingDetail = false; }
    }

    private void RebuildColumns()
    {
        var tasks = Board.Tasks.Where(t => t.MatchesSearch(SearchQuery)).ToList();
        var statuses = StatusOrder.Where(s => IncludeArchived || s != "archived").ToList();
        foreach (var status in tasks.Select(t => t.Status).Distinct())
            if (!statuses.Contains(status)) statuses.Add(status);
        Columns = new ObservableCollection<KanbanColumn>(
            statuses.Select(status => new KanbanColumn(status, tasks.Where(t => string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase)))));
    }

    [RelayCommand]
    private void NewTask()
    {
        DialogError = null;
        TaskTitle = TaskBody = TaskAssignee = TaskTenant = TaskSkills = TaskParents = string.Empty;
        TaskPriority = 0;
        TaskStartsInTriage = false;
        ShowTaskDialog = true;
    }

    [RelayCommand]
    private async Task SaveNewTaskAsync()
    {
        if (_mainVm.ActiveConnection is null || SelectedBoard is null) return;
        var draft = new KanbanTaskDraft
        {
            Title = TaskTitle,
            Body = TaskBody,
            Assignee = TaskAssignee,
            Priority = TaskPriority,
            Tenant = TaskTenant,
            SkillsText = TaskSkills,
            ParentIdsText = TaskParents,
            StartsInTriage = TaskStartsInTriage
        };
        if (draft.ValidationError is { } err)
        {
            DialogError = err;
            return;
        }
        try
        {
            var id = await _service.CreateTaskAsync(_mainVm.ActiveConnection, SelectedBoard.Slug, draft);
            ShowTaskDialog = false;
            StatusMessage = $"Created {id}.";
            await LoadBoardAsync(SelectedBoard.Slug);
            SelectedTask = Board.Tasks.FirstOrDefault(t => t.Id == id);
        }
        catch (Exception ex) { DialogError = ex.Message; }
    }

    [RelayCommand] private void CancelTaskDialog() => ShowTaskDialog = false;

    [RelayCommand]
    private void NewBoard()
    {
        DialogError = null;
        BoardSlug = BoardName = BoardDescription = BoardIcon = BoardColor = string.Empty;
        BoardSwitchAfterCreate = true;
        ShowBoardDialog = true;
    }

    [RelayCommand]
    private async Task SaveNewBoardAsync()
    {
        if (_mainVm.ActiveConnection is null) return;
        var draft = new KanbanBoardDraft
        {
            Slug = BoardSlug,
            Name = BoardName,
            Description = BoardDescription,
            Icon = BoardIcon,
            Color = BoardColor,
            SwitchAfterCreate = BoardSwitchAfterCreate
        };
        if (draft.ValidationError is { } err)
        {
            DialogError = err;
            return;
        }
        try
        {
            var board = await _service.CreateBoardAsync(_mainVm.ActiveConnection, draft);
            ShowBoardDialog = false;
            StatusMessage = $"Created board {board.ResolvedName}.";
            await RefreshAsync();
            SelectedBoard = Boards.FirstOrDefault(b => b.Slug == board.Slug) ?? SelectedBoard;
        }
        catch (Exception ex) { DialogError = ex.Message; }
    }

    [RelayCommand] private void CancelBoardDialog() => ShowBoardDialog = false;

    [RelayCommand]
    private async Task ArchiveBoardAsync()
    {
        if (_mainVm.ActiveConnection is null || SelectedBoard is null || SelectedBoard.IsDefault) return;
        try
        {
            await _service.ArchiveBoardAsync(_mainVm.ActiveConnection, SelectedBoard.Slug);
            StatusMessage = "Board archived.";
            await RefreshAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private Task AddCommentAsync() => MutateSelectedAsync(new() { ["action"] = "comment", ["text"] = CommentText }, clear: () => CommentText = string.Empty);

    [RelayCommand]
    private Task AssignAsync() => MutateSelectedAsync(new() { ["action"] = "assign", ["assignee"] = AssignAssignee });

    [RelayCommand]
    private Task BlockAsync() => MutateSelectedAsync(new() { ["action"] = "block", ["text"] = BlockReason });

    [RelayCommand]
    private Task UnblockAsync() => MutateSelectedAsync(new() { ["action"] = "unblock" });

    [RelayCommand]
    private Task CompleteAsync() => MutateSelectedAsync(new() { ["action"] = "complete", ["result"] = CompleteResult });

    [RelayCommand]
    private Task ReclaimAsync() => MutateSelectedAsync(new() { ["action"] = "reclaim", ["text"] = RecoveryReason });

    [RelayCommand]
    private Task ReassignAsync() => MutateSelectedAsync(new()
    {
        ["action"] = "reassign",
        ["assignee"] = AssignAssignee,
        ["text"] = RecoveryReason,
        ["reclaim_first"] = ReclaimBeforeReassign
    });

    [RelayCommand]
    private Task EditResultAsync() => MutateSelectedAsync(new()
    {
        ["action"] = "edit_result",
        ["result"] = CompleteResult,
        ["summary"] = RecoverySummary,
        ["metadata_json"] = RecoveryMetadataJson
    });

    [RelayCommand]
    private Task SaveFieldsAsync() => MutateSelectedAsync(new()
    {
        ["action"] = "update_fields",
        ["body"] = EditBody,
        ["tenant"] = EditTenant,
        ["priority"] = EditPriority,
        ["skills"] = KanbanTaskDraft.NormalizedCommaList(EditSkills)
    });

    [RelayCommand]
    private Task SaveParentsAsync() => MutateSelectedAsync(new()
    {
        ["action"] = "set_parents",
        ["parent_ids"] = KanbanTaskDraft.NormalizedIdList(EditParents)
    });

    [RelayCommand]
    private Task SaveChildrenAsync() => MutateSelectedAsync(new()
    {
        ["action"] = "set_children",
        ["child_ids"] = KanbanTaskDraft.NormalizedIdList(EditChildren)
    });

    [RelayCommand]
    private Task ArchiveTaskAsync() => MutateSelectedAsync(new() { ["action"] = "archive" });

    [RelayCommand]
    private Task DeleteTaskAsync() => MutateSelectedAsync(new() { ["action"] = "delete" }, selectTask: false);

    [RelayCommand]
    private async Task DispatchNowAsync()
    {
        if (_mainVm.ActiveConnection is null || SelectedBoard is null) return;
        try
        {
            var result = await _service.DispatchNowAsync(_mainVm.ActiveConnection, SelectedBoard.Slug);
            StatusMessage = result is null ? "Dispatcher nudged." : $"Kanban dispatch: {result.Spawned.Count} spawned, {result.Promoted} promoted.";
            await LoadBoardAsync(SelectedBoard.Slug);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task ToggleHomeSubscriptionAsync(KanbanHomeChannel? channel)
    {
        if (_mainVm.ActiveConnection is null || SelectedBoard is null || SelectedTask is null || channel is null) return;
        try
        {
            await _service.SetHomeSubscriptionAsync(_mainVm.ActiveConnection, SelectedBoard.Slug, SelectedTask.Id, channel, !channel.Subscribed);
            await LoadDetailAsync(SelectedTask.Id);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private async Task MutateSelectedAsync(Dictionary<string, object?> parameters, Action? clear = null, bool selectTask = true)
    {
        if (_mainVm.ActiveConnection is null || SelectedBoard is null || SelectedTask is null) return;
        try
        {
            var id = SelectedTask.Id;
            parameters["task_id"] = id;
            var response = await _service.MutateTaskAsync(_mainVm.ActiveConnection, SelectedBoard.Slug, parameters);
            clear?.Invoke();
            StatusMessage = response.Message ?? "Kanban updated.";
            await LoadBoardAsync(SelectedBoard.Slug);
            SelectedTask = selectTask ? Board.Tasks.FirstOrDefault(t => t.Id == (response.TaskId ?? id)) : null;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private void LoadActionDrafts(KanbanTask task)
    {
        AssignAssignee = task.Assignee ?? string.Empty;
        EditBody = task.Body ?? string.Empty;
        EditTenant = task.Tenant ?? string.Empty;
        EditPriority = task.Priority;
        EditSkills = KanbanTaskDraft.ListText(task.Skills);
        EditParents = KanbanTaskDraft.ListText(task.ParentIds);
        EditChildren = KanbanTaskDraft.ListText(task.ChildIds);
        CompleteResult = task.Result ?? string.Empty;
    }
}

public class KanbanColumn
{
    public string Status { get; }
    public string Title { get; }
    public ObservableCollection<KanbanTask> Tasks { get; }
    public int Count => Tasks.Count;

    public KanbanColumn(string status, IEnumerable<KanbanTask> tasks)
    {
        Status = status;
        Title = string.IsNullOrWhiteSpace(status) ? "Unknown" : char.ToUpperInvariant(status[0]) + status[1..].Replace("_", " ");
        Tasks = new ObservableCollection<KanbanTask>(tasks);
    }
}
