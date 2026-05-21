using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HermesDesktop.Models;

public class KanbanBoardsResponse
{
    [JsonPropertyName("ok")] public bool? Ok { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("boards")] public List<KanbanProject> Boards { get; set; } = new();
    [JsonPropertyName("current")] public string? Current { get; set; }
    [JsonPropertyName("supports_board_management")] public bool SupportsBoardManagement { get; set; }
}

public class KanbanBoardResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("board")] public KanbanBoard Board { get; set; } = KanbanBoard.Empty;
}

public class KanbanTaskDetailResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("detail")] public KanbanTaskDetail? Detail { get; set; }
}

public class KanbanOperationResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("task_id")] public string? TaskId { get; set; }
    [JsonPropertyName("detail")] public KanbanTaskDetail? Detail { get; set; }
    [JsonPropertyName("dispatch")] public KanbanDispatchResult? Dispatch { get; set; }
}

public class KanbanBoardOperationResponse
{
    [JsonPropertyName("ok")] public bool? Ok { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("board")] public KanbanProject? Board { get; set; }
    [JsonPropertyName("boards")] public List<KanbanProject>? Boards { get; set; }
    [JsonPropertyName("current")] public string? Current { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("result")] public JsonElement? Result { get; set; }
}

public class KanbanProject
{
    public const string DefaultSlug = "default";

    [JsonPropertyName("slug")] public string Slug { get; set; } = DefaultSlug;
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("created_at")] public int? CreatedAt { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("db_path")] public string? DatabasePath { get; set; }
    [JsonPropertyName("is_current")] public bool IsCurrent { get; set; }
    [JsonPropertyName("counts")] public Dictionary<string, int> Counts { get; set; } = new();
    [JsonPropertyName("total")] public int? Total { get; set; }

    [JsonIgnore] public bool IsDefault => Slug == DefaultSlug;
    [JsonIgnore] public string ResolvedName => !string.IsNullOrWhiteSpace(Name) ? Name.Trim() : IsDefault ? "Default" : TitleizeSlug(Slug);
    [JsonIgnore] public string? ResolvedDescription => string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
    [JsonIgnore] public int TaskTotal => Total ?? Counts.Values.Sum();
    [JsonIgnore] public DateTime? CreatedDate => CreatedAt is { } v ? DateTimeOffset.FromUnixTimeSeconds(v).LocalDateTime : null;

    private static string TitleizeSlug(string slug) =>
        string.Join(" ", slug.Replace("_", "-").Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
}

public class KanbanBoard
{
    public static KanbanBoard Empty { get; } = new()
    {
        DatabasePath = "~/.hermes/kanban.db",
        HostWide = true,
        IsInitialized = false,
        HasKanbanModule = false,
        HasHermesCli = false
    };

    [JsonPropertyName("database_path")] public string DatabasePath { get; set; } = "~/.hermes/kanban.db";
    [JsonPropertyName("host_wide")] public bool HostWide { get; set; }
    [JsonPropertyName("is_initialized")] public bool IsInitialized { get; set; }
    [JsonPropertyName("has_kanban_module")] public bool HasKanbanModule { get; set; }
    [JsonPropertyName("has_hermes_cli")] public bool HasHermesCli { get; set; }
    [JsonPropertyName("dispatcher")] public KanbanDispatcherStatus? Dispatcher { get; set; }
    [JsonPropertyName("latest_event_id")] public int? LatestEventId { get; set; }
    [JsonPropertyName("tasks")] public List<KanbanTask> Tasks { get; set; } = new();
    [JsonPropertyName("assignees")] public List<KanbanAssignee> Assignees { get; set; } = new();
    [JsonPropertyName("tenants")] public List<string> Tenants { get; set; } = new();
    [JsonPropertyName("stats")] public KanbanStats? Stats { get; set; }
}

public class KanbanTask
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("assignee")] public string? Assignee { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "unknown";
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }
    [JsonPropertyName("created_at")] public int? CreatedAt { get; set; }
    [JsonPropertyName("started_at")] public int? StartedAt { get; set; }
    [JsonPropertyName("completed_at")] public int? CompletedAt { get; set; }
    [JsonPropertyName("workspace_kind")] public string WorkspaceKind { get; set; } = "scratch";
    [JsonPropertyName("workspace_path")] public string? WorkspacePath { get; set; }
    [JsonPropertyName("tenant")] public string? Tenant { get; set; }
    [JsonPropertyName("result")] public string? Result { get; set; }
    [JsonPropertyName("skills")] public List<string> Skills { get; set; } = new();
    [JsonPropertyName("spawn_failures")] public int SpawnFailures { get; set; }
    [JsonPropertyName("worker_pid")] public int? WorkerPid { get; set; }
    [JsonPropertyName("last_spawn_error")] public string? LastSpawnError { get; set; }
    [JsonPropertyName("max_runtime_seconds")] public int? MaxRuntimeSeconds { get; set; }
    [JsonPropertyName("max_retries")] public int? MaxRetries { get; set; }
    [JsonPropertyName("last_heartbeat_at")] public int? LastHeartbeatAt { get; set; }
    [JsonPropertyName("current_run_id")] public int? CurrentRunId { get; set; }
    [JsonPropertyName("parent_ids")] public List<string> ParentIds { get; set; } = new();
    [JsonPropertyName("child_ids")] public List<string> ChildIds { get; set; } = new();
    [JsonPropertyName("progress")] public KanbanTaskProgress? Progress { get; set; }
    [JsonPropertyName("comment_count")] public int CommentCount { get; set; }
    [JsonPropertyName("event_count")] public int EventCount { get; set; }
    [JsonPropertyName("run_count")] public int RunCount { get; set; }
    [JsonPropertyName("latest_event_at")] public int? LatestEventAt { get; set; }
    [JsonPropertyName("warnings")] public KanbanTaskWarnings? Warnings { get; set; }

    [JsonIgnore] public string ResolvedTitle => string.IsNullOrWhiteSpace(Title) ? Id : Title.Trim();
    [JsonIgnore] public string DisplayStatus => Status.Replace("_", " ").Trim().ToLowerInvariant() switch
    {
        "" => "Unknown",
        var s => char.ToUpperInvariant(s[0]) + s[1..]
    };
    [JsonIgnore] public string PriorityLabel => Priority > 0 ? $"P+{Priority}" : $"P{Priority}";
    [JsonIgnore] public bool HasActiveWarnings => Warnings?.HasWarnings == true;
    [JsonIgnore] public bool CanSpecify => string.Equals(Status, "triage", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public DateTime? LatestActivityDate => (LatestEventAt ?? CompletedAt ?? StartedAt ?? CreatedAt) is { } v
        ? DateTimeOffset.FromUnixTimeSeconds(v).LocalDateTime
        : null;

    public bool MatchesSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        var haystacks = new[]
        {
            Id, ResolvedTitle, Body ?? "", Assignee ?? "", Status, Tenant ?? "",
            Result ?? "", WorkspacePath ?? "", CreatedBy ?? "", Warnings?.SearchText ?? ""
        }.Concat(Skills).Concat(ParentIds).Concat(ChildIds);
        return haystacks.Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase));
    }
}

public class KanbanTaskProgress
{
    [JsonPropertyName("done")] public int Done { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
}

public class KanbanTaskWarnings
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("kinds")] public Dictionary<string, int> Kinds { get; set; } = new();
    [JsonPropertyName("latest_at")] public int? LatestAt { get; set; }
    [JsonIgnore] public bool HasWarnings => Count > 0 || Kinds.Count > 0;
    [JsonIgnore] public string SearchText => string.Join(" ", Kinds.Keys);
}

public class KanbanTaskDetail
{
    [JsonPropertyName("task")] public KanbanTask Task { get; set; } = new();
    [JsonPropertyName("parent_ids")] public List<string> ParentIds { get; set; } = new();
    [JsonPropertyName("child_ids")] public List<string> ChildIds { get; set; } = new();
    [JsonPropertyName("comments")] public List<KanbanComment> Comments { get; set; } = new();
    [JsonPropertyName("events")] public List<KanbanEvent> Events { get; set; } = new();
    [JsonPropertyName("runs")] public List<KanbanRun> Runs { get; set; } = new();
    [JsonPropertyName("worker_log")] public string? WorkerLog { get; set; }
    [JsonPropertyName("home_channels")] public List<KanbanHomeChannel> HomeChannels { get; set; } = new();
}

public class KanbanHomeChannel
{
    [JsonPropertyName("platform")] public string Platform { get; set; } = string.Empty;
    [JsonPropertyName("chat_id")] public string ChatId { get; set; } = string.Empty;
    [JsonPropertyName("thread_id")] public string ThreadId { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("subscribed")] public bool Subscribed { get; set; }
    [JsonIgnore] public string Id => $"{Platform}:{ChatId}:{ThreadId}";
    [JsonIgnore] public string ResolvedName => string.IsNullOrWhiteSpace(Name) ? "Home" : Name.Trim();
}

public class KanbanComment
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("task_id")] public string TaskId { get; set; } = string.Empty;
    [JsonPropertyName("author")] public string Author { get; set; } = string.Empty;
    [JsonPropertyName("body")] public string Body { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public int CreatedAt { get; set; }
}

public class KanbanEvent
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("task_id")] public string TaskId { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("payload")] public JsonElement? Payload { get; set; }
    [JsonPropertyName("created_at")] public int CreatedAt { get; set; }
    [JsonPropertyName("run_id")] public int? RunId { get; set; }
}

public class KanbanRun
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("task_id")] public string TaskId { get; set; } = string.Empty;
    [JsonPropertyName("profile")] public string? Profile { get; set; }
    [JsonPropertyName("step_key")] public string? StepKey { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("outcome")] public string? Outcome { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("metadata")] public JsonElement? Metadata { get; set; }
    [JsonPropertyName("worker_pid")] public int? WorkerPid { get; set; }
    [JsonPropertyName("started_at")] public int StartedAt { get; set; }
    [JsonPropertyName("ended_at")] public int? EndedAt { get; set; }
}

public class KanbanAssignee
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("on_disk")] public bool OnDisk { get; set; }
    [JsonPropertyName("counts")] public Dictionary<string, int> Counts { get; set; } = new();
}

public class KanbanStats
{
    [JsonPropertyName("by_status")] public Dictionary<string, int> ByStatus { get; set; } = new();
    [JsonPropertyName("by_assignee")] public Dictionary<string, Dictionary<string, int>> ByAssignee { get; set; } = new();
    [JsonPropertyName("oldest_ready_age_seconds")] public int? OldestReadyAgeSeconds { get; set; }
    [JsonPropertyName("now")] public int? Now { get; set; }
}

public class KanbanDispatcherStatus
{
    [JsonPropertyName("running")] public bool? Running { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public class KanbanDispatchResult
{
    [JsonPropertyName("reclaimed")] public int Reclaimed { get; set; }
    [JsonPropertyName("crashed")] public List<string> Crashed { get; set; } = new();
    [JsonPropertyName("timed_out")] public List<string> TimedOut { get; set; } = new();
    [JsonPropertyName("auto_blocked")] public List<string> AutoBlocked { get; set; } = new();
    [JsonPropertyName("promoted")] public int Promoted { get; set; }
    [JsonPropertyName("spawned")] public List<KanbanSpawnedTask> Spawned { get; set; } = new();
    [JsonPropertyName("skipped_unassigned")] public List<string> SkippedUnassigned { get; set; } = new();
}

public class KanbanSpawnedTask
{
    [JsonPropertyName("task_id")] public string TaskId { get; set; } = string.Empty;
    [JsonPropertyName("assignee")] public string Assignee { get; set; } = string.Empty;
    [JsonPropertyName("workspace")] public string Workspace { get; set; } = string.Empty;
}

public class KanbanTaskDraft
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Tenant { get; set; } = string.Empty;
    public string SkillsText { get; set; } = string.Empty;
    public string ParentIdsText { get; set; } = string.Empty;
    public string MaxRetriesText { get; set; } = string.Empty;
    public bool StartsInTriage { get; set; }

    public string NormalizedTitle => (Title ?? string.Empty).Trim();
    public string? NormalizedBody => NormalizeOptional(Body);
    public string? NormalizedAssignee => NormalizeOptional(Assignee);
    public string? NormalizedTenant => NormalizeOptional(Tenant);
    public List<string> Skills => NormalizedCommaList(SkillsText);
    public List<string> ParentIds => NormalizedIdList(ParentIdsText);
    public int? NormalizedMaxRetries
    {
        get
        {
            var value = (MaxRetriesText ?? string.Empty).Trim();
            return value.Length == 0 ? null : int.TryParse(value, out var n) ? n : null;
        }
    }
    public string? ValidationError =>
        string.IsNullOrEmpty(NormalizedTitle) ? "Task title is required." :
        !string.IsNullOrWhiteSpace(MaxRetriesText) && NormalizedMaxRetries is not > 0 ? "Max retries must be a whole number greater than 0." :
        null;

    public static List<string> NormalizedCommaList(string value) =>
        Unique((value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));

    public static List<string> NormalizedIdList(string value) =>
        Unique(Regex.Split(value ?? string.Empty, @"[\s,]+").Select(s => s.Trim()).Where(s => s.Length > 0));

    public static string ListText(IEnumerable<string> values) => string.Join(", ", values);

    private static List<string> Unique(IEnumerable<string> values)
    {
        var seen = new HashSet<string>();
        var result = new List<string>();
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                result.Add(value);
        return result;
    }

    private static string? NormalizeOptional(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}

public class KanbanBoardDraft
{
    private static readonly Regex SlugPattern = new("^[a-z0-9][a-z0-9\\-_]{0,63}$", RegexOptions.Compiled);

    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool SwitchAfterCreate { get; set; }

    public string NormalizedSlug => (Slug ?? string.Empty).Trim().ToLowerInvariant();
    public string? NormalizedName => NormalizeOptional(Name);
    public string? NormalizedDescription => NormalizeOptional(Description);
    public string? NormalizedIcon => NormalizeOptional(Icon);
    public string? NormalizedColor => NormalizeOptional(Color);
    public string? ValidationError =>
        string.IsNullOrEmpty(NormalizedSlug) ? "Board slug is required." :
        !SlugPattern.IsMatch(NormalizedSlug) ? "Board slug must be 1-64 lowercase letters, numbers, hyphens, or underscores." :
        null;

    private static string? NormalizeOptional(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
