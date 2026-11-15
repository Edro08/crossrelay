using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrossRelay.Desktop.Services;

/// <summary>
/// Enforces a single running desktop instance per user and forwards activation
/// requests from secondary launches to the primary window.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public bool IsPrimaryInstance { get; }

    public SingleInstanceGuard(string appId = "CrossRelay.Desktop")
    {
        var user = SanitizeIdentifier(Environment.UserName);
        var mutexName = $"{appId}-{user}-Mutex";
        _pipeName = $"{appId}-{user}-Pipe";

        _mutex = new Mutex(initiallyOwned: true, name: mutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    /// <summary>
    /// Signals the already-running primary instance to bring its window to the foreground.
    /// </summary>
    public void NotifyPrimaryInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(1500);
            var payload = Encoding.UTF8.GetBytes("ACTIVATE\n");
            client.Write(payload, 0, payload.Length);
            client.Flush();
        }
        catch (Exception ex)
        {
            CrossRelayLogger.Instance.Warn("SingleInstance", $"No se pudo notificar a la instancia principal: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts listening in the background for activation signals from secondary launches.
    /// </summary>
    public void StartListening(Action onActivateRequested)
    {
        if (!IsPrimaryInstance || _cts != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var line = await reader.ReadLineAsync(token);
                    if (string.Equals(line, "ACTIVATE", StringComparison.OrdinalIgnoreCase))
                    {
                        onActivateRequested();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    CrossRelayLogger.Instance.Warn("SingleInstance", $"Error en listener IPC: {ex.Message}");
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
            }
        }, token);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Ignore if already released on another thread.
            }
        }
        _mutex.Dispose();
    }

    private static string SanitizeIdentifier(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "default";
        }

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }
        return sb.ToString();
    }
}
