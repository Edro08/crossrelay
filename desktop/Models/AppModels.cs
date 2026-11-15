using System;
using System.Collections.Generic;

namespace CrossRelay.Desktop.Models;

public record PeerDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Alias { get; set; }
    public DeviceOperatingSystem OS { get; init; } = DeviceOperatingSystem.Unknown;
    public required string IpAddress { get; init; }
    public int Port { get; set; } = 4242;
    public string ProtocolVersion { get; init; } = "1";
    public required string CertificateFingerprint { get; init; }
    public PairingStatus PairingStatus { get; set; } = PairingStatus.Unpaired;
    public double LatencyMs { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public string DisplayName => !string.IsNullOrWhiteSpace(Alias) ? Alias : Name;
    public bool IsPaired => PairingStatus == PairingStatus.Paired;
}

public record TrustedPeer
{
    public required string Id { get; init; }
    public required string OriginalName { get; init; }
    public string? Alias { get; set; }
    public DeviceOperatingSystem OS { get; init; }
    public required string CertificateFingerprint { get; init; }
    public required string PairingToken { get; init; }
    public TransferPolicy Policy { get; set; } = TransferPolicy.AutoAccept;
    public string? CustomDownloadDirectory { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }
    public DateTime PairedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public TelemetryStats Stats { get; init; } = new();

    public string DisplayName => !string.IsNullOrWhiteSpace(Alias) ? Alias : OriginalName;
}

public record TelemetryStats
{
    public long TotalBytesSent { get; set; }
    public long TotalBytesReceived { get; set; }
    public int TotalFilesSent { get; set; }
    public int TotalFilesReceived { get; set; }
    public double AverageSpeedMBps { get; set; }
    public double PeakSpeedMBps { get; set; }
    public double LastLatencyMs { get; set; }
    public int SuccessfulTransfersCount { get; set; }
    public int FailedTransfersCount { get; set; }

    public long TotalBytesExchanged => TotalBytesSent + TotalBytesReceived;
    public int TotalFilesExchanged => TotalFilesSent + TotalFilesReceived;
    public double ReliabilityPercentage => (SuccessfulTransfersCount + FailedTransfersCount) == 0 
        ? 100.0 
        : (double)SuccessfulTransfersCount / (SuccessfulTransfersCount + FailedTransfersCount) * 100.0;
}

public record TransferItem
{
    public required string FileId { get; init; }
    public required string Name { get; init; }
    public required string LocalPath { get; init; }
    public long SizeBytes { get; init; }
    public string? MimeType { get; init; }
    public string? ExpectedSha256 { get; init; }
    public long BytesTransferred { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Pending;

    public double ProgressPercentage => SizeBytes == 0 ? 100.0 : (double)BytesTransferred / SizeBytes * 100.0;
}

public record TransferBatch
{
    public required string SessionId { get; init; }
    public bool IsIncoming { get; init; }
    public required PeerDevice RemotePeer { get; init; }
    public List<TransferItem> Files { get; init; } = [];
    public long TotalBytes { get; set; }
    public long TotalBytesTransferred { get; set; }
    public double CurrentSpeedMBps { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Pending;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public double OverallProgressPercentage => TotalBytes == 0 
        ? 100.0 
        : (double)TotalBytesTransferred / TotalBytes * 100.0;
}

public record LocalNodeIdentity
{
    public required string Id { get; init; }
    public required string DeviceName { get; set; }
    public DeviceOperatingSystem OS { get; init; } = DeviceOperatingSystem.Windows;
    public int Port { get; set; } = 4242;
    public string PortMode { get; set; } = "auto";
    public int PortRangeEnd { get; set; } = 4248;
    public string DuplicatePolicy { get; set; } = "rename";
    public required string TlsFingerprint { get; set; }
    public NodeVisibilityMode VisibilityMode { get; set; } = NodeVisibilityMode.VisibleToAll;
    public string DefaultDownloadDirectory { get; set; } = string.Empty;
    public BandwidthLimitMode BandwidthLimit { get; set; } = BandwidthLimitMode.Unlimited;
    public double CustomBandwidthLimitMBps { get; set; } = 100.0;
    public bool BringToFrontOnRequest { get; set; } = true;
}

public class TransferProgressEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public required string CurrentFileName { get; init; }
    public double ProgressPercentage { get; init; }
    public double SpeedMBps { get; init; }
    public TimeSpan RemainingTime { get; init; }
    public long BytesTransferred { get; init; }
    public long TotalBytes { get; init; }
}

public record IncomingFileEntry(string Name, long SizeBytes);

public class TransferRequestArgs : EventArgs
{
    public required string SenderId { get; init; }
    public required string SenderName { get; init; }
    public required string BatchTitle { get; init; }
    public required long TotalBytes { get; init; }
    public required int FilesCount { get; init; }
    public required string RemoteEndpoint { get; init; }
    public IReadOnlyList<IncomingFileEntry> Files { get; init; } = Array.Empty<IncomingFileEntry>();
}

public class IncomingTransferProgressEventArgs : EventArgs
{
    public string SenderId { get; init; } = string.Empty;
    public required string SenderName { get; init; }
    public required string BatchTitle { get; init; }
    public required string CurrentFileName { get; init; }
    public required long BytesReceived { get; init; }
    public required long TotalBytes { get; init; }
    public required double SpeedMBps { get; init; }
    public required TimeSpan RemainingTime { get; init; }
    public string TargetDirectory { get; init; } = string.Empty;
    public IReadOnlyList<IncomingFileEntry> Files { get; init; } = Array.Empty<IncomingFileEntry>();
    public bool Success { get; init; } = true;
    public string Error { get; init; } = string.Empty;
}

public record PortDiagnosticEntry(int Port, string State);

public record PortDiagnosticsResult(
    string OS,
    int ActivePort,
    string FirewallStatus,
    IReadOnlyList<PortDiagnosticEntry> Ports);
