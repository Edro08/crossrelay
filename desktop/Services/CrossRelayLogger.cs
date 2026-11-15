using System;

namespace CrossRelay.Desktop.Services;

public class CrossRelayLogger
{
    private static CrossRelayLogger? _instance;
    public static CrossRelayLogger Instance => _instance ??= new CrossRelayLogger();

    public void Info(string category, string message) =>
        Console.WriteLine($"[INFO] [{category}] {message}");

    public void Warn(string category, string message) =>
        Console.WriteLine($"[WARN] [{category}] {message}");

    public void Error(string category, string message, Exception? ex = null) =>
        Console.WriteLine($"[ERROR] [{category}] {message} {ex?.Message}");

    public void Fatal(string category, string message, Exception? ex = null) =>
        Console.WriteLine($"[FATAL] [{category}] {message} {ex?.Message}");
}
