using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossRelay.Desktop.Models;
using CrossRelay.Desktop.Services;

namespace CrossRelay.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly CrossRelayEngine _engine;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _currentTabName = "Enviar";

    public bool IsSendActive => CurrentTabName == "Enviar";
    public bool IsReceiveActive => CurrentTabName == "Recibir";
    public bool IsLinksActive => CurrentTabName == "Vínculos" || CurrentTabName == "Dispositivos";
    public bool IsHistoryActive => CurrentTabName == "Historial";
    public bool IsSettingsActive => CurrentTabName == "Ajustes";

    // Telemetría de la barra inferior (Bottom Status Bar)
    [ObservableProperty]
    private bool _isOnline = true;

    [ObservableProperty]
    private string _localNodeName = "CrossRelay PC";

    [ObservableProperty]
    private string _localIp = ResolveLocalIp();

    [ObservableProperty]
    private int _localPort = 4242;

    [ObservableProperty]
    private string _tlsStatus = "TLS 1.3 (ECDSA)";

    [ObservableProperty]
    private string _uploadSpeed = "↑ 0.0 MB/s";

    [ObservableProperty]
    private string _downloadSpeed = "↓ 0.0 MB/s";

    [ObservableProperty]
    private string _bandwidthLimitStatus = "Sin límite";

    // Diálogo Modal de PIN de 6 dígitos
    [ObservableProperty]
    private bool _isPinDialogOpen;

    [ObservableProperty]
    private string _pinRequesterName = string.Empty;

    [ObservableProperty]
    private string _pinCode = string.Empty;

    // Diálogo Modal de Confirmación de Transferencia Entrante
    [ObservableProperty]
    private bool _isIncomingTransferDialogOpen;

    [ObservableProperty]
    private string _incomingSenderName = string.Empty;

    [ObservableProperty]
    private string _incomingBatchTitle = string.Empty;

    [ObservableProperty]
    private string _incomingSizeText = string.Empty;

    [ObservableProperty]
    private string _incomingFilesCountText = string.Empty;

    private TaskCompletionSource<bool>? _incomingTransferTcs;
    private CancellationTokenSource? _autoToastHideCts;
    private bool _bringToFrontOnRequest = true;

    // Toast flotante no bloqueante para Descarga Automática (Opción B)
    [ObservableProperty]
    private bool _isAutoReceiveToastVisible;

    [ObservableProperty]
    private string _autoReceiveToastSender = string.Empty;

    [ObservableProperty]
    private string _autoReceiveToastTitle = string.Empty;

    [ObservableProperty]
    private string _autoReceiveToastMetrics = "Iniciando...";

    [ObservableProperty]
    private double _autoReceiveToastProgress;

    // Sub ViewModels
    public SendViewModel SendVm { get; }
    public ReceiveViewModel ReceiveVm { get; }
    public TrustedPeersViewModel TrustedPeersVm { get; }
    public HistoryViewModel HistoryVm { get; }
    public SettingsViewModel SettingsVm { get; }

    public MainViewModel()
    {
        _engine = new CrossRelayEngine();
        
        SendVm = new SendViewModel(_engine);
        ReceiveVm = new ReceiveViewModel(_engine);
        TrustedPeersVm = new TrustedPeersViewModel(_engine);
        HistoryVm = new HistoryViewModel(_engine);
        SettingsVm = new SettingsViewModel(_engine);

        TrustedPeersVm.QuickSendRequested += peerId =>
        {
            SendVm.SelectPeerById(peerId);
            Navigate("Enviar");
        };

        _currentPage = SendVm;

        SettingsVm.SettingsSaved += (settings) =>
        {
            LocalNodeName = settings.DeviceName;
            LocalPort = settings.Port;
            ReceiveVm.DownloadPath = settings.DefaultDownloadDirectory;
            _bringToFrontOnRequest = settings.BringToFrontOnRequest;
            BandwidthLimitStatus = settings.BandwidthLimit switch
            {
                BandwidthLimitMode.Unlimited => "Sin límite",
                BandwidthLimitMode.Rate100MBps => "Máx 100 MB/s",
                BandwidthLimitMode.Rate50MBps => "Máx 50 MB/s",
                BandwidthLimitMode.Rate10MBps => "Máx 10 MB/s",
                _ => "Personalizado"
            };
        };

        // Suscribirse a eventos del engine
        _engine.PinChallengeReceived += OnPinChallengeReceived;
        _engine.PeerPairingCompleted += _ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsPinDialogOpen = false;
            });
        };
        _engine.TransferProgressChanged += OnTransferProgressChanged;
        _engine.OutgoingPauseChanged += paused =>
        {
            if (paused)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => UploadSpeed = "↑ 0.0 MB/s");
            }
        };
        _engine.OutgoingTransferCompleted += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UploadSpeed = "↑ 0.0 MB/s");
        };
        _engine.TransferConfirmationRequested += OnTransferConfirmationRequested;
        _engine.AutoTransferStarted += OnAutoTransferStarted;
        _engine.IncomingTransferProgressChanged += OnIncomingTransferProgressChanged;
        _engine.IncomingTransferCompleted += OnIncomingTransferCompleted;
        _engine.IncomingTransferExpired += OnIncomingTransferExpired;

        _ = InitializeEngineAsync();
    }

    private async Task InitializeEngineAsync()
    {
        try
        {
            await _engine.StartAsync();
            if (_engine.LocalIdentity != null)
            {
                LocalNodeName = _engine.LocalIdentity.DeviceName;
                LocalPort = _engine.LocalIdentity.Port;
                _bringToFrontOnRequest = _engine.LocalIdentity.BringToFrontOnRequest;
                LocalIp = ResolveLocalIp();
                BandwidthLimitStatus = _engine.LocalIdentity.BandwidthLimit switch
                {
                    BandwidthLimitMode.Unlimited => "Sin límite",
                    BandwidthLimitMode.Rate100MBps => "Máx 100 MB/s",
                    BandwidthLimitMode.Rate50MBps => "Máx 50 MB/s",
                    BandwidthLimitMode.Rate10MBps => "Máx 10 MB/s",
                    _ => "Personalizado"
                };
            }
            IsOnline = true;
        }
        catch (Exception)
        {
            IsOnline = true;
            LocalNodeName = Environment.MachineName;
            LocalIp = ResolveLocalIp();
        }
    }

    private static string ResolveLocalIp()
    {
        try
        {
            var primary = new List<string>();
            var secondary = new List<string>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                if (IsVirtualOrIgnoredAdapter(ni.Name, ni.Description))
                    continue;

                var isEthernet = ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                    or NetworkInterfaceType.GigabitEthernet
                    or NetworkInterfaceType.FastEthernetT
                    or NetworkInterfaceType.FastEthernetFx;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    var ip = ua.Address.ToString();
                    var isLinkLocal = ip.StartsWith("169.254.", StringComparison.Ordinal);
                    if (isLinkLocal && !isEthernet)
                        continue;

                    if (isEthernet && !isLinkLocal)
                        primary.Insert(0, ip);
                    else if (isEthernet)
                        secondary.Add(ip);
                    else
                        primary.Add(ip);
                }
            }

            var candidates = primary.Concat(secondary).Distinct().Take(2).ToList();
            if (candidates.Count > 0)
                return string.Join(" / ", candidates);
        }
        catch { }

        return "127.0.0.1";
    }

    private static bool IsVirtualOrIgnoredAdapter(string name, string description)
    {
        var combined = $"{name} {description}";
        foreach (var marker in new[] { "Default Switch", "vEthernet", "Norton", "VPN", "Bluetooth", "Virtual", "WSL", "Docker", "VMware", "VirtualBox", "Loopback" })
        {
            if (combined.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void OnPinChallengeReceived(string requesterName, string pin)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            PinRequesterName = requesterName;
            PinCode = pin;
            IsPinDialogOpen = true;
            if (_bringToFrontOnRequest)
            {
                App.BringToFront();
            }
        });
    }

    private void OnTransferProgressChanged(object? sender, TransferProgressEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UploadSpeed = e.BytesTransferred >= e.TotalBytes ? "↑ 0.0 MB/s" : $"↑ {e.SpeedMBps:F1} MB/s";
        });
    }

    private async Task<bool> OnTransferConfirmationRequested(TransferRequestArgs args)
    {
        _incomingTransferTcs?.TrySetResult(false);
        var tcs = new TaskCompletionSource<bool>();
        _incomingTransferTcs = tcs;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IncomingSenderName = args.SenderName;
            IncomingBatchTitle = args.BatchTitle;
            IncomingSizeText = FormatFileSize(args.TotalBytes);
            IncomingFilesCountText = args.FilesCount == 1 ? "1 archivo" : $"{args.FilesCount} archivos";
            IsIncomingTransferDialogOpen = true;
            if (_bringToFrontOnRequest)
            {
                App.BringToFront();
            }
        });

        return await tcs.Task;
    }

    private void OnAutoTransferStarted(object? sender, TransferRequestArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _autoToastHideCts?.Cancel();
            _autoToastHideCts = null;

            var sizeLabel = FormatFileSize(e.TotalBytes);
            AutoReceiveToastSender = $"Recibiendo de {e.SenderName}";
            AutoReceiveToastTitle = $"{e.BatchTitle} ({sizeLabel})";
            AutoReceiveToastProgress = 0;
            AutoReceiveToastMetrics = "0% • Conectando...";
            IsAutoReceiveToastVisible = true;

            ShowNativeOsNotification(
                "Descarga automática en curso",
                $"{e.SenderName} te está enviando \"{e.BatchTitle}\" ({sizeLabel})");
        });
    }

    private void OnIncomingTransferProgressChanged(object? sender, IncomingTransferProgressEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DownloadSpeed = $"↓ {e.SpeedMBps:F1} MB/s";
            if (IsAutoReceiveToastVisible)
            {
                var pct = e.TotalBytes > 0 ? (double)e.BytesReceived / e.TotalBytes * 100.0 : 100.0;
                AutoReceiveToastProgress = pct;
                AutoReceiveToastMetrics = $"{pct:F0}% • {e.SpeedMBps:F1} MB/s";
            }
        });
    }

    private void OnIncomingTransferCompleted(object? sender, IncomingTransferProgressEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DownloadSpeed = "↓ 0.0 MB/s";
            if (IsAutoReceiveToastVisible)
            {
                AutoReceiveToastProgress = 100;
                AutoReceiveToastMetrics = "100% • Completado";
                ScheduleAutoToastHide(3000);
            }
        });
    }

    private void OnIncomingTransferExpired(string reason)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsIncomingTransferDialogOpen = false;
            _incomingTransferTcs?.TrySetResult(false);
            _incomingTransferTcs = null;

            AutoReceiveToastSender = "Solicitud expirada";
            AutoReceiveToastTitle = string.IsNullOrWhiteSpace(reason) ? "Tiempo de espera agotado" : reason;
            AutoReceiveToastProgress = 0;
            AutoReceiveToastMetrics = "La transferencia ya no está activa";
            IsAutoReceiveToastVisible = true;
            ScheduleAutoToastHide(3500);
        });
    }

    private void ScheduleAutoToastHide(int delayMs)
    {
        var cts = new CancellationTokenSource();
        _autoToastHideCts?.Cancel();
        _autoToastHideCts = cts;
        _ = Task.Delay(delayMs, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => IsAutoReceiveToastVisible = false);
            }
        });
    }

    [RelayCommand]
    public void DismissAutoReceiveToast()
    {
        _autoToastHideCts?.Cancel();
        IsAutoReceiveToastVisible = false;
    }

    [RelayCommand]
    public void OpenReceiveFromToast()
    {
        _autoToastHideCts?.Cancel();
        IsAutoReceiveToastVisible = false;
        Navigate("Recibir");
    }

    private static void ShowNativeOsNotification(string title, string body)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var psi = new ProcessStartInfo("notify-send")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("-a");
                psi.ArgumentList.Add("CrossRelay");
                psi.ArgumentList.Add(title);
                psi.ArgumentList.Add(body);
                Process.Start(psi);
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                var isHiddenOrMinimized =
                    Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime dt &&
                    dt.MainWindow is { } w &&
                    (!w.IsVisible || w.WindowState == Avalonia.Controls.WindowState.Minimized || !w.IsActive);

                if (!isHiddenOrMinimized) return;

                var psi = new ProcessStartInfo("powershell")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-WindowStyle");
                psi.ArgumentList.Add("Hidden");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(
                    "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null; " +
                    "$t = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                    "$nodes = $t.GetElementsByTagName('text'); " +
                    "$nodes.Item(0).AppendChild($t.CreateTextNode($env:CR_TOAST_TITLE)) > $null; " +
                    "$nodes.Item(1).AppendChild($t.CreateTextNode($env:CR_TOAST_BODY)) > $null; " +
                    "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('CrossRelay').Show([Windows.UI.Notifications.ToastNotification]::new($t));");
                psi.Environment["CR_TOAST_TITLE"] = title;
                psi.Environment["CR_TOAST_BODY"] = body;
                Process.Start(psi);
            }
        }
        catch { }
    }

    [RelayCommand]
    public void AcceptIncomingTransfer()
    {
        IsIncomingTransferDialogOpen = false;
        _incomingTransferTcs?.TrySetResult(true);
        Navigate("Recibir");
    }

    [RelayCommand]
    public void RejectIncomingTransfer()
    {
        IsIncomingTransferDialogOpen = false;
        _incomingTransferTcs?.TrySetResult(false);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }

    [RelayCommand]
    public void TogglePane()
    {
        IsPaneOpen = !IsPaneOpen;
    }

    [RelayCommand]
    public void Navigate(string destination)
    {
        CurrentTabName = destination;
        CurrentPage = destination switch
        {
            "Enviar" => SendVm,
            "Recibir" => ReceiveVm,
            "Vínculos" or "Dispositivos" => TrustedPeersVm,
            "Historial" => HistoryVm,
            "Ajustes" => SettingsVm,
            _ => SendVm
        };

        OnPropertyChanged(nameof(IsSendActive));
        OnPropertyChanged(nameof(IsReceiveActive));
        OnPropertyChanged(nameof(IsLinksActive));
        OnPropertyChanged(nameof(IsHistoryActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }

    [RelayCommand]
    public void ClosePinDialog()
    {
        IsPinDialogOpen = false;
        PinCode = string.Empty;
    }
}
