using System.Net.Sockets;
using System.IO;
using System.Text.RegularExpressions;

namespace Nvp;

internal enum NoraErrorSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

internal enum NoraOperation
{
    ApplicationStart,
    Connect,
    Disconnect,
    SwitchServer,
    ImportProfile,
    InstallServer,
    UninstallServer,
    CreateUser,
    DeleteUser,
    RefreshUsers,
    CopyKey,
    PingServers,
    Diagnostics,
    LoadProfile,
    BackendRuntime,
    CommandLine
}

internal sealed record NoraErrorDefinition(
    string Code,
    NoraErrorSeverity Severity,
    string Title,
    string Message,
    string Meaning,
    string Action,
    bool ShowToast = true);

internal sealed record NoraErrorIncident(
    NoraErrorDefinition Definition,
    NoraOperation Operation,
    string TechnicalDetails)
{
    public string Code => Definition.Code;
    public string Title => Definition.Title;
    public string Message => Definition.Message;
    public string Action => Definition.Action;
    public bool ShowToast => Definition.ShowToast;

    public string ToLogLine()
        => $"[error] code={Code} severity={Definition.Severity.ToString().ToLowerInvariant()} operation={Operation.ToString().ToLowerInvariant()} details={Sanitize(TechnicalDetails)}";

    private static string Sanitize(string value)
    {
        var sanitized = Regex.Replace(value.Replace('\r', ' ').Replace('\n', ' '), @"\s+", " ").Trim();
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)\b(password|privatekey|presharedkey|credential_key|credentialkey|authorization)\s*[:=]\s*[^\s,;]+",
            "$1=<redacted>");
        sanitized = Regex.Replace(sanitized, @"(?i)\b(?:nora1\.[A-Za-z0-9_-]+|vless://\S+)", "<redacted-key>");
        return sanitized.Length <= 1200 ? sanitized : sanitized[..1200] + "...";
    }
}

internal sealed class NoraAppException : Exception
{
    public NoraAppException(string code, string? technicalDetails = null, Exception? innerException = null)
        : base(technicalDetails ?? code, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

internal static class NoraErrors
{
    private static NoraErrorDefinition D(
        string code,
        NoraErrorSeverity severity,
        string title,
        string message,
        string meaning,
        string action,
        bool showToast = true)
        => new(code, severity, title, message, meaning, action, showToast);

    public static readonly IReadOnlyList<NoraErrorDefinition> Catalog =
    [
        D("NORA-APP-1001", NoraErrorSeverity.Critical, "NORA could not start", "The application stopped because of an unexpected internal error.", "An unhandled application error reached the top-level safety handler.", "Restart NORA VPN. If it happens again, send this code and the Logs page to support."),
        D("NORA-APP-1002", NoraErrorSeverity.Error, "Administrator access required", "Windows did not grant the permissions needed to manage VPN adapters and routes.", "The UAC elevation request was rejected or could not be started.", "Close NORA VPN, start it again, and approve the Windows administrator prompt."),
        D("NORA-APP-1003", NoraErrorSeverity.Critical, "Required component is missing", "A required NORA VPN runtime component is missing or blocked.", "The packaged application or one of its native runtime files is incomplete.", "Restore the complete NORA VPN folder or reinstall the application."),

        D("NORA-CON-2001", NoraErrorSeverity.Error, "No server selected", "NORA VPN has no usable server profile to connect with.", "The active profile was removed, is missing, or has not been selected.", "Open Servers or Add and select or import a server."),
        D("NORA-CON-2002", NoraErrorSeverity.Error, "Protocol is not supported", "This server uses a protocol that the installed client cannot run.", "No backend is registered for the selected profile type.", "Refresh or reimport the profile, or select a KRot, VLESS/Xray, or AWG 2.0 server."),
        D("NORA-CON-2003", NoraErrorSeverity.Error, "VPN permission denied", "Windows blocked creation of the tunnel adapter or network routes.", "The VPN backend received an access-denied response from Windows.", "Restart NORA VPN as administrator and approve the UAC prompt."),
        D("NORA-CON-2004", NoraErrorSeverity.Error, "Server route could not be protected", "NORA VPN could not keep the selected server outside the full-tunnel route.", "The endpoint bypass route was not installed, which would create a routing loop.", "Disconnect other VPN clients, then reconnect. Check Logs if Windows still rejects the route."),
        D("NORA-CON-2005", NoraErrorSeverity.Error, "VPN engine is missing", "The engine required by this server is missing or was blocked.", "The KRot, Xray, sing-box, AWG, or Wintun component cannot be found or loaded.", "Restore the complete NORA VPN folder and check Windows Security quarantine."),
        D("NORA-CON-2006", NoraErrorSeverity.Error, "VPN engine did not start", "The selected VPN engine stopped before the tunnel became ready.", "The backend process failed during startup or rejected its generated configuration.", "Open Logs for the engine detail, refresh the profile, and try the server again."),
        D("NORA-CON-2007", NoraErrorSeverity.Error, "Secure handshake rejected", "The server rejected the secure connection or its credentials.", "The key, UUID, Reality parameters, certificate, or KRot credential did not match the server.", "Refresh or reimport the key/subscription. If only one node fails, choose another node."),
        D("NORA-CON-2008", NoraErrorSeverity.Error, "Server did not respond", "The selected server did not become ready before the connection timeout.", "The endpoint may be offline, blocked, overloaded, or unreachable from the current network.", "Try the server once more, then choose another server or network."),
        D("NORA-CON-2009", NoraErrorSeverity.Error, "DNS is unavailable", "The tunnel started, but domain names could not be resolved through it.", "The protected DNS path failed its end-to-end check.", "Reconnect once. If the error returns, choose another server and check Logs."),
        D("NORA-CON-2010", NoraErrorSeverity.Error, "Internet check failed", "The VPN engine started, but verified internet traffic did not pass through the tunnel.", "The end-to-end HTTPS/data-plane acceptance check failed, so NORA did not claim a working connection.", "Disconnect other VPN clients, retry, or select another server. Use the code with Logs for support."),
        D("NORA-CON-2011", NoraErrorSeverity.Critical, "VPN connection was interrupted", "The active VPN engine stopped unexpectedly.", "A backend process or tunnel service exited after the connection had already been verified.", "Reconnect. If it repeats, open Logs and check the backend-specific error immediately before this code."),
        D("NORA-CON-2012", NoraErrorSeverity.Warning, "Another VPN is active", "Another tunnel may interfere with NORA VPN routing.", "A non-NORA TUN, TAP, or Wintun adapter is currently active.", "Disconnect the other VPN before connecting NORA VPN.", false),
        D("NORA-CON-2013", NoraErrorSeverity.Error, "Server switch failed", "NORA VPN could not complete the switch to the selected server.", "Disconnecting the old backend or starting/verifying the new backend failed.", "Reconnect to the previous server or choose another server."),
        D("NORA-CON-2014", NoraErrorSeverity.Error, "Disconnect failed", "NORA VPN could not stop and clean up the active tunnel normally.", "The backend, tunnel service, or route cleanup did not finish successfully.", "Close NORA VPN, confirm the tunnel is gone, and restart the application."),

        D("NORA-KRT-3001", NoraErrorSeverity.Error, "KRot profile is invalid", "The selected KRot profile is missing required protocol settings.", "The profile schema, endpoint, tunnel, security, or credential fields cannot start a KRot session.", "Reimport the original NORA/KRot key or create a new user key."),
        D("NORA-KRT-3002", NoraErrorSeverity.Error, "KRot cover connection failed", "KRot could not establish its protected TLS/HTTP cover channel.", "The cover endpoint, TLS name, certificate, or cover service did not complete negotiation.", "Retry once, then verify the KRot server domain/cover configuration."),
        D("NORA-KRT-3003", NoraErrorSeverity.Error, "KRot credential rejected", "The KRot server did not accept this connection credential.", "The credential is invalid, disabled, deleted, or belongs to another server profile.", "Ask the server owner for a new key or enable the user again."),
        D("NORA-KRT-3004", NoraErrorSeverity.Error, "KRot adapter failed", "KRot could not open or configure its Wintun adapter.", "Wintun is missing, blocked, already owned, or Windows denied adapter access.", "Run NORA as administrator, close other VPNs, and restore `wintun.dll` if missing."),
        D("NORA-KRT-3005", NoraErrorSeverity.Error, "KRot network setup failed", "KRot could not apply or verify its DNS and full-tunnel routes.", "The Windows network state does not match the routes required by the KRot adapter.", "Disconnect other VPNs, run as administrator, and reconnect."),
        D("NORA-KRT-3006", NoraErrorSeverity.Error, "KRot data channel failed", "The encrypted KRot session stopped while carrying tunnel packets.", "Frame encryption, authentication, stream framing, or relay packet processing failed.", "Reconnect. If it repeats, send this code and Logs to the server owner."),
        D("NORA-KRT-3007", NoraErrorSeverity.Error, "KRot reconnect failed", "KRot could not resume the session after the network path changed.", "The resume token, retained session, replay window, or reconnect deadline was rejected.", "Reconnect manually. If it repeats during Wi-Fi/mobile changes, update both client and server core."),
        D("NORA-KRT-3008", NoraErrorSeverity.Error, "KRot server is unavailable", "The KRot relay did not accept or retain the session.", "The server service is stopped, unreachable, overloaded, or incompatible with this client core.", "Check the VPS KRot service and version, then retry."),

        D("NORA-XRY-3101", NoraErrorSeverity.Error, "Xray components are missing", "The VLESS/Xray backend or its TUN frontend is not available.", "xray.exe or sing-box.exe is absent, blocked, or cannot be launched.", "Restore the complete application folder and check Windows Security quarantine."),
        D("NORA-XRY-3102", NoraErrorSeverity.Error, "VLESS profile is incomplete", "The selected VLESS node is missing required transport or security parameters.", "The subscription did not provide a usable UUID, host, Reality/TLS, or transport configuration.", "Refresh the subscription. If only this node fails, select another node."),
        D("NORA-XRY-3103", NoraErrorSeverity.Error, "Xray backend failed", "Xray rejected the selected VLESS node or stopped during startup.", "The protocol adapter could not open its local SOCKS service or outbound connection.", "Refresh the subscription and try another node. Use Logs to identify the rejected field."),
        D("NORA-XRY-3104", NoraErrorSeverity.Error, "Xray tunnel failed", "The Windows TUN frontend for Xray did not become ready.", "sing-box could not create or configure the TUN/DNS path used by Xray.", "Run NORA VPN as administrator and disconnect other VPN clients."),
        D("NORA-XRY-3105", NoraErrorSeverity.Error, "Xray route loop prevented", "NORA VPN could not install the physical route required by the VLESS endpoint.", "Starting full tunnel without the endpoint bypass would route Xray into itself.", "Disconnect other VPN clients and retry. Check Windows routes if it continues."),
        D("NORA-XRY-3106", NoraErrorSeverity.Error, "VLESS server rejected the connection", "The selected VLESS/Reality node did not accept its handshake or transport request.", "The node may be expired, incompatible, blocked, or configured differently by the provider.", "Refresh the subscription and try another node."),

        D("NORA-AWG-3201", NoraErrorSeverity.Error, "AWG profile is incomplete", "The imported AWG 2.0 profile is missing required Interface or Peer settings.", "The configuration cannot create a valid AmneziaWG tunnel.", "Import the complete AWG 2.0 configuration again."),
        D("NORA-AWG-3202", NoraErrorSeverity.Error, "AWG components are missing", "The AmneziaWG 2.0 service or control tool is not available.", "amneziawg.exe or awg.exe is absent or blocked.", "Restore the complete application folder and check Windows Security quarantine."),
        D("NORA-AWG-3203", NoraErrorSeverity.Error, "AWG service could not be installed", "Windows rejected installation of the AmneziaWG tunnel service.", "The service command failed, usually because of permissions, driver state, or an invalid profile.", "Run NORA VPN as administrator, then reimport the profile if needed."),
        D("NORA-AWG-3204", NoraErrorSeverity.Error, "AWG tunnel did not become ready", "The AmneziaWG service started but did not expose a working tunnel in time.", "The driver, endpoint, or profile did not reach a ready state.", "Reconnect once, then check Logs or import a fresh AWG profile."),

        D("NORA-SUB-4001", NoraErrorSeverity.Error, "Subscription address is invalid", "The entered subscription URL or payload is not valid.", "NORA VPN could not recognize a supported URL or subscription body.", "Paste the complete HTTPS subscription URL and try again."),
        D("NORA-SUB-4002", NoraErrorSeverity.Error, "Subscription download failed", "NORA VPN could not download the subscription.", "The panel returned an HTTP error, timed out, or could not be reached.", "Check the URL and internet connection, then retry."),
        D("NORA-SUB-4003", NoraErrorSeverity.Error, "No supported servers found", "The subscription contains no usable KRot, VLESS/Xray, or AWG 2.0 servers.", "The payload is empty, expired, or uses formats not supported by this client.", "Open the subscription in the provider panel or request a compatible link."),
        D("NORA-SUB-4004", NoraErrorSeverity.Error, "Subscription could not be read", "The downloaded subscription is malformed or incomplete.", "Its Base64, YAML, JSON, URI, or protocol fields could not be parsed safely.", "Refresh the link in the provider panel and import it again."),
        D("NORA-SUB-4005", NoraErrorSeverity.Error, "Subscription could not be saved", "NORA VPN could not store the imported subscription on this computer.", "The application data directory is unavailable or not writable.", "Check free disk space and folder permissions, then retry."),

        D("NORA-VPS-5001", NoraErrorSeverity.Error, "VPS details are incomplete", "Required VPS or SSH fields are missing or invalid.", "Provisioning cannot begin without a valid host, port, SSH user, and authentication.", "Correct the highlighted VPS fields and try again."),
        D("NORA-VPS-5002", NoraErrorSeverity.Error, "SSH connection failed", "NORA VPN could not authenticate to the VPS.", "The server, SSH port, user, password, host key, or firewall rejected the connection.", "Verify the SSH details by logging in to the VPS, then retry."),
        D("NORA-VPS-5003", NoraErrorSeverity.Error, "KRot installation failed", "KRot could not be installed or verified on the VPS.", "A remote install, service, upload, firewall, or verification command failed.", "Open Logs for the failed remote step, fix the VPS issue, and run Install again."),
        D("NORA-VPS-5004", NoraErrorSeverity.Error, "KRot reinstall failed", "The existing KRot installation could not be replaced safely.", "The old service or files could not be stopped, updated, or verified.", "Check VPS disk space and service permissions, then retry reinstall."),
        D("NORA-VPS-5005", NoraErrorSeverity.Error, "KRot uninstall failed", "NORA VPN could not remove KRot from the self-hosted VPS.", "The SSH cleanup, service removal, or local self-hosted state cleanup did not finish safely.", "Verify SSH access to the VPS, then retry Uninstall KRot from Users."),

        D("NORA-USR-6001", NoraErrorSeverity.Error, "Server cannot be managed", "This server has no saved self-hosted administration credentials.", "Imported end-user keys do not grant permission to create or remove server users.", "Install or reinstall KRot from the Add page to register this VPS as self-hosted."),
        D("NORA-USR-6002", NoraErrorSeverity.Error, "User name is invalid", "Enter a non-empty user name using supported characters.", "The requested login is empty or cannot be represented safely on the server.", "Correct the user name and try again."),
        D("NORA-USR-6003", NoraErrorSeverity.Error, "User was not created", "The server did not retain the new KRot credential.", "The SSH mutation, server write, restart, or verification step failed.", "Check the VPS connection and retry. The user is not active unless it appears in the list."),
        D("NORA-USR-6004", NoraErrorSeverity.Error, "User was not deleted", "The server did not disable the selected KRot credential.", "The SSH mutation or remote verification step failed.", "Retry after confirming the VPS is reachable."),
        D("NORA-USR-6005", NoraErrorSeverity.Warning, "User statistics are stale", "NORA VPN could not refresh user status and traffic right now.", "The background SSH statistics refresh failed; existing values remain visible.", "No action is required unless it continues. Check VPS availability in Logs.", false),
        D("NORA-USR-6006", NoraErrorSeverity.Error, "Connection key is unavailable", "NORA VPN could not reconstruct or copy this user's connection key.", "The local credential material is missing or the clipboard operation failed.", "Create a new user key if the original credential is no longer stored."),

        D("NORA-NET-7001", NoraErrorSeverity.Error, "DNS check failed", "The protected DNS request did not complete through the tunnel.", "DNS routing, hijacking, or the selected server's DNS path is unavailable.", "Reconnect or select another server."),
        D("NORA-NET-7002", NoraErrorSeverity.Error, "HTTPS check failed", "The protected web request did not complete through the tunnel.", "The tunnel exists, but verified HTTPS traffic is blocked or misrouted.", "Reconnect, disable competing VPNs, or select another server."),
        D("NORA-NET-7003", NoraErrorSeverity.Error, "Windows route setup failed", "NORA VPN could not apply or verify full-tunnel routes.", "Windows rejected a required route or assigned it to the wrong adapter.", "Run as administrator and disconnect other VPN clients."),
        D("NORA-NET-7004", NoraErrorSeverity.Error, "Server is unreachable", "The selected server cannot be reached from the current network.", "The endpoint is offline, blocked, or unavailable on this connection.", "Try another network or server."),
        D("NORA-NET-7005", NoraErrorSeverity.Error, "Physical network was not found", "NORA VPN could not identify an active Wi-Fi or Ethernet gateway.", "No usable physical route exists for the VPN endpoint.", "Reconnect Wi-Fi/Ethernet, then start the VPN again."),
        D("NORA-NET-7006", NoraErrorSeverity.Error, "Network operation timed out", "A required network operation did not finish in time.", "The network or server stopped responding during a required step.", "Retry once, then select another server or network."),

        D("NORA-CFG-8001", NoraErrorSeverity.Error, "Profile was not found", "The selected server profile no longer exists on this computer.", "The profile file was moved, deleted, or not included with the application state.", "Import the key/configuration again."),
        D("NORA-CFG-8002", NoraErrorSeverity.Error, "Profile is malformed", "The selected profile cannot be read safely.", "Required JSON, key, transport, or address fields are invalid.", "Import a fresh profile from the source."),
        D("NORA-CFG-8003", NoraErrorSeverity.Error, "Key format is not supported", "NORA VPN did not recognize the pasted key or configuration.", "The input is not a supported nora1/KRot, VLESS, subscription, or AWG profile.", "Paste the complete original key or configuration without editing it."),
        D("NORA-CFG-8004", NoraErrorSeverity.Error, "Credential is missing", "The profile exists, but its secret credential is unavailable.", "The local key material needed for authentication was removed or not saved.", "Reimport the original key or create a new user credential."),

        D("NORA-DIA-9001", NoraErrorSeverity.Error, "Diagnostics failed", "NORA VPN could not complete the requested diagnostic check.", "A diagnostic dependency or network stage failed.", "Open Logs for details and retry after resolving the reported component."),
        D("NORA-INT-9999", NoraErrorSeverity.Critical, "Unexpected NORA VPN error", "The requested operation stopped because of an unexpected internal error.", "No more specific safe classification matched this failure.", "Retry once. If it repeats, send this code and the Logs page to support.")
    ];

    private static readonly IReadOnlyDictionary<string, NoraErrorDefinition> ByCode =
        Catalog.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

    public static NoraErrorIncident Classify(NoraOperation operation, Exception exception)
    {
        var details = ExceptionText(exception);
        if (FindExplicitCode(exception) is { } explicitCode && ByCode.TryGetValue(explicitCode, out var explicitDefinition))
            return new NoraErrorIncident(explicitDefinition, operation, details);
        var embeddedCode = Regex.Match(details, @"\bNORA-[A-Z]{3}-\d{4}\b", RegexOptions.IgnoreCase).Value;
        if (embeddedCode.Length > 0 && ByCode.TryGetValue(embeddedCode, out var embeddedDefinition))
            return new NoraErrorIncident(embeddedDefinition, operation, details);

        var m = details.ToLowerInvariant();
        var code = ClassifyCode(operation, exception, m);
        return new NoraErrorIncident(ByCode[code], operation, details);
    }

    public static NoraErrorIncident Create(string code, NoraOperation operation, string technicalDetails)
    {
        if (!ByCode.TryGetValue(code, out var definition))
            definition = ByCode["NORA-INT-9999"];
        return new NoraErrorIncident(definition, operation, technicalDetails);
    }

    public static int RunSelfTest(TextWriter output)
    {
        var failures = new List<string>();
        var codePattern = new Regex(@"^NORA-[A-Z]{3}-\d{4}$", RegexOptions.CultureInvariant);
        foreach (var item in Catalog)
        {
            if (!codePattern.IsMatch(item.Code)) failures.Add("Invalid code: " + item.Code);
            if (string.IsNullOrWhiteSpace(item.Title)) failures.Add("Missing title: " + item.Code);
            if (string.IsNullOrWhiteSpace(item.Message)) failures.Add("Missing message: " + item.Code);
            if (string.IsNullOrWhiteSpace(item.Meaning)) failures.Add("Missing meaning: " + item.Code);
            if (string.IsNullOrWhiteSpace(item.Action)) failures.Add("Missing action: " + item.Code);
        }
        if (Catalog.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Catalog.Count)
            failures.Add("Duplicate error codes detected");

        Expect(NoraOperation.Connect, new UnauthorizedAccessException("Access is denied"), "NORA-CON-2003");
        Expect(NoraOperation.Connect, new InvalidOperationException("VPN core started, but internet traffic did not pass through it"), "NORA-CON-2010");
        Expect(NoraOperation.Connect, new InvalidOperationException("xray.exe was not found in the NORA VPN cores directory"), "NORA-XRY-3101");
        Expect(NoraOperation.ImportProfile, new InvalidOperationException("Unsupported key. Expected nora1"), "NORA-CFG-8003");
        Expect(NoraOperation.RefreshUsers, new TimeoutException("SSH connection timed out"), "NORA-USR-6005", showToast: false);
        Expect(NoraOperation.InstallServer, new InvalidOperationException("SSH authentication failed"), "NORA-VPS-5002");
        Expect(NoraOperation.UninstallServer, new InvalidOperationException("plink.exe exited with code 1"), "NORA-VPS-5002");
        Expect(NoraOperation.Connect, new InvalidOperationException("Full-tunnel 0.0.0.0/1 route is missing"), "NORA-NET-7003");
        Expect(NoraOperation.Connect, new InvalidOperationException("Wintun adapter session could not be opened"), "NORA-KRT-3004");
        Expect(NoraOperation.Connect, new InvalidOperationException("KRot credential was rejected by server"), "NORA-KRT-3003");
        Expect(NoraOperation.Disconnect, new InvalidOperationException("Backend cleanup failed"), "NORA-CON-2014");
        var redacted = Create("NORA-CFG-8004", NoraOperation.LoadProfile, "PrivateKey=secret-value nora1.secretpayload").ToLogLine();
        if (redacted.Contains("secret-value", StringComparison.Ordinal) || redacted.Contains("secretpayload", StringComparison.Ordinal))
            failures.Add("Structured log redaction exposed credential material");

        foreach (var failure in failures)
            output.WriteLine("FAIL: " + failure);
        if (failures.Count == 0)
            output.WriteLine($"PASS: {Catalog.Count} unique NORA errors and classifier expectations validated");
        return failures.Count == 0 ? 0 : 1;

        void Expect(NoraOperation operation, Exception exception, string expectedCode, bool? showToast = null)
        {
            var actual = Classify(operation, exception);
            if (!actual.Code.Equals(expectedCode, StringComparison.Ordinal))
                failures.Add($"{operation}: expected {expectedCode}, got {actual.Code} for '{exception.Message}'");
            if (showToast.HasValue && actual.ShowToast != showToast.Value)
                failures.Add($"{actual.Code}: expected ShowToast={showToast.Value}, got {actual.ShowToast}");
        }
    }

    private static string ClassifyCode(NoraOperation operation, Exception exception, string m)
    {
        if (operation == NoraOperation.RefreshUsers)
            return "NORA-USR-6005";
        if (operation == NoraOperation.BackendRuntime)
            return "NORA-CON-2011";
        if (operation == NoraOperation.SwitchServer)
            return "NORA-CON-2013";
        if (operation == NoraOperation.ApplicationStart)
        {
            if (Has(m, "dllnotfound", "dll was not found", "presentationnative", "wpfgfx")) return "NORA-APP-1003";
            if (Has(m, "access is denied", "requires elevation", "administrator", "operation was canceled by the user")) return "NORA-APP-1002";
            return "NORA-APP-1001";
        }

        if (operation is NoraOperation.CreateUser or NoraOperation.DeleteUser or NoraOperation.CopyKey)
        {
            if (Has(m, "no stored ssh", "manage users", "self-hosted")) return "NORA-USR-6001";
            if (Has(m, "user name is required", "unsupported characters")) return "NORA-USR-6002";
            if (Has(m, "credential key was not found", "key was not found", "clipboard")) return "NORA-USR-6006";
            return operation switch
            {
                NoraOperation.CreateUser => "NORA-USR-6003",
                NoraOperation.DeleteUser => "NORA-USR-6004",
                _ => "NORA-USR-6006"
            };
        }

        if (operation == NoraOperation.InstallServer)
        {
            if (Has(m, "server ip", "server port", "ssh user is required", "ssh password is required", "required")) return "NORA-VPS-5001";
            if (Has(m, "ssh", "plink", "pscp", "host key", "authentication", "connection refused")) return "NORA-VPS-5002";
            if (Has(m, "reinstall", "already installed")) return "NORA-VPS-5004";
            return "NORA-VPS-5003";
        }

        if (operation == NoraOperation.UninstallServer)
        {
            if (Has(m, "ssh", "plink", "host key", "authentication", "connection refused")) return "NORA-VPS-5002";
            return "NORA-VPS-5005";
        }

        if (operation == NoraOperation.ImportProfile)
        {
            if (Has(m, "unsupported key", "expected nora1", "not recognize")) return "NORA-CFG-8003";
            if (Has(m, "no supported", "contains no", "empty subscription")) return "NORA-SUB-4003";
            if (Has(m, "http", "subscription server returned", "download", "connection refused", "name or service not known")) return "NORA-SUB-4002";
            if (Has(m, "base64", "yaml", "json", "parse", "malformed", "schema")) return "NORA-SUB-4004";
            if (Has(m, "unauthorizedaccess", "disk", "directory", "write", "space")) return "NORA-SUB-4005";
            return "NORA-SUB-4001";
        }

        if (operation == NoraOperation.LoadProfile)
        {
            if (exception is FileNotFoundException || Has(m, "not found", "does not exist", "no longer exists")) return "NORA-CFG-8001";
            if (Has(m, "credential", "secret", "private key")) return "NORA-CFG-8004";
            return "NORA-CFG-8002";
        }

        if (operation == NoraOperation.Diagnostics)
            return "NORA-DIA-9001";

        if (operation is NoraOperation.Connect or NoraOperation.Disconnect)
        {
            if (Has(m, "no backend is available", "unsupported protocol")) return "NORA-CON-2002";
            if (Has(m, "profile", "configuration file no longer exists") && Has(m, "not found", "no longer exists", "missing")) return "NORA-CFG-8001";
            if (exception is UnauthorizedAccessException || Has(m, "access is denied", "permission denied", "administrator rights", "requires elevation")) return "NORA-CON-2003";
            if (Has(m, "xray.exe was not found", "sing-box.exe was not found")) return "NORA-XRY-3101";
            if (Has(m, "amneziawg", "awg.exe") && Has(m, "missing", "not found")) return "NORA-AWG-3202";
            if (Has(m, "wintun")) return "NORA-KRT-3004";
            if (Has(m, "nvp.exe", "core is missing", "dll was not found")) return "NORA-CON-2005";
            if (Has(m, "endpoint bypass", "cannot install endpoint bypass")) return m.Contains("xray") ? "NORA-XRY-3105" : "NORA-CON-2004";
            if (Has(m, "full-tunnel", "route is missing", "route setup", "windows route")) return "NORA-NET-7003";
            if (Has(m, "no active physical", "no ipv4 gateway", "physical gateway")) return "NORA-NET-7005";
            if (Has(m, "internet traffic did not pass", "data-plane", "data plane", "traffic exited through")) return "NORA-CON-2010";
            if (Has(m, "dns", "name_not_resolved", "name not resolved")) return "NORA-CON-2009";
            if (Has(m, "https", "tls probe", "web request")) return "NORA-NET-7002";
            if (Has(m, "krot", "nvp") && Has(m, "credential", "authentication failed", "rejected", "unauthorized")) return "NORA-KRT-3003";
            if (Has(m, "bad credential tag", "bad hello mac")) return "NORA-KRT-3003";
            if (Has(m, "bad client hello", "bad server hello", "tcp cover", "tls authentication")) return "NORA-KRT-3002";
            if (Has(m, "cover channel", "tls cover", "cover transport", "cover service")) return "NORA-KRT-3002";
            if (Has(m, "resume", "reconnect") && Has(m, "rejected", "failed", "expired", "deadline")) return "NORA-KRT-3007";
            if (Has(m, "plaintext and ciphertext", "authentication tag", "read past the end", "encrypted frame", "uplink failed", "downlink failed")) return "NORA-KRT-3006";
            if (Has(m, "handshake", "authentication", "credential", "certificate", "rejected", "unauthorized")) return m.Contains("vless") || m.Contains("reality") || m.Contains("xray") ? "NORA-XRY-3106" : "NORA-CON-2007";
            if (Has(m, "awg backend requires", "[interface]", "[peer]")) return "NORA-AWG-3201";
            if (Has(m, "installtunnelservice", "tunnel service") && Has(m, "exited with code", "install")) return "NORA-AWG-3203";
            if (Has(m, "amneziawg tunnel service did not become ready")) return "NORA-AWG-3204";
            if (Has(m, "xray tun frontend", "sing-box tun frontend")) return "NORA-XRY-3104";
            if (Has(m, "xray") && Has(m, "failed", "exited", "did not become ready", "cannot start")) return "NORA-XRY-3103";
            if (exception is TimeoutException || exception is SocketException || Has(m, "timeout", "timed out", "did not become ready", "unreachable", "no route to host")) return "NORA-CON-2008";
            if (Has(m, "exited before", "cannot start", "failed to start", "panic", "fatal")) return "NORA-CON-2006";
            if (operation == NoraOperation.Disconnect) return "NORA-CON-2014";
        }

        if (operation == NoraOperation.PingServers)
            return "NORA-NET-7004";
        return "NORA-INT-9999";
    }

    private static string ExceptionText(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && !parts.Contains(current.Message, StringComparer.Ordinal))
                parts.Add(current.Message.Trim());
        }
        return string.Join(" | ", parts);
    }

    private static string? FindExplicitCode(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is NoraAppException appException)
                return appException.Code;
        return null;
    }

    private static bool Has(string value, params string[] needles)
        => needles.Any(value.Contains);
}
