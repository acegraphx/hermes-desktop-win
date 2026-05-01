using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class WikiBrowserViewModel : ObservableObject
{
    private readonly IWikiService _wikiService;
    private readonly MainViewModel _mainVm;
    private readonly IConnectionStore _connectionStore;
    private readonly ILogger<WikiBrowserViewModel> _logger;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _autosaveCts;
    private bool _suppressViewModePersist;

    [ObservableProperty]
    private string? _wikiRoot;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingDocument;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private ObservableCollection<WikiEntry> _entries = new();

    [ObservableProperty]
    private ObservableCollection<WikiEntry> _filteredEntries = new();

    [ObservableProperty]
    private ObservableCollection<WikiNode> _rootNodes = new();

    [ObservableProperty]
    private WikiEntry? _selectedEntry;

    [ObservableProperty]
    private WikiNode? _selectedNode;

    [ObservableProperty]
    private WikiDocument? _openedDocument;

    [ObservableProperty]
    private string? _previewMarkdown;

    [ObservableProperty]
    private string _currentDir = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FrontmatterPair> _frontmatterPairs = new();

    [ObservableProperty]
    private ObservableCollection<string> _currentTags = new();

    [ObservableProperty]
    private string _editorContent = string.Empty;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _conflictMessage;

    [ObservableProperty]
    private bool _showDiscardDialog;

    [ObservableProperty]
    private ObservableCollection<string> _backlinks = new();

    [ObservableProperty]
    private bool _isLoadingBacklinks;

    [ObservableProperty]
    private bool _backlinksExpanded;

    [ObservableProperty]
    private bool _backlinksLoaded;

    private WikiEntry? _previousEntry;
    private WikiEntry? _pendingEntry;
    private bool _isReverting;

    [ObservableProperty]
    private WikiViewMode _viewMode = WikiViewMode.Preview;

    [ObservableProperty]
    private GridLength _editorRowHeight = new(0);

    [ObservableProperty]
    private GridLength _previewRowHeight = new(1, GridUnitType.Star);

    [ObservableProperty]
    private GridLength _splitterRowHeight = new(0);

    [ObservableProperty]
    private string? _saveStatus;

    private double _splitEditorRatio = 0.5;

    [ObservableProperty]
    private string _filterQuery = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private ObservableCollection<WikiSearchResult> _searchResults = new();

    public WikiAssetResolver AssetResolver { get; }
    public ConnectionProfile? ActiveConnection => _mainVm.ActiveConnection;

    public bool IsFilterActive => !string.IsNullOrWhiteSpace(FilterQuery);
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchQuery);
    public bool IsEditorVisible => ViewMode is WikiViewMode.Edit or WikiViewMode.Split;
    public bool IsPreviewVisible => ViewMode is WikiViewMode.Preview or WikiViewMode.Split;

    public WikiBrowserViewModel(
        IWikiService wikiService,
        MainViewModel mainVm,
        WikiAssetResolver assetResolver,
        IConnectionStore connectionStore,
        ILogger<WikiBrowserViewModel> logger)
    {
        _wikiService = wikiService;
        _mainVm = mainVm;
        _connectionStore = connectionStore;
        _logger = logger;
        AssetResolver = assetResolver;

        WikiRoot = _mainVm.ActiveConnection?.RemoteWikiPath;

        var prefs = _connectionStore.Preferences;
        _splitEditorRatio = Math.Clamp(prefs.WikiSplitEditorRatio, 0.1, 0.9);
        _suppressViewModePersist = true;
        try
        {
            ViewMode = ParseViewMode(prefs.WikiViewMode);
        }
        finally
        {
            _suppressViewModePersist = false;
        }

        _ = LoadEntriesAsync();
    }

    private static WikiViewMode ParseViewMode(string? raw) =>
        raw switch
        {
            "Edit" => WikiViewMode.Edit,
            "Split" => WikiViewMode.Split,
            _ => WikiViewMode.Preview,
        };

    partial void OnSelectedEntryChanged(WikiEntry? value)
    {
        if (_isReverting) return;

        if (IsDirty && value != _previousEntry)
        {
            _pendingEntry = value;
            _isReverting = true;
            try { SelectedEntry = _previousEntry; }
            finally { _isReverting = false; }
            ShowDiscardDialog = true;
            return;
        }

        _previousEntry = value;
        if (value != null)
            _ = OpenEntryAsync(value);
        else
        {
            OpenedDocument = null;
            PreviewMarkdown = null;
            CurrentDir = string.Empty;
        }
    }

    [RelayCommand]
    private void ConfirmDiscardAndNavigate()
    {
        ShowDiscardDialog = false;
        if (OpenedDocument != null)
        {
            EditorContent = OpenedDocument.Content;
            IsDirty = false;
        }
        if (_pendingEntry != null)
        {
            var target = _pendingEntry;
            _pendingEntry = null;
            _previousEntry = target;
            _isReverting = true;
            try { SelectedEntry = target; }
            finally { _isReverting = false; }
            _ = OpenEntryAsync(target);
        }
    }

    [RelayCommand]
    private void CancelDiscardNavigation()
    {
        ShowDiscardDialog = false;
        _pendingEntry = null;
    }

    partial void OnSelectedNodeChanged(WikiNode? value)
    {
        if (value?.Entry != null)
            SelectedEntry = value.Entry;
    }

    partial void OnFilterQueryChanged(string value)
    {
        ApplyFilter();
        OnPropertyChanged(nameof(IsFilterActive));
    }

    partial void OnSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearchActive));
        _ = RunSearchAsync(value);
    }

    [RelayCommand]
    private async Task LoadEntriesAsync()
    {
        if (_mainVm.ActiveConnection == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var list = await _wikiService.ListAsync(_mainVm.ActiveConnection);
            Entries = new ObservableCollection<WikiEntry>(list);
            BuildTree();
            ApplyFilter();

            if (Entries.Count == 0)
                ErrorMessage = "No markdown files found in this wiki.";
            else
                RestoreLastOpenedPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load wiki entries");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildTree()
    {
        var root = new WikiNode { Name = string.Empty, Path = string.Empty, IsDirectory = true };
        var dirs = new Dictionary<string, WikiNode> { [string.Empty] = root };

        foreach (var entry in Entries)
        {
            var dirPath = entry.Dir ?? string.Empty;
            var parent = EnsureDir(dirPath, dirs);
            parent.Children.Add(new WikiNode
            {
                Name = entry.DisplayName,
                Path = entry.RelativePath,
                IsDirectory = false,
                Entry = entry,
            });
        }

        SortRecursive(root);
        RootNodes = new ObservableCollection<WikiNode>(root.Children);
    }

    private static WikiNode EnsureDir(string dirPath, Dictionary<string, WikiNode> dirs)
    {
        if (dirs.TryGetValue(dirPath, out var existing))
            return existing;

        var parts = dirPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = dirs[string.Empty];
        var soFar = string.Empty;
        foreach (var part in parts)
        {
            soFar = soFar.Length == 0 ? part : $"{soFar}/{part}";
            if (!dirs.TryGetValue(soFar, out var node))
            {
                node = new WikiNode { Name = part, Path = soFar, IsDirectory = true };
                current.Children.Add(node);
                dirs[soFar] = node;
            }
            current = node;
        }
        return current;
    }

    private static void SortRecursive(WikiNode node)
    {
        node.Children.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var child in node.Children)
            if (child.IsDirectory) SortRecursive(child);
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(FilterQuery))
        {
            FilteredEntries = new ObservableCollection<WikiEntry>(Entries);
            return;
        }
        var q = FilterQuery.Trim().ToLowerInvariant();
        FilteredEntries = new ObservableCollection<WikiEntry>(
            Entries.Where(e =>
                e.RelativePath.ToLowerInvariant().Contains(q) ||
                (e.Title?.ToLowerInvariant().Contains(q) ?? false) ||
                (e.Tags?.Any(t => t.ToLowerInvariant().Contains(q)) ?? false)));
    }

    private async Task RunSearchAsync(string query)
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        if (string.IsNullOrWhiteSpace(query) || _mainVm.ActiveConnection == null)
        {
            SearchResults = new ObservableCollection<WikiSearchResult>();
            IsSearching = false;
            return;
        }

        try
        {
            await Task.Delay(250, cts.Token);
            IsSearching = true;
            var hits = await _wikiService.SearchAsync(_mainVm.ActiveConnection, query, cts.Token);
            if (cts.IsCancellationRequested) return;
            SearchResults = new ObservableCollection<WikiSearchResult>(hits);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (_searchCts == cts) IsSearching = false;
        }
    }

    [RelayCommand]
    private void OpenSearchHit(WikiSearchResult? hit)
    {
        if (hit == null) return;
        var entry = Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, hit.RelativePath, StringComparison.OrdinalIgnoreCase));
        if (entry != null)
            SelectedEntry = entry;
    }

    private async Task OpenEntryAsync(WikiEntry entry)
    {
        if (_mainVm.ActiveConnection == null) return;

        try
        {
            IsLoadingDocument = true;
            ErrorMessage = null;
            PreviewMarkdown = null;

            var doc = await _wikiService.ReadAsync(_mainVm.ActiveConnection, entry.RelativePath);
            OpenedDocument = doc;
            PreviewMarkdown = doc.Body;
            EditorContent = doc.Content;
            IsDirty = false;
            ConflictMessage = null;
            SaveStatus = null;
            CurrentDir = doc.Dir ?? string.Empty;
            FrontmatterPairs = new ObservableCollection<FrontmatterPair>(BuildPairs(doc.Frontmatter));
            CurrentTags = new ObservableCollection<string>(doc.Tags ?? new List<string>());
            Backlinks = new ObservableCollection<string>();
            BacklinksLoaded = false;
            if (BacklinksExpanded)
                _ = LoadBacklinksAsync();

            PrefetchImages(doc.Body, CurrentDir);
            _ = SaveLastOpenedPageAsync(entry.RelativePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open wiki entry {Path}", entry.RelativePath);
            ErrorMessage = ex.Message;
            OpenedDocument = null;
            PreviewMarkdown = null;
            FrontmatterPairs = new ObservableCollection<FrontmatterPair>();
            CurrentTags = new ObservableCollection<string>();
        }
        finally
        {
            IsLoadingDocument = false;
        }
    }

    private static IEnumerable<FrontmatterPair> BuildPairs(Dictionary<string, object?>? fm)
    {
        if (fm == null) yield break;
        foreach (var kv in fm)
        {
            if (string.Equals(kv.Key, "tags", StringComparison.OrdinalIgnoreCase))
                continue;  // tags shown as chips separately
            yield return new FrontmatterPair(kv.Key, FormatValue(kv.Value));
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        IEnumerable<object?> list => string.Join(", ", list.Select(FormatValue)),
        _ => value.ToString() ?? string.Empty,
    };

    [RelayCommand]
    private void FilterByTag(string? tag)
    {
        if (!string.IsNullOrWhiteSpace(tag))
            FilterQuery = tag;
    }

    partial void OnEditorContentChanged(string value)
    {
        IsDirty = OpenedDocument != null && value != OpenedDocument.Content;
        _ = SchedulePreviewUpdateAsync(value);
        if (IsDirty && _connectionStore.Preferences.WikiAutosave)
            _ = ScheduleAutosaveAsync();
    }

    private async Task ScheduleAutosaveAsync()
    {
        _autosaveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _autosaveCts = cts;
        try
        {
            await Task.Delay(1500, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (!IsDirty || IsSaving) return;
            await SaveAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async Task SchedulePreviewUpdateAsync(string value)
    {
        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        try
        {
            await Task.Delay(300, cts.Token);
            if (cts.IsCancellationRequested) return;
            PreviewMarkdown = StripFrontmatter(value ?? string.Empty);
        }
        catch (OperationCanceledException) { }
    }

    private static readonly Regex _frontmatterRegex = new(
        @"^---\s*\r?\n[\s\S]*?\r?\n---\s*\r?\n?", RegexOptions.Compiled);

    private static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---")) return content;
        var match = _frontmatterRegex.Match(content);
        return match.Success ? content[match.Length..] : content;
    }

    private static readonly Regex _imageRegex = new(
        @"!\[[^\]]*\]\(([^)\s]+)", RegexOptions.Compiled);

    private void PrefetchImages(string body, string currentDir)
    {
        var profile = _mainVm.ActiveConnection;
        if (profile == null || string.IsNullOrEmpty(body)) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in _imageRegex.Matches(body))
        {
            var src = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(src)) continue;
            if (Regex.IsMatch(src, @"^(https?:|data:|blob:)", RegexOptions.IgnoreCase)) continue;
            var rel = src.StartsWith('/')
                ? src.TrimStart('/')
                : (string.IsNullOrEmpty(currentDir) ? src : $"{currentDir}/{src}");
            if (seen.Add(rel))
                _ = AssetResolver.GetAsync(profile, rel);
        }
    }

    private async Task SaveLastOpenedPageAsync(string relativePath)
    {
        var profile = _mainVm.ActiveConnection;
        if (profile == null || _connectionStore == null) return;
        try
        {
            var prefs = _connectionStore.Preferences;
            prefs.LastWikiRelativePathByConnection[profile.Id.ToString()] = relativePath;
            await _connectionStore.SavePreferencesAsync(prefs);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist last wiki page");
        }
    }

    private void RestoreLastOpenedPage()
    {
        var profile = _mainVm.ActiveConnection;
        if (profile == null || _connectionStore == null) return;
        if (!_connectionStore.Preferences.LastWikiRelativePathByConnection.TryGetValue(
                profile.Id.ToString(), out var rel) || string.IsNullOrWhiteSpace(rel))
            return;
        var match = Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, rel, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            SelectedEntry = match;
    }

    partial void OnViewModeChanged(WikiViewMode value)
    {
        OnPropertyChanged(nameof(IsEditorVisible));
        OnPropertyChanged(nameof(IsPreviewVisible));

        switch (value)
        {
            case WikiViewMode.Edit:
                EditorRowHeight = new GridLength(1, GridUnitType.Star);
                PreviewRowHeight = new GridLength(0);
                SplitterRowHeight = new GridLength(0);
                break;
            case WikiViewMode.Preview:
                EditorRowHeight = new GridLength(0);
                PreviewRowHeight = new GridLength(1, GridUnitType.Star);
                SplitterRowHeight = new GridLength(0);
                break;
            case WikiViewMode.Split:
                var ratio = Math.Clamp(_splitEditorRatio, 0.1, 0.9);
                EditorRowHeight = new GridLength(ratio, GridUnitType.Star);
                PreviewRowHeight = new GridLength(1.0 - ratio, GridUnitType.Star);
                SplitterRowHeight = new GridLength(6);
                break;
        }

        if (!_suppressViewModePersist)
            _ = PersistViewModeAsync(value);
    }

    public void UpdateSplitRatio(double editorFraction)
    {
        var clamped = Math.Clamp(editorFraction, 0.1, 0.9);
        if (Math.Abs(clamped - _splitEditorRatio) < 0.005) return;
        _splitEditorRatio = clamped;
        _ = PersistSplitRatioAsync(clamped);
    }

    private async Task PersistViewModeAsync(WikiViewMode mode)
    {
        try
        {
            var prefs = _connectionStore.Preferences;
            prefs.WikiViewMode = mode.ToString();
            await _connectionStore.SavePreferencesAsync(prefs);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist wiki view mode");
        }
    }

    private async Task PersistSplitRatioAsync(double ratio)
    {
        try
        {
            var prefs = _connectionStore.Preferences;
            prefs.WikiSplitEditorRatio = ratio;
            await _connectionStore.SavePreferencesAsync(prefs);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist wiki split ratio");
        }
    }

    [RelayCommand]
    private void SetEditMode() => ViewMode = WikiViewMode.Edit;

    [RelayCommand]
    private void SetSplitMode() => ViewMode = WikiViewMode.Split;

    [RelayCommand]
    private void SetPreviewMode() => ViewMode = WikiViewMode.Preview;

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (_mainVm.ActiveConnection == null || OpenedDocument == null) return;
        if (!IsDirty) return;

        try
        {
            IsSaving = true;
            ConflictMessage = null;

            var result = await _wikiService.SaveAsync(
                _mainVm.ActiveConnection, OpenedDocument, EditorContent ?? string.Empty);

            if (result.Success && result.UpdatedDocument != null)
            {
                OpenedDocument = result.UpdatedDocument;
                IsDirty = false;
                SaveStatus = $"Saved {DateTime.Now:HH:mm:ss}";
                _mainVm.ShowStatus("Saved");
            }
            else
            {
                ConflictMessage = result.ConflictMessage ?? "Save failed.";
                SaveStatus = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save failed");
            ConflictMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        if (OpenedDocument == null) return;
        EditorContent = OpenedDocument.Content;
        IsDirty = false;
        ConflictMessage = null;
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (SelectedEntry != null)
        {
            ConflictMessage = null;
            await OpenEntryAsync(SelectedEntry);
        }
    }

    partial void OnBacklinksExpandedChanged(bool value)
    {
        if (value && !BacklinksLoaded && OpenedDocument != null)
            _ = LoadBacklinksAsync();
    }

    [RelayCommand]
    private async Task LoadBacklinksAsync()
    {
        if (_mainVm.ActiveConnection == null || OpenedDocument == null) return;
        try
        {
            IsLoadingBacklinks = true;
            var sources = await _wikiService.BacklinksAsync(
                _mainVm.ActiveConnection,
                OpenedDocument.Basename,
                OpenedDocument.RelativePath);
            Backlinks = new ObservableCollection<string>(sources);
            BacklinksLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backlinks load failed");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingBacklinks = false;
        }
    }

    [RelayCommand]
    private void OpenBacklink(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        var match = Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            SelectedEntry = match;
    }

    public void TryNavigateToBasename(string basename)
    {
        if (string.IsNullOrWhiteSpace(basename)) return;
        var trimmed = basename.Trim();
        // Strip a trailing .md if the wikilink author included it.
        var bare = StripMd(trimmed);

        // 1. Same directory takes priority.
        if (!string.IsNullOrEmpty(CurrentDir))
        {
            var sameDir = Entries.FirstOrDefault(e =>
                string.Equals(e.Dir, CurrentDir, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(StripMd(e.Name), bare, StringComparison.OrdinalIgnoreCase));
            if (sameDir != null) { SelectedEntry = sameDir; return; }
        }

        // 2. Exact relative path match (e.g. [[notes/foo]]).
        var byRelative = Entries.FirstOrDefault(e =>
            string.Equals(StripMd(e.RelativePath), bare, StringComparison.OrdinalIgnoreCase));
        if (byRelative != null) { SelectedEntry = byRelative; return; }

        // 3. Frontmatter title match.
        var byTitle = Entries.FirstOrDefault(e =>
            !string.IsNullOrWhiteSpace(e.Title) &&
            string.Equals(e.Title, bare, StringComparison.OrdinalIgnoreCase));
        if (byTitle != null) { SelectedEntry = byTitle; return; }

        // 4. Bare-basename match anywhere.
        var matches = Entries
            .Where(e => string.Equals(StripMd(e.Name), bare, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 1)
        {
            SelectedEntry = matches[0];
        }
        else if (matches.Count > 1)
        {
            _mainVm.ShowStatus($"Multiple matches for [[{bare}]] — using {matches[0].RelativePath}");
            SelectedEntry = matches[0];
        }
        else
        {
            ErrorMessage = $"Wikilink target not found: {bare}";
        }
    }

    private static string StripMd(string fileName)
    {
        return fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^3] : fileName;
    }
}

public record FrontmatterPair(string Key, string Value);

public enum WikiViewMode
{
    Edit,
    Split,
    Preview
}
