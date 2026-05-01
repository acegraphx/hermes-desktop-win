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
}
