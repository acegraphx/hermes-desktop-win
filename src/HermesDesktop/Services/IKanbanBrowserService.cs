using HermesDesktop.Models;

namespace HermesDesktop.Services;

public interface IKanbanBrowserService
{
    Task<KanbanBoardsResponse> ListBoardsAsync(ConnectionProfile profile, bool includeArchived, CancellationToken ct = default);
    Task<KanbanBoard> LoadBoardAsync(ConnectionProfile profile, string boardSlug, bool includeArchived, CancellationToken ct = default);
    Task<KanbanTaskDetail> LoadTaskDetailAsync(ConnectionProfile profile, string boardSlug, string taskId, CancellationToken ct = default);
    Task<KanbanProject> CreateBoardAsync(ConnectionProfile profile, KanbanBoardDraft draft, CancellationToken ct = default);
    Task ArchiveBoardAsync(ConnectionProfile profile, string boardSlug, CancellationToken ct = default);
    Task<string> CreateTaskAsync(ConnectionProfile profile, string boardSlug, KanbanTaskDraft draft, CancellationToken ct = default);
    Task<KanbanOperationResponse> MutateTaskAsync(ConnectionProfile profile, string boardSlug, Dictionary<string, object?> parameters, CancellationToken ct = default);
    Task SetHomeSubscriptionAsync(ConnectionProfile profile, string boardSlug, string taskId, KanbanHomeChannel channel, bool subscribed, CancellationToken ct = default);
    Task<KanbanDispatchResult?> DispatchNowAsync(ConnectionProfile profile, string boardSlug, int maxSpawn = 8, CancellationToken ct = default);
}
