using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public class WikiEntry
{
    [JsonPropertyName("relative_path")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("dir")]
    public string Dir { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("mtime")]
    public double Mtime { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? StripExtension(Name) : Title!;

    private static string StripExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}

public class WikiDocument
{
    public string RelativePath { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public Dictionary<string, object?>? Frontmatter { get; init; }
    public List<string>? Tags { get; init; }
    public List<string>? OutgoingLinks { get; init; }

    public string Basename
    {
        get
        {
            var slash = RelativePath.LastIndexOfAny(new[] { '/', '\\' });
            var name = slash >= 0 ? RelativePath[(slash + 1)..] : RelativePath;
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name[..dot] : name;
        }
    }

    public string? Dir
    {
        get
        {
            var slash = RelativePath.LastIndexOfAny(new[] { '/', '\\' });
            return slash >= 0 ? RelativePath[..slash] : null;
        }
    }
}

public record WikiSaveResult(bool Success, string? ConflictMessage, WikiDocument? UpdatedDocument);

public class WikiNode
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public bool IsExpanded { get; set; } = true;
    public List<WikiNode> Children { get; } = new();
    public WikiEntry? Entry { get; init; }
}

public class WikiSearchResult
{
    [JsonPropertyName("relative_path")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("line_no")]
    public int LineNo { get; set; }

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = string.Empty;
}
