using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace HermesDesktop.Helpers;

public static class FontRegistry
{
    public const string DefaultFamily = "Consolas";
    public const string BundledVirtualHost = "hermes.fonts.bundled";
    public const string UserVirtualHost = "hermes.fonts.user";

    private static readonly object _lock = new();
    private static IReadOnlyList<FontEntry>? _availableCache;

    public static event EventHandler? Changed;

    public static string BundledFontsFolder =>
        AppAssets.ResolveAssetFolder("Fonts", "CascadiaCode-Regular.ttf")
        ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Fonts");

    public static string UserFontsFolder
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "HermesDesktop", "fonts");
        }
    }

    public static IReadOnlyList<FontEntry> GetAvailable()
    {
        lock (_lock)
        {
            return _availableCache ??= BuildAvailable();
        }
    }

    public static FontEntry? Resolve(string? family)
    {
        if (string.IsNullOrWhiteSpace(family)) return null;
        return GetAvailable().FirstOrDefault(e =>
            string.Equals(e.Family, family, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns one FontEntry per catalog manifest entry. If the entry has been
    /// downloaded, Source = Downloaded with a usable WpfFamily and IsInstalled = true.
    /// Otherwise Source = Downloadable, WpfFamily falls back to Consolas (preview not
    /// available until install), and IsInstalled = false.
    /// </summary>
    public static IReadOnlyList<FontEntry> GetCatalog()
    {
        var available = GetAvailable();
        var downloadedByFamily = available
            .Where(e => e.Source == FontSource.Downloaded)
            .ToDictionary(e => e.Family, StringComparer.OrdinalIgnoreCase);

        var fallbackPreview = new FontFamily(DefaultFamily);
        var result = new List<FontEntry>();
        foreach (var c in FontCatalog.Entries)
        {
            if (downloadedByFamily.TryGetValue(c.Family, out var installed))
            {
                result.Add(installed with { CatalogId = c.Id, DisplayName = c.DisplayName });
            }
            else
            {
                result.Add(new FontEntry(
                    Family: c.Family,
                    DisplayName: c.DisplayName,
                    Source: FontSource.Downloadable,
                    WpfFamily: fallbackPreview,
                    FileName: c.FileName,
                    CatalogId: c.Id,
                    IsInstalled: false));
            }
        }
        return result;
    }

    public static string CssFontFaceBlock()
    {
        var sb = new StringBuilder();
        foreach (var entry in GetAvailable())
        {
            string? virtualHost = entry.Source switch
            {
                FontSource.Bundled => BundledVirtualHost,
                FontSource.Downloaded => UserVirtualHost,
                _ => null
            };
            if (virtualHost is null || string.IsNullOrEmpty(entry.FileName)) continue;

            sb.Append("@font-face{font-family:'")
              .Append(EscapeCss(entry.Family))
              .Append("';src:url('https://")
              .Append(virtualHost)
              .Append('/')
              .Append(Uri.EscapeDataString(entry.FileName))
              .Append("') format('truetype');font-display:block;}\n");
        }
        return sb.ToString();
    }

    public static void Invalidate()
    {
        lock (_lock) { _availableCache = null; }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static IReadOnlyList<FontEntry> BuildAvailable()
    {
        var bundled = EnumerateBundled();
        var downloaded = EnumerateDownloaded();
        var system = EnumerateSystemMonospace();

        var localFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in bundled) localFamilies.Add(e.Family);
        foreach (var e in downloaded) localFamilies.Add(e.Family);

        var systemFiltered = system.Where(e => !localFamilies.Contains(e.Family));

        var all = new List<FontEntry>();
        all.AddRange(bundled);
        all.AddRange(downloaded);
        all.AddRange(systemFiltered);
        all.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return all;
    }

    private static List<FontEntry> EnumerateBundled()
    {
        // Bundled fonts are static; reading metadata via GlyphTypeface is safe here.
        var folder = BundledFontsFolder;
        var result = new List<FontEntry>();
        if (!Directory.Exists(folder)) return result;

        var folderUri = new Uri(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        foreach (var file in Directory.GetFiles(folder, "*.ttf"))
        {
            try
            {
                var fileName = Path.GetFileName(file);
                var glyph = new GlyphTypeface(new Uri(file));
                var familyName = glyph.FamilyNames.Values.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(familyName)) continue;
                if (result.Any(r => string.Equals(r.Family, familyName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var wpfFamily = new FontFamily(folderUri, $"./#{familyName}");
                result.Add(new FontEntry(
                    Family: familyName,
                    DisplayName: familyName,
                    Source: FontSource.Bundled,
                    WpfFamily: wpfFamily,
                    FileName: fileName,
                    CatalogId: null,
                    IsInstalled: true));
            }
            catch
            {
                // Skip corrupt files.
            }
        }
        return result;
    }

    private static List<FontEntry> EnumerateDownloaded()
    {
        // Trust the catalog manifest for family names + file presence. Reading
        // the .ttf via GlyphTypeface right after File.Move can fail transiently
        // (Windows Defender file scan, brief OS lock) and would otherwise hide
        // the just-installed font.
        //
        // WpfFamily intentionally falls back to the system default. Binding a
        // disk-backed FontFamily to a TextBlock measure right after install
        // crashed WPF when Defender held the file: TryGetFontTable threw
        // FileNotFoundException out of MeasureOverride → unhandled → process exit.
        // The terminal renders downloaded fonts via xterm.js + @font-face anyway;
        // the WPF dropdown only needs the family *name* to be selectable.
        var folder = UserFontsFolder;
        var result = new List<FontEntry>();
        if (!Directory.Exists(folder)) return result;

        var safePreview = new FontFamily(DefaultFamily);
        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in FontCatalog.Entries)
        {
            var path = Path.Combine(folder, c.FileName);
            if (!File.Exists(path)) continue;

            result.Add(new FontEntry(
                Family: c.Family,
                DisplayName: c.DisplayName,
                Source: FontSource.Downloaded,
                WpfFamily: safePreview,
                FileName: c.FileName,
                CatalogId: c.Id,
                IsInstalled: true));
            processedFiles.Add(c.FileName);
        }

        // Fallback: any user-dropped .ttf not described by the catalog.
        foreach (var file in Directory.GetFiles(folder, "*.ttf"))
        {
            var fileName = Path.GetFileName(file);
            if (processedFiles.Contains(fileName)) continue;
            try
            {
                var glyph = new GlyphTypeface(new Uri(file));
                var familyName = glyph.FamilyNames.Values.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(familyName)) continue;
                if (result.Any(r => string.Equals(r.Family, familyName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                result.Add(new FontEntry(
                    Family: familyName,
                    DisplayName: familyName,
                    Source: FontSource.Downloaded,
                    WpfFamily: safePreview,
                    FileName: fileName,
                    CatalogId: null,
                    IsInstalled: true));
            }
            catch
            {
                // Skip unreadable user-dropped files.
            }
        }
        return result;
    }

    private static List<FontEntry> EnumerateSystemMonospace()
    {
        var result = new List<FontEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            try
            {
                if (!IsMonospace(family)) continue;
                var name = family.Source;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!seen.Add(name)) continue;
                result.Add(new FontEntry(
                    Family: name,
                    DisplayName: name,
                    Source: FontSource.System,
                    WpfFamily: family,
                    FileName: null,
                    CatalogId: null,
                    IsInstalled: true));
            }
            catch
            {
                // Skip fonts that fail to load.
            }
        }
        return result;
    }

    private static bool IsMonospace(FontFamily family)
    {
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (!typeface.TryGetGlyphTypeface(out var glyph)) return false;

        var i = AdvanceFor(glyph, 'i');
        var m = AdvanceFor(glyph, 'M');
        var w = AdvanceFor(glyph, 'W');
        if (i is null || m is null || w is null) return false;
        return Math.Abs(i.Value - m.Value) < 0.001 && Math.Abs(m.Value - w.Value) < 0.001;
    }

    private static double? AdvanceFor(GlyphTypeface glyph, char c) =>
        glyph.CharacterToGlyphMap.TryGetValue(c, out var idx)
            ? glyph.AdvanceWidths[idx]
            : null;

    private static string EscapeCss(string s) =>
        s.Replace("\\", "\\\\").Replace("'", "\\'");
}
