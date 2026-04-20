using HermesDesktop.Models;

namespace HermesDesktop.Services;

public class CronBrowserService : ICronBrowserService
{
    private readonly IRemoteScriptExecutor _executor;

    public CronBrowserService(IRemoteScriptExecutor executor)
    {
        _executor = executor;
    }

    public async Task<List<CronJob>> ListJobsAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        var response = await _executor.ExecuteAsync<CronJobListResponse>(
            profile, "cron_list.py", null, ct);
        if (!response.Ok) throw new InvalidOperationException(response.Error ?? "Unable to list cron jobs.");
        return response.Jobs;
    }

    public async Task<string> CreateJobAsync(ConnectionProfile profile, CronJobDraft draft, CancellationToken ct = default)
    {
        var response = await _executor.ExecuteAsync<CronMutationResponse>(
            profile, "cron_mutate.py", BuildMutationPayload("create", null, draft), ct);
        if (!response.Ok) throw new InvalidOperationException(response.Error ?? "Unable to create cron job.");
        return response.JobId ?? throw new InvalidOperationException("Remote did not return a job ID.");
    }

    public async Task UpdateJobAsync(ConnectionProfile profile, string jobId, CronJobDraft draft, CancellationToken ct = default)
    {
        var response = await _executor.ExecuteAsync<CronMutationResponse>(
            profile, "cron_mutate.py", BuildMutationPayload("update", jobId, draft), ct);
        if (!response.Ok) throw new InvalidOperationException(response.Error ?? "Unable to update cron job.");
    }

    public Task PauseJobAsync(ConnectionProfile profile, string jobId, CancellationToken ct = default) =>
        RunCommandAsync(profile, jobId, "pause", ct);

    public Task ResumeJobAsync(ConnectionProfile profile, string jobId, CancellationToken ct = default) =>
        RunCommandAsync(profile, jobId, "resume", ct);

    public Task RunJobNowAsync(ConnectionProfile profile, string jobId, CancellationToken ct = default) =>
        RunCommandAsync(profile, jobId, "run", ct);

    public Task RemoveJobAsync(ConnectionProfile profile, string jobId, CancellationToken ct = default) =>
        RunCommandAsync(profile, jobId, "remove", ct);

    private async Task RunCommandAsync(ConnectionProfile profile, string jobId, string command, CancellationToken ct)
    {
        var parameters = new Dictionary<string, object>
        {
            ["job_id"] = jobId,
            ["command"] = command
        };
        var response = await _executor.ExecuteAsync<CronCommandResponse>(
            profile, "cron_command.py", parameters, ct);
        if (!response.Ok) throw new InvalidOperationException(response.Error ?? $"Cron {command} failed.");
    }

    private static Dictionary<string, object> BuildMutationPayload(string action, string? jobId, CronJobDraft draft)
    {
        var parameters = new Dictionary<string, object>
        {
            ["action"] = action,
            ["draft"] = new Dictionary<string, object?>
            {
                ["name"] = draft.NormalizedName,
                ["prompt"] = draft.NormalizedPrompt,
                ["schedule"] = draft.Schedule.Expression ?? string.Empty,
                ["skills"] = draft.NormalizedSkills,
                ["model"] = draft.NormalizedModel,
                ["provider"] = draft.NormalizedProvider,
                ["base_url"] = draft.NormalizedBaseUrl,
                ["deliver"] = draft.NormalizedDeliveryTarget,
                ["timezone"] = draft.NormalizedTimezone
            }
        };
        if (jobId is not null) parameters["job_id"] = jobId;
        return parameters;
    }

    private class CronMutationResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("ok")]
        public bool Ok { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public string? Error { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("job_id")]
        public string? JobId { get; set; }
    }

    private class CronCommandResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("ok")]
        public bool Ok { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public string? Error { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
