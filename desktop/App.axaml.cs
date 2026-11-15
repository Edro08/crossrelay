using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CrossRelay.Desktop.Services;
using CrossRelay.Desktop.ViewModels;
using CrossRelay.Desktop.Views;

namespace CrossRelay.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (_, _) => CoreProcessClient.Instance.Dispose();
            desktop.Exit += (_, _) => CoreProcessClient.Instance.Dispose();
            Program.ActiveInstanceGuard?.StartListening(() =>
                Avalonia.Threading.Dispatcher.UIThread.Post(BringMainWindowToFront));

            // Soporte de captura automatizada para verificación visual con --screenshot <ruta> [tab]
            var args = desktop.Args;
            if (args != null && args.Length >= 2 && args[0] == "--screenshot")
            {
                var outputPath = args[1];
                var tab = args.Length >= 3 ? args[2] : null;
                var vm = (MainViewModel)mainWindow.DataContext!;

                if (tab == "Recibir")
                {
                    vm.NavigateCommand.Execute("Recibir");
                }
                else if (tab == "Dispositivos")
                {
                    vm.NavigateCommand.Execute("Dispositivos");
                }
                else if (tab == "Historial")
                {
                    vm.NavigateCommand.Execute("Historial");
                }
                else if (tab == "Ajustes")
                {
                    vm.NavigateCommand.Execute("Ajustes");
                }
                else if (tab == "ModalCambiar")
                {
                    vm.SendVm.IsSwitchDeviceDialogOpen = true;
                }
                else if (tab == "Min")
                {
                    mainWindow.Width = 1000;
                    mainWindow.Height = 640;
                }

                mainWindow.Opened += async (s, e) =>
                {
                    await System.Threading.Tasks.Task.Delay(1200);
                    try
                    {
                        var width = (int)mainWindow.Bounds.Width;
                        var height = (int)mainWindow.Bounds.Height;
                        if (width <= 0) width = 1050;
                        if (height <= 0) height = 700;

                        var pixelSize = new PixelSize(width, height);
                        var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(pixelSize, new Vector(96, 96));
                        rtb.Render(mainWindow);
                        #pragma warning disable CS0618
                        rtb.Save(outputPath);
                        #pragma warning restore CS0618
                        Console.WriteLine($"[SCREENSHOT_SUCCESS] Captured {pixelSize.Width}x{pixelSize.Height} for {tab ?? "Enviar"} to {outputPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SCREENSHOT_ERROR] {ex.Message}");
                    }
                    finally
                    {
                        MainWindow.IsExiting = true;
                        desktop.Shutdown();
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void BringToFront()
    {
        if (Current is App app)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(app.BringMainWindowToFront);
        }
    }

    private void BringMainWindowToFront()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            desktop.MainWindow.Show();
            if (desktop.MainWindow.WindowState == WindowState.Minimized)
            {
                desktop.MainWindow.WindowState = WindowState.Normal;
            }
            desktop.MainWindow.Activate();
        }
    }

    private void OnOpenTrayClicked(object? sender, EventArgs e)
    {
        BringMainWindowToFront();
    }

    private void OnExitTrayClicked(object? sender, EventArgs e)
    {
        MainWindow.IsExiting = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
