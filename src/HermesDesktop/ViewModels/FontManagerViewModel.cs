using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Helpers;
using Serilog;

namespace HermesDesktop.ViewModels;

public partial class FontManagerViewModel : ObservableObject, IDisposable
{
    public ObservableCollection<FontManagerItemViewModel> Items { get; } = new();

    public FontManagerViewModel()
    {
        BuildItems();
        FontRegistry.Changed += OnFontRegistryChanged;
    }

    private void OnFontRegistryChanged(object? sender, EventArgs e)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.BeginInvoke(NotifyAllItems);
        }
        else
        {
            NotifyAllItems();
        }
    }

    private void NotifyAllItems()
    {
        foreach (var item in Items)
            item.NotifyStateChanged();
    }

    private void BuildItems()
    {
        Items.Clear();

        foreach (var entry in FontRegistry.GetAvailable().Where(e => e.Source == FontSource.Bundled))
        {
            Items.Add(new FontManagerItemViewModel(bundledEntry: entry));
        }

        foreach (var c in FontCatalog.Entries)
        {
            Items.Add(new FontManagerItemViewModel(catalogEntry: c));
        }
    }

    public void Dispose()
    {
        FontRegistry.Changed -= OnFontRegistryChanged;
        foreach (var item in Items) item.Dispose();
    }
}

public partial class FontManagerItemViewModel : ObservableObject, IDisposable
{
    private readonly FontEntry? _bundledEntry;
    private readonly FontCatalogEntry? _catalogEntry;

    public FontManagerItemViewModel(FontEntry bundledEntry)
    {
        _bundledEntry = bundledEntry;
        _catalogEntry = null;
    }

    public FontManagerItemViewModel(FontCatalogEntry catalogEntry)
    {
        _catalogEntry = catalogEntry;
        _bundledEntry = null;
    }

    public string DisplayName =>
        _bundledEntry?.DisplayName ?? _catalogEntry?.DisplayName ?? string.Empty;

    public string Family =>
        _bundledEntry?.Family ?? _catalogEntry?.Family ?? string.Empty;

    public FontFamily WpfFamily
    {
        get
        {
            if (_bundledEntry is not null) return _bundledEntry.WpfFamily;
            // For catalog items, prefer the actual installed font's WpfFamily so
            // the preview shows in its own face once downloaded.
            if (_catalogEntry is not null)
            {
                var resolved = FontRegistry.Resolve(_catalogEntry.Family);
                return resolved?.WpfFamily ?? new FontFamily(FontRegistry.DefaultFamily);
            }
            return new FontFamily(FontRegistry.DefaultFamily);
        }
    }

    public string SourceLabel
    {
        get
        {
            if (_bundledEntry is not null) return "Bundled";
            return IsInstalled ? "Installed" : "Available";
        }
    }

    public string? LicenseLabel
    {
        get
        {
            if (_catalogEntry is null) return null;
            var sizeKb = _catalogEntry.SizeBytes / 1024.0;
            return $"{_catalogEntry.License} · {sizeKb:F0} KB";
        }
    }

    public bool IsInstalled => _catalogEntry is not null && FontCatalog.IsInstalled(_catalogEntry);
    public bool IsCatalogItem => _catalogEntry is not null;
    public bool CanInstall => IsCatalogItem && !IsInstalled && !IsBusy;
    public bool CanUninstall => IsCatalogItem && IsInstalled && !IsBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(WpfFamily));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanUninstall));
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (_catalogEntry is null) return;

        IsBusy = true;
        ErrorMessage = null;
        Progress = 0;
        try
        {
            await FontCatalog.InstallAsync(
                _catalogEntry,
                new Progress<double>(p => Progress = p));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Log.Error(ex, "FontManagerItem install failed for {Id}", _catalogEntry.Id);
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private void Uninstall()
    {
        if (_catalogEntry is null) return;
        try
        {
            FontCatalog.Uninstall(_catalogEntry);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Log.Error(ex, "FontManagerItem uninstall failed for {Id}", _catalogEntry.Id);
        }
    }

    public void Dispose()
    {
        // Nothing to clean up — subscriptions live on the parent VM.
    }
}
