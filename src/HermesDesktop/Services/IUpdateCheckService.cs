using HermesDesktop.Models;

namespace HermesDesktop.Services;

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckLatestReleaseAsync(CancellationToken ct = default);
    bool IsNewerVersion(string latestVersion, string currentVersion);
}
