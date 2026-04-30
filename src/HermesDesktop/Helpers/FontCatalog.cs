using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace HermesDesktop.Helpers;

public sealed class FontCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("family")]
    public string Family { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("license")]
    public string License { get; set; } = string.Empty;

    [JsonPropertyName("licenseUrl")]
    public string? LicenseUrl { get; set; }
}

internal sealed class FontCatalogManifest
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("fonts")]
    public List<FontCatalogEntry> Fonts { get; set; } = new();
}

public static class FontCatalog
{
    private static IReadOnlyList<FontCatalogEntry>? _cached;
    private static readonly object _lock = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public static IReadOnlyList<FontCatalogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _cached ??= Load();
            }
        }
    }

    public static FontCatalogEntry? FindById(string id) =>
        Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a catalog entry has already been downloaded and is on disk.
    /// </summary>
    public static bool IsInstalled(FontCatalogEntry entry)
    {
        var path = Path.Combine(FontRegistry.UserFontsFolder, entry.FileName);
        return File.Exists(path);
    }

    /// <summary>
    /// Download + verify + persist a catalog font to <see cref="FontRegistry.UserFontsFolder"/>.
    /// Streams to a .partial file, verifies SHA-256, then atomically moves into place.
    /// On any failure, the .partial file is removed and the exception bubbles up.
    /// Raises <see cref="FontRegistry.Changed"/> on success.
    /// </summary>
    public static async Task InstallAsync(
        FontCatalogEntry entry,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(FontRegistry.UserFontsFolder);
        var finalPath = Path.Combine(FontRegistry.UserFontsFolder, entry.FileName);
        var partialPath = finalPath + ".partial";

        Log.Information("Font install start: id={Id} url={Url} -> {Final}", entry.Id, entry.Url, finalPath);

        if (File.Exists(partialPath))
        {
            try { File.Delete(partialPath); } catch { /* best effort */ }
        }

        try
        {
            using var resp = await _http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            Log.Debug("Font install: GET {Url} -> {Status}", entry.Url, (int)resp.StatusCode);
            resp.EnsureSuccessStatusCode();

            var totalBytes = resp.Content.Headers.ContentLength ?? entry.SizeBytes;
            using (var input = await resp.Content.ReadAsStreamAsync(ct))
            using (var output = File.Create(partialPath))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await output.WriteAsync(buffer, 0, read, ct);
                    copied += read;
                    if (totalBytes > 0)
                        progress?.Report((double)copied / totalBytes);
                }
                Log.Debug("Font install: downloaded {Bytes} bytes for {Id}", copied, entry.Id);
            }

            // Verify SHA-256 against the manifest before moving into place.
            var actualHash = await ComputeSha256Async(partialPath, ct);
            var actualSize = new FileInfo(partialPath).Length;
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("Font install hash mismatch for {Id}: expected {Expected} got {Actual} ({Size} bytes)",
                    entry.Id, entry.Sha256, actualHash, actualSize);
                throw new InvalidDataException(
                    $"SHA-256 mismatch for '{entry.Id}' ({actualSize} bytes): expected {entry.Sha256[..12]}…, got {actualHash[..12]}….");
            }

            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(partialPath, finalPath);
            Log.Information("Font install success: {Id} -> {Final}", entry.Id, finalPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Font install failed for {Id}", entry.Id);
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { /* best effort */ }
            }
            throw;
        }

        FontRegistry.Invalidate();
    }

    /// <summary>
    /// Remove a downloaded font from disk. No-op for fonts that aren't installed.
    /// Raises <see cref="FontRegistry.Changed"/>.
    /// </summary>
    public static void Uninstall(FontCatalogEntry entry)
    {
        var path = Path.Combine(FontRegistry.UserFontsFolder, entry.FileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        FontRegistry.Invalidate();
    }

    private static IReadOnlyList<FontCatalogEntry> Load()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            // Default-namespace embedding maps Resources/font-catalog.json -> {asm}.Resources.font-catalog.json
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("font-catalog.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName is null) return Array.Empty<FontCatalogEntry>();

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return Array.Empty<FontCatalogEntry>();

            var manifest = JsonSerializer.Deserialize<FontCatalogManifest>(stream);
            return manifest?.Fonts ?? new List<FontCatalogEntry>();
        }
        catch
        {
            return Array.Empty<FontCatalogEntry>();
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
