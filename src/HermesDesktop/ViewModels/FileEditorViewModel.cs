using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class FileEditorViewModel : ObservableObject
{
    private readonly IFileEditorService _fileEditorService;
    private readonly MainViewModel _mainVm;
    private readonly ILogger<FileEditorViewModel> _logger;

    [ObservableProperty]
    private string _selectedFileName = "USER.md";

    [ObservableProperty]
    private string _editorContent = string.Empty;

    [ObservableProperty]
    private RemoteFileDocument? _loadedDocument;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _conflictMessage;

    public string[] AvailableFiles { get; } =
    {
        "USER.md",
        "MEMORY.md",
        "SOUL.md"
    };

    private static readonly Dictionary<string, string> FilePathMap = new()
    {
        ["USER.md"] = "memories/USER.md",
        ["MEMORY.md"] = "memories/MEMORY.md",
        ["SOUL.md"] = "SOUL.md",
    };

    public FileEditorViewModel(
        IFileEditorService fileEditorService,
        MainViewModel mainVm,
        ILogger<FileEditorViewModel> logger)
    {
        _fileEditorService = fileEditorService;
        _mainVm = mainVm;
        _logger = logger;

        _ = LoadFileAsync();
    }

    partial void OnEditorContentChanged(string value)
    {
        IsDirty = LoadedDocument != null && value != LoadedDocument.Content;
    }

    partial void OnSelectedFileNameChanged(string value)
    {
        _ = LoadFileAsync();
    }

    [RelayCommand]
    private async Task LoadFileAsync()
    {
        if (_mainVm.ActiveConnection == null) return;
        if (!FilePathMap.TryGetValue(SelectedFileName, out var remotePath)) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            ConflictMessage = null;

            LoadedDocument = await _fileEditorService.LoadFileAsync(
                _mainVm.ActiveConnection, remotePath);
            EditorContent = LoadedDocument.Content;
            IsDirty = false;
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
    private async Task SaveFileAsync()
    {
        if (_mainVm.ActiveConnection == null || LoadedDocument == null) return;

        try
        {
            IsSaving = true;
            ErrorMessage = null;
            ConflictMessage = null;

            var result = await _fileEditorService.SaveFileAsync(
                _mainVm.ActiveConnection, LoadedDocument, EditorContent);

            if (result.Success)
            {
                LoadedDocument = result.UpdatedDocument;
                IsDirty = false;
            }
            else
            {
                ConflictMessage = result.ConflictMessage;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        if (LoadedDocument != null)
        {
            EditorContent = LoadedDocument.Content;
            IsDirty = false;
            ConflictMessage = null;
        }
    }
}
