using System.IO;
using System.Security.Cryptography;
using System.Text;
using HermesDesktop.Helpers;
using HermesDesktop.Models;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.Services;

public class WorkflowLaunchDiagnostics
{
    private readonly ILogger<WorkflowLaunchDiagnostics> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _logPath;

    public WorkflowLaunchDiagnostics(ILogger<WorkflowLaunchDiagnostics> logger)
    {
        _logger = logger;
        _logPath = Path.Combine(AppPaths.LogsDirectory, "workflow-launch-latest.log");
        AppPaths.EnsureDirectories();
        File.WriteAllText(_logPath, $"ts={DateTimeOffset.UtcNow:O} event=diagnostics_session_started path=\"{_logPath}\"\n");
    }

    public Task RecordWorkflowRunRequestedAsync(WorkflowPreset workflow, ConnectionProfile connection, string commandLine, string initialInput, string destination) =>
        RecordAsync("workflow_run_requested", new()
        {
            ["workflow_id"] = workflow.Id.ToString("D"),
            ["workflow_name"] = workflow.Name,
            ["destination"] = destination,
            ["connection"] = connection.Label,
            ["hermes_profile"] = connection.CliHermesProfileName ?? "default",
            ["skill_count"] = workflow.AssignedSkills.Count.ToString(),
            ["skills"] = string.Join(",", workflow.AssignedSkills.Select(s => s.RelativePath)),
            ["command_line"] = commandLine,
            ["prompt_chars"] = workflow.Prompt.Length.ToString(),
            ["prompt_utf8_bytes"] = Encoding.UTF8.GetByteCount(workflow.Prompt).ToString(),
            ["prompt_hash"] = HashPrefix(workflow.Prompt),
            ["initial_input_chars"] = initialInput.Length.ToString(),
            ["initial_input_hash"] = HashPrefix(initialInput)
        });

    public Task RecordInitialInputSentAsync(string title, int characterCount, string reason) =>
        RecordAsync("initial_input_sent", new()
        {
            ["title"] = title,
            ["chars"] = characterCount.ToString(),
            ["reason"] = reason
        });

    private async Task RecordAsync(string eventName, Dictionary<string, string> fields)
    {
        var line = $"ts={DateTimeOffset.UtcNow:O} event={eventName} " +
                   string.Join(" ", fields.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}=\"{Sanitize(kv.Value)}\""));
        var acquired = false;
        try
        {
            await _lock.WaitAsync();
            acquired = true;
            await File.AppendAllTextAsync(_logPath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write workflow launch diagnostics");
        }
        finally
        {
            if (acquired) _lock.Release();
        }
    }

    private static string HashPrefix(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(hash[..6]).ToLowerInvariant();
    }

    private static string Sanitize(string value) =>
        (value ?? string.Empty)
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\"", "'")
            .Replace("|", "/");
}
