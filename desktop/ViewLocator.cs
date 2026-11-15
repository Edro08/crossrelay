using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CrossRelay.Desktop.ViewModels;
using CrossRelay.Desktop.Views;

namespace CrossRelay.Desktop;

/// <summary>
/// Mapeador tipado directo de ViewModel a Vista (100% compatible con Native AOT y sin reflexión).
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param switch
        {
            SendViewModel => new SendView(),
            ReceiveViewModel => new ReceiveView(),
            TrustedPeersViewModel => new TrustedPeersView(),
            HistoryViewModel => new HistoryView(),
            SettingsViewModel => new SettingsView(),
            _ => new TextBlock { Text = "Vista no encontrada para: " + param?.GetType().Name }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
