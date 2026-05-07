using HermesDesktop.Models;

namespace HermesDesktop.Services;

public class HermesChatService : IHermesChatService
{
    private readonly IRemoteScriptExecutor _executor;

    public HermesChatService(IRemoteScriptExecutor executor)
    {
        _executor = executor;
    }

    public async Task<HermesChatTurnResult> SendMessageAsync(
        ConnectionProfile profile,
        string prompt,
        string? sessionId,
        bool autoApproveCommands,
        CancellationToken ct = default)
    {
        var invocation = new HermesChatInvocation
        {
            SessionId = sessionId,
            Prompt = prompt,
            AutoApproveCommands = autoApproveCommands
        };

        var payload = new Dictionary<string, object>
        {
            ["hermes_home"] = profile.RemoteHermesHomePath,
            ["session_id"] = sessionId!,
            ["timeout_seconds"] = 1800,
            ["executor_timeout_seconds"] = 1860,
            ["auto_approve_commands"] = autoApproveCommands,
            ["arguments"] = invocation.Arguments
        };

        var response = await _executor.ExecuteAsync<HermesChatTurnResult>(profile, "hermes_chat.py", payload, ct);
        if (!response.Ok) throw new InvalidOperationException(response.Error ?? "Unable to run Hermes chat.");
        return response;
    }
}
