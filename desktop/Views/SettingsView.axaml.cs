using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrossRelay.Desktop.ViewModels;

namespace CrossRelay.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnChangeFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleccionar Carpeta Global de Descargas",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is SettingsViewModel vm)
        {
            vm.DownloadDirectory = folders[0].Path.LocalPath;
        }
    }
}
