using System;
using System.Threading.Tasks;
using Avalonia;
using CrossRelay.Desktop.Services;

namespace CrossRelay.Desktop;

sealed class Program
{
    internal static SingleInstanceGuard? ActiveInstanceGuard { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        var isScreenshot = args.Length >= 2 && args[0] == "--screenshot";
        using var singleInstance = isScreenshot ? null : new SingleInstanceGuard();
        if (singleInstance is { IsPrimaryInstance: false })
        {
            singleInstance.NotifyPrimaryInstance();
            return;
        }
        ActiveInstanceGuard = singleInstance;
        // Captura global de excepciones no controladas para diagnóstico
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"), ex.ToString());
                CrossRelayLogger.Instance.Fatal("CrashHandler", "Excepción no controlada en AppDomain", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            CrossRelayLogger.Instance.Error("CrashHandler", "Excepción no observada en TaskScheduler", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"), ex.ToString());
            CrossRelayLogger.Instance.Fatal("Program", "Fallo fatal durante la ejecución de la UI", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

        if (OperatingSystem.IsLinux() &&
            (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")) ||
             Environment.GetEnvironmentVariable("CROSSRELAY_SOFTWARE_RENDER") == "1"))
        {
            builder = builder.With(new X11PlatformOptions
            {
                RenderingMode = new[] { X11RenderingMode.Software }
            });
        }

        return builder;
    }
}
