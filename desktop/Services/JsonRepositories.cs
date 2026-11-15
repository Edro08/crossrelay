using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrossRelay.Desktop.Models;

namespace CrossRelay.Desktop.Services;

public static class StoragePaths
{
    private static readonly string BaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CrossRelay");

    public static string GetPath(string fileName)
    {
        Directory.CreateDirectory(BaseDirectory);
        return Path.Combine(BaseDirectory, fileName);
    }

    public static string GetDefaultDownloadPath()
    {
        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var target = Path.Combine(downloads, "Downloads", "CrossRelay");
        Directory.CreateDirectory(target);
        return target;
    }

    public static string GetLogsDirectory()
    {
        var logs = Path.Combine(BaseDirectory, "logs");
        Directory.CreateDirectory(logs);
        return logs;
    }
}

public class FileTrustedPeersRepository
{
    private readonly string _filePath = StoragePaths.GetPath("trusted_peers.json");
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, TrustedPeer> _cache = new();

    public async Task<IReadOnlyList<TrustedPeer>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _cache.Values.ToList();
    }

    public async Task<TrustedPeer?> GetByIdAsync(string peerId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        _cache.TryGetValue(peerId, out var peer);
        return peer;
    }

    public async Task<TrustedPeer?> GetByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _cache.Values.FirstOrDefault(p => string.Equals(p.CertificateFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveAsync(TrustedPeer peer, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        _cache[peer.Id] = peer;
        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string peerId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        if (_cache.TryRemove(peerId, out _))
        {
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task UpdateLastSeenAsync(string peerId, DateTime lastSeen, CancellationToken cancellationToken = default)
    {
        var peer = await GetByIdAsync(peerId, cancellationToken).ConfigureAwait(false);
        if (peer != null)
        {
            var updated = peer with { LastSeenAt = lastSeen };
            await SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RecordTransferStatsAsync(string peerId, long bytesSent, long bytesReceived, bool success, double speedMBps, CancellationToken cancellationToken = default)
    {
        var peer = await GetByIdAsync(peerId, cancellationToken).ConfigureAwait(false);
        if (peer != null)
        {
            var stats = peer.Stats;
            stats.TotalBytesSent += bytesSent;
            stats.TotalBytesReceived += bytesReceived;
            if (bytesSent > 0) stats.TotalFilesSent++;
            if (bytesReceived > 0) stats.TotalFilesReceived++;
            if (success) stats.SuccessfulTransfersCount++; else stats.FailedTransfersCount++;
            if (speedMBps > stats.PeakSpeedMBps) stats.PeakSpeedMBps = speedMBps;
            peer.LastSeenAt = DateTime.UtcNow;

            await SaveAsync(peer, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (!_cache.IsEmpty) return;

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_cache.IsEmpty) return;

            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
                var peers = JsonSerializer.Deserialize<List<TrustedPeer>>(json);
                if (peers != null)
                {
                    foreach (var peer in peers)
                    {
                        _cache[peer.Id] = peer;
                    }
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = _cache.Values.ToList();
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

public class FileSettingsRepository
{
    private readonly string _filePath = StoragePaths.GetPath("settings.json");
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<LocalNodeIdentity> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
                var settings = JsonSerializer.Deserialize<LocalNodeIdentity>(json);
                if (settings != null) return settings;
            }

            var hostname = Environment.MachineName;
            var defaultIdentity = new LocalNodeIdentity
            {
                Id = Guid.NewGuid().ToString(),
                DeviceName = !string.IsNullOrWhiteSpace(hostname) ? hostname : "Desktop-PC",
                OS = OperatingSystem.IsWindows() ? DeviceOperatingSystem.Windows : DeviceOperatingSystem.Linux,
                Port = 4242,
                TlsFingerprint = string.Empty,
                VisibilityMode = NodeVisibilityMode.VisibleToAll,
                DefaultDownloadDirectory = StoragePaths.GetDefaultDownloadPath(),
                BandwidthLimit = BandwidthLimitMode.Unlimited
            };

            return defaultIdentity;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveSettingsAsync(LocalNodeIdentity settings, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

public class FileBlacklistRepository
{
    private readonly string _filePath = StoragePaths.GetPath("blacklist.json");
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, BlockedEntry> _cache = new();

    public async Task<bool> IsBlockedAsync(string peerId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        if (_cache.TryGetValue(peerId, out var entry))
        {
            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
            {
                await UnblockAsync(peerId, cancellationToken).ConfigureAwait(false);
                return false;
            }
            return true;
        }
        return false;
    }

    public async Task BlockAsync(string peerId, string reason, TimeSpan? duration = null, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var entry = new BlockedEntry
        {
            PeerId = peerId,
            Reason = reason,
            BlockedAt = DateTime.UtcNow,
            ExpiresAt = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : null
        };
        _cache[peerId] = entry;
        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UnblockAsync(string peerId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        if (_cache.TryRemove(peerId, out _))
        {
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<string>> GetAllBlockedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _cache.Keys.ToList();
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (!_cache.IsEmpty) return;
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_cache.IsEmpty) return;
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
                var entries = JsonSerializer.Deserialize<List<BlockedEntry>>(json);
                if (entries != null)
                {
                    foreach (var e in entries) _cache[e.PeerId] = e;
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = _cache.Values.ToList();
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private record BlockedEntry
    {
        public required string PeerId { get; init; }
        public required string Reason { get; init; }
        public DateTime BlockedAt { get; init; }
        public DateTime? ExpiresAt { get; init; }
    }
}

public record PersistedHistoryRecord
{
    public required string Id { get; init; }
    public required string SessionTitle { get; init; }
    public required string PeerName { get; init; }
    public string PeerOs { get; init; } = string.Empty;
    public bool IsSender { get; init; }
    public long TotalBytes { get; init; }
    public int FilesCount { get; init; }
    public double SpeedMBps { get; init; }
    public string StatusText { get; init; } = "Completado";
    public string TargetPath { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

public class FileTransferHistoryRepository
{
    private readonly string _filePath = StoragePaths.GetPath("history.json");
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<List<PersistedHistoryRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath)) return [];
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<PersistedHistoryRecord>>(json) ?? [];
        }
        catch
        {
            return [];
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task AddRecordAsync(PersistedHistoryRecord record, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = new List<PersistedHistoryRecord>();
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
                list = JsonSerializer.Deserialize<List<PersistedHistoryRecord>>(json) ?? [];
            }
            list.Insert(0, record);
            var updatedJson = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, updatedJson, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
