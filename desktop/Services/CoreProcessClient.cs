using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrossRelay.Desktop.Services;

/// <summary>
/// Manages the lifecycle of the Go crossrelay-core sidecar process and communicates via local control API
/// </summary>
public class CoreProcessClient : IDisposable
{
    private static CoreProcessClient? _instance;
    public static CoreProcessClient Instance => _instance ??= new CoreProcessClient();

    private Process? _coreProcess;
    private readonly HttpClient _http;
    private CancellationTokenSource? _eventsCts;
    private const int ControlPortMin = 14242;
    private const int ControlPortMax = 14250;
    private int _controlPort = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 14242 : 14243;
    private string _baseUrl;

    public event Action<JsonElement>? OnPeerDiscovered;
    public event Action<string>? OnPeerLost;
    public event Action<JsonElement>? OnPeerPaired;
    public event Action<string, string>? OnPinChallenge; // requesterName, pinCode
    public event Action<JsonElement>? OnTransferRequested;
    public event Action<string, long, long, long>? OnTransferProgress; // sessionId, written, total, speed
    public event Action<string, bool, string>? OnTransferCompleted; // sessionId, success, error

    public CoreProcessClient()
    {
        _baseUrl = $"https://127.0.0.1:{_controlPort}";
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true // Self-signed ephemeral cert
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task StartAsync()
    {
        // 1. Check if core is already running on any port in the control pool (14242..14250)
        var running = await FindHealthyControlPortAsync();
        if (!running)
        {
            KillStaleCoreProcesses();
            _controlPort = SelectAvailableControlPort();
            _baseUrl = $"https://127.0.0.1:{_controlPort}";
            LaunchCoreProcess();
            // Wait up to 30s for Core to initialize (handles AV DeepScreen/CyberCapture scans on new builds)
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                if (await IsCoreHealthyAsync()) break;
            }
        }

        // 2. Start SSE events listener in background
        _eventsCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenEventsAsync(_eventsCts.Token));
    }

    private async Task<bool> FindHealthyControlPortAsync()
    {
        var candidates = new List<int> { _controlPort };
        for (int p = ControlPortMin; p <= ControlPortMax; p++)
        {
            if (p != _controlPort) candidates.Add(p);
        }

        foreach (var port in candidates)
        {
            _controlPort = port;
            _baseUrl = $"https://127.0.0.1:{port}";
            if (await IsCoreHealthyAsync())
            {
                return true;
            }
        }

        _controlPort = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 14242 : 14243;
        _baseUrl = $"https://127.0.0.1:{_controlPort}";
        return false;
    }

    private static int SelectAvailableControlPort()
    {
        int preferred = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 14242 : 14243;
        var candidates = new List<int> { preferred };
        for (int p = ControlPortMin; p <= ControlPortMax; p++)
        {
            if (p != preferred) candidates.Add(p);
        }

        foreach (var port in candidates)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch { }
        }

        return preferred;
    }

    private static void KillStaleCoreProcesses()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("crossrelay-core"))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(1000);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }
    }

    private void LaunchCoreProcess()
    {
        try
        {
            var binName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "crossrelay-core.exe" : "crossrelay-core";
            var binPath = Path.Combine(AppContext.BaseDirectory, binName);

            // Fallback to project root dist/ if running in dev mode
            if (!File.Exists(binPath))
            {
                var platformFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64" : "linux-x64";
                var devPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist", platformFolder, binName));
                if (File.Exists(devPath)) binPath = devPath;
            }

            if (File.Exists(binPath))
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        File.SetUnixFileMode(binPath,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
                    catch { }
                }

                _coreProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = binPath,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        WorkingDirectory = AppContext.BaseDirectory
                    }
                };
                _coreProcess.StartInfo.Environment["CROSSRELAY_CONTROL_PORT"] = _controlPort.ToString();
                _coreProcess.Start();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CoreProcessClient] Error launching core: {ex.Message}");
        }
    }

    public async Task<bool> IsCoreHealthyAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var resp = await _http.GetAsync($"{_baseUrl}/local/v1/status", cts.Token);
            if (!resp.IsSuccessStatusCode) return false;

            var json = await resp.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("os", out var osProp))
            {
                var expectedOs = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "windows"
                    : (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "darwin");
                if (!string.Equals(osProp.GetString(), expectedOs, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<JsonElement> GetStatusAsync()
    {
        var resp = await _http.GetAsync($"{_baseUrl}/local/v1/status");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task<JsonElement> GetSettingsAsync()
    {
        var resp = await _http.GetAsync($"{_baseUrl}/local/v1/settings");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task SaveSettingsAsync(object settings)
    {
        var payload = JsonSerializer.Serialize(settings);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}/local/v1/settings", content);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement> GetTrustedPeersAsync()
    {
        var resp = await _http.GetAsync($"{_baseUrl}/local/v1/trusted");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task SetBlockedAsync(string peerId, bool blocked)
    {
        var payload = JsonSerializer.Serialize(new { id = peerId, blocked });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}/local/v1/trusted/block", content);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteTrustedAsync(string peerId)
    {
        var resp = await _http.DeleteAsync($"{_baseUrl}/local/v1/trusted/{peerId}");
        resp.EnsureSuccessStatusCode();
    }

    public async Task InitiatePairingAsync(object targetPeer)
    {
        var payload = JsonSerializer.Serialize(targetPeer);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}/local/v1/pair/init", content);
        resp.EnsureSuccessStatusCode();
    }

    public async Task VerifyPairingAsync(object targetPeer, string pin)
    {
        var payload = JsonSerializer.Serialize(new { target = targetPeer, pin });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}/local/v1/pair/verify", content);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<bool> ProbePeerAsync(string ip, int port = 0)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { ip, port });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{_baseUrl}/local/v1/peers/probe", content);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<CrossRelay.Desktop.Models.PortDiagnosticsResult?> DiagnosePortsAsync(int[] ports)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { ports });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{_baseUrl}/local/v1/diagnostics/ports", content);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var os = root.TryGetProperty("os", out var osEl) ? osEl.GetString() ?? "" : "";
            var activePort = root.TryGetProperty("activePort", out var apEl) ? apEl.GetInt32() : 4242;
            var fwStatus = root.TryGetProperty("firewallStatus", out var fwEl) ? fwEl.GetString() ?? "unknown" : "unknown";
            var list = new List<CrossRelay.Desktop.Models.PortDiagnosticEntry>();
            if (root.TryGetProperty("ports", out var portsEl) && portsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in portsEl.EnumerateArray())
                {
                    var p = item.TryGetProperty("port", out var pEl) ? pEl.GetInt32() : 0;
                    var st = item.TryGetProperty("state", out var stEl) ? stEl.GetString() ?? "free" : "free";
                    if (p > 0) list.Add(new CrossRelay.Desktop.Models.PortDiagnosticEntry(p, st));
                }
            }
            return new CrossRelay.Desktop.Models.PortDiagnosticsResult(os, activePort, fwStatus, list);
        }
        catch
        {
            return null;
        }
    }

    public async Task SendFilesAsync(object targetPeer, string[] filePaths)
    {
        var payload = JsonSerializer.Serialize(new { target = targetPeer, filePaths });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}/local/v1/send", content);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<bool> RespondTransferAsync(string sessionId, bool accepted)
    {
        var payload = JsonSerializer.Serialize(new { sessionId, accepted });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}/local/v1/transfer/respond", content);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> SetTransferPausedAsync(bool paused)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { paused });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{_baseUrl}/local/v1/transfer/pause", content);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CancelTransferAsync()
    {
        try
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{_baseUrl}/local/v1/transfer/cancel", content);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CancelIncomingTransferAsync()
    {
        try
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{_baseUrl}/local/v1/receive/cancel", content);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task UpdateTrustedPeerAsync(string id, string customAlias, string policy, string dedicatedDownloadFolder)
    {
        var payload = JsonSerializer.Serialize(new { id, customAlias, policy, dedicatedDownloadFolder });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}/local/v1/trusted/update", content);
        resp.EnsureSuccessStatusCode();
    }

    private async Task ListenEventsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/local/v1/events");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);

                string? currentEvent = null;
                while (!ct.IsCancellationRequested && !reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("event: "))
                    {
                        currentEvent = line.Substring(7).Trim();
                    }
                    else if (line.StartsWith("data: ") && currentEvent != null)
                    {
                        var data = line.Substring(6).Trim();
                        DispatchEvent(currentEvent, data);
                        currentEvent = null;
                    }
                }
            }
            catch
            {
                await Task.Delay(2000, ct); // Reconnect delay
            }
        }
    }

    private void DispatchEvent(string eventType, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();

            switch (eventType)
            {
                case "peer_discovered":
                    OnPeerDiscovered?.Invoke(root);
                    break;
                case "peer_lost":
                    if (root.TryGetProperty("id", out var idProp))
                        OnPeerLost?.Invoke(idProp.GetString() ?? "");
                    break;
                case "peer_paired":
                    OnPeerPaired?.Invoke(root);
                    break;
                case "pin_challenge":
                    var name = root.TryGetProperty("requesterName", out var n) ? n.GetString() ?? "Dispositivo" : "Dispositivo";
                    var pin = root.TryGetProperty("pinCode", out var p) ? p.GetString() ?? "" : "";
                    OnPinChallenge?.Invoke(name, pin);
                    break;
                case "transfer_requested":
                    OnTransferRequested?.Invoke(root);
                    break;
                case "transfer_progress":
                    var sessId = root.GetProperty("sessionId").GetString() ?? "";
                    var bTrans = root.GetProperty("bytesTransferred").GetInt64();
                    var total = root.GetProperty("totalBytes").GetInt64();
                    var speed = root.GetProperty("speedBps").GetInt64();
                    OnTransferProgress?.Invoke(sessId, bTrans, total, speed);
                    break;
                case "transfer_completed":
                    var sId = root.GetProperty("sessionId").GetString() ?? "";
                    var success = root.GetProperty("success").GetBoolean();
                    var err = root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
                    OnTransferCompleted?.Invoke(sId, success, err);
                    break;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _eventsCts?.Cancel();
        _eventsCts?.Dispose();
        _http.Dispose();
        if (_coreProcess != null && !_coreProcess.HasExited)
        {
            try { _coreProcess.Kill(); } catch { }
            _coreProcess.Dispose();
        }
    }
}
