#if WINDOWS
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Nvp;

internal sealed record NoraDiagnosticTarget(
    string Name,
    string Protocol,
    string Host,
    int Port,
    string ProfilePath,
    NoraSubscriptionServer? SubscriptionServer,
    bool ExpectEndpointExit);

internal sealed record NoraDiagnosticProgress(int Step, int Total, string Label);

internal sealed record NoraDiagnosticResult(
    bool Success,
    bool Cancelled,
    string Code,
    string Summary,
    string Report);

internal sealed class NoraConnectionDiagnosticRunner
{
    private const int TotalSteps = 8;
    private readonly Action<string> _output;
    private readonly Action<NoraDiagnosticProgress> _progress;
    private readonly StringBuilder _report = new();
    private readonly object _reportGate = new();
    private readonly NoraCoreLogLimiter _coreLimiter = new();

    public NoraConnectionDiagnosticRunner(Action<string> output, Action<NoraDiagnosticProgress> progress)
    {
        _output = output;
        _progress = progress;
    }

    public async Task<NoraDiagnosticResult> RunAsync(
        NoraDiagnosticTarget target,
        Func<Action<string>, IVpnCoreProcess> coreFactory,
        CancellationToken cancellationToken)
    {
        IVpnCoreProcess? core = null;
        NoraDirectLatencyResult? endpointProbe = null;
        NoraDirectLatencyResult? controlProbe = null;
        NoraBackendProbeResult? backendProbe = null;
        Exception? failure = null;
        var coreStarted = false;
        var dataPlanePassed = false;
        var cancelled = false;
        IReadOnlyList<IPAddress> endpointAddresses = [];

        Write("=== DIAGNOSTIC START ===");
        Write($"session={Guid.NewGuid():N}; target={SafeLabel(target.Name)}; protocol={SafeLabel(target.Protocol)}; endpoint={SafeEndpoint(target.Host, target.Port)}");

        try
        {
            Stage(1, "Collecting system information");
            foreach (var line in NoraDiagnosticEnvironment.Describe())
                Write(line);
            cancellationToken.ThrowIfCancellationRequested();

            Stage(2, "Inspecting active network adapters");
            foreach (var line in await NoraDiagnosticEnvironment.DescribeAdaptersAsync(cancellationToken))
                Write(line);
            foreach (var line in await NoraDiagnosticEnvironment.DescribeFirewallAsync(cancellationToken))
                Write(line);

            Stage(3, "Resolving and probing the VPN endpoint");
            endpointAddresses = await ResolveIpv4Async(target.Host, cancellationToken);
            Write(endpointAddresses.Count == 0
                ? "endpoint_dns result=fail; ipv4_count=0"
                : $"endpoint_dns result=pass; ipv4_count={endpointAddresses.Count}; addresses={string.Join(',', endpointAddresses.Take(4))}");
            foreach (var address in endpointAddresses.Take(4))
                foreach (var row in await NoraDiagnosticEnvironment.DescribeRoutesAsync(address, cancellationToken))
                    Write($"route_before target={address}; {row}");

            var endpointTask = NoraDirectLatencyProbe.ProbeAsync(target.Host, target.Port, TimeSpan.FromSeconds(6), cancellationToken);
            var controlTask = NoraDirectLatencyProbe.ProbeAsync("1.1.1.1", 443, TimeSpan.FromSeconds(6), cancellationToken);
            await Task.WhenAll(endpointTask, controlTask);
            endpointProbe = await endpointTask;
            controlProbe = await controlTask;
            Write(FormatProbe("endpoint_tcp", endpointProbe));
            Write(FormatProbe("control_tcp", controlProbe));

            Stage(4, "Starting the selected VPN backend");
            core = coreFactory(HandleCoreLine);
            if (core is XrayCoreProcess xray)
            {
                await xray.StartBackendForDiagnosticsAsync(TimeSpan.FromSeconds(24), cancellationToken);
                Write("xray_local_backend result=pass; listener=ready");
                foreach (var address in endpointAddresses.Take(4))
                    foreach (var row in await NoraDiagnosticEnvironment.DescribeRoutesAsync(address, cancellationToken))
                        Write($"route_after_bypass target={address}; {row}");

                Stage(5, "Testing VLESS through Xray before TUN");
                backendProbe = await xray.ProbeLocalSocksAsync(TimeSpan.FromSeconds(12), cancellationToken);
                Write($"xray_socks_https result={(backendProbe.Success ? "pass" : "fail")}; status={backendProbe.Status}; elapsed_ms={backendProbe.Milliseconds}; detail={backendProbe.Detail}");

                Stage(6, "Starting the temporary TUN data path");
                await xray.StartTunFrontendForDiagnosticsAsync(TimeSpan.FromSeconds(24), cancellationToken);
                coreStarted = true;
            }
            else
            {
                Stage(5, "Starting the temporary TUN data path");
                await core.StartAsync(TimeSpan.FromSeconds(35)).WaitAsync(cancellationToken);
                coreStarted = true;
            }

            foreach (var address in endpointAddresses.Take(4))
                foreach (var row in await NoraDiagnosticEnvironment.DescribeRoutesAsync(address, cancellationToken))
                    Write($"route_with_tun target={address}; {row}");

            Stage(7, "Verifying HTTPS and DNS through the tunnel");
            var expectedExit = target.ExpectEndpointExit ? target.Host : "";
            await NoraDataPlaneProbe.VerifyAsync(core, TimeSpan.FromSeconds(30), Write, expectedExit, cancellationToken);
            dataPlanePassed = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            Write("diagnostic_cancelled result=cancelled; requested_by=user");
        }
        catch (Exception ex)
        {
            failure = ex;
            Write($"diagnostic_failure type={ex.GetBaseException().GetType().Name}; hresult=0x{ex.GetBaseException().HResult:X8}; detail={ex.GetBaseException().Message}");
        }
        finally
        {
            Stage(8, "Stopping test components and restoring routes");
            if (core is not null)
            {
                try
                {
                    await core.StopAsync(TimeSpan.FromSeconds(8));
                    Write("cleanup core=stopped; routes=restored");
                }
                catch (Exception ex)
                {
                    Write($"cleanup result=warning; type={ex.GetBaseException().GetType().Name}; detail={ex.GetBaseException().Message}");
                }
            }
            foreach (var coreSummary in _coreLimiter.Summaries())
                Write(coreSummary);
        }

        var (code, summary, success) = Decide(
            cancelled,
            endpointProbe,
            controlProbe,
            backendProbe,
            coreStarted,
            dataPlanePassed,
            failure);
        Write($"DIAGNOSIS code={code}; result={(success ? "pass" : cancelled ? "cancelled" : "fail")}; summary={summary}");
        Write("=== DIAGNOSTIC END ===");

        lock (_reportGate)
            return new NoraDiagnosticResult(success, cancelled, code, summary, _report.ToString());
    }

    private void Stage(int step, string label)
    {
        _progress(new NoraDiagnosticProgress(step, TotalSteps, label));
        Write($"stage={step}/{TotalSteps}; {label}");
    }

    private void HandleCoreLine(string line)
    {
        var accepted = _coreLimiter.Accept(line);
        if (!string.IsNullOrWhiteSpace(accepted))
            Write("core " + accepted);
    }

    private void Write(string line)
    {
        var safe = NoraDiagnosticRedactor.Sanitize(line);
        var record = $"[diag] {safe}";
        lock (_reportGate)
            _report.AppendLine(record);
        _output(record);
    }

    private static string FormatProbe(string name, NoraDirectLatencyResult result)
        => $"{name} result={(result.Success ? "pass" : "fail")}; status={result.Status}; elapsed_ms={result.Milliseconds?.ToString() ?? ""}; interface={SafeLabel(result.InterfaceName)}; detail={result.Detail}";

    private static string SafeEndpoint(string host, int port)
        => SafeLabel(host) + ":" + port;

    private static string SafeLabel(string value)
        => NoraDiagnosticRedactor.Sanitize((value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim());

    private static async Task<IReadOnlyList<IPAddress>> ResolveIpv4Async(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
            return literal.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? [literal] : [];
        try
        {
            return (await Dns.GetHostAddressesAsync(host, cancellationToken))
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Distinct()
                .ToArray();
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    private static (string Code, string Summary, bool Success) Decide(
        bool cancelled,
        NoraDirectLatencyResult? endpoint,
        NoraDirectLatencyResult? control,
        NoraBackendProbeResult? backend,
        bool coreStarted,
        bool dataPlanePassed,
        Exception? failure)
    {
        if (cancelled)
            return ("TEST_CANCELLED", "The diagnostic was cancelled and test components were stopped.", false);
        if (dataPlanePassed)
            return ("CONNECTION_OK", "The selected server, VPN backend, TUN, HTTPS and DNS all passed.", true);
        if (endpoint is { Success: false } && control is { Success: true })
            return ("ENDPOINT_TCP_BLOCKED", "General internet access works, but the selected VPN endpoint is unreachable before TUN starts.", false);
        if (endpoint is { Success: false } && control is { Success: false })
            return ("PHYSICAL_NETWORK_UNAVAILABLE", "Both the VPN endpoint and the control connection failed on the physical interface.", false);
        if (backend is { Success: false })
            return ("VPN_TRANSPORT_FAILED", "The endpoint accepts TCP, but the selected VPN transport could not carry HTTPS before TUN started.", false);
        if (!coreStarted)
            return ("BACKEND_START_FAILED", "The VPN backend did not reach its ready state.", false);
        if (coreStarted && !dataPlanePassed)
            return ("TUN_DATA_PLANE_FAILED", "The backend started, but HTTPS or DNS did not pass through the temporary TUN path.", false);
        return ("DIAGNOSTIC_INCONCLUSIVE", "The test failed without a decisive network-layer signature: " + (failure?.GetBaseException().GetType().Name ?? "unknown"), false);
    }

    internal static int RunSelfTest(TextWriter output)
    {
        var limiter = new NoraCoreLogLimiter();
        var emitted = 0;
        for (var index = 0; index < 120; index++)
            if (limiter.Accept("[xray] transport/internet/tcp: dialing TCP to tcp:203.0.113.10:8443") is not null)
                emitted++;

        var secret = "vless://11111111-2222-3333-4444-555555555555@example.test:443?password=secret";
        var redacted = NoraDiagnosticRedactor.Sanitize(secret);
        var endpointFail = new NoraDirectLatencyResult(false, null, "timeout", "Wi-Fi", "timeout");
        var controlPass = new NoraDirectLatencyResult(true, 12, "ok", "Wi-Fi", "ok");
        var decision = Decide(false, endpointFail, controlPass, null, false, false, null);
        var passed = emitted <= 8 &&
                     limiter.Summaries().Count == 1 &&
                     !redacted.Contains("11111111", StringComparison.Ordinal) &&
                     !redacted.Contains("secret", StringComparison.Ordinal) &&
                     decision.Code == "ENDPOINT_TCP_BLOCKED";
        output.WriteLine($"CONNECTION DIAGNOSTIC SELF-TEST: redaction={!redacted.Contains("secret", StringComparison.Ordinal)}; rate_limit={emitted}/120; decision={decision.Code}");
        return passed ? 0 : 1;
    }
}

internal static class NoraDiagnosticEnvironment
{
    private static readonly Regex RouteRow = new(
        @"^\s*(?<destination>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mask>\d{1,3}(?:\.\d{1,3}){3})\s+(?<gateway>\S+)\s+(?<interface>\S+)\s+(?<metric>\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Describe()
    {
        var identity = WindowsIdentity.GetCurrent();
        var admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var assembly = Assembly.GetExecutingAssembly().GetName();
        var lines = new List<string>
        {
            $"app version={assembly.Version}; process_arch={RuntimeInformation.ProcessArchitecture}; runtime={Environment.Version}",
            $"system os={RuntimeInformation.OSDescription}; os_arch={RuntimeInformation.OSArchitecture}; admin={admin}; network_available={NetworkInterface.GetIsNetworkAvailable()}",
            $"proxy_env http={(HasEnvironmentValue("HTTP_PROXY") ? "set" : "unset")}; https={(HasEnvironmentValue("HTTPS_PROXY") ? "set" : "unset")}; all={(HasEnvironmentValue("ALL_PROXY") ? "set" : "unset")}",
            DescribeCore("xray", NoraSubscriptionStore.XrayPath()),
            DescribeCore("sing-box", NoraSubscriptionStore.SingBoxPath()),
            DescribeCore("amneziawg", NoraSubscriptionStore.AmneziaWgPath()),
            DescribeCore("awg", NoraSubscriptionStore.AmneziaWgCliPath())
        };
        var discordMissing = NoraDiscordModeSettings.MissingComponents();
        lines.Add($"discord_mode enabled={NoraDiscordModeSettings.Enabled}; components={(discordMissing.Count == 0 ? "ready" : "missing")}; protocols=VLESS,KRot; awg_supported=false");
        if (discordMissing.Count > 0)
            lines.Add("discord_mode missing=" + string.Join(",", discordMissing.Select(Path.GetFileName)));
        return lines;
    }

    public static async Task<IReadOnlyList<string>> DescribeAdaptersAsync(CancellationToken cancellationToken)
    {
        var snapshot = await NoraNetworkInterfaceCache.GetSnapshotAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await Task.Run(
            () => DescribeAdapters(snapshot.Interfaces),
            cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> DescribeAdapters(IReadOnlyList<NetworkInterface> interfaces)
    {
        var lines = new List<string>();
        foreach (var adapter in interfaces
                     .Where(item => item.OperationalStatus == OperationalStatus.Up)
                     .OrderBy(item => item.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 ? 0 : 1)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var properties = adapter.GetIPProperties();
                var index = properties.GetIPv4Properties()?.Index ?? 0;
                var ipv4 = properties.UnicastAddresses
                    .Where(item => item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(item => item.Address + "/" + item.PrefixLength)
                    .ToArray();
                var gateways = properties.GatewayAddresses
                    .Select(item => item.Address)
                    .Where(item => item.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .ToArray();
                var dns = properties.DnsAddresses
                    .Where(item => item.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .ToArray();
                lines.Add($"adapter if={index}; type={adapter.NetworkInterfaceType}; name={NoraDiagnosticRedactor.Sanitize(adapter.Name)}; ipv4={JoinOrNone(ipv4)}; gateway={JoinOrNone(gateways)}; dns={JoinOrNone(dns)}");
            }
            catch (Exception ex)
            {
                lines.Add($"adapter name={NoraDiagnosticRedactor.Sanitize(adapter.Name)}; inspect=failed; type={ex.GetType().Name}");
            }
        }
        if (lines.Count == 0)
            lines.Add("adapter result=fail; active_count=0");
        return lines;
    }

    public static async Task<IReadOnlyList<string>> DescribeFirewallAsync(CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "advfirewall", "show", "currentprofile", "state" })
                psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi);
            if (process is null)
                return ["firewall inspect=unavailable"];
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var compact = ((await stdout) + " " + (await stderr))
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Contains("State", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("Состояние", StringComparison.OrdinalIgnoreCase) ||
                               line.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
                               line.Equals("OFF", StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .Select(NoraDiagnosticRedactor.Sanitize)
                .ToArray();
            return [$"firewall exit={process.ExitCode}; state={(compact.Length == 0 ? "unavailable" : string.Join(" | ", compact))}"];
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return [$"firewall inspect=failed; type={ex.GetType().Name}"];
        }
    }

    public static async Task<IReadOnlyList<string>> DescribeRoutesAsync(IPAddress target, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo("route.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("PRINT");
            psi.ArgumentList.Add(target.ToString());
            using var process = Process.Start(psi);
            if (process is null)
                return ["inspect=unavailable"];
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await stdout) + Environment.NewLine + (await stderr);
            var rows = RouteRow.Matches(output)
                .Select(match => $"destination={match.Groups["destination"].Value}; mask={match.Groups["mask"].Value}; gateway={match.Groups["gateway"].Value}; interface={match.Groups["interface"].Value}; metric={match.Groups["metric"].Value}")
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToArray();
            return rows.Length == 0 ? ["inspect=no_matching_rows"] : rows;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return [$"inspect=failed; type={ex.GetType().Name}"];
        }
    }

    private static bool HasEnvironmentValue(string name)
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));

    private static string DescribeCore(string name, string path)
    {
        if (!File.Exists(path))
            return $"core name={name}; present=false";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var size = new FileInfo(path).Length;
            return $"core name={name}; present=true; version={info.FileVersion ?? "unknown"}; bytes={size}";
        }
        catch
        {
            return $"core name={name}; present=true; version=unavailable";
        }
    }

    private static string JoinOrNone<T>(IEnumerable<T> values)
    {
        var array = values.Select(value => value?.ToString() ?? "").Where(value => value.Length > 0).ToArray();
        return array.Length == 0 ? "none" : string.Join(',', array);
    }
}

internal sealed class NoraCoreLogLimiter
{
    private sealed class Counter
    {
        public int Count;
        public string Latest = "";
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Counter> _counters = new(StringComparer.Ordinal);

    public string? Accept(string raw)
    {
        var safe = NoraDiagnosticRedactor.Sanitize(raw);
        var category = Category(safe);
        if (category is null)
            return safe;
        lock (_gate)
        {
            if (!_counters.TryGetValue(category, out var counter))
                _counters[category] = counter = new Counter();
            counter.Count++;
            counter.Latest = safe;
            if (counter.Count <= 3)
                return safe;
            if (counter.Count is 10 or 25 or 50 || counter.Count % 100 == 0)
                return $"{category}: repeated {counter.Count} times (duplicate core lines suppressed)";
            return null;
        }
    }

    public IReadOnlyList<string> Summaries()
    {
        lock (_gate)
            return _counters
                .Where(pair => pair.Value.Count > 3)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"core_summary category={pair.Key}; occurrences={pair.Value.Count}; duplicates_suppressed={pair.Value.Count - 3}")
                .ToArray();
    }

    private static string? Category(string line)
    {
        if (line.Contains("dialing TCP", StringComparison.OrdinalIgnoreCase)) return "xray_tcp_dial";
        if (line.Contains("context deadline exceeded", StringComparison.OrdinalIgnoreCase)) return "deadline_exceeded";
        if (line.Contains("exchange failed", StringComparison.OrdinalIgnoreCase)) return "dns_exchange_failed";
        if (line.Contains("accepted tcp", StringComparison.OrdinalIgnoreCase)) return "xray_tcp_accepted";
        if (line.Contains("connection ends", StringComparison.OrdinalIgnoreCase)) return "xray_connection_end";
        if (line.Contains("inbound connection", StringComparison.OrdinalIgnoreCase)) return "tun_inbound_connection";
        if (line.Contains("traffic:", StringComparison.OrdinalIgnoreCase)) return "traffic_sample";
        return null;
    }
}

internal static partial class NoraDiagnosticRedactor
{
    private static readonly Regex VlessLink = new(@"(?i)\bvless://\S+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Uuid = new(@"(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretField = new(@"(?i)\b(password|token|private[_-]?key|short[_-]?id|premium[_-]?code|uuid)\s*[:=]\s*[^\s;]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UserPath = new(@"(?i)C:\\Users\\[^\\\s]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Sanitize(string value)
    {
        var safe = VlessLink.Replace(value ?? "", "<redacted-vless-link>");
        safe = Uuid.Replace(safe, "<redacted-id>");
        safe = SecretField.Replace(safe, match => match.Groups[1].Value + "=<redacted>");
        safe = UserPath.Replace(safe, "%USERPROFILE%");
        return safe.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
#endif
