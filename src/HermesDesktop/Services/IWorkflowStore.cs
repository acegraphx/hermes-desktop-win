using HermesDesktop.Models;

namespace HermesDesktop.Services;

public interface IWorkflowStore
{
    Task<IReadOnlyList<WorkflowPreset>> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(IReadOnlyList<WorkflowPreset> workflows, CancellationToken ct = default);
}
