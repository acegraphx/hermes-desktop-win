using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using HermesDesktop.Models;

namespace HermesDesktop.Services;

public class UpdateCheckService : IUpdateCheckService
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/acegraphx/hermes-desktop-win/releases/latest");
    private readonly HttpClient _httpClient;

    public UpdateCheckService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HermesDesktopWin");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<UpdateCheckResult> CheckLatestReleaseAsync(CancellationToken ct = default)
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseUri, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ct);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return new UpdateCheckResult { CurrentVersion = current, Error = "GitHub returned an empty release response." };

            var latest = release.TagName.TrimStart('v', 'V');
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                LatestVersion = latest,
                HasUpdate = IsNewerVersion(latest, current),
                ReleaseName = release.Name,
                ReleaseUrl = release.HtmlUrl,
                Notes = release.Body
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult { CurrentVersion = current, Error = ex.Message };
        }
    }

    public bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        var latest = ParseVersion(latestVersion);
        var current = ParseVersion(currentVersion);
        return latest.CompareTo(current) > 0;
    }

    private static Version ParseVersion(string value)
    {
        var text = (value ?? string.Empty).Trim().TrimStart('v', 'V');
        var suffixIndex = text.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0) text = text[..suffixIndex];
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var n) ? n : 0)
            .ToList();
        while (parts.Count < 3) parts.Add(0);
        return new Version(parts[0], parts[1], parts[2]);
    }
}
