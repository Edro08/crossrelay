using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrossRelay.Desktop.Models;

namespace CrossRelay.Desktop.Services;

/// <summary>
/// CrossRelayEngine acts as the Desktop client coordinator delegating network, discovery and transfers to the Go Core.
/// </summary>
public class CrossRelayEngine : IDisposable
{
    private readonly CoreProcessClient _client;
    private readonly FileTrustedPeersRepository _trustedPeersRepo;
    private readonly FileSettingsRepository _settingsRepo;
    private readonly FileBlacklistRepository _blacklistRepo;
    private sealed class IncomingSessionMeta
    {
        public string SenderName { get; init; } = "Peer";
        public string BatchTitle { get; init; } = "Transferencia";
        public long TotalBytes { get; init; }
        public double LastSpeedMBps { get; set; }
        public IReadOnlyList<IncomingFileEntry> Files { get; init; } = Array.Empty<IncomingFileEntry>();
    }

    private readonly ConcurrentDictionary<string, PeerDevice> _activePeers = new();
    private readonly ConcurrentDictionary<string, IncomingSessionMeta> _incomingSessions = new();
    private LocalNodeIdentity? _localIdentity;

    public event EventHandler<PeerDevice>? PeerDiscovered;
    public event EventHandler<string>? PeerLost;
    public event EventHandler<TransferProgressEventArgs>? TransferProgressChanged;
    public event Action<string, string>? PinChallengeReceived; // (requesterName, 6-digit pin)
    public event Action<TrustedPeer>? PeerPairingCompleted;
    public event Action? TrustedPeersChanged;
    public event Func<TransferRequestArgs, Task<bool>>? TransferConfirmationRequested;
    public event EventHandler<TransferRequestArgs>? AutoTransferStarted;
    public event EventHandler<TransferRequestArgs>? IncomingTransferStarted;
    public event EventHandler<IncomingTransferProgressEventArgs>? IncomingTransferProgressChanged;
    public event EventHandler<IncomingTransferProgressEventArgs>? IncomingTransferCompleted;
    public event Action<string>? IncomingTransferExpired;
    public event EventHandler<(bool Success, string Error)>? OutgoingTransferCompleted;
    public event Action<bool>? OutgoingPauseChanged;

    public IReadOnlyList<PeerDevice> ActivePeers => _activePeers.Values.ToList();
    public LocalNodeIdentity? LocalIdentity => _localIdentity;
    public FileTrustedPeersRepository TrustedPeersRepository => _trustedPeersRepo;
    public FileBlacklistRepository BlacklistRepository => _blacklistRepo;

    public CrossRelayEngine(
        FileTrustedPeersRepository? trustedPeersRepo = null,
        FileSettingsRepository? settingsRepo = null,
        FileBlacklistRepository? blacklistRepo = null)
    {
        _client = CoreProcessClient.Instance;
        _trustedPeersRepo = trustedPeersRepo ?? new FileTrustedPeersRepository();
        _settingsRepo = settingsRepo ?? new FileSettingsRepository();
        _blacklistRepo = blacklistRepo ?? new FileBlacklistRepository();

        // Wire Go Core SSE events
        _client.OnPeerDiscovered += OnCorePeerDiscovered;
        _client.OnPeerLost += OnCorePeerLost;
        _client.OnPeerPaired += OnCorePeerPaired;
        _client.OnPinChallenge += (name, pin) => PinChallengeReceived?.Invoke(name, pin);
        _client.OnTransferRequested += OnCoreTransferRequested;
        _client.OnTransferProgress += OnCoreTransferProgress;
        _client.OnTransferCompleted += OnCoreTransferCompleted;
    }

    private void OnCorePeerPaired(JsonElement root)
    {
        try
        {
            var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(id)) return;

            var name = root.TryGetProperty("deviceName", out var n) ? n.GetString() ?? "Peer" : "Peer";
            var osStr = root.TryGetProperty("os", out var o) ? o.GetString() ?? "" : "";
            var fp = root.TryGetProperty("certFingerprint", out var f) ? f.GetString() ?? "" : "";
            var token = root.TryGetProperty("pairingToken", out var t) ? t.GetString() ?? "" : "";

            var os = osStr.ToLowerInvariant() switch
            {
                "android" => DeviceOperatingSystem.Android,
                "linux" => DeviceOperatingSystem.Linux,
                "apple" or "macos" or "ios" or "darwin" => DeviceOperatingSystem.Apple,
                _ => DeviceOperatingSystem.Windows
            };

            var paired = new TrustedPeer
            {
                Id = id,
                OriginalName = name,
                Alias = name,
                OS = os,
                CertificateFingerprint = fp,
                PairingToken = token,
                Policy = TransferPolicy.AskConfirmation,
                CustomDownloadDirectory = null,
                PairedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };

            _ = Task.Run(async () =>
            {
                await _trustedPeersRepo.SaveAsync(paired);
                PeerPairingCompleted?.Invoke(paired);
                TrustedPeersChanged?.Invoke();
            });
        }
        catch { }
    }

    private void OnCorePeerDiscovered(JsonElement p)
    {
        try
        {
            var id = p.GetProperty("id").GetString() ?? "";
            var name = p.GetProperty("name").GetString() ?? "";
            var osStr = p.TryGetProperty("os", out var o) ? o.GetString() ?? "" : "";
            var ip = p.TryGetProperty("ip", out var ipProp) ? ipProp.GetString() ?? "127.0.0.1" : "127.0.0.1";
            var port = p.TryGetProperty("port", out var portProp) ? portProp.GetInt32() : 4242;
            var fp = p.TryGetProperty("fingerprint", out var fpProp) ? fpProp.GetString() ?? "" : "";

            var os = osStr.ToLowerInvariant() switch
            {
                "android" => DeviceOperatingSystem.Android,
                "linux" => DeviceOperatingSystem.Linux,
                "apple" or "macos" or "ios" or "darwin" => DeviceOperatingSystem.Apple,
                _ => DeviceOperatingSystem.Windows
            };

            var dev = new PeerDevice
            {
                Id = id,
                Name = name,
                OS = os,
                CertificateFingerprint = fp,
                IpAddress = ip,
                Port = port,
                PairingStatus = PairingStatus.Unpaired
            };
            _activePeers[id] = dev;
            PeerDiscovered?.Invoke(this, dev);
        }
        catch { }
    }

    private void OnCorePeerLost(string id)
    {
        if (_activePeers.TryRemove(id, out _))
        {
            PeerLost?.Invoke(this, id);
        }
    }

    private void OnCoreTransferRequested(JsonElement root)
    {
        try
        {
            var sessionId = root.TryGetProperty("sessionId", out var sProp) ? sProp.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(sessionId)) return;

            var senderId = root.TryGetProperty("senderId", out var sidProp) ? sidProp.GetString() ?? "" : "";
            var senderName = root.TryGetProperty("senderName", out var snProp) ? snProp.GetString() ?? "Peer" : "Peer";
            var totalBytes = root.TryGetProperty("totalBytes", out var tbProp) ? tbProp.GetInt64() : 0;
            var totalFiles = root.TryGetProperty("totalFiles", out var tfProp) ? tfProp.GetInt32() : 0;
            var requiresConfirmation = root.TryGetProperty("requiresConfirmation", out var rcProp) && rcProp.GetBoolean();

            var batchTitle = "Transferencia";
            var parsedFiles = new List<IncomingFileEntry>();
            if (root.TryGetProperty("files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Array && filesProp.GetArrayLength() > 0)
            {
                foreach (var fileEl in filesProp.EnumerateArray())
                {
                    if (parsedFiles.Count >= 200)
                        break;
                    var fName = fileEl.TryGetProperty("name", out var fnProp) ? fnProp.GetString() ?? "" : "";
                    var fSize = fileEl.TryGetProperty("size", out var fsProp) ? fsProp.GetInt64() : 0;
                    if (!string.IsNullOrEmpty(fName))
                    {
                        parsedFiles.Add(new IncomingFileEntry(fName, fSize));
                    }
                }

                if (parsedFiles.Count > 0)
                {
                    var firstName = parsedFiles[0].Name;
                    var slashIdx = firstName.IndexOf('/');
                    if (slashIdx > 0)
                    {
                        var rootFolder = firstName[..slashIdx];
                        batchTitle = totalFiles > 1 ? $"{rootFolder} ({totalFiles} archivos)" : rootFolder;
                    }
                    else
                    {
                        batchTitle = totalFiles > 1 ? $"{firstName} (+{totalFiles - 1} archivos)" : firstName;
                    }
                }
            }

            _incomingSessions[sessionId] = new IncomingSessionMeta
            {
                SenderName = senderName,
                BatchTitle = batchTitle,
                TotalBytes = totalBytes,
                Files = parsedFiles
            };

            var reqArgs = new TransferRequestArgs
            {
                SenderId = senderId,
                SenderName = senderName,
                BatchTitle = batchTitle,
                TotalBytes = totalBytes,
                FilesCount = totalFiles,
                RemoteEndpoint = "LAN TLS 1.3",
                Files = parsedFiles
            };

            if (requiresConfirmation)
            {
                _ = Task.Run(async () =>
                {
                    bool accepted = true;
                    try
                    {
                        if (TransferConfirmationRequested != null)
                            accepted = await TransferConfirmationRequested.Invoke(reqArgs);
                    }
                    catch
                    {
                        accepted = false;
                    }

                    if (!accepted)
                    {
                        _incomingSessions.TryRemove(sessionId, out _);
                        try
                        {
                            await _client.RespondTransferAsync(sessionId, false);
                        }
                        catch { }
                        return;
                    }

                    bool sent;
                    try
                    {
                        sent = await _client.RespondTransferAsync(sessionId, true);
                    }
                    catch
                    {
                        sent = false;
                    }

                    if (sent)
                    {
                        IncomingTransferStarted?.Invoke(this, reqArgs);
                    }
                    else
                    {
                        _incomingSessions.TryRemove(sessionId, out _);
                        IncomingTransferExpired?.Invoke("Tiempo de espera agotado");
                    }
                });
            }
            else
            {
                AutoTransferStarted?.Invoke(this, reqArgs);
                IncomingTransferStarted?.Invoke(this, reqArgs);
            }
        }
        catch { }
    }

    private void OnCoreTransferProgress(string sessionId, long written, long total, long speedBps)
    {
        var speedMBps = speedBps > 0 ? speedBps / 1048576.0 : 0.0;
        var pct = total > 0 ? (double)written / total * 100.0 : 0;

        if (_incomingSessions.TryGetValue(sessionId, out var inc))
        {
            if (speedMBps > 0) inc.LastSpeedMBps = speedMBps;
            var effectiveTotal = total > 0 ? total : inc.TotalBytes;
            var remaining = speedBps > 0 && effectiveTotal > written
                ? TimeSpan.FromSeconds((effectiveTotal - written) / (double)speedBps)
                : TimeSpan.Zero;
            IncomingTransferProgressChanged?.Invoke(this, new IncomingTransferProgressEventArgs
            {
                SenderName = inc.SenderName,
                BatchTitle = inc.BatchTitle,
                CurrentFileName = inc.BatchTitle,
                BytesReceived = written,
                TotalBytes = effectiveTotal,
                SpeedMBps = speedMBps,
                RemainingTime = remaining,
                Files = inc.Files
            });
            return;
        }

        TransferProgressChanged?.Invoke(this, new TransferProgressEventArgs
        {
            SessionId = sessionId,
            CurrentFileName = "Archivo",
            ProgressPercentage = pct,
            SpeedMBps = speedMBps,
            RemainingTime = TimeSpan.Zero,
            BytesTransferred = written,
            TotalBytes = total
        });
    }

    private void OnCoreTransferCompleted(string sessionId, bool success, string err)
    {
        if (_incomingSessions.TryRemove(sessionId, out var inc))
        {
            IncomingTransferCompleted?.Invoke(this, new IncomingTransferProgressEventArgs
            {
                SenderName = inc.SenderName,
                BatchTitle = inc.BatchTitle,
                CurrentFileName = inc.BatchTitle,
                BytesReceived = success ? inc.TotalBytes : 0,
                TotalBytes = inc.TotalBytes,
                SpeedMBps = success ? inc.LastSpeedMBps : 0,
                RemainingTime = TimeSpan.Zero,
                Files = inc.Files,
                Success = success,
                Error = err
            });
            if (!success)
            {
                IncomingTransferExpired?.Invoke(string.IsNullOrWhiteSpace(err) ? "Transferencia expirada" : err);
            }
            return;
        }

        OutgoingTransferCompleted?.Invoke(this, (success, err));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _localIdentity = await _settingsRepo.GetSettingsAsync(cancellationToken);
        await _client.StartAsync();

        await SyncLocalSettingsToCoreAsync(_localIdentity);

        try
        {
            var peers = await _trustedPeersRepo.GetAllAsync(cancellationToken);
            foreach (var p in peers)
            {
                await SyncTrustedPeerAsync(p);
            }
        }
        catch { }
    }

    /// <summary>
    /// Ensures Go Core has the active desktop settings (especially DefaultDownloadDirectory) on startup.
    /// </summary>
    public async Task SyncLocalSettingsToCoreAsync(LocalNodeIdentity settings)
    {
        var downloadDir = string.IsNullOrWhiteSpace(settings.DefaultDownloadDirectory)
            ? StoragePaths.GetDefaultDownloadPath()
            : settings.DefaultDownloadDirectory;

        try
        {
            await _client.SaveSettingsAsync(new
            {
                deviceId = settings.Id,
                deviceName = settings.DeviceName,
                port = settings.Port,
                portMode = settings.PortMode,
                portRangeEnd = settings.PortRangeEnd,
                duplicatePolicy = settings.DuplicatePolicy,
                downloadDirectory = downloadDir,
                visibility = settings.VisibilityMode switch
                {
                    NodeVisibilityMode.OnlyPaired => "paired",
                    NodeVisibilityMode.Invisible => "hidden",
                    _ => "everyone"
                },
                speedLimit = (int)settings.BandwidthLimit
            });
        }
        catch { }
    }

    public Task StopAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    public Task<bool> ProbePeerAsync(string ip, int port = 0)
        => _client.ProbePeerAsync(ip, port);

    public async Task<long> PingPeerAsync(PeerDevice target, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(12L);
    }

    public async Task<bool> InitiatePairingAsync(PeerDevice target, CancellationToken cancellationToken = default)
    {
        try
        {
            var targetObj = new
            {
                id = target.Id,
                name = target.DisplayName,
                os = target.OS.ToString().ToLowerInvariant(),
                fingerprint = target.CertificateFingerprint ?? "",
                ip = target.IpAddress ?? "127.0.0.1",
                port = target.Port
            };
            await _client.InitiatePairingAsync(targetObj);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CompletePairingAsync(PeerDevice target, string pin, CancellationToken cancellationToken = default)
    {
        try
        {
            var targetObj = new
            {
                id = target.Id,
                name = target.DisplayName,
                os = target.OS.ToString().ToLowerInvariant(),
                fingerprint = target.CertificateFingerprint ?? "",
                ip = target.IpAddress ?? "127.0.0.1",
                port = target.Port
            };
            await _client.VerifyPairingAsync(targetObj, pin);
            var paired = new TrustedPeer
            {
                Id = target.Id,
                OriginalName = target.Name,
                Alias = target.Alias,
                OS = target.OS,
                CertificateFingerprint = target.CertificateFingerprint ?? "",
                PairingToken = "token",
                Policy = TransferPolicy.AskConfirmation,
                CustomDownloadDirectory = null,
                PairedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };
            await _trustedPeersRepo.SaveAsync(paired, cancellationToken);
            PeerPairingCompleted?.Invoke(paired);
            TrustedPeersChanged?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SendFilesAsync(PeerDevice target, IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
    {
        try
        {
            var targetObj = new
            {
                id = target.Id,
                name = target.DisplayName,
                os = target.OS.ToString().ToLowerInvariant(),
                fingerprint = target.CertificateFingerprint ?? "",
                ip = target.IpAddress ?? "127.0.0.1",
                port = target.Port
            };
            await _client.SendFilesAsync(targetObj, filePaths.ToArray());
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SetTransferPausedAsync(bool paused)
    {
        var ok = await _client.SetTransferPausedAsync(paused);
        OutgoingPauseChanged?.Invoke(paused);
        return ok;
    }

    public async Task<bool> CancelOutgoingTransferAsync()
    {
        var ok = await _client.CancelTransferAsync();
        OutgoingPauseChanged?.Invoke(false);
        return ok;
    }

    public async Task<bool> CancelIncomingTransferAsync()
    {
        return await _client.CancelIncomingTransferAsync();
    }

    public void RefreshPeers()
    {
    }

    public void UpdateIdentity(LocalNodeIdentity identity)
    {
        _localIdentity = identity;
    }

    public void NotifyTrustedPeersChanged()
    {
        TrustedPeersChanged?.Invoke();
    }

    public async Task SyncTrustedPeerAsync(TrustedPeer peer)
    {
        try
        {
            var policy = peer.Policy == TransferPolicy.AutoAccept ? "AutoAccept" : "AskConfirmation";
            await _client.UpdateTrustedPeerAsync(
                peer.Id,
                peer.Alias ?? peer.OriginalName,
                policy,
                peer.CustomDownloadDirectory ?? "");
        }
        catch { }
    }

    public async Task DeleteTrustedPeerInCoreAsync(string id)
    {
        try
        {
            await _client.DeleteTrustedAsync(id);
        }
        catch { }
    }

    public async Task BlockTrustedPeerInCoreAsync(string id, bool blocked)
    {
        try
        {
            await _client.SetBlockedAsync(id, blocked);
        }
        catch { }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
