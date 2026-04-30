using HermesDesktop.Models;

namespace HermesDesktop.Services;

public interface IWikiService
{
    Task<List<WikiEntry>> ListAsync(ConnectionProfile profile, CancellationToken ct = default);
    Task<WikiDocument> ReadAsync(ConnectionProfile profile, string relativePath, CancellationToken ct = default);
    Task<WikiSaveResult> SaveAsync(ConnectionProfile profile, WikiDocument document, string newContent, CancellationToken ct = default);
    Task<List<WikiSearchResult>> SearchAsync(ConnectionProfile profile, string query, CancellationToken ct = default);
    Task<List<string>> BacklinksAsync(ConnectionProfile profile, string pageBasename, string? selfRelativePath = null, CancellationToken ct = default);
}
