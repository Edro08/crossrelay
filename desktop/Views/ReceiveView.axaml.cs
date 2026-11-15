using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrossRelay.Desktop.ViewModels;

namespace CrossRelay.Desktop.Views;

public partial class ReceiveView : UserControl
{
    public ReceiveView()
    {
        InitializeComponent();
    }

    private async void OnChangeFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleccionar Carpeta para Recibir Archivos",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is ReceiveViewModel vm)
        {
            vm.DownloadPath = folders[0].Path.LocalPath;
        }
    }
}
