using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class WorkflowsViewModel : ObservableObject
{
    private readonly IWorkflowStore _workflowStore;
    private readonly ISkillBrowserService _skillService;
    private readonly ISshTransport _sshTransport;
    private readonly TerminalViewModel _terminalViewModel;
    private readonly MainViewModel _mainVm;
    private readonly WorkflowLaunchDiagnostics _workflowLaunchDiagnostics;
    private readonly ILogger<WorkflowsViewModel> _logger;
    private List<WorkflowPreset> _allWorkflows = new();

    [ObservableProperty] private ObservableCollection<WorkflowPreset> _workflows = new();
    [ObservableProperty] private ObservableCollection<WorkflowSkillOption> _availableSkills = new();
    [ObservableProperty] private WorkflowPreset? _selectedWorkflow;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _draftName = string.Empty;
    [ObservableProperty] private string _draftPrompt = string.Empty;

    private Guid? _editingId;

    public WorkflowsViewModel(
        IWorkflowStore workflowStore,
        ISkillBrowserService skillService,
        ISshTransport sshTransport,
        TerminalViewModel terminalViewModel,
        MainViewModel mainVm,
        WorkflowLaunchDiagnostics workflowLaunchDiagnostics,
        ILogger<WorkflowsViewModel> logger)
    {
        _workflowStore = workflowStore;
        _skillService = skillService;
        _sshTransport = sshTransport;
        _terminalViewModel = terminalViewModel;
        _mainVm = mainVm;
        _workflowLaunchDiagnostics = workflowLaunchDiagnostics;
        _logger = logger;
        _ = RefreshAsync();
    }

    partial void OnSearchQueryChanged(string value) => RebuildList();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_mainVm.ActiveConnection is null) return;
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            _allWorkflows = (await _workflowStore.LoadAsync()).ToList();
            await LoadSkillsAsync();
            RebuildList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh workflows");
            ErrorMessage = ex.Message;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void NewWorkflow()
    {
        _editingId = null;
        DraftName = string.Empty;
        DraftPrompt = string.Empty;
        foreach (var skill in AvailableSkills) skill.IsSelected = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void EditWorkflow(WorkflowPreset? workflow)
    {
        if (workflow is null) return;
        _editingId = workflow.Id;
        DraftName = workflow.Name;
        DraftPrompt = workflow.Prompt;
        var selected = workflow.AssignedSkills.Select(s => s.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in AvailableSkills) skill.IsSelected = selected.Contains(skill.Reference.RelativePath);
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveWorkflowAsync()
    {
        if (_mainVm.ActiveConnection is null) return;
        var draft = new WorkflowDraft
        {
            Name = DraftName,
            Prompt = DraftPrompt,
            SelectedSkills = AvailableSkills.Where(s => s.IsSelected).Select(s => s.Reference).ToList()
        };
        if (draft.ValidationError is { } err)
        {
            ErrorMessage = err;
            return;
        }

        var now = DateTime.UtcNow;
        var workflow = _editingId is { } id
            ? _allWorkflows.FirstOrDefault(w => w.Id == id) ?? new WorkflowPreset { Id = id, CreatedAt = now }
            : new WorkflowPreset { CreatedAt = now };

        workflow.WorkspaceScopeFingerprint = _mainVm.ActiveConnection.WorkspaceScopeFingerprint;
        workflow.Name = draft.NormalizedName;
        workflow.Prompt = draft.NormalizedPrompt;
        workflow.AssignedSkills = draft.SelectedSkills
            .GroupBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(s => s.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
        workflow.UpdatedAt = now;

        _allWorkflows.RemoveAll(w => w.Id == workflow.Id);
        _allWorkflows.Add(workflow);
        await _workflowStore.SaveAsync(_allWorkflows);
        SelectedWorkflow = workflow;
        IsEditing = false;
        RebuildList();
    }

    [RelayCommand]
    private async Task DeleteWorkflowAsync(WorkflowPreset? workflow)
    {
        if (workflow is null) return;
        _allWorkflows.RemoveAll(w => w.Id == workflow.Id);
        await _workflowStore.SaveAsync(_allWorkflows);
        if (SelectedWorkflow?.Id == workflow.Id) SelectedWorkflow = null;
        RebuildList();
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private Task RunInTerminalAsync(WorkflowPreset? workflow) =>
        LaunchAsync(workflow, WorkflowRunDestination.Terminal);

    [RelayCommand]
    private Task RunInChatAsync(WorkflowPreset? workflow) =>
        LaunchAsync(workflow, WorkflowRunDestination.Chat);

    private async Task LaunchAsync(WorkflowPreset? workflow, WorkflowRunDestination destination)
    {
        if (_mainVm.ActiveConnection is null || workflow is null) return;
        var connection = _mainVm.ActiveConnection;
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(connection.CliHermesProfileName))
            args.AddRange(["--profile", connection.CliHermesProfileName]);

        var initialInput = WorkflowDraft.NormalizePromptForLaunch(workflow.Prompt);
        if (destination == WorkflowRunDestination.Terminal)
        {
            foreach (var skill in workflow.AssignedSkills)
                args.AddRange(["--skills", skill.RelativePath]);
            args.Add("chat");
        }
        else
        {
            args.Add("--tui");
            initialInput = string.Join("\n", workflow.AssignedSkills.Select(s => "/" + s.Slug).Append(initialInput).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        _mainVm.SelectedSection = NavigationSection.Terminal;
        var commandLine = connection.RemoteHermesCommandLine(args);
        await _workflowLaunchDiagnostics.RecordWorkflowRunRequestedAsync(workflow, connection, commandLine, initialInput, destination.ToString());
        await _terminalViewModel.OpenTabWithStartupCommandAsync(
            commandLine,
            initialInput,
            $"{workflow.Name} - {destination}");
    }

    private async Task LoadSkillsAsync()
    {
        if (_mainVm.ActiveConnection is null) return;
        try
        {
            var skills = await _skillService.GetSkillsAsync(_mainVm.ActiveConnection);
            skills = await FilterLaunchableSkillsAsync(_mainVm.ActiveConnection, skills);
            AvailableSkills = new ObservableCollection<WorkflowSkillOption>(
                skills.Select(WorkflowSkillReference.FromSkill)
                    .GroupBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(s => s.Slug, StringComparer.OrdinalIgnoreCase)
                    .Select(s => new WorkflowSkillOption(s)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load workflow skills");
            AvailableSkills = new ObservableCollection<WorkflowSkillOption>();
        }
    }

    private async Task<List<SkillInfo>> FilterLaunchableSkillsAsync(ConnectionProfile connection, List<SkillInfo> discovered)
    {
        try
        {
            var result = await _sshTransport.ExecuteCommandAsync(
                connection,
                connection.RemoteServiceCommand("hermes skills list"),
                timeout: TimeSpan.FromSeconds(20));
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
                return discovered;
            var allowed = ParseLaunchableSkillIdentifiers(result.StandardOutput);
            return allowed.Count == 0
                ? discovered
                : discovered.Where(s => allowed.Contains(WorkflowSkillReference.FromSkill(s).RelativePath)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to filter workflow skills by launchable inventory");
            return discovered;
        }
    }

    private static HashSet<string> ParseLaunchableSkillIdentifiers(string output)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("│", StringComparison.Ordinal) || !line.EndsWith("│", StringComparison.Ordinal))
                continue;
            var columns = line.Split('│').Skip(1).SkipLast(1).Select(c => c.Trim()).ToArray();
            if (columns.Length != 5 || columns[0] == "Name" || columns[4] != "enabled" || columns[0].Length == 0)
                continue;
            result.Add(string.IsNullOrWhiteSpace(columns[1]) ? columns[0] : $"{columns[1]}/{columns[0]}");
        }
        return result;
    }

    private void RebuildList()
    {
        if (_mainVm.ActiveConnection is null)
        {
            Workflows.Clear();
            return;
        }
        var scope = _mainVm.ActiveConnection.WorkspaceScopeFingerprint;
        Workflows = new ObservableCollection<WorkflowPreset>(
            _allWorkflows
                .Where(w => w.WorkspaceScopeFingerprint == scope)
                .Where(w => w.MatchesSearch(SearchQuery))
                .OrderByDescending(w => w.UpdatedAt));
    }
}

public partial class WorkflowSkillOption : ObservableObject
{
    public WorkflowSkillReference Reference { get; }
    public string Label => Reference.ResolvedName;
    public string RelativePath => Reference.RelativePath;

    [ObservableProperty] private bool _isSelected;

    public WorkflowSkillOption(WorkflowSkillReference reference)
    {
        Reference = reference;
    }
}
