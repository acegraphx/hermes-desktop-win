using System.Text.Json;
using System.Text.Json.Serialization;
using HermesDesktop.Models;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.Services;

public class WikiService : IWikiService
{
    private readonly IRemoteScriptExecutor _executor;
    private readonly ILogger<WikiService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public WikiService(IRemoteScriptExecutor executor, ILogger<WikiService> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public async Task<List<WikiEntry>> ListAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        var json = await _executor.ExecuteRawAsync(
            profile, "wiki_list.py",
            new() { ["wiki_path"] = profile.RemoteWikiPath }, ct);

        var response = JsonSerializer.Deserialize<WikiListResponse>(json, _jsonOptions);
        if (response == null || !response.Ok)
            throw new InvalidOperationException(response?.Error ?? "Failed to list wiki entries.");

        return response.Entries ?? new List<WikiEntry>();
    }

    public async Task<WikiDocument> ReadAsync(ConnectionProfile profile, string relativePath, CancellationToken ct = default)
    {
        var json = await _executor.ExecuteRawAsync(
            profile, "wiki_read.py",
            new()
            {
                ["wiki_path"] = profile.RemoteWikiPath,
                ["relative_path"] = relativePath,
            }, ct);

        var response = JsonSerializer.Deserialize<WikiReadResponse>(json, _jsonOptions);
        if (response == null || !response.Ok)
            throw new InvalidOperationException(response?.Error ?? "Failed to read wiki entry.");

        return new WikiDocument
        {
            RelativePath = response.RelativePath ?? relativePath,
            Content = response.Content ?? string.Empty,
            Body = response.Body ?? response.Content ?? string.Empty,
            ContentHash = response.ContentHash ?? string.Empty,
            Frontmatter = response.Frontmatter,
            Tags = response.Tags,
            OutgoingLinks = response.OutgoingLinks,
        };
    }

    public async Task<WikiSaveResult> SaveAsync(ConnectionProfile profile, WikiDocument document, string newContent, CancellationToken ct = default)
    {
        try
        {
            var json = await _executor.ExecuteRawAsync(
                profile, "wiki_write.py",
                new()
                {
                    ["wiki_path"] = profile.RemoteWikiPath,
                    ["relative_path"] = document.RelativePath,
                    ["content"] = newContent,
                    ["expected_content_hash"] = document.ContentHash,
                }, ct);

            var response = JsonSerializer.Deserialize<WikiWriteResponse>(json, _jsonOptions);
            if (response == null || !response.Ok)
                return new WikiSaveResult(false, response?.Error ?? "Save failed.", null);

            var updated = new WikiDocument
            {
                RelativePath = document.RelativePath,
                Content = newContent,
                Body = document.Body,
                ContentHash = response.ContentHash ?? string.Empty,
                Frontmatter = document.Frontmatter,
                Tags = document.Tags,
                OutgoingLinks = document.OutgoingLinks,
            };
            return new WikiSaveResult(true, null, updated);
        }
        catch (RemoteScriptException ex)
        {
            return new WikiSaveResult(false, ex.Message, null);
        }
    }

    public async Task<List<WikiSearchResult>> SearchAsync(ConnectionProfile profile, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<WikiSearchResult>();

        var json = await _executor.ExecuteRawAsync(
            profile, "wiki_search.py",
            new()
            {
                ["wiki_path"] = profile.RemoteWikiPath,
                ["query"] = query,
            }, ct);

        var response = JsonSerializer.Deserialize<WikiSearchResponse>(json, _jsonOptions);
        if (response == null || !response.Ok)
            throw new InvalidOperationException(response?.Error ?? "Search failed.");

        return response.Results ?? new List<WikiSearchResult>();
    }

    public async Task<List<string>> BacklinksAsync(ConnectionProfile profile, string pageBasename, string? selfRelativePath = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pageBasename))
            return new List<string>();

        var parameters = new Dictionary<string, object>
        {
            ["wiki_path"] = profile.RemoteWikiPath,
            ["page_basename"] = pageBasename,
        };
        if (!string.IsNullOrEmpty(selfRelativePath))
            parameters["self_relative_path"] = selfRelativePath;

        var json = await _executor.ExecuteRawAsync(profile, "wiki_backlinks.py", parameters, ct);
        var response = JsonSerializer.Deserialize<WikiBacklinksResponse>(json, _jsonOptions);
        if (response == null || !response.Ok)
            throw new InvalidOperationException(response?.Error ?? "Backlinks scan failed.");
        return response.Sources ?? new List<string>();
    }

    private class WikiListResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("root")] public string? Root { get; set; }
        [JsonPropertyName("entries")] public List<WikiEntry>? Entries { get; set; }
    }

    private class WikiReadResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("relative_path")] public string? RelativePath { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("content_hash")] public string? ContentHash { get; set; }
        [JsonPropertyName("frontmatter")] public Dictionary<string, JsonElement>? FrontmatterRaw { get; set; }
        [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
        [JsonPropertyName("outgoing_links")] public List<string>? OutgoingLinks { get; set; }

        [JsonIgnore]
        public Dictionary<string, object?>? Frontmatter
        {
            get
            {
                if (FrontmatterRaw == null) return null;
                var result = new Dictionary<string, object?>();
                foreach (var kv in FrontmatterRaw)
                    result[kv.Key] = JsonElementToObject(kv.Value);
                return result;
            }
        }

        private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var i) ? i : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => el.ToString(),
        };
    }

    private class WikiSearchResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("results")] public List<WikiSearchResult>? Results { get; set; }
    }

    private class WikiWriteResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("relative_path")] public string? RelativePath { get; set; }
        [JsonPropertyName("content_hash")] public string? ContentHash { get; set; }
    }

    private class WikiBacklinksResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("sources")] public List<string>? Sources { get; set; }
    }
}
