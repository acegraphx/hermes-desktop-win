using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class SkillBrowserViewModel : ObservableObject
{
    private readonly IRemoteScriptExecutor _executor;
    private readonly MainViewModel _mainVm;
    private readonly ILogger<SkillBrowserViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _filterQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SkillItem> _skills = new();

    [ObservableProperty]
    private ObservableCollection<SkillItem> _filteredSkills = new();

    [ObservableProperty]
    private SkillItem? _selectedSkill;

    [ObservableProperty]
    private string? _selectedSkillMarkdown;

    [ObservableProperty]
    private bool _isLoadingDetail;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isCreating;

    [ObservableProperty]
    private string _editorContent = string.Empty;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _conflictMessage;

    [ObservableProperty]
    private string? _saveStatus;

    [ObservableProperty]
    private string _newSkillRelativePath = string.Empty;

    [ObservableProperty]
    private string? _newSkillError;

    private string? _loadedContentHash;

    public bool CanEdit => SelectedSkill != null && !SelectedSkill.IsReadOnly && !IsEditing && !IsCreating;
    public bool CanShowEditor => IsEditing || IsCreating;

    public SkillBrowserViewModel(
        IRemoteScriptExecutor executor,
        MainViewModel mainVm,
        ILogger<SkillBrowserViewModel> logger)
    {
        _executor = executor;
        _mainVm = mainVm;
        _logger = logger;

        _ = LoadSkillsAsync();
    }

    partial void OnFilterQueryChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedSkillChanged(SkillItem? value)
    {
        IsEditing = false;
        IsCreating = false;
        IsDirty = false;
        ConflictMessage = null;
        SaveStatus = null;
        EditorContent = string.Empty;
        _loadedContentHash = null;

        if (value != null)
            _ = LoadSkillDetailAsync(value);
        else
            SelectedSkillMarkdown = null;

        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanShowEditor));
    }

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanShowEditor));
    }

    partial void OnIsCreatingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanShowEditor));
    }

    partial void OnEditorContentChanged(string value)
    {
        if (IsCreating)
        {
            IsDirty = !string.IsNullOrEmpty(value);
        }
        else if (IsEditing)
        {
            IsDirty = !string.Equals(value, SelectedSkillMarkdown, StringComparison.Ordinal);
        }
    }

    private async Task LoadSkillDetailAsync(SkillItem skill)
    {
        if (_mainVm.ActiveConnection == null || skill.RelativePath == null) return;

        try
        {
            IsLoadingDetail = true;
            SelectedSkillMarkdown = null;

            var args = new Dictionary<string, object> { ["relative_path"] = skill.RelativePath };
            if (!string.IsNullOrEmpty(skill.SourceId))
                args["source_id"] = skill.SourceId;

            var json = await _executor.ExecuteRawAsync(
                _mainVm.ActiveConnection, "read_skill_detail.py", args);

            var result = System.Text.Json.JsonSerializer.Deserialize<SkillDetailResponse>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Ok == true)
            {
                SelectedSkillMarkdown = result.MarkdownContent;
                _loadedContentHash = result.ContentHash;
            }
            else
            {
                ErrorMessage = result?.Error ?? "Failed to load skill content";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load skill detail");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingDetail = false;
        }
    }

    [RelayCommand]
    private async Task LoadSkillsAsync()
    {
        if (_mainVm.ActiveConnection == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var json = await _executor.ExecuteRawAsync(
                _mainVm.ActiveConnection, "discover_skills.py");

            var result = System.Text.Json.JsonSerializer.Deserialize<SkillListResponse>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null || !result.Ok)
            {
                ErrorMessage = result?.Error ?? "Failed to load skills";
                return;
            }

            Skills = new ObservableCollection<SkillItem>(result.Items ?? new());
            ApplyFilter();
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

    [RelayCommand]
    private void EditSkill()
    {
        if (SelectedSkill == null || SelectedSkill.IsReadOnly) return;
        if (string.IsNullOrEmpty(SelectedSkillMarkdown)) return;
        EditorContent = SelectedSkillMarkdown;
        IsEditing = true;
        IsCreating = false;
        IsDirty = false;
        ConflictMessage = null;
        SaveStatus = null;
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        if (IsCreating)
        {
            IsCreating = false;
            EditorContent = string.Empty;
            NewSkillRelativePath = string.Empty;
            NewSkillError = null;
        }
        else if (IsEditing)
        {
            IsEditing = false;
            EditorContent = SelectedSkillMarkdown ?? string.Empty;
        }
        IsDirty = false;
        ConflictMessage = null;
    }

    [RelayCommand]
    private void NewSkill()
    {
        if (_mainVm.ActiveConnection == null) return;
        IsEditing = false;
        IsCreating = true;
        SelectedSkill = null;
        SelectedSkillMarkdown = null;
        ConflictMessage = null;
        NewSkillError = null;
        NewSkillRelativePath = string.Empty;
        EditorContent = "---\nname: New Skill\ndescription: Describe what this skill does.\n---\n\n";
        IsDirty = false;
        SaveStatus = null;
    }

    [RelayCommand]
    private async Task SaveSkillAsync()
    {
        if (_mainVm.ActiveConnection == null) return;
        if (!IsEditing && !IsCreating) return;
        if (IsSaving) return;

        try
        {
            IsSaving = true;
            ConflictMessage = null;
            NewSkillError = null;

            string relativePath;
            string? expectedHash;

            if (IsCreating)
            {
                relativePath = (NewSkillRelativePath ?? string.Empty).Trim().Trim('/');
                if (string.IsNullOrEmpty(relativePath))
                {
                    NewSkillError = "Pick a folder name (e.g. experiments/test).";
                    return;
                }
                expectedHash = null;
            }
            else
            {
                if (SelectedSkill?.RelativePath is null)
                    return;
                relativePath = SelectedSkill.RelativePath;
                expectedHash = _loadedContentHash;
            }

            var args = new Dictionary<string, object>
            {
                ["relative_path"] = relativePath,
                ["markdown_content"] = EditorContent ?? string.Empty,
                ["source_id"] = "local",
            };
            if (expectedHash != null)
                args["expected_content_hash"] = expectedHash;

            var json = await _executor.ExecuteRawAsync(
                _mainVm.ActiveConnection, "write_skill.py", args);

            var result = System.Text.Json.JsonSerializer.Deserialize<SkillWriteResponse>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null || !result.Ok)
            {
                var error = result?.Error ?? "Save failed.";
                if (IsCreating) NewSkillError = error;
                else ConflictMessage = error;
                return;
            }

            _loadedContentHash = result.ContentHash;
            SelectedSkillMarkdown = EditorContent;
            SaveStatus = $"Saved {DateTime.Now:HH:mm:ss}";
            IsDirty = false;

            if (IsCreating)
            {
                IsCreating = false;
                IsEditing = false;
                NewSkillRelativePath = string.Empty;
                await LoadSkillsAsync();
                var match = Skills.FirstOrDefault(s =>
                    string.Equals(s.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.SourceKind, "local", StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    SelectedSkill = match;
            }
            else
            {
                IsEditing = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save skill");
            if (IsCreating) NewSkillError = ex.Message;
            else ConflictMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(FilterQuery))
        {
            FilteredSkills = new ObservableCollection<SkillItem>(Skills);
        }
        else
        {
            var q = FilterQuery.ToLowerInvariant();
            FilteredSkills = new ObservableCollection<SkillItem>(
                Skills.Where(s =>
                    (s.Name?.ToLower().Contains(q) ?? false) ||
                    (s.Category?.ToLower().Contains(q) ?? false) ||
                    (s.Description?.ToLower().Contains(q) ?? false) ||
                    (s.SourceLabel?.ToLower().Contains(q) ?? false)));
        }
    }
}

public class SkillItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("relative_path")]
    public string? RelativePath { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("source_kind")]
    public string? SourceKind { get; set; }

    [JsonPropertyName("source_label")]
    public string? SourceLabel { get; set; }

    [JsonPropertyName("is_read_only")]
    public bool IsReadOnly { get; set; }

    [JsonPropertyName("root_path")]
    public string? RootPath { get; set; }

    public string DisplayName => Name ?? Slug ?? Id;
    public string DisplayCategory => Category ?? "Uncategorized";
    public string DisplaySource => SourceLabel ?? "Local";
    public bool IsExternal => string.Equals(SourceKind, "external", StringComparison.OrdinalIgnoreCase);
}

public class SkillListResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("items")]
    public List<SkillItem>? Items { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class SkillDetailResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("markdown_content")]
    public string? MarkdownContent { get; set; }

    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; set; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("is_read_only")]
    public bool IsReadOnly { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class SkillWriteResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("relative_path")]
    public string? RelativePath { get; set; }

    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; set; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
