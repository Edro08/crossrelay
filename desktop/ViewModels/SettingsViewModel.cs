using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossRelay.Desktop.Models;
using CrossRelay.Desktop.Services;

namespace CrossRelay.Desktop.ViewModels;

public record PortDiagnosticItemViewModel(
    int Port,
    string PortLabel,
    string StateLabel,
    string CardBg,
    string CardBorder,
    string StateFg);

public partial class SettingsViewModel : ViewModelBase
{
    private readonly CrossRelayEngine? _engine;
    private readonly FileSettingsRepository _settingsRepo = new();
    private readonly FileBlacklistRepository _blacklistRepo = new();

    [ObservableProperty]
    private string _deviceName = Environment.MachineName;

    [ObservableProperty]
    private int _listenPort = 4242;

    [ObservableProperty]
    private string _downloadDirectory = StoragePaths.GetDefaultDownloadPath();

    [ObservableProperty]
    private int _selectedVisibilityIndex = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPortRangeEditable))]
    private int _selectedPortModeIndex = 0; // 0: auto, 1: fixed, 2: range

    partial void OnSelectedPortModeIndexChanged(int value)
    {
        PortRangeText = value switch
        {
            1 => ListenPort.ToString(),
            2 => $"{ListenPort}-{(PortRangeEnd >= ListenPort ? PortRangeEnd : ListenPort + 6)}",
            _ => "4242, 4244-4248"
        };
    }

    public bool IsPortRangeEditable => SelectedPortModeIndex != 0;

    [ObservableProperty]
    private int _portRangeEnd = 4248;

    [ObservableProperty]
    private string _portRangeText = "4242, 4244-4248";

    [ObservableProperty]
    private string _activePortBadgeText = "● Escuchando en :4242 · UDP :4243";

    [ObservableProperty]
    private int _selectedDuplicatePolicyIndex = 0; // 0: rename, 1: overwrite, 2: skip

    [ObservableProperty]
    private bool _bringToFrontOnRequest = true;

    [ObservableProperty]
    private bool _isPortDiagnosticsModalOpen;

    [ObservableProperty]
    private ObservableCollection<PortDiagnosticItemViewModel> _portDiagnosticItems = new();

    [ObservableProperty]
    private string _firewallStatusText = "Verificando regla de firewall...";

    [ObservableProperty]
    private string _firewallStatusBg = "#131316";

    [ObservableProperty]
    private string _firewallStatusBorder = "#23222B";

    [ObservableProperty]
    private string _firewallStatusFg = "#A1A1AA";

    [ObservableProperty]
    private string _lanProbeIp = string.Empty;

    [ObservableProperty]
    private string _lanProbeResult = "Consejo: Un bloqueo entrante real solo se confirma probando desde otro dispositivo en la red.";

    [ObservableProperty]
    private string _lanProbeResultFg = "#A1A1AA";

    public string UfwHelpCommand => "sudo ufw allow 4242,4244:4248/tcp && sudo ufw allow 4243/udp";
    public string FirewalldHelpCommand => "sudo firewall-cmd --add-port=4242/tcp --add-port=4244-4248/tcp --add-port=4243/udp --permanent && sudo firewall-cmd --reload";
    public string WindowsFwHelpCommand => "netsh advfirewall firewall add rule name=\"CrossRelay\" dir=in action=allow protocol=TCP localport=4242,4244-4248";

    [ObservableProperty]
    private BandwidthLimitMode _limitMode = BandwidthLimitMode.Unlimited;

    public bool IsUnlimited => LimitMode == BandwidthLimitMode.Unlimited;
    public bool Is100MBps => LimitMode == BandwidthLimitMode.Rate100MBps;
    public bool Is50MBps => LimitMode == BandwidthLimitMode.Rate50MBps;
    public bool Is10MBps => LimitMode == BandwidthLimitMode.Rate10MBps;

    public string UnlimitedBg => IsUnlimited ? "#1E1738" : "#131316";
    public string UnlimitedBorder => IsUnlimited ? "#582BF5" : "#23222B";
    public string UnlimitedSubFg => IsUnlimited ? "#C185FC" : "#71717A";

    public string Rate100Bg => Is100MBps ? "#1E1738" : "#131316";
    public string Rate100Border => Is100MBps ? "#582BF5" : "#23222B";
    public string Rate100SubFg => Is100MBps ? "#C185FC" : "#71717A";

    public string Rate50Bg => Is50MBps ? "#1E1738" : "#131316";
    public string Rate50Border => Is50MBps ? "#582BF5" : "#23222B";
    public string Rate50SubFg => Is50MBps ? "#C185FC" : "#71717A";

    public string Rate10Bg => Is10MBps ? "#1E1738" : "#131316";
    public string Rate10Border => Is10MBps ? "#582BF5" : "#23222B";
    public string Rate10SubFg => Is10MBps ? "#C185FC" : "#71717A";

    private void NotifyLimitProperties()
    {
        OnPropertyChanged(nameof(IsUnlimited));
        OnPropertyChanged(nameof(Is100MBps));
        OnPropertyChanged(nameof(Is50MBps));
        OnPropertyChanged(nameof(Is10MBps));
        OnPropertyChanged(nameof(UnlimitedBg));
        OnPropertyChanged(nameof(UnlimitedBorder));
        OnPropertyChanged(nameof(UnlimitedSubFg));
        OnPropertyChanged(nameof(Rate100Bg));
        OnPropertyChanged(nameof(Rate100Border));
        OnPropertyChanged(nameof(Rate100SubFg));
        OnPropertyChanged(nameof(Rate50Bg));
        OnPropertyChanged(nameof(Rate50Border));
        OnPropertyChanged(nameof(Rate50SubFg));
        OnPropertyChanged(nameof(Rate10Bg));
        OnPropertyChanged(nameof(Rate10Border));
        OnPropertyChanged(nameof(Rate10SubFg));
    }

    [RelayCommand]
    public void SelectBandwidthMode(string mode)
    {
        LimitMode = mode switch
        {
            "100" => BandwidthLimitMode.Rate100MBps,
            "50" => BandwidthLimitMode.Rate50MBps,
            "10" => BandwidthLimitMode.Rate10MBps,
            _ => BandwidthLimitMode.Unlimited
        };
        NotifyLimitProperties();
    }

    [ObservableProperty]
    private double _customLimitMBps = 100.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlacklistedPeers))]
    [NotifyPropertyChangedFor(nameof(BlacklistedCountText))]
    private ObservableCollection<string> _blacklistedPeers = new();

    public bool HasBlacklistedPeers => BlacklistedPeers.Count > 0;
    public string BlacklistedCountText => BlacklistedPeers.Count == 1 ? "1 nodo" : $"{BlacklistedPeers.Count} nodos";

    [ObservableProperty]
    private string _statusFeedback = "✓ Sistema sincronizado en caliente";

    public event Action<LocalNodeIdentity>? SettingsSaved;

    public SettingsViewModel(CrossRelayEngine? engine = null)
    {
        _engine = engine;
        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        var activePortFromCore = settings.Port;
        try
        {
            var statusJson = await CoreProcessClient.Instance.GetStatusAsync();
            if (statusJson.TryGetProperty("activePort", out var apProp) && apProp.GetInt32() > 0)
            {
                activePortFromCore = apProp.GetInt32();
            }

            var coreSettings = await CoreProcessClient.Instance.GetSettingsAsync();
            if (coreSettings.TryGetProperty("deviceName", out var devProp) && !string.IsNullOrWhiteSpace(devProp.GetString()))
            {
                var coreName = devProp.GetString()!;
                if (!string.Equals(coreName, "CrossRelay Device", StringComparison.OrdinalIgnoreCase))
                {
                    settings.DeviceName = coreName;
                }
            }
            if (coreSettings.TryGetProperty("portMode", out var pmProp) && !string.IsNullOrWhiteSpace(pmProp.GetString()))
            {
                settings.PortMode = pmProp.GetString()!;
            }
            if (coreSettings.TryGetProperty("portRangeEnd", out var preProp) && preProp.GetInt32() > 0)
            {
                settings.PortRangeEnd = preProp.GetInt32();
            }
            if (coreSettings.TryGetProperty("duplicatePolicy", out var dpProp) && !string.IsNullOrWhiteSpace(dpProp.GetString()))
            {
                settings.DuplicatePolicy = dpProp.GetString()!;
            }
        }
        catch { }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DeviceName = settings.DeviceName;
            ListenPort = settings.Port;
            PortRangeEnd = settings.PortRangeEnd > 0 ? settings.PortRangeEnd : 4248;
            SelectedPortModeIndex = settings.PortMode switch
            {
                "fixed" => 1,
                "range" => 2,
                _ => 0
            };
            PortRangeText = SelectedPortModeIndex switch
            {
                1 => ListenPort.ToString(),
                2 => $"{ListenPort}-{PortRangeEnd}",
                _ => "4242, 4244-4248"
            };
            ActivePortBadgeText = $"● Escuchando en :{activePortFromCore} · UDP :4243";
            SelectedDuplicatePolicyIndex = settings.DuplicatePolicy switch
            {
                "overwrite" => 1,
                "skip" => 2,
                _ => 0
            };
            DownloadDirectory = !string.IsNullOrEmpty(settings.DefaultDownloadDirectory)
                ? settings.DefaultDownloadDirectory
                : StoragePaths.GetDefaultDownloadPath();
            LimitMode = settings.BandwidthLimit;
            CustomLimitMBps = settings.CustomBandwidthLimitMBps;
            SelectedVisibilityIndex = (int)settings.VisibilityMode;
            BringToFrontOnRequest = settings.BringToFrontOnRequest;
            NotifyLimitProperties();
        });

        var blocked = await _blacklistRepo.GetAllBlockedAsync();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            BlacklistedPeers.Clear();
            foreach (var b in blocked)
            {
                BlacklistedPeers.Add(b);
            }
            OnPropertyChanged(nameof(HasBlacklistedPeers));
            OnPropertyChanged(nameof(BlacklistedCountText));
        });
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        settings.DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName.Trim();
        ParsePortConfiguration(out var portMode, out var startPort, out var endPort);
        ListenPort = startPort;
        PortRangeEnd = endPort;
        settings.PortMode = portMode;
        settings.Port = startPort;
        settings.PortRangeEnd = endPort;
        settings.DuplicatePolicy = SelectedDuplicatePolicyIndex switch
        {
            1 => "overwrite",
            2 => "skip",
            _ => "rename"
        };
        settings.DefaultDownloadDirectory = DownloadDirectory;
        settings.BandwidthLimit = LimitMode;
        settings.CustomBandwidthLimitMBps = CustomLimitMBps;
        settings.VisibilityMode = (NodeVisibilityMode)SelectedVisibilityIndex;
        settings.BringToFrontOnRequest = BringToFrontOnRequest;

        await _settingsRepo.SaveSettingsAsync(settings);
        _engine?.UpdateIdentity(settings);
        SettingsSaved?.Invoke(settings);

        try
        {
            await CoreProcessClient.Instance.SaveSettingsAsync(new
            {
                deviceId = settings.Id,
                deviceName = settings.DeviceName,
                port = settings.Port,
                portMode = settings.PortMode,
                portRangeEnd = settings.PortRangeEnd,
                duplicatePolicy = settings.DuplicatePolicy,
                downloadDirectory = settings.DefaultDownloadDirectory,
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

        StatusFeedback = "✓ Ajustes guardados (si cambiaste el puerto, reinicia para re-enlazar).";
    }

    private void ParsePortConfiguration(out string portMode, out int startPort, out int endPort)
    {
        startPort = ListenPort > 0 && ListenPort <= 65535 ? ListenPort : 4242;
        endPort = PortRangeEnd >= startPort ? PortRangeEnd : 4248;

        switch (SelectedPortModeIndex)
        {
            case 1:
                portMode = "fixed";
                if (int.TryParse(PortRangeText.Trim(), out var fixedP) && fixedP is > 0 and <= 65535)
                {
                    startPort = fixedP;
                    endPort = fixedP;
                }
                break;
            case 2:
                portMode = "range";
                var parts = PortRangeText.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && int.TryParse(parts[0], out var sP) && sP is > 0 and <= 65535)
                {
                    startPort = sP;
                    endPort = sP;
                }
                if (parts.Length >= 2 && int.TryParse(parts[1], out var eP) && eP >= startPort && eP <= 65535)
                {
                    endPort = Math.Min(eP, startPort + 29);
                }
                break;
            default:
                portMode = "auto";
                startPort = 4242;
                endPort = 4248;
                break;
        }
    }

    private int[] GetConfiguredPortsForDiagnostics()
    {
        ParsePortConfiguration(out var mode, out var startPort, out var endPort);
        if (mode == "fixed") return new[] { startPort };
        if (mode == "range")
        {
            var list = new List<int>();
            for (int p = startPort; p <= endPort; p++) list.Add(p);
            return list.ToArray();
        }
        return new[] { 4242, 4244, 4245, 4246, 4247, 4248 };
    }

    [RelayCommand]
    public async Task UnblockPeerAsync(string peerId)
    {
        await _blacklistRepo.UnblockAsync(peerId);
        try
        {
            await CoreProcessClient.Instance.SetBlockedAsync(peerId, false);
        }
        catch { }
        BlacklistedPeers.Remove(peerId);
        OnPropertyChanged(nameof(HasBlacklistedPeers));
        OnPropertyChanged(nameof(BlacklistedCountText));
        _engine?.NotifyTrustedPeersChanged();
        StatusFeedback = $"Nodo {peerId} desbloqueado.";
    }

    [RelayCommand]
    public async Task OpenPortDiagnosticsAsync()
    {
        IsPortDiagnosticsModalOpen = true;
        await RunPortDiagnosticsAsync();
    }

    [RelayCommand]
    public void ClosePortDiagnostics()
    {
        IsPortDiagnosticsModalOpen = false;
    }

    [RelayCommand]
    public async Task RunPortDiagnosticsAsync()
    {
        var ports = GetConfiguredPortsForDiagnostics();
        var result = await CoreProcessClient.Instance.DiagnosePortsAsync(ports);
        PortDiagnosticItems.Clear();

        if (result == null)
        {
            FirewallStatusText = "No se pudo consultar al Core Go";
            FirewallStatusBg = "#1F1014";
            FirewallStatusBorder = "#3F1922";
            FirewallStatusFg = "#FCA5A5";
            return;
        }

        ActivePortBadgeText = $"● Escuchando en :{result.ActivePort} · UDP :4243";
        foreach (var item in result.Ports)
        {
            var vm = item.State switch
            {
                "active_self" => new PortDiagnosticItemViewModel(item.Port, $":{item.Port}", "● Activo (CrossRelay)", "#091914", "#133829", "#10B981"),
                "occupied" => new PortDiagnosticItemViewModel(item.Port, $":{item.Port}", "▲ Ocupado", "#1C1409", "#3F2A10", "#F59E0B"),
                _ => new PortDiagnosticItemViewModel(item.Port, $":{item.Port}", "○ Libre", "#131316", "#23222B", "#A1A1AA")
            };
            PortDiagnosticItems.Add(vm);
        }

        if (result.FirewallStatus == "allowed")
        {
            FirewallStatusText = "✓ Regla CrossRelay detectada";
            FirewallStatusBg = "#091914";
            FirewallStatusBorder = "#133829";
            FirewallStatusFg = "#10B981";
        }
        else if (result.FirewallStatus == "unknown")
        {
            FirewallStatusText = "⚠ Sin regla explícita 'CrossRelay'";
            FirewallStatusBg = "#1C1409";
            FirewallStatusBorder = "#3F2A10";
            FirewallStatusFg = "#F59E0B";
        }
        else
        {
            FirewallStatusText = "Verificar manualmente en UFW / Firewalld";
            FirewallStatusBg = "#131316";
            FirewallStatusBorder = "#23222B";
            FirewallStatusFg = "#A1A1AA";
        }
    }

    [RelayCommand]
    public async Task RunLanProbeAsync()
    {
        var raw = LanProbeIp.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            LanProbeResult = "Escribe una IP o IP:puerto de otro equipo en tu LAN.";
            LanProbeResultFg = "#F59E0B";
            return;
        }

        var ip = raw;
        var port = 0;
        var colonIdx = raw.LastIndexOf(':');
        if (colonIdx > 0 && colonIdx < raw.Length - 1 && int.TryParse(raw[(colonIdx + 1)..], out var p) && p is > 0 and <= 65535)
        {
            ip = raw[..colonIdx].Trim();
            port = p;
        }

        LanProbeResult = $"Probando conexión TLS 1.3 hacia {raw}...";
        LanProbeResultFg = "#C185FC";
        var ok = await CoreProcessClient.Instance.ProbePeerAsync(ip, port);
        if (ok)
        {
            LanProbeResult = $"✓ Conexión TLS 1.3 exitosa hacia {raw}. Sin bloqueo de firewall.";
            LanProbeResultFg = "#10B981";
        }
        else
        {
            LanProbeResult = $"✕ No se pudo contactar a {raw}. Revisa Firewall (UFW / Firewalld / Windows) o que CrossRelay esté abierto.";
            LanProbeResultFg = "#EF4444";
        }
    }

    [RelayCommand]
    public async Task CopyFirewallCmdAsync(string cmd)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(cmd);
                LanProbeResult = "✓ Comando de firewall copiado al portapapeles.";
                LanProbeResultFg = "#10B981";
            }
        }
        catch { }
    }

    [RelayCommand]
    public void OpenLogsFolder()
    {
        try
        {
            var logsDir = StoragePaths.GetLogsDirectory();
            if (Directory.Exists(logsDir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = logsDir,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }
}
