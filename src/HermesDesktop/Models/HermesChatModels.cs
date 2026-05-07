using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public class HermesChatInvocation
{
    public string? SessionId { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public bool AutoApproveCommands { get; init; }

    public List<string> Arguments
    {
        get
        {
            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(SessionId))
                values.AddRange(["--resume", SessionId]);
            if (AutoApproveCommands)
                values.Add("--yolo");
            values.AddRange(["chat", "--quiet", "--query", Prompt]);
            return values;
        }
    }
}

public class HermesSessionResumeInvocation
{
    public string SessionId { get; }
    public string? HermesProfileName { get; }

    public HermesSessionResumeInvocation(string sessionId, ConnectionProfile connection)
    {
        SessionId = sessionId;
        HermesProfileName = connection.TrimmedHermesProfile;
    }

    public List<string> Arguments
    {
        get
        {
            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(HermesProfileName))
                values.AddRange(["--profile", HermesProfileName]);
            values.AddRange(["--resume", SessionId]);
            return values;
        }
    }

    public string CommandLine => string.Join(" ", new[] { "hermes" }.Concat(Arguments).Select(ShellQuote));

    public static string ShellQuote(string value)
    {
        if (string.IsNullOrEmpty(value)) return "''";
        if (value.All(ch => char.IsLetterOrDigit(ch) || "_+-./:=@".Contains(ch)))
            return value;
        return "'" + value.Replace("'", "'\\''") + "'";
    }
}

public class PendingSessionTurn
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? SessionId { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public bool AutoApproveCommands { get; init; }
}

public class HermesChatTurnResult
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("stdout")] public string? Stdout { get; set; }
    [JsonPropertyName("stderr")] public string? Stderr { get; set; }
}
