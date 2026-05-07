using HermesDesktop.Models;

namespace HermesDesktop.Services;

public interface IHermesChatService
{
    Task<HermesChatTurnResult> SendMessageAsync(
        ConnectionProfile profile,
        string prompt,
        string? sessionId,
        bool autoApproveCommands,
        CancellationToken ct = default);
}
