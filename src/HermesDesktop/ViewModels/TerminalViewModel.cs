using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HermesDesktop.Controls;
using HermesDesktop.Helpers;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.ViewModels;

public partial class TerminalViewModel : ObservableObject
{
    private readonly ISshTransport _sshTransport;
    private readonly MainViewModel _mainVm;
    private readonly IConnectionStore _connectionStore;
    private readonly ILogger<TerminalViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<TerminalTabViewModel> _tabs = new();

    [ObservableProperty]
    private TerminalTabViewModel? _activeTab;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private TerminalThemeStyle _currentThemeStyle = TerminalThemeStyle.System;

    [ObservableProperty]
    private string _currentFontFamily = FontRegistry.DefaultFamily;

    [ObservableProperty]
    private int _currentFontSize = 14;

    [ObservableProperty]
    private IReadOnlyList<FontEntry> _availableFontFamilies = FontRegistry.GetAvailable();

    public IReadOnlyList<TerminalThemeStyle> AvailableThemeStyles { get; } = new[]
    {
        TerminalThemeStyle.System,
        TerminalThemeStyle.Graphite,
        TerminalThemeStyle.Evergreen,
        TerminalThemeStyle.Dusk,
        TerminalThemeStyle.Paper
    };

    public IReadOnlyList<int> AvailableFontSizes { get; } = new[]
    {
        10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24
    };

    public TerminalViewModel(
        ISshTransport sshTransport,
        MainViewModel mainVm,
        IConnectionStore connectionStore,
        ILogger<TerminalViewModel> logger)
    {
        _sshTransport = sshTransport;
        _mainVm = mainVm;
        _connectionStore = connectionStore;
        _logger = logger;

        CurrentThemeStyle = _connectionStore.Preferences.TerminalTheme.Style;

        var savedFont = _connectionStore.Preferences.TerminalFontFamily;
        var resolved = FontRegistry.Resolve(savedFont) ?? FontRegistry.Resolve(FontRegistry.DefaultFamily);
        if (resolved is not null)
        {
            CurrentFontFamily = resolved.Family;
        }
        else if (AvailableFontFamilies.Count > 0)
        {
            CurrentFontFamily = AvailableFontFamilies[0].Family;
        }

        var savedSize = _connectionStore.Preferences.TerminalFontSize;
        if (savedSize is { } size && AvailableFontSizes.Contains(size))
            CurrentFontSize = size;

        FontRegistry.Changed += OnFontRegistryChanged;

        // Singleton VM: when the active connection changes, all open SSH sessions
        // belong to the previous connection and must be torn down.
        _mainVm.PropertyChanged += OnMainVmPropertyChanged;
    }

    private void OnFontRegistryChanged(object? sender, EventArgs e)
    {
        AvailableFontFamilies = FontRegistry.GetAvailable();

        // If the active selection got removed, fall back to default.
        if (FontRegistry.Resolve(CurrentFontFamily) is null)
        {
            CurrentFontFamily = FontRegistry.DefaultFamily;
        }

        // Re-inject @font-face into every open terminal so a newly-installed font
        // becomes immediately renderable and an uninstalled one stops resolving.
        foreach (var tab in Tabs)
        {
            if (tab.TerminalControl is { } ctrl)
            {
                _ = ctrl.InstallFontFacesAsync();
            }
        }
    }

    private void OnMainVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ActiveConnection)) return;

        ActiveTab = null;
        foreach (var tab in Tabs.ToList())
        {
            try { tab.Dispose(); } catch { }
        }
        Tabs.Clear();
        ErrorMessage = null;
    }

    public TerminalThemeAppearance ResolveCurrentAppearance() =>
        new TerminalThemePreference { Style = CurrentThemeStyle }
            .ResolvedAppearance(ThemeManager.IsSystemDarkMode());

    partial void OnCurrentThemeStyleChanged(TerminalThemeStyle value)
    {
        _ = PersistAndApplyThemeAsync(value);
    }

    partial void OnCurrentFontFamilyChanged(string value)
    {
        _ = PersistAndApplyFontAsync();
    }

    partial void OnCurrentFontSizeChanged(int value)
    {
        _ = PersistAndApplyFontAsync();
    }

    private async Task PersistAndApplyThemeAsync(TerminalThemeStyle style)
    {
        try
        {
            var prefs = _connectionStore.Preferences;
            prefs.TerminalTheme = new TerminalThemePreference { Style = style };
            await _connectionStore.SavePreferencesAsync(prefs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save terminal theme preference");
        }

        var appearance = ResolveCurrentAppearance();
        foreach (var tab in Tabs)
        {
            if (tab.TerminalControl is { } ctrl)
            {
                _ = ctrl.ApplyThemeAsync(appearance);
            }
        }
    }

    private async Task PersistAndApplyFontAsync()
    {
        try
        {
            var prefs = _connectionStore.Preferences;
            prefs.TerminalFontFamily = CurrentFontFamily;
            prefs.TerminalFontSize = CurrentFontSize;
            await _connectionStore.SavePreferencesAsync(prefs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save terminal font preference");
        }

        foreach (var tab in Tabs)
        {
            if (tab.TerminalControl is { } ctrl)
            {
                _ = ctrl.ApplyFontAsync(CurrentFontFamily, CurrentFontSize);
            }
        }
    }

    [RelayCommand]
    private void SetTheme(TerminalThemeStyle? style)
    {
        if (style is null) return;
        CurrentThemeStyle = style.Value;
    }

    [RelayCommand]
    private void OpenFontManager()
    {
        var window = new Views.FontManagerWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    partial void OnActiveTabChanged(TerminalTabViewModel? value)
    {
        foreach (var tab in Tabs)
            tab.IsActive = tab == value;
    }

    [RelayCommand]
    private Task NewTabAsync() => OpenTabWithStartupCommandAsync(null);

    public async Task OpenTabWithStartupCommandAsync(string? startupCommand)
    {
        if (_mainVm.ActiveConnection == null)
        {
            ErrorMessage = "No active connection.";
            return;
        }

        try
        {
            ErrorMessage = null;
            var session = await _sshTransport.OpenShellAsync(
                _mainVm.ActiveConnection, 120, 40);

            if (!string.IsNullOrWhiteSpace(startupCommand))
            {
                try
                {
                    var command = BuildStartupShellCommand(_mainVm.ActiveConnection, startupCommand) + "\n";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(command);
                    await session.Stream.WriteAsync(bytes, 0, bytes.Length);
                    await session.Stream.FlushAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to run startup command in terminal");
                }
            }
            // Profile-aware: if a non-default Hermes profile is selected, bootstrap HERMES_HOME
            else if (!_mainVm.ActiveConnection.UsesDefaultHermesProfile)
            {
                try
                {
                    var bootstrap = _mainVm.ActiveConnection.RemoteShellBootstrapCommand + "\n";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(bootstrap);
                    await session.Stream.WriteAsync(bytes, 0, bytes.Length);
                    await session.Stream.FlushAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to bootstrap Hermes profile in shell");
                }
            }

            var title = !_mainVm.ActiveConnection.UsesDefaultHermesProfile
                ? $"{_mainVm.ActiveConnection.Label} ({_mainVm.ActiveConnection.ResolvedHermesProfileName})"
                : _mainVm.ActiveConnection.Label ?? $"Terminal {Tabs.Count + 1}";
            if (!string.IsNullOrWhiteSpace(startupCommand))
                title = $"Hermes resume - {_mainVm.ActiveConnection.Label}";

            var tab = new TerminalTabViewModel(session, title,
                _mainVm.ActiveConnection,
                _sshTransport);
            Tabs.Add(tab);
            ActiveTab = tab;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to open terminal: {ex.Message}";
            _logger.LogError(ex, "Failed to open terminal tab");
        }
    }

    private static string BuildStartupShellCommand(ConnectionProfile profile, string startupCommand)
    {
        var command = $"exec \"${{SHELL:-/bin/bash}}\" -lc {ShellQuote(startupCommand)}";
        if (profile.UsesDefaultHermesProfile) return command;
        var homeExpr = $"$HOME/.hermes/profiles/{profile.ResolvedHermesProfileName.Replace("\"", "\\\"")}";
        return $"export HERMES_HOME=\"{homeExpr}\"; {command}";
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";

    [RelayCommand]
    private void CloseTab(TerminalTabViewModel tab)
    {
        tab.Dispose();
        Tabs.Remove(tab);
        if (ActiveTab == tab)
            ActiveTab = Tabs.LastOrDefault();
    }
}

public partial class TerminalTabViewModel : ObservableObject, IDisposable
{
    private readonly ConnectionProfile _profile;
    private readonly ISshTransport _sshTransport;

    public ShellStreamSession Session { get; private set; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isDisconnected;

    [ObservableProperty]
    private int? _exitCode;

    [ObservableProperty]
    private bool _isReconnecting;

    public TerminalControl? TerminalControl { get; set; }

    public string StatusText
    {
        get
        {
            if (IsReconnecting) return "Reconnecting...";
            if (ExitCode.HasValue) return $"Shell exited (code {ExitCode.Value})";
            if (IsDisconnected) return "Connection lost";
            return "";
        }
    }

    public TerminalTabViewModel(ShellStreamSession session, string title,
        ConnectionProfile profile, ISshTransport sshTransport)
    {
        Session = session;
        _title = title;
        _profile = profile;
        _sshTransport = sshTransport;

        // Monitor connection health
        _ = MonitorConnectionAsync();
    }

    private async Task MonitorConnectionAsync()
    {
        while (!IsDisconnected)
        {
            await Task.Delay(2000);
            try
            {
                if (Session.Client == null || !Session.Client.IsConnected)
                {
                    IsDisconnected = true;
                    OnPropertyChanged(nameof(StatusText));
                    break;
                }
            }
            catch
            {
                IsDisconnected = true;
                OnPropertyChanged(nameof(StatusText));
                break;
            }
        }
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        try
        {
            IsReconnecting = true;
            OnPropertyChanged(nameof(StatusText));

            // Dispose old session
            try { Session.Stream?.Dispose(); } catch { }
            try { Session.Client?.Dispose(); } catch { }

            // Open new session
            var newSession = await _sshTransport.OpenShellAsync(_profile, 120, 40);
            Session = newSession;
            IsDisconnected = false;
            ExitCode = null;

            // Re-attach to the terminal control
            TerminalControl?.AttachSession(newSession.Stream, newSession.Client);

            OnPropertyChanged(nameof(StatusText));
        }
        catch (Exception)
        {
            IsDisconnected = true;
            OnPropertyChanged(nameof(StatusText));
        }
        finally
        {
            IsReconnecting = false;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public void Dispose()
    {
        IsDisconnected = true;
        TerminalControl?.Dispose();
        try { Session.Stream?.Dispose(); } catch { }
        try { Session.Client?.Dispose(); } catch { }
    }
}
