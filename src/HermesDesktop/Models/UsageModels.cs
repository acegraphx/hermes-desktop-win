using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public class UsageSummary
{
    [JsonPropertyName("total_sessions")]
    public int TotalSessions { get; set; }

    [JsonPropertyName("total_input_tokens")]
    public long TotalInputTokens { get; set; }

    [JsonPropertyName("total_output_tokens")]
    public long TotalOutputTokens { get; set; }

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    [JsonPropertyName("models")]
    public List<ModelUsage> Models { get; set; } = new();

    [JsonPropertyName("top_sessions")]
    public List<SessionUsage> TopSessions { get; set; } = new();

    [JsonPropertyName("daily_usage")]
    public List<DailyUsage> DailyUsage { get; set; } = new();
}

public class ModelUsage
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("session_count")]
    public int SessionCount { get; set; }

    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    public long TotalTokens => InputTokens + OutputTokens;
}

public class SessionUsage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }
}

public class DailyUsage
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    public long TotalTokens => InputTokens + OutputTokens;
}

public class ProfileUsageRow
{
    public string ProfileName { get; set; } = string.Empty;
    public string ProfilePath { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }
    public int SessionCount { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public long ReasoningTokens { get; set; }

    public long TotalTokens => InputTokens + OutputTokens;
    public long AllCategoriesTotal =>
        InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens + ReasoningTokens;
}

public class HostWideUsageSummary
{
    public List<ProfileUsageRow> Profiles { get; set; } = new();

    public int ReadableProfileCount => Profiles.Count(p => p.IsAvailable);
    public int TotalSessions => Profiles.Where(p => p.IsAvailable).Sum(p => p.SessionCount);
    public long TotalInputTokens => Profiles.Where(p => p.IsAvailable).Sum(p => p.InputTokens);
    public long TotalOutputTokens => Profiles.Where(p => p.IsAvailable).Sum(p => p.OutputTokens);
    public long TotalCacheReadTokens => Profiles.Where(p => p.IsAvailable).Sum(p => p.CacheReadTokens);
    public long TotalCacheWriteTokens => Profiles.Where(p => p.IsAvailable).Sum(p => p.CacheWriteTokens);
    public long TotalReasoningTokens => Profiles.Where(p => p.IsAvailable).Sum(p => p.ReasoningTokens);
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;
    public long TotalAllCategories =>
        TotalInputTokens + TotalOutputTokens + TotalCacheReadTokens + TotalCacheWriteTokens + TotalReasoningTokens;
}
