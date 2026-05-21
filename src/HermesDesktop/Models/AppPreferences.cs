using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public class AppPreferences
{
    [JsonPropertyName("lastConnectionId")]
    public Guid? LastConnectionId { get; set; }

    [JsonPropertyName("terminalTheme")]
    public TerminalThemePreference TerminalTheme { get; set; } = new();

    [JsonPropertyName("terminalFontFamily")]
    public string? TerminalFontFamily { get; set; }

    [JsonPropertyName("terminalFontSize")]
    public int? TerminalFontSize { get; set; }

    [JsonPropertyName("lastWikiRelativePathByConnection")]
    public Dictionary<string, string> LastWikiRelativePathByConnection { get; set; } = new();

    [JsonPropertyName("pinnedSessionIdsByWorkspace")]
    public Dictionary<string, List<string>> PinnedSessionIdsByWorkspace { get; set; } = new();

    [JsonPropertyName("pinnedSessions")]
    public List<PinnedSessionSnapshot> PinnedSessions { get; set; } = new();

    [JsonPropertyName("sidebarCollapsed")]
    public bool SidebarCollapsed { get; set; }

    [JsonPropertyName("sidebarExpandedWidth")]
    public double SidebarExpandedWidth { get; set; } = 220;

    [JsonPropertyName("wikiViewMode")]
    public string WikiViewMode { get; set; } = "Preview";

    [JsonPropertyName("wikiSplitEditorRatio")]
    public double WikiSplitEditorRatio { get; set; } = 0.5;

    [JsonPropertyName("wikiAutosave")]
    public bool WikiAutosave { get; set; } = true;

    [JsonPropertyName("automaticUpdateChecks")]
    public bool AutomaticUpdateChecks { get; set; } = true;

    [JsonPropertyName("lastAutomaticUpdateCheckAt")]
    public DateTime? LastAutomaticUpdateCheckAt { get; set; }

    [JsonPropertyName("lastDismissedRelease")]
    public string? LastDismissedRelease { get; set; }
}

public class PinnedSessionSnapshot
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("workspaceScopeFingerprint")]
    public string WorkspaceScopeFingerprint { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("startedAt")]
    public object? StartedAt { get; set; }

    [JsonPropertyName("lastActive")]
    public object? LastActive { get; set; }

    [JsonPropertyName("messageCount")]
    public int? MessageCount { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
