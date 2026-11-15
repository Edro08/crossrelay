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

public class HistoryRecordItem
{
    public string Id { get; set; } = string.Empty;
    public string SessionTitle { get; set; } = string.Empty;
    public string PeerName { get; set; } = string.Empty;
    public bool IsSender { get; set; }
    public string DirectionPrefix => IsSender ? "Enviado a:" : "Recibido de:";
    public string TileBg => IsSender ? "#1E1738" : "#18191E";
    public string TileBorder => IsSender ? "#332763" : "#262B36";
    public string SizeFormatted { get; set; } = string.Empty;
    public string SpeedFormatted { get; set; } = string.Empty;
    public string StatusText { get; set; } = "✓ Completado";
    public string TargetPath { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string FormattedTimestamp
    {
        get
        {
            if (Timestamp.Date == DateTime.Today)
                return $"Hoy, {Timestamp:HH:mm}";
            if (Timestamp.Date == DateTime.Today.AddDays(-1))
                return $"Ayer, {Timestamp:HH:mm}";
            return Timestamp.ToString("dd/MM/yyyy, HH:mm");
        }
    }
    public string TitleWithSize => $"{SessionTitle} • {SizeFormatted}";
}

public partial class HistoryViewModel : ViewModelBase
{
    private readonly CrossRelayEngine? _engine;
    private readonly FileTransferHistoryRepository _historyRepo = new();
    private readonly List<HistoryRecordItem> _allRecords = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilteredRecords))]
    private ObservableCollection<HistoryRecordItem> _filteredRecords = new();

    public bool HasFilteredRecords => FilteredRecords.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllFilterActive))]
    [NotifyPropertyChangedFor(nameof(IsSentFilterActive))]
    [NotifyPropertyChangedFor(nameof(IsReceivedFilterActive))]
    [NotifyPropertyChangedFor(nameof(AllBtnBg))]
    [NotifyPropertyChangedFor(nameof(SentBtnBg))]
    [NotifyPropertyChangedFor(nameof(ReceivedBtnBg))]
    private string _activeFilter = "Todos";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value)
    {
        RefreshFilter();
    }

    public bool IsAllFilterActive => ActiveFilter == "Todos";
    public bool IsSentFilterActive => ActiveFilter == "Enviados";
    public bool IsReceivedFilterActive => ActiveFilter == "Recibidos";

    public string AllBtnBg => IsAllFilterActive ? "#582BF5" : "#18191E";
    public string SentBtnBg => IsSentFilterActive ? "#582BF5" : "#18191E";
    public string ReceivedBtnBg => IsReceivedFilterActive ? "#582BF5" : "#18191E";

    public string AllCountLabel => $"Todos ({_allRecords.Count})";
    public string SentCountLabel => $"Enviados ({_allRecords.Count(r => r.IsSender)})";
    public string ReceivedCountLabel => $"Recibidos ({_allRecords.Count(r => !r.IsSender)})";

    public HistoryViewModel(CrossRelayEngine? engine = null)
    {
        _engine = engine;
        _ = LoadHistoryAsync();

        if (_engine != null)
        {
            _engine.TransferProgressChanged += (s, e) =>
            {
                if (e.ProgressPercentage >= 100.0)
                {
                    var rec = new PersistedHistoryRecord
                    {
                        Id = e.SessionId,
                        SessionTitle = e.CurrentFileName,
                        PeerName = "Dispositivo Vinculado",
                        IsSender = true,
                        TotalBytes = e.TotalBytes,
                        FilesCount = 1,
                        SpeedMBps = e.SpeedMBps,
                        StatusText = "✓ Completado",
                        TargetPath = StoragePaths.GetDefaultDownloadPath(),
                        Timestamp = DateTime.Now
                    };
                    _ = AppendRecordAsync(rec);
                }
            };

            _engine.IncomingTransferCompleted += (s, e) =>
            {
                var rec = new PersistedHistoryRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionTitle = e.BatchTitle,
                    PeerName = e.SenderName,
                    IsSender = false,
                    TotalBytes = e.TotalBytes,
                    FilesCount = 1,
                    SpeedMBps = e.SpeedMBps,
                    StatusText = "✓ Completado",
                    TargetPath = !string.IsNullOrEmpty(e.TargetDirectory) ? e.TargetDirectory : StoragePaths.GetDefaultDownloadPath(),
                    Timestamp = DateTime.Now
                };
                _ = AppendRecordAsync(rec);
            };
        }
    }

    public async Task LoadHistoryAsync()
    {
        var persisted = await _historyRepo.GetAllAsync().ConfigureAwait(false);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _allRecords.Clear();
            foreach (var p in persisted)
            {
                _allRecords.Add(MapToItem(p));
            }
            RefreshFilter();
        });
    }

    private async Task AppendRecordAsync(PersistedHistoryRecord record)
    {
        await _historyRepo.AddRecordAsync(record).ConfigureAwait(false);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_allRecords.Any(r => r.Id == record.Id)) return;
            _allRecords.Insert(0, MapToItem(record));
            RefreshFilter();
        });
    }

    private static HistoryRecordItem MapToItem(PersistedHistoryRecord p)
    {
        return new HistoryRecordItem
        {
            Id = p.Id,
            SessionTitle = p.SessionTitle,
            PeerName = p.PeerName,
            IsSender = p.IsSender,
            SizeFormatted = SendViewModel.FormatSize(p.TotalBytes),
            SpeedFormatted = p.SpeedMBps > 0 ? $"{p.SpeedMBps:F1} MB/s" : "-- MB/s",
            StatusText = p.StatusText.StartsWith("✓") ? p.StatusText : $"✓ {p.StatusText}",
            TargetPath = p.TargetPath,
            Timestamp = p.Timestamp
        };
    }

    private void RefreshFilter()
    {
        FilteredRecords.Clear();
        IEnumerable<HistoryRecordItem> query = _allRecords;
        if (ActiveFilter == "Enviados")
            query = query.Where(r => r.IsSender);
        else if (ActiveFilter == "Recibidos")
            query = query.Where(r => !r.IsSender);

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var term = SearchQuery.Trim();
            query = query.Where(r =>
                r.TitleWithSize.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.PeerName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query)
        {
            FilteredRecords.Add(item);
        }

        OnPropertyChanged(nameof(HasFilteredRecords));
        OnPropertyChanged(nameof(AllCountLabel));
        OnPropertyChanged(nameof(SentCountLabel));
        OnPropertyChanged(nameof(ReceivedCountLabel));
    }

    [RelayCommand]
    public void SetFilter(string filter)
    {
        ActiveFilter = filter;
        RefreshFilter();
    }

    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        await _historyRepo.ClearAsync();
        _allRecords.Clear();
        RefreshFilter();
    }

    [RelayCommand]
    public void OpenItemLocation(HistoryRecordItem? item)
    {
        try
        {
            var path = !string.IsNullOrEmpty(item?.TargetPath) && Directory.Exists(item.TargetPath)
                ? item.TargetPath
                : StoragePaths.GetDefaultDownloadPath();

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
