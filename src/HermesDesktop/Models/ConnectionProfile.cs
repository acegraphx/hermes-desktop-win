using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public class ConnectionProfile
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("sshAlias")]
    public string SshAlias { get; set; } = string.Empty;

    [JsonPropertyName("sshHost")]
    public string SshHost { get; set; } = string.Empty;

    [JsonPropertyName("sshUser")]
    public string SshUser { get; set; } = string.Empty;

    [JsonPropertyName("sshPort")]
    public int SshPort { get; set; } = 22;

    [JsonPropertyName("sshKeyPath")]
    public string? SshKeyPath { get; set; }

    [JsonPropertyName("hermesProfile")]
    public string? HermesProfile { get; set; }

    [JsonPropertyName("customHermesHomePath")]
    public string? CustomHermesHomePath { get; set; }

    [JsonPropertyName("wikiPath")]
    public string? WikiPath { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastConnectedAt")]
    public DateTime? LastConnectedAt { get; set; }

    [JsonIgnore]
    public string? TrimmedAlias
    {
        get
        {
            var v = (SshAlias ?? string.Empty).Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }

    [JsonIgnore]
    public string? TrimmedHost
    {
        get
        {
            var v = (SshHost ?? string.Empty).Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }

    [JsonIgnore]
    public string? TrimmedUser
    {
        get
        {
            var v = (SshUser ?? string.Empty).Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }

    [JsonIgnore]
    public string? TrimmedHermesProfile
    {
        get
        {
            if (HermesProfile is null) return null;
            var v = HermesProfile.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            if (string.Equals(v, "default", StringComparison.OrdinalIgnoreCase)) return null;
            return v;
        }
    }

    [JsonIgnore]
    public string ResolvedHermesProfileName => TrimmedHermesProfile ?? "default";

    [JsonIgnore]
    public string? TrimmedCustomHermesHomePath
    {
        get
        {
            if (CustomHermesHomePath is null) return null;
            var v = NormalizeCustomHermesHomePath(CustomHermesHomePath.Trim());
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }

    [JsonIgnore]
    public bool UsesCustomHermesHome => TrimmedCustomHermesHomePath is not null;

    [JsonIgnore]
    public bool UsesDefaultHermesProfile => TrimmedHermesProfile is null && !UsesCustomHermesHome;

    [JsonIgnore]
    public string? CliHermesProfileName => UsesCustomHermesHome ? null : TrimmedHermesProfile;

    [JsonIgnore]
    public string RemoteHermesHomePath =>
        TrimmedCustomHermesHomePath is { } custom
            ? custom
            : TrimmedHermesProfile is { } profile
                ? $"~/.hermes/profiles/{profile}"
                : "~/.hermes";

    [JsonIgnore]
    public string RemoteHermesHomeShellExpression =>
        TrimmedCustomHermesHomePath is { } custom
            ? CustomHermesHomeShellExpression(custom)
            : TrimmedHermesProfile is { } profile
                ? $"$HOME/.hermes/profiles/{EscapeDoubleQuotedShell(profile)}"
                : "$HOME/.hermes";

    [JsonIgnore]
    public string RemoteHermesSearchPathShellExpression
    {
        get
        {
            var entries = new[]
            {
                $"{RemoteHermesHomeShellExpression}/hermes-agent/venv/bin",
                "$HOME/.local/bin",
                "$HOME/.hermes/hermes-agent/venv/bin",
                "$HOME/.cargo/bin",
                "/opt/homebrew/bin",
                "/usr/local/bin",
                "$PATH"
            };
            return string.Join(":", entries.Distinct());
        }
    }

    [JsonIgnore]
    public string RemoteHermesCommandPrefix =>
        "if [ -x \"$HERMES_HOME/hermes-agent/venv/bin/hermes\" ]; then HERMES_BIN=\"$HERMES_HOME/hermes-agent/venv/bin/hermes\"; " +
        "elif [ -x \"$HOME/.local/bin/hermes\" ]; then HERMES_BIN=\"$HOME/.local/bin/hermes\"; " +
        "elif [ -x \"$HOME/.hermes/hermes-agent/venv/bin/hermes\" ]; then HERMES_BIN=\"$HOME/.hermes/hermes-agent/venv/bin/hermes\"; " +
        "elif command -v hermes >/dev/null 2>&1; then HERMES_BIN=\"$(command -v hermes)\"; " +
        "else printf 'Hermes CLI not found.\\n' >&2; exit 127; fi; \"$HERMES_BIN\"";

    [JsonIgnore]
    public string RemoteServiceEnvironmentCommand =>
        $"export HERMES_HOME=\"{RemoteHermesHomeShellExpression}\"; export PATH=\"{RemoteHermesSearchPathShellExpression}\"";

    public string RemoteHermesCommandLine(IEnumerable<string> arguments)
    {
        var quoted = string.Join(" ", arguments.Select(ShellQuote));
        return string.IsNullOrWhiteSpace(quoted) ? RemoteHermesCommandPrefix : $"{RemoteHermesCommandPrefix} {quoted}";
    }

    public string RemoteServiceCommand(string commandLine)
    {
        var escapedCommand = EscapeDoubleQuotedShell(commandLine);
        var innerCommand = $"{RemoteServiceEnvironmentCommand}; exec /bin/sh -c \"{escapedCommand}\"";
        return $"exec /bin/sh -c \"{EscapeDoubleQuotedShell(innerCommand)}\"";
    }

    [JsonIgnore]
    public string LegacyRemoteHermesHomePath =>
        TrimmedHermesProfile is { } profile
            ? $"~/.hermes/profiles/{profile}"
            : "~/.hermes";

    [JsonIgnore]
    public string RemoteKanbanHomePath => "~/.hermes";

    [JsonIgnore]
    public string RemoteSkillsPath => $"{RemoteHermesHomePath}/skills";

    [JsonIgnore]
    public string RemoteCronJobsPath => $"{RemoteHermesHomePath}/cron/jobs.json";

    [JsonIgnore]
    public string? TrimmedWikiPath
    {
        get
        {
            if (WikiPath is null) return null;
            var v = WikiPath.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }

    [JsonIgnore]
    public string RemoteWikiPath =>
        TrimmedWikiPath ?? $"{RemoteHermesHomePath}/home/wiki";

    [JsonIgnore]
    public string RemoteShellBootstrapCommand
    {
        get
        {
            return RemoteShellBootstrapCommandWithStartup(null);
        }
    }

    public string RemoteShellBootstrapCommandWithStartup(string? startupCommand)
    {
        var shell = "\"${SHELL:-/bin/bash}\"";
        if (string.IsNullOrWhiteSpace(startupCommand))
            return $"{RemoteServiceEnvironmentCommand}; exec {shell} -l";

        var startupSequence = $"{startupCommand}; hermes_bootstrap_exit_code=$?; " +
                              "if [ \"$hermes_bootstrap_exit_code\" -ne 0 ]; then printf '\\n[Hermes Desktop] Startup command exited with status %s.\\n' \"$hermes_bootstrap_exit_code\"; fi; " +
                              $"exec {shell} -l";
        return $"{RemoteServiceEnvironmentCommand}; exec {shell} -lc {ShellQuote(startupSequence)}";
    }

    [JsonIgnore]
    public string EffectiveTarget => TrimmedAlias ?? TrimmedHost ?? string.Empty;

    [JsonIgnore]
    public bool UsesAliasSourceOfTruth => TrimmedAlias is not null && TrimmedHost is null;

    [JsonIgnore]
    public int? ResolvedPort
    {
        get
        {
            if (SshPort <= 0) return null;
            if (UsesAliasSourceOfTruth && SshPort == 22) return null;
            return SshPort;
        }
    }

    [JsonIgnore]
    public string HostConnectionFingerprint =>
        $"{EffectiveTarget}|{TrimmedUser ?? string.Empty}|{(ResolvedPort?.ToString() ?? string.Empty)}";

    [JsonIgnore]
    public string WorkspaceScopeFingerprint =>
        $"{HostConnectionFingerprint}|{RemoteHermesHomePath}";

    [JsonIgnore]
    public string DisplayDestination =>
        TrimmedUser is { } u ? $"{u}@{EffectiveTarget}" : EffectiveTarget;

    [JsonIgnore]
    public bool IsValid => ValidationError is null;

    [JsonIgnore]
    public string DisplayTarget =>
        ResolvedPort is { } port
            ? $"{TrimmedUser ?? SshUser}@{EffectiveTarget}:{port}"
            : $"{TrimmedUser ?? SshUser}@{EffectiveTarget}";

    public ConnectionProfile WithHermesProfile(string? profileName)
    {
        return new ConnectionProfile
        {
            Id = Id,
            Label = Label,
            SshAlias = SshAlias,
            SshHost = SshHost,
            SshUser = SshUser,
            SshPort = SshPort,
            SshKeyPath = SshKeyPath,
            HermesProfile = string.IsNullOrWhiteSpace(profileName) ||
                            string.Equals(profileName, "default", StringComparison.OrdinalIgnoreCase)
                ? null
                : profileName.Trim(),
            CustomHermesHomePath = null,
            WikiPath = WikiPath,
            CreatedAt = CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            LastConnectedAt = LastConnectedAt,
        };
    }

    [JsonIgnore]
    public string? ValidationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Label)) return "Name is required.";
            if (string.IsNullOrEmpty(EffectiveTarget)) return "Add an SSH host.";
            if (TrimmedHermesProfile is not null && TrimmedCustomHermesHomePath is not null)
                return "Choose either a Hermes profile or a custom Hermes home path.";
            if (TrimmedHermesProfile is { } profile &&
                (profile.Contains('/') || profile is "." or ".." || ContainsControlCharacter(profile)))
                return "Hermes profile must be a profile name, not a path.";
            if (TrimmedCustomHermesHomePath is { } custom &&
                (!IsValidCustomHermesHomePath(custom) || ContainsControlCharacter(custom)))
                return "Custom Hermes home must start with ~, ~/ or /.";
            return null;
        }
    }

    private static string NormalizeCustomHermesHomePath(string value)
    {
        if (value is "" or "~" or "/") return value;
        if (value == "~/") return "~";
        while (value.Length > 1 && value.EndsWith('/'))
            value = value[..^1];
        return value;
    }

    private static bool IsValidCustomHermesHomePath(string value) =>
        value == "~" || value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("/", StringComparison.Ordinal);

    private static string CustomHermesHomeShellExpression(string value)
    {
        if (value == "~") return "$HOME";
        if (value.StartsWith("~/", StringComparison.Ordinal))
            return "$HOME/" + EscapeDoubleQuotedShell(value[2..]);
        return EscapeDoubleQuotedShell(value);
    }

    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    public static string EscapeDoubleQuotedShell(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");

    private static bool ContainsControlCharacter(string value) => value.Any(char.IsControl);
}
