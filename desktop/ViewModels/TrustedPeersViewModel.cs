using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossRelay.Desktop.Models;
using CrossRelay.Desktop.Services;

namespace CrossRelay.Desktop.ViewModels;

public partial class TrustedPeerItemViewModel : ObservableObject
{
    public TrustedPeer Model { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(TileBg))]
    [NotifyPropertyChangedFor(nameof(TileBorder))]
    [NotifyPropertyChangedFor(nameof(IconColor))]
    [NotifyPropertyChangedFor(nameof(PolicyColor))]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DotColor))]
    [NotifyPropertyChangedFor(nameof(OnlineStatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(OnlineStatusBadgeBg))]
    [NotifyPropertyChangedFor(nameof(OnlineStatusBadgeBorder))]
    [NotifyPropertyChangedFor(nameof(OnlineStatusBadgeFg))]
    private bool _isOnline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnlineStatusBadgeText))]
    private string _ipAddress = string.Empty;

    [ObservableProperty]
    private int _port = 4242;

    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string Initials
    {
        get
        {
            var name = DisplayName.Trim();
            if (string.IsNullOrEmpty(name)) return "CR";
            if (name.Length >= 2) return name[..2].ToUpperInvariant();
            return name.ToUpperInvariant();
        }
    }

    public bool IsWindows => Model.OS == DeviceOperatingSystem.Windows || Model.OS == DeviceOperatingSystem.Unknown;
    public bool IsLinux => Model.OS == DeviceOperatingSystem.Linux;
    public bool IsAndroid => Model.OS == DeviceOperatingSystem.Android || Model.OS == DeviceOperatingSystem.Apple;

    public string PolicyText => Model.Policy == TransferPolicy.AutoAccept ? "Auto-Aceptar" : "Confirmar antes";
    public string PolicyColor => IsSelected ? "#C185FC" : "#A1A1AA";
    public string CardBorderColor => IsSelected ? "#582BF5" : "#23222B";
    public string TileBg => IsSelected ? "#1E1738" : "#18191E";
    public string TileBorder => IsSelected ? "#332763" : "#262B36";
    public string IconColor => IsSelected ? "#C185FC" : "#A1A1AA";
    public string DotColor => IsOnline ? "#10B981" : "#71717A";

    public string OnlineStatusBadgeText => IsOnline
        ? $"En línea ({(string.IsNullOrEmpty(IpAddress) ? "LAN" : IpAddress)})"
        : "Desconectado";
    public string OnlineStatusBadgeBg => IsOnline ? "#091914" : "#18191E";
    public string OnlineStatusBadgeBorder => IsOnline ? "#133829" : "#262B36";
    public string OnlineStatusBadgeFg => IsOnline ? "#10B981" : "#A1A1AA";

    public string ShortFingerprint
    {
        get
        {
            var fp = Model.CertificateFingerprint ?? string.Empty;
            var clean = fp.Replace("SHA256:", "", StringComparison.OrdinalIgnoreCase).Replace(":", "");
            if (clean.Length > 10)
            {
                return $"Fingerprint: {clean[..4].ToLowerInvariant()}...{clean[^4..].ToLowerInvariant()} • TLS 1.3 ECDSA";
            }
            return "Fingerprint: verificado • TLS 1.3 ECDSA";
        }
    }

    public TrustedPeerItemViewModel(TrustedPeer model, bool isOnline = false, string ipAddress = "", int port = 4242)
    {
        Model = model;
        _isOnline = isOnline;
        _ipAddress = ipAddress;
        _port = port;
    }

    public void RefreshUI()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(PolicyText));
    }
}

public partial class TrustedPeersViewModel : ViewModelBase
{
    private readonly CrossRelayEngine? _engine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrustedPeersCount))]
    [NotifyPropertyChangedFor(nameof(HasTrustedPeers))]
    private ObservableCollection<TrustedPeerItemViewModel> _trustedPeers = new();

    public int TrustedPeersCount => TrustedPeers.Count;
    public bool HasTrustedPeers => TrustedPeers.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPeer))]
    [NotifyPropertyChangedFor(nameof(SelectedPeerHeaderTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedPeerInitials))]
    [NotifyPropertyChangedFor(nameof(SelectedPeerFingerprintText))]
    [NotifyPropertyChangedFor(nameof(SelectedPeerOnlineBadgeText))]
    [NotifyPropertyChangedFor(nameof(SelectedPeerOnlineBadgeBg))]
    [NotifyPropertyChangedFor(nameof(SelectedPeerOnlineBadgeBorder))]
    [NotifyPropertyChangedFor(nameof(SelectedPeerOnlineBadgeFg))]
    private TrustedPeerItemViewModel? _selectedPeerItem;

    public bool HasSelectedPeer => SelectedPeerItem != null;
    public string SelectedPeerHeaderTitle => SelectedPeerItem != null
        ? $"Ficha de Dispositivo: {SelectedPeerItem.DisplayName}"
        : "Sin dispositivo seleccionado";
    public string SelectedPeerInitials => SelectedPeerItem?.Initials ?? "--";
    public string SelectedPeerFingerprintText => SelectedPeerItem?.ShortFingerprint ?? "Selecciona o vincula un equipo de tu red local";
    public string SelectedPeerOnlineBadgeText => SelectedPeerItem?.OnlineStatusBadgeText ?? "Sin vínculo";
    public string SelectedPeerOnlineBadgeBg => SelectedPeerItem?.OnlineStatusBadgeBg ?? "#18191E";
    public string SelectedPeerOnlineBadgeBorder => SelectedPeerItem?.OnlineStatusBadgeBorder ?? "#262B36";
    public string SelectedPeerOnlineBadgeFg => SelectedPeerItem?.OnlineStatusBadgeFg ?? "#A1A1AA";

    [ObservableProperty]
    private string _editedAlias = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskConfirmation))]
    [NotifyPropertyChangedFor(nameof(AutoAcceptBorderColor))]
    [NotifyPropertyChangedFor(nameof(AutoAcceptRadioBg))]
    [NotifyPropertyChangedFor(nameof(AutoAcceptRadioBorder))]
    [NotifyPropertyChangedFor(nameof(AskConfirmBorderColor))]
    [NotifyPropertyChangedFor(nameof(AskConfirmRadioBg))]
    [NotifyPropertyChangedFor(nameof(AskConfirmRadioBorder))]
    private bool _isAutoAccept = true;

    public bool IsAskConfirmation => !IsAutoAccept;
    public string AutoAcceptBorderColor => IsAutoAccept ? "#582BF5" : "#23222B";
    public string AutoAcceptRadioBg => IsAutoAccept ? "#582BF5" : "#23222B";
    public string AutoAcceptRadioBorder => IsAutoAccept ? "#C185FC" : "#71717A";

    public string AskConfirmBorderColor => !IsAutoAccept ? "#582BF5" : "#23222B";
    public string AskConfirmRadioBg => !IsAutoAccept ? "#582BF5" : "#23222B";
    public string AskConfirmRadioBorder => !IsAutoAccept ? "#C185FC" : "#71717A";

    [ObservableProperty]
    private string _dedicatedDownloadFolder = string.Empty;

    // Telemetría formateada real
    [ObservableProperty]
    private string _formattedTotalBytes = "0 B";

    [ObservableProperty]
    private string _formattedFilesCount = "0";

    [ObservableProperty]
    private string _formattedAverageSpeed = "0.0 MB/s";

    [ObservableProperty]
    private string _formattedLatency = "-- ms";

    [ObservableProperty]
    private string _statusFeedback = "✓ Nodo verificado y sincronizado";

    // MODAL: Vincular Nuevo Equipo (Descubrimiento de equipos NO vinculados + Entrada de PIN de 6 dígitos)
    [ObservableProperty]
    private bool _isPairNewDeviceModalOpen;

    [ObservableProperty]
    private ObservableCollection<PeerDeviceItem> _unpairedDiscoveredPeers = new();

    [ObservableProperty]
    private bool _isPinEntryStepVisible;

    [ObservableProperty]
    private string _pairingTargetName = string.Empty;

    [ObservableProperty]
    private string _enteredPin = string.Empty;

    [ObservableProperty]
    private string _pairingFeedback = string.Empty;

    [ObservableProperty]
    private bool _isPairingBusy;

    private PeerDevice? _pairingTargetPeer;

    public TrustedPeersViewModel(CrossRelayEngine? engine = null)
    {
        _engine = engine;
        _ = LoadPeersAsync();

        if (_engine != null)
        {
            _engine.PeerDiscovered += (_, dev) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var existing = TrustedPeers.FirstOrDefault(tp => string.Equals(tp.Id, dev.Id, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.IsOnline = true;
                        existing.IpAddress = dev.IpAddress;
                        existing.Port = dev.Port;
                    }
                    RefreshUnpairedPeers();
                });
            };

            _engine.PeerLost += (_, id) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var existing = TrustedPeers.FirstOrDefault(tp => string.Equals(tp.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.IsOnline = false;
                    }
                    RefreshUnpairedPeers();
                });
            };

            _engine.PeerPairingCompleted += (peer) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    await LoadPeersAsync();
                    RefreshUnpairedPeers();
                    StatusFeedback = $"✓ ¡Nodo '{peer.DisplayName}' vinculado exitosamente!";
                });
            };

            _engine.TrustedPeersChanged += () =>
            {
                _ = LoadPeersAsync();
            };
        }
    }

    public async Task LoadPeersAsync()
    {
        if (_engine == null) return;

        var peers = await _engine.TrustedPeersRepository.GetAllAsync().ConfigureAwait(false);
        var blocked = await _engine.BlacklistRepository.GetAllBlockedAsync().ConfigureAwait(false);
        var activeLan = _engine.ActivePeers.ToList();

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var prevSelectedId = SelectedPeerItem?.Id;
            TrustedPeers.Clear();

            foreach (var p in peers)
            {
                if (p.IsBlocked || blocked.Contains(p.Id, StringComparer.OrdinalIgnoreCase))
                    continue;

                var live = activeLan.FirstOrDefault(a => string.Equals(a.Id, p.Id, StringComparison.OrdinalIgnoreCase));
                var itemVm = new TrustedPeerItemViewModel(
                    p,
                    isOnline: live != null,
                    ipAddress: live?.IpAddress ?? string.Empty,
                    port: live?.Port ?? 4242);

                TrustedPeers.Add(itemVm);
            }

            OnPropertyChanged(nameof(TrustedPeersCount));
            OnPropertyChanged(nameof(HasTrustedPeers));

            if (prevSelectedId != null)
            {
                SelectedPeerItem = TrustedPeers.FirstOrDefault(tp => string.Equals(tp.Id, prevSelectedId, StringComparison.OrdinalIgnoreCase));
            }

            if (SelectedPeerItem == null && TrustedPeers.Count > 0)
            {
                SelectedPeerItem = TrustedPeers[0];
            }

            UpdateSelectionHighlights();
        });
    }

    private void UpdateSelectionHighlights()
    {
        foreach (var item in TrustedPeers)
        {
            item.IsSelected = SelectedPeerItem != null && string.Equals(item.Id, SelectedPeerItem.Id, StringComparison.OrdinalIgnoreCase);
        }
    }

    private string? _lastSelectedPeerId;

    partial void OnSelectedPeerItemChanged(TrustedPeerItemViewModel? value)
    {
        UpdateSelectionHighlights();

        if (value != null)
        {
            var m = value.Model;
            if (_lastSelectedPeerId != value.Id)
            {
                _lastSelectedPeerId = value.Id;
                EditedAlias = m.Alias ?? m.OriginalName;
            }
            IsAutoAccept = m.Policy == TransferPolicy.AutoAccept;
            DedicatedDownloadFolder = !string.IsNullOrEmpty(m.CustomDownloadDirectory)
                ? m.CustomDownloadDirectory
                : StoragePaths.GetDefaultDownloadPath();

            FormattedTotalBytes = SendViewModel.FormatSize(m.Stats.TotalBytesExchanged);
            FormattedFilesCount = $"{m.Stats.TotalFilesExchanged:N0}";
            FormattedAverageSpeed = $"{m.Stats.AverageSpeedMBps:F1} MB/s";
            FormattedLatency = m.Stats.LastLatencyMs > 0 ? $"{m.Stats.LastLatencyMs:F1} ms" : "-- ms";
            StatusFeedback = "✓ Nodo verificado y sincronizado";

            if (value.IsOnline && !string.IsNullOrEmpty(value.IpAddress) && _engine != null)
            {
                _ = MeasurePeerLatencyAsync(value);
            }
        }
        else
        {
            EditedAlias = string.Empty;
            DedicatedDownloadFolder = string.Empty;
            FormattedTotalBytes = "0 B";
            FormattedFilesCount = "0";
            FormattedAverageSpeed = "0.0 MB/s";
            FormattedLatency = "-- ms";
            StatusFeedback = "Sin dispositivo seleccionado";
        }
    }

    private async Task MeasurePeerLatencyAsync(TrustedPeerItemViewModel item)
    {
        if (_engine == null || string.IsNullOrEmpty(item.IpAddress)) return;
        try
        {
            var target = new PeerDevice
            {
                Id = item.Id,
                Name = item.DisplayName,
                IpAddress = item.IpAddress,
                Port = item.Port,
                CertificateFingerprint = item.Model.CertificateFingerprint
            };
            var rtt = await _engine.PingPeerAsync(target).ConfigureAwait(false);
            item.Model.Stats.LastLatencyMs = rtt;
            await _engine.TrustedPeersRepository.SaveAsync(item.Model).ConfigureAwait(false);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (SelectedPeerItem?.Id == item.Id)
                {
                    FormattedLatency = $"{rtt:F1} ms";
                }
            });
        }
        catch { }
    }

    public event Action<string>? QuickSendRequested;

    [RelayCommand]
    public void QuickSendToSelectedPeer()
    {
        if (SelectedPeerItem != null)
        {
            QuickSendRequested?.Invoke(SelectedPeerItem.Id);
        }
    }

    [RelayCommand]
    public void SelectTrustedPeer(TrustedPeerItemViewModel? item)
    {
        if (item != null)
        {
            SelectedPeerItem = item;
        }
    }

    [RelayCommand]
    public void SetPolicyAutoAccept()
    {
        IsAutoAccept = true;
        _ = SavePeerChangesAsync();
    }

    [RelayCommand]
    public void SetPolicyAskConfirmation()
    {
        IsAutoAccept = false;
        _ = SavePeerChangesAsync();
    }

    [RelayCommand]
    public void OpenPairNewDeviceModal()
    {
        _engine?.RefreshPeers();
        IsPinEntryStepVisible = false;
        EnteredPin = string.Empty;
        PairingFeedback = string.Empty;
        _pairingTargetPeer = null;
        RefreshUnpairedPeers();
        IsPairNewDeviceModalOpen = true;
    }

    [RelayCommand]
    public void ClosePairNewDeviceModal()
    {
        IsPairNewDeviceModalOpen = false;
        IsPinEntryStepVisible = false;
        EnteredPin = string.Empty;
        _pairingTargetPeer = null;
    }

    [ObservableProperty]
    private string _manualIpAddress = string.Empty;

    [RelayCommand]
    public void RefreshRadar()
    {
        _engine?.RefreshPeers();
        RefreshUnpairedPeers();
    }

    [RelayCommand]
    public async Task ConnectByIpAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualIpAddress) || _engine == null) return;
        var raw = ManualIpAddress.Trim();
        var ip = raw;
        var port = 0; // 0 = scan P2P pool (4242, 4244..4248)

        var colonIdx = raw.LastIndexOf(':');
        if (colonIdx > 0 && colonIdx < raw.Length - 1 &&
            int.TryParse(raw[(colonIdx + 1)..], out var parsedPort) &&
            parsedPort is > 0 and <= 65535)
        {
            ip = raw[..colonIdx].Trim();
            port = parsedPort;
        }

        PairingFeedback = port > 0
            ? $"Conectando directamente con {ip}:{port}..."
            : $"Escaneando puertos P2P (4242, 4244..4248) en {ip}...";

        var ok = await _engine.ProbePeerAsync(ip, port);
        if (ok)
        {
            ManualIpAddress = string.Empty;
            PairingFeedback = port > 0
                ? $"✓ Nodo en {ip}:{port} detectado con éxito."
                : $"✓ Nodo en {ip} detectado con éxito.";
            await Task.Delay(300);
            RefreshUnpairedPeers();
        }
        else
        {
            PairingFeedback = port > 0
                ? $"No se pudo contactar a {ip}:{port}. Verifica firewall o que CrossRelay esté abierto."
                : $"No se pudo contactar a {ip} en los puertos 4242, 4244..4248. Verifica firewall o que CrossRelay esté abierto.";
        }
    }

    private void RefreshUnpairedPeers()
    {
        if (_engine == null) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            var trustedList = await _engine.TrustedPeersRepository.GetAllAsync();
            var blockedList = await _engine.BlacklistRepository.GetAllBlockedAsync();
            var trustedIds = trustedList.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            UnpairedDiscoveredPeers.Clear();
            foreach (var peer in _engine.ActivePeers)
            {
                if (trustedIds.Contains(peer.Id) || blockedList.Contains(peer.Id, StringComparer.OrdinalIgnoreCase))
                    continue;

                UnpairedDiscoveredPeers.Add(new PeerDeviceItem(peer, isTrusted: false, isOnline: true));
            }
        });
    }

    [RelayCommand]
    public async Task InitiatePinPairingAsync(PeerDeviceItem? item)
    {
        if (item == null || _engine == null) return;

        _pairingTargetPeer = item.Peer;
        PairingTargetName = item.DisplayName;
        EnteredPin = string.Empty;
        PairingFeedback = $"Solicitando código PIN de 6 dígitos a '{item.DisplayName}'...";
        IsPinEntryStepVisible = true;
        IsPairingBusy = true;

        try
        {
            var ok = await _engine.InitiatePairingAsync(item.Peer);
            if (ok)
            {
                PairingFeedback = $"Mira la pantalla de '{item.DisplayName}' y digita el PIN de 6 dígitos (expira en 60s):";
            }
            else
            {
                PairingFeedback = $"El equipo '{item.DisplayName}' rechazó o no respondió a la solicitud.";
            }
        }
        catch (Exception ex)
        {
            PairingFeedback = $"Error de conexión TLS 1.3: {ex.Message}";
        }
        finally
        {
            IsPairingBusy = false;
        }
    }

    [RelayCommand]
    public async Task VerifyEnteredPinAsync()
    {
        if (_pairingTargetPeer == null || _engine == null) return;
        var cleanPin = EnteredPin?.Trim() ?? string.Empty;
        if (cleanPin.Length != 6 || !cleanPin.All(char.IsDigit))
        {
            PairingFeedback = "Debes digitar exactamente los 6 números del PIN.";
            return;
        }

        IsPairingBusy = true;
        PairingFeedback = "Verificando PIN criptográfico vía TLS 1.3...";

        try
        {
            var success = await _engine.CompletePairingAsync(_pairingTargetPeer, cleanPin);
            if (success)
            {
                PairingFeedback = $"✓ ¡'{_pairingTargetPeer.DisplayName}' vinculado con éxito!";
                await LoadPeersAsync();
                RefreshUnpairedPeers();
                await Task.Delay(600);
                ClosePairNewDeviceModal();
            }
            else
            {
                PairingFeedback = "PIN incorrecto o expirado. Verifica los dígitos en el otro equipo.";
            }
        }
        catch (Exception ex)
        {
            PairingFeedback = $"Fallo al verificar PIN: {ex.Message}";
        }
        finally
        {
            IsPairingBusy = false;
        }
    }

    [RelayCommand]
    public void BackToRadarList()
    {
        IsPinEntryStepVisible = false;
        EnteredPin = string.Empty;
        _pairingTargetPeer = null;
        RefreshUnpairedPeers();
    }

    [RelayCommand]
    public async Task SavePeerChangesAsync()
    {
        if (SelectedPeerItem == null || _engine == null) return;

        var model = SelectedPeerItem.Model;
        model.Alias = string.IsNullOrWhiteSpace(EditedAlias) ? model.OriginalName : EditedAlias.Trim();
        model.Policy = IsAutoAccept ? TransferPolicy.AutoAccept : TransferPolicy.AskConfirmation;
        model.CustomDownloadDirectory = DedicatedDownloadFolder;

        await _engine.TrustedPeersRepository.SaveAsync(model);
        await _engine.SyncTrustedPeerAsync(model);
        SelectedPeerItem.RefreshUI();
        OnPropertyChanged(nameof(SelectedPeerHeaderTitle));
        OnPropertyChanged(nameof(SelectedPeerInitials));

        _engine.NotifyTrustedPeersChanged();
        StatusFeedback = "✓ Cambios guardados y sincronizados.";
    }

    [RelayCommand]
    public async Task DeletePeerAsync()
    {
        if (SelectedPeerItem == null || _engine == null) return;

        var id = SelectedPeerItem.Id;
        var name = SelectedPeerItem.DisplayName;
        await _engine.TrustedPeersRepository.DeleteAsync(id);
        await _engine.DeleteTrustedPeerInCoreAsync(id);
        TrustedPeers.Remove(SelectedPeerItem);
        OnPropertyChanged(nameof(TrustedPeersCount));
        OnPropertyChanged(nameof(HasTrustedPeers));

        SelectedPeerItem = TrustedPeers.Count > 0 ? TrustedPeers[0] : null;
        _engine.NotifyTrustedPeersChanged();
        StatusFeedback = $"Dispositivo '{name}' desvinculado. Ya no podrá enviar ni recibir archivos.";
    }

    [RelayCommand]
    public async Task BlockPeerAsync()
    {
        if (SelectedPeerItem == null || _engine == null) return;

        var id = SelectedPeerItem.Id;
        var name = SelectedPeerItem.DisplayName;
        SelectedPeerItem.Model.IsBlocked = true;
        await _engine.TrustedPeersRepository.SaveAsync(SelectedPeerItem.Model);
        await _engine.BlacklistRepository.BlockAsync(id, "Bloqueado manualmente por el usuario");
        await _engine.BlockTrustedPeerInCoreAsync(id, true);

        TrustedPeers.Remove(SelectedPeerItem);
        OnPropertyChanged(nameof(TrustedPeersCount));
        OnPropertyChanged(nameof(HasTrustedPeers));

        SelectedPeerItem = TrustedPeers.Count > 0 ? TrustedPeers[0] : null;
        _engine.NotifyTrustedPeersChanged();
        StatusFeedback = $"Nodo '{name}' bloqueado y revocado.";
    }
}
