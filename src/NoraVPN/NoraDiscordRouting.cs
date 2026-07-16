#if WINDOWS
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Nvp;

/// <summary>
/// Stores the user's Discord-only routing preference. This is a free routing
/// feature and is intentionally independent from Premium appearance settings.
/// </summary>
internal static class NoraDiscordModeSettings
{
    private sealed class SettingsFile
    {
        public bool Enabled { get; set; }
    }

    public static bool Enabled
    {
        get
        {
            try
            {
                var path = SettingsPath();
                if (!File.Exists(path))
                    return false;
                return (JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(path)) ?? new SettingsFile()).Enabled;
            }
            catch
            {
                return false;
            }
        }
        set
        {
            try { SetEnabledOrThrow(value); }
            catch
            {
                // A preference write must never interfere with normal VPN use.
            }
        }
    }

    public static void SetEnabledOrThrow(bool enabled)
    {
        var path = SettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        var json = JsonSerializer.Serialize(new SettingsFile { Enabled = enabled }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static string VoiceAssetPath => Path.Combine(AppContext.BaseDirectory, "assets", "router-with-screen-svgrepo-com.svg");

    public static IReadOnlyList<string> MissingComponents()
    {
        var required = new[]
        {
            NoraSubscriptionStore.SingBoxPath(),
            VoiceAssetPath
        };
        return required.Where(path => !File.Exists(path)).ToArray();
    }

    public static async Task PrepareAsync(Action<string> status, Action<string> log, CancellationToken cancellationToken)
    {
        status("Checking Discord routing components…");
        var missing = MissingComponents();
        if (missing.Count > 0)
            throw new NoraAppException(
                "NORA-DIS-9101",
                "Discord Mode components are missing: " + string.Join(", ", missing.Select(Path.GetFileName)) +
                ". Restore the complete NORA portable folder and try again.");

        cancellationToken.ThrowIfCancellationRequested();
        status("Validating the Discord-only route…");
        log("[discord] validating Discord-only routing configuration");
        var runtime = Path.Combine(NoraAppState.DataRoot, "runtime");
        Directory.CreateDirectory(runtime);
        var configPath = Path.Combine(runtime, "discord-mode-check.json");
        try
        {
            var config = await NoraDiscordRouting.BuildForSocksAsync(9, cancellationToken);
            File.WriteAllText(configPath, config);
            await NoraDiscordRouting.CheckConfigAsync(configPath, TimeSpan.FromSeconds(10), cancellationToken);
        }
        finally
        {
            try { File.Delete(configPath); } catch { }
        }

        status("Discord Mode is ready");
        log("[discord] Discord Mode components are ready");
    }

    private static string SettingsPath() => Path.Combine(NoraAppState.DataRoot, "discord-mode.json");
}

internal interface INoraDiscordModeCore
{
    Task VerifyDiscordPathAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

internal static class NoraDiscordRouting
{
    internal static readonly string[] ProcessNames =
    [
        "Discord.exe",
        "DiscordPTB.exe",
        "DiscordCanary.exe",
        "DiscordDevelopment.exe"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static async Task<string> BuildForSocksAsync(int socksPort, CancellationToken cancellationToken = default)
    {
        var vpn = new JsonObject
        {
            ["type"] = "socks",
            ["tag"] = "vpn",
            ["server"] = "127.0.0.1",
            ["server_port"] = socksPort
        };
        var physical = await FindPhysicalInterfaceNameAsync(cancellationToken).ConfigureAwait(false);
        return Build(vpn, physical);
    }

    public static async Task<string> BuildForInterfaceAsync(
        string interfaceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
            throw new ArgumentException("The KRot interface name is required.", nameof(interfaceName));
        var vpn = new JsonObject
        {
            ["type"] = "direct",
            ["tag"] = "vpn",
            ["bind_interface"] = interfaceName
        };
        var physical = await FindPhysicalInterfaceNameAsync(cancellationToken).ConfigureAwait(false);
        return Build(vpn, physical);
    }

    private static string Build(JsonObject vpnOutbound, string physical)
    {
        var processNames = new JsonArray();
        foreach (var name in ProcessNames)
            processNames.Add(name);

        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info", ["timestamp"] = true },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "tun",
                    ["tag"] = "discord-in",
                    ["interface_name"] = "NORA-Discord",
                    ["address"] = new JsonArray("172.21.0.1/30"),
                    ["mtu"] = 1400,
                    ["stack"] = "mixed",
                    ["auto_route"] = true,
                    ["strict_route"] = false,
                    ["route_address"] = new JsonArray("0.0.0.0/1", "128.0.0.0/1")
                }
            },
            ["outbounds"] = new JsonArray
            {
                vpnOutbound,
                new JsonObject
                {
                    ["type"] = "direct",
                    ["tag"] = "direct",
                    ["bind_interface"] = physical
                }
            },
            ["route"] = new JsonObject
            {
                ["rules"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["process_name"] = processNames,
                        ["action"] = "route",
                        ["outbound"] = "vpn"
                    }
                },
                ["find_process"] = true,
                ["final"] = "direct"
            }
        };
        return root.ToJsonString(JsonOptions);
    }

    public static async Task CheckConfigAsync(string configPath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var executable = NoraSubscriptionStore.SingBoxPath();
        if (!File.Exists(executable))
            throw new NoraAppException("NORA-DIS-9101", "sing-box.exe is missing from the NORA portable folder.");

        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        psi.ArgumentList.Add("check");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(configPath);
        using var process = Process.Start(psi) ?? throw new NoraAppException("NORA-DIS-9102", "Discord routing validation could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var output = ((await stdout) + Environment.NewLine + (await stderr)).Trim();
        if (process.ExitCode != 0)
            throw new NoraAppException("NORA-DIS-9102", "Discord routing configuration was rejected: " + output);
    }

    public static int RunSelfTest(TextWriter output)
    {
        var runtime = Path.Combine(Path.GetTempPath(), "nora-discord-routing-selftest");
        var originalDataRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
        Environment.SetEnvironmentVariable("NORA_DATA_ROOT", Path.Combine(runtime, "data"));
        Directory.CreateDirectory(runtime);
        var socksPath = Path.Combine(runtime, "discord-socks.json");
        var interfacePath = Path.Combine(runtime, "discord-interface.json");
        try
        {
            var socks = BuildForSocksAsync(20808).GetAwaiter().GetResult();
            var bound = BuildForInterfaceAsync("NORA-KRot-Test").GetAwaiter().GetResult();
            File.WriteAllText(socksPath, socks);
            File.WriteAllText(interfacePath, bound);

            using var socksJson = JsonDocument.Parse(socks);
            using var boundJson = JsonDocument.Parse(bound);
            NoraDiscordModeSettings.SetEnabledOrThrow(true);
            var persistedOn = NoraDiscordModeSettings.Enabled;
            NoraDiscordModeSettings.SetEnabledOrThrow(false);
            var persistedOff = !NoraDiscordModeSettings.Enabled;
            var socksRoot = socksJson.RootElement;
            var boundRoot = boundJson.RootElement;
            var rule = socksRoot.GetProperty("route").GetProperty("rules")[0];
            var names = rule.GetProperty("process_name").EnumerateArray().Select(x => x.GetString()).ToArray();
            var passed = persistedOn && persistedOff &&
                         ProcessNames.All(name => names.Contains(name, StringComparer.OrdinalIgnoreCase)) &&
                         rule.GetProperty("outbound").GetString() == "vpn" &&
                         socksRoot.GetProperty("route").GetProperty("final").GetString() == "direct" &&
                         socksRoot.GetProperty("outbounds")[0].GetProperty("type").GetString() == "socks" &&
                         boundRoot.GetProperty("outbounds")[0].GetProperty("bind_interface").GetString() == "NORA-KRot-Test" &&
                         !socks.Contains("steam", StringComparison.OrdinalIgnoreCase) &&
                         !socks.Contains("amnezia", StringComparison.OrdinalIgnoreCase);
            if (!passed)
            {
                output.WriteLine("DISCORD ROUTING SELF-TEST FAIL: config semantics are invalid");
                return 1;
            }

            CheckConfigAsync(socksPath, TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            CheckConfigAsync(interfacePath, TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            output.WriteLine("DISCORD ROUTING SELF-TEST PASS: Discord-only process rules; final=direct; VLESS SOCKS and KRot interface egress valid");
            return 0;
        }
        catch (Exception ex)
        {
            output.WriteLine("DISCORD ROUTING SELF-TEST FAIL: " + ex.GetBaseException().Message);
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", originalDataRoot);
            try { File.Delete(socksPath); } catch { }
            try { File.Delete(interfacePath); } catch { }
            try { Directory.Delete(runtime, recursive: true); } catch { }
        }
    }

    private static async Task<string> FindPhysicalInterfaceNameAsync(CancellationToken cancellationToken)
    {
        var snapshot = await NoraNetworkInterfaceCache.GetSnapshotAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var candidates = snapshot.Interfaces
            .Where(x => x.OperationalStatus == OperationalStatus.Up)
            .Where(x => x.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .Where(x => !IsTunnelLike(x.Name + " " + x.Description))
            .Select(x => new
            {
                Interface = x,
                HasIpv4Gateway = SafeProperties(x)?.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !g.Address.Equals(IPAddress.Any)) == true
            })
            .OrderByDescending(x => x.HasIpv4Gateway)
            .ToList();
        var selected = candidates.FirstOrDefault(x => x.HasIpv4Gateway)?.Interface ?? candidates.FirstOrDefault()?.Interface;
        if (selected is null)
            throw new NoraAppException("NORA-NET-7005", "No active physical Ethernet or Wi-Fi adapter with a gateway was found.");
        return selected.Name;
    }

    private static IPInterfaceProperties? SafeProperties(NetworkInterface networkInterface)
    {
        try { return networkInterface.GetIPProperties(); }
        catch { return null; }
    }

    private static bool IsTunnelLike(string value)
        => value.Contains("tunnel", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("wintun", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("tap", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("amnezia", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("wireguard", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("nora", StringComparison.OrdinalIgnoreCase);
}

internal sealed class NoraDiscordTunProcess(Func<CancellationToken, Task<string>> buildConfig, Action<string> log) : IVpnCoreProcess
{
    private Process? _process;
    private string _configPath = "";
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task StartAsync(TimeSpan timeout)
    {
        var executable = NoraSubscriptionStore.SingBoxPath();
        if (!File.Exists(executable))
            throw new NoraAppException("NORA-DIS-9101", "sing-box.exe is missing from the NORA portable folder.");
        var runtime = Path.Combine(NoraAppState.DataRoot, "runtime");
        Directory.CreateDirectory(runtime);
        _configPath = Path.Combine(runtime, "discord-router-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".json");
        File.WriteAllText(_configPath, await buildConfig(CancellationToken.None));
        await NoraDiscordRouting.CheckConfigAsync(_configPath, TimeSpan.FromSeconds(10), CancellationToken.None);

        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(_configPath);
        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        _process.ErrorDataReceived += (_, e) => HandleLine(e.Data);
        _process.Exited += (_, _) =>
        {
            if (!_ready.Task.IsCompleted)
                _ready.TrySetException(new NoraAppException("NORA-DIS-9103", "Discord routing stopped before it became ready."));
            _exited.TrySetResult();
        };
        if (!_process.Start())
            throw new NoraAppException("NORA-DIS-9103", "Discord routing could not start.");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var registration = timeoutCts.Token.Register(() =>
            _ready.TrySetException(new NoraAppException("NORA-DIS-9103", "Discord routing did not become ready in time.")));
        await _ready.Task;
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        var process = _process;
        if (process is not null && !process.HasExited)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            await Task.WhenAny(_exited.Task, Task.Delay(timeout));
        }
        _exited.TrySetResult();
        try { if (_configPath.Length > 0) File.Delete(_configPath); } catch { }
    }

    public Task WaitForExitAsync() => _exited.Task;

    private void HandleLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        log("[discord-router] " + line);
        if (line.Contains("panic", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("access is denied", StringComparison.OrdinalIgnoreCase))
            _ready.TrySetException(new NoraAppException("NORA-DIS-9103", line));
        else if (line.Contains("started", StringComparison.OrdinalIgnoreCase))
            _ready.TrySetResult();
    }
}

internal sealed class NoraDiscordKrotCoreProcess : IVpnCoreProcess, INoraDiscordModeCore
{
    private readonly NvpConfig _config;
    private readonly NvpCoreProcess _krot;
    private readonly NoraDiscordTunProcess _router;
    private readonly Action<string> _log;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _verified;

    public NoraDiscordKrotCoreProcess(string profilePath, Action<string> log)
    {
        _config = NvpConfig.Load(profilePath);
        _log = log;
        _krot = new NvpCoreProcess(profilePath, log, selectiveMode: true);
        _router = new NoraDiscordTunProcess(
            cancellationToken => NoraDiscordRouting.BuildForInterfaceAsync(_config.Tunnel.InterfaceName, cancellationToken),
            log);
    }

    public async Task StartAsync(TimeSpan timeout)
    {
        try
        {
            _log("[discord] starting KRot as a selective VPN egress");
            await _krot.StartAsync(timeout);
            await VerifyDiscordPathAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            _log("[discord] starting process-based Discord routing");
            await _router.StartAsync(timeout);
            _ = MonitorExitAsync();
        }
        catch
        {
            try { await _router.StopAsync(TimeSpan.FromSeconds(2)); } catch { }
            try { await _krot.StopAsync(TimeSpan.FromSeconds(4)); } catch { }
            throw;
        }
    }

    public async Task VerifyDiscordPathAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_verified)
            return;
        var result = await NoraDiscordInterfaceProbe.ProbeAsync(_config.Tunnel.InterfaceName, timeout, cancellationToken);
        if (!result.Success)
            throw new NoraAppException("NORA-DIS-9103", "KRot selective route is unavailable: " + result.Detail);
        _verified = true;
        _log($"[discord] KRot selective route ready on {result.InterfaceName}; probe_ms={result.Milliseconds}");
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        await _router.StopAsync(timeout);
        await _krot.StopAsync(timeout);
        _exited.TrySetResult();
    }

    public Task WaitForExitAsync() => _exited.Task;

    private async Task MonitorExitAsync()
    {
        await Task.WhenAny(_router.WaitForExitAsync(), _krot.WaitForExitAsync());
        _exited.TrySetResult();
    }
}

internal sealed record NoraDiscordInterfaceProbeResult(
    bool Success,
    long Milliseconds,
    string InterfaceName,
    string Detail);

internal static class NoraDiscordInterfaceProbe
{
    private const int IpUnicastInterface = 31;

    public static async Task<NoraDiscordInterfaceProbeResult> ProbeAsync(
        string interfaceName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var snapshot = await NoraNetworkInterfaceCache.GetSnapshotAsync(
            forceRefresh: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var selected = snapshot.Interfaces
            .Select(networkInterface => new
            {
                Interface = networkInterface,
                V4 = SafeV4(networkInterface),
                Address = SafeAddresses(networkInterface).FirstOrDefault(address =>
                    address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address))?.Address
            })
            .FirstOrDefault(item => item.V4 is not null && item.Address is not null &&
                item.Interface.Name.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
        if (selected?.V4 is null || selected.Address is null)
            return new(false, 0, interfaceName, "The KRot interface does not have an IPv4 address.");

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            socket.SetSocketOption(
                SocketOptionLevel.IP,
                (SocketOptionName)IpUnicastInterface,
                IPAddress.HostToNetworkOrder(selected.V4.Index));
            socket.Bind(new IPEndPoint(selected.Address, 0));
        }
        catch (Exception ex)
        {
            return new(false, 0, interfaceName, "Windows could not bind a probe to the KRot interface: " + ex.Message);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 443), timeoutCts.Token);
            return new(true, Math.Max(1, stopwatch.ElapsedMilliseconds), interfaceName, "TCP passed through the KRot interface.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, Math.Max(1, stopwatch.ElapsedMilliseconds), interfaceName, "The KRot interface probe timed out.");
        }
        catch (Exception ex)
        {
            return new(false, Math.Max(1, stopwatch.ElapsedMilliseconds), interfaceName, ex.GetBaseException().Message);
        }
    }

    private static IPv4InterfaceProperties? SafeV4(NetworkInterface networkInterface)
    {
        try { return networkInterface.GetIPProperties().GetIPv4Properties(); }
        catch { return null; }
    }

    private static IReadOnlyList<UnicastIPAddressInformation> SafeAddresses(NetworkInterface networkInterface)
    {
        try { return networkInterface.GetIPProperties().UnicastAddresses.ToArray(); }
        catch { return []; }
    }
}
#endif
