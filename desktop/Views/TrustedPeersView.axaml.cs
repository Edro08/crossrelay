using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrossRelay.Desktop.ViewModels;

namespace CrossRelay.Desktop.Views;

public partial class TrustedPeersView : UserControl
{
    public TrustedPeersView()
    {
        InitializeComponent();
    }

    private async void OnChangeFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleccionar Carpeta Dedicada para este Dispositivo",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is TrustedPeersViewModel vm)
        {
            vm.DedicatedDownloadFolder = folders[0].Path.LocalPath;
        }
    }
}
