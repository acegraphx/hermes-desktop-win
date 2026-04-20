using HermesDesktop.Models;

namespace HermesDesktop.Services;

public interface ICronBrowserService
{
    Task<List<CronJob>> ListJobsAsync(ConnectionProfile profile, CancellationToken ct = default);
    Task<string> CreateJobAsync(ConnectionProfile profile, CronJobDraft draft, CancellationToken ct = default);
    Task UpdateJobAsync(ConnectionProfile profile, string jobId, CronJobDraft draft, CancellationToken ct = default);
    Task PauseJobAsync(ConnectionProfile profile, string jobId, CancellationToken ct = default);
    Task ResumeJobAsync(ConnectionProfile profile, string jobId, CancellationToken ct = default);
    Task RunJobNowAsync(ConnectionProfile profile, string jobId, CancellationToken ct = default);
    Task RemoveJobAsync(ConnectionProfile profile, string jobId, CancellationToken ct = default);
}
