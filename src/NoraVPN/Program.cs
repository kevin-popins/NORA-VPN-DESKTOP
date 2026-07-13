using System.Buffers.Binary;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Nvp;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                // A normal second launch must restore the existing tray-hidden
                // app before it can trigger UAC or create another GUI process.
                if (NoraSingleInstance.TryActivateExisting())
                    return 0;
                if (!NvpClient.IsAdministrator())
                {
                    return RelaunchElevatedGui();
                }
                return RunGuiSingleInstance();
            }
#endif
            Console.WriteLine("Usage:");
            Console.WriteLine("  nvp server <server-profile.json>");
            Console.WriteLine("  nvp client <client-profile.json>");
            return 2;
        }

        if (args.Length == 1 && args[0].Equals("gui", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                return RunGuiSingleInstance();
            }
#endif
            Console.Error.WriteLine("GUI mode is only available on Windows.");
            return 2;
        }

        if (args.Length == 1 && args[0].Equals("error-selftest", StringComparison.OrdinalIgnoreCase))
            return NoraErrors.RunSelfTest(Console.Out);

        if (args.Length == 1 && args[0].Equals("hwid-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            var first = NoraSubscriptionStore.BuildDeviceHwid("fixture-machine-guid");
            var second = NoraSubscriptionStore.BuildDeviceHwid("fixture-machine-guid");
            using var request = new HttpRequestMessage();
            NoraSubscriptionStore.AddSubscriptionIdentityHeaders(request, "NORAvpn/1.0", first, "NORA-TEST-PC");
            static bool HeaderIs(HttpRequestMessage message, string name, string expected)
                => message.Headers.TryGetValues(name, out var values) && values.SingleOrDefault() == expected;
            var passed = first == second &&
                         first.StartsWith("NORAvpn-", StringComparison.Ordinal) &&
                         first.Length == 40 &&
                         HeaderIs(request, "X-HWID", first) &&
                         HeaderIs(request, "X-Device-Name", "NORA-TEST-PC") &&
                         HeaderIs(request, "X-Client-Name", "NORAvpn") &&
                         HeaderIs(request, "User-Agent", "NORAvpn/1.0");
            Console.WriteLine(passed ? "HWID SELF-TEST PASS" : "HWID SELF-TEST FAIL");
            return passed ? 0 : 1;
#else
            Console.Error.WriteLine("HWID self-test is only available on Windows.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("crypt5-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return HappCrypt5Decoder.RunSelfTest(Console.Out);
#else
            Console.Error.WriteLine("HAPP crypt5 self-test is only available in the Windows client build.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("subscription-transport-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return NoraSubscriptionStore.RunTransportSelfTest(Console.Out);
#else
            Console.Error.WriteLine("Subscription transport self-test is only available in the Windows client build.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("subscription-order-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return NoraSubscriptionStore.RunOrderingSelfTest(Console.Out);
#else
            Console.Error.WriteLine("Subscription ordering self-test is only available in the Windows client build.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("country-flag-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return FlagIcon.RunSelfTest(Console.Out);
#else
            Console.Error.WriteLine("Country flag self-test is only available in the Windows client build.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("cores-check", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            var missing = NoraSubscriptionStore.MissingVlessCorePaths();
            if (missing.Count == 0)
            {
                Console.WriteLine("VLESS CORE CHECK PASS: xray.exe and sing-box.exe are present.");
                return 0;
            }
            Console.Error.WriteLine("VLESS CORE CHECK FAIL: " + string.Join(", ", missing));
            return 1;
#else
            Console.Error.WriteLine("Core check is only available on Windows.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("errors", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var error in NoraErrors.Catalog)
                Console.WriteLine($"{error.Code}\t{error.Severity}\t{error.Title}\t{error.Message}\t{error.Action}");
            return 0;
        }

        if (args.Length is >= 2 and <= 4 && args[0].Equals("ui-snapshot", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
                return NoraWpfShell.RenderSnapshot(
                    args[1],
                    args.Length >= 3 ? args[2] : "ready",
                    args.Length == 4 && int.TryParse(args[3], out var snapshotDelay) ? snapshotDelay : 700);
#endif
            Console.Error.WriteLine("UI snapshot mode is only available on Windows.");
            return 2;
        }

        if (args.Length == 1 && args[0].Equals("direct-latency-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return NoraDirectLatencyProbe.RunSelfTest(Console.Out);
#else
            Console.Error.WriteLine("Direct latency self-test is only available in the Windows client build.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("audio-capture-smoke", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return NoraSpectrumAnalyzer.RunCaptureSmoke(Console.Out);
#else
            Console.Error.WriteLine("Audio capture smoke is only available in the Windows client build.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("audio-session-meter-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return NoraSpectrumAnalyzer.RunSessionMeterFallbackSelfTest(Console.Out);
#else
            Console.Error.WriteLine("Audio session-meter diagnostics are available only on Windows.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("run-log-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return NoraRunLog.RunSelfTest(Console.Out);
#else
            Console.Error.WriteLine("Desktop run-log diagnostics are available only on Windows.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("connection-diagnostic-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return NoraConnectionDiagnosticRunner.RunSelfTest(Console.Out);
#else
            Console.Error.WriteLine("Connection diagnostic self-test is only available in the Windows client build.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("traffic-motion-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return NoraTrafficGraph.RunMotionSelfTest(Console.Out);
#else
            Console.Error.WriteLine("Traffic motion self-test is only available in the Windows client build.");
            return 2;
#endif
        }

        if (args.Length == 1 && args[0].Equals("ui-scale-selftest", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            return NoraWindowScalePolicy.RunSelfTest();
#else
            Console.Error.WriteLine("Window scale self-test is only available in the Windows client build.");
            return 2;
#endif
        }

        if (args.Length == 3 && args[0].Equals("direct-latency-probe", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            {
                Console.Error.WriteLine("Port must be between 1 and 65535.");
                return 2;
            }
            var result = NoraDirectLatencyProbe.ProbeAsync(args[1], port, TimeSpan.FromMilliseconds(2500)).GetAwaiter().GetResult();
            Console.WriteLine($"direct-latency: status={result.Status}; latency_ms={result.Milliseconds?.ToString(CultureInfo.InvariantCulture) ?? ""}; interface={result.InterfaceName}; detail={result.Detail}");
            return result.Success ? 0 : 1;
#else
            Console.Error.WriteLine("Direct latency probing is only available in the Windows client build.");
            return 2;
#endif
        }

        if (args.Length == 2 && args[0].Equals("ui-tray-smoke", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
                return NoraWpfShell.RunTraySmoke(args[1]);
#endif
            Console.Error.WriteLine("UI tray smoke mode is only available on Windows.");
            return 2;
        }

        if (args.Length == 2 && args[0].Equals("ui-connect-cancel-smoke", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
                return NoraWpfShell.RunConnectCancelSmoke(args[1]);
#endif
            Console.Error.WriteLine("UI connection-cancel smoke mode is only available on Windows.");
            return 2;
        }

        if (args.Length < 2 || args[0] is not ("server" or "client" or "diag" or "resume-diag" or "security-diag" or "cover-diag" or "garbage-diag" or "lint" or "tuncheck" or "subdiag" or "singbox-config" or "xray-config" or "xray-tun-config" or "xray-diag" or "user-add" or "user-disable"))
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  nvp server <server-profile.json>");
            Console.WriteLine("  nvp client <client-profile.json>");
            Console.WriteLine("  nvp diag <client-profile.json>");
            Console.WriteLine("  nvp resume-diag <client-profile.json>");
            Console.WriteLine("  nvp security-diag <client-profile.json>");
            Console.WriteLine("  nvp cover-diag <client-profile.json>");
            Console.WriteLine("  nvp garbage-diag <client-profile.json>");
            Console.WriteLine("  nvp lint <profile.json>");
            Console.WriteLine("  nvp tuncheck <client-profile.json>");
            Console.WriteLine("  nvp subdiag <subscription-url-or-payload>");
            Console.WriteLine("  nvp singbox-config <subscription-server.json>");
            Console.WriteLine("  nvp xray-config <subscription-server.json>");
            Console.WriteLine("  nvp xray-tun-config <local-socks-port>");
            Console.WriteLine("  nvp xray-diag <subscription-server.json> [result.log]");
            Console.WriteLine("  nvp user-add <server-profile.json> <credential.json>");
            Console.WriteLine("  nvp user-disable <server-profile.json> <credential-id>");
            Console.WriteLine("  nvp errors");
            Console.WriteLine("  nvp error-selftest");
            Console.WriteLine("  nvp crypt5-selftest");
            Console.WriteLine("  nvp subscription-transport-selftest");
            Console.WriteLine("  nvp subscription-order-selftest");
            Console.WriteLine("  nvp country-flag-selftest");
            Console.WriteLine("  nvp direct-latency-selftest");
            Console.WriteLine("  nvp audio-capture-smoke");
            Console.WriteLine("  nvp audio-session-meter-selftest");
            Console.WriteLine("  nvp run-log-selftest");
            Console.WriteLine("  nvp connection-diagnostic-selftest");
            Console.WriteLine("  nvp traffic-motion-selftest");
            Console.WriteLine("  nvp ui-scale-selftest");
            Console.WriteLine("  nvp direct-latency-probe <host> <port>");
            Console.WriteLine("  nvp ui-connect-cancel-smoke <result.json>");
            return 2;
        }

        try
        {
            if (args[0] == "user-add")
            {
                if (args.Length != 3)
                    throw new InvalidOperationException("user-add requires a server profile and credential mutation file.");
                var credential = JsonSerializer.Deserialize<NvpCredential>(File.ReadAllText(args[2]), NvpConfig.JsonOptions())
                    ?? throw new InvalidOperationException("Credential mutation file is empty.");
                NvpProfileStore.AddCredential(args[1], credential);
                File.Delete(args[2]);
                Console.WriteLine("credential-added:" + credential.Id);
                return 0;
            }
            if (args[0] == "user-disable")
            {
                if (args.Length != 3)
                    throw new InvalidOperationException("user-disable requires a server profile and credential id.");
                NvpProfileStore.DisableCredential(args[1], args[2]);
                Console.WriteLine("credential-disabled:" + args[2]);
                return 0;
            }
            if (args[0] == "subdiag")
            {
#if WINDOWS
                var sub = NoraSubscriptionStore.ImportAsync(args[1], Console.WriteLine).GetAwaiter().GetResult();
                Console.WriteLine($"subscription: title={sub.Title}; servers={sub.Servers.Count}; upload={sub.UploadBytes}; download={sub.DownloadBytes}; total={sub.TotalBytes}; update_hours={sub.UpdateIntervalHours}; hwid={sub.Hwid}");
                foreach (var server in sub.Servers)
                    Console.WriteLine($"server: name={server.Name}; host={server.Host}:{server.Port}; protocol={server.Protocol}; inbounds={server.InboundCount}; outbounds={server.OutboundCount}; rules={server.RuleCount}; path={server.LocalPath}");
                return sub.Servers.Count > 0 ? 0 : 1;
#else
                Console.Error.WriteLine("subdiag is only available in the Windows client build.");
                return 2;
#endif
            }
            if (args[0] == "singbox-config")
            {
#if WINDOWS
                if (!NoraSubscriptionStore.TryLoadServer(args[1], out var server))
                    throw new InvalidOperationException("Cannot load subscription server json.");
                Console.WriteLine(NoraSubscriptionStore.BuildSingBoxConfig(server));
                return 0;
#else
                Console.Error.WriteLine("singbox-config is only available in the Windows client build.");
                return 2;
#endif
            }
            if (args[0] == "xray-config")
            {
#if WINDOWS
                if (!NoraSubscriptionStore.TryLoadServer(args[1], out var server))
                    throw new InvalidOperationException("Cannot load subscription server json.");
                Console.WriteLine(NoraSubscriptionStore.BuildXrayConfig(server));
                return 0;
#else
                Console.Error.WriteLine("xray-config is only available in the Windows client build.");
                return 2;
#endif
            }
            if (args[0] == "xray-tun-config")
            {
#if WINDOWS
                // Accept a local SOCKS port (legacy) or a subscription server json path so the
                // generated frontend includes that server's provider routing split.
                if (int.TryParse(args[1], out var socksPort) && socksPort is > 0 and <= 65535)
                {
                    Console.WriteLine(NoraSubscriptionStore.BuildXrayTunFrontendConfig(socksPort));
                    return 0;
                }
                if (NoraSubscriptionStore.TryLoadServer(args[1], out var tunServer))
                {
                    Console.WriteLine(NoraSubscriptionStore.BuildXrayTunFrontendConfig(20808, tunServer));
                    return 0;
                }
                throw new InvalidOperationException("xray-tun-config expects a local SOCKS port or a subscription server json path.");
#else
                Console.Error.WriteLine("xray-tun-config is only available in the Windows client build.");
                return 2;
#endif
            }
            if (args[0] == "xray-diag")
            {
#if WINDOWS
                if (!NoraSubscriptionStore.TryLoadServer(args[1], out var server))
                    throw new InvalidOperationException("Cannot load subscription server json.");
                var resultPath = args.Length > 2 ? args[2] : Path.Combine(NoraAppState.DataRoot, "logs", "xray-diag.log");
                Directory.CreateDirectory(Path.GetDirectoryName(resultPath) ?? NoraAppState.DataRoot);
                void Log(string line)
                {
                    var record = $"[{DateTime.Now:O}] {line}";
                    Console.WriteLine(record);
                    File.AppendAllText(resultPath, record + Environment.NewLine);
                }
                File.WriteAllText(resultPath, "");
                var core = new XrayCoreProcess(server, Log);
                try
                {
                    Log("Starting elevated Xray + sing-box TUN diagnostic");
                    core.StartAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
                    NoraDataPlaneProbe.VerifyAsync(core, TimeSpan.FromSeconds(35), Log).GetAwaiter().GetResult();
                    Log("PASS: Xray VLESS backend, sing-box TUN frontend, and internet data plane are operational");
                    return 0;
                }
                finally
                {
                    core.StopAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
                }
#else
                Console.Error.WriteLine("xray-diag is only available in the Windows client build.");
                return 2;
#endif
            }

            var config = NvpConfig.Load(args[1]);
            if (args[0] == "server")
                new NvpServer(config).RunAsync().GetAwaiter().GetResult();
            else if (args[0] == "client")
                new NvpClient(config).RunAsync().GetAwaiter().GetResult();
            else if (args[0] == "diag")
            {
                var diag = NvpDiagnostics.ProbeAsync(config, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"{diag.Stage}: success={diag.Success}; {diag.Details}");
                return diag.Success ? 0 : 1;
            }
            else if (args[0] == "resume-diag")
            {
                var diag = NvpDiagnostics.ProbeResumeAsync(config, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"{diag.Stage}: success={diag.Success}; {diag.Details}");
                return diag.Success ? 0 : 1;
            }
            else if (args[0] == "security-diag")
            {
                var diag = NvpDiagnostics.ProbeSecurityAsync(config, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"{diag.Stage}: success={diag.Success}; {diag.Details}");
                return diag.Success ? 0 : 1;
            }
            else if (args[0] == "cover-diag")
            {
                var diag = NvpDiagnostics.ProbeCoverAsync(config, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"{diag.Stage}: success={diag.Success}; {diag.Details}");
                return diag.Success ? 0 : 1;
            }
            else if (args[0] == "garbage-diag")
            {
                var diag = NvpDiagnostics.ProbeGarbageAsync(config, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"{diag.Stage}: success={diag.Success}; {diag.Details}");
                return diag.Success ? 0 : 1;
            }
            else if (args[0] == "lint")
            {
                foreach (var line in NvpProfileLinter.Check(config))
                    Console.WriteLine(line);
                return NvpProfileLinter.HasErrors(config) ? 1 : 0;
            }
            else
            {
                var diag = NvpDiagnostics.TunCheck(config);
                Console.WriteLine($"{diag.Stage}: success={diag.Success}; {diag.Details}");
                return diag.Success ? 0 : 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            var incident = NoraErrors.Classify(OperationForCommand(args[0]), ex);
            Console.Error.WriteLine(incident.ToLogLine());
            Console.Error.WriteLine($"{incident.Code}: {incident.Message} What to do: {incident.Action}");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RelaunchElevatedGui()
    {
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path");
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            psi.ArgumentList.Add("gui");
            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            var incident = NoraErrors.Classify(NoraOperation.ApplicationStart, ex);
#if WINDOWS
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    $"{incident.Message}\n\nWhat to do: {incident.Action}\n\nError code: {incident.Code}",
                    incident.Title,
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch
            {
                Console.Error.WriteLine(incident.ToLogLine());
            }
#else
            Console.Error.WriteLine(incident.ToLogLine());
#endif
            return 1;
        }
    }

#if WINDOWS
    private static int RunGuiSingleInstance()
    {
        if (!NoraSingleInstance.TryClaimPrimary())
        {
            // The owner may still be constructing its native window. Give it a
            // short bounded chance to publish the activation target, then leave
            // the existing process alone rather than opening a second instance.
            _ = NoraSingleInstance.TryActivateExisting(TimeSpan.FromSeconds(2));
            return 0;
        }

        try
        {
            InstallGuiExceptionHandlers();
            GuiApp.Run();
            return 0;
        }
        finally
        {
            NoraSingleInstance.ReleasePrimary();
        }
    }
#endif

    private static NoraOperation OperationForCommand(string command)
        => command.ToLowerInvariant() switch
        {
            "client" => NoraOperation.Connect,
            "subdiag" => NoraOperation.ImportProfile,
            "xray-diag" => NoraOperation.Diagnostics,
            "diag" or "resume-diag" or "security-diag" or "cover-diag" or "garbage-diag" or "tuncheck" => NoraOperation.Diagnostics,
            "user-add" => NoraOperation.CreateUser,
            "user-disable" => NoraOperation.DeleteUser,
            "lint" or "singbox-config" or "xray-config" or "xray-tun-config" => NoraOperation.LoadProfile,
            _ => NoraOperation.CommandLine
        };

#if WINDOWS
    private static void InstallGuiExceptionHandlers()
    {
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, e) =>
        {
            var incident = NoraErrors.Classify(NoraOperation.ApplicationStart, e.Exception);
            Console.Error.WriteLine(incident.ToLogLine());
            Console.Error.WriteLine(e.Exception);
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    $"{incident.Message}\n\nWhat to do: {incident.Action}\n\nError code: {incident.Code}",
                    incident.Title,
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch
            {
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Console.Error.WriteLine(NoraErrors.Classify(NoraOperation.ApplicationStart, ex).ToLogLine());
                Console.Error.WriteLine(ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine(NoraErrors.Classify(NoraOperation.ApplicationStart, e.Exception).ToLogLine());
            Console.Error.WriteLine(e.Exception);
            e.SetObserved();
        };
    }
#else
    private static void InstallGuiExceptionHandlers()
    {
    }
#endif
}

#if WINDOWS
// The primary GUI runs elevated, while the normal EXE launcher often starts at
// medium integrity. A registered, explicitly whitelisted window message lets
// that launcher restore the existing tray-hidden window without another UAC
// prompt. The named mutex prevents a race during first startup.
internal static class NoraSingleInstance
{
    private const string MutexName = @"Local\NORA.VPN.Desktop.SingleInstance.v1";
    private const string ActivationMessageName = "NORA.VPN.Desktop.Activate.v1";
    private const uint MessageFilterAllow = 1;
    private static readonly int ActivationMessageValue = unchecked((int)RegisterWindowMessage(ActivationMessageName));
    private static readonly object Sync = new();
    private static Mutex? _primaryMutex;

    internal static int ActivationMessage => ActivationMessageValue;

    public static bool TryClaimPrimary()
    {
        lock (Sync)
        {
            if (_primaryMutex is not null)
                return true;

            var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return false;
            }

            _primaryMutex = mutex;
            return true;
        }
    }

    public static bool TryActivateExisting(TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.Zero);
        do
        {
            if (TryPostActivation())
                return true;
            if (DateTime.UtcNow >= until)
                return false;
            Thread.Sleep(50);
        }
        while (true);
    }

    public static void PublishWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            var marker = new InstanceMarker(Environment.ProcessId, hwnd.ToInt64(), CurrentProcessName());
            var path = MarkerPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(marker));
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // The mutex still protects against duplicate elevated instances if
            // the local marker cannot be written.
        }
    }

    public static void AllowActivationMessage(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || ActivationMessageValue == 0)
            return;
        try
        {
            _ = ChangeWindowMessageFilterEx(hwnd, unchecked((uint)ActivationMessageValue), MessageFilterAllow, IntPtr.Zero);
        }
        catch (EntryPointNotFoundException)
        {
            // Windows 10/11 supports this API. On an unsupported old system the
            // elevated second-stage mutex path still prevents a duplicate GUI.
        }
    }

    public static void ReleasePrimary()
    {
        try
        {
            DeleteOwnMarker();
        }
        finally
        {
            lock (Sync)
            {
                if (_primaryMutex is not null)
                {
                    try { _primaryMutex.ReleaseMutex(); }
                    catch (ApplicationException) { }
                    _primaryMutex.Dispose();
                    _primaryMutex = null;
                }
            }
        }
    }

    private static bool TryPostActivation()
    {
        try
        {
            var path = MarkerPath();
            if (!File.Exists(path))
                return false;
            var marker = JsonSerializer.Deserialize<InstanceMarker>(File.ReadAllText(path));
            if (marker is null || marker.ProcessId == Environment.ProcessId || marker.WindowHandle == 0)
                return false;

            using var process = Process.GetProcessById(marker.ProcessId);
            if (process.HasExited || !string.Equals(marker.ProcessName, CurrentProcessName(), StringComparison.OrdinalIgnoreCase))
            {
                DeleteStaleMarker(path);
                return false;
            }

            return PostMessage(new IntPtr(marker.WindowHandle), unchecked((uint)ActivationMessageValue), IntPtr.Zero, IntPtr.Zero);
        }
        catch (ArgumentException)
        {
            DeleteStaleMarker(MarkerPath());
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string CurrentProcessName()
        => Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "NoraVPN");

    private static string MarkerPath() => Path.Combine(NoraAppState.DataRoot, "runtime", "desktop-instance.json");

    private static void DeleteOwnMarker()
    {
        try
        {
            var path = MarkerPath();
            if (!File.Exists(path))
                return;
            var marker = JsonSerializer.Deserialize<InstanceMarker>(File.ReadAllText(path));
            if (marker?.ProcessId == Environment.ProcessId)
                File.Delete(path);
        }
        catch { }
    }

    private static void DeleteStaleMarker(string path)
    {
        try { File.Delete(path); }
        catch { }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string text);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeFilterStruct);

    private sealed record InstanceMarker(int ProcessId, long WindowHandle, string ProcessName);
}
#endif

internal sealed class NvpConfig
{
    public string Schema { get; set; } = "nvp-profile-v1";
    public string ProfileId { get; set; } = "";
    public string CredentialId { get; set; } = "";
    public string CredentialKey { get; set; } = "";
    public List<NvpCredential> Credentials { get; set; } = [];
    public List<NvpServerEntry> Servers { get; set; } = [];
    public NvpTunnelConfig Tunnel { get; set; } = new();
    public string TransportProfile { get; set; } = "tcp_emergency_morph_v1";
    public List<NvpCoverProfile> CoverProfiles { get; set; } = [];
    public NvpTlsConfig Tls { get; set; } = new();
    public NvpSecurityConfig Security { get; set; } = new();
    public NvpShapingConfig Shaping { get; set; } = new();
    public int ListenPort { get; set; } = 443;
    [JsonIgnore]
    public string SourcePath { get; set; } = "";
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }

    public static NvpConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<NvpConfig>(json, JsonOptions()) ?? throw new InvalidOperationException("Empty config");
        cfg.SourcePath = path;
        if (cfg.Servers.Count == 0)
            cfg.Servers.Add(new NvpServerEntry { Address = "0.0.0.0", Port = cfg.ListenPort });
        if (cfg.CoverProfiles.Count == 0)
            cfg.CoverProfiles.Add(new NvpCoverProfile { Name = cfg.TransportProfile, Mode = cfg.TransportProfile });
        if (string.IsNullOrWhiteSpace(cfg.CredentialId) || string.IsNullOrWhiteSpace(cfg.CredentialKey))
            throw new InvalidOperationException("credential_id and credential_key are required");
        _ = Convert.FromBase64String(cfg.CredentialKey);
        foreach (var credential in cfg.ActiveCredentials())
            _ = Convert.FromBase64String(credential.Key);
        return cfg;
    }

    public IEnumerable<NvpCredential> ActiveCredentials()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(CredentialId) && !string.IsNullOrWhiteSpace(CredentialKey))
        {
            seen.Add(CredentialId);
            yield return new NvpCredential { Id = CredentialId, Key = CredentialKey, Name = "Primary", Enabled = true, ClientIp = Tunnel.ClientIp };
        }

        foreach (var credential in Credentials)
        {
            if (!credential.Enabled)
                continue;
            if (string.IsNullOrWhiteSpace(credential.Id) || string.IsNullOrWhiteSpace(credential.Key))
                continue;
            if (seen.Add(credential.Id))
                yield return credential;
        }
    }

    public static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
}

internal sealed class NvpCredential
{
    public string Id { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string CreatedAt { get; set; } = "";
    public string LastOnlineAt { get; set; } = "";
    public long UplinkBytes { get; set; }
    public long DownlinkBytes { get; set; }
    public string ClientIp { get; set; } = "";
}

internal static class NvpProfileStore
{
    public static void AddCredential(string profilePath, NvpCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.Id) || string.IsNullOrWhiteSpace(credential.Key) || string.IsNullOrWhiteSpace(credential.Name))
            throw new InvalidOperationException("Credential id, key and name are required.");
        _ = Convert.FromBase64String(credential.Key);
        Mutate(profilePath, config =>
        {
            if (config.Credentials.Any(x => string.Equals(x.Id, credential.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException("Credential id already exists.");
            if (config.Credentials.Any(x => x.Enabled && string.Equals(x.Name, credential.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("An enabled credential with this user name already exists.");
            if (config.ActiveCredentials().Any(x => string.Equals(CredentialIp(x, config), CredentialIp(credential, config), StringComparison.Ordinal)))
                throw new InvalidOperationException("Credential client IP is already in use.");
            config.Credentials.Add(credential);
            return true;
        });
    }

    public static void DisableCredential(string profilePath, string credentialId)
    {
        Mutate(profilePath, config =>
        {
            var credential = config.Credentials.FirstOrDefault(x => string.Equals(x.Id, credentialId, StringComparison.Ordinal));
            if (credential is null)
                throw new InvalidOperationException("Credential not found.");
            credential.Enabled = false;
            return true;
        });
    }

    public static bool UpdateCredentialStats(string profilePath, string credentialId, DateTimeOffset now, long uplinkDelta, long downlinkDelta)
        => Mutate(profilePath, config =>
        {
            var credential = config.Credentials.FirstOrDefault(x => string.Equals(x.Id, credentialId, StringComparison.Ordinal));
            if (credential is null)
                return false;
            credential.LastOnlineAt = now.ToString("O");
            credential.UplinkBytes = Math.Max(0, credential.UplinkBytes + uplinkDelta);
            credential.DownlinkBytes = Math.Max(0, credential.DownlinkBytes + downlinkDelta);
            return true;
        });

    private static T Mutate<T>(string profilePath, Func<NvpConfig, T> mutation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(profilePath))!);
        var lockPath = profilePath + ".lock";
        var deadline = DateTime.UtcNow.AddSeconds(12);
        FileStream? lease = null;
        while (lease is null)
        {
            try { lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(80); }
        }
        using (lease)
        {
            var config = NvpConfig.Load(profilePath);
            var result = mutation(config);
            var temp = profilePath + ".tmp-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(config, NvpConfig.JsonOptions()));
                File.Move(temp, profilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            return result;
        }
    }

    private static string CredentialIp(NvpCredential credential, NvpConfig config)
        => string.IsNullOrWhiteSpace(credential.ClientIp) ? config.Tunnel.ClientIp : credential.ClientIp.Trim();
}

internal sealed class NvpServerEntry
{
    public string Address { get; set; } = "";
    public int Port { get; set; } = 443;
    public string TlsName { get; set; } = "";
    public string CoverHost { get; set; } = "";
}

internal sealed class NvpTunnelConfig
{
    public string InterfaceName { get; set; } = "NVP-1";
    public string LinuxInterfaceName { get; set; } = "nvp0";
    public string ClientIp { get; set; } = "10.66.0.2";
    public string ServerIp { get; set; } = "10.66.0.1";
    public string Cidr { get; set; } = "10.66.0.0/24";
    public List<string> Dns { get; set; } = ["1.1.1.1", "8.8.8.8"];
}

internal sealed class NvpCoverProfile
{
    public string Name { get; set; } = "tcp_emergency_morph_v1";
    public string Mode { get; set; } = "tcp_emergency_morph_v1";
    public string Compliance { get; set; } = "NVP-1D";
    public bool BrowserGrade { get; set; }
    public List<string> BootstrapPaths { get; set; } = [];
    public int FallbackDelayMinMs { get; set; } = 80;
    public int FallbackDelayMaxMs { get; set; } = 260;
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

internal sealed class NvpTlsConfig
{
    public bool Enabled { get; set; }
    public string CertificatePath { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
}

internal sealed class NvpSecurityConfig
{
    public bool KeyUpdateEnabled { get; set; } = true;
    public int KeyUpdateMinPackets { get; set; } = 128;
    public int KeyUpdateMaxPackets { get; set; } = 384;
    public long KeyUpdateMinBytes { get; set; } = 256 * 1024;
    public long KeyUpdateMaxBytes { get; set; } = 2 * 1024 * 1024;
    public int KeyUpdateMinSeconds { get; set; } = 180;
    public int KeyUpdateMaxSeconds { get; set; } = 600;
    public bool BootstrapTimestampRequired { get; set; } = true;
    public int BootstrapTimestampSkewSeconds { get; set; } = 300;
    public int BootstrapReplayCacheSeconds { get; set; } = 300;
    public bool WindowsBlockIpv6 { get; set; } = true;
}

internal sealed class NvpShapingConfig
{
    public bool FramePaddingEnabled { get; set; } = true;
    public int FramePaddingMinBytes { get; set; } = 0;
    public int FramePaddingMaxBytes { get; set; } = 96;
}

internal static class NvpProfileLinter
{
    public static IReadOnlyList<string> Check(NvpConfig config)
    {
        var lines = new List<string>();
        if (!string.Equals(config.Schema, "nvp-profile-v1", StringComparison.Ordinal) &&
            !string.Equals(config.Schema, "nvp-server-v1", StringComparison.Ordinal))
            lines.Add("ERROR profile_schema: unsupported schema " + config.Schema);
        if (config.Servers.Count == 0)
            lines.Add("ERROR server: no server entries");
        if (string.IsNullOrWhiteSpace(config.TransportProfile))
            lines.Add("ERROR transport_profile: missing transport profile");
        if (config.TransportProfile.Contains("browser_", StringComparison.OrdinalIgnoreCase))
        {
            var browserProfile = config.CoverProfiles.FirstOrDefault(p => string.Equals(p.Name, config.TransportProfile, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Mode, config.TransportProfile, StringComparison.OrdinalIgnoreCase));
            if (browserProfile is null || !browserProfile.BrowserGrade)
                lines.Add("ERROR cover_profile: browser-grade label without browser-grade adapter metadata");
        }
        if (string.Equals(config.TransportProfile, "tcp_http_cover_v1", StringComparison.OrdinalIgnoreCase))
            lines.Add("ERROR transport_profile: tcp_http_cover_v1 is rejected; use tcp_emergency_morph_v1 for degraded fallback");
        if (string.Equals(config.TransportProfile, "tcp_emergency_morph_v1", StringComparison.OrdinalIgnoreCase))
            lines.Add("WARN transport_profile: degraded emergency TCP profile, not KRot-B browser-grade");
        if (string.Equals(config.TransportProfile, "tls_http_cover_v1", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("WARN transport_profile: TLS cover is valid-origin degraded mode, not browser-grade H2/H3");
            if (config.Servers.Count > 0 && string.IsNullOrWhiteSpace(config.Servers[0].TlsName))
                lines.Add("ERROR tls_name: tls_http_cover_v1 requires servers[0].tls_name");
        }
        if (config.Tunnel.Dns.Count == 0)
            lines.Add("ERROR dns: no DNS servers configured for full tunnel");
        if (config.Tunnel.ClientIp == config.Tunnel.ServerIp)
            lines.Add("ERROR tunnel: client_ip and server_ip must differ");
        if (!config.Security.KeyUpdateEnabled)
            lines.Add("WARN security: key_update_v1 is disabled; long-lived sessions keep one traffic-key epoch");
        if (config.Security.KeyUpdateMinPackets < 8 || config.Security.KeyUpdateMaxPackets < config.Security.KeyUpdateMinPackets)
            lines.Add("ERROR security: invalid key_update packet threshold range");
        if (config.Security.KeyUpdateMinBytes < 4096 || config.Security.KeyUpdateMaxBytes < config.Security.KeyUpdateMinBytes)
            lines.Add("ERROR security: invalid key_update byte threshold range");
        if (config.Security.KeyUpdateMinSeconds < 30 || config.Security.KeyUpdateMaxSeconds < config.Security.KeyUpdateMinSeconds)
            lines.Add("ERROR security: invalid key_update time threshold range");
        if (config.Security.BootstrapTimestampSkewSeconds < 30 || config.Security.BootstrapTimestampSkewSeconds > 3600)
            lines.Add("ERROR security: bootstrap timestamp skew must be between 30 and 3600 seconds");
        if (config.Security.BootstrapReplayCacheSeconds < 60 || config.Security.BootstrapReplayCacheSeconds > 3600)
            lines.Add("ERROR security: bootstrap replay cache TTL must be between 60 and 3600 seconds");
        if (!config.Shaping.FramePaddingEnabled)
            lines.Add("WARN shaping: shape_padding_v1 is disabled; encrypted frame lengths track packet lengths closely");
        if (config.Shaping.FramePaddingMinBytes < 0 || config.Shaping.FramePaddingMaxBytes < config.Shaping.FramePaddingMinBytes || config.Shaping.FramePaddingMaxBytes > 512)
            lines.Add("ERROR shaping: invalid frame padding range");
        foreach (var cover in config.CoverProfiles)
        {
            if (cover.FallbackDelayMinMs < 0 || cover.FallbackDelayMaxMs < cover.FallbackDelayMinMs || cover.FallbackDelayMaxMs > 5000)
                lines.Add("ERROR cover_profile: invalid fallback delay range for " + cover.Name);
            if (cover.BootstrapPaths.Any(path => string.IsNullOrWhiteSpace(path) || !path.TrimStart().StartsWith('/')))
                lines.Add("ERROR cover_profile: bootstrap paths must be absolute URL paths for " + cover.Name);
        }
        return lines;
    }

    public static bool HasErrors(NvpConfig config)
        => Check(config).Any(line => line.StartsWith("ERROR ", StringComparison.Ordinal));
}

internal sealed class NvpServer(NvpConfig config)
{
    private readonly object _statsLock = new();
    private readonly object _sessionsLock = new();
    private readonly Dictionary<string, NvpServerSession> _sessionsByRelayIp = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NvpServerSession> _sessionsById = new(StringComparer.Ordinal);
    private static readonly TimeSpan ResumeGrace = TimeSpan.FromSeconds(90);

    public async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Server mode is intended for Linux");

        Console.WriteLine("[server] preparing tun/nat");
        using var tun = LinuxTun.Open(config.Tunnel.LinuxInterfaceName);
        LinuxNet.ConfigureServer(config);

        var listener = new TcpListener(System.Net.IPAddress.Any, config.ListenPort);
        listener.Start();
        _ = Task.Run(() => RouteTunToSessionsAsync(tun));
        _ = Task.Run(RefreshRevokedSessionsAsync);
        _ = Task.Run(KeepaliveSessionsAsync);
        Console.WriteLine($"[server] listening on 0.0.0.0:{config.ListenPort}");

        while (true)
        {
            var tcp = await listener.AcceptTcpClientAsync();
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleClientAsync(tcp, tun);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[server] client ended: " + ex.Message);
                }
            });
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, LinuxTun tun)
    {
        tcp.NoDelay = true;
        await using var rawStream = tcp.GetStream();
        Console.WriteLine("[server] tcp accepted from " + tcp.Client.RemoteEndPoint);
        var runtimeConfig = LoadRuntimeConfig();

        Stream coverStream = rawStream;
        SslStream? tlsStream = null;
        if (NvpTransports.IsTls(runtimeConfig))
        {
            var cert = NvpTransports.LoadServerCertificate(runtimeConfig);
            tlsStream = new SslStream(rawStream, leaveInnerStreamOpen: false);
            await tlsStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = cert,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ApplicationProtocols = [SslApplicationProtocol.Http11]
            });
            coverStream = tlsStream;
        }

        NvpResumePlan? resumePlan = null;
        var outcome = await NvpHandshake.AcceptAsync(coverStream, runtimeConfig, request =>
        {
            resumePlan = PrepareResumePlan(request.Credential, request.ClientCapabilities, runtimeConfig);
            return NvpHandshake.BuildServerCapabilities(runtimeConfig, request.ClientCapabilities, resumePlan.Ticket, resumePlan.Resumed);
        });
        var accepted = outcome.Accepted;
        if (accepted is null)
        {
            await CoverFallback.ServeAsync(coverStream, outcome.CoverRequest, runtimeConfig);
            tlsStream?.Dispose();
            return;
        }
        var secure = accepted.Secure;
        var credential = accepted.Credential;
        var clientIp = CredentialClientIp(credential, runtimeConfig);

        Console.WriteLine("[server] nvp session established for " + credential.Id + " client_ip=" + clientIp);
        using var cts = new CancellationTokenSource();
        long relayToTunPackets = 0;
        long relayToTunBytes = 0;
        long relayToTunDrops = 0;
        var session = await AttachSessionAsync(resumePlan ?? PrepareResumePlan(credential, accepted.ClientCapabilities, runtimeConfig), secure, cts);
        var gracefulClose = false;
        PersistCredentialStats(credential.Id, 0, 0, onlineNow: true);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var frame = await secure.ReadFrameAsync(cts.Token);
                if (frame.Type == NvpFrameType.Packet)
                {
                    if (!NvpPacketTrace.IsRoutableIpv4From(frame.Payload.Span, clientIp))
                    {
                        var drops = Interlocked.Increment(ref relayToTunDrops);
                        if (NvpPacketTrace.ShouldLog(drops))
                            Console.WriteLine($"[server] drop relay->tun #{drops} {frame.Payload.Length}b {NvpPacketTrace.Describe(frame.Payload.Span)}");
                        continue;
                    }
                    var count = Interlocked.Increment(ref relayToTunPackets);
                    Interlocked.Add(ref relayToTunBytes, frame.Payload.Length);
                    PersistCredentialStats(credential.Id, frame.Payload.Length, 0, onlineNow: true, throttle: true);
                    if (NvpPacketTrace.ShouldLog(count))
                        Console.WriteLine($"[server] relay->tun #{count} {frame.Payload.Length}b {NvpPacketTrace.Describe(frame.Payload.Span)}");
                    var toTun = frame.Payload.ToArray();
                    if (!string.Equals(session.RelayClientIp, clientIp, StringComparison.Ordinal))
                        NvpPacketTrace.RewriteIpv4Source(toTun, session.RelayClientIp);
                    NvpPacketTrace.RepairIpv4Checksums(toTun);
                    await tun.WritePacketAsync(toTun, cts.Token);
                }
                else if (frame.Type == NvpFrameType.Close)
                {
                    gracefulClose = true;
                    break;
                }
                else if (frame.Type == NvpFrameType.Ping)
                    await secure.WriteFrameAsync(NvpFrameType.Pong, ReadOnlyMemory<byte>.Empty, cts.Token);
                else if (frame.Type == NvpFrameType.Pong)
                    session.MarkPong();
            }
        }
        finally
        {
            cts.Cancel();
            var retained = DetachSession(session, cts, preserveForResume: !gracefulClose);
            PersistCredentialStats(credential.Id, 0, 0, onlineNow: false);
            tlsStream?.Dispose();
            Console.WriteLine($"[server] nvp transport closed; retained={retained}; relay->tun={relayToTunPackets}/{relayToTunBytes}b tun->relay={session.TunToRelayPackets}/{session.TunToRelayBytes}b drops={relayToTunDrops}/{session.TunToRelayDrops}");
        }
    }

    private async Task RouteTunToSessionsAsync(LinuxTun tun)
    {
        var buffer = new byte[65535];
        while (true)
        {
            try
            {
                var len = await tun.ReadPacketAsync(buffer, CancellationToken.None);
                if (len <= 0)
                    continue;
                var toRelay = buffer.AsSpan(0, len).ToArray();
                if (!NvpPacketTrace.TryGetIpv4Destination(toRelay, out var destination))
                    continue;
                NvpServerSession? session;
                lock (_sessionsLock)
                    _sessionsByRelayIp.TryGetValue(destination, out session);
                if (session is null)
                    continue;
                if (!string.Equals(session.RelayClientIp, session.WireClientIp, StringComparison.Ordinal))
                    NvpPacketTrace.RewriteIpv4Destination(toRelay, session.WireClientIp);
                NvpPacketTrace.RepairIpv4Checksums(toRelay);
                await session.SendPacketAsync(toRelay, delta => PersistCredentialStats(session.CredentialId, 0, delta, onlineNow: true, throttle: true));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[server] tun router failed: " + ex.Message);
                await Task.Delay(250);
            }
        }
    }

    private NvpConfig LoadRuntimeConfig()
    {
        if (string.IsNullOrWhiteSpace(config.SourcePath))
            return config;
        try
        {
            return NvpConfig.Load(config.SourcePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[server] profile reload failed; using last startup profile: " + ex.Message);
            return config;
        }
    }

    private static string CredentialClientIp(NvpCredential credential, NvpConfig runtimeConfig)
        => string.IsNullOrWhiteSpace(credential.ClientIp) ? runtimeConfig.Tunnel.ClientIp : credential.ClientIp.Trim();

    private NvpResumePlan PrepareResumePlan(NvpCredential credential, byte[] clientCapabilities, NvpConfig runtimeConfig)
    {
        var resumable = NvpHandshake.SupportsSessionResume(clientCapabilities);
        var requestedTicket = resumable ? NvpHandshake.ReadResumeTicket(clientCapabilities) : "";
        lock (_sessionsLock)
        {
            if (resumable && NvpResumeTickets.TryValidate(requestedTicket, credential, out var sessionId) &&
                _sessionsById.TryGetValue(sessionId, out var existing) && existing.CanResume(credential.Id))
            {
                Console.WriteLine("[server] resume accepted for " + credential.Id + " session=" + sessionId);
                return new NvpResumePlan(existing, existing.ResumeTicket, Resumed: true);
            }

            var wireClientIp = CredentialClientIp(credential, runtimeConfig);
            var relayClientIp = AllocateRelayClientIp(wireClientIp, runtimeConfig);
            var newSessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var ticket = resumable ? NvpResumeTickets.Issue(credential, newSessionId, DateTimeOffset.UtcNow.AddHours(12)) : "";
            var session = new NvpServerSession(credential.Id, wireClientIp, relayClientIp, newSessionId, ticket, resumable);
            _sessionsByRelayIp[relayClientIp] = session;
            if (resumable)
                _sessionsById[newSessionId] = session;
            Console.WriteLine($"[server] session route {session.WireClientIp} -> {session.RelayClientIp} for {credential.Id}; resumable={resumable}");
            return new NvpResumePlan(session, ticket, Resumed: false);
        }
    }

    private async Task<NvpServerSession> AttachSessionAsync(NvpResumePlan plan, NvpSecureStream secure, CancellationTokenSource cts)
    {
        var previous = plan.Session.Attach(secure, cts);
        previous?.Cancel();
        await plan.Session.FlushResumeBacklogAsync(delta => PersistCredentialStats(plan.Session.CredentialId, 0, delta, onlineNow: true, throttle: true));
        return plan.Session;
    }

    private bool DetachSession(NvpServerSession session, CancellationTokenSource cts, bool preserveForResume)
    {
        var retained = session.Detach(cts, preserveForResume ? ResumeGrace : TimeSpan.Zero);
        if (!retained)
            return true;
        if (!preserveForResume || !session.Resumable)
        {
            RemoveSession(session);
            return false;
        }
        return true;
    }

    private void RemoveSession(NvpServerSession session)
    {
        lock (_sessionsLock)
        {
            if (_sessionsByRelayIp.TryGetValue(session.RelayClientIp, out var current) && ReferenceEquals(current, session))
                _sessionsByRelayIp.Remove(session.RelayClientIp);
            if (_sessionsById.TryGetValue(session.SessionId, out var byId) && ReferenceEquals(byId, session))
                _sessionsById.Remove(session.SessionId);
        }
    }

    private string AllocateRelayClientIp(string preferred, NvpConfig runtimeConfig)
    {
        if (!_sessionsByRelayIp.ContainsKey(preferred))
            return preferred;

        var reserved = new HashSet<string>(StringComparer.Ordinal)
        {
            runtimeConfig.Tunnel.ServerIp,
            runtimeConfig.Tunnel.ClientIp
        };
        foreach (var credential in runtimeConfig.ActiveCredentials())
            reserved.Add(CredentialClientIp(credential, runtimeConfig));

        var cidr = runtimeConfig.Tunnel.Cidr;
        var slash = cidr.IndexOf('/');
        var baseIp = slash > 0 ? cidr[..slash] : "10.66.0.0";
        var octets = baseIp.Split('.');
        var prefix = octets.Length == 4 ? $"{octets[0]}.{octets[1]}.{octets[2]}." : "10.66.0.";
        for (var last = 10; last <= 250; last++)
        {
            var candidate = prefix + last;
            if (!reserved.Contains(candidate) && !_sessionsByRelayIp.ContainsKey(candidate))
                return candidate;
        }
        throw new InvalidOperationException("No free relay client IP addresses remain in " + runtimeConfig.Tunnel.Cidr);
    }

    private async Task RefreshRevokedSessionsAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            var runtimeConfig = LoadRuntimeConfig();
            var enabled = new HashSet<string>(runtimeConfig.ActiveCredentials().Select(x => x.Id), StringComparer.Ordinal);
            List<NvpServerSession> revoked;
            List<NvpServerSession> expired;
            lock (_sessionsLock)
            {
                revoked = _sessionsByRelayIp.Values.Where(session => !enabled.Contains(session.CredentialId)).Distinct().ToList();
                expired = _sessionsByRelayIp.Values.Where(session => session.IsExpired(DateTimeOffset.UtcNow)).Distinct().ToList();
            }
            foreach (var session in revoked)
            {
                Console.WriteLine("[server] revoking disabled credential session " + session.CredentialId);
                session.Stop();
                RemoveSession(session);
                PersistCredentialStats(session.CredentialId, 0, 0, onlineNow: false);
            }
            foreach (var session in expired)
            {
                Console.WriteLine("[server] resume grace expired for session " + session.SessionId);
                RemoveSession(session);
            }
        }
    }

    private async Task KeepaliveSessionsAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            List<NvpServerSession> sessions;
            lock (_sessionsLock)
                sessions = _sessionsByRelayIp.Values.Distinct().ToList();
            foreach (var session in sessions)
            {
                if (session.ShouldExpireTransport(DateTimeOffset.UtcNow))
                {
                    Console.WriteLine("[server] keepalive timeout for session " + session.SessionId);
                    session.Stop();
                    continue;
                }
                if (session.ShouldSendPing(DateTimeOffset.UtcNow))
                    await session.SendPingAsync();
            }
        }
    }

    private sealed class NvpServerSession(string credentialId, string wireClientIp, string relayClientIp, string sessionId, string resumeTicket, bool resumable)
    {
        private const int MaxResumeBacklogBytes = 512 * 1024;
        private readonly object _transportLock = new();
        private readonly Queue<byte[]> _resumeBacklog = new();
        private NvpSecureStream? _secure;
        private CancellationTokenSource? _transportCts;
        private DateTimeOffset? _resumeUntil;
        private DateTimeOffset _lastPongAt = DateTimeOffset.UtcNow;
        private DateTimeOffset _nextPingAt = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.Next(18, 33));
        private int _resumeBacklogBytes;
        public string CredentialId { get; } = credentialId;
        public string WireClientIp { get; } = wireClientIp;
        public string RelayClientIp { get; } = relayClientIp;
        public string SessionId { get; } = sessionId;
        public string ResumeTicket { get; } = resumeTicket;
        public bool Resumable { get; } = resumable;
        public long TunToRelayPackets;
        public long TunToRelayBytes;
        public long TunToRelayDrops;
        public long KeepalivePings;
        public long KeepalivePongs;

        public CancellationTokenSource? Attach(NvpSecureStream secure, CancellationTokenSource cts)
        {
            lock (_transportLock)
            {
                var previous = _transportCts;
                _secure = secure;
                _transportCts = cts;
                _resumeUntil = null;
                _lastPongAt = DateTimeOffset.UtcNow;
                _nextPingAt = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.Next(18, 33));
                return previous;
            }
        }

        public bool Detach(CancellationTokenSource cts, TimeSpan grace)
        {
            lock (_transportLock)
            {
                if (!ReferenceEquals(_transportCts, cts))
                    return false;
                _secure = null;
                _transportCts = null;
                _resumeUntil = Resumable && grace > TimeSpan.Zero ? DateTimeOffset.UtcNow.Add(grace) : DateTimeOffset.UtcNow;
                return true;
            }
        }

        public bool CanResume(string credentialId)
        {
            lock (_transportLock)
                return Resumable && string.Equals(CredentialId, credentialId, StringComparison.Ordinal) &&
                    (_secure is not null || (_resumeUntil is not null && _resumeUntil > DateTimeOffset.UtcNow));
        }

        public bool IsExpired(DateTimeOffset now)
        {
            lock (_transportLock)
                return _secure is null && _resumeUntil is not null && _resumeUntil <= now;
        }

        public void Stop()
        {
            CancellationTokenSource? cts;
            lock (_transportLock)
                cts = _transportCts;
            cts?.Cancel();
        }

        public void MarkPong()
        {
            lock (_transportLock)
                _lastPongAt = DateTimeOffset.UtcNow;
            var count = Interlocked.Increment(ref KeepalivePongs);
            if (NvpPacketTrace.ShouldLog(count))
                Console.WriteLine($"[server] keepalive pong #{count} session={SessionId}");
        }

        public bool ShouldSendPing(DateTimeOffset now)
        {
            lock (_transportLock)
                return _secure is not null && _transportCts is { IsCancellationRequested: false } && now >= _nextPingAt;
        }

        public bool ShouldExpireTransport(DateTimeOffset now)
        {
            lock (_transportLock)
                return _secure is not null && _transportCts is { IsCancellationRequested: false } && now - _lastPongAt > TimeSpan.FromSeconds(75);
        }

        public async Task SendPingAsync()
        {
            NvpSecureStream? secure;
            CancellationToken token;
            lock (_transportLock)
            {
                secure = _secure;
                token = _transportCts?.Token ?? new CancellationToken(true);
                _nextPingAt = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.Next(18, 33));
            }
            if (secure is null || token.IsCancellationRequested)
                return;
            try
            {
                var count = Interlocked.Increment(ref KeepalivePings);
                if (NvpPacketTrace.ShouldLog(count))
                    Console.WriteLine($"[server] keepalive ping #{count} session={SessionId}");
                await secure.WriteFrameAsync(NvpFrameType.Ping, ReadOnlyMemory<byte>.Empty, token);
            }
            catch
            {
                Stop();
            }
        }

        public async Task FlushResumeBacklogAsync(Action<long> persistDownlink)
        {
            List<byte[]> pending;
            lock (_transportLock)
            {
                if (_secure is null || _transportCts is null || _transportCts.IsCancellationRequested || _resumeBacklog.Count == 0)
                    return;
                pending = _resumeBacklog.ToList();
                _resumeBacklog.Clear();
                _resumeBacklogBytes = 0;
            }
            foreach (var packet in pending)
                await SendPacketAsync(packet, persistDownlink);
        }

        public async Task SendPacketAsync(byte[] packet, Action<long> persistDownlink)
        {
            NvpSecureStream? secure;
            CancellationToken token;
            lock (_transportLock)
            {
                secure = _secure;
                token = _transportCts?.Token ?? new CancellationToken(true);
            }
            if (secure is null || token.IsCancellationRequested)
            {
                QueueForResume(packet);
                return;
            }
            try
            {
                var count = Interlocked.Increment(ref TunToRelayPackets);
                Interlocked.Add(ref TunToRelayBytes, packet.Length);
                persistDownlink(packet.Length);
                if (NvpPacketTrace.ShouldLog(count))
                    Console.WriteLine($"[server] tun->relay #{count} {packet.Length}b {NvpPacketTrace.Describe(packet)} client_ip={RelayClientIp}");
                await secure.WriteFrameAsync(NvpFrameType.Packet, packet, token);
            }
            catch (Exception ex)
            {
                var drops = Interlocked.Increment(ref TunToRelayDrops);
                if (NvpPacketTrace.ShouldLog(drops))
                    Console.WriteLine($"[server] drop tun->relay #{drops} {packet.Length}b client_ip={RelayClientIp}: {ex.Message}");
                Stop();
            }
        }

        private void QueueForResume(byte[] packet)
        {
            lock (_transportLock)
            {
                if (!Resumable || _resumeUntil is null || _resumeUntil <= DateTimeOffset.UtcNow)
                    return;
                if (packet.Length > MaxResumeBacklogBytes || _resumeBacklogBytes + packet.Length > MaxResumeBacklogBytes)
                {
                    var drops = Interlocked.Increment(ref TunToRelayDrops);
                    if (NvpPacketTrace.ShouldLog(drops))
                        Console.WriteLine($"[server] resume backlog full; dropping {packet.Length}b client_ip={RelayClientIp}");
                    return;
                }
                _resumeBacklog.Enqueue(packet);
                _resumeBacklogBytes += packet.Length;
            }
        }
    }

    private sealed record NvpResumePlan(NvpServerSession Session, string Ticket, bool Resumed);

    private readonly Dictionary<string, DateTimeOffset> _lastStatsFlush = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Uplink, long Downlink)> _pendingStats = new(StringComparer.Ordinal);

    private void PersistCredentialStats(string credentialId, long uplinkDelta, long downlinkDelta, bool onlineNow, bool throttle = false)
    {
        if (string.IsNullOrWhiteSpace(config.SourcePath))
            return;
        var now = DateTimeOffset.UtcNow;
        lock (_statsLock)
        {
            var pending = _pendingStats.TryGetValue(credentialId, out var existing) ? existing : (0, 0);
            pending = (pending.Item1 + uplinkDelta, pending.Item2 + downlinkDelta);
            if (throttle &&
                _lastStatsFlush.TryGetValue(credentialId, out var last) &&
                now - last < TimeSpan.FromSeconds(4) &&
                pending.Item1 + pending.Item2 < 256 * 1024)
            {
                _pendingStats[credentialId] = pending;
                return;
            }

            try
            {
                if (!NvpProfileStore.UpdateCredentialStats(config.SourcePath, credentialId, now, pending.Item1, pending.Item2))
                {
                    var latest = NvpConfig.Load(config.SourcePath);
                    if (!string.Equals(latest.CredentialId, credentialId, StringComparison.Ordinal))
                        return;
                    _pendingStats.Remove(credentialId);
                    return;
                }
                _pendingStats.Remove(credentialId);
                _lastStatsFlush[credentialId] = now;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[server] stats update failed: " + ex.Message);
            }
        }
    }
}

internal sealed class NvpClient(NvpConfig config)
{
    public async Task RunAsync()
    {
        if (OperatingSystem.IsLinux())
        {
            await new NvpLinuxClient(config).RunAsync();
            return;
        }
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Client mode is intended for Windows or Linux");
        if (!IsAdministrator())
            throw new InvalidOperationException("Run nvp.exe as Administrator/root so it can create TUN routes");

        await using var session = new NvpClientSession(config, line => Console.WriteLine("[client] " + line));
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            session.Stop();
        };
        var stdinStop = NvpConsole.CreateInteractiveStopTask(session.Stop);
        await session.StartAsync();
        Console.WriteLine("[client] tunnel is up. Press Ctrl+C to stop.");
        await Task.WhenAny(session.WaitAsync(), stdinStop);
    }

    public static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

internal sealed class NvpLinuxClient(NvpConfig config)
{
    public async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Linux client mode is intended for Linux");
        if (geteuid() != 0)
            throw new InvalidOperationException("Run nvp client as root so it can create TUN routes");

        await using var session = new NvpLinuxClientSession(config, line => Console.WriteLine("[client-linux] " + line));
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            session.Stop();
        };
        var stdinStop = NvpConsole.CreateInteractiveStopTask(session.Stop);
        await session.StartAsync();
        Console.WriteLine("[client-linux] tunnel is up. Press Ctrl+C to stop.");
        await Task.WhenAny(session.WaitAsync(), stdinStop);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();
}

internal static class NvpConsole
{
    public static Task CreateInteractiveStopTask(Action stop)
    {
        if (Console.IsInputRedirected)
            return Task.Delay(Timeout.InfiniteTimeSpan);
        return Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var line = await Console.In.ReadLineAsync();
                    if (line is null)
                        continue;
                    if (line.Equals("stop", StringComparison.OrdinalIgnoreCase))
                    {
                        stop();
                        return;
                    }
                }
            }
            catch
            {
            }
        });
    }
}

internal sealed class NvpLinuxClientSession(NvpConfig config, Action<string>? log = null) : IAsyncDisposable
{
    private TcpClient? _tcp;
    private Stream? _stream;
    private NvpSecureStream? _secure;
    private LinuxTun? _tun;
    private CancellationTokenSource? _cts;
    private Task? _uplink;
    private Task? _downlink;
    private NvpServerEntry? _server;
    private readonly SemaphoreSlim _resumeGate = new(1, 1);
    private string? _resumeTicket;
    private int _stopping;
    private long _tunToRelayPackets;
    private long _tunToRelayBytes;
    private long _relayToTunPackets;
    private long _relayToTunBytes;

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public async Task StartAsync()
    {
        if (IsRunning)
            return;
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Linux client session is intended for Linux");

        var lint = NvpProfileLinter.Check(config);
        foreach (var line in lint)
            Log("Profile lint: " + line);
        if (lint.Any(line => line.StartsWith("ERROR ", StringComparison.Ordinal)))
            throw new InvalidOperationException("Profile lint failed; refusing to start unsafe profile");

        _server = config.Servers[0];
        _cts = new CancellationTokenSource();
        Log($"Connecting to {_server.Address}:{_server.Port}");
        _secure = await OpenTransportAsync(null, requireResume: false, _cts.Token);
        Log("KRot encrypted session established");

        Log("Opening Linux TUN adapter");
        _tun = LinuxTun.Open(config.Tunnel.LinuxInterfaceName);
        Log("Applying Linux routes and DNS");
        LinuxNet.ConfigureClient(config, _server.Address);
        var routeStatus = LinuxNet.VerifyClient(config, _server.Address);
        if (!routeStatus.Success)
            throw new InvalidOperationException(routeStatus.Details);
        Log("Linux TUN, DNS and full-tunnel routes verified: " + routeStatus.Details);

        _uplink = Task.Run(async () =>
        {
            var buffer = new byte[65535];
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var len = await _tun.ReadPacketAsync(buffer, _cts.Token);
                    if (len <= 0)
                        continue;
                    if (!NvpPacketTrace.IsRoutableIpv4From(buffer.AsSpan(0, len), config.Tunnel.ClientIp))
                    {
                        Log($"drop tun->relay {len}b {NvpPacketTrace.Describe(buffer.AsSpan(0, len))}");
                        continue;
                    }
                    var count = Interlocked.Increment(ref _tunToRelayPackets);
                    Interlocked.Add(ref _tunToRelayBytes, len);
                    if (NvpPacketTrace.ShouldLog(count))
                        Log($"tun->relay #{count} {len}b {NvpPacketTrace.Describe(buffer.AsSpan(0, len))}");
                    var toRelay = buffer.AsSpan(0, len).ToArray();
                    NvpPacketTrace.RepairIpv4Checksums(toRelay);
                    var secure = _secure ?? throw new IOException("KRot transport is unavailable");
                    try
                    {
                        await secure.WriteFrameAsync(NvpFrameType.Packet, toRelay, _cts.Token);
                    }
                    catch (Exception ex) when (!_cts.IsCancellationRequested)
                    {
                        Log("Uplink transport lost: " + ex.Message);
                        if (!await ResumeTransportAsync(secure, _cts.Token))
                            throw;
                        await (_secure ?? throw new IOException("KRot resume did not attach a transport")).WriteFrameAsync(NvpFrameType.Packet, toRelay, _cts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log("Uplink failed: " + ex.Message);
                Stop();
            }
        }, _cts.Token);

        _downlink = Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var secure = _secure ?? throw new IOException("KRot transport is unavailable");
                    NvpFrame frame;
                    try
                    {
                        frame = await secure.ReadFrameAsync(_cts.Token);
                    }
                    catch (Exception ex) when (!_cts.IsCancellationRequested)
                    {
                        Log("Downlink transport lost: " + ex.Message);
                        if (await ResumeTransportAsync(secure, _cts.Token))
                            continue;
                        throw;
                    }
                    if (frame.Type == NvpFrameType.Packet)
                    {
                        var count = Interlocked.Increment(ref _relayToTunPackets);
                        Interlocked.Add(ref _relayToTunBytes, frame.Payload.Length);
                        if (NvpPacketTrace.ShouldLog(count))
                            Log($"relay->tun #{count} {frame.Payload.Length}b {NvpPacketTrace.Describe(frame.Payload.Span)}");
                        var toTun = frame.Payload.ToArray();
                        NvpPacketTrace.RepairIpv4Checksums(toTun);
                        await _tun.WritePacketAsync(toTun, _cts.Token);
                    }
                    else if (frame.Type == NvpFrameType.Ping)
                        await secure.WriteFrameAsync(NvpFrameType.Pong, ReadOnlyMemory<byte>.Empty, _cts.Token);
                    else if (frame.Type == NvpFrameType.Close)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log("Downlink failed: " + ex.Message);
            }
            finally { }
        }, _cts.Token);
        Log("Triggering IPv4 data-plane probe");
        LinuxNet.ProbeIpv4Traffic();
        await WaitForDataPlaneCounterAsync(() => Interlocked.Read(ref _tunToRelayPackets), TimeSpan.FromSeconds(3), _cts.Token);
        if (Interlocked.Read(ref _tunToRelayPackets) == 0)
            throw new InvalidOperationException("No IPv4 packets were captured from Linux TUN after route setup and explicit ping probe");
    }

    private static async Task WaitForDataPlaneCounterAsync(Func<long> readCounter, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (readCounter() == 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(100, ct).ConfigureAwait(false);
    }

    public async Task WaitAsync()
    {
        var downlink = _downlink;
        var uplink = _uplink;
        if (downlink is null || uplink is null)
            return;
        await Task.WhenAny(downlink, uplink);
        Stop();
    }

    public void Stop()
    {
        var cts = _cts;
        if (cts is null || cts.IsCancellationRequested || Interlocked.Exchange(ref _stopping, 1) != 0)
            return;

        Log("Disconnecting");
        _ = _secure?.WriteFrameAsync(NvpFrameType.Close, ReadOnlyMemory<byte>.Empty, CancellationToken.None).ContinueWith(_ => { });
        cts.Cancel();
        if (_server is not null)
            LinuxNet.RestoreClient(config, _server.Address);
        Log($"Data-plane counters: tun->relay={Interlocked.Read(ref _tunToRelayPackets)}/{Interlocked.Read(ref _tunToRelayBytes)}b relay->tun={Interlocked.Read(ref _relayToTunPackets)}/{Interlocked.Read(ref _relayToTunBytes)}b");
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        _tcp?.Dispose();
        _stream?.Dispose();
        _tun?.Dispose();
        if (_uplink is not null)
            await Task.WhenAny(_uplink, Task.Delay(1000));
        if (_downlink is not null)
            await Task.WhenAny(_downlink, Task.Delay(1000));
        _resumeGate.Dispose();
    }

    private async Task<NvpSecureStream> OpenTransportAsync(string? resumeTicket, bool requireResume, CancellationToken ct)
    {
        var server = _server ?? throw new InvalidOperationException("KRot server is not selected");
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(server.Address, server.Port, ct);
            tcp.NoDelay = true;
            var stream = tcp.GetStream();
            Log(requireResume ? "Reconnecting TLS cover channel" : "TCP cover channel established");
            var handshake = await NvpHandshake.ConnectAsync(stream, config, server, resumeTicket);
            if (requireResume && !handshake.ResumeAccepted)
                throw new IOException("Server did not accept the resume ticket");
            var oldTcp = _tcp;
            var oldStream = _stream;
            _tcp = tcp;
            _stream = stream;
            _secure = handshake.Secure;
            _resumeTicket = string.IsNullOrWhiteSpace(handshake.ResumeTicket) ? _resumeTicket : handshake.ResumeTicket;
            oldStream?.Dispose();
            oldTcp?.Dispose();
            return handshake.Secure;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    private async Task<bool> ResumeTransportAsync(NvpSecureStream failedTransport, CancellationToken ct)
    {
        if (Volatile.Read(ref _stopping) != 0 || string.IsNullOrWhiteSpace(_resumeTicket))
            return false;
        await _resumeGate.WaitAsync(ct);
        try
        {
            if (!ReferenceEquals(_secure, failedTransport))
                return _secure is not null;
            _secure = null;
            _stream?.Dispose();
            _tcp?.Dispose();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(85);
            var attempt = 0;
            Exception? last = null;
            while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
            {
                attempt++;
                try
                {
                    await OpenTransportAsync(_resumeTicket, requireResume: true, ct);
                    Log($"KRot session resumed after transport loss (attempt {attempt})");
                    return true;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    last = ex;
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(4000, 250 * attempt)), ct);
                }
            }
            Log("KRot resume window expired" + (last is null ? "" : ": " + last.Message));
            return false;
        }
        finally
        {
            _resumeGate.Release();
        }
    }

    private void Log(string message) => log?.Invoke(message);
}

internal sealed class NvpClientSession(NvpConfig config, Action<string>? log = null) : IAsyncDisposable
{
    private TcpClient? _tcp;
    private Stream? _stream;
    private NvpSecureStream? _secure;
    private WintunDevice? _tun;
    private CancellationTokenSource? _cts;
    private Task? _uplink;
    private Task? _downlink;
    private Task? _metrics;
    private NvpServerEntry? _server;
    private string? _publicIpBefore;
    private readonly SemaphoreSlim _resumeGate = new(1, 1);
    private string? _resumeTicket;
    private int _stopping;
    private long _tunToRelayPackets;
    private long _tunToRelayBytes;
    private long _relayToTunPackets;
    private long _relayToTunBytes;

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public async Task StartAsync()
    {
        if (IsRunning)
            return;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Client mode is intended for Windows");
        if (!NvpClient.IsAdministrator())
            throw new InvalidOperationException("Administrator privileges are required to create the VPN interface and routes");
        var lint = NvpProfileLinter.Check(config);
        foreach (var line in lint)
            Log("Profile lint: " + line);
        if (lint.Any(line => line.StartsWith("ERROR ", StringComparison.Ordinal)))
            throw new NoraAppException("NORA-KRT-3001", "Profile lint failed; refusing to start unsafe profile");

        _server = config.Servers[0];
        _cts = new CancellationTokenSource();
        Log($"Connecting to {_server.Address}:{_server.Port}");
        try
        {
            _publicIpBefore = await NvpDiagnostics.ResolvePublicIpAsync(TimeSpan.FromSeconds(8), CancellationToken.None);
            Log("Public IP before connect: " + _publicIpBefore);
        }
        catch (Exception ex)
        {
            _publicIpBefore = null;
            Log("Public IP probe before connect unavailable: " + ex.Message);
        }

        _secure = await OpenTransportAsync(null, requireResume: false, _cts.Token);
        Log("KRot encrypted session established");

        Log("Opening Wintun adapter");
        _tun = WintunDevice.Open(config.Tunnel.InterfaceName);
        Log($"Wintun interface index: {_tun.InterfaceIndex}");
        foreach (var warning in WindowsNet.ActiveVpnWarnings(config))
            Log("Route warning: " + warning);
        Log("Applying Windows routes and DNS");
        WindowsNet.ConfigureClient(config, _server.Address, _tun.InterfaceIndex);
        var routeStatus = WindowsNet.VerifyClient(config, _server.Address, _tun.InterfaceIndex);
        if (!routeStatus.Success)
            throw new NoraAppException("NORA-KRT-3005", routeStatus.Details);
        Log("Wintun adapter, DNS and full-tunnel routes verified: " + routeStatus.Details);
        _uplink = Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var packet = await _tun.ReadPacketAsync(_cts.Token);
                    if (packet.Length > 0)
                    {
                        if (!NvpPacketTrace.IsRoutableIpv4From(packet.Span, config.Tunnel.ClientIp))
                        {
                            Log($"drop tun->relay {packet.Length}b {NvpPacketTrace.Describe(packet.Span)}");
                            continue;
                        }
                        var count = Interlocked.Increment(ref _tunToRelayPackets);
                        Interlocked.Add(ref _tunToRelayBytes, packet.Length);
                        if (NvpPacketTrace.ShouldLog(count))
                            Log($"tun->relay #{count} {packet.Length}b {NvpPacketTrace.Describe(packet.Span)}");
                        var toRelay = packet.ToArray();
                        NvpPacketTrace.RepairIpv4Checksums(toRelay);
                        var secure = _secure ?? throw new IOException("KRot transport is unavailable");
                        try
                        {
                            await secure.WriteFrameAsync(NvpFrameType.Packet, toRelay, _cts.Token);
                        }
                        catch (Exception ex) when (!_cts.IsCancellationRequested)
                        {
                            Log("Uplink transport lost: " + ex.Message);
                            if (!await ResumeTransportAsync(secure, _cts.Token))
                                throw;
                            await (_secure ?? throw new IOException("KRot resume did not attach a transport")).WriteFrameAsync(NvpFrameType.Packet, toRelay, _cts.Token);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log("Uplink failed: " + ex.Message);
                Stop();
            }
        }, _cts.Token);

        _downlink = Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var secure = _secure ?? throw new IOException("KRot transport is unavailable");
                    NvpFrame frame;
                    try
                    {
                        frame = await secure.ReadFrameAsync(_cts.Token);
                    }
                    catch (Exception ex) when (!_cts.IsCancellationRequested)
                    {
                        Log("Downlink transport lost: " + ex.Message);
                        if (await ResumeTransportAsync(secure, _cts.Token))
                            continue;
                        throw;
                    }
                    if (frame.Type == NvpFrameType.Packet)
                    {
                        var count = Interlocked.Increment(ref _relayToTunPackets);
                        Interlocked.Add(ref _relayToTunBytes, frame.Payload.Length);
                        if (NvpPacketTrace.ShouldLog(count))
                            Log($"relay->tun #{count} {frame.Payload.Length}b {NvpPacketTrace.Describe(frame.Payload.Span)}");
                        var toTun = frame.Payload.ToArray();
                        NvpPacketTrace.RepairIpv4Checksums(toTun);
                        _tun.WritePacket(toTun);
                    }
                    else if (frame.Type == NvpFrameType.Ping)
                        await secure.WriteFrameAsync(NvpFrameType.Pong, ReadOnlyMemory<byte>.Empty, _cts.Token);
                    else if (frame.Type == NvpFrameType.Close)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log("Downlink failed: " + ex.Message);
            }
            finally { }
        }, _cts.Token);

        _metrics = Task.Run(async () =>
        {
            var lastUp = 0L;
            var lastDown = 0L;
            var lastSample = Stopwatch.GetTimestamp();
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(250, _cts.Token).ConfigureAwait(false);
                var up = Interlocked.Read(ref _tunToRelayBytes);
                var down = Interlocked.Read(ref _relayToTunBytes);
                var now = Stopwatch.GetTimestamp();
                var elapsed = Math.Max(0.05, Stopwatch.GetElapsedTime(lastSample, now).TotalSeconds);
                var upRate = (long)(Math.Max(0, up - lastUp) / elapsed);
                var downRate = (long)(Math.Max(0, down - lastDown) / elapsed);
                Log($"traffic: up_bps={upRate} down_bps={downRate} up_total={up} down_total={down}");
                lastUp = up;
                lastDown = down;
                lastSample = now;
            }
        }, _cts.Token);

        Log("Triggering IPv4 data-plane probe");
        WindowsNet.ProbeIpv4Traffic();
        await WaitForDataPlaneCounterAsync(() => Interlocked.Read(ref _tunToRelayPackets), TimeSpan.FromSeconds(3), _cts.Token);

        Log("Probing public IP through tunnel");
        using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
        {
            probeCts.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var after = await NvpDiagnostics.ResolvePublicIpAsync(TimeSpan.FromSeconds(12), probeCts.Token);
                Log("Public IP after connect: " + after);
                if (_publicIpBefore is not null && string.Equals(after, _publicIpBefore, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Public IP did not change after tunnel setup; another route or VPN is still carrying traffic");
            }
            catch (Exception ex)
            {
                Log("Public IP probe after connect unavailable: " + ex.Message);
            }
        }
        if (Interlocked.Read(ref _tunToRelayPackets) == 0)
            throw new NoraAppException("NORA-KRT-3006", "No IPv4 packets were captured from Wintun after route setup and explicit ping -4 probe; full-tunnel data plane is not active");
        if (Interlocked.Read(ref _relayToTunPackets) == 0)
            throw new NoraAppException("NORA-KRT-3006", "No packets returned from relay after route setup; server egress or return path is not active");
    }

    private static async Task WaitForDataPlaneCounterAsync(Func<long> readCounter, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (readCounter() == 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(100, ct).ConfigureAwait(false);
    }

    public async Task WaitAsync()
    {
        var downlink = _downlink;
        var uplink = _uplink;
        if (downlink is null || uplink is null)
            return;
        await Task.WhenAny(downlink, uplink);
        Stop();
    }

    public void Stop()
    {
        var cts = _cts;
        if (cts is null || cts.IsCancellationRequested || Interlocked.Exchange(ref _stopping, 1) != 0)
            return;

        Log("Disconnecting");
        _ = _secure?.WriteFrameAsync(NvpFrameType.Close, ReadOnlyMemory<byte>.Empty, CancellationToken.None).ContinueWith(_ => { });
        cts.Cancel();
        if (_server is not null && OperatingSystem.IsWindows())
            WindowsNet.RestoreClient(config, _server.Address, _tun?.InterfaceIndex);
        Log($"Data-plane counters: tun->relay={_tunToRelayPackets}/{_tunToRelayBytes}b relay->tun={_relayToTunPackets}/{_relayToTunBytes}b");
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_uplink is not null)
            await _uplink.ContinueWith(_ => { });
        if (_downlink is not null)
            await _downlink.ContinueWith(_ => { });
        if (_metrics is not null)
            await _metrics.ContinueWith(_ => { });
        _stream?.Dispose();
        _tcp?.Dispose();
        _tun?.Dispose();
        _cts?.Dispose();
        _resumeGate.Dispose();
        Log("Disconnected");
    }

    private async Task<NvpSecureStream> OpenTransportAsync(string? resumeTicket, bool requireResume, CancellationToken ct)
    {
        var server = _server ?? throw new InvalidOperationException("KRot server is not selected");
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(server.Address, server.Port, ct);
            tcp.NoDelay = true;
            var stream = tcp.GetStream();
            Log(requireResume ? "Reconnecting TLS cover channel" : "TCP cover channel established");
            var handshake = await NvpHandshake.ConnectAsync(stream, config, server, resumeTicket);
            if (requireResume && !handshake.ResumeAccepted)
                throw new IOException("Server did not accept the resume ticket");
            var oldTcp = _tcp;
            var oldStream = _stream;
            _tcp = tcp;
            _stream = stream;
            _secure = handshake.Secure;
            _resumeTicket = string.IsNullOrWhiteSpace(handshake.ResumeTicket) ? _resumeTicket : handshake.ResumeTicket;
            oldStream?.Dispose();
            oldTcp?.Dispose();
            return handshake.Secure;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    private async Task<bool> ResumeTransportAsync(NvpSecureStream failedTransport, CancellationToken ct)
    {
        if (Volatile.Read(ref _stopping) != 0 || string.IsNullOrWhiteSpace(_resumeTicket))
            return false;
        await _resumeGate.WaitAsync(ct);
        try
        {
            if (!ReferenceEquals(_secure, failedTransport))
                return _secure is not null;
            _secure = null;
            _stream?.Dispose();
            _tcp?.Dispose();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(85);
            var attempt = 0;
            Exception? last = null;
            while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
            {
                attempt++;
                try
                {
                    await OpenTransportAsync(_resumeTicket, requireResume: true, ct);
                    Log($"KRot session resumed after transport loss (attempt {attempt})");
                    return true;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    last = ex;
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(4000, 250 * attempt)), ct);
                }
            }
            Log("KRot resume window expired" + (last is null ? "" : ": " + last.Message));
            return false;
        }
        finally
        {
            _resumeGate.Release();
        }
    }

    private void Log(string message) => log?.Invoke(message);
}

internal sealed class NvpDiagnostics
{
    public string Stage { get; init; } = "";
    public bool Success { get; init; }
    public string Details { get; init; } = "";

    public static async Task<string> ResolvePublicIpAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            UseProxy = false
        };
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        using var client = new HttpClient(handler)
        {
            Timeout = timeout
        };
        using var response = await client.GetAsync("https://1.1.1.1/cdn-cgi/trace", ct);
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("Empty public IP response");
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("ip=", StringComparison.OrdinalIgnoreCase))
                return line[3..].Trim();
        }
        return body;
    }

    public static async Task<NvpDiagnostics> ProbeAsync(NvpConfig config, CancellationToken ct)
    {
        var lint = NvpProfileLinter.Check(config);
        if (lint.Any(line => line.StartsWith("ERROR ", StringComparison.Ordinal)))
            return new NvpDiagnostics { Stage = "profile_lint", Success = false, Details = string.Join("; ", lint) };
        var server = config.Servers[0];
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(server.Address, server.Port, ct);
            await using var stream = tcp.GetStream();
            _ = await NvpHandshake.ConnectAsync(stream, config, server);
            return new NvpDiagnostics { Stage = "relay_negotiation", Success = true, Details = "Server accepted KRot session" };
        }
        catch (SocketException ex)
        {
            return new NvpDiagnostics { Stage = "tcp_connect", Success = false, Details = ex.Message };
        }
        catch (CryptographicException ex)
        {
            return new NvpDiagnostics { Stage = "handshake_crypto", Success = false, Details = ex.Message };
        }
        catch (Exception ex)
        {
            return new NvpDiagnostics { Stage = "relay_negotiation", Success = false, Details = ex.GetBaseException().Message };
        }
    }

    public static async Task<NvpDiagnostics> ProbeResumeAsync(NvpConfig config, CancellationToken ct)
    {
        var lint = NvpProfileLinter.Check(config);
        if (lint.Any(line => line.StartsWith("ERROR ", StringComparison.Ordinal)))
            return new NvpDiagnostics { Stage = "profile_lint", Success = false, Details = string.Join("; ", lint) };
        var server = config.Servers[0];
        try
        {
            string ticket;
            using (var firstTcp = new TcpClient())
            {
                await firstTcp.ConnectAsync(server.Address, server.Port, ct);
                await using var firstStream = firstTcp.GetStream();
                var first = await NvpHandshake.ConnectAsync(firstStream, config, server);
                ticket = first.ResumeTicket;
                if (string.IsNullOrWhiteSpace(ticket))
                    return new NvpDiagnostics { Stage = "resume_ticket", Success = false, Details = "Server did not issue a session_resume_v1 ticket" };
            }

            await Task.Delay(250, ct);
            using var secondTcp = new TcpClient();
            await secondTcp.ConnectAsync(server.Address, server.Port, ct);
            await using var secondStream = secondTcp.GetStream();
            var second = await NvpHandshake.ConnectAsync(secondStream, config, server, ticket);
            return new NvpDiagnostics
            {
                Stage = "session_resume_v1",
                Success = second.ResumeAccepted,
                Details = second.ResumeAccepted ? "Server accepted fresh transport for the retained logical session" : "Server completed handshake but rejected resume ticket"
            };
        }
        catch (Exception ex)
        {
            return new NvpDiagnostics { Stage = "session_resume_v1", Success = false, Details = ex.Message };
        }
    }

    public static async Task<NvpDiagnostics> ProbeSecurityAsync(NvpConfig config, CancellationToken ct)
    {
        var lint = NvpProfileLinter.Check(config);
        if (lint.Any(line => line.StartsWith("ERROR ", StringComparison.Ordinal)))
            return new NvpDiagnostics { Stage = "profile_lint", Success = false, Details = string.Join("; ", lint) };
        var server = config.Servers[0];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var lastPing = -1;
        NvpSecureStream? secureForFailure = null;
        TcpClient? tcp = null;
        try
        {
            tcp = new TcpClient();
            await tcp.ConnectAsync(server.Address, server.Port, timeout.Token);
            var stream = tcp.GetStream();
            var handshake = await NvpHandshake.ConnectAsync(stream, config, server);
            var secure = handshake.Secure;
            secureForFailure = secure;
            if (!secure.KeyUpdateEnabled)
                return new NvpDiagnostics { Stage = "key_update_v1", Success = false, Details = "Server did not negotiate key_update_v1" };
            if (!secure.PaddingEnabled)
                return new NvpDiagnostics { Stage = "shape_padding_v1", Success = false, Details = "Server did not negotiate shape_padding_v1" };

            for (var i = 0; i < 450; i++)
            {
                lastPing = i;
                var payload = new byte[1 + (i % 31)];
                RandomNumberGenerator.Fill(payload);
                await secure.WriteFrameAsync(NvpFrameType.Ping, payload, timeout.Token);
                while (true)
                {
                    var reply = await secure.ReadFrameAsync(timeout.Token);
                    if (reply.Type == NvpFrameType.Pong)
                        break;
                    if (reply.Type == NvpFrameType.Ping)
                    {
                        await secure.WriteFrameAsync(NvpFrameType.Pong, ReadOnlyMemory<byte>.Empty, timeout.Token);
                        continue;
                    }
                    return new NvpDiagnostics { Stage = "key_update_v1", Success = false, Details = "Expected PONG, got " + reply.Type };
                }
            }

            var ok = secure.SendEpoch > 0 && secure.RecvEpoch > 0;
            var result = new NvpDiagnostics
            {
                Stage = "security_contour",
                Success = ok,
                Details = ok
                    ? $"key_update_v1 and shape_padding_v1 negotiated; epochs c2s={secure.SendEpoch}, s2c={secure.RecvEpoch}"
                    : $"No key epoch rollover observed; epochs c2s={secure.SendEpoch}, s2c={secure.RecvEpoch}"
            };
            await TrySendDiagCloseAsync(secure);
            return result;
        }
        catch (OperationCanceledException ex)
        {
            var epochs = secureForFailure is null ? "no secure stream" : $"epochs c2s={secureForFailure.SendEpoch}, s2c={secureForFailure.RecvEpoch}";
            return new NvpDiagnostics { Stage = "security_contour", Success = false, Details = $"Timed out at ping {lastPing}; {epochs}; {ex.Message}" };
        }
        catch (Exception ex)
        {
            var epochs = secureForFailure is null ? "no secure stream" : $"epochs c2s={secureForFailure.SendEpoch}, s2c={secureForFailure.RecvEpoch}";
            return new NvpDiagnostics { Stage = "security_contour", Success = false, Details = $"Failed at ping {lastPing}; {epochs}; {ex.Message}" };
        }
        finally
        {
            AbortTcp(tcp);
        }
    }

    private static async Task TrySendDiagCloseAsync(NvpSecureStream secure)
    {
        try
        {
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await secure.WriteFrameAsync(NvpFrameType.Close, ReadOnlyMemory<byte>.Empty, closeTimeout.Token);
        }
        catch
        {
            // Diagnostics must not hang on graceful TLS/socket shutdown.
        }
    }

    private static void AbortTcp(TcpClient? tcp)
    {
        if (tcp is null)
            return;
        try
        {
            tcp.Client.LingerState = new LingerOption(true, 0);
        }
        catch
        {
        }
        try
        {
            tcp.Close();
        }
        catch
        {
        }
    }

    public static async Task<NvpDiagnostics> ProbeCoverAsync(NvpConfig config, CancellationToken ct)
    {
        var lint = NvpProfileLinter.Check(config);
        if (lint.Any(line => line.StartsWith("ERROR ", StringComparison.Ordinal)))
            return new NvpDiagnostics { Stage = "profile_lint", Success = false, Details = string.Join("; ", lint) };
        var server = config.Servers[0];
        var scheme = NvpTransports.IsTls(config) ? "https" : "http";
        var port = (scheme == "https" && server.Port == 443) || (scheme == "http" && server.Port == 80)
            ? ""
            : ":" + server.Port.ToString(CultureInfo.InvariantCulture);
        var baseUri = new Uri($"{scheme}://{server.Address}{port}");
        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
            var root = await SendCoverRequestAsync(client, HttpMethod.Get, new Uri(baseUri, "/"), null, ct);
            if (root.StatusCode != HttpStatusCode.OK || !root.ContentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) || !root.Body.Contains("NORA Edge", StringComparison.Ordinal))
                return new NvpDiagnostics { Stage = "cover_get_root", Success = false, Details = $"{(int)root.StatusCode} {root.ContentType}" };
            var css = await SendCoverRequestAsync(client, HttpMethod.Get, new Uri(baseUri, "/assets/site.css"), null, ct);
            if (css.StatusCode != HttpStatusCode.OK || !css.ContentType.Contains("text/css", StringComparison.OrdinalIgnoreCase))
                return new NvpDiagnostics { Stage = "cover_get_asset", Success = false, Details = $"{(int)css.StatusCode} {css.ContentType}" };
            var head = await SendCoverRequestAsync(client, HttpMethod.Head, new Uri(baseUri, "/status"), null, ct);
            if (head.StatusCode != HttpStatusCode.OK || !head.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return new NvpDiagnostics { Stage = "cover_head_status", Success = false, Details = $"{(int)head.StatusCode} {head.ContentType}" };
            var postBody = new byte[96];
            RandomNumberGenerator.Fill(postBody);
            var post = await SendCoverRequestAsync(client, HttpMethod.Post, new Uri(baseUri, "/api/v1/collect/probe"), postBody, ct);
            if (post.StatusCode != HttpStatusCode.OK || !post.ContentType.Contains("application/octet-stream", StringComparison.OrdinalIgnoreCase) || post.BodyBytes < 192)
                return new NvpDiagnostics { Stage = "cover_invalid_post", Success = false, Details = $"{(int)post.StatusCode} {post.ContentType} {post.BodyBytes}b" };
            return new NvpDiagnostics { Stage = "cover_service", Success = true, Details = $"GET/HEAD/POST cover responses OK at {baseUri}" };
        }
        catch (Exception ex)
        {
            return new NvpDiagnostics { Stage = "cover_service", Success = false, Details = ex.Message };
        }
    }

    public static async Task<NvpDiagnostics> ProbeGarbageAsync(NvpConfig config, CancellationToken ct)
    {
        var lint = NvpProfileLinter.Check(config);
        if (lint.Any(line => line.StartsWith("ERROR ", StringComparison.Ordinal)))
            return new NvpDiagnostics { Stage = "profile_lint", Success = false, Details = string.Join("; ", lint) };
        var server = config.Servers[0];
        var sw = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(server.Address, server.Port, ct);
            Stream stream = tcp.GetStream();
            if (NvpTransports.IsTls(config))
            {
                var targetHost = string.IsNullOrWhiteSpace(server.TlsName) ? server.Address : server.TlsName;
                var tls = new SslStream(stream, leaveInnerStreamOpen: false);
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = targetHost,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    ApplicationProtocols = [SslApplicationProtocol.Http11]
                }, ct);
                stream = tls;
            }

            var garbage = new byte[96];
            RandomNumberGenerator.Fill(garbage);
            await stream.WriteAsync(garbage, ct);
            await stream.FlushAsync(ct);

            var one = new byte[1];
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var read = await stream.ReadAsync(one, readTimeout.Token);
                sw.Stop();
                if (read == 0)
                    return new NvpDiagnostics { Stage = "cover_garbage_probe", Success = true, Details = $"non-HTTP bytes after TLS were closed without cover response after {sw.ElapsedMilliseconds} ms" };
                return new NvpDiagnostics { Stage = "cover_garbage_probe", Success = false, Details = $"server returned application data after non-HTTP garbage after {sw.ElapsedMilliseconds} ms; first_byte=0x{one[0]:x2}" };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                sw.Stop();
                return new NvpDiagnostics { Stage = "cover_garbage_probe", Success = true, Details = $"non-HTTP bytes after TLS produced no application response for {sw.ElapsedMilliseconds} ms" };
            }
            catch (IOException ex)
            {
                sw.Stop();
                return new NvpDiagnostics { Stage = "cover_garbage_probe", Success = true, Details = $"non-HTTP bytes after TLS closed with IO error after {sw.ElapsedMilliseconds} ms: {ex.GetBaseException().Message}" };
            }
        }
        catch (Exception ex)
        {
            return new NvpDiagnostics { Stage = "cover_garbage_probe", Success = false, Details = ex.GetBaseException().Message };
        }
    }

    private static async Task<CoverProbeResult> SendCoverRequestAsync(HttpClient client, HttpMethod method, Uri uri, byte[]? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.TryAddWithoutValidation("Accept", method == HttpMethod.Post ? "application/octet-stream,*/*;q=0.8" : "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        }
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        var bytes = method == HttpMethod.Head ? [] : await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
        var text = bytes.Length == 0 ? "" : Encoding.UTF8.GetString(bytes);
        return new CoverProbeResult(response.StatusCode, contentType, text, bytes.Length);
    }

    private sealed record CoverProbeResult(HttpStatusCode StatusCode, string ContentType, string Body, int BodyBytes);

    public static NvpDiagnostics TunCheck(NvpConfig config)
    {
        if (!OperatingSystem.IsWindows())
            return new NvpDiagnostics { Stage = "wintun_platform", Success = false, Details = "Wintun diagnostics are Windows-only" };
        if (!NvpClient.IsAdministrator())
            return new NvpDiagnostics { Stage = "admin_or_service", Success = false, Details = "Run as Administrator or through the KRot core service" };
        try
        {
            using var tun = WintunDevice.Open(config.Tunnel.InterfaceName);
            var net = WindowsNet.SnapshotCheck(config, tun.InterfaceIndex);
            if (!net.Success)
                return net;
            return new NvpDiagnostics { Stage = "wintun_adapter", Success = true, Details = "Wintun adapter/session opened successfully; " + net.Details };
        }
        catch (Exception ex)
        {
            return new NvpDiagnostics { Stage = "wintun_adapter", Success = false, Details = ex.Message };
        }
    }
}

internal static class NvpTransports
{
    public static bool IsTls(NvpConfig config)
        => config.Tls.Enabled || string.Equals(config.TransportProfile, "tls_http_cover_v1", StringComparison.OrdinalIgnoreCase);

    public static X509Certificate2 LoadServerCertificate(NvpConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Tls.CertificatePath) || string.IsNullOrWhiteSpace(config.Tls.PrivateKeyPath))
            throw new InvalidOperationException("TLS certificate_path and private_key_path are required on the server");
        var cert = X509Certificate2.CreateFromPemFile(config.Tls.CertificatePath, config.Tls.PrivateKeyPath);
        return new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
    }
}

internal static class NvpHandshake
{
    public static async Task<NvpClientHandshake> ConnectAsync(Stream stream, NvpConfig config, NvpServerEntry server, string? resumeTicket = null)
    {
        if (NvpTransports.IsTls(config))
        {
            var targetHost = string.IsNullOrWhiteSpace(server.TlsName) ? server.Address : server.TlsName;
            var tls = new SslStream(stream, leaveInnerStreamOpen: false);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ApplicationProtocols = [SslApplicationProtocol.Http11]
            });
            stream = tls;
        }

        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var clientPublic = ecdh.ExportSubjectPublicKeyInfo();
        var clientNonce = RandomBytes(16);
        var hello = BuildClientHello(config, clientNonce, clientPublic, resumeTicket);

        await NvpCoverPrelude.WriteClientBootstrapAsync(stream, config, server, hello, CancellationToken.None);

        await ReadHttpResponseAsync(stream);
        var parsed = ParseServerHello(await ReadServerHelloBytesAsync(stream, CancellationToken.None), config, clientNonce);

        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(parsed.PublicKey, out _);
        var shared = ecdh.DeriveKeyMaterial(peer.PublicKey);
        var resume = ParseServerResumeCapabilities(parsed.Capabilities);
        var secureOptions = ParseSecureOptions(parsed.Capabilities);
        return new NvpClientHandshake(
            NvpSecureStream.CreateClient(stream, shared, config.CredentialKey, clientNonce, parsed.Nonce, secureOptions),
            resume.Ticket,
            resume.Accepted);
    }

    public static async Task<NvpHandshakeOutcome> AcceptAsync(Stream stream, NvpConfig config, Func<NvpHandshakeRequest, byte[]>? serverCapabilities = null)
    {
        var request = await ReadHttpRequestAsync(stream);
        if (request.Body.Length == 0)
            return NvpHandshakeOutcome.Cover(request);

        ParsedHello parsed;
        NvpCredential credential;
        try
        {
            parsed = ParseClientHello(request.Body, config, out credential);
        }
        catch
        {
            return NvpHandshakeOutcome.Cover(request);
        }

        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(parsed.PublicKey, out _);
        var shared = ecdh.DeriveKeyMaterial(peer.PublicKey);

        var capabilityBytes = serverCapabilities?.Invoke(new NvpHandshakeRequest(credential, parsed.Capabilities)) ?? BuildCapabilityJson(config);
        var serverPublic = ecdh.ExportSubjectPublicKeyInfo();
        var serverNonce = RandomBytes(16);
        var hello = BuildServerHello(credential.Key, parsed.Nonce, serverNonce, serverPublic, capabilityBytes);
        var response = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nCache-Control: no-cache\r\nContent-Length: " + hello.Length + "\r\nConnection: keep-alive\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
        await stream.WriteAsync(hello);
        await stream.FlushAsync();

        var secureOptions = ParseSecureOptions(capabilityBytes);
        return NvpHandshakeOutcome.ForSession(new NvpAcceptedSession(
            NvpSecureStream.CreateServer(stream, shared, credential.Key, parsed.Nonce, serverNonce, secureOptions),
            credential,
            parsed.Capabilities));
    }

    private static byte[] BuildClientHello(NvpConfig config, byte[] nonce, byte[] publicKey, string? resumeTicket)
    {
        var capabilities = BuildCapabilityJson(config, resumeTicket);
        using var ms = new MemoryStream();
        ms.Write(nonce);
        ms.Write(CredentialTag(config.CredentialId, config.CredentialKey, nonce));
        Span<byte> u16 = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(u16, (ushort)publicKey.Length);
        ms.Write(u16);
        ms.Write(publicKey);
        BinaryPrimitives.WriteUInt16BigEndian(u16, (ushort)capabilities.Length);
        ms.Write(u16);
        ms.Write(capabilities);
        var prefix = ms.ToArray();
        ms.Write(Hmac(config.CredentialKey, WithLabel("nvp1 client hello", prefix)));
        return ms.ToArray();
    }

    private static byte[] BuildServerHello(string credentialKey, byte[] clientNonce, byte[] serverNonce, byte[] publicKey, byte[] capabilities)
    {
        using var ms = new MemoryStream();
        ms.Write(serverNonce);
        Span<byte> u16 = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(u16, (ushort)publicKey.Length);
        ms.Write(u16);
        ms.Write(publicKey);
        BinaryPrimitives.WriteUInt16BigEndian(u16, (ushort)capabilities.Length);
        ms.Write(u16);
        ms.Write(capabilities);
        var prefix = ms.ToArray();
        ms.Write(Hmac(credentialKey, WithLabel("nvp1 server hello", clientNonce, prefix)));
        return ms.ToArray();
    }

    private static ParsedHello ParseClientHello(byte[] data, NvpConfig config, out NvpCredential credential)
    {
        if (data.Length < 16 + 16 + 2 + 2 + 32)
            throw new InvalidOperationException("Bad client hello");
        var offset = 0;
        var nonce = data.AsSpan(offset, 16).ToArray();
        offset += 16;
        var tag = data.AsSpan(offset, 16).ToArray();
        offset += 16;
        var publicKey = ReadVariable(data, ref offset, 4096, "client public key");
        var capabilities = ReadVariable(data, ref offset, 8192, "client capabilities");
        if (offset + 32 != data.Length)
            throw new InvalidOperationException("Bad client hello length");
        var mac = data.AsSpan(offset, 32).ToArray();
        var macData = WithLabel("nvp1 client hello", data.AsSpan(0, data.Length - 32).ToArray());
        foreach (var candidate in config.ActiveCredentials())
        {
            if (!CryptographicOperations.FixedTimeEquals(tag, CredentialTag(candidate.Id, candidate.Key, nonce)))
                continue;
            VerifyMac(macData, mac, candidate.Key);
            ValidateClientBootstrapFreshness(config, candidate, nonce, tag, mac, capabilities);
            credential = candidate;
            return new ParsedHello(nonce, publicKey, mac, capabilities);
        }
        throw new InvalidOperationException("Bad credential tag");
    }

    private static ParsedHello ParseServerHello(byte[] data, NvpConfig config, byte[] clientNonce)
    {
        if (data.Length < 16 + 2 + 2 + 32)
            throw new InvalidOperationException("Bad server hello");
        var offset = 0;
        var nonce = data.AsSpan(offset, 16).ToArray();
        offset += 16;
        var publicKey = ReadVariable(data, ref offset, 4096, "server public key");
        var capabilities = ReadVariable(data, ref offset, 8192, "server capabilities");
        if (offset + 32 != data.Length)
            throw new InvalidOperationException("Bad server hello length");
        var mac = data.AsSpan(offset, 32).ToArray();
        VerifyMac(WithLabel("nvp1 server hello", clientNonce, data.AsSpan(0, data.Length - 32).ToArray()), mac, config.CredentialKey);
        return new ParsedHello(nonce, publicKey, mac, capabilities);
    }

    private static byte[] ReadVariable(byte[] data, ref int offset, int maxLength, string label)
    {
        if (offset + 2 > data.Length)
            throw new InvalidOperationException("Missing " + label + " length");
        var len = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
        offset += 2;
        if (len > maxLength || offset + len > data.Length)
            throw new InvalidOperationException("Bad " + label + " length");
        var value = data.AsSpan(offset, len).ToArray();
        offset += len;
        return value;
    }

    private static void VerifyMac(byte[] data, byte[] mac, string credentialKey)
    {
        var expected = Hmac(credentialKey, data);
        if (!CryptographicOperations.FixedTimeEquals(expected, mac))
            throw new InvalidOperationException("Bad hello MAC");
    }

    private static byte[] Hmac(string configKey, byte[] data)
    {
        using var h = new HMACSHA256(Convert.FromBase64String(configKey));
        return h.ComputeHash(data);
    }

    private static byte[] CredentialTag(string credentialId, string credentialKey, byte[] nonce)
        => Hmac(credentialKey, WithLabel("nvp1 credential tag", Encoding.UTF8.GetBytes(credentialId), nonce)).AsSpan(0, 16).ToArray();

    public static bool SupportsSessionResume(ReadOnlySpan<byte> capabilities)
    {
        try
        {
            using var doc = JsonDocument.Parse(capabilities.ToArray());
            return doc.RootElement.TryGetProperty("session_resume_v1", out var supported) && supported.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    public static string ReadResumeTicket(ReadOnlySpan<byte> capabilities)
    {
        try
        {
            using var doc = JsonDocument.Parse(capabilities.ToArray());
            return doc.RootElement.TryGetProperty("resume_ticket", out var ticket) && ticket.ValueKind == JsonValueKind.String
                ? ticket.GetString() ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    public static byte[] BuildServerCapabilities(NvpConfig config, ReadOnlySpan<byte> clientCapabilities, string? ticket, bool resumeAccepted)
    {
        var clientKeyUpdate = ReadBoolCapability(clientCapabilities, "key_update_v1");
        var clientPadding = ReadBoolCapability(clientCapabilities, "shape_padding_v1");
        var keyUpdateEnabled = clientKeyUpdate && config.Security.KeyUpdateEnabled;
        var paddingEnabled = clientPadding && config.Shaping.FramePaddingEnabled;
        var json = JsonSerializer.Serialize(new
        {
            transport_profile = config.TransportProfile,
            relay = new[] { "packet_v1" },
            commands = new[] { "packet", "ping", "pong", "close", "resume", "key_update" },
            compliance = config.CoverProfiles.FirstOrDefault()?.Compliance ?? "NVP-1D",
            session_resume_v1 = !string.IsNullOrWhiteSpace(ticket),
            resume_ticket = ticket ?? "",
            resume_accepted = resumeAccepted,
            resume_grace_seconds = 90,
            key_update_v1 = keyUpdateEnabled,
            key_update_min_packets = config.Security.KeyUpdateMinPackets,
            key_update_max_packets = config.Security.KeyUpdateMaxPackets,
            key_update_min_bytes = config.Security.KeyUpdateMinBytes,
            key_update_max_bytes = config.Security.KeyUpdateMaxBytes,
            key_update_min_seconds = config.Security.KeyUpdateMinSeconds,
            key_update_max_seconds = config.Security.KeyUpdateMaxSeconds,
            shape_padding_v1 = paddingEnabled,
            frame_padding_min_bytes = config.Shaping.FramePaddingMinBytes,
            frame_padding_max_bytes = config.Shaping.FramePaddingMaxBytes
        }, NvpConfig.JsonOptions());
        return Encoding.UTF8.GetBytes(json);
    }

    private static byte[] BuildCapabilityJson(NvpConfig config, string? resumeTicket = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            transport_profile = config.TransportProfile,
            relay = new[] { "packet_v1" },
            commands = new[] { "packet", "ping", "pong", "close", "resume", "key_update" },
            compliance = config.CoverProfiles.FirstOrDefault()?.Compliance ?? "NVP-1D",
            session_resume_v1 = true,
            resume_ticket = resumeTicket ?? "",
            bootstrap_ts_unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            key_update_v1 = config.Security.KeyUpdateEnabled,
            shape_padding_v1 = config.Shaping.FramePaddingEnabled
        }, NvpConfig.JsonOptions());
        return Encoding.UTF8.GetBytes(json);
    }

    public static NvpSecureOptions ParseSecureOptions(ReadOnlySpan<byte> capabilities)
    {
        try
        {
            using var doc = JsonDocument.Parse(capabilities.ToArray());
            var root = doc.RootElement;
            var keyUpdate = ReadBool(root, "key_update_v1");
            var padding = ReadBool(root, "shape_padding_v1");
            return new NvpSecureOptions(
                keyUpdate,
                padding,
                ReadInt(root, "frame_padding_min_bytes", 0, 0, 512),
                ReadInt(root, "frame_padding_max_bytes", 0, 0, 512),
                ReadInt(root, "key_update_min_packets", 128, 8, 1_000_000),
                ReadInt(root, "key_update_max_packets", 384, 8, 1_000_000),
                ReadLong(root, "key_update_min_bytes", 256 * 1024, 4096, 1L << 34),
                ReadLong(root, "key_update_max_bytes", 2 * 1024 * 1024, 4096, 1L << 34),
                TimeSpan.FromSeconds(ReadInt(root, "key_update_min_seconds", 180, 30, 86400)),
                TimeSpan.FromSeconds(ReadInt(root, "key_update_max_seconds", 600, 30, 86400))).Normalize();
        }
        catch
        {
            return NvpSecureOptions.Disabled;
        }
    }

    private static bool ReadBoolCapability(ReadOnlySpan<byte> capabilities, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(capabilities.ToArray());
            return ReadBool(doc.RootElement, name);
        }
        catch
        {
            return false;
        }
    }

    private static bool ReadBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int ReadInt(JsonElement root, string name, int fallback, int min, int max)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var parsed))
            return fallback;
        return Math.Clamp(parsed, min, max);
    }

    private static long ReadLong(JsonElement root, string name, long fallback, long min, long max)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt64(out var parsed))
            return fallback;
        return Math.Clamp(parsed, min, max);
    }

    private static void ValidateClientBootstrapFreshness(NvpConfig config, NvpCredential credential, byte[] nonce, byte[] tag, byte[] mac, byte[] capabilities)
    {
        if (config.Security.BootstrapTimestampRequired)
        {
            long timestamp;
            try
            {
                using var doc = JsonDocument.Parse(capabilities);
                if (!doc.RootElement.TryGetProperty("bootstrap_ts_unix", out var ts) || !ts.TryGetInt64(out timestamp))
                    throw new InvalidOperationException("Missing bootstrap timestamp");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Bad bootstrap capabilities JSON", ex);
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var skew = Math.Clamp(config.Security.BootstrapTimestampSkewSeconds, 30, 3600);
            if (timestamp < now - skew || timestamp > now + skew)
                throw new InvalidOperationException("Expired bootstrap timestamp");
        }

        var ttl = TimeSpan.FromSeconds(Math.Clamp(config.Security.BootstrapReplayCacheSeconds, 60, 3600));
        var replayKey = Convert.ToHexString(SHA256.HashData(WithLabel(
            "nvp1 bootstrap replay",
            Encoding.UTF8.GetBytes(credential.Id),
            nonce,
            tag,
            mac)));
        if (!NvpBootstrapReplayCache.TryAccept(replayKey, ttl))
            throw new InvalidOperationException("Replay bootstrap rejected");
    }

    private static (string Ticket, bool Accepted) ParseServerResumeCapabilities(ReadOnlySpan<byte> capabilities)
    {
        try
        {
            using var doc = JsonDocument.Parse(capabilities.ToArray());
            var ticket = doc.RootElement.TryGetProperty("resume_ticket", out var ticketProperty) && ticketProperty.ValueKind == JsonValueKind.String
                ? ticketProperty.GetString() ?? ""
                : "";
            var accepted = doc.RootElement.TryGetProperty("resume_accepted", out var acceptedProperty) && acceptedProperty.ValueKind == JsonValueKind.True;
            return (ticket, accepted);
        }
        catch
        {
            return ("", false);
        }
    }

    private static byte[] WithLabel(string label, params byte[][] parts)
    {
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(label));
        ms.WriteByte(0);
        foreach (var part in parts)
            ms.Write(part);
        return ms.ToArray();
    }

    private static async Task<NvpHttpRequest> ReadHttpRequestAsync(Stream stream)
    {
        var headerBytes = new List<byte>(4096);
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one) != 1)
                throw new EndOfStreamException();
            headerBytes.Add(one[0]);
            var n = headerBytes.Count;
            if (n >= 4 && headerBytes[n - 4] == '\r' && headerBytes[n - 3] == '\n' && headerBytes[n - 2] == '\r' && headerBytes[n - 1] == '\n')
                break;
            if (n > 16384)
                throw new InvalidOperationException("HTTP header too large");
        }

        var header = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = header.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines.Length == 0 ? "" : lines[0];
        var requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var method = requestParts.Length > 0 ? requestParts[0].Trim().ToUpperInvariant() : "GET";
        var path = requestParts.Length > 1 ? requestParts[1].Trim() : "/";
        if (string.IsNullOrWhiteSpace(path))
            path = "/";
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (!headers.ContainsKey(name))
                headers[name] = value;
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out contentLength))
                    throw new InvalidOperationException("Bad Content-Length");
            }
        }
        if (contentLength is < 0 or > 65535)
            throw new InvalidOperationException("HTTP body too large");
        var body = contentLength == 0 ? [] : await ReadExactAsync(stream, contentLength, CancellationToken.None);
        return new NvpHttpRequest(method, path, header, headers, body);
    }

    private static async Task ReadHttpResponseAsync(Stream stream)
    {
        var bytes = new List<byte>(512);
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one) != 1)
                throw new EndOfStreamException();
            bytes.Add(one[0]);
            var n = bytes.Count;
            if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n')
                return;
            if (n > 8192)
                throw new InvalidOperationException("HTTP response too large");
        }
    }

    private static async Task<byte[]> ReadServerHelloBytesAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var nonce = await ReadExactAsync(stream, 16, ct);
        ms.Write(nonce);
        var publicKey = await ReadVariableAsync(stream, 4096, "server public key", ct);
        ms.Write(ToU16(publicKey.Length));
        ms.Write(publicKey);
        var capabilities = await ReadVariableAsync(stream, 8192, "server capabilities", ct);
        ms.Write(ToU16(capabilities.Length));
        ms.Write(capabilities);
        var mac = await ReadExactAsync(stream, 32, ct);
        ms.Write(mac);
        return ms.ToArray();
    }

    private static async Task<byte[]> ReadVariableAsync(Stream stream, int maxLength, string label, CancellationToken ct)
    {
        var lenBytes = await ReadExactAsync(stream, 2, ct);
        var len = BinaryPrimitives.ReadUInt16BigEndian(lenBytes);
        if (len > maxLength)
            throw new InvalidOperationException(label + " is too large");
        return await ReadExactAsync(stream, len, ct);
    }

    private static byte[] ToU16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int len, CancellationToken ct)
    {
        var buf = new byte[len];
        var offset = 0;
        while (offset < len)
        {
            var read = await stream.ReadAsync(buf.AsMemory(offset, len - offset), ct);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
        return buf;
    }

    private static byte[] RandomBytes(int len)
    {
        var bytes = new byte[len];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private sealed record ParsedHello(byte[] Nonce, byte[] PublicKey, byte[] Mac, byte[] Capabilities);
}

internal sealed record NvpHandshakeRequest(NvpCredential Credential, byte[] ClientCapabilities);
internal sealed record NvpHandshakeOutcome(NvpAcceptedSession? Accepted, NvpHttpRequest CoverRequest)
{
    public static NvpHandshakeOutcome ForSession(NvpAcceptedSession session) => new(session, NvpHttpRequest.Empty);
    public static NvpHandshakeOutcome Cover(NvpHttpRequest request) => new(null, request);
}

internal sealed record NvpHttpRequest(string Method, string Path, string Header, IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    public static readonly NvpHttpRequest Empty = new("GET", "/", "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);

    public string PathOnly
    {
        get
        {
            var path = string.IsNullOrWhiteSpace(Path) ? "/" : Path;
            var query = path.IndexOf('?');
            return query >= 0 ? path[..query] : path;
        }
    }
}

internal sealed record NvpAcceptedSession(NvpSecureStream Secure, NvpCredential Credential, byte[] ClientCapabilities);
internal sealed record NvpClientHandshake(NvpSecureStream Secure, string ResumeTicket, bool ResumeAccepted);

internal static class NvpBootstrapReplayCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTimeOffset> Seen = new(StringComparer.Ordinal);
    private static DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    public static bool TryAccept(string key, TimeSpan ttl)
    {
        var now = DateTimeOffset.UtcNow;
        lock (Gate)
        {
            if (now >= _nextCleanup)
            {
                foreach (var expired in Seen.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                    Seen.Remove(expired);
                _nextCleanup = now.AddSeconds(30);
            }

            if (Seen.TryGetValue(key, out var expiresAt) && expiresAt > now)
                return false;
            Seen[key] = now.Add(ttl);
            return true;
        }
    }
}

internal static class NvpResumeTickets
{
    public static string Issue(NvpCredential credential, string sessionId, DateTimeOffset expiresAt)
    {
        var expires = expiresAt.ToUnixTimeSeconds();
        var payload = sessionId + "." + expires;
        using var hmac = new HMACSHA256(Convert.FromBase64String(credential.Key));
        var mac = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("krot resume v1\0" + credential.Id + "\0" + payload))).ToLowerInvariant();
        return Base64UrlEncode(Encoding.UTF8.GetBytes(payload + "." + mac));
    }

    public static bool TryValidate(string ticket, NvpCredential credential, out string sessionId)
    {
        sessionId = "";
        try
        {
            var padded = ticket.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('.', StringSplitOptions.None);
            if (parts.Length != 3 || parts[0].Length != 32 || !long.TryParse(parts[1], out var expires))
                return false;
            if (DateTimeOffset.FromUnixTimeSeconds(expires) <= DateTimeOffset.UtcNow)
                return false;
            var payload = parts[0] + "." + parts[1];
            using var hmac = new HMACSHA256(Convert.FromBase64String(credential.Key));
            var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("krot resume v1\0" + credential.Id + "\0" + payload))).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])))
                return false;
            sessionId = parts[0];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal enum NvpFrameType : byte
{
    Packet = 1,
    Ping = 2,
    Pong = 3,
    Close = 4,
    Resume = 5,
    KeyUpdate = 6
}

internal readonly record struct NvpFrame(NvpFrameType Type, ReadOnlyMemory<byte> Payload);

internal sealed record NvpSecureOptions(
    bool KeyUpdateEnabled,
    bool PaddingEnabled,
    int PaddingMinBytes,
    int PaddingMaxBytes,
    int KeyUpdateMinPackets,
    int KeyUpdateMaxPackets,
    long KeyUpdateMinBytes,
    long KeyUpdateMaxBytes,
    TimeSpan KeyUpdateMinAge,
    TimeSpan KeyUpdateMaxAge)
{
    public static readonly NvpSecureOptions Disabled = new(
        KeyUpdateEnabled: false,
        PaddingEnabled: false,
        PaddingMinBytes: 0,
        PaddingMaxBytes: 0,
        KeyUpdateMinPackets: 128,
        KeyUpdateMaxPackets: 384,
        KeyUpdateMinBytes: 256 * 1024,
        KeyUpdateMaxBytes: 2 * 1024 * 1024,
        KeyUpdateMinAge: TimeSpan.FromMinutes(3),
        KeyUpdateMaxAge: TimeSpan.FromMinutes(10));

    public NvpSecureOptions Normalize()
    {
        var padMin = Math.Clamp(PaddingMinBytes, 0, 512);
        var padMax = Math.Clamp(Math.Max(PaddingMaxBytes, padMin), padMin, 512);
        var minPackets = Math.Clamp(KeyUpdateMinPackets, 8, 1_000_000);
        var maxPackets = Math.Clamp(Math.Max(KeyUpdateMaxPackets, minPackets), minPackets, 1_000_000);
        var minBytes = Math.Clamp(KeyUpdateMinBytes, 4096, 1L << 34);
        var maxBytes = Math.Clamp(Math.Max(KeyUpdateMaxBytes, minBytes), minBytes, 1L << 34);
        var minAge = KeyUpdateMinAge < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : KeyUpdateMinAge;
        var maxAge = KeyUpdateMaxAge < minAge ? minAge : KeyUpdateMaxAge;
        return this with
        {
            PaddingMinBytes = padMin,
            PaddingMaxBytes = padMax,
            KeyUpdateMinPackets = minPackets,
            KeyUpdateMaxPackets = maxPackets,
            KeyUpdateMinBytes = minBytes,
            KeyUpdateMaxBytes = maxBytes,
            KeyUpdateMinAge = minAge,
            KeyUpdateMaxAge = maxAge
        };
    }
}

internal static class NvpPacketTrace
{
    public static bool ShouldLog(long count)
        => count <= 10 || count is 25 or 50 or 100 || count % 250 == 0;

    public static string Describe(ReadOnlySpan<byte> packet)
    {
        if (packet.Length == 0)
            return "empty";
        var version = packet[0] >> 4;
        if (version == 4)
            return DescribeIpv4(packet);
        if (version == 6)
            return DescribeIpv6(packet);
        return $"unknown_ip_version={version}";
    }

    public static bool IsIpv4(ReadOnlySpan<byte> packet)
        => packet.Length >= 20 && (packet[0] >> 4) == 4;

    public static bool IsRoutableIpv4(ReadOnlySpan<byte> packet)
        => IsIpv4(packet) && IsRoutableDestination(packet);

    public static bool IsRoutableIpv4From(ReadOnlySpan<byte> packet, string source)
        => IsIpv4From(packet, source) && IsRoutableDestination(packet);

    public static bool IsIpv4From(ReadOnlySpan<byte> packet, string source)
        => IsIpv4AddressMatch(packet, source, offset: 12);

    public static void RepairIpv4Checksums(Span<byte> packet)
    {
        if (!IsIpv4(packet))
            return;

        var headerLen = (packet[0] & 0x0F) * 4;
        if (headerLen < 20 || packet.Length < headerLen)
            return;

        var totalLen = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2));
        if (totalLen < headerLen || totalLen > packet.Length)
            return;

        var frag = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(6, 2));
        if ((frag & 0x3FFF) != 0)
        {
            FixIpv4HeaderChecksum(packet, headerLen);
            return;
        }

        FixIpv4HeaderChecksum(packet, headerLen);

        var transportLen = totalLen - headerLen;
        if (transportLen <= 0)
            return;

        var protocol = packet[9];
        var transport = packet.Slice(headerLen, transportLen);
        switch (protocol)
        {
            case 1: // ICMP
                if (transportLen >= 4)
                {
                    transport[2] = 0;
                    transport[3] = 0;
                    WriteChecksum(transport, 2, ComputeChecksum(transport));
                }
                break;
            case 6: // TCP
                if (transportLen >= 20)
                {
                    transport[16] = 0;
                    transport[17] = 0;
                    WriteChecksum(transport, 16, ComputeTcpUdpChecksum(packet, headerLen, transportLen));
                }
                break;
            case 17: // UDP
                if (transportLen >= 8)
                {
                    transport[6] = 0;
                    transport[7] = 0;
                    var checksum = ComputeTcpUdpChecksum(packet, headerLen, transportLen);
                    if (checksum == 0)
                        checksum = 0xFFFF;
                    WriteChecksum(transport, 6, checksum);
                }
                break;
        }
    }

    private static void FixIpv4HeaderChecksum(Span<byte> packet, int headerLen)
    {
        packet[10] = 0;
        packet[11] = 0;
        WriteChecksum(packet, 10, ComputeChecksum(packet.Slice(0, headerLen)));
    }

    private static ushort ComputeTcpUdpChecksum(ReadOnlySpan<byte> packet, int headerLen, int transportLen)
    {
        var sum = 0u;
        sum = AddWord(sum, packet, 12);
        sum = AddWord(sum, packet, 14);
        sum = AddWord(sum, packet, 16);
        sum = AddWord(sum, packet, 18);
        sum += packet[9];
        sum += (uint)transportLen;
        sum = AddSpan(sum, packet.Slice(headerLen, transportLen));
        return FoldChecksum(sum);
    }

    private static ushort ComputeChecksum(ReadOnlySpan<byte> data)
    {
        var sum = AddSpan(0u, data);
        return FoldChecksum(sum);
    }

    private static uint AddSpan(uint sum, ReadOnlySpan<byte> data)
    {
        var i = 0;
        while (i + 1 < data.Length)
        {
            sum += BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i, 2));
            i += 2;
        }
        if (i < data.Length)
            sum += (uint)(data[i] << 8);
        return sum;
    }

    private static uint AddWord(uint sum, ReadOnlySpan<byte> packet, int offset)
        => sum + BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset, 2));

    private static ushort FoldChecksum(uint sum)
    {
        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);
        return (ushort)~sum;
    }

    private static void WriteChecksum(Span<byte> packet, int offset, ushort checksum)
    {
        BinaryPrimitives.WriteUInt16BigEndian(packet.Slice(offset, 2), checksum);
    }

    public static bool IsIpv4To(ReadOnlySpan<byte> packet, string destination)
        => IsIpv4AddressMatch(packet, destination, offset: 16);

    public static bool TryGetIpv4Destination(ReadOnlySpan<byte> packet, out string destination)
    {
        destination = "";
        if (!IsIpv4(packet) || packet.Length < 20)
            return false;
        destination = new IPAddress(packet.Slice(16, 4).ToArray()).ToString();
        return true;
    }

    public static bool RewriteIpv4Source(Span<byte> packet, string source)
        => RewriteIpv4Address(packet, source, offset: 12);

    public static bool RewriteIpv4Destination(Span<byte> packet, string destination)
        => RewriteIpv4Address(packet, destination, offset: 16);

    private static bool RewriteIpv4Address(Span<byte> packet, string address, int offset)
    {
        if (!IsIpv4(packet) || packet.Length < offset + 4 || !IPAddress.TryParse(address, out var parsed))
            return false;
        var bytes = parsed.GetAddressBytes();
        if (bytes.Length != 4)
            return false;
        bytes.CopyTo(packet.Slice(offset, 4));
        return true;
    }

    private static bool IsIpv4AddressMatch(ReadOnlySpan<byte> packet, string expected, int offset)
    {
        if (!IsIpv4(packet) || packet.Length < offset + 4)
            return false;
        if (!IPAddress.TryParse(expected, out var address))
            return false;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && packet.Slice(offset, 4).SequenceEqual(bytes);
    }

    private static bool IsRoutableDestination(ReadOnlySpan<byte> packet)
    {
        if (!IsIpv4(packet) || packet.Length < 20)
            return false;
        var a = packet[16];
        var b = packet[17];
        var c = packet[18];
        var d = packet[19];
        if (a >= 224)
            return false;
        if (a == 255 && b == 255 && c == 255 && d == 255)
            return false;
        if (a == 10 && b == 66 && c == 0 && d == 255)
            return false;
        return true;
    }

    private static string DescribeIpv4(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 20)
            return "ipv4 truncated";
        var ihl = (packet[0] & 0x0f) * 4;
        if (ihl < 20 || packet.Length < ihl)
            return "ipv4 bad_ihl";
        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2));
        var proto = packet[9];
        var src = new IPAddress(packet.Slice(12, 4).ToArray());
        var dst = new IPAddress(packet.Slice(16, 4).ToArray());
        return $"IPv4 {ProtocolName(proto)} {src}->{dst} len={totalLength}";
    }

    private static string DescribeIpv6(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 40)
            return "ipv6 truncated";
        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(4, 2));
        var next = packet[6];
        var src = new IPAddress(packet.Slice(8, 16).ToArray());
        var dst = new IPAddress(packet.Slice(24, 16).ToArray());
        return $"IPv6 {ProtocolName(next)} {src}->{dst} payload={payloadLength}";
    }

    private static string ProtocolName(byte protocol) => protocol switch
    {
        1 => "ICMP",
        6 => "TCP",
        17 => "UDP",
        58 => "ICMPv6",
        _ => "proto-" + protocol
    };
}

internal sealed class NvpSecureStream
{
    private readonly Stream _stream;
    private readonly byte[] _seed;
    private readonly string _sendInfo;
    private readonly string _recvInfo;
    private readonly NvpSecureOptions _options;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private AesGcm _send;
    private AesGcm _recv;
    private ulong _sendSeq;
    private ulong _recvSeq;
    private int _sendEpoch;
    private int _recvEpoch;
    private int _sendFramesSinceKeyUpdate;
    private long _sendBytesSinceKeyUpdate;
    private int _nextKeyUpdateFrames;
    private long _nextKeyUpdateBytes;
    private DateTimeOffset _nextKeyUpdateAt;

    private NvpSecureStream(Stream stream, byte[] seed, string sendInfo, string recvInfo, NvpSecureOptions options)
    {
        _stream = stream;
        _seed = seed;
        _sendInfo = sendInfo;
        _recvInfo = recvInfo;
        _options = options.Normalize();
        _send = new AesGcm(DeriveTrafficKey(sendInfo, 0), 16);
        _recv = new AesGcm(DeriveTrafficKey(recvInfo, 0), 16);
        ScheduleNextKeyUpdate();
    }

    public int SendEpoch => Volatile.Read(ref _sendEpoch);
    public int RecvEpoch => Volatile.Read(ref _recvEpoch);
    public bool KeyUpdateEnabled => _options.KeyUpdateEnabled;
    public bool PaddingEnabled => _options.PaddingEnabled;

    public static NvpSecureStream CreateClient(Stream stream, byte[] shared, string credentialKey, byte[] clientNonce, byte[] serverNonce, NvpSecureOptions options)
    {
        var seed = BuildSeed(shared, credentialKey, clientNonce, serverNonce);
        return new NvpSecureStream(stream, seed, "nvp1 c2s", "nvp1 s2c", options);
    }

    public static NvpSecureStream CreateServer(Stream stream, byte[] shared, string credentialKey, byte[] clientNonce, byte[] serverNonce, NvpSecureOptions options)
    {
        var seed = BuildSeed(shared, credentialKey, clientNonce, serverNonce);
        return new NvpSecureStream(stream, seed, "nvp1 s2c", "nvp1 c2s", options);
    }

    public async Task WriteFrameAsync(NvpFrameType type, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length > 65535)
            throw new InvalidOperationException("Frame too large");

        await _writeLock.WaitAsync(ct);
        try
        {
            if (type != NvpFrameType.KeyUpdate && ShouldSendKeyUpdate())
                await WriteKeyUpdateLockedAsync(ct);
            await WriteFrameLockedAsync(type, payload, allowPadding: type != NvpFrameType.KeyUpdate, ct);
            if (type != NvpFrameType.KeyUpdate)
            {
                _sendFramesSinceKeyUpdate++;
                _sendBytesSinceKeyUpdate += payload.Length;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<NvpFrame> ReadFrameAsync(CancellationToken ct)
    {
        while (true)
        {
            var frame = await ReadOneFrameAsync(ct);
            if (frame.Type != NvpFrameType.KeyUpdate)
                return frame;
            if (!_options.KeyUpdateEnabled)
                throw new InvalidOperationException("Unexpected key update frame");
            if (frame.Payload.Length != 4)
                throw new InvalidOperationException("Bad key update frame");
            var nextEpoch = BinaryPrimitives.ReadInt32BigEndian(frame.Payload.Span);
            if (nextEpoch != _recvEpoch + 1)
                throw new InvalidOperationException("Unexpected key update epoch");
            AdvanceRecvEpoch(nextEpoch);
        }
    }

    private async Task<NvpFrame> ReadOneFrameAsync(CancellationToken ct)
    {
        var lenBuf = await ReadExactAsync(4, ct);
        var len = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
        var maxLen = 5 + 65535 + (_options.PaddingEnabled ? _options.PaddingMaxBytes : 0) + 16;
        if (len < 21 || len > maxLen)
            throw new InvalidOperationException("Bad encrypted record length");
        var cipher = await ReadExactAsync((int)len, ct);
        var seq = _recvSeq++;
        var tag = cipher.AsSpan(cipher.Length - 16, 16).ToArray();
        var ciphertext = cipher.AsSpan(0, cipher.Length - 16).ToArray();
        var plain = new byte[ciphertext.Length];
        _recv.Decrypt(Nonce(_recvEpoch, seq), ciphertext, tag, plain, AssociatedData(_recvEpoch, seq));
        var type = (NvpFrameType)plain[0];
        var payloadLen = BinaryPrimitives.ReadUInt32BigEndian(plain.AsSpan(1, 4));
        if (payloadLen > plain.Length - 5)
            throw new InvalidOperationException("Bad frame length");
        var paddingLen = plain.Length - 5 - (int)payloadLen;
        if (paddingLen != 0 && (!_options.PaddingEnabled || paddingLen > _options.PaddingMaxBytes))
            throw new InvalidOperationException("Unexpected frame padding");
        return new NvpFrame(type, plain.AsMemory(5, (int)payloadLen));
    }

    private async Task WriteFrameLockedAsync(NvpFrameType type, ReadOnlyMemory<byte> payload, bool allowPadding, CancellationToken ct)
    {
        var paddingLen = allowPadding ? ChoosePaddingLength(type, payload.Length) : 0;
        var plain = new byte[1 + 4 + payload.Length + paddingLen];
        plain[0] = (byte)type;
        BinaryPrimitives.WriteUInt32BigEndian(plain.AsSpan(1, 4), (uint)payload.Length);
        payload.CopyTo(plain.AsMemory(5));
        if (paddingLen > 0)
            RandomNumberGenerator.Fill(plain.AsSpan(5 + payload.Length));

        var seq = _sendSeq++;
        var nonce = Nonce(_sendEpoch, seq);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        _send.Encrypt(nonce, plain, cipher, tag, AssociatedData(_sendEpoch, seq));

        var len = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)(cipher.Length + tag.Length));
        await _stream.WriteAsync(len, ct);
        await _stream.WriteAsync(cipher, ct);
        await _stream.WriteAsync(tag, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task WriteKeyUpdateLockedAsync(CancellationToken ct)
    {
        var nextEpoch = _sendEpoch + 1;
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, nextEpoch);
        await WriteFrameLockedAsync(NvpFrameType.KeyUpdate, payload, allowPadding: false, ct);
        AdvanceSendEpoch(nextEpoch);
    }

    private bool ShouldSendKeyUpdate()
        => _options.KeyUpdateEnabled &&
           (_sendFramesSinceKeyUpdate >= _nextKeyUpdateFrames ||
            _sendBytesSinceKeyUpdate >= _nextKeyUpdateBytes ||
            DateTimeOffset.UtcNow >= _nextKeyUpdateAt);

    private int ChoosePaddingLength(NvpFrameType type, int payloadLength)
    {
        if (!_options.PaddingEnabled || _options.PaddingMaxBytes <= 0 || type == NvpFrameType.Close)
            return 0;
        var min = _options.PaddingMinBytes;
        var max = _options.PaddingMaxBytes;
        if (payloadLength <= 96)
            min = Math.Min(max, Math.Max(min, 16));
        return RandomNumberGenerator.GetInt32(min, max + 1);
    }

    private void AdvanceSendEpoch(int nextEpoch)
    {
        var old = _send;
        _send = new AesGcm(DeriveTrafficKey(_sendInfo, nextEpoch), 16);
        old.Dispose();
        _sendEpoch = nextEpoch;
        _sendSeq = 0;
        _sendFramesSinceKeyUpdate = 0;
        _sendBytesSinceKeyUpdate = 0;
        ScheduleNextKeyUpdate();
    }

    private void AdvanceRecvEpoch(int nextEpoch)
    {
        var old = _recv;
        _recv = new AesGcm(DeriveTrafficKey(_recvInfo, nextEpoch), 16);
        old.Dispose();
        _recvEpoch = nextEpoch;
        _recvSeq = 0;
    }

    private void ScheduleNextKeyUpdate()
    {
        _nextKeyUpdateFrames = RandomNumberGenerator.GetInt32(_options.KeyUpdateMinPackets, _options.KeyUpdateMaxPackets + 1);
        _nextKeyUpdateBytes = NextInt64(_options.KeyUpdateMinBytes, _options.KeyUpdateMaxBytes + 1);
        var minTicks = _options.KeyUpdateMinAge.Ticks;
        var maxTicks = _options.KeyUpdateMaxAge.Ticks;
        _nextKeyUpdateAt = DateTimeOffset.UtcNow.AddTicks(NextInt64(minTicks, maxTicks + 1));
    }

    private static long NextInt64(long minInclusive, long maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            return minInclusive;
        var range = (ulong)(maxExclusive - minInclusive);
        Span<byte> bytes = stackalloc byte[8];
        var limit = ulong.MaxValue - (ulong.MaxValue % range);
        ulong value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }
        while (value >= limit);
        return (long)(value % range) + minInclusive;
    }

    private async Task<byte[]> ReadExactAsync(int len, CancellationToken ct)
    {
        var buf = new byte[len];
        var offset = 0;
        while (offset < len)
        {
            var read = await _stream.ReadAsync(buf.AsMemory(offset, len - offset), ct);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
        return buf;
    }

    private static byte[] BuildSeed(byte[] shared, string credentialKey, byte[] clientNonce, byte[] serverNonce)
    {
        using var ms = new MemoryStream();
        ms.Write(shared);
        ms.Write(Convert.FromBase64String(credentialKey));
        ms.Write(clientNonce);
        ms.Write(serverNonce);
        return SHA256.HashData(ms.ToArray());
    }

    private byte[] DeriveTrafficKey(string info, int epoch)
        => Hkdf(_seed, $"{info} epoch {epoch}", 32);

    private static byte[] Hkdf(byte[] ikm, string info, int len)
    {
        using var extract = new HMACSHA256(new byte[32]);
        var prk = extract.ComputeHash(ikm);
        var okm = new byte[len];
        var previous = Array.Empty<byte>();
        var written = 0;
        byte counter = 1;
        while (written < len)
        {
            using var expand = new HMACSHA256(prk);
            expand.TransformBlock(previous, 0, previous.Length, null, 0);
            var infoBytes = Encoding.ASCII.GetBytes(info);
            expand.TransformBlock(infoBytes, 0, infoBytes.Length, null, 0);
            expand.TransformFinalBlock([counter], 0, 1);
            previous = expand.Hash!;
            var take = Math.Min(previous.Length, len - written);
            Buffer.BlockCopy(previous, 0, okm, written, take);
            written += take;
            counter++;
        }
        return okm;
    }

    private static byte[] Nonce(int epoch, ulong seq)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(0, 4), (uint)epoch);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), seq);
        return nonce;
    }

    private static byte[] AssociatedData(int epoch, ulong seq)
    {
        var aad = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(0, 4), (uint)epoch);
        BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(4), seq);
        return aad;
    }
}

internal static class NvpCoverPrelude
{
    private sealed record Template(string Path, string ContentType, string Accept, string CacheControl, bool AddFetchHeaders);

    private static readonly Template[] DefaultTemplates =
    [
        new("/assets/{hex16}.bin", "application/octet-stream", "*/*", "no-cache", false),
        new("/api/v1/collect/{hex12}", "application/octet-stream", "application/octet-stream,*/*;q=0.8", "no-store", true),
        new("/edge/beacon/{hex16}", "application/x-protobuf", "*/*", "no-store", true),
        new("/sync/{hex8}/events", "application/octet-stream", "*/*", "max-age=0", false),
        new("/cdn/assets/{hex12}/payload", "application/octet-stream", "*/*", "no-cache", false)
    ];

    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36"
    ];

    public static async Task WriteClientBootstrapAsync(Stream stream, NvpConfig config, NvpServerEntry server, byte[] hello, CancellationToken ct)
    {
        var template = SelectTemplate(config);
        var host = string.IsNullOrWhiteSpace(server.CoverHost)
            ? (string.IsNullOrWhiteSpace(server.TlsName) ? server.Address : server.TlsName)
            : server.CoverHost;
        var requestId = RandomHex(16);
        var header = new StringBuilder()
            .Append("POST ").Append(ExpandPath(template.Path)).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(host).Append("\r\n")
            .Append("Connection: keep-alive\r\n")
            .Append("Cache-Control: ").Append(template.CacheControl).Append("\r\n")
            .Append("User-Agent: ").Append(UserAgents[RandomNumberGenerator.GetInt32(UserAgents.Length)]).Append("\r\n")
            .Append("Accept: ").Append(template.Accept).Append("\r\n")
            .Append("Accept-Language: en-US,en;q=0.9\r\n")
            .Append("Content-Type: ").Append(template.ContentType).Append("\r\n")
            .Append("X-Request-ID: ").Append(requestId).Append("\r\n");
        if (template.AddFetchHeaders)
        {
            header
                .Append("Sec-Fetch-Site: same-origin\r\n")
                .Append("Sec-Fetch-Mode: cors\r\n")
                .Append("Sec-Fetch-Dest: empty\r\n")
                .Append("Origin: https://").Append(host).Append("\r\n");
        }
        header
            .Append("Content-Length: ").Append(hello.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
            .Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
        var writeMode = RandomNumberGenerator.GetInt32(3);
        if (writeMode == 0)
        {
            var combined = new byte[headerBytes.Length + hello.Length];
            Buffer.BlockCopy(headerBytes, 0, combined, 0, headerBytes.Length);
            Buffer.BlockCopy(hello, 0, combined, headerBytes.Length, hello.Length);
            await stream.WriteAsync(combined, ct);
        }
        else
        {
            await stream.WriteAsync(headerBytes, ct);
            if (writeMode == 2)
                await Task.Delay(RandomNumberGenerator.GetInt32(3, 18), ct);
            await stream.WriteAsync(hello, ct);
        }
        await stream.FlushAsync(ct);
    }

    private static Template SelectTemplate(NvpConfig config)
    {
        var profile = config.CoverProfiles.FirstOrDefault();
        if (profile?.BootstrapPaths is { Count: > 0 })
        {
            var path = profile.BootstrapPaths[RandomNumberGenerator.GetInt32(profile.BootstrapPaths.Count)];
            return new Template(path, "application/octet-stream", "*/*", "no-store", true);
        }
        return DefaultTemplates[RandomNumberGenerator.GetInt32(DefaultTemplates.Length)];
    }

    private static string ExpandPath(string path)
        => path
            .Replace("{hex16}", RandomHex(16), StringComparison.Ordinal)
            .Replace("{hex12}", RandomHex(12), StringComparison.Ordinal)
            .Replace("{hex8}", RandomHex(8), StringComparison.Ordinal)
            .Replace("{nonce}", RandomHex(12), StringComparison.Ordinal);

    private static string RandomHex(int chars)
    {
        var bytes = new byte[(chars + 1) / 2];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant()[..chars];
    }
}

internal static class CoverFallback
{
    public static async Task ServeAsync(Stream stream, NvpHttpRequest request, NvpConfig config)
    {
        var profile = config.CoverProfiles.FirstOrDefault();
        var minDelay = profile?.FallbackDelayMinMs ?? 80;
        var maxDelay = Math.Max(profile?.FallbackDelayMaxMs ?? 260, minDelay);
        await Task.Delay(RandomNumberGenerator.GetInt32(minDelay, maxDelay + 1));

        var method = request.Method.ToUpperInvariant();
        var path = request.PathOnly;
        CoverResponse response;
        if (method == "HEAD")
            response = BuildGetResponse(path, headOnly: true);
        else if (method == "GET")
            response = BuildGetResponse(path, headOnly: false);
        else if (method == "POST")
            response = BuildPostResponse(path, request.Body.Length);
        else
            response = new CoverResponse(405, "Method Not Allowed", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("method not allowed\n"), [("Allow", "GET, HEAD, POST")], HeadOnly: false);

        await WriteResponseAsync(stream, response);
    }

    private static CoverResponse BuildGetResponse(string path, bool headOnly)
    {
        if (path is "/" or "/index.html")
        {
            const string body = "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>NORA Edge</title><link rel=\"stylesheet\" href=\"/assets/site.css\"></head><body><main><h1>NORA Edge</h1><p>Service is online.</p><script src=\"/assets/app.js\" defer></script></main></body></html>";
            return Html(body, headOnly);
        }
        if (path is "/status" or "/health" or "/healthz")
        {
            var body = Encoding.UTF8.GetBytes("{\"status\":\"ok\",\"edge\":\"online\"}\n");
            return new CoverResponse(200, "OK", "application/json; charset=utf-8", body, [("Cache-Control", "no-store")], headOnly);
        }
        if (path == "/robots.txt")
            return new CoverResponse(200, "OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("User-agent: *\nDisallow:\n"), [("Cache-Control", "max-age=3600")], headOnly);
        if (path == "/assets/site.css")
            return new CoverResponse(200, "OK", "text/css; charset=utf-8", Encoding.UTF8.GetBytes("body{margin:0;font-family:system-ui,-apple-system,Segoe UI,sans-serif;background:#0b0d12;color:#e8edf7}main{max-width:760px;margin:12vh auto;padding:32px}p{color:#9aa7bb}\n"), [("Cache-Control", "public, max-age=86400")], headOnly);
        if (path == "/assets/app.js")
            return new CoverResponse(200, "OK", "application/javascript; charset=utf-8", Encoding.UTF8.GetBytes("(()=>{document.documentElement.dataset.ready='1';})();\n"), [("Cache-Control", "public, max-age=86400")], headOnly);
        if (path == "/favicon.ico")
            return new CoverResponse(204, "No Content", "image/x-icon", [], [("Cache-Control", "public, max-age=86400")], headOnly);
        if (path.StartsWith("/assets/", StringComparison.Ordinal) || path.StartsWith("/cdn/", StringComparison.Ordinal))
        {
            var bytes = RandomBytes(RandomNumberGenerator.GetInt32(256, 1537));
            return new CoverResponse(200, "OK", "application/octet-stream", bytes, [("Cache-Control", "public, max-age=300")], headOnly);
        }
        return new CoverResponse(404, "Not Found", "text/html; charset=utf-8", Encoding.UTF8.GetBytes("<!doctype html><title>Not Found</title><body>not found</body>"), [("Cache-Control", "no-store")], headOnly);
    }

    private static CoverResponse BuildPostResponse(string path, int bodyLength)
    {
        if (path.StartsWith("/assets/", StringComparison.Ordinal) ||
            path.StartsWith("/api/", StringComparison.Ordinal) ||
            path.StartsWith("/edge/", StringComparison.Ordinal) ||
            path.StartsWith("/sync/", StringComparison.Ordinal) ||
            path.StartsWith("/cdn/", StringComparison.Ordinal))
        {
            var size = Math.Clamp(192 + (bodyLength % 449), 192, 768);
            return new CoverResponse(200, "OK", "application/octet-stream", RandomBytes(size), [("Cache-Control", "no-store")], HeadOnly: false);
        }
        return new CoverResponse(202, "Accepted", "application/json; charset=utf-8", Encoding.UTF8.GetBytes("{\"accepted\":true}\n"), [("Cache-Control", "no-store")], HeadOnly: false);
    }

    private static CoverResponse Html(string body, bool headOnly)
        => new(200, "OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(body), [("Cache-Control", "no-store")], headOnly);

    private static async Task WriteResponseAsync(Stream stream, CoverResponse response)
    {
        var body = response.HeadOnly ? [] : response.Body;
        var header = new StringBuilder()
            .Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(response.Reason).Append("\r\n")
            .Append("Date: ").Append(DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n")
            .Append("Content-Type: ").Append(response.ContentType).Append("\r\n")
            .Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
            .Append("Connection: close\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n");
        foreach (var (name, value) in response.Headers)
            header.Append(name).Append(": ").Append(value).Append("\r\n");
        header.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()));
        if (body.Length > 0)
            await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static byte[] RandomBytes(int len)
    {
        var bytes = new byte[len];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private sealed record CoverResponse(int StatusCode, string Reason, string ContentType, byte[] Body, IReadOnlyList<(string Name, string Value)> Headers, bool HeadOnly);
}

internal sealed class LinuxTun : IDisposable
{
    private const int O_RDWR = 2;
    private const ulong TUNSETIFF = 0x400454ca;
    private const ushort IFF_TUN = 0x0001;
    private const ushort IFF_NO_PI = 0x1000;
    private readonly SafeFileHandle _handle;

    private LinuxTun(SafeFileHandle handle) => _handle = handle;

    public static LinuxTun Open(string name)
    {
        var fd = open("/dev/net/tun", O_RDWR);
        if (fd < 0)
            throw new InvalidOperationException("Cannot open /dev/net/tun. Run as root and ensure tun module is available.");

        var ifr = new byte[40];
        Encoding.ASCII.GetBytes(name.AsSpan(), ifr.AsSpan(0, Math.Min(15, name.Length)));
        BinaryPrimitives.WriteUInt16LittleEndian(ifr.AsSpan(16, 2), (ushort)(IFF_TUN | IFF_NO_PI));
        var pinned = GCHandle.Alloc(ifr, GCHandleType.Pinned);
        try
        {
            if (ioctl(fd, TUNSETIFF, pinned.AddrOfPinnedObject()) < 0)
                throw new InvalidOperationException("TUNSETIFF failed");
        }
        finally
        {
            pinned.Free();
        }

        return new LinuxTun(new SafeFileHandle((IntPtr)fd, ownsHandle: true));
    }

    public async Task<int> ReadPacketAsync(byte[] buffer, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var len = read(_handle.DangerousGetHandle().ToInt32(), pinned.AddrOfPinnedObject(), buffer.Length);
                if (len < 0)
                    throw new InvalidOperationException("TUN read failed: " + Marshal.GetLastPInvokeError());
                return len;
            }
            finally
            {
                pinned.Free();
            }
        }, ct);
    }

    public async Task WritePacketAsync(ReadOnlyMemory<byte> packet, CancellationToken ct)
    {
        var copy = packet.ToArray();
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var pinned = GCHandle.Alloc(copy, GCHandleType.Pinned);
            try
            {
                var written = 0;
                while (written < copy.Length)
                {
                    var len = write(_handle.DangerousGetHandle().ToInt32(), IntPtr.Add(pinned.AddrOfPinnedObject(), written), copy.Length - written);
                    if (len < 0)
                        throw new InvalidOperationException("TUN write failed: " + Marshal.GetLastPInvokeError());
                    if (len == 0)
                        throw new InvalidOperationException("TUN write returned 0 bytes");
                    written += len;
                }
            }
            finally
            {
                pinned.Free();
            }
        }, ct);
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, IntPtr argp);

    [DllImport("libc", SetLastError = true)]
    private static extern int read(int fd, IntPtr buf, int count);

    [DllImport("libc", SetLastError = true)]
    private static extern int write(int fd, IntPtr buf, int count);
}

internal static class LinuxNet
{
    public static void ConfigureServer(NvpConfig config)
    {
        var externalInterface = GetDefaultRouteInterface();
        Run("ip", $"addr replace {config.Tunnel.ServerIp}/24 dev {config.Tunnel.LinuxInterfaceName}");
        Run("ip", $"link set dev {config.Tunnel.LinuxInterfaceName} up mtu 1400");
        Run("sysctl", "-w net.ipv4.ip_forward=1");
        Run("sysctl", "-w net.ipv4.conf.all.rp_filter=0");
        Run("sysctl", "-w net.ipv4.conf.default.rp_filter=0");
        Run("sysctl", $"-w net.ipv4.conf.{externalInterface}.rp_filter=0", throwOnError: false);
        Run("sysctl", $"-w net.ipv4.conf.{config.Tunnel.LinuxInterfaceName}.rp_filter=0", throwOnError: false);
        Run("sysctl", "-w net.ipv4.conf.all.send_redirects=0");
        Run("sysctl", "-w net.ipv4.conf.default.send_redirects=0");
        Run("sysctl", $"-w net.ipv4.conf.{externalInterface}.send_redirects=0", throwOnError: false);
        Run("sysctl", $"-w net.ipv6.conf.{config.Tunnel.LinuxInterfaceName}.disable_ipv6=1", throwOnError: false);
        if (Run("iptables", $"-t nat -C POSTROUTING -s {config.Tunnel.Cidr} -o {externalInterface} -j MASQUERADE", throwOnError: false) != 0)
            Run("iptables", $"-t nat -A POSTROUTING -s {config.Tunnel.Cidr} -o {externalInterface} -j MASQUERADE");
        Run("iptables", $"-C FORWARD -i {config.Tunnel.LinuxInterfaceName} -j ACCEPT", throwOnError: false);
        if (Run("iptables", $"-C FORWARD -i {config.Tunnel.LinuxInterfaceName} -j ACCEPT", throwOnError: false) != 0)
            Run("iptables", $"-A FORWARD -i {config.Tunnel.LinuxInterfaceName} -j ACCEPT");
        if (Run("iptables", $"-C FORWARD -o {config.Tunnel.LinuxInterfaceName} -j ACCEPT", throwOnError: false) != 0)
            Run("iptables", $"-A FORWARD -o {config.Tunnel.LinuxInterfaceName} -j ACCEPT");
        if (Run("iptables", $"-C FORWARD -i {config.Tunnel.LinuxInterfaceName} -o {externalInterface} -j ACCEPT", throwOnError: false) != 0)
            Run("iptables", $"-I FORWARD 1 -i {config.Tunnel.LinuxInterfaceName} -o {externalInterface} -j ACCEPT");
        if (Run("iptables", $"-C FORWARD -i {externalInterface} -o {config.Tunnel.LinuxInterfaceName} -m conntrack --ctstate RELATED,ESTABLISHED -j ACCEPT", throwOnError: false) != 0)
            Run("iptables", $"-I FORWARD 1 -i {externalInterface} -o {config.Tunnel.LinuxInterfaceName} -m conntrack --ctstate RELATED,ESTABLISHED -j ACCEPT");
    }

    public static void ConfigureClient(NvpConfig config, string serverAddress)
    {
        var snapshot = GetDefaultRouteSnapshot();
        Run("ip", $"addr replace {config.Tunnel.ClientIp}/24 dev {config.Tunnel.LinuxInterfaceName}");
        Run("ip", $"link set dev {config.Tunnel.LinuxInterfaceName} up mtu 1400");
        Run("sysctl", $"-w net.ipv6.conf.{config.Tunnel.LinuxInterfaceName}.disable_ipv6=1", throwOnError: false);
        Run("ip", $"route replace {serverAddress}/32 via {snapshot.Gateway} dev {snapshot.Interface}");
        PinSshClientRoute(snapshot);
        Run("ip", $"route replace 0.0.0.0/1 dev {config.Tunnel.LinuxInterfaceName} src {config.Tunnel.ClientIp}");
        Run("ip", $"route replace 128.0.0.0/1 dev {config.Tunnel.LinuxInterfaceName} src {config.Tunnel.ClientIp}");
    }

    public static NvpDiagnostics VerifyClient(NvpConfig config, string serverAddress)
    {
        try
        {
            var link = RunCapture("ip", $"addr show dev {config.Tunnel.LinuxInterfaceName}");
            var r1 = RunCapture("ip", "route show 0.0.0.0/1");
            var r2 = RunCapture("ip", "route show 128.0.0.0/1");
            var rs = RunCapture("ip", $"route get {serverAddress}");
            if (!link.Contains(config.Tunnel.ClientIp + "/", StringComparison.Ordinal))
                return new NvpDiagnostics { Stage = "linux_route_verify", Success = false, Details = "Client TUN address is missing" };
            if (!r1.Contains(config.Tunnel.LinuxInterfaceName, StringComparison.Ordinal))
                return new NvpDiagnostics { Stage = "linux_route_verify", Success = false, Details = "0.0.0.0/1 does not point to Linux TUN: " + r1.Trim() };
            if (!r2.Contains(config.Tunnel.LinuxInterfaceName, StringComparison.Ordinal))
                return new NvpDiagnostics { Stage = "linux_route_verify", Success = false, Details = "128.0.0.0/1 does not point to Linux TUN: " + r2.Trim() };
            if (rs.Contains(config.Tunnel.LinuxInterfaceName, StringComparison.Ordinal))
                return new NvpDiagnostics { Stage = "linux_route_verify", Success = false, Details = "Server host route loops into TUN: " + rs.Trim() };
            return new NvpDiagnostics { Stage = "linux_route_verify", Success = true, Details = $"tun={config.Tunnel.LinuxInterfaceName}; server_route={rs.Trim()}" };
        }
        catch (Exception ex)
        {
            return new NvpDiagnostics { Stage = "linux_route_verify", Success = false, Details = ex.Message };
        }
    }

    public static void RestoreClient(NvpConfig config, string serverAddress)
    {
        Run("ip", "route del 0.0.0.0/1", throwOnError: false);
        Run("ip", "route del 128.0.0.0/1", throwOnError: false);
        Run("ip", $"route del {serverAddress}/32", throwOnError: false);
        var sshClient = GetSshClientAddress();
        if (!string.IsNullOrWhiteSpace(sshClient))
            Run("ip", $"route del {sshClient}/32", throwOnError: false);
    }

    public static void ProbeIpv4Traffic()
    {
        Run("ping", "-4 -c 1 -W 1 1.1.1.1", throwOnError: false);
    }

    private static int Run(string file, string args, bool throwOnError = true)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (throwOnError && p.ExitCode != 0)
            throw new InvalidOperationException($"{file} {args} failed: {p.StandardError.ReadToEnd()}");
        return p.ExitCode;
    }

    private static string GetDefaultRouteInterface()
        => GetDefaultRouteSnapshot().Interface;

    private static LinuxRouteSnapshot GetDefaultRouteSnapshot()
    {
        var output = RunCapture("ip", "route show default");
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var devIndex = Array.FindIndex(tokens, t => t.Equals("dev", StringComparison.OrdinalIgnoreCase));
            var viaIndex = Array.FindIndex(tokens, t => t.Equals("via", StringComparison.OrdinalIgnoreCase));
            if (devIndex >= 0 && devIndex + 1 < tokens.Length && viaIndex >= 0 && viaIndex + 1 < tokens.Length)
                return new LinuxRouteSnapshot(tokens[devIndex + 1], tokens[viaIndex + 1]);
        }
        throw new InvalidOperationException("Cannot determine default route interface");
    }

    private static void PinSshClientRoute(LinuxRouteSnapshot snapshot)
    {
        var sshClient = GetSshClientAddress();
        if (string.IsNullOrWhiteSpace(sshClient))
            return;
        if (IPAddress.TryParse(sshClient, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            Run("ip", $"route replace {sshClient}/32 via {snapshot.Gateway} dev {snapshot.Interface}", throwOnError: false);
    }

    private static string? GetSshClientAddress()
    {
        var sshClient = Environment.GetEnvironmentVariable("SSH_CLIENT");
        if (string.IsNullOrWhiteSpace(sshClient))
            return null;
        return sshClient.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }

    private static string RunCapture(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{file} {args} failed: {error}");
        return output;
    }

    private sealed record LinuxRouteSnapshot(string Interface, string Gateway);
}

internal sealed class WintunDevice : IDisposable
{
    private const uint SessionCapacity = 0x400000;
    private readonly IntPtr _adapter;
    private readonly IntPtr _session;
    public int InterfaceIndex { get; }

    private WintunDevice(IntPtr adapter, IntPtr session, int interfaceIndex)
    {
        _adapter = adapter;
        _session = session;
        InterfaceIndex = interfaceIndex;
    }

    public static WintunDevice Open(string name)
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "wintun.dll")))
            throw new NoraAppException("NORA-KRT-3004", "wintun.dll is missing next to NoraVPN.exe");

        var adapter = WintunOpenAdapter(name);
        var openError = Marshal.GetLastPInvokeError();
        if (adapter == IntPtr.Zero)
        {
            adapter = WintunCreateAdapter(name, "NVP", IntPtr.Zero);
        }
        if (adapter == IntPtr.Zero)
        {
            var createError = Marshal.GetLastPInvokeError();
            throw new NoraAppException("NORA-KRT-3004", "Cannot create/open Wintun adapter. " +
                $"open_error={FormatWin32(openError)}, create_error={FormatWin32(createError)}. " +
                "Install/allow the Wintun driver and run through an elevated core service.");
        }

        var session = WintunStartSession(adapter, SessionCapacity);
        if (session == IntPtr.Zero)
        {
            var sessionError = Marshal.GetLastPInvokeError();
            WintunCloseAdapter(adapter);
            throw new NoraAppException("NORA-KRT-3004", "Cannot start Wintun session. error=" + FormatWin32(sessionError));
        }
        WintunGetAdapterLuid(adapter, out var luid);
        if (ConvertInterfaceLuidToIndex(ref luid, out var interfaceIndex) != 0)
            throw new NoraAppException("NORA-KRT-3004", "Cannot resolve Wintun interface index");
        return new WintunDevice(adapter, session, unchecked((int)interfaceIndex));
    }

    public async Task<ReadOnlyMemory<byte>> ReadPacketAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var packet = WintunReceivePacket(_session, out var size);
            if (packet != IntPtr.Zero)
            {
                var managed = new byte[size];
                Marshal.Copy(packet, managed, 0, (int)size);
                WintunReleaseReceivePacket(_session, packet);
                return managed;
            }

            var wait = WintunGetReadWaitEvent(_session);
            await Task.Run(() => WaitForSingleObject(wait, 250), ct);
        }
        return ReadOnlyMemory<byte>.Empty;
    }

    public void WritePacket(ReadOnlySpan<byte> packet)
    {
        var dst = WintunAllocateSendPacket(_session, (uint)packet.Length);
        if (dst == IntPtr.Zero)
            return;
        var managed = packet.ToArray();
        Marshal.Copy(managed, 0, dst, managed.Length);
        WintunSendPacket(_session, dst);
    }

    public void Dispose()
    {
        if (_session != IntPtr.Zero)
            WintunEndSession(_session);
        if (_adapter != IntPtr.Zero)
            WintunCloseAdapter(_adapter);
    }

    [DllImport("wintun.dll", EntryPoint = "WintunCreateAdapter", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr WintunCreateAdapter(string name, string tunnelType, IntPtr requestedGuid);

    [DllImport("wintun.dll", EntryPoint = "WintunOpenAdapter", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr WintunOpenAdapter(string name);

    [DllImport("wintun.dll", EntryPoint = "WintunCloseAdapter", SetLastError = true)]
    private static extern void WintunCloseAdapter(IntPtr adapter);

    [DllImport("wintun.dll", EntryPoint = "WintunStartSession", SetLastError = true)]
    private static extern IntPtr WintunStartSession(IntPtr adapter, uint capacity);

    [DllImport("wintun.dll", EntryPoint = "WintunEndSession", SetLastError = true)]
    private static extern void WintunEndSession(IntPtr session);

    [DllImport("wintun.dll", EntryPoint = "WintunGetReadWaitEvent", SetLastError = true)]
    private static extern IntPtr WintunGetReadWaitEvent(IntPtr session);

    [DllImport("wintun.dll", EntryPoint = "WintunReceivePacket", SetLastError = true)]
    private static extern IntPtr WintunReceivePacket(IntPtr session, out uint packetSize);

    [DllImport("wintun.dll", EntryPoint = "WintunReleaseReceivePacket", SetLastError = true)]
    private static extern void WintunReleaseReceivePacket(IntPtr session, IntPtr packet);

    [DllImport("wintun.dll", EntryPoint = "WintunAllocateSendPacket", SetLastError = true)]
    private static extern IntPtr WintunAllocateSendPacket(IntPtr session, uint packetSize);

    [DllImport("wintun.dll", EntryPoint = "WintunSendPacket", SetLastError = true)]
    private static extern void WintunSendPacket(IntPtr session, IntPtr packet);

    [DllImport("wintun.dll", EntryPoint = "WintunGetAdapterLUID", SetLastError = true)]
    private static extern void WintunGetAdapterLuid(IntPtr adapter, out NetLuid luid);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int ConvertInterfaceLuidToIndex(ref NetLuid interfaceLuid, out uint interfaceIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct NetLuid
    {
        public ulong Value;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    private static string FormatWin32(int error)
        => error == 0 ? "0" : $"{error} ({new Win32Exception(error).Message})";
}

internal static class WindowsNet
{
    private const string Ipv6SinkAddress = "fd66::2";

    public static IReadOnlyList<string> ActiveVpnWarnings(NvpConfig config)
    {
        var warnings = new List<string>();
        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up)
                continue;
            if (iface.Name.Equals(config.Tunnel.InterfaceName, StringComparison.OrdinalIgnoreCase) ||
                iface.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase))
                continue;
            var label = iface.Name + " / " + iface.Description;
            if (label.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Amnezia", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("TAP-", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Hamachi", StringComparison.OrdinalIgnoreCase))
                warnings.Add("active VPN adapter can override KRot routes: " + label);
        }
        return warnings;
    }

    public static NvpDiagnostics SnapshotCheck(NvpConfig config, int tunIndex)
    {
        try
        {
            var snapshot = GetSnapshot(config, tunIndex);
            return new NvpDiagnostics
            {
                Stage = "windows_network_snapshot",
                Success = true,
                Details = $"tun={snapshot.TunName}/{snapshot.TunIndex}; physical_if={snapshot.PhysicalIndex}; gateway={snapshot.PhysicalGateway}"
            };
        }
        catch (Exception ex)
        {
            return new NvpDiagnostics { Stage = "windows_network_snapshot", Success = false, Details = ex.Message };
        }
    }

    public static void ConfigureClient(NvpConfig config, string serverAddress, int tunIndex)
    {
        var snapshot = GetSnapshot(config, tunIndex);
        var mask = CidrToMask(24);

        Run("netsh.exe", $"interface ipv4 set address name={tunIndex} static {config.Tunnel.ClientIp} {mask} none", timeoutMs: 20000);
        Run("netsh.exe", $"interface ipv4 set interface {tunIndex} metric=1", timeoutMs: 15000);
        if (config.Tunnel.Dns.Count > 0)
        {
            Run("netsh.exe", $"interface ipv4 set dnsservers name={tunIndex} static {config.Tunnel.Dns[0]} primary validate=no", timeoutMs: 15000);
            for (var i = 1; i < config.Tunnel.Dns.Count; i++)
                Run("netsh.exe", $"interface ipv4 add dnsservers name={tunIndex} address={config.Tunnel.Dns[i]} index={i + 1} validate=no", timeoutMs: 15000);
        }
        if (config.Security.WindowsBlockIpv6)
            InstallIpv6LeakGuard(snapshot.TunIndex);

        // Other VPN clients often own split-default /1 routes; KRot must win them to own the full tunnel.
        RemoveCompetingFullTunnelRoutes(config);
        Run("route.exe", $"DELETE 0.0.0.0 MASK 128.0.0.0", throwOnError: false, timeoutMs: 10000);
        Run("route.exe", $"DELETE 128.0.0.0 MASK 128.0.0.0", throwOnError: false, timeoutMs: 10000);
        Run("route.exe", $"DELETE {serverAddress} MASK 255.255.255.255", throwOnError: false, timeoutMs: 10000);

        Run("route.exe", $"ADD {serverAddress} MASK 255.255.255.255 {snapshot.PhysicalGateway} IF {snapshot.PhysicalIndex} METRIC 1", timeoutMs: 10000);
        Run("netsh.exe", $"interface ipv4 add route prefix=0.0.0.0/1 interface={snapshot.TunIndex} metric=1 store=active", timeoutMs: 10000);
        Run("netsh.exe", $"interface ipv4 add route prefix=128.0.0.0/1 interface={snapshot.TunIndex} metric=1 store=active", timeoutMs: 10000);
    }

    public static NvpDiagnostics VerifyClient(NvpConfig config, string serverAddress, int tunIndex)
    {
        try
        {
            var snapshot = GetSnapshot(config, tunIndex);
            var r1 = RunCapture("route.exe", "PRINT -4 0.0.0.0", timeoutMs: 5000);
            var r2 = RunCapture("route.exe", "PRINT -4 128.0.0.0", timeoutMs: 5000);
            var rs = RunCapture("route.exe", $"PRINT -4 {serverAddress}", timeoutMs: 5000);
            var tunRoutes = RunCapture("netsh.exe", $"interface ipv4 show route interface={snapshot.TunIndex}", timeoutMs: 5000);
            var dns = RunCapture("netsh.exe", $"interface ipv4 show dnsservers name={snapshot.TunIndex}", timeoutMs: 5000);

            if (!HasTunPrefix(tunRoutes, "0.0.0.0/1") && !HasOnLinkTunRoute(r1, "0.0.0.0", "128.0.0.0", config.Tunnel.ClientIp, snapshot.TunIndex))
                return new NvpDiagnostics { Stage = "route_dns_verify", Success = false, Details = "Full-tunnel 0.0.0.0/1 route is missing; " + CompactRouteDump(tunRoutes, r1) };
            if (!HasTunPrefix(tunRoutes, "128.0.0.0/1") && !HasOnLinkTunRoute(r2, "128.0.0.0", "128.0.0.0", config.Tunnel.ClientIp, snapshot.TunIndex))
                return new NvpDiagnostics { Stage = "route_dns_verify", Success = false, Details = "Full-tunnel 128.0.0.0/1 route is missing; " + CompactRouteDump(tunRoutes, r2) };
            if (!HasRoute(rs, serverAddress, "255.255.255.255", snapshot.PhysicalGateway))
                return new NvpDiagnostics { Stage = "route_dns_verify", Success = false, Details = "Server host route loop guard is missing" };
            if (config.Tunnel.Dns.Count > 0 && !config.Tunnel.Dns.Any(d => dns.Contains(d, StringComparison.Ordinal)))
                return new NvpDiagnostics { Stage = "route_dns_verify", Success = false, Details = "DNS capture is missing on TUN adapter" };
            if (config.Security.WindowsBlockIpv6)
            {
                var ipv6Routes = RunCapture("netsh.exe", "interface ipv6 show route store=active", timeoutMs: 5000);
                if (!HasIpv6SinkRoutes(ipv6Routes, snapshot.TunIndex))
                {
                    // Windows netsh IPv6 route output varies by locale/version and cannot be
                    // filtered by interface. The add-route commands above are the hard gate;
                    // verification is advisory so a parser mismatch cannot break IPv4 VPN.
                    Console.WriteLine("[client] WARN IPv6 leak guard route verification was inconclusive");
                }
            }

            var probeRoute = RunCapture("route.exe", "PRINT -4 1.1.1.1", timeoutMs: 5000);
            var ipv6 = config.Security.WindowsBlockIpv6 ? "; ipv6=blocked" : "";
            return new NvpDiagnostics { Stage = "route_dns_verify", Success = true, Details = $"adapter={snapshot.TunName}; ifIndex={snapshot.TunIndex}; dns={string.Join(",", config.Tunnel.Dns)}{ipv6}; probe_route={CompactRouteDump(tunRoutes, probeRoute)}" };
        }
        catch (Exception ex)
        {
            return new NvpDiagnostics { Stage = "route_dns_verify", Success = false, Details = ex.Message };
        }
    }

    public static void RestoreClient(NvpConfig config, string serverAddress, int? tunIndex)
    {
        Run("route.exe", $"DELETE 0.0.0.0 MASK 128.0.0.0", throwOnError: false, timeoutMs: 10000);
        Run("route.exe", $"DELETE 128.0.0.0 MASK 128.0.0.0", throwOnError: false, timeoutMs: 10000);
        Run("route.exe", $"DELETE {serverAddress} MASK 255.255.255.255", throwOnError: false, timeoutMs: 10000);
        if (tunIndex is not null)
        {
            if (config.Security.WindowsBlockIpv6)
                RemoveIpv6LeakGuard(tunIndex.Value);
            Run("netsh.exe", $"interface ipv4 set dnsservers name={tunIndex.Value} source=dhcp", throwOnError: false, timeoutMs: 10000);
            Run("netsh.exe", $"interface ipv4 set address name={tunIndex.Value} source=dhcp", throwOnError: false, timeoutMs: 10000);
        }
    }

    public static void ProbeIpv4Traffic()
    {
        Run("ping.exe", "-4 -n 1 -w 1200 1.1.1.1", throwOnError: false, timeoutMs: 3000);
    }

    private static WindowsNetSnapshot GetSnapshot(NvpConfig config, int tunIndex, bool throwOnError = true)
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            var tun = interfaces
                .Select(i => new { Interface = i, Props = SafeIpProps(i), V4 = SafeV4Props(i) })
                .Where(i => i.V4 is not null && i.V4!.Index == tunIndex)
                .FirstOrDefault() ?? throw new InvalidOperationException($"Wintun adapter not found for ifIndex {tunIndex}");

            var physical = interfaces
                .Select(i => new { Interface = i, Props = SafeIpProps(i), V4 = SafeV4Props(i) })
                .Where(i => i.V4 is not null && i.Props is not null && i.V4!.Index != tun.V4!.Index)
                .Where(i => i.Interface.OperationalStatus == OperationalStatus.Up)
                .Select(i => new
                {
                    i.Interface,
                    i.V4,
                    Gateway = i.Props!.GatewayAddresses
                        .Select(g => g.Address)
                        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(a))
                })
                .Where(i => i.Gateway is not null)
                .OrderBy(i => i.V4!.Index == 1 ? 1 : 0)
                .FirstOrDefault() ?? throw new InvalidOperationException("Physical route not found");

            return new WindowsNetSnapshot
            {
                TunName = tun.Interface.Name,
                TunIndex = tun.V4!.Index,
                PhysicalIndex = physical.V4!.Index,
                PhysicalGateway = physical.Gateway!.ToString()
            };
        }
        catch
        {
            if (throwOnError)
                throw;
            return null!;
        }
    }

    private static int Run(string file, string args, bool throwOnError = true, int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{file} {args} timed out while configuring Windows networking");
        }
        if (throwOnError && p.ExitCode != 0)
            throw new InvalidOperationException($"{file} {args} failed: {p.StandardOutput.ReadToEnd()} {p.StandardError.ReadToEnd()}");
        return p.ExitCode;
    }

    private static string RunCapture(string file, string args, int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{file} timed out while verifying Windows networking");
        }
        var output = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{file} failed: {output} {error}");
        return output;
    }

    private static void InstallIpv6LeakGuard(int tunIndex)
    {
        RemoveIpv6LeakGuard(tunIndex);
        Run("netsh.exe", $"interface ipv6 add address interface={tunIndex} address={Ipv6SinkAddress} store=active", timeoutMs: 10000);
        Run("netsh.exe", $"interface ipv6 add route prefix=::/1 interface={tunIndex} metric=1 store=active", timeoutMs: 10000);
        Run("netsh.exe", $"interface ipv6 add route prefix=8000::/1 interface={tunIndex} metric=1 store=active", timeoutMs: 10000);
    }

    private static void RemoveIpv6LeakGuard(int tunIndex)
    {
        Run("netsh.exe", $"interface ipv6 delete route prefix=::/1 interface={tunIndex} store=active", throwOnError: false, timeoutMs: 10000);
        Run("netsh.exe", $"interface ipv6 delete route prefix=8000::/1 interface={tunIndex} store=active", throwOnError: false, timeoutMs: 10000);
        Run("netsh.exe", $"interface ipv6 delete address interface={tunIndex} address={Ipv6SinkAddress} store=active", throwOnError: false, timeoutMs: 10000);
    }

    private static bool HasIpv6SinkRoutes(string routeOutput, int tunIndex)
    {
        var compact = routeOutput.Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "|", StringComparison.Ordinal);
        return compact.Contains("::/1", StringComparison.OrdinalIgnoreCase)
            && compact.Contains("8000::/1", StringComparison.OrdinalIgnoreCase)
            && compact.Contains(tunIndex.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static string CidrToMask(int prefixLength)
    {
        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        return string.Join('.', new[] { (mask >> 24) & 255, (mask >> 16) & 255, (mask >> 8) & 255, mask & 255 });
    }

    private static IPInterfaceProperties? SafeIpProps(NetworkInterface iface)
    {
        try { return iface.GetIPProperties(); } catch { return null; }
    }

    private static IPv4InterfaceProperties? SafeV4Props(NetworkInterface iface)
    {
        try { return iface.GetIPProperties().GetIPv4Properties(); } catch { return null; }
    }

    private static bool HasRoute(string routePrint, string destination, string mask, string gateway)
    {
        foreach (var line in routePrint.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5 &&
                parts[0] == destination &&
                parts[1] == mask &&
                parts[2].Equals(gateway, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool HasOnLinkTunRoute(string routePrint, string destination, string mask, string interfaceAddress, int tunIndex)
    {
        var tunIndexDecimal = tunIndex.ToString(CultureInfo.InvariantCulture);
        var tunIndexHex = tunIndex.ToString("x", CultureInfo.InvariantCulture);
        foreach (var line in routePrint.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || parts[0] != destination || parts[1] != mask)
                continue;

            var gateway = parts[2];
            var iface = parts[3];
            var gatewayMatches = gateway.Equals("On-link", StringComparison.OrdinalIgnoreCase) ||
                                 gateway.Equals(interfaceAddress, StringComparison.OrdinalIgnoreCase) ||
                                 gateway == "0.0.0.0";
            var ifaceMatches = iface.Equals(interfaceAddress, StringComparison.OrdinalIgnoreCase) ||
                               iface.Equals(tunIndexDecimal, StringComparison.OrdinalIgnoreCase) ||
                               iface.Equals(tunIndexHex, StringComparison.OrdinalIgnoreCase);
            if (gatewayMatches && ifaceMatches)
                return true;
        }
        return false;
    }

    private static bool HasTunPrefix(string netshRouteOutput, string prefix)
        => netshRouteOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Contains(prefix, StringComparison.OrdinalIgnoreCase));

    private static string CompactRouteDump(string netshRoutes, string routePrint)
    {
        static string Clean(string value)
        {
            var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Where(line => line.Contains("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("128.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("/1", StringComparison.OrdinalIgnoreCase))
                .Take(8);
            return string.Join(" | ", lines);
        }

        var netsh = Clean(netshRoutes);
        var route = Clean(routePrint);
        if (string.IsNullOrWhiteSpace(netsh) && string.IsNullOrWhiteSpace(route))
            return "route dump empty";
        return $"netsh=[{netsh}] route=[{route}]";
    }

    private static void RemoveCompetingFullTunnelRoutes(NvpConfig config)
    {
        foreach (var (destination, mask, output) in new[]
        {
            ("0.0.0.0", "128.0.0.0", RunCapture("route.exe", "PRINT -4 0.0.0.0", timeoutMs: 5000)),
            ("128.0.0.0", "128.0.0.0", RunCapture("route.exe", "PRINT -4 128.0.0.0", timeoutMs: 5000))
        })
        {
            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                    continue;
                if (!parts[0].Equals(destination, StringComparison.OrdinalIgnoreCase) || !parts[1].Equals(mask, StringComparison.OrdinalIgnoreCase))
                    continue;
                var gateway = parts[2];
                if (gateway.Equals(config.Tunnel.ServerIp, StringComparison.OrdinalIgnoreCase))
                    continue;
                Run("route.exe", $"DELETE {destination} MASK {mask} {gateway}", throwOnError: false, timeoutMs: 10000);
            }
        }
    }

    private sealed class WindowsNetSnapshot
    {
        public string TunName { get; set; } = "";
        public int TunIndex { get; set; }
        public int PhysicalIndex { get; set; }
        public string PhysicalGateway { get; set; } = "";
    }
}
