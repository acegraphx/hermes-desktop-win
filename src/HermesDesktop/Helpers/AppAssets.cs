using System.IO;
using System.Reflection;

namespace HermesDesktop.Helpers;

public static class AppAssets
{
    private const string ResourcePrefix = "HermesDesktop.EmbeddedAssets";
    private static readonly object ExtractLock = new();

    private static readonly IReadOnlyDictionary<string, string[]> AssetFiles =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Terminal"] =
            [
                "terminal.html",
                "terminal-bridge.js",
                "xterm.css",
                "xterm.js",
                "xterm-addon-fit.js",
                "xterm-addon-webgl.js"
            ],
            ["Markdown"] =
            [
                "markdown.html",
                "marked.min.js"
            ],
            ["Wiki"] =
            [
                "preview.html",
                "editor.html",
                "marked.min.js",
                "codemirror.bundle.js"
            ],
            ["Fonts"] =
            [
                "CascadiaCode-Regular.ttf",
                "FiraCode-Regular.ttf",
                "Hack-Regular.ttf",
                "IBMPlexMono-Regular.ttf",
                "JetBrainsMono-Regular.ttf",
                "LICENSES.md"
            ]
        };

    public static string? ResolveAssetFolder(string subfolder, params string[] requiredFiles)
    {
        var local = FindLocalAssetFolder(subfolder, requiredFiles);
        if (local != null) return local;

        return ExtractEmbeddedAssetFolder(subfolder, requiredFiles);
    }

    private static string? FindLocalAssetFolder(string subfolder, IReadOnlyCollection<string> requiredFiles)
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (var i = 0; i < 6; i++)
        {
            var candidate = Path.Combine(dir, "Assets", subfolder);
            if (HasRequiredFiles(candidate, requiredFiles))
                return candidate;

            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        return null;
    }

    private static string? ExtractEmbeddedAssetFolder(string subfolder, IReadOnlyCollection<string> requiredFiles)
    {
        if (!AssetFiles.TryGetValue(subfolder, out var files))
            return null;

        lock (ExtractLock)
        {
            try
            {
                var targetDir = Path.Combine(ExtractedAssetsRoot(), subfolder);
                Directory.CreateDirectory(targetDir);

                var assembly = typeof(AppAssets).Assembly;
                foreach (var file in files)
                {
                    var resourceName = $"{ResourcePrefix}.{subfolder}.{file}";
                    using var resource = assembly.GetManifestResourceStream(resourceName);
                    if (resource == null) return null;

                    var targetPath = Path.GetFullPath(Path.Combine(targetDir, file));
                    if (!IsPathInside(targetPath, targetDir)) return null;

                    if (File.Exists(targetPath) && new FileInfo(targetPath).Length == resource.Length)
                        continue;

                    var tempPath = Path.Combine(
                        targetDir,
                        $"{file}.{Environment.ProcessId}.{Guid.NewGuid():N}.partial");
                    using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        resource.CopyTo(output);
                    }

                    File.Move(tempPath, targetPath, overwrite: true);
                }

                return HasRequiredFiles(targetDir, requiredFiles) ? targetDir : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static string ExtractedAssetsRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var version = typeof(AppAssets).Assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
            ?? typeof(AppAssets).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        return Path.Combine(localAppData, "HermesDesktop", "Assets", version);
    }

    private static bool HasRequiredFiles(string folder, IReadOnlyCollection<string> requiredFiles)
    {
        if (!Directory.Exists(folder)) return false;
        if (requiredFiles.Count == 0) return Directory.EnumerateFiles(folder).Any();
        return requiredFiles.All(file => File.Exists(Path.Combine(folder, file)));
    }

    private static bool IsPathInside(string path, string folder)
    {
        var normalizedFolder = Path.GetFullPath(folder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase);
    }
}
