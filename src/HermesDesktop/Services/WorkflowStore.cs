using System.IO;
using System.Text.Json;
using HermesDesktop.Helpers;
using HermesDesktop.Models;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.Services;

public class WorkflowStore : IWorkflowStore
{
    private readonly ILogger<WorkflowStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WorkflowStore(ILogger<WorkflowStore> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<WorkflowPreset>> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(AppPaths.WorkflowsFile)) return Array.Empty<WorkflowPreset>();
            var json = await File.ReadAllTextAsync(AppPaths.WorkflowsFile, ct);
            return JsonSerializer.Deserialize<List<WorkflowPreset>>(json, _jsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workflows");
            return Array.Empty<WorkflowPreset>();
        }
    }

    public async Task SaveAsync(IReadOnlyList<WorkflowPreset> workflows, CancellationToken ct = default)
    {
        AppPaths.EnsureDirectories();
        var tempPath = AppPaths.WorkflowsFile + ".tmp";
        var json = JsonSerializer.Serialize(workflows, _jsonOptions);
        await File.WriteAllTextAsync(tempPath, json, ct);
        File.Move(tempPath, AppPaths.WorkflowsFile, overwrite: true);
    }
}
