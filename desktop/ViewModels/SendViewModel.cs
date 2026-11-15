using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossRelay.Desktop.Models;
using CrossRelay.Desktop.Services;

namespace CrossRelay.Desktop.ViewModels;

public partial class StagedTransferItem : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string FullPath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public string FormattedSize { get; init; } = "0 B";
    public string IconText { get; init; } = "📄";
    public string CategoryLabel { get; init; } = "Archivo";
    public string FileCategory { get; init; } = "file";

    public bool IsFolderCategory => FileCategory == "folder";
    public bool IsVideoCategory => FileCategory == "video";
    public bool IsImageCategory => FileCategory == "image";
    public bool IsFileCategory => FileCategory != "folder" && FileCategory != "video" && FileCategory != "image";

    [ObservableProperty]
    private bool _isSelected = true;

    public Action? SelectionChanged { get; set; }

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(StatusBg))]
    [NotifyPropertyChangedFor(nameof(StatusBorder))]
    [NotifyPropertyChangedFor(nameof(TileBg))]
    [NotifyPropertyChangedFor(nameof(TileBorder))]
    [NotifyPropertyChangedFor(nameof(TileIconColor))]
    private string _status = "En cola"; // "En cola", "En progreso", "Enviando", "Completado", "Error", "Cancelado"

    public string StatusColor => Status switch
    {
        "Completado" => "#10B981",
        "En progreso" or "Enviando" or "Enviando..." => "#C185FC",
        "Error" => "#EF4444",
        "Pausado" or "Cancelado" => "#FBBF24",
        _ => "#A1A1AA"
    };

    public string StatusBg => Status switch
    {
        "Completado" => "#091914",
        "En progreso" or "Enviando" or "Enviando..." => "#1E1738",
        "Error" => "#1F1014",
        "Pausado" or "Cancelado" => "#1F1A0F",
        _ => "#18191E"
    };

    public string StatusBorder => Status switch
    {
        "Completado" => "#133829",
        "En progreso" or "Enviando" or "Enviando..." => "#332763",
        "Error" => "#3F1922",
        "Pausado" or "Cancelado" => "#403318",
        _ => "#262B36"
    };

    public string TileBg => Status == "Error" ? "#1F1014" : FileCategory switch
    {
        "folder" => "#241909",
        "video" => "#1E1738",
        "image" => "#091E17",
        _ => "#0C192E"
    };

    public string TileBorder => Status == "Error" ? "#3F1922" : FileCategory switch
    {
        "folder" => "#4E3510",
        "video" => "#332763",
        "image" => "#133D2E",
        _ => "#1E3A5F"
    };

    public string TileIconColor => Status == "Error" ? "#EF4444" : FileCategory switch
    {
        "folder" => "#FBBF24",
        "video" => "#C185FC",
        "image" => "#34D399",
        _ => "#60A5FA"
    };

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowBg))]
    private bool _isCurrent;

    public string RowBg => IsCurrent ? "#121020" : "Transparent";
}

public partial class PeerDeviceItem : ObservableObject
{
    public PeerDevice Peer { get; }

    [ObservableProperty]
    private bool _isTrusted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnlineStatusText))]
    [NotifyPropertyChangedFor(nameof(OnlineStatusColor))]
    [NotifyPropertyChangedFor(nameof(FullSubtitle))]
    private bool _isOnline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(IsNotSelected))]
    private bool _isSelected;

    public bool IsNotSelected => !IsSelected;

    public string CardBorderColor => IsSelected ? "#582BF5" : "#23222B";

    public string DisplayName => Peer.DisplayName;
    public string IpAddress => Peer.IpAddress;
    public int Port => Peer.Port;
    public string OS => Peer.OS.ToString();
    public bool IsWindows => Peer.OS == DeviceOperatingSystem.Windows || Peer.OS == DeviceOperatingSystem.Unknown;
    public bool IsLinux => Peer.OS == DeviceOperatingSystem.Linux;
    public bool IsAndroid => Peer.OS == DeviceOperatingSystem.Android || Peer.OS == DeviceOperatingSystem.Apple;

    public string IconBg => IsLinux ? "#091914" : (IsAndroid ? "#13152C" : "#1E1738");
    public string IconBorder => IsLinux ? "#133829" : (IsAndroid ? "#242954" : "#332763");
    public string IconFg => IsLinux ? "#10B981" : (IsAndroid ? "#818CF8" : "#C185FC");

    public string OnlineStatusText => IsOnline ? "En línea" : "Sin conexión";
    public string OnlineStatusColor => IsOnline ? "#10B981" : "#71717A";

    public string FullSubtitle => Peer.OS switch
    {
        DeviceOperatingSystem.Windows => $"Windows 11 • {(string.IsNullOrEmpty(Peer.IpAddress) ? "Vinculado" : Peer.IpAddress)}",
        DeviceOperatingSystem.Linux => $"Linux • {(string.IsNullOrEmpty(Peer.IpAddress) ? "Vinculado" : Peer.IpAddress)}",
        DeviceOperatingSystem.Android => $"Android • {(string.IsNullOrEmpty(Peer.IpAddress) ? "Vinculado" : Peer.IpAddress)}",
        _ => $"{Peer.OS} • {(string.IsNullOrEmpty(Peer.IpAddress) ? "Vinculado" : Peer.IpAddress)}"
    };

    public PeerDeviceItem(PeerDevice peer, bool isTrusted = true, bool isOnline = false)
    {
        Peer = peer;
        _isTrusted = isTrusted;
        _isOnline = isOnline;
    }
}

public partial class SendViewModel : ViewModelBase
{
    private readonly CrossRelayEngine? _engine;
    private CancellationTokenSource? _transferCts;
    private bool _suppressSelectAllUpdate;
    private bool _isAllSelected = true;

    /// <summary>
    /// Lista exclusiva de dispositivos VINCULADOS (TrustedPeers).
    /// Ningún dispositivo no vinculado aparece aquí ni en el modal "Cambiar".
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PeerDeviceItem> _activePeers = new();

    [ObservableProperty]
    private ObservableCollection<PeerDeviceItem> _filteredPeers = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPeer))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPeer))]
    [NotifyPropertyChangedFor(nameof(CanSendBatch))]
    [NotifyPropertyChangedFor(nameof(TargetPeerNameDisplay))]
    [NotifyPropertyChangedFor(nameof(TargetPeerSubtitleDisplay))]
    [NotifyPropertyChangedFor(nameof(TargetPeerOnlineColor))]
    [NotifyPropertyChangedFor(nameof(TargetPeerOnlineText))]
    [NotifyPropertyChangedFor(nameof(SendButtonText))]
    [NotifyPropertyChangedFor(nameof(IsTargetPeerWindows))]
    [NotifyPropertyChangedFor(nameof(IsTargetPeerLinux))]
    [NotifyPropertyChangedFor(nameof(IsTargetPeerAndroid))]
    [NotifyPropertyChangedFor(nameof(TargetPeerIconBg))]
    [NotifyPropertyChangedFor(nameof(TargetPeerIconBorder))]
    [NotifyPropertyChangedFor(nameof(TargetPeerIconFg))]
    private PeerDeviceItem? _selectedPeerItem;

    public PeerDevice? SelectedPeer => SelectedPeerItem?.Peer;
    public bool HasSelectedPeer => SelectedPeerItem != null && SelectedPeerItem.IsTrusted;
    public bool CanSendBatch => HasSelectedPeer && HasStagedItems && SelectedStagedFilesCount > 0 && !IsTransferring && SelectedPeerItem!.IsOnline;

    public string TargetPeerNameDisplay => SelectedPeerItem?.DisplayName ?? "Ninguno";
    public string TargetPeerSubtitleDisplay => SelectedPeerItem?.FullSubtitle ?? "Vincula en la pestaña Dispositivos";
    public string TargetPeerOnlineColor => SelectedPeerItem is { IsOnline: true } ? "#10B981" : "#71717A";
    public string TargetPeerOnlineText => SelectedPeerItem is { IsOnline: true } ? "En línea" : (SelectedPeerItem != null ? "Desconectado" : "No vinculado");

    public string SendButtonText => HasSelectedPeer ? $"Enviar a {TargetPeerNameDisplay}" : "Enviar Lote";
    public bool IsTargetPeerWindows => SelectedPeerItem == null || SelectedPeerItem.IsWindows;
    public bool IsTargetPeerLinux => SelectedPeerItem is { IsLinux: true };
    public bool IsTargetPeerAndroid => SelectedPeerItem is { IsAndroid: true };
    public string TargetPeerIconBg => SelectedPeerItem?.IconBg ?? "#1E1738";
    public string TargetPeerIconBorder => SelectedPeerItem?.IconBorder ?? "#332763";
    public string TargetPeerIconFg => SelectedPeerItem?.IconFg ?? "#C185FC";

    [ObservableProperty]
    private ObservableCollection<StagedTransferItem> _stagedItems = new();

    public bool IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            if (SetProperty(ref _isAllSelected, value) && !_suppressSelectAllUpdate)
            {
                _suppressSelectAllUpdate = true;
                foreach (var item in StagedItems)
                {
                    item.IsSelected = value;
                }
                _suppressSelectAllUpdate = false;
                UpdateStagedTotals();
                UpdateStatusMessage();
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSendBatch))]
    [NotifyPropertyChangedFor(nameof(BatchStatusText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusColor))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBg))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBorder))]
    private bool _isTransferring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusColor))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBg))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBorder))]
    private bool _isPaused;

    public string PauseButtonText => IsPaused ? "Reanudar" : "Pausar";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DonutSweepAngle))]
    [NotifyPropertyChangedFor(nameof(TransferProgressText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusColor))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBg))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBorder))]
    private double _transferProgress;

    public double DonutSweepAngle => Math.Clamp(TransferProgress * 3.6, 0.0, 360.0);
    public string TransferProgressText => $"{(int)Math.Round(TransferProgress)}%";

    public string BatchStatusText
    {
        get
        {
            if (!HasSelectedPeer) return "Sin vínculo";
            if (IsTransferring && IsPaused) return "Pausado";
            if (IsTransferring) return "Enviando";
            if (TransferProgress >= 100.0 && HasStagedItems) return "Completado";
            if (Eta is "Cancelado" or "Rechazado" or "Error" && HasStagedItems) return Eta;
            if (HasStagedItems) return "Listo";
            return "En espera";
        }
    }

    public string BatchStatusColor => BatchStatusText switch
    {
        "Completado" => "#10B981",
        "Enviando" or "Listo" => "#C185FC",
        "Pausado" or "Sin vínculo" => "#FBBF24",
        "Cancelado" or "Rechazado" or "Error" => "#F87171",
        _ => "#A1A1AA"
    };

    public string BatchStatusBg => BatchStatusText switch
    {
        "Completado" => "#091914",
        "Enviando" or "Listo" => "#1E1738",
        "Pausado" or "Sin vínculo" => "#1F1A0F",
        "Cancelado" or "Rechazado" or "Error" => "#1F1014",
        _ => "#18191E"
    };

    public string BatchStatusBorder => BatchStatusText switch
    {
        "Completado" => "#133829",
        "Enviando" or "Listo" => "#332763",
        "Pausado" or "Sin vínculo" => "#403318",
        "Cancelado" or "Rechazado" or "Error" => "#3F1922",
        _ => "#262B36"
    };

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _currentFileSizeText = string.Empty;

    [ObservableProperty]
    private string _transferredBytesSummary = "0 B de 0 B";

    [ObservableProperty]
    private string _transferSpeed = "0.0 MB/s";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchStatusText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusColor))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBg))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBorder))]
    private string _eta = "En espera";

    [ObservableProperty]
    private string _statusMessage = "Selecciona un dispositivo vinculado y añade archivos";

    [ObservableProperty]
    private string _totalStagedSizeText = "0 B";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchSummarySubtitle))]
    private int _totalStagedFilesCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueueHeaderTitle))]
    [NotifyPropertyChangedFor(nameof(CanSendBatch))]
    private int _selectedStagedFilesCount;

    public string QueueHeaderTitle => $"Archivos seleccionados ({SelectedStagedFilesCount})";
    public string BatchSummarySubtitle => HasStagedItems
        ? $"{TotalStagedFilesCount} archivo(s) • {StatusMessage}"
        : StatusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueueEmpty))]
    [NotifyPropertyChangedFor(nameof(CanSendBatch))]
    [NotifyPropertyChangedFor(nameof(BatchStatusText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusColor))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBg))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBorder))]
    [NotifyPropertyChangedFor(nameof(BatchSummarySubtitle))]
    private bool _hasStagedItems;

    public bool IsQueueEmpty => !HasStagedItems;

    public string FoundPeersSummary => ActivePeers.Count == 1
        ? "1 dispositivo vinculado"
        : $"{ActivePeers.Count} dispositivos vinculados";

    // Diálogo Modal para Cambiar Dispositivo Destino (Solo muestra dispositivos vinculados)
    [ObservableProperty]
    private bool _isSwitchDeviceDialogOpen;

    public SendViewModel(CrossRelayEngine? engine = null)
    {
        _engine = engine;
        if (_engine != null)
        {
            _engine.PeerDiscovered += OnPeerDiscovered;
            _engine.PeerLost += OnPeerLost;
            _engine.TransferProgressChanged += OnTransferProgressChanged;
            _engine.OutgoingTransferCompleted += OnOutgoingTransferCompleted;
            _engine.PeerPairingCompleted += OnPeerPairingCompleted;
            _engine.TrustedPeersChanged += OnTrustedPeersChanged;
            _ = ReloadTrustedPeersAsync();
        }
        UpdateStatusMessage();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        FilteredPeers.Clear();
        var q = SearchQuery?.Trim() ?? string.Empty;
        foreach (var item in ActivePeers)
        {
            if (string.IsNullOrEmpty(q) ||
                item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.IpAddress.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredPeers.Add(item);
            }
        }
        OnPropertyChanged(nameof(FoundPeersSummary));
    }

    /// <summary>
    /// Carga únicamente los dispositivos que están vinculados en TrustedPeersRepository y no bloqueados.
    /// Cruza con los peers activos en mDNS para conocer su IP actual y estado "En línea".
    /// </summary>
    public async Task ReloadTrustedPeersAsync()
    {
        if (_engine == null) return;

        var trustedList = await _engine.TrustedPeersRepository.GetAllAsync().ConfigureAwait(false);
        var blockedList = await _engine.BlacklistRepository.GetAllBlockedAsync().ConfigureAwait(false);
        var discovered = _engine.ActivePeers.ToList();

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var previousSelectedId = SelectedPeerItem?.Peer.Id;
            ActivePeers.Clear();

            foreach (var tp in trustedList)
            {
                if (tp.IsBlocked || blockedList.Contains(tp.Id, StringComparer.OrdinalIgnoreCase))
                    continue;

                var livePeer = discovered.FirstOrDefault(d => string.Equals(d.Id, tp.Id, StringComparison.OrdinalIgnoreCase));
                var peerModel = new PeerDevice
                {
                    Id = tp.Id,
                    Name = tp.OriginalName,
                    Alias = tp.Alias,
                    OS = livePeer?.OS != DeviceOperatingSystem.Unknown && livePeer != null ? livePeer.OS : tp.OS,
                    IpAddress = livePeer?.IpAddress ?? string.Empty,
                    Port = livePeer?.Port ?? 4242,
                    CertificateFingerprint = !string.IsNullOrEmpty(livePeer?.CertificateFingerprint) ? livePeer.CertificateFingerprint : tp.CertificateFingerprint,
                    PairingStatus = PairingStatus.Paired
                };

                var item = new PeerDeviceItem(peerModel, isTrusted: true, isOnline: livePeer != null);
                ActivePeers.Add(item);
            }

            if (previousSelectedId != null)
            {
                SelectedPeerItem = ActivePeers.FirstOrDefault(p => string.Equals(p.Peer.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase));
            }

            if (SelectedPeerItem == null && ActivePeers.Count > 0)
            {
                SelectedPeerItem = ActivePeers.FirstOrDefault(p => p.IsOnline) ?? ActivePeers[0];
            }

            UpdateSelectionFlags();
            ApplySearchFilter();
            UpdateStatusMessage();
        });
    }

    private void UpdateSelectionFlags()
    {
        foreach (var p in ActivePeers)
        {
            p.IsSelected = SelectedPeerItem != null && string.Equals(p.Peer.Id, SelectedPeerItem.Peer.Id, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnPeerDiscovered(object? sender, PeerDevice peer)
    {
        _ = ReloadTrustedPeersAsync();
    }

    private void OnPeerLost(object? sender, string peerId)
    {
        _ = ReloadTrustedPeersAsync();
    }

    private void OnPeerPairingCompleted(TrustedPeer trustedPeer)
    {
        _ = ReloadTrustedPeersAsync();
    }

    private void OnTrustedPeersChanged()
    {
        _ = ReloadTrustedPeersAsync();
    }

    private void OnTransferProgressChanged(object? sender, TransferProgressEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsTransferring && Eta == "Cancelado")
                return;

            var pct = e.TotalBytes > 0 ? Math.Clamp(e.BytesTransferred * 100.0 / e.TotalBytes, 0.0, 100.0) : 0;
            IsTransferring = pct < 100.0;
            if (pct >= 100.0) IsPaused = false;
            TransferProgress = pct;
            TransferredBytesSummary = $"{FormatSize(e.BytesTransferred)} de {FormatSize(e.TotalBytes)}";

            if (pct >= 100.0)
            {
                TransferSpeed = $"{e.SpeedMBps:F1} MB/s";
                Eta = "Completado";
                StatusMessage = "¡Lote enviado con éxito!";
            }
            else if (IsPaused)
            {
                TransferSpeed = "0.0 MB/s";
                Eta = "En pausa";
                StatusMessage = "Transferencia en pausa";
            }
            else if (e.SpeedMBps > 0 && e.TotalBytes > e.BytesTransferred)
            {
                TransferSpeed = $"{e.SpeedMBps:F1} MB/s";
                var remainingSec = (e.TotalBytes - e.BytesTransferred) / (e.SpeedMBps * 1048576.0);
                Eta = FormatRemainingTime(TimeSpan.FromSeconds(remainingSec));
                StatusMessage = $"Transfiriendo a {SelectedPeer?.DisplayName}...";
            }
            else
            {
                TransferSpeed = $"{e.SpeedMBps:F1} MB/s";
                Eta = "Transfiriendo...";
                StatusMessage = $"Transfiriendo a {SelectedPeer?.DisplayName}...";
            }

            long cumulative = 0;
            bool currentAssigned = false;
            var selected = StagedItems.Where(i => i.IsSelected).ToList();
            foreach (var item in selected)
            {
                var itemSize = Math.Max(1, item.SizeBytes);
                var itemStart = cumulative;
                cumulative += itemSize;
                if (pct >= 100.0 || e.BytesTransferred >= cumulative)
                {
                    item.IsCurrent = false;
                    item.IsCompleted = true;
                    item.Status = "Completado";
                    item.ProgressPercent = 100.0;
                }
                else if (!currentAssigned)
                {
                    currentAssigned = true;
                    item.IsCurrent = true;
                    item.IsCompleted = false;
                    item.Status = IsPaused ? "Pausado" : "Enviando";
                    var itemDone = Math.Max(0, e.BytesTransferred - itemStart);
                    item.ProgressPercent = item.SizeBytes > 0 ? Math.Clamp(itemDone * 100.0 / item.SizeBytes, 0.0, 99.0) : pct;
                    CurrentFileName = item.DisplayName;
                    CurrentFileSizeText = item.FormattedSize;
                }
                else
                {
                    item.IsCurrent = false;
                    item.IsCompleted = false;
                    item.Status = "En cola";
                    item.ProgressPercent = 0.0;
                }
            }

            OnPropertyChanged(nameof(BatchSummarySubtitle));
        });
    }

    private void OnOutgoingTransferCompleted(object? sender, (bool Success, string Error) e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsTransferring = false;
            IsPaused = false;
            var selected = StagedItems.Where(i => i.IsSelected).ToList();
            if (e.Success)
            {
                TransferProgress = 100.0;
                Eta = "Completado";
                StatusMessage = "¡Transferencia finalizada con éxito!";
                var totalSelectedBytes = selected.Sum(i => i.SizeBytes);
                var totalFormatted = FormatSize(totalSelectedBytes);
                TransferredBytesSummary = $"{totalFormatted} de {totalFormatted}";
                foreach (var item in selected)
                {
                    item.IsCurrent = false;
                    item.IsCompleted = true;
                    item.Status = "Completado";
                    item.ProgressPercent = 100.0;
                }
            }
            else
            {
                TransferProgress = 0;
                TransferSpeed = "0.0 MB/s";
                var canceled = e.Error.Contains("cancel", StringComparison.OrdinalIgnoreCase);
                var rejected = e.Error.Contains("reject", StringComparison.OrdinalIgnoreCase)
                            || e.Error.Contains("declin", StringComparison.OrdinalIgnoreCase)
                            || e.Error.Contains("rechaz", StringComparison.OrdinalIgnoreCase)
                            || e.Error.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
                Eta = canceled ? "Cancelado" : (rejected ? "Rechazado" : "Error");
                StatusMessage = canceled
                    ? "Transferencia cancelada por el usuario"
                    : (rejected
                        ? $"Transferencia rechazada por {SelectedPeer?.DisplayName ?? "el destinatario"}"
                        : (string.IsNullOrWhiteSpace(e.Error) ? "Error en la transferencia" : $"Error: {e.Error}"));
                foreach (var item in selected.Where(i => !i.IsCompleted))
                {
                    item.IsCurrent = false;
                    item.Status = canceled ? "Cancelado" : "Error";
                }
            }

            OnPropertyChanged(nameof(BatchSummarySubtitle));
            OnPropertyChanged(nameof(CanSendBatch));
        });
    }

    partial void OnSelectedPeerItemChanged(PeerDeviceItem? value)
    {
        UpdateSelectionFlags();
        UpdateStatusMessage();
    }

    [RelayCommand]
    public void RefreshPeers()
    {
        _engine?.RefreshPeers();
        _ = ReloadTrustedPeersAsync();
    }

    [RelayCommand]
    public void AddFiles(string[] paths)
    {
        int totalFilesAdded = 0;
        foreach (var p in paths)
        {
            if (File.Exists(p))
            {
                var fileInfo = new FileInfo(p);
                if (StagedItems.Any(item => item.FullPath == p)) continue;

                var (catKey, catLabel) = GetFileCategory(fileInfo.Name, false);
                StagedItems.Add(new StagedTransferItem
                {
                    FullPath = p,
                    RelativePath = FormatUnixParentPath(fileInfo.DirectoryName),
                    DisplayName = fileInfo.Name,
                    IsDirectory = false,
                    SizeBytes = fileInfo.Length,
                    FormattedSize = FormatSize(fileInfo.Length),
                    IconText = GetFileIcon(fileInfo.Name),
                    FileCategory = catKey,
                    CategoryLabel = catLabel,
                    Status = "En cola",
                    SelectionChanged = OnItemSelectionChanged
                });
                totalFilesAdded++;
            }
            else if (Directory.Exists(p))
            {
                var dirInfo = new DirectoryInfo(p);
                if (StagedItems.Any(item => item.FullPath == p)) continue;

                long dirSize = 0;
                int filesCount = 0;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    {
                        dirSize += new FileInfo(file).Length;
                        filesCount++;
                    }
                }
                catch { }

                var (catKey, catLabel) = GetFileCategory(dirInfo.Name, true);
                StagedItems.Add(new StagedTransferItem
                {
                    FullPath = p,
                    RelativePath = FormatUnixParentPath(dirInfo.Parent?.FullName),
                    DisplayName = filesCount > 0 ? $"{dirInfo.Name} ({filesCount} archivos)" : dirInfo.Name,
                    IsDirectory = true,
                    SizeBytes = dirSize,
                    FormattedSize = FormatSize(dirSize),
                    IconText = "📁",
                    FileCategory = catKey,
                    CategoryLabel = catLabel,
                    Status = "En cola",
                    SelectionChanged = OnItemSelectionChanged
                });
                totalFilesAdded++;
            }
        }

        if (totalFilesAdded > 0 && TransferProgress >= 100.0)
        {
            TransferProgress = 0.0;
            Eta = "Listo";
        }

        UpdateStagedTotals();
        UpdateStatusMessage();
    }

    private void OnItemSelectionChanged()
    {
        if (_suppressSelectAllUpdate) return;
        UpdateStagedTotals();
        UpdateStatusMessage();
    }

    [RelayCommand]
    public void RemoveStagedItem(StagedTransferItem? item)
    {
        if (item != null)
        {
            StagedItems.Remove(item);
            UpdateStagedTotals();
            UpdateStatusMessage();
        }
    }

    [RelayCommand]
    public void ClearStagedFiles()
    {
        StagedItems.Clear();
        IsPaused = false;
        TransferProgress = 0.0;
        TransferSpeed = "0.0 MB/s";
        Eta = "En espera";
        TransferredBytesSummary = "0 B de 0 B";
        CurrentFileName = string.Empty;
        CurrentFileSizeText = string.Empty;
        UpdateStagedTotals();
        UpdateStatusMessage();
    }

    private void UpdateStagedTotals()
    {
        HasStagedItems = StagedItems.Count > 0;
        TotalStagedFilesCount = StagedItems.Count;
        var selectedItems = StagedItems.Where(i => i.IsSelected).ToList();
        SelectedStagedFilesCount = selectedItems.Count;
        long totalBytes = selectedItems.Sum(i => i.SizeBytes);
        TotalStagedSizeText = FormatSize(totalBytes);

        var allChecked = StagedItems.Count > 0 && selectedItems.Count == StagedItems.Count;
        if (_isAllSelected != allChecked)
        {
            _suppressSelectAllUpdate = true;
            SetProperty(ref _isAllSelected, allChecked, nameof(IsAllSelected));
            _suppressSelectAllUpdate = false;
        }

        if (!IsTransferring)
        {
            TransferredBytesSummary = $"0 B de {TotalStagedSizeText}";
            var first = selectedItems.FirstOrDefault() ?? StagedItems.FirstOrDefault();
            CurrentFileName = first?.DisplayName ?? string.Empty;
            CurrentFileSizeText = first?.FormattedSize ?? string.Empty;
        }

        OnPropertyChanged(nameof(QueueHeaderTitle));
        OnPropertyChanged(nameof(BatchSummarySubtitle));
        OnPropertyChanged(nameof(CanSendBatch));
    }

    private void UpdateStatusMessage()
    {
        if (!HasSelectedPeer)
        {
            StatusMessage = "Debes vincular un dispositivo en la pestaña 'Dispositivos' antes de poder enviar";
        }
        else if (!SelectedPeerItem!.IsOnline)
        {
            StatusMessage = $"El dispositivo vinculado '{SelectedPeerItem.DisplayName}' está desconectado de la LAN";
        }
        else if (!HasStagedItems)
        {
            StatusMessage = $"Destino verificado: {SelectedPeerItem.DisplayName}. Añade archivos o carpetas al lote";
        }
        else
        {
            StatusMessage = $"Listo para enviar {SelectedStagedFilesCount} elemento(s) ({TotalStagedSizeText}) a {SelectedPeerItem.DisplayName}";
        }
        OnPropertyChanged(nameof(BatchSummarySubtitle));
        OnPropertyChanged(nameof(CanSendBatch));
    }

    [RelayCommand]
    public void OpenSwitchDeviceDialog()
    {
        _ = ReloadTrustedPeersAsync();
        IsSwitchDeviceDialogOpen = true;
    }

    [RelayCommand]
    public void SelectPeerAndClose(PeerDeviceItem? item)
    {
        if (item != null && item.IsTrusted)
        {
            SelectedPeerItem = item;
        }
        IsSwitchDeviceDialogOpen = false;
    }

    public void SelectPeerById(string peerId)
    {
        var match = ActivePeers.FirstOrDefault(p => p.Peer.Id == peerId && p.IsTrusted);
        if (match != null)
        {
            SelectedPeerItem = match;
        }
    }

    [RelayCommand]
    public void ClearQueue(string criteria)
    {
        if (criteria == "completados")
        {
            var toRemove = StagedItems.Where(i => i.Status == "Completado").ToList();
            foreach (var item in toRemove) StagedItems.Remove(item);
        }
        else if (criteria == "errores")
        {
            var toRemove = StagedItems.Where(i => i.Status == "Error").ToList();
            foreach (var item in toRemove) StagedItems.Remove(item);
        }
        else if (criteria == "inactivos")
        {
            var toRemove = StagedItems.Where(i => i.Status != "En cola" && i.Status != "En progreso" && i.Status != "Enviando" && i.Status != "Enviando..." && i.Status != "Pausado").ToList();
            foreach (var item in toRemove) StagedItems.Remove(item);
        }
        else if (criteria == "todo")
        {
            StagedItems.Clear();
            IsPaused = false;
            TransferProgress = 0.0;
            TransferSpeed = "0.0 MB/s";
            Eta = "En espera";
            TransferredBytesSummary = "0 B de 0 B";
            CurrentFileName = string.Empty;
            CurrentFileSizeText = string.Empty;
        }
        UpdateStagedTotals();
        UpdateStatusMessage();
    }

    [RelayCommand]
    public void CloseSwitchDeviceDialog()
    {
        IsSwitchDeviceDialogOpen = false;
    }

    [RelayCommand]
    public async Task TogglePause()
    {
        if (!IsTransferring) return;
        IsPaused = !IsPaused;
        if (IsPaused)
        {
            TransferSpeed = "0.0 MB/s";
            Eta = "En pausa";
            StatusMessage = "Transferencia en pausa";
            var current = StagedItems.FirstOrDefault(i => i.IsCurrent);
            if (current != null) current.Status = "Pausado";
        }
        else
        {
            Eta = "Reanudando...";
            StatusMessage = $"Transfiriendo a {SelectedPeer?.DisplayName}...";
            var current = StagedItems.FirstOrDefault(i => i.IsCurrent);
            if (current != null) current.Status = "Enviando";
        }

        if (_engine != null)
        {
            await _engine.SetTransferPausedAsync(IsPaused);
        }
        OnPropertyChanged(nameof(BatchSummarySubtitle));
    }

    [RelayCommand]
    public async Task SendBatchAsync()
    {
        if (SelectedPeerItem == null || !SelectedPeerItem.IsTrusted || SelectedPeer == null)
        {
            StatusMessage = "Bloqueado: Solo puedes enviar archivos a dispositivos previamente vinculados.";
            OnPropertyChanged(nameof(BatchSummarySubtitle));
            return;
        }

        if (!SelectedPeerItem.IsOnline || string.IsNullOrWhiteSpace(SelectedPeer.IpAddress))
        {
            StatusMessage = $"El dispositivo vinculado '{SelectedPeer.DisplayName}' no está en línea en este momento.";
            OnPropertyChanged(nameof(BatchSummarySubtitle));
            return;
        }

        var selectedItems = StagedItems.Where(i => i.IsSelected && !string.IsNullOrEmpty(i.FullPath)).ToList();
        if (selectedItems.Count == 0)
        {
            StatusMessage = "Marca al menos un archivo o carpeta antes de enviar.";
            OnPropertyChanged(nameof(BatchSummarySubtitle));
            return;
        }

        if (_engine != null)
        {
            _transferCts = new CancellationTokenSource();
            IsPaused = false;
            IsTransferring = true;
            TransferProgress = 0;
            TransferSpeed = "0.0 MB/s";
            TransferredBytesSummary = $"0 B de {TotalStagedSizeText}";
            CurrentFileName = selectedItems[0].DisplayName;
            CurrentFileSizeText = selectedItems[0].FormattedSize;
            Eta = "Conectando...";
            StatusMessage = $"Conectando a {SelectedPeer.DisplayName} vía TLS 1.3...";
            OnPropertyChanged(nameof(BatchSummarySubtitle));

            var paths = selectedItems.Select(i => i.FullPath).ToList();

            foreach (var item in selectedItems)
            {
                item.Status = "En cola";
                item.ProgressPercent = 0;
                item.IsCompleted = false;
                item.IsCurrent = false;
            }

            try
            {
                var queued = await _engine.SendFilesAsync(SelectedPeer, paths, _transferCts.Token);
                if (queued)
                {
                    StatusMessage = $"Esperando confirmación o enviando a {SelectedPeer.DisplayName}...";
                    Eta = "En espera...";
                }
                else
                {
                    StatusMessage = "Error al iniciar la transferencia con el nodo remoto";
                    Eta = "Error";
                    IsTransferring = false;
                    foreach (var item in selectedItems.Where(i => i.Status != "Completado"))
                    {
                        item.Status = "Error";
                        item.IsCurrent = false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Transferencia cancelada por el usuario";
                Eta = "Cancelado";
                IsTransferring = false;
                foreach (var item in selectedItems.Where(i => i.Status != "Completado"))
                {
                    item.Status = "Cancelado";
                    item.IsCurrent = false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Eta = "Error";
                IsTransferring = false;
                foreach (var item in selectedItems.Where(i => i.Status != "Completado"))
                {
                    item.Status = "Error";
                    item.IsCurrent = false;
                }
            }
            finally
            {
                _transferCts = null;
                OnPropertyChanged(nameof(BatchSummarySubtitle));
                OnPropertyChanged(nameof(CanSendBatch));
            }
        }
    }

    [RelayCommand]
    public void CancelTransfer()
    {
        _ = _engine?.CancelOutgoingTransferAsync();
        _transferCts?.Cancel();

        IsPaused = false;
        IsTransferring = false;
        TransferProgress = 0;
        TransferSpeed = "0.0 MB/s";
        Eta = "Cancelado";
        StatusMessage = "Transferencia cancelada por el usuario";
        foreach (var item in StagedItems.Where(i => i.IsSelected && !i.IsCompleted))
        {
            item.Status = "Cancelado";
            item.IsCurrent = false;
        }
        OnPropertyChanged(nameof(BatchSummarySubtitle));
        OnPropertyChanged(nameof(CanSendBatch));
    }

    public static string FormatRemainingTime(TimeSpan remaining)
    {
        if (remaining.TotalSeconds <= 0 || double.IsInfinity(remaining.TotalSeconds) || double.IsNaN(remaining.TotalSeconds))
            return "--";

        if (remaining.TotalHours >= 1)
        {
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m {remaining.Seconds}s restantes";
        }
        if (remaining.TotalMinutes >= 1)
        {
            return $"{remaining.Minutes}m {remaining.Seconds}s restantes";
        }
        return $"{Math.Max(1, (int)remaining.TotalSeconds)}s restantes";
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }

    private static (string Key, string Label) GetFileCategory(string fileName, bool isDirectory)
    {
        if (isDirectory) return ("folder", "Carpeta");
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" => ("video", "Video"),
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".svg" => ("image", "Imagen"),
            ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".log" or ".doc" or ".docx" or ".pdf" => ("text", "Texto"),
            _ => ("file", "Archivo")
        };
    }

    private static string FormatUnixParentPath(string? dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath)) return "/";
        var normalized = dirPath.Replace('\\', '/').TrimEnd('/');
        var colonIdx = normalized.IndexOf(':');
        if (colonIdx >= 0 && colonIdx + 1 < normalized.Length)
        {
            normalized = normalized[(colonIdx + 1)..];
        }
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "/";
        var tail = parts.Length > 2 ? parts.Skip(parts.Length - 2) : parts;
        return "/" + string.Join("/", tail);
    }

    private static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".zip" or ".tar" or ".gz" or ".rar" or ".7z" => "📦",
            ".iso" or ".img" => "💿",
            ".mp4" or ".mkv" or ".mov" or ".avi" => "🎬",
            ".mp3" or ".wav" or ".flac" => "🎵",
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "🖼️",
            ".pdf" => "📕",
            ".doc" or ".docx" or ".txt" => "📄",
            ".exe" or ".deb" or ".apk" or ".msi" => "⚙️",
            _ => "📄"
        };
    }
}
