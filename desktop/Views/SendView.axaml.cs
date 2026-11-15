using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrossRelay.Desktop.ViewModels;

namespace CrossRelay.Desktop.Views;

public partial class SendView : UserControl
{
    public SendView()
    {
        InitializeComponent();

        var dropZone = this.FindControl<Border>("DropZoneBorder");
        if (dropZone != null)
        {
            dropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            dropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is { } files)
        {
            var pathList = new List<string>();
            foreach (var item in files)
            {
                if (item is IStorageFile file)
                {
                    pathList.Add(file.Path.LocalPath);
                }
                else if (item is IStorageFolder folder)
                {
                    pathList.Add(folder.Path.LocalPath);
                }
            }

            if (DataContext is SendViewModel vm && pathList.Count > 0)
            {
                vm.AddFiles(pathList.ToArray());
            }
        }
    }

    private async void OnBrowseFilesClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar Archivos para Transferir",
            AllowMultiple = true
        });

        if (files.Count > 0 && DataContext is SendViewModel vm)
        {
            var paths = files.Select(f => f.Path.LocalPath).ToArray();
            vm.AddFiles(paths);
        }
    }

    private async void OnBrowseFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleccionar Carpeta para Transferir",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is SendViewModel vm)
        {
            var folderPath = folders[0].Path.LocalPath;
            if (Directory.Exists(folderPath))
            {
                vm.AddFiles(new[] { folderPath });
            }
        }
    }

    private void OnCleanOptionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string criteria && DataContext is SendViewModel vm)
        {
            vm.ClearQueue(criteria);
        }

        this.FindControl<Button>("CleanMenuButton")?.Flyout?.Hide();
    }
}
