using HermesDesktop.Models;

namespace HermesDesktop.Services;

public class KanbanBrowserService : IKanbanBrowserService
{
    private readonly IRemoteScriptExecutor _executor;

    public KanbanBrowserService(IRemoteScriptExecutor executor)
    {
        _executor = executor;
    }

    public async Task<KanbanBoardsResponse> ListBoardsAsync(ConnectionProfile profile, bool includeArchived, CancellationToken ct = default)
    {
        var response = await _executor.ExecuteAsync<KanbanBoardsResponse>(
            profile, "kanban.py", BasePayload(profile, "list_boards", includeArchived), ct);
        if (response.Ok == false) throw new InvalidOperationException(response.Error ?? "Unable to load Kanban boards.");
        return response;
    }

    public async Task<KanbanBoard> LoadBoardAsync(ConnectionProfile profile, string boardSlug, bool includeArchived, CancellationToken ct = default)
    {
        var payload = BasePayload(profile, "load_board", includeArchived);
        payload["board_slug"] = boardSlug;
        var response = await _executor.ExecuteAsync<KanbanBoardResponse>(profile, "kanban.py", payload, ct);
        if (!response.Ok) throw new InvalidOperationException(response.Error ?? "Unable to load Kanban board.");
        return response.Board;
    }

    public async Task<KanbanTaskDetail> LoadTaskDetailAsync(ConnectionProfile profile, string boardSlug, string taskId, CancellationToken ct = default)
    {
        var payload = BasePayload(profile, "task_detail", false);
        payload["board_slug"] = boardSlug;
        payload["task_id"] = taskId;
        var response = await _executor.ExecuteAsync<KanbanTaskDetailResponse>(profile, "kanban.py", payload, ct);
        if (!response.Ok || response.Detail is null)
            throw new InvalidOperationException(response.Error ?? "Unable to load Kanban task.");
        return response.Detail;
    }

    public async Task<KanbanProject> CreateBoardAsync(ConnectionProfile profile, KanbanBoardDraft draft, CancellationToken ct = default)
    {
        var payload = BasePayload(profile, "create_board", false);
        payload["slug"] = draft.NormalizedSlug;
        payload["name"] = draft.NormalizedName!;
        payload["description"] = draft.NormalizedDescription!;
        payload["icon"] = draft.NormalizedIcon!;
        payload["color"] = draft.NormalizedColor!;
        payload["switch_after_create"] = draft.SwitchAfterCreate;
        var response = await _executor.ExecuteAsync<KanbanBoardOperationResponse>(profile, "kanban.py", payload, ct);
        if (response.Ok == false || response.Board is null)
            throw new InvalidOperationException(response.Error ?? "Unable to create Kanban board.");
        return response.Board;
    }

    public async Task ArchiveBoardAsync(ConnectionProfile profile, string boardSlug, CancellationToken ct = default)
    {
        var payload = BasePayload(profile, "archive_board", false);
        payload["board_slug"] = boardSlug;
        var response = await _executor.ExecuteAsync<KanbanBoardOperationResponse>(profile, "kanban.py", payload, ct);
        if (response.Ok == false) throw new InvalidOperationException(response.Error ?? "Unable to archive Kanban board.");
    }

    public async Task<string> CreateTaskAsync(ConnectionProfile profile, string boardSlug, KanbanTaskDraft draft, CancellationToken ct = default)
    {
        var payload = MutationPayload(profile, boardSlug, "create");
        payload["title"] = draft.NormalizedTitle;
        payload["body"] = draft.NormalizedBody!;
        payload["assignee"] = draft.NormalizedAssignee!;
        payload["priority"] = draft.Priority;
        payload["tenant"] = draft.NormalizedTenant!;
        payload["skills"] = draft.Skills;
        payload["triage"] = draft.StartsInTriage;
        payload["parent_ids"] = draft.ParentIds;
        payload["max_retries"] = draft.NormalizedMaxRetries!;
        var response = await ExecuteMutationAsync(profile, payload, ct);
        return response.TaskId ?? throw new InvalidOperationException("Remote did not return a task ID.");
    }

    public Task<KanbanOperationResponse> MutateTaskAsync(
        ConnectionProfile profile,
        string boardSlug,
        Dictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        var payload = MutationPayload(profile, boardSlug, parameters.TryGetValue("action", out var action) ? Convert.ToString(action) ?? "" : "");
        foreach (var pair in parameters)
            payload[pair.Key] = pair.Value!;
        return ExecuteMutationAsync(profile, payload, ct);
    }

    public async Task SetHomeSubscriptionAsync(ConnectionProfile profile, string boardSlug, string taskId, KanbanHomeChannel channel, bool subscribed, CancellationToken ct = default)
    {
        var payload = BasePayload(profile, "home_subscription", false);
        payload["board_slug"] = boardSlug;
        payload["task_id"] = taskId;
        payload["platform"] = channel.Platform;
        payload["subscribed"] = subscribed;
        var response = await _executor.ExecuteAsync<KanbanOperationResponse>(profile, "kanban.py", payload, ct);
        if (!response.Ok) throw new InvalidOperationException(response.Error ?? "Unable to update Kanban home channel.");
    }

    public async Task<KanbanDispatchResult?> DispatchNowAsync(ConnectionProfile profile, string boardSlug, int maxSpawn = 8, CancellationToken ct = default)
    {
        var payload = MutationPayload(profile, boardSlug, "dispatch");
        payload["max_spawn"] = maxSpawn;
        var response = await ExecuteMutationAsync(profile, payload, ct);
        return response.Dispatch;
    }

    private async Task<KanbanOperationResponse> ExecuteMutationAsync(ConnectionProfile profile, Dictionary<string, object> payload, CancellationToken ct)
    {
        var response = await _executor.ExecuteAsync<KanbanOperationResponse>(profile, "kanban.py", payload, ct);
        if (!response.Ok) throw new InvalidOperationException(response.Error ?? "Unable to update Kanban.");
        return response;
    }

    private static Dictionary<string, object> BasePayload(ConnectionProfile profile, string operation, bool includeArchived) => new()
    {
        ["operation"] = operation,
        ["kanban_home"] = profile.RemoteKanbanHomePath,
        ["hermes_home"] = profile.RemoteHermesHomePath,
        ["include_archived"] = includeArchived
    };

    private static Dictionary<string, object> MutationPayload(ConnectionProfile profile, string boardSlug, string action) => new()
    {
        ["operation"] = "mutate",
        ["kanban_home"] = profile.RemoteKanbanHomePath,
        ["hermes_home"] = profile.RemoteHermesHomePath,
        ["board_slug"] = boardSlug,
        ["author"] = profile.ResolvedHermesProfileName,
        ["action"] = action
    };
}
