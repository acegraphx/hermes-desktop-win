using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public class UpdateCheckResult
{
    public bool HasUpdate { get; init; }
    public string? LatestVersion { get; init; }
    public string? CurrentVersion { get; init; }
    public string? ReleaseName { get; init; }
    public string? ReleaseUrl { get; init; }
    public string? Notes { get; init; }
    public string? Error { get; init; }
}

public class GitHubReleaseResponse
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
}
