using System.Text.Json;
using HermesDesktop.Models;
using HermesDesktop.Services;
using Microsoft.Extensions.Logging;

if (args.Contains("--remote-kanban-smoke"))
{
    await RemoteKanbanSmokeAsync();
    return;
}

var tests = new (string Name, Action Run)[]
{
    ("Kanban JSON decoding", KanbanJsonDecoding),
    ("Kanban board slug validation", KanbanSlugValidation),
    ("Kanban task draft normalization", KanbanTaskDraftNormalization),
    ("Cron script-only validation", CronScriptOnlyValidation),
    ("Cron all delivery target", CronAllDeliveryTarget),
    ("Connection custom Hermes home", ConnectionCustomHermesHome),
    ("Pinned session snapshot", PinnedSessionSnapshotRoundTrip),
    ("Workflow launch normalization", WorkflowLaunchNormalization),
    ("Hermes chat arguments", HermesChatArguments),
    ("Resume command quoting", ResumeCommandQuoting),
    ("Update version comparison", UpdateVersionComparison),
    ("Kanban script paths", KanbanScriptPaths)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static void KanbanJsonDecoding()
{
    var json = """
    {
      "ok": true,
      "board": {
        "database_path": "~/.hermes/kanban.db",
        "host_wide": true,
        "is_initialized": true,
        "has_kanban_module": true,
        "has_hermes_cli": true,
        "tasks": [{
          "id": "task-1",
          "title": "Ship Windows parity",
          "status": "ready",
          "priority": 2,
          "parent_ids": ["parent"],
          "child_ids": [],
          "skills": ["csharp"],
          "warnings": {"count": 1, "kinds": {"suspected_hallucinated_references": 1}}
        }]
      }
    }
    """;
    var response = JsonSerializer.Deserialize<KanbanBoardResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    Assert(response?.Board.Tasks.Count == 1, "task count");
    if (response is null) throw new InvalidOperationException("response");
    Assert(response.Board.Tasks[0].HasActiveWarnings, "warnings");
    Assert(response.Board.Tasks[0].ResolvedTitle == "Ship Windows parity", "title");
}

static void KanbanSlugValidation()
{
    Assert(new KanbanBoardDraft { Slug = "ops_board-1" }.ValidationError is null, "valid slug");
    Assert(new KanbanBoardDraft { Slug = "Bad Slug" }.ValidationError is not null, "invalid slug");
}

static void KanbanTaskDraftNormalization()
{
    var draft = new KanbanTaskDraft
    {
        Title = "  Task  ",
        SkillsText = "one, two, one",
        ParentIdsText = "a b, a"
    };
    Assert(draft.NormalizedTitle == "Task", "title trim");
    Assert(draft.Skills.SequenceEqual(["one", "two"]), "skills unique");
    Assert(draft.ParentIds.SequenceEqual(["a", "b"]), "parents unique");
    draft.MaxRetriesText = "3";
    Assert(draft.NormalizedMaxRetries == 3, "max retries");
    draft.MaxRetriesText = "0";
    Assert(draft.ValidationError == "Max retries must be a whole number greater than 0.", "max retries validation");
}

static void CronAllDeliveryTarget()
{
    Assert(CronDeliveryPreset.All.Title() == "All Connected Channels", "all title");
    Assert(CronDeliveryPreset.All.ResolvedValue() == "all", "all value");
    var (preset, custom) = CronDeliveryPresetExtensions.FromDeliveryTarget("all");
    Assert(preset == CronDeliveryPreset.All && custom == string.Empty, "all parse");
}

static void ConnectionCustomHermesHome()
{
    var profile = new ConnectionProfile { Label = "Prod", SshHost = "host", SshUser = "ace", CustomHermesHomePath = "~/hermes-prod" };
    Assert(profile.IsValid, profile.ValidationError ?? "custom home valid");
    Assert(profile.UsesCustomHermesHome, "custom mode");
    Assert(profile.CliHermesProfileName is null, "no cli profile");
    Assert(profile.RemoteHermesHomePath == "~/hermes-prod", "remote home");
    Assert(profile.WorkspaceScopeFingerprint.Contains("~/hermes-prod"), "fingerprint");
    Assert(profile.RemoteServiceCommand("python3 -").Contains("HERMES_HOME"), "service env");
}

static void WorkflowLaunchNormalization()
{
    var prompt = WorkflowDraft.NormalizePromptForLaunch("  first line \n\n second line  ");
    Assert(prompt == "first line second line", "prompt compact");
    var workflow = new WorkflowPreset
    {
        Name = "Ship",
        Prompt = "Investigate",
        AssignedSkills = [new WorkflowSkillReference { RelativePath = "dev/csharp", Slug = "dev/csharp", Name = "C#" }]
    };
    Assert(workflow.MatchesSearch("csharp"), "workflow skill search");
}

static void PinnedSessionSnapshotRoundTrip()
{
    var snapshot = new PinnedSessionSnapshot
    {
        Id = "session-123",
        WorkspaceScopeFingerprint = "host-profile",
        Title = "Launch follow-up",
        Model = "gpt-5.2",
        StartedAt = 1766800000,
        LastActive = "2026-05-03T18:20:00Z",
        MessageCount = 18,
        Preview = "Release checklist",
        CreatedAt = DateTime.UnixEpoch.AddSeconds(100),
        UpdatedAt = DateTime.UnixEpoch.AddSeconds(200)
    };

    var json = JsonSerializer.Serialize(new AppPreferences { PinnedSessions = [snapshot] });
    var prefs = JsonSerializer.Deserialize<AppPreferences>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    var pinned = prefs?.PinnedSessions.Single();
    Assert(pinned?.Id == "session-123", "pinned id");
    Assert(pinned?.WorkspaceScopeFingerprint == "host-profile", "pinned scope");
    Assert(pinned?.Title == "Launch follow-up", "pinned title");
    Assert(pinned?.MessageCount == 18, "pinned count");
}

static void CronScriptOnlyValidation()
{
    var agent = new CronJobDraft { Name = "Agent", Prompt = "", DeliveryPreset = CronDeliveryPreset.Local };
    Assert(agent.ValidationError == "A prompt is required.", "agent prompt required");
    var scriptMissing = new CronJobDraft { Name = "Script", NoAgent = true, DeliveryPreset = CronDeliveryPreset.Local };
    Assert(scriptMissing.ValidationError == "A script path is required for script-only jobs.", "script required");
    var script = new CronJobDraft { Name = "Script", NoAgent = true, Script = "jobs/report.sh", DeliveryPreset = CronDeliveryPreset.Local };
    Assert(script.ValidationError is null, "script valid");
}

static void HermesChatArguments()
{
    var args = new HermesChatInvocation
    {
        SessionId = "abc",
        Prompt = "hello",
        AutoApproveCommands = true
    }.Arguments;
    Assert(args.SequenceEqual(["--resume", "abc", "--yolo", "chat", "--quiet", "--query", "hello"]), "chat args");
}

static void ResumeCommandQuoting()
{
    var profile = new ConnectionProfile { HermesProfile = "researcher" };
    var invocation = new HermesSessionResumeInvocation("debug session's final turn", profile);
    Assert(invocation.CommandLine == "hermes --profile researcher --resume 'debug session'\\''s final turn'", "resume quote");
}

static void UpdateVersionComparison()
{
    var service = new UpdateCheckService();
    Assert(service.IsNewerVersion("v1.5.4", "1.5.3"), "patch newer");
    Assert(!service.IsNewerVersion("1.5.3", "1.5.3"), "same version");
    Assert(service.IsNewerVersion("2.0.0-beta.1", "1.9.9"), "suffix parse");
}

static void KanbanScriptPaths()
{
    var root = FindRepoRoot();
    var script = File.ReadAllText(Path.Combine(root, "src", "HermesDesktop", "Scripts", "kanban.py"));
    Assert(script.Contains("return kanban_home_path() / \"kanban.db\""), "default db path");
    Assert(script.Contains("return board_dir(normalized) / \"kanban.db\""), "board db path");
    Assert(script.Contains("return kanban_home_path() / \"kanban\" / \"boards\""), "boards path");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "HermesDesktop.sln")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("Unable to find repo root.");
}

static async Task RemoteKanbanSmokeAsync()
{
    var appData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HermesDesktop");
    var connectionsPath = Path.Combine(appData, "connections.json");
    var preferencesPath = Path.Combine(appData, "preferences.json");
    if (!File.Exists(connectionsPath))
        throw new FileNotFoundException("No saved Hermes Desktop connections were found.", connectionsPath);

    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var connections = JsonSerializer.Deserialize<List<ConnectionProfile>>(
        await File.ReadAllTextAsync(connectionsPath),
        options) ?? [];
    if (connections.Count == 0)
        throw new InvalidOperationException("No saved Hermes Desktop connections were found.");

    Guid? lastConnectionId = null;
    if (File.Exists(preferencesPath))
    {
        var prefs = JsonSerializer.Deserialize<AppPreferences>(
            await File.ReadAllTextAsync(preferencesPath),
            options);
        lastConnectionId = prefs?.LastConnectionId;
    }

    var profile = connections.FirstOrDefault(c => c.Id == lastConnectionId) ?? connections[0];
    using var loggerFactory = LoggerFactory.Create(_ => { });
    using var sshPool = new SshConnectionPool(loggerFactory.CreateLogger<SshConnectionPool>());
    using var sftpPool = new SftpConnectionPool(loggerFactory.CreateLogger<SftpConnectionPool>(), sshPool);
    var ssh = new SshTransport(sshPool, loggerFactory.CreateLogger<SshTransport>());
    var executor = new RemotePythonScriptExecutor(
        ssh,
        sftpPool,
        loggerFactory.CreateLogger<RemotePythonScriptExecutor>());
    var kanban = new KanbanBrowserService(executor);

    var boards = await kanban.ListBoardsAsync(profile, includeArchived: false);
    var slug = boards.Current ?? boards.Boards.FirstOrDefault()?.Slug ?? KanbanProject.DefaultSlug;
    var board = await kanban.LoadBoardAsync(profile, slug, includeArchived: false);
    Console.WriteLine($"PASS Remote Kanban smoke: boards={boards.Boards.Count}; current={slug}; tasks={board.Tasks.Count}; db={board.DatabasePath}");
}
