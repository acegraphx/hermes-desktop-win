using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public class AppPreferences
{
    [JsonPropertyName("lastConnectionId")]
    public Guid? LastConnectionId { get; set; }

    [JsonPropertyName("terminalTheme")]
    public TerminalThemePreference TerminalTheme { get; set; } = new();
}
