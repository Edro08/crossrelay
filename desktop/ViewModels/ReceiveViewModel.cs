using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossRelay.Desktop.Models;
using CrossRelay.Desktop.Services;

namespace CrossRelay.Desktop.ViewModels;

public partial class IncomingFileItem : ObservableObject
{
    public string DisplayName { get; init; } = string.Empty;
    public string RelativePath { get; init; } = "/";
    public long SizeBytes { get; init; }
    public string FormattedSize { get; init; } = "0 B";
    public string CategoryLabel { get; init; } = "Archivo";
    public string FileCategory { get; init; } = "file";

    public bool IsFolderCategory => FileCategory == "folder";
    public bool IsVideoCategory => FileCategory == "video";
    public bool IsImageCategory => FileCategory == "image";
    public bool IsFileCategory => !IsFolderCategory && !IsVideoCategory && !IsImageCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(StatusBg))]
    [NotifyPropertyChangedFor(nameof(StatusBorder))]
    private string _status = "En espera";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowBg))]
    private bool _isCurrent;

    public string RowBg => IsCurrent ? "#121020" : "Transparent";

    public string StatusColor => Status switch
    {
        "Verificado ✓" => "#10B981",
        "Recibiendo" => "#C185FC",
        "Error" => "#EF4444",
        "Cancelado" => "#FBBF24",
        _ => "#A1A1AA"
    };

    public string StatusBg => Status switch
    {
        "Verificado ✓" => "#091914",
        "Recibiendo" => "#1E1738",
        "Error" => "#1F1014",
        "Cancelado" => "#1F1A0F",
        _ => "#18191E"
    };

    public string StatusBorder => Status switch
    {
        "Verificado ✓" => "#133829",
        "Recibiendo" => "#332763",
        "Error" => "#3F1922",
        "Cancelado" => "#403318",
        _ => "#262B36"
    };

    public string TileBg => FileCategory switch
    {
        "folder" => "#241909",
        "video" => "#1E1738",
        "image" => "#091E17",
        _ => "#0C192E"
    };

    public string TileBorder => FileCategory switch
    {
        "folder" => "#4E3510",
        "video" => "#332763",
        "image" => "#133D2E",
        _ => "#1E3A5F"
    };

    public string TileIconColor => FileCategory switch
    {
        "folder" => "#FBBF24",
        "video" => "#C185FC",
        "image" => "#34D399",
        _ => "#60A5FA"
    };
}

public partial class ReceiveViewModel : ViewModelBase
{
    private readonly CrossRelayEngine? _engine;
    private readonly FileSettingsRepository _settingsRepo = new();

    [ObservableProperty]
    private string _downloadPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListenEndpointBadge))]
    private int _listenPort = 4242;

    public string ListenEndpointBadge => $"TLS 1.3 :{ListenPort}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchStatusText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusColor))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBg))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBorder))]
    private bool _hasActiveTransfer;

    [ObservableProperty]
    private string _activeTransferTitle = string.Empty;

    [ObservableProperty]
    private string _activeTransferSender = string.Empty;

    private const int MaxPreviewFiles = 200;
    private int _totalBatchFilesCount;

    public ObservableCollection<IncomingFileItem> IncomingFiles { get; } = new();

    public bool HasIncomingFiles => IncomingFiles.Count > 0;
    public bool IsListeningIdle => !HasIncomingFiles;

    public string QueueHeaderTitle => HasIncomingFiles
        ? $"Lote entrante: {ActiveTransferTitle}"
        : "Recepciones recientes";

    public string QueueSubtitle => HasIncomingFiles
        ? $"Origen verificado: {ActiveTransferSender} • Verificación SHA-256"
        : "Esperando conexiones TLS 1.3 de dispositivos vinculados";

    [ObservableProperty]
    private string _filesCounterText = "0 / 0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DonutSweepAngle))]
    [NotifyPropertyChangedFor(nameof(ActiveProgressPercentText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusColor))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBg))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBorder))]
    private double _activeProgressPercent;

    public double DonutSweepAngle => Math.Clamp(ActiveProgressPercent * 3.6, 0.0, 360.0);
    public string ActiveProgressPercentText => $"{(int)Math.Round(ActiveProgressPercent)}%";

    [ObservableProperty]
    private string _activeSpeedText = "0.0 MB/s";

    [ObservableProperty]
    private string _activeBytesSummary = "0 B de 0 B";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchStatusText))]
    [NotifyPropertyChangedFor(nameof(BatchStatusColor))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBg))]
    [NotifyPropertyChangedFor(nameof(BatchStatusBorder))]
    private string _activeEtaText = "En espera";

    [ObservableProperty]
    private string _currentFileName = "Ningún archivo en curso";

    [ObservableProperty]
    private string _currentFileSizeText = "--";

    [ObservableProperty]
    private string _lastCompletedText = "Última descarga: Ninguna en esta sesión";

    public string BatchStatusText
    {
        get
        {
            if (HasActiveTransfer) return "Recibiendo";
            if (ActiveEtaText is "Cancelado" or "Error") return ActiveEtaText;
            if (ActiveProgressPercent >= 100.0 && HasIncomingFiles) return "Completado";
            return "En espera";
        }
    }

    public string BatchStatusColor => BatchStatusText switch
    {
        "Completado" => "#10B981",
        "Recibiendo" => "#C185FC",
        "Cancelado" or "Error" => "#F87171",
        _ => "#A1A1AA"
    };

    public string BatchStatusBg => BatchStatusText switch
    {
        "Completado" => "#091914",
        "Recibiendo" => "#1E1738",
        "Cancelado" or "Error" => "#1F1014",
        _ => "#18191E"
    };

    public string BatchStatusBorder => BatchStatusText switch
    {
        "Completado" => "#133829",
        "Recibiendo" => "#332763",
        "Cancelado" or "Error" => "#3F1922",
        _ => "#262B36"
    };

    public ReceiveViewModel(CrossRelayEngine? engine = null)
    {
        _engine = engine;
        _downloadPath = StoragePaths.GetDefaultDownloadPath();

        _ = LoadSettingsAsync();

        if (!Directory.Exists(_downloadPath))
        {
            try { Directory.CreateDirectory(_downloadPath); } catch { }
        }

        if (_engine != null)
        {
            _engine.IncomingTransferStarted += (_, args) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    InitializeIncomingBatch(args.BatchTitle, args.SenderName, args.TotalBytes, args.FilesCount, args.Files);
                });
            };

            _engine.IncomingTransferProgressChanged += (_, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!HasIncomingFiles && e.Files.Count > 0)
                    {
                        InitializeIncomingBatch(e.BatchTitle, e.SenderName, e.TotalBytes, e.Files.Count, e.Files);
                    }

                    var pct = e.TotalBytes > 0 ? Math.Clamp(e.BytesReceived * 100.0 / e.TotalBytes, 0.0, 100.0) : 0.0;
                    HasActiveTransfer = e.TotalBytes == 0 || e.BytesReceived < e.TotalBytes;
                    ActiveTransferTitle = e.BatchTitle;
                    ActiveTransferSender = e.SenderName;
                    ActiveProgressPercent = pct;
                    ActiveSpeedText = $"{e.SpeedMBps:F1} MB/s";
                    ActiveBytesSummary = $"{SendViewModel.FormatSize(e.BytesReceived)} de {SendViewModel.FormatSize(e.TotalBytes)}";

                    if (pct >= 100.0)
                    {
                        ActiveEtaText = "Completado";
                    }
                    else if (e.RemainingTime > TimeSpan.Zero)
                    {
                        ActiveEtaText = SendViewModel.FormatRemainingTime(e.RemainingTime);
                    }
                    else
                    {
                        ActiveEtaText = "Recibiendo...";
                    }

                    long cumulative = 0;
                    bool currentAssigned = false;
                    int doneCount = 0;
                    foreach (var item in IncomingFiles)
                    {
                        var itemSize = Math.Max(1, item.SizeBytes);
                        cumulative += itemSize;
                        if (pct >= 100.0 || e.BytesReceived >= cumulative)
                        {
                            item.IsCurrent = false;
                            item.Status = "Verificado ✓";
                            doneCount++;
                        }
                        else if (!currentAssigned)
                        {
                            currentAssigned = true;
                            item.IsCurrent = true;
                            item.Status = "Recibiendo";
                            CurrentFileName = item.DisplayName;
                            CurrentFileSizeText = item.FormattedSize;
                        }
                        else
                        {
                            item.IsCurrent = false;
                            item.Status = "En espera";
                        }
                    }

                    if (_totalBatchFilesCount > 0)
                    {
                        var reportedDone = _totalBatchFilesCount > IncomingFiles.Count && e.TotalBytes > 0
                            ? (int)Math.Clamp(Math.Round((double)e.BytesReceived / e.TotalBytes * _totalBatchFilesCount), doneCount, _totalBatchFilesCount)
                            : doneCount;
                        FilesCounterText = $"{reportedDone} / {_totalBatchFilesCount}";
                    }

                    NotifyHeaderChanged();
                });
            };

            _engine.IncomingTransferCompleted += (_, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    HasActiveTransfer = false;
                    if (e.Success)
                    {
                        ActiveProgressPercent = 100.0;
                        var totalFmt = SendViewModel.FormatSize(e.TotalBytes);
                        ActiveBytesSummary = $"{totalFmt} de {totalFmt}";
                        ActiveSpeedText = $"{e.SpeedMBps:F1} MB/s";
                        ActiveEtaText = "Completado";
                        LastCompletedText = $"Última descarga: {e.BatchTitle} ({DateTime.Now:HH:mm:ss})";
                        foreach (var item in IncomingFiles)
                        {
                            item.IsCurrent = false;
                            item.Status = "Verificado ✓";
                        }
                        if (_totalBatchFilesCount > 0)
                        {
                            FilesCounterText = $"{_totalBatchFilesCount} / {_totalBatchFilesCount}";
                        }
                    }
                    else
                    {
                        var canceled = e.Error.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                                    || e.Error.Contains("interrupt", StringComparison.OrdinalIgnoreCase);
                        ActiveSpeedText = "0.0 MB/s";
                        ActiveEtaText = canceled ? "Cancelado" : "Error";
                        foreach (var item in IncomingFiles.Where(i => i.Status != "Verificado ✓"))
                        {
                            item.IsCurrent = false;
                            item.Status = canceled ? "Cancelado" : "Error";
                        }
                    }

                    NotifyHeaderChanged();
                });
            };
        }
    }

    private void InitializeIncomingBatch(string title, string sender, long totalBytes, int totalFilesCount, IReadOnlyList<IncomingFileEntry> files)
    {
        ActiveTransferTitle = title;
        ActiveTransferSender = sender;
        HasActiveTransfer = true;
        ActiveProgressPercent = 0;
        ActiveSpeedText = "0.0 MB/s";
        ActiveBytesSummary = $"0 B de {SendViewModel.FormatSize(totalBytes)}";
        ActiveEtaText = "Conectando...";
        _totalBatchFilesCount = Math.Max(totalFilesCount, files.Count);

        IncomingFiles.Clear();
        foreach (var file in files.Take(MaxPreviewFiles))
        {
            var normalized = file.Name.Replace('\\', '/');
            var lastSlash = normalized.LastIndexOf('/');
            var fileName = lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
            var parentRel = lastSlash > 0 ? "/" + normalized[..lastSlash] : "/";
            var (catKey, catLabel) = GetFileCategory(fileName);

            IncomingFiles.Add(new IncomingFileItem
            {
                DisplayName = fileName,
                RelativePath = parentRel,
                SizeBytes = file.SizeBytes,
                FormattedSize = SendViewModel.FormatSize(file.SizeBytes),
                FileCategory = catKey,
                CategoryLabel = catLabel,
                Status = "En espera"
            });
        }

        if (IncomingFiles.Count > 0)
        {
            IncomingFiles[0].IsCurrent = true;
            IncomingFiles[0].Status = "Recibiendo";
            CurrentFileName = IncomingFiles[0].DisplayName;
            CurrentFileSizeText = IncomingFiles[0].FormattedSize;
            FilesCounterText = $"0 / {_totalBatchFilesCount}";
        }
        else
        {
            CurrentFileName = title;
            CurrentFileSizeText = SendViewModel.FormatSize(totalBytes);
            FilesCounterText = "0 / 0";
        }

        NotifyHeaderChanged();
    }

    private void NotifyHeaderChanged()
    {
        OnPropertyChanged(nameof(HasIncomingFiles));
        OnPropertyChanged(nameof(IsListeningIdle));
        OnPropertyChanged(nameof(QueueHeaderTitle));
        OnPropertyChanged(nameof(QueueSubtitle));
        OnPropertyChanged(nameof(BatchStatusText));
        OnPropertyChanged(nameof(BatchStatusColor));
        OnPropertyChanged(nameof(BatchStatusBg));
        OnPropertyChanged(nameof(BatchStatusBorder));
    }

    private static (string Key, string Label) GetFileCategory(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" => ("video", "Video"),
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".svg" => ("image", "Imagen"),
            ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".log" or ".doc" or ".docx" or ".pdf" => ("text", "Texto"),
            _ => ("file", "Archivo")
        };
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrEmpty(settings.DefaultDownloadDirectory))
            {
                DownloadPath = settings.DefaultDownloadDirectory;
            }
            if (settings.Port > 0)
            {
                ListenPort = settings.Port;
            }
        });
    }

    partial void OnDownloadPathChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _ = Task.Run(async () =>
            {
                var settings = await _settingsRepo.GetSettingsAsync();
                settings.DefaultDownloadDirectory = value;
                await _settingsRepo.SaveSettingsAsync(settings);
                if (_engine != null)
                {
                    await _engine.SyncLocalSettingsToCoreAsync(settings);
                }
            });
        }
    }

    [RelayCommand]
    public void OpenDownloadFolder()
    {
        try
        {
            if (!Directory.Exists(DownloadPath))
            {
                Directory.CreateDirectory(DownloadPath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = DownloadPath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public void CancelActiveTransfer()
    {
        _ = _engine?.CancelIncomingTransferAsync();
        HasActiveTransfer = false;
        ActiveSpeedText = "0.0 MB/s";
        ActiveEtaText = "Cancelado";
        foreach (var item in IncomingFiles.Where(i => i.Status != "Verificado ✓"))
        {
            item.IsCurrent = false;
            item.Status = "Cancelado";
        }
        NotifyHeaderChanged();
    }
}
