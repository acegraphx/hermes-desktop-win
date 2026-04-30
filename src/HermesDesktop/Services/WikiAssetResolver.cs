using System.Collections.Concurrent;
using System.IO;
using HermesDesktop.Models;
using Microsoft.Extensions.Logging;

namespace HermesDesktop.Services;

public class WikiAssetResolver
{
    private const long MaxCacheBytes = 64L * 1024 * 1024;
    private const int MaxCacheEntries = 256;

    private readonly SftpConnectionPool _sftpPool;
    private readonly ILogger<WikiAssetResolver> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private long _cachedBytes;

    public WikiAssetResolver(SftpConnectionPool sftpPool, ILogger<WikiAssetResolver> logger)
    {
        _sftpPool = sftpPool;
        _logger = logger;
    }

    public async Task<(byte[]? Bytes, string Mime)> GetAsync(
        ConnectionProfile profile, string relativePath, CancellationToken ct = default)
    {
        var cleaned = CleanRelativePath(relativePath);
        if (cleaned == null)
            return (null, "application/octet-stream");

        var cacheKey = $"{profile.Id}:{cleaned}";
        if (_cache.TryGetValue(cacheKey, out var hit))
        {
            hit.LastUsed = DateTime.UtcNow;
            return (hit.Bytes, hit.Mime);
        }

        var remotePath = JoinRemotePath(profile.RemoteWikiPath, cleaned);

        try
        {
            var sftp = await _sftpPool.GetOrCreateAsync(profile, ct);
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await Task.Run(() => sftp.DownloadFile(remotePath, ms), ct);
                bytes = ms.ToArray();
            }

            var mime = GuessMime(cleaned);
            var entry = new CacheEntry(bytes, mime);
            _cache[cacheKey] = entry;
            Interlocked.Add(ref _cachedBytes, bytes.LongLength);
            EnforceCacheLimits();
            return (bytes, mime);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Asset fetch failed: {Path}", remotePath);
            return (null, "application/octet-stream");
        }
    }

    public void InvalidateProfile(Guid profileId)
    {
        var prefix = profileId.ToString() + ":";
        foreach (var key in _cache.Keys.Where(k => k.StartsWith(prefix)).ToList())
        {
            if (_cache.TryRemove(key, out var removed))
                Interlocked.Add(ref _cachedBytes, -removed.Bytes.LongLength);
        }
    }

    private void EnforceCacheLimits()
    {
        while ((Interlocked.Read(ref _cachedBytes) > MaxCacheBytes || _cache.Count > MaxCacheEntries)
               && _cache.Count > 0)
        {
            var oldest = _cache
                .OrderBy(kvp => kvp.Value.LastUsed)
                .FirstOrDefault();
            if (oldest.Key == null) break;
            if (_cache.TryRemove(oldest.Key, out var removed))
                Interlocked.Add(ref _cachedBytes, -removed.Bytes.LongLength);
        }
    }

    private static string? CleanRelativePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Replace('\\', '/').Trim().TrimStart('/');
        if (s.Contains("..", StringComparison.Ordinal)) return null;
        if (string.IsNullOrEmpty(s)) return null;
        return s;
    }

    private static string JoinRemotePath(string root, string relative)
    {
        var rootTrimmed = root.TrimEnd('/');
        return $"{rootTrimmed}/{relative}";
    }

    private static string GuessMime(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".pdf" => "application/pdf",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            _ => "application/octet-stream",
        };
    }

    private class CacheEntry
    {
        public byte[] Bytes { get; }
        public string Mime { get; }
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;

        public CacheEntry(byte[] bytes, string mime) { Bytes = bytes; Mime = mime; }
    }
}
