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
    public bool UsesDefaultHermesProfile => TrimmedHermesProfile is null;

    [JsonIgnore]
    public string RemoteHermesHomePath =>
        TrimmedHermesProfile is { } profile
            ? $"~/.hermes/profiles/{profile}"
            : "~/.hermes";

    [JsonIgnore]
    public string RemoteSkillsPath => $"{RemoteHermesHomePath}/skills";

    [JsonIgnore]
    public string RemoteCronJobsPath => $"{RemoteHermesHomePath}/cron/jobs.json";

    [JsonIgnore]
    public string RemoteShellBootstrapCommand
    {
        get
        {
            var homeExpr = TrimmedHermesProfile is { } p
                ? $"$HOME/.hermes/profiles/{p.Replace("\"", "\\\"")}"
                : "$HOME/.hermes";
            return $"export HERMES_HOME=\"{homeExpr}\"; exec \"${{SHELL:-/bin/bash}}\" -l";
        }
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
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Label) &&
        !string.IsNullOrEmpty(EffectiveTarget);

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
            CreatedAt = CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            LastConnectedAt = LastConnectedAt,
        };
    }
}
