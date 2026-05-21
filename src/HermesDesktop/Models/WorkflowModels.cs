using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public class WorkflowSkillReference
{
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonIgnore]
    public string ResolvedName => string.IsNullOrWhiteSpace(Name) ? Slug : Name!;

    public static WorkflowSkillReference FromSkill(SkillInfo skill)
    {
        var relativePath = !string.IsNullOrWhiteSpace(skill.RelativePath)
            ? skill.RelativePath
            : !string.IsNullOrWhiteSpace(skill.Path)
                ? skill.Path
            : string.IsNullOrWhiteSpace(skill.Category) ? skill.Name : $"{skill.Category}/{skill.Name}";
        return new WorkflowSkillReference
        {
            RelativePath = relativePath,
            Slug = string.IsNullOrWhiteSpace(skill.Category) ? skill.Name : $"{skill.Category}/{skill.Name}",
            Name = string.IsNullOrWhiteSpace(skill.Name) ? null : skill.Name
        };
    }
}

public class WorkflowPreset
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("workspaceScopeFingerprint")]
    public string WorkspaceScopeFingerprint { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("assignedSkills")]
    public List<WorkflowSkillReference> AssignedSkills { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string PromptPreview
    {
        get
        {
            var compact = string.Join(" ", (Prompt ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return compact.Length <= 140 ? compact : compact[..140].TrimEnd() + "...";
        }
    }

    public bool MatchesSearch(string query)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0) return true;
        return new[] { Name, Prompt }
            .Concat(AssignedSkills.Select(s => s.RelativePath))
            .Concat(AssignedSkills.Select(s => s.ResolvedName))
            .Any(v => (v ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));
    }
}

public class WorkflowDraft
{
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public List<WorkflowSkillReference> SelectedSkills { get; set; } = new();

    public string NormalizedName => Name.Trim();
    public string NormalizedPrompt => Prompt.Trim();

    public string? ValidationError =>
        string.IsNullOrWhiteSpace(NormalizedName) ? "Workflow name is required." :
        string.IsNullOrWhiteSpace(NormalizedPrompt) ? "Workflow prompt is required." :
        null;

    public static string NormalizePromptForLaunch(string prompt) =>
        string.Join(" ", (prompt ?? string.Empty)
            .Split('\n', '\r')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0));
}

public enum WorkflowRunDestination
{
    Terminal,
    Chat
}
