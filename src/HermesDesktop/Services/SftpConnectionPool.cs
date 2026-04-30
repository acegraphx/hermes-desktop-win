using System.Collections.Concurrent;
using HermesDesktop.Models;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace HermesDesktop.Services;

public class SftpConnectionPool : IDisposable
{
    private readonly ILogger<SftpConnectionPool> _logger;
    private readonly SshConnectionPool _sshPool;
    private readonly ConcurrentDictionary<Guid, PooledSftp> _connections = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public SftpConnectionPool(ILogger<SftpConnectionPool> logger, SshConnectionPool sshPool)
    {
        _logger = logger;
        _sshPool = sshPool;
    }

    public async Task<SftpClient> GetOrCreateAsync(ConnectionProfile profile, CancellationToken ct)
    {
        if (_connections.TryGetValue(profile.Id, out var pooled) && pooled.Client.IsConnected)
        {
            pooled.LastUsed = DateTime.UtcNow;
            return pooled.Client;
        }

        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_connections.TryGetValue(profile.Id, out pooled) && pooled.Client.IsConnected)
            {
                pooled.LastUsed = DateTime.UtcNow;
                return pooled.Client;
            }

            if (pooled != null)
            {
                try { pooled.Client.Dispose(); } catch { }
                _connections.TryRemove(profile.Id, out _);
            }

            var client = CreateClient(profile);
            _logger.LogInformation("Opening SFTP to {Target}...", profile.DisplayTarget);
            await Task.Run(() => client.Connect(), ct);
            _logger.LogInformation("SFTP connected to {Target}", profile.DisplayTarget);

            _connections[profile.Id] = new PooledSftp(client);
            return client;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public void DisconnectProfile(Guid profileId)
    {
        if (_connections.TryRemove(profileId, out var pooled))
        {
            try { pooled.Client.Disconnect(); } catch { }
            try { pooled.Client.Dispose(); } catch { }
            _logger.LogInformation("Disconnected SFTP for profile {Id}", profileId);
        }
    }

    public void Dispose()
    {
        foreach (var kvp in _connections)
        {
            try { kvp.Value.Client.Dispose(); } catch { }
        }
        _connections.Clear();
        _connectionLock.Dispose();
    }

    private SftpClient CreateClient(ConnectionProfile profile)
    {
        var authMethods = _sshPool.BuildAuthMethods(profile);

        if (authMethods.Count == 0)
            throw new InvalidOperationException(
                $"No SSH authentication methods available for {profile.DisplayTarget}.");

        var connectionInfo = new ConnectionInfo(
            profile.SshHost,
            profile.SshPort,
            profile.SshUser,
            authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(15),
            RetryAttempts = 1,
            Encoding = System.Text.Encoding.UTF8
        };

        return new SftpClient(connectionInfo);
    }

    private class PooledSftp
    {
        public SftpClient Client { get; }
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;

        public PooledSftp(SftpClient client) { Client = client; }
    }
}
