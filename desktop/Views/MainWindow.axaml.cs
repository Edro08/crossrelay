using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CrossRelay.Desktop.Views;

public partial class MainWindow : Window
{
    public static bool IsExiting { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsLinux())
        {
            WindowDecorations = WindowDecorations.None;

            // CenterScreen on WSLg's 8192x8192 virtual root can place the window off-screen
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new Avalonia.PixelPoint(80, 80);
            }
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var isWsl = OperatingSystem.IsLinux() &&
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"));

        if (!IsExiting && !isWsl)
        {
            e.Cancel = true;
            this.Hide();
        }
        else
        {
            Services.CoreProcessClient.Instance.Dispose();
            base.OnClosing(e);
        }
    }
}
