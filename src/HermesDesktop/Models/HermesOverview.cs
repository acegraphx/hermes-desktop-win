using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public class HermesOverview
{
    [JsonPropertyName("home")]
    public string Home { get; set; } = string.Empty;

    [JsonPropertyName("hermes_root")]
    public string HermesRoot { get; set; } = string.Empty;

    [JsonPropertyName("session_source")]
    public string? SessionSource { get; set; }

    [JsonPropertyName("session_store")]
    public string? SessionStore { get; set; }

    [JsonPropertyName("tracked_files")]
    public List<TrackedFile> TrackedFiles { get; set; } = new();

    [JsonPropertyName("python_version")]
    public string? PythonVersion { get; set; }

    [JsonPropertyName("hermes_cli_available")]
    public bool HermesCliAvailable { get; set; }

    [JsonPropertyName("hermes_cli_path")]
    public string? HermesCliPath { get; set; }

    [JsonPropertyName("hermes_home")]
    public string? HermesHome { get; set; }

    [JsonPropertyName("profile_name")]
    public string? ProfileName { get; set; }

    [JsonPropertyName("available_profiles")]
    public List<RemoteHermesProfile> AvailableProfiles { get; set; } = new();
}

public class RemoteHermesProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    public string DisplayName => IsDefault ? "default" : Name;
}

public class TrackedFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}
