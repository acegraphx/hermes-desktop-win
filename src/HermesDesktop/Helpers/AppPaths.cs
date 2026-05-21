using System.IO;

namespace HermesDesktop.Helpers;

public static class AppPaths
{
    private static readonly string _appDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HermesDesktop");

    public static string AppDataDirectory => _appDataDir;
    public static string ConnectionsFile => Path.Combine(_appDataDir, "connections.json");
    public static string PreferencesFile => Path.Combine(_appDataDir, "preferences.json");
    public static string WorkflowsFile => Path.Combine(_appDataDir, "workflows.json");
    public static string LogsDirectory => Path.Combine(_appDataDir, "logs");

    public static string SshDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(LogsDirectory);
    }
}
