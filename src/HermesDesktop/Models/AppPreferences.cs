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
}
