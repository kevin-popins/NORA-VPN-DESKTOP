using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Nvp;

internal sealed class NoraSubscriptionInfo
{
    public string Id { get; set; } = "";
    // Local-only position. The provider payload never controls this value, so a
    // subscription refresh cannot undo an order chosen in the NORA UI.
    public int DisplayOrder { get; set; }
    public string Title { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string WebPageUrl { get; set; } = "";
    public string Announce { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public string Hwid { get; set; } = "";
    public long UploadBytes { get; set; }
    public long DownloadBytes { get; set; }
    public long TotalBytes { get; set; }
    public long ExpireUnix { get; set; }
    public int UpdateIntervalHours { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<NoraSubscriptionServer> Servers { get; set; } = [];
}

internal sealed class NoraSubscriptionServer
{
    public string Id { get; set; } = "";
    // Local-only position within its subscription; preserved by Store().
    public int DisplayOrder { get; set; }
    public string SubscriptionId { get; set; } = "";
    public string SubscriptionTitle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "VLESS Reality";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 443;
    public string Country { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string Flow { get; set; } = "";
    public string Network { get; set; } = "tcp";
    public string Security { get; set; } = "reality";
    public string Sni { get; set; } = "";
    public string Fingerprint { get; set; } = "firefox";
    public string PublicKey { get; set; } = "";
    public string ShortId { get; set; } = "";
    public string SpiderX { get; set; } = "";
    public string Encryption { get; set; } = "none";
    public string HeaderType { get; set; } = "none";
    public string HostHeader { get; set; } = "";
    public string Path { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Authority { get; set; } = "";
    public string Alpn { get; set; } = "";
    public bool AllowInsecure { get; set; }
    public string XhttpExtraJson { get; set; } = "";
    public int InboundCount { get; set; }
    public int OutboundCount { get; set; }
    public int RuleCount { get; set; }
    public string RawConfigJson { get; set; } = "";
    public string SourceLink { get; set; } = "";
    public string LocalPath { get; set; } = "";
}

// One bucket of domain/ip matchers pulled from a provider routing rule.
internal sealed class NoraRouteMatchers
{
    public List<string> Domains { get; } = new();        // exact host match
    public List<string> DomainSuffixes { get; } = new(); // host and its subdomains
    public List<string> DomainKeywords { get; } = new(); // substring match
    public List<string> DomainRegexes { get; } = new();  // Go regexp
    public List<string> IpCidrs { get; } = new();        // literal CIDR / IP

    public bool IsEmpty => Domains.Count == 0 && DomainSuffixes.Count == 0 &&
        DomainKeywords.Count == 0 && DomainRegexes.Count == 0 && IpCidrs.Count == 0;
}

// The routing intent baked into a subscription/config: which traffic bypasses the
// tunnel (Direct), which is force-proxied, and which is blocked. Translated into
// sing-box route rules so the app honors the provider's split instead of forcing
// a full tunnel.
internal sealed class NoraRoutePolicy
{
    public NoraRouteMatchers Direct { get; } = new();
    public NoraRouteMatchers Proxy { get; } = new();
    public NoraRouteMatchers Block { get; } = new();
    public bool BlockBittorrent { get; set; }
    public int SkippedRuleSets { get; set; } // geosite:/geoip: entries we cannot honor without rule-set files

    public bool HasRules => !Direct.IsEmpty || !Proxy.IsEmpty || !Block.IsEmpty || BlockBittorrent;

    public int DirectMatcherCount => Direct.Domains.Count + Direct.DomainSuffixes.Count +
        Direct.DomainKeywords.Count + Direct.DomainRegexes.Count + Direct.IpCidrs.Count;
}

internal sealed class NoraExternalProfileInfo
{
    public string Protocol { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Country { get; set; } = "";
}

internal static class NoraSubscriptionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static string Root
    {
        get
        {
            var root = Path.Combine(NoraAppState.DataRoot, "subscriptions");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static async Task<NoraSubscriptionInfo> ImportAsync(string input, Action<string> log)
    {
        input = input.Trim();
        if (HappCrypt5Decoder.IsCrypt5Link(input))
        {
            var decrypted = HappCrypt5Decoder.Decrypt(input);
            log("Encrypted HAPP subscription unlocked.");
            if (Uri.TryCreate(decrypted, UriKind.Absolute, out var decryptedUri) && decryptedUri.Scheme is "http" or "https")
                return await ImportUrlAsync(decryptedUri, log, input);
            var parsed = ParsePayload(decrypted, input, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), log);
            return Store(parsed, input);
        }
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return await ImportUrlAsync(uri, log);
        return Store(ParsePayload(input, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), log), input);
    }

    public static async Task<NoraSubscriptionInfo> RefreshAsync(NoraSubscriptionInfo subscription, Action<string> log)
    {
        var source = subscription.SourceUrl.Trim();
        if (HappCrypt5Decoder.IsCrypt5Link(source))
        {
            var decrypted = HappCrypt5Decoder.Decrypt(source);
            if (Uri.TryCreate(decrypted, UriKind.Absolute, out var decryptedUri) && decryptedUri.Scheme is "http" or "https")
                return await ImportUrlAsync(decryptedUri, log, source);
            return Store(ParsePayload(decrypted, source, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), log), source);
        }
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new NoraAppException("NORA-SUB-4002", "This subscription was imported without a refresh URL.");
        return await ImportUrlAsync(uri, log);
    }

    public static IReadOnlyList<NoraSubscriptionInfo> LoadAll()
    {
        if (!Directory.Exists(Root))
            return [];

        var subscriptions = new List<NoraSubscriptionInfo>();
        foreach (var path in Directory.GetFiles(Root, "subscription.json", SearchOption.AllDirectories))
        {
            NoraSubscriptionInfo? sub = null;
            try { sub = JsonSerializer.Deserialize<NoraSubscriptionInfo>(File.ReadAllText(path), JsonOptions); }
            catch { }
            if (sub is null)
                continue;
            var dir = Path.GetDirectoryName(path) ?? Root;
            foreach (var server in sub.Servers)
            {
                NormalizeServerPresentation(server);
                server.LocalPath = ServerPath(dir, server.Id);
            }
            sub.Servers = OrderServers(sub.Servers).ToList();
            subscriptions.Add(sub);
        }
        return OrderSubscriptions(subscriptions).ToArray();
    }

    public static bool TryLoadServer(string path, out NoraSubscriptionServer server)
    {
        server = new NoraSubscriptionServer();
        try
        {
            if (!File.Exists(path))
                return false;
            server = JsonSerializer.Deserialize<NoraSubscriptionServer>(File.ReadAllText(path), JsonOptions) ?? new NoraSubscriptionServer();
            NormalizeServerPresentation(server);
            server.LocalPath = path;
            return !string.IsNullOrWhiteSpace(server.Host);
        }
        catch
        {
            return false;
        }
    }

    public static bool DeleteSubscription(NoraSubscriptionInfo subscription)
    {
        if (string.IsNullOrWhiteSpace(subscription.Id))
            return false;
        var dir = Path.Combine(Root, subscription.Id);
        EnsureUnderRoot(dir);
        if (!Directory.Exists(dir))
            return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    public static bool DeleteServer(NoraSubscriptionServer server)
    {
        if (string.IsNullOrWhiteSpace(server.LocalPath))
            return false;
        var serverPath = Path.GetFullPath(server.LocalPath);
        EnsureUnderRoot(serverPath);
        var dir = Path.GetDirectoryName(serverPath);
        if (string.IsNullOrWhiteSpace(dir))
            return false;
        var subscriptionPath = Path.Combine(dir, "subscription.json");
        if (!File.Exists(subscriptionPath))
        {
            if (File.Exists(serverPath))
                File.Delete(serverPath);
            return true;
        }

        var subscription = JsonSerializer.Deserialize<NoraSubscriptionInfo>(File.ReadAllText(subscriptionPath), JsonOptions) ?? new NoraSubscriptionInfo();
        var before = subscription.Servers.Count;
        subscription.Servers.RemoveAll(x =>
            string.Equals(x.Id, server.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ServerPath(dir, x.Id), serverPath, StringComparison.OrdinalIgnoreCase));

        if (File.Exists(serverPath))
            File.Delete(serverPath);

        if (subscription.Servers.Count == 0)
        {
            Directory.Delete(dir, recursive: true);
            return before > 0;
        }

        foreach (var item in subscription.Servers)
        {
            item.SubscriptionId = subscription.Id;
            item.SubscriptionTitle = subscription.Title;
            item.LocalPath = ServerPath(dir, item.Id);
        }
        File.WriteAllText(subscriptionPath, JsonSerializer.Serialize(subscription, JsonOptions));
        return before != subscription.Servers.Count;
    }

    private static void EnsureUnderRoot(string path)
    {
        var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new NoraAppException("NORA-SUB-4005", "Refusing to modify a subscription file outside the NORA subscription store.");
    }

    public static string GetDeviceHwid()
    {
        var raw = "";
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                raw = key?.GetValue("MachineGuid")?.ToString() ?? "";
            }
            catch { }
        }
        if (string.IsNullOrWhiteSpace(raw))
            raw = Environment.MachineName + "|" + Environment.UserName;
        return BuildDeviceHwid(raw);
    }

    internal static string BuildDeviceHwid(string machineIdentity)
    {
        var identity = machineIdentity.Trim();
        if (identity.Length == 0)
            identity = "fallback:" + Environment.MachineName + "|" + Environment.UserName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("NORA VPN/HWID/v1\0" + identity));
        return "NORAvpn-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    internal static string GetDeviceDisplayName()
    {
        var name = Environment.MachineName.Trim();
        if (name.Length == 0)
            name = "Windows PC";
        return name.Length <= 64 ? name : name[..64];
    }

    internal static void AddSubscriptionIdentityHeaders(
        HttpRequestMessage request,
        string userAgent,
        string? hwid = null,
        string? deviceName = null)
    {
        hwid ??= GetDeviceHwid();
        deviceName ??= GetDeviceDisplayName();
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("X-HWID", hwid);
        request.Headers.TryAddWithoutValidation("X-Device-ID", hwid);
        request.Headers.TryAddWithoutValidation("Device-ID", hwid);
        request.Headers.TryAddWithoutValidation("HWID", hwid);
        request.Headers.TryAddWithoutValidation("X-Nora-HWID", hwid);
        request.Headers.TryAddWithoutValidation("X-Device-Name", deviceName);
        request.Headers.TryAddWithoutValidation("Device-Name", deviceName);
        request.Headers.TryAddWithoutValidation("X-Device-Model", deviceName);
        request.Headers.TryAddWithoutValidation("X-Device-OS", "Windows");
        request.Headers.TryAddWithoutValidation("X-Client-Name", "NORAvpn");
        request.Headers.TryAddWithoutValidation("Client-Name", "NORAvpn");
        request.Headers.TryAddWithoutValidation("X-Client-Version", "1.0");
        request.Headers.TryAddWithoutValidation("Profile-Update-Interval", "24");
    }

    public static string SingBoxPath()
        => Path.Combine(AppContext.BaseDirectory, "cores", "sing-box.exe");

    public static string XrayPath()
        => Path.Combine(AppContext.BaseDirectory, "cores", "xray.exe");

    public static IReadOnlyList<string> MissingVlessCorePaths()
        => new[] { XrayPath(), SingBoxPath() }.Where(path => !File.Exists(path)).ToArray();

    public static string AmneziaWgPath()
        => Path.Combine(AppContext.BaseDirectory, "cores", "amneziawg.exe");

    public static string AmneziaWgCliPath()
        => Path.Combine(AppContext.BaseDirectory, "cores", "awg.exe");

    public static string BuildXrayConfig(
        NoraSubscriptionServer server,
        int socksPort = 20808,
        bool nativeTun = false,
        IReadOnlyCollection<IPAddress>? endpointAddresses = null)
    {
        var proxyOutbound = ExtractVlessOutbound(server.RawConfigJson) ?? BuildXrayVlessOutbound(server);
        proxyOutbound["tag"] = "proxy";
        RemoveLocalInterfaceBindings(proxyOutbound);
        var dns = new JsonObject
        {
            ["servers"] = new JsonArray("1.1.1.1", "8.8.8.8")
        };
        // The selected VLESS hostname can be a provider load balancer. Resolve it
        // once before TUN routing is enabled and make Xray use that exact same pool.
        // Otherwise Windows can protect the system-DNS answers while Xray receives a
        // different rotating answer from its own resolver and dials back into TUN.
        var endpointIps = endpointAddresses?
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToArray() ?? [];
        if (endpointIps.Length > 0 && !IPAddress.TryParse(server.Host, out _))
        {
            dns["hosts"] = new JsonObject
            {
                [server.Host] = StringArray(endpointIps.Select(address => address.ToString()))
            };
            // `AsIs` delegates the proxy hostname to Go's system dialer and ignores
            // dns.hosts. Force Xray's built-in IPv4 resolver instead: it will use the
            // pinned hosts pool above and can never fall back to an unprotected DNS
            // answer after the full-tunnel routes are active.
            if (proxyOutbound["streamSettings"] is JsonObject stream)
            {
                var sockopt = stream["sockopt"] as JsonObject ?? new JsonObject();
                sockopt["domainStrategy"] = "ForceIPv4";
                stream["sockopt"] = sockopt;
            }
        }
        var root = new JsonObject
        {
            ["outbounds"] = new JsonArray
            {
                proxyOutbound,
                new JsonObject { ["protocol"] = "freedom", ["tag"] = "direct" },
                new JsonObject { ["protocol"] = "blackhole", ["tag"] = "block" }
            },
            ["dns"] = dns,
            ["routing"] = new JsonObject
            {
                ["domainStrategy"] = "IPIfNonMatch",
                ["rules"] = new JsonArray()
            }
        };
        root["log"] = new JsonObject { ["loglevel"] = "info" };
        root["inbounds"] = nativeTun
            ? new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "tun-in",
                    ["protocol"] = "tun",
                    ["settings"] = new JsonObject
                    {
                        ["name"] = "NORA-Xray",
                        ["mtu"] = 1400,
                        ["gateway"] = new JsonArray("172.20.0.1/30"),
                        ["dns"] = new JsonArray("1.1.1.1", "8.8.8.8"),
                        ["userLevel"] = 0,
                        ["autoSystemRoutingTable"] = new JsonArray("0.0.0.0/1", "128.0.0.0/1")
                    },
                    ["sniffing"] = new JsonObject
                    {
                        ["enabled"] = true,
                        ["destOverride"] = new JsonArray("http", "tls", "quic")
                    }
                }
            }
            : new JsonArray
        {
            new JsonObject
            {
                ["tag"] = "socks",
                ["listen"] = "127.0.0.1",
                ["port"] = socksPort,
                ["protocol"] = "socks",
                ["settings"] = new JsonObject { ["udp"] = true }
            }
        };
        return root.ToJsonString(JsonOptions);
    }

    private static void RemoveLocalInterfaceBindings(JsonObject outbound)
    {
        if (!string.Equals(outbound["protocol"]?.GetValue<string>(), "vless", StringComparison.OrdinalIgnoreCase))
            return;
        if (outbound["streamSettings"] is not JsonObject stream)
            return;
        if (stream["sockopt"] is not JsonObject sockopt)
            return;
        sockopt.Remove("interface");
        sockopt.Remove("dialerProxy");
        if (sockopt.Count == 0)
            stream.Remove("sockopt");
    }

    public static string ExternalProfileMetadataPath(string profilePath) => profilePath + ".meta.json";

    public static bool TryReadExternalProfileInfo(string profilePath, out NoraExternalProfileInfo info)
    {
        info = new NoraExternalProfileInfo();
        try
        {
            var metadataPath = ExternalProfileMetadataPath(profilePath);
            if (!File.Exists(metadataPath))
                return false;
            info = JsonSerializer.Deserialize<NoraExternalProfileInfo>(File.ReadAllText(metadataPath), JsonOptions) ?? new NoraExternalProfileInfo();
            return !string.IsNullOrWhiteSpace(info.Protocol) && !string.IsNullOrWhiteSpace(info.Host);
        }
        catch
        {
            info = new NoraExternalProfileInfo();
            return false;
        }
    }

    public static void WriteExternalProfileInfo(string profilePath, NoraExternalProfileInfo info)
        => File.WriteAllText(ExternalProfileMetadataPath(profilePath), JsonSerializer.Serialize(info, JsonOptions));

    public static bool TryParseExternalProfile(string text, string profilePath, string protocolHint, out NoraExternalProfileInfo info)
    {
        info = new NoraExternalProfileInfo();
        if (text.Contains("[Interface]", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("[Peer]", StringComparison.OrdinalIgnoreCase))
            return TryParseAwgProfile(text, profilePath, protocolHint, out info);

        if (text.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseVlessUri(text, "Direct profiles", out var server))
                return false;
            info = new NoraExternalProfileInfo
            {
                Protocol = server.Protocol,
                Name = server.Name,
                Host = server.Host,
                Port = server.Port,
                Country = server.Country
            };
            return true;
        }
        return false;
    }

    public static async Task<NoraExternalProfileInfo> EnrichExternalProfileAsync(NoraExternalProfileInfo info)
    {
        if (NeedsCountryLookup(info.Country))
        {
            var resolved = await ResolveCountryForHostAsync(info.Host);
            if (!string.IsNullOrWhiteSpace(resolved))
                info.Country = resolved;
        }
        if (string.IsNullOrWhiteSpace(info.Name) || LooksGeneratedExternalName(info.Name))
            info.Name = BuildExternalDisplayName(info);
        return info;
    }

    private static bool TryParseAwgProfile(string text, string profilePath, string protocolHint, out NoraExternalProfileInfo info)
    {
        info = new NoraExternalProfileInfo();
        var endpoint = Regex.Match(text, @"(?im)^\s*Endpoint\s*=\s*(?:\[(?<ipv6>[^\]]+)\]|(?<host>[^:\s]+)):(?<port>\d+)\s*$");
        if (!endpoint.Success)
            return false;
        var host = endpoint.Groups["ipv6"].Success ? endpoint.Groups["ipv6"].Value : endpoint.Groups["host"].Value;
        var port = int.TryParse(endpoint.Groups["port"].Value, out var parsedPort) ? parsedPort : 51820;
        var baseName = Path.GetFileNameWithoutExtension(profilePath);
        var protocol = text.Contains("Jc =", StringComparison.OrdinalIgnoreCase) ||
                       text.Contains("H1 =", StringComparison.OrdinalIgnoreCase) ||
                       protocolHint.Contains("AWG", StringComparison.OrdinalIgnoreCase)
            ? "AWG 2.0"
            : "WireGuard";
        var country = GuessCountry(baseName, host);
        info = new NoraExternalProfileInfo
        {
            Protocol = protocol,
            Name = baseName,
            Host = host,
            Port = port,
            Country = country
        };
        info.Name = BuildExternalDisplayName(info);
        return true;
    }

    private static bool LooksGeneratedExternalName(string value)
        => Regex.IsMatch(value, @"^(awg|wireguard|xray|vless)[-_ ]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string BuildExternalDisplayName(NoraExternalProfileInfo info)
    {
        var location = KnownLocationForHost(info.Host);
        var place = !string.IsNullOrWhiteSpace(location.City) ? location.City :
            !NeedsCountryLookup(info.Country) ? info.Country : "";
        if (!string.IsNullOrWhiteSpace(place))
            return info.Protocol.Contains("AWG", StringComparison.OrdinalIgnoreCase) ? place + " AWG" : place + " " + info.Protocol;
        return info.Protocol + " profile";
    }

    private static JsonObject? ExtractVlessOutbound(string rawConfigJson)
    {
        if (string.IsNullOrWhiteSpace(rawConfigJson))
            return null;
        try
        {
            var source = JsonNode.Parse(rawConfigJson)?.AsObject();
            var outbound = source?["outbounds"]?.AsArray()
                .OfType<JsonObject>()
                .FirstOrDefault(x => string.Equals(x["protocol"]?.GetValue<string>(), "vless", StringComparison.OrdinalIgnoreCase));
            return outbound?.DeepClone().AsObject();
        }
        catch
        {
            return null;
        }
    }

    // Reads the routing intent baked into a provider Xray config (the `routing.rules`
    // block). Returns null when there are no usable split rules, so the caller keeps
    // the plain full-tunnel behavior.
    public static NoraRoutePolicy? ParseXrayRoutingPolicy(string rawConfigJson)
    {
        if (string.IsNullOrWhiteSpace(rawConfigJson))
            return null;
        try
        {
            var root = JsonNode.Parse(rawConfigJson)?.AsObject();
            if (root?["routing"]?["rules"] is not JsonArray rules)
                return null;
            var policy = new NoraRoutePolicy();
            foreach (var node in rules)
            {
                if (node is not JsonObject rule)
                    continue;
                var tag = (rule["outboundTag"]?.GetValue<string>() ?? "").Trim();
                var bucket = ClassifyOutboundTag(tag);
                if (bucket is null && !HasProtocol(rule, "bittorrent"))
                    continue;

                if (HasProtocol(rule, "bittorrent") && bucket != NoraRouteBucket.Proxy)
                    policy.BlockBittorrent = true;

                var target = bucket switch
                {
                    NoraRouteBucket.Direct => policy.Direct,
                    NoraRouteBucket.Block => policy.Block,
                    NoraRouteBucket.Proxy => policy.Proxy,
                    _ => null
                };
                if (target is null)
                    continue;
                if (rule["domain"] is JsonArray domains)
                    foreach (var d in domains)
                        ClassifyDomainToken(d?.GetValue<string>(), target, policy);
                if (rule["ip"] is JsonArray ips)
                    foreach (var ip in ips)
                        ClassifyIpToken(ip?.GetValue<string>(), target, policy);
            }
            return policy.HasRules ? policy : null;
        }
        catch
        {
            return null;
        }
    }

    private enum NoraRouteBucket { Direct, Proxy, Block }

    private static NoraRouteBucket? ClassifyOutboundTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var t = tag.ToLowerInvariant();
        if (t.Contains("direct") || t.Contains("freedom") || t.Contains("bypass"))
            return NoraRouteBucket.Direct;
        if (t.Contains("block") || t.Contains("reject") || t.Contains("blackhole") || t.Contains("ad"))
            return NoraRouteBucket.Block;
        if (t.Contains("proxy") || t.Contains("vless") || t.Contains("vpn") || t.Contains("select"))
            return NoraRouteBucket.Proxy;
        return null;
    }

    private static bool HasProtocol(JsonObject rule, string name)
        => rule["protocol"] is JsonArray protocols &&
           protocols.Any(p => string.Equals(p?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));

    private static void ClassifyDomainToken(string? token, NoraRouteMatchers matchers, NoraRoutePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;
        token = token.Trim();
        if (token.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase))
            matchers.DomainRegexes.Add(token[7..]);
        else if (token.StartsWith("full:", StringComparison.OrdinalIgnoreCase))
            matchers.Domains.Add(token[5..].ToLowerInvariant());
        else if (token.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase))
            matchers.DomainKeywords.Add(token[8..].ToLowerInvariant());
        else if (token.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
        {
            var host = token[7..].ToLowerInvariant();
            matchers.Domains.Add(host);            // Xray domain: matches the host itself
            matchers.DomainSuffixes.Add("." + host); // and every subdomain
        }
        else if (token.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))
            policy.SkippedRuleSets++;              // needs a rule-set file we do not ship
        else
            matchers.DomainKeywords.Add(token.ToLowerInvariant()); // bare Xray token = substring
    }

    private static readonly string[] PrivateCidrs =
    {
        "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8",
        "169.254.0.0/16", "::1/128", "fc00::/7", "fe80::/10"
    };

    private static void ClassifyIpToken(string? token, NoraRouteMatchers matchers, NoraRoutePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;
        token = token.Trim();
        if (token.StartsWith("geoip:private", StringComparison.OrdinalIgnoreCase))
            matchers.IpCidrs.AddRange(PrivateCidrs);
        else if (token.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
            policy.SkippedRuleSets++;
        else
            matchers.IpCidrs.Add(token);
    }

    private static JsonArray StringArray(IEnumerable<string> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
            array.Add(item);
        return array;
    }

    private static void AddMatchers(JsonObject rule, NoraRouteMatchers matchers)
    {
        if (matchers.Domains.Count > 0)
            rule["domain"] = StringArray(matchers.Domains.Distinct());
        if (matchers.DomainSuffixes.Count > 0)
            rule["domain_suffix"] = StringArray(matchers.DomainSuffixes.Distinct());
        if (matchers.DomainKeywords.Count > 0)
            rule["domain_keyword"] = StringArray(matchers.DomainKeywords.Distinct());
        if (matchers.DomainRegexes.Count > 0)
            rule["domain_regex"] = StringArray(matchers.DomainRegexes.Distinct());
        if (matchers.IpCidrs.Count > 0)
            rule["ip_cidr"] = StringArray(matchers.IpCidrs.Distinct());
    }

    // Emits sing-box route rules for a provider policy: block first, then direct
    // (bypass tunnel via the local `direct` outbound), then explicit proxy. The
    // caller keeps `final=proxy`, so anything unmatched still goes through the VPN.
    private static void AppendPolicyRouteRules(JsonArray rules, NoraRoutePolicy policy)
    {
        if (policy.BlockBittorrent)
            rules.Add(new JsonObject { ["protocol"] = new JsonArray("bittorrent"), ["action"] = "reject" });
        if (!policy.Block.IsEmpty)
        {
            var rule = new JsonObject();
            AddMatchers(rule, policy.Block);
            rule["action"] = "reject";
            rules.Add(rule);
        }
        if (!policy.Direct.IsEmpty)
        {
            var rule = new JsonObject();
            AddMatchers(rule, policy.Direct);
            rule["action"] = "route";
            rule["outbound"] = "direct";
            rules.Add(rule);
        }
        if (!policy.Proxy.IsEmpty)
        {
            var rule = new JsonObject();
            AddMatchers(rule, policy.Proxy);
            rule["action"] = "route";
            rule["outbound"] = "proxy";
            rules.Add(rule);
        }
    }

    // Direct domains should resolve through the local resolver so their answers are
    // the same region-specific IPs a normal Russian client would get, matching the
    // provider's DNS split intent.
    private static void AppendPolicyDnsRules(JsonArray dnsRules, NoraRoutePolicy policy)
    {
        var direct = policy.Direct;
        if (direct.Domains.Count == 0 && direct.DomainSuffixes.Count == 0 &&
            direct.DomainKeywords.Count == 0 && direct.DomainRegexes.Count == 0)
            return;
        var rule = new JsonObject();
        if (direct.Domains.Count > 0)
            rule["domain"] = StringArray(direct.Domains.Distinct());
        if (direct.DomainSuffixes.Count > 0)
            rule["domain_suffix"] = StringArray(direct.DomainSuffixes.Distinct());
        if (direct.DomainKeywords.Count > 0)
            rule["domain_keyword"] = StringArray(direct.DomainKeywords.Distinct());
        if (direct.DomainRegexes.Count > 0)
            rule["domain_regex"] = StringArray(direct.DomainRegexes.Distinct());
        rule["server"] = "local";
        dnsRules.Add(rule);
    }

    public static string BuildXrayTunFrontendConfig(int socksPort, NoraSubscriptionServer? server = null)
    {
        var policy = server is null ? null : ParseXrayRoutingPolicy(server.RawConfigJson);

        var dns = new JsonObject
        {
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "tls",
                    ["tag"] = "remote",
                    ["server"] = "1.1.1.1",
                    ["server_port"] = 853,
                    ["detour"] = "proxy"
                },
                new JsonObject { ["type"] = "local", ["tag"] = "local" }
            },
            ["final"] = "remote"
        };
        if (policy is not null)
        {
            var dnsRules = new JsonArray();
            AppendPolicyDnsRules(dnsRules, policy);
            if (dnsRules.Count > 0)
                dns["rules"] = dnsRules;
        }

        // Sniff TLS SNI / HTTP host first so the provider's domain rules can match,
        // then hand DNS to sing-box, then apply the split (direct traffic leaves through
        // the local `direct` outbound and bypasses the tunnel), then default to proxy.
        var routeRules = new JsonArray
        {
            new JsonObject { ["action"] = "sniff" },
            new JsonObject { ["port"] = 53, ["action"] = "hijack-dns" },
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" }
        };
        if (policy is not null)
            AppendPolicyRouteRules(routeRules, policy);

        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info", ["timestamp"] = true },
            ["dns"] = dns,
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["interface_name"] = "NORA-Xray",
                    ["address"] = new JsonArray("172.20.0.1/30"),
                    ["mtu"] = 1400,
                    ["stack"] = "mixed",
                    ["auto_route"] = true,
                    ["strict_route"] = true,
                    // More-specific routes win over default routes left by other VPN clients.
                    ["route_address"] = new JsonArray("0.0.0.0/1", "128.0.0.0/1")
                }
            },
            ["outbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "socks",
                    ["tag"] = "proxy",
                    ["server"] = "127.0.0.1",
                    ["server_port"] = socksPort
                },
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" }
            },
            ["route"] = new JsonObject
            {
                ["rules"] = routeRules,
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = "local",
                ["final"] = "proxy"
            }
        };
        return root.ToJsonString(JsonOptions);
    }

    private static JsonObject BuildXrayVlessOutbound(NoraSubscriptionServer server)
    {
        var stream = BuildXrayStreamSettings(server);

        return new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "vless",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = server.Host,
                        ["port"] = server.Port,
                        ["users"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = server.Uuid,
                                ["encryption"] = string.IsNullOrWhiteSpace(server.Encryption) ? "none" : server.Encryption,
                                ["flow"] = server.Flow
                            }
                        }
                    }
                }
            },
            ["streamSettings"] = stream
        };
    }

    private static JsonObject BuildXrayStreamSettings(NoraSubscriptionServer server)
    {
        var network = NormalizeXrayNetwork(server.Network);
        var security = string.IsNullOrWhiteSpace(server.Security) ? "none" : server.Security.ToLowerInvariant();
        var stream = new JsonObject { ["network"] = network, ["security"] = security };

        if (security == "reality")
        {
            var reality = new JsonObject
            {
                ["serverName"] = server.Sni,
                ["fingerprint"] = string.IsNullOrWhiteSpace(server.Fingerprint) ? "firefox" : server.Fingerprint,
                ["publicKey"] = server.PublicKey,
                ["shortId"] = server.ShortId
            };
            if (!string.IsNullOrWhiteSpace(server.SpiderX))
                reality["spiderX"] = server.SpiderX;
            stream["realitySettings"] = reality;
        }
        else if (security == "tls")
        {
            var tls = new JsonObject
            {
                ["serverName"] = server.Sni,
                ["fingerprint"] = string.IsNullOrWhiteSpace(server.Fingerprint) ? "chrome" : server.Fingerprint,
                ["allowInsecure"] = server.AllowInsecure
            };
            var alpn = SplitCsv(server.Alpn);
            if (alpn.Length > 0)
                tls["alpn"] = new JsonArray(alpn.Select(x => JsonValue.Create(x)).ToArray());
            stream["tlsSettings"] = tls;
        }

        var hostHeaders = SplitCsv(server.HostHeader);
        switch (network)
        {
            case "ws":
                var wsHeaders = new JsonObject();
                if (hostHeaders.Length > 0) wsHeaders["Host"] = hostHeaders[0];
                stream["wsSettings"] = new JsonObject
                {
                    ["path"] = EmptyAs(server.Path, "/"),
                    ["headers"] = wsHeaders
                };
                break;
            case "grpc":
                stream["grpcSettings"] = new JsonObject
                {
                    ["serviceName"] = server.ServiceName,
                    ["authority"] = server.Authority
                };
                break;
            case "httpupgrade":
                var huHeaders = new JsonObject();
                if (hostHeaders.Length > 0) huHeaders["Host"] = hostHeaders[0];
                stream["httpupgradeSettings"] = new JsonObject
                {
                    ["path"] = EmptyAs(server.Path, "/"),
                    ["host"] = hostHeaders.FirstOrDefault() ?? "",
                    ["headers"] = huHeaders
                };
                break;
            case "xhttp":
                var xhttp = new JsonObject
                {
                    ["path"] = EmptyAs(server.Path, "/"),
                    ["host"] = hostHeaders.FirstOrDefault() ?? "",
                    ["mode"] = EmptyAs(server.Mode, "auto")
                };
                MergeJsonObject(xhttp, server.XhttpExtraJson);
                stream["xhttpSettings"] = xhttp;
                break;
            case "raw":
            case "tcp":
                if (!string.IsNullOrWhiteSpace(server.HeaderType) && !server.HeaderType.Equals("none", StringComparison.OrdinalIgnoreCase))
                    stream["rawSettings"] = new JsonObject { ["header"] = new JsonObject { ["type"] = server.HeaderType } };
                break;
        }
        return stream;
    }

    private static string NormalizeXrayNetwork(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "tcp" : value.Trim().ToLowerInvariant();
        return value switch
        {
            "splithttp" => "xhttp",
            "httpupgrade" or "http-upgrade" => "httpupgrade",
            _ => value
        };
    }

    private static string[] SplitCsv(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string EmptyAs(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static void MergeJsonObject(JsonObject target, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject source)
                return;
            foreach (var pair in source)
                target[pair.Key] = pair.Value?.DeepClone();
        }
        catch
        {
        }
    }

    public static string BuildSingBoxConfig(NoraSubscriptionServer server)
    {
        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info", ["timestamp"] = true },
            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tls",
                        ["tag"] = "remote",
                        ["server"] = "1.1.1.1",
                        ["server_port"] = 853,
                        ["detour"] = "proxy"
                    },
                    new JsonObject { ["type"] = "local", ["tag"] = "local" }
                },
                ["final"] = "remote"
            },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["interface_name"] = "NORA",
                    ["address"] = new JsonArray("172.19.0.1/30"),
                    ["auto_route"] = true,
                    ["strict_route"] = true
                }
            },
            ["outbounds"] = new JsonArray
            {
                BuildVlessOutbound(server),
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" },
                new JsonObject { ["type"] = "block", ["tag"] = "block" }
            },
            ["route"] = new JsonObject
            {
                ["rules"] = new JsonArray
                {
                    new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" }
                },
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = "local",
                ["final"] = "proxy"
            }
        };
        return root.ToJsonString(JsonOptions);
    }

    private static JsonObject BuildVlessOutbound(NoraSubscriptionServer server)
    {
        var reality = new JsonObject
        {
            ["enabled"] = true,
            ["public_key"] = server.PublicKey,
            ["short_id"] = server.ShortId
        };
        if (!string.IsNullOrWhiteSpace(server.SpiderX))
            reality["spider_x"] = server.SpiderX;

        var tls = new JsonObject
        {
            ["enabled"] = true,
            ["server_name"] = server.Sni,
            ["utls"] = new JsonObject
            {
                ["enabled"] = true,
                ["fingerprint"] = string.IsNullOrWhiteSpace(server.Fingerprint) ? "firefox" : server.Fingerprint
            },
            ["reality"] = reality
        };

        var outbound = new JsonObject
        {
            ["type"] = "vless",
            ["tag"] = "proxy",
            ["server"] = server.Host,
            ["server_port"] = server.Port,
            ["uuid"] = server.Uuid,
            ["network"] = string.IsNullOrWhiteSpace(server.Network) ? "tcp" : server.Network,
            ["tls"] = tls
        };
        if (!string.IsNullOrWhiteSpace(server.Flow))
            outbound["flow"] = server.Flow;
        return outbound;
    }

    // Client identities tried in order when fetching a subscription. Many panels only
    // include the provider's routing rules (as an Xray/sing-box config) when the client
    // is a sing-box/Happ-family app; a plain browser/NORA identity often gets only a raw
    // node list with no routing. We therefore prefer a routing-capable identity and fall
    // back to the NORA one for panels that key their response off it.
    private static readonly string[] SubscriptionUserAgents =
    {
        "NORAvpn/1.0",
        "Happ/1.10.1"
    };

    private static async Task<NoraSubscriptionInfo> ImportUrlAsync(
        Uri uri,
        Action<string> log,
        string? sourceReference = null,
        int redirectDepth = 0)
    {
        if (redirectDepth > 3)
            throw new NoraAppException("NORA-SUB-4002", "The encrypted subscription contains too many nested links.");
        var hwid = GetDeviceHwid();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };

        NoraSubscriptionInfo? parsed = null;
        var lastError = "no response";
        foreach (var userAgent in SubscriptionUserAgents)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            AddSubscriptionIdentityHeaders(request, userAgent, hwid);

            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                lastError = $"{(int)response.StatusCode}: {TrimForError(body)}";
                log($"Subscription fetch as {userAgent} returned {(int)response.StatusCode}; trying next client identity.");
                continue;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in response.Headers)
                headers[pair.Key] = string.Join(",", pair.Value);
            foreach (var pair in response.Content.Headers)
                headers[pair.Key] = string.Join(",", pair.Value);

            if (HappCrypt5Decoder.IsCrypt5Link(body))
            {
                body = HappCrypt5Decoder.Decrypt(body);
                log("Encrypted HAPP response unlocked.");
                if (Uri.TryCreate(body, UriKind.Absolute, out var nestedUri) && nestedUri.Scheme is "http" or "https")
                    return await ImportUrlAsync(nestedUri, log, sourceReference ?? uri.ToString(), redirectDepth + 1);
            }

            var storedSource = sourceReference ?? uri.ToString();
            var candidate = ParsePayload(body, storedSource, headers, log);
            candidate.Hwid = hwid;
            candidate.UserAgent = userAgent;
            var withRules = candidate.Servers.Count(s => !string.IsNullOrEmpty(s.RawConfigJson));
            if (candidate.Servers.Count > 0)
            {
                if (withRules > 0)
                {
                    parsed = candidate;
                    log($"Subscription accepted as {userAgent}: {candidate.Servers.Count} nodes, {withRules} carry routing rules.");
                    break;
                }
                // A node list with no routing yet: keep it, but keep probing for a
                // routing-bearing response from another identity.
                parsed ??= candidate;
            }
        }

        if (parsed is null)
            throw new NoraAppException("NORA-SUB-4002", $"Subscription server did not return a usable payload ({lastError})");

        await ResolveMissingCountriesAsync(parsed, log);
        return Store(parsed, sourceReference ?? uri.ToString());
    }

    private static async Task ResolveMissingCountriesAsync(NoraSubscriptionInfo sub, Action<string> log)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        foreach (var server in sub.Servers)
        {
            if (!NeedsCountryLookup(server.Country))
                continue;
            var resolved = await ResolveCountryAsync(http, server.Host);
            if (string.IsNullOrWhiteSpace(resolved))
                continue;
            server.Country = resolved;
            log($"Resolved {server.Host} country as {resolved}");
        }
    }

    private static bool NeedsCountryLookup(string country)
        => string.IsNullOrWhiteSpace(country) || country.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ResolveCountryAsync(HttpClient http, string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "";

        var known = KnownLocationForHost(host);
        if (!string.IsNullOrWhiteSpace(known.Country))
            return known.Country;

        foreach (var url in new[]
        {
            $"https://ipwho.is/{Uri.EscapeDataString(host)}",
            $"https://ipapi.co/{Uri.EscapeDataString(host)}/json/",
            $"http://ip-api.com/json/{Uri.EscapeDataString(host)}?fields=status,country,countryCode"
        })
        {
            try
            {
                using var response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    continue;
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                var country = First(Str(root, "country_code"), Str(root, "countryCode"), Str(root, "country"), Str(root, "country_name"));
                if (!string.IsNullOrWhiteSpace(country) && !country.Equals("reserved", StringComparison.OrdinalIgnoreCase))
                    return CanonicalCountry(country);
            }
            catch
            {
            }
        }
        return "";
    }

    public static async Task<string> ResolveCountryForHostAsync(string host)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        return await ResolveCountryAsync(http, host);
    }

    private static NoraSubscriptionInfo Store(NoraSubscriptionInfo sub, string source)
    {
        if (string.IsNullOrWhiteSpace(sub.Id))
            sub.Id = StableId(source + "|" + sub.Title);
        var dir = Path.Combine(Root, sub.Id);
        Directory.CreateDirectory(dir);

        var subscriptionPath = Path.Combine(dir, "subscription.json");
        NoraSubscriptionInfo? previous = null;
        try
        {
            if (File.Exists(subscriptionPath))
                previous = JsonSerializer.Deserialize<NoraSubscriptionInfo>(File.ReadAllText(subscriptionPath), JsonOptions);
        }
        catch { }

        // The remote list may be reordered on every refresh. Keep an existing
        // local order by stable server id and append genuinely new nodes after it.
        sub.DisplayOrder = previous?.DisplayOrder > 0
            ? previous.DisplayOrder
            : NextSubscriptionDisplayOrder();
        var previousOrders = previous?.Servers
            .Where(server => !string.IsNullOrWhiteSpace(server.Id))
            .ToDictionary(server => server.Id, server => server.DisplayOrder, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nextServerOrder = Math.Max(0, previousOrders.Values.DefaultIfEmpty(0).Max());
        foreach (var server in sub.Servers)
        {
            if (previousOrders.TryGetValue(server.Id, out var priorOrder) && priorOrder > 0)
                server.DisplayOrder = priorOrder;
            else
                server.DisplayOrder = ++nextServerOrder;
        }
        sub.Servers = OrderServers(sub.Servers).ToList();

        foreach (var server in sub.Servers)
        {
            NormalizeServerPresentation(server);
            server.SubscriptionId = sub.Id;
            server.SubscriptionTitle = sub.Title;
            server.LocalPath = ServerPath(dir, server.Id);
            File.WriteAllText(server.LocalPath, JsonSerializer.Serialize(server, JsonOptions));
        }
        var liveFiles = sub.Servers.Select(x => Path.GetFullPath(x.LocalPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in Directory.GetFiles(dir, "server-*.json", SearchOption.TopDirectoryOnly))
        {
            if (!liveFiles.Contains(Path.GetFullPath(stale)))
                File.Delete(stale);
        }
        File.WriteAllText(subscriptionPath, JsonSerializer.Serialize(sub, JsonOptions));
        return sub;
    }

    public static bool MoveSubscription(string subscriptionId, int direction)
    {
        if (direction is not (-1 or 1) || string.IsNullOrWhiteSpace(subscriptionId))
            return false;
        var subscriptions = LoadAll().ToList();
        var index = subscriptions.FindIndex(subscription =>
            string.Equals(subscription.Id, subscriptionId, StringComparison.OrdinalIgnoreCase));
        if (!TryMove(subscriptions, index, direction))
            return false;

        for (var order = 0; order < subscriptions.Count; order++)
        {
            subscriptions[order].DisplayOrder = order + 1;
            PersistSubscriptionOrder(subscriptions[order]);
        }
        return true;
    }

    public static bool MoveServer(string subscriptionId, string serverId, int direction)
    {
        if (direction is not (-1 or 1) || string.IsNullOrWhiteSpace(subscriptionId) || string.IsNullOrWhiteSpace(serverId))
            return false;
        var subscription = LoadAll().FirstOrDefault(item =>
            string.Equals(item.Id, subscriptionId, StringComparison.OrdinalIgnoreCase));
        if (subscription is null)
            return false;

        var servers = OrderServers(subscription.Servers).ToList();
        var index = servers.FindIndex(server => string.Equals(server.Id, serverId, StringComparison.OrdinalIgnoreCase));
        if (!TryMove(servers, index, direction))
            return false;

        for (var order = 0; order < servers.Count; order++)
            servers[order].DisplayOrder = order + 1;
        subscription.Servers = servers;
        PersistSubscriptionOrder(subscription);
        return true;
    }

    private static IEnumerable<NoraSubscriptionInfo> OrderSubscriptions(IEnumerable<NoraSubscriptionInfo> source)
    {
        var subscriptions = source.ToList();
        // Legacy subscriptions have no DisplayOrder. Keep their stored order
        // until the user deliberately enters the arrangement mode.
        if (subscriptions.All(subscription => subscription.DisplayOrder <= 0))
            return subscriptions;
        return subscriptions
            .OrderBy(subscription => subscription.DisplayOrder > 0 ? subscription.DisplayOrder : int.MaxValue)
            .ThenByDescending(subscription => subscription.UpdatedAt)
            .ThenBy(subscription => subscription.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(subscription => subscription.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<NoraSubscriptionServer> OrderServers(IEnumerable<NoraSubscriptionServer> source)
    {
        var servers = source.ToList();
        if (servers.All(server => server.DisplayOrder <= 0))
            return servers;
        return servers
            .OrderBy(server => server.DisplayOrder > 0 ? server.DisplayOrder : int.MaxValue)
            .ThenBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(server => server.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(server => server.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static int NextSubscriptionDisplayOrder()
    {
        var existing = LoadAll();
        return Math.Max(existing.Count, existing.Select(subscription => subscription.DisplayOrder).DefaultIfEmpty(0).Max()) + 1;
    }

    private static bool TryMove<T>(List<T> items, int index, int direction)
    {
        var destination = index + direction;
        if (index < 0 || destination < 0 || destination >= items.Count)
            return false;
        (items[index], items[destination]) = (items[destination], items[index]);
        return true;
    }

    private static void PersistSubscriptionOrder(NoraSubscriptionInfo subscription)
    {
        if (string.IsNullOrWhiteSpace(subscription.Id))
            return;
        var dir = Path.Combine(Root, subscription.Id);
        EnsureUnderRoot(dir);
        if (!Directory.Exists(dir))
            return;

        subscription.Servers = OrderServers(subscription.Servers).ToList();
        foreach (var server in subscription.Servers)
        {
            NormalizeServerPresentation(server);
            server.SubscriptionId = subscription.Id;
            server.SubscriptionTitle = subscription.Title;
            server.LocalPath = ServerPath(dir, server.Id);
            File.WriteAllText(server.LocalPath, JsonSerializer.Serialize(server, JsonOptions));
        }
        File.WriteAllText(Path.Combine(dir, "subscription.json"), JsonSerializer.Serialize(subscription, JsonOptions));
    }

    public static bool IsAutomaticServer(NoraSubscriptionServer server)
        => Regex.IsMatch(server.Name ?? "", @"(?:^|[^\p{L}\p{N}])(?:auto|авто)(?:$|[^\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // An AUTO entry often represents a provider-managed front door. If that front door
    // becomes unavailable, the other AUTO entries in the same subscription are the only
    // sensible failover candidates: we must never silently switch to a named location.
    public static IReadOnlyList<NoraSubscriptionServer> GetAutomaticFailoverCandidates(NoraSubscriptionServer primary)
    {
        if (!IsAutomaticServer(primary) || string.IsNullOrWhiteSpace(primary.SubscriptionId))
            return [primary];

        var source = LoadAll().FirstOrDefault(subscription =>
            string.Equals(subscription.Id, primary.SubscriptionId, StringComparison.OrdinalIgnoreCase));
        return BuildAutomaticFailoverCandidates(primary, source?.Servers ?? []);
    }

    internal static IReadOnlyList<NoraSubscriptionServer> BuildAutomaticFailoverCandidates(
        NoraSubscriptionServer primary,
        IEnumerable<NoraSubscriptionServer> candidates)
    {
        if (!IsAutomaticServer(primary))
            return [primary];

        var fallback = candidates
            .Where(IsAutomaticServer)
            .Where(server => !string.Equals(server.Id, primary.Id, StringComparison.OrdinalIgnoreCase))
            .Where(server => server.Protocol.Contains("VLESS", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(server => string.Equals(NormalizeXrayNetwork(server.Network), NormalizeXrayNetwork(primary.Network), StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(server => string.Equals(server.Security, primary.Security, StringComparison.OrdinalIgnoreCase))
            .ThenBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(server => server.Host, StringComparer.OrdinalIgnoreCase);
        return [primary, .. fallback];
    }

    public static string DescribeSubscriptionProtocol(NoraSubscriptionServer server)
    {
        var transport = NormalizeXrayNetwork(server.Network);
        return transport switch
        {
            "xhttp" => "VLESS · XHTTP",
            "ws" => "VLESS · WebSocket",
            "grpc" => "VLESS · gRPC",
            "httpupgrade" => "VLESS · HTTPUpgrade",
            "h2" or "http" => "VLESS · HTTP/2",
            "raw" => "VLESS · RAW",
            _ => "VLESS"
        };
    }

    public static int RunTransportSelfTest(TextWriter output)
    {
        const string fixture = "vless://11111111-1111-4111-8111-111111111111@198.51.100.9:8443?encryption=none&security=reality&sni=example.org&fp=firefox&pbk=fixture-public-key&sid=abcd&type=xhttp&host=front.example&path=%2Fedge&mode=stream-one#AUTO%20fixture";
        try
        {
            if (!TryParseVlessUri(fixture, "fixture", out var primary))
                throw new InvalidOperationException("The XHTTP fixture could not be parsed.");
            primary.SubscriptionId = "fixture-subscription";
            var backup = new NoraSubscriptionServer
            {
                Id = "backup",
                Name = "AUTO backup",
                Protocol = "VLESS",
                Network = "xhttp",
                Security = "reality"
            };
            var named = new NoraSubscriptionServer { Id = "named", Name = "Netherlands", Protocol = "VLESS", Network = "xhttp" };
            var failover = BuildAutomaticFailoverCandidates(primary, [primary, named, backup]);

            var endpointPool = new[]
            {
                IPAddress.Parse("198.51.100.44"),
                IPAddress.Parse("198.51.100.45")
            };
            var pooled = new NoraSubscriptionServer
            {
                Host = "pool.fixture.invalid",
                Port = 8443,
                Protocol = "VLESS",
                Network = "tcp",
                Security = "reality"
            };
            using var config = JsonDocument.Parse(BuildXrayConfig(primary));
            using var pooledConfig = JsonDocument.Parse(BuildXrayConfig(pooled, endpointAddresses: endpointPool));
            var proxy = config.RootElement.GetProperty("outbounds")[0];
            var stream = proxy.GetProperty("streamSettings");
            var xhttp = stream.GetProperty("xhttpSettings");
            var pinnedPool = pooledConfig.RootElement.GetProperty("dns").GetProperty("hosts")
                .GetProperty("pool.fixture.invalid")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray();
            var pinnedStrategy = pooledConfig.RootElement.GetProperty("outbounds")[0]
                .GetProperty("streamSettings")
                .GetProperty("sockopt")
                .GetProperty("domainStrategy")
                .GetString();
            var passed = primary.Network.Equals("xhttp", StringComparison.OrdinalIgnoreCase) &&
                         primary.Path == "/edge" &&
                         primary.HostHeader == "front.example" &&
                         primary.Mode == "stream-one" &&
                         DescribeSubscriptionProtocol(primary) == "VLESS · XHTTP" &&
                         stream.GetProperty("network").GetString() == "xhttp" &&
                         stream.TryGetProperty("realitySettings", out _) &&
                         xhttp.GetProperty("path").GetString() == "/edge" &&
                         xhttp.GetProperty("host").GetString() == "front.example" &&
                         xhttp.GetProperty("mode").GetString() == "stream-one" &&
                         pinnedPool.SequenceEqual(["198.51.100.44", "198.51.100.45"], StringComparer.Ordinal) &&
                         pinnedStrategy == "ForceIPv4" &&
                         failover.Count == 2 && failover[0].Id == primary.Id && failover[1].Id == backup.Id;
            output.WriteLine(passed
                ? "SUBSCRIPTION TRANSPORT SELF-TEST PASS: xhttp=preserved; auto-failover=bounded; endpoint-pool=pinned"
                : "SUBSCRIPTION TRANSPORT SELF-TEST FAIL");
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            output.WriteLine("SUBSCRIPTION TRANSPORT SELF-TEST FAIL: " + exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
    }

    public static int RunOrderingSelfTest(TextWriter output)
    {
        var originalRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "nora-subscription-order-" + Guid.NewGuid().ToString("N"));
        try
        {
            var subscriptions = new List<NoraSubscriptionInfo>
            {
                new() { Id = "third", DisplayOrder = 3, Title = "Third" },
                new() { Id = "first", DisplayOrder = 1, Title = "First" },
                new() { Id = "second", DisplayOrder = 2, Title = "Second" }
            };
            var orderedSubscriptions = OrderSubscriptions(subscriptions).Select(item => item.Id).ToArray();

            var servers = new List<NoraSubscriptionServer>
            {
                new() { Id = "a", DisplayOrder = 1 },
                new() { Id = "b", DisplayOrder = 2 },
                new() { Id = "c", DisplayOrder = 3 }
            };
            var moved = TryMove(servers, 1, -1);
            for (var index = 0; index < servers.Count; index++)
                servers[index].DisplayOrder = index + 1;

            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", temporaryRoot);
            var first = Store(new NoraSubscriptionInfo
            {
                Id = "first-subscription",
                Title = "First",
                Servers = [new NoraSubscriptionServer { Id = "first-node", Name = "First node", Host = "198.51.100.1" }]
            }, "fixture:first");
            var second = Store(new NoraSubscriptionInfo
            {
                Id = "second-subscription",
                Title = "Second",
                Servers =
                [
                    new NoraSubscriptionServer { Id = "second-a", Name = "Second A", Host = "198.51.100.2" },
                    new NoraSubscriptionServer { Id = "second-b", Name = "Second B", Host = "198.51.100.3" }
                ]
            }, "fixture:second");
            var movedSubscription = MoveSubscription(second.Id, -1);
            var movedServer = MoveServer(second.Id, "second-b", -1);
            var refreshed = Store(new NoraSubscriptionInfo
            {
                Id = second.Id,
                Title = second.Title,
                Servers =
                [
                    new NoraSubscriptionServer { Id = "second-a", Name = "Second A", Host = "198.51.100.2" },
                    new NoraSubscriptionServer { Id = "second-b", Name = "Second B", Host = "198.51.100.3" },
                    new NoraSubscriptionServer { Id = "second-c", Name = "Second C", Host = "198.51.100.4" }
                ]
            }, "fixture:second");
            var persistedSubscriptions = LoadAll().Select(item => item.Id).ToArray();
            var persistedServers = refreshed.Servers.Select(item => item.Id).ToArray();

            var passed = orderedSubscriptions.SequenceEqual(["first", "second", "third"], StringComparer.Ordinal) &&
                         moved &&
                         servers.Select(item => item.Id).SequenceEqual(["b", "a", "c"], StringComparer.Ordinal) &&
                         servers.Select(item => item.DisplayOrder).SequenceEqual([1, 2, 3]) &&
                         first.DisplayOrder == 1 && second.DisplayOrder == 2 &&
                         movedSubscription && movedServer &&
                         persistedSubscriptions.SequenceEqual(["second-subscription", "first-subscription"], StringComparer.Ordinal) &&
                         persistedServers.SequenceEqual(["second-b", "second-a", "second-c"], StringComparer.Ordinal);
            output.WriteLine(passed
                ? "SUBSCRIPTION ORDER SELF-TEST PASS: persisted-order=stable; moves=bounded"
                : "SUBSCRIPTION ORDER SELF-TEST FAIL: unexpected ordering result");
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            output.WriteLine("SUBSCRIPTION ORDER SELF-TEST FAIL: " + exception.GetType().Name);
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", originalRoot);
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, recursive: true);
            }
            catch { }
        }
    }

    private static void NormalizeServerPresentation(NoraSubscriptionServer server)
    {
        if (!server.Protocol.Contains("VLESS", StringComparison.OrdinalIgnoreCase) &&
            !server.Protocol.Equals("VLESS", StringComparison.OrdinalIgnoreCase))
            return;
        server.Protocol = DescribeSubscriptionProtocol(server);
    }

    private static string ServerPath(string dir, string id) => Path.Combine(dir, "server-" + id + ".json");

    private static NoraSubscriptionInfo ParsePayload(string body, string sourceUrl, IReadOnlyDictionary<string, string> headers, Action<string> log)
    {
        var title = DecodeHeaderText(Header(headers, "profile-title"));
        if (string.IsNullOrWhiteSpace(title))
            title = DecodeHeaderText(Header(headers, "profile-title*"));
        if (string.IsNullOrWhiteSpace(title))
            title = "Imported subscription";

        var sub = new NoraSubscriptionInfo
        {
            Id = StableId(sourceUrl.Length == 0 ? body[..Math.Min(body.Length, 128)] : sourceUrl),
            Title = title,
            SourceUrl = sourceUrl,
            WebPageUrl = Header(headers, "profile-web-page-url"),
            Announce = DecodeHeaderText(Header(headers, "announce")),
            UpdateIntervalHours = ParseInt(Header(headers, "profile-update-interval")),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        ApplyUserInfo(sub, Header(headers, "subscription-userinfo"));

        var trimmed = body.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("{", StringComparison.Ordinal))
            sub.Servers = ParseXrayJson(trimmed, title).ToList();
        else
        {
            var decoded = TryBase64Decode(trimmed);
            var payload = decoded.Length > 0 ? decoded : trimmed;
            if (payload.Contains("proxies:", StringComparison.OrdinalIgnoreCase))
                sub.Servers = ParseClashYaml(payload, title).ToList();
            else
                sub.Servers = ParseShareLinks(payload, title).ToList();
        }
        if (sub.Servers.Count == 0)
            throw new NoraAppException("NORA-SUB-4003", "Subscription contains no supported VLESS servers.");
        log($"Imported subscription `{sub.Title}` with {sub.Servers.Count} supported servers.");
        return sub;
    }

    private static IEnumerable<NoraSubscriptionServer> ParseXrayJson(string json, string title)
    {
        using var doc = JsonDocument.Parse(json);
        var index = 0;
        var configs = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : [doc.RootElement];
        foreach (var cfg in configs)
        {
            index++;
            if (!cfg.TryGetProperty("outbounds", out var outbounds) || outbounds.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var outbound in outbounds.EnumerateArray())
            {
                if (!string.Equals(Str(outbound, "protocol"), "vless", StringComparison.OrdinalIgnoreCase))
                    continue;
                var settings = outbound.GetProperty("settings");
                var modern = !settings.TryGetProperty("vnext", out var vnextArray);
                var vnext = modern ? settings : vnextArray[0];
                var user = modern ? settings : vnext.GetProperty("users")[0];
                var stream = outbound.TryGetProperty("streamSettings", out var ss) ? ss : default;
                var reality = stream.ValueKind == JsonValueKind.Object && stream.TryGetProperty("realitySettings", out var rs) ? rs : default;
                var tls = stream.ValueKind == JsonValueKind.Object && stream.TryGetProperty("tlsSettings", out var ts) ? ts : default;
                var name = Str(cfg, "remarks");
                if (string.IsNullOrWhiteSpace(name))
                    name = $"{title} #{index}";
                var host = Str(vnext, "address");
                var network = Str(stream, "network", "tcp");
                var transport = TransportFields(stream, network);
                yield return new NoraSubscriptionServer
                {
                    Id = StableId(title + "|" + name + "|" + host + "|" + Int(vnext, "port")),
                    Name = name,
                    Host = host,
                    Port = Int(vnext, "port", 443),
                    Country = GuessCountry(name, host),
                    Uuid = Str(user, "id"),
                    Encryption = Str(user, "encryption", "none"),
                    Flow = Str(user, "flow"),
                    Network = network,
                    Security = Str(stream, "security", "none"),
                    Sni = First(Str(reality, "serverName"), Str(tls, "serverName")),
                    Fingerprint = First(Str(reality, "fingerprint"), Str(tls, "fingerprint"), "firefox"),
                    PublicKey = First(Str(reality, "publicKey"), Str(reality, "password")),
                    ShortId = Str(reality, "shortId"),
                    SpiderX = Str(reality, "spiderX"),
                    HeaderType = transport.HeaderType,
                    HostHeader = transport.Host,
                    Path = transport.Path,
                    ServiceName = transport.ServiceName,
                    Mode = transport.Mode,
                    Authority = transport.Authority,
                    Alpn = JoinJsonArray(tls, "alpn"),
                    AllowInsecure = Bool(tls, "allowInsecure"),
                    XhttpExtraJson = transport.ExtraJson,
                    InboundCount = CountArray(cfg, "inbounds"),
                    OutboundCount = CountArray(cfg, "outbounds"),
                    RuleCount = cfg.TryGetProperty("routing", out var routing) ? CountArray(routing, "rules") : 0,
                    RawConfigJson = cfg.GetRawText()
                };
            }
        }
    }

    private static IEnumerable<NoraSubscriptionServer> ParseShareLinks(string payload, string title)
    {
        foreach (var line in payload.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                continue;
            if (TryParseVlessUri(line, title, out var server))
                yield return server;
        }
    }

    private static IEnumerable<NoraSubscriptionServer> ParseClashYaml(string yaml, string title)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root ||
            !TryYamlChild(root, "proxies", out var proxiesNode) || proxiesNode is not YamlSequenceNode proxies)
            yield break;

        foreach (var node in proxies.Children.OfType<YamlMappingNode>())
        {
            var item = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            FlattenYaml(node, "", item);
            if (!IsVless(item))
                continue;
            var name = Get(item, "name", $"{title} VLESS");
            var host = Get(item, "server", "");
            yield return new NoraSubscriptionServer
            {
                Id = StableId(title + "|" + name + "|" + host + "|" + Get(item, "port", "443")),
                Name = name,
                Host = host,
                Port = int.TryParse(Get(item, "port", "443"), out var port) ? port : 443,
                Country = GuessCountry(name, host),
                Uuid = Get(item, "uuid", ""),
                Encryption = Get(item, "encryption", "none"),
                Flow = Get(item, "flow", ""),
                Network = Get(item, "network", "tcp"),
                Security = Get(item, "security", Get(item, "tls", "false").Equals("true", StringComparison.OrdinalIgnoreCase) ? "tls" : "none"),
                Sni = First(Get(item, "reality_opts.server_name", ""), Get(item, "servername", ""), Get(item, "sni", "")),
                Fingerprint = First(Get(item, "client_fingerprint", ""), Get(item, "fingerprint", ""), "firefox"),
                PublicKey = First(Get(item, "reality_opts.public_key", ""), Get(item, "reality_opts.publicKey", ""), Get(item, "pbk", "")),
                ShortId = First(Get(item, "reality_opts.short_id", ""), Get(item, "sid", "")),
                HeaderType = Get(item, "header_type", "none"),
                HostHeader = First(Get(item, "ws_opts.headers.Host", ""), Get(item, "ws_opts.headers.host", ""), Get(item, "server_name", "")),
                Path = First(Get(item, "ws_opts.path", ""), Get(item, "http_opts.path", ""), Get(item, "xhttp_opts.path", "")),
                ServiceName = Get(item, "grpc_opts.grpc_service_name", ""),
                Authority = Get(item, "grpc_opts.grpc_authority", ""),
                Mode = Get(item, "xhttp_opts.mode", ""),
                Alpn = Get(item, "alpn", ""),
                AllowInsecure = Get(item, "skip_cert_verify", "false").Equals("true", StringComparison.OrdinalIgnoreCase),
                XhttpExtraJson = Get(item, "xhttp_opts.extra", ""),
                SourceLink = ""
            };
        }
    }

    private static void FlattenYaml(YamlNode node, string prefix, IDictionary<string, string> output)
    {
        if (node is not YamlMappingNode map)
            return;
        foreach (var pair in map.Children)
        {
            var key = ((YamlScalarNode)pair.Key).Value?.Replace('-', '_') ?? "";
            var path = string.IsNullOrWhiteSpace(prefix) ? key : prefix + "." + key;
            if (pair.Value is YamlMappingNode child)
                FlattenYaml(child, path, output);
            else if (pair.Value is YamlSequenceNode sequence)
                output[path] = string.Join(',', sequence.Children.OfType<YamlScalarNode>().Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)));
            else if (pair.Value is YamlScalarNode scalar)
                output[path] = scalar.Value ?? "";
        }
    }

    private static bool TryYamlChild(YamlMappingNode map, string key, out YamlNode node)
    {
        foreach (var pair in map.Children)
        {
            if (pair.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                node = pair.Value;
                return true;
            }
        }
        node = null!;
        return false;
    }

    private static bool TryParseVlessUri(string link, string title, out NoraSubscriptionServer server)
    {
        server = new NoraSubscriptionServer();
        try
        {
            var uri = new Uri(link);
            var uuid = Uri.UnescapeDataString(uri.UserInfo);
            var query = ParseQuery(uri.Query);
            var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
            if (string.IsNullOrWhiteSpace(name))
                name = $"{title} VLESS";
            server = new NoraSubscriptionServer
            {
                Id = StableId(title + "|" + name + "|" + uri.Host + "|" + uri.Port),
                Name = name,
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 443,
                Country = GuessCountry(name, uri.Host),
                Uuid = uuid,
                Encryption = Get(query, "encryption", "none"),
                Flow = Get(query, "flow", ""),
                Network = Get(query, "type", Get(query, "network", "tcp")),
                Security = Get(query, "security", "reality"),
                Sni = First(Get(query, "sni", ""), Get(query, "serverName", "")),
                Fingerprint = Get(query, "fp", "firefox"),
                PublicKey = Get(query, "pbk", ""),
                ShortId = Get(query, "sid", ""),
                SpiderX = Get(query, "spx", ""),
                HeaderType = Get(query, "headerType", Get(query, "header", "none")),
                HostHeader = Get(query, "host", ""),
                Path = Get(query, "path", ""),
                ServiceName = First(Get(query, "serviceName", ""), Get(query, "service_name", "")),
                Mode = Get(query, "mode", ""),
                Authority = Get(query, "authority", ""),
                Alpn = Get(query, "alpn", ""),
                AllowInsecure = Get(query, "allowInsecure", "0") is "1" or "true",
                XhttpExtraJson = Get(query, "extra", ""),
                SourceLink = link
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsVless(Dictionary<string, string> item)
        => string.Equals(Get(item, "type", ""), "vless", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
        }
        return result;
    }

    private static void ApplyUserInfo(NoraSubscriptionInfo sub, string userInfo)
    {
        foreach (var part in userInfo.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2 || !long.TryParse(kv[1], out var value))
                continue;
            if (kv[0].Equals("upload", StringComparison.OrdinalIgnoreCase)) sub.UploadBytes = value;
            else if (kv[0].Equals("download", StringComparison.OrdinalIgnoreCase)) sub.DownloadBytes = value;
            else if (kv[0].Equals("total", StringComparison.OrdinalIgnoreCase)) sub.TotalBytes = value;
            else if (kv[0].Equals("expire", StringComparison.OrdinalIgnoreCase)) sub.ExpireUnix = value;
        }
    }

    private static string Header(IReadOnlyDictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var value) ? value : "";

    private static string DecodeHeaderText(string value)
    {
        value = DecodeRfc5987(value).Trim();
        if (!value.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
            return value;
        return TryDecodeBase64Text(value["base64:".Length..]);
    }

    private static string DecodeRfc5987(string value)
    {
        var marker = "UTF-8''";
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? Uri.UnescapeDataString(value[(index + marker.Length)..]) : value;
    }

    private static string TryBase64Decode(string value)
    {
        try
        {
            var cleaned = value.Trim().Replace('-', '+').Replace('_', '/');
            cleaned = cleaned.PadRight(cleaned.Length + (4 - cleaned.Length % 4) % 4, '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cleaned));
            return text.Contains("://", StringComparison.Ordinal) || text.Contains("proxies:", StringComparison.OrdinalIgnoreCase) ? text : "";
        }
        catch { return ""; }
    }

    private static string TryDecodeBase64Text(string value)
    {
        try
        {
            var cleaned = value.Trim().Replace('-', '+').Replace('_', '/');
            cleaned = cleaned.PadRight(cleaned.Length + (4 - cleaned.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(cleaned)).Trim();
        }
        catch { return ""; }
    }

    private static string StableId(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();

    private static string TrimForError(string value)
        => value.Trim().Length > 240 ? value.Trim()[..240] + "..." : value.Trim();

    private static int ParseInt(string value)
        => int.TryParse(value, out var parsed) ? parsed : 0;

    private static int Int(JsonElement el, string name, int fallback = 0)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : fallback;

    private static string Str(JsonElement el, string name, string fallback = "")
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : fallback;

    private static int CountArray(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : 0;

    private static bool Bool(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) &&
           (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var parsed) && parsed));

    private static string JoinJsonArray(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? string.Join(',', value.EnumerateArray().Select(x => x.ToString()))
            : "";

    private static TransportInfo TransportFields(JsonElement stream, string network)
    {
        if (stream.ValueKind != JsonValueKind.Object)
            return new TransportInfo();
        JsonElement settings = default;
        var normalized = NormalizeXrayNetwork(network);
        var property = normalized switch
        {
            "ws" => "wsSettings",
            "grpc" => "grpcSettings",
            "httpupgrade" => "httpupgradeSettings",
            "xhttp" => "xhttpSettings",
            "raw" or "tcp" => "rawSettings",
            _ => ""
        };
        if (property.Length > 0)
            stream.TryGetProperty(property, out settings);
        var headers = settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("headers", out var h) ? h : default;
        var header = settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("header", out var hd) ? hd : default;
        return new TransportInfo
        {
            HeaderType = Str(header, "type", "none"),
            Host = First(Str(settings, "host"), Str(headers, "Host"), Str(headers, "host")),
            Path = Str(settings, "path"),
            ServiceName = Str(settings, "serviceName"),
            Mode = Str(settings, "mode"),
            Authority = Str(settings, "authority")
            ,ExtraJson = normalized == "xhttp" && settings.ValueKind == JsonValueKind.Object ? settings.GetRawText() : ""
        };
    }

    private sealed class TransportInfo
    {
        public string HeaderType { get; init; } = "none";
        public string Host { get; init; } = "";
        public string Path { get; init; } = "";
        public string ServiceName { get; init; } = "";
        public string Mode { get; init; } = "";
        public string Authority { get; init; } = "";
        public string ExtraJson { get; init; } = "";
    }

    private static string Get(IReadOnlyDictionary<string, string> map, string key, string fallback)
        => map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string First(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static string GuessCountry(string name, string host)
    {
        var code = CountryCodeFromFlag(name);
        if (code.Length == 2)
            return CountryNameFromCode(code);
        foreach (Match match in Regex.Matches(name, @"(?<![\p{L}\p{N}])([A-Z]{2})(?![\p{L}\p{N}])", RegexOptions.CultureInvariant))
        {
            var country = CountryNameFromCode(match.Groups[1].Value);
            if (country.Length > 0)
                return country;
        }
        foreach (var alias in CountryAliases)
        {
            if (alias.Aliases.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase)))
                return CountryNameFromCode(alias.Code);
        }
        var known = KnownLocationForHost(host);
        return !string.IsNullOrWhiteSpace(known.Country) ? known.Country : "Unknown";
    }

    private static string CanonicalCountry(string value)
    {
        value = value.Trim();
        if (value.Length == 2)
        {
            var byCode = CountryNameFromCode(value);
            if (byCode.Length > 0)
                return byCode;
        }
        foreach (var alias in CountryAliases)
        {
            if (alias.Aliases.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase)))
                return CountryNameFromCode(alias.Code);
        }
        return value.Trim();
    }

    private static string CountryCodeFromFlag(string value)
    {
        var runes = value.EnumerateRunes().ToArray();
        for (var i = 0; i + 1 < runes.Length; i++)
        {
            if (runes[i].Value is >= 0x1F1E6 and <= 0x1F1FF && runes[i + 1].Value is >= 0x1F1E6 and <= 0x1F1FF)
                return string.Concat((char)('A' + runes[i].Value - 0x1F1E6), (char)('A' + runes[i + 1].Value - 0x1F1E6));
        }
        return "";
    }

    private static string CountryNameFromCode(string code)
    {
        if (code.Equals("EU", StringComparison.OrdinalIgnoreCase))
            return "Europe";
        try { return new RegionInfo(code.ToUpperInvariant()).EnglishName; }
        catch { return ""; }
    }

    private static (string Country, string City) KnownLocationForHost(string host)
    {
        host = host.Trim().Trim('[', ']');
        foreach (var item in KnownEndpointLocations)
        {
            if (item.Exact && string.Equals(host, item.Host, StringComparison.OrdinalIgnoreCase))
                return (CountryNameFromCode(item.Code), item.City);
            if (!item.Exact && host.StartsWith(item.Host, StringComparison.OrdinalIgnoreCase))
                return (CountryNameFromCode(item.Code), item.City);
        }
        return ("", "");
    }

    private static readonly (string Host, bool Exact, string Code, string City)[] KnownEndpointLocations =
        [];

    private static readonly (string Code, string[] Aliases)[] CountryAliases =
    [
        ("NL", ["Netherlands", "Nederland", "Нидерланд"]),
        ("DE", ["Germany", "Deutschland", "Германи"]),
        ("IE", ["Ireland", "Ирланди"]),
        ("PL", ["Poland", "Polska", "Польш"]),
        ("RU", ["Russia", "Россий", "Россия", "Москва"]),
        ("US", ["United States", "USA", "США", "Майами", "Miami"]),
        ("FI", ["Finland", "Финлянд"]),
        ("LV", ["Latvia", "Латви"]),
        ("LT", ["Lithuania", "Литв"]),
        ("SE", ["Sweden", "Швец"]),
        ("IT", ["Italy", "Italia", "Итали"]),
        ("UA", ["Ukraine", "Украин"]),
        ("IN", ["India", "Индия", "Индии"]),
        ("NG", ["Nigeria", "Нигери"]),
        ("GB", ["United Kingdom", "Great Britain", "Britain", "Великобритан", "Англи"]),
        ("FR", ["France", "Франци"]),
        ("ES", ["Spain", "Испани"]),
        ("CH", ["Switzerland", "Швейцари"]),
        ("AT", ["Austria", "Австри"]),
        ("CZ", ["Czech", "Чехи"]),
        ("EE", ["Estonia", "Эстони"]),
        ("TR", ["Turkey", "Türkiye", "Турци"]),
        ("JP", ["Japan", "Япони"]),
        ("SG", ["Singapore", "Сингапур"]),
        ("CA", ["Canada", "Канад"]),
        ("BR", ["Brazil", "Бразили"]),
        ("AE", ["United Arab Emirates", "UAE", "ОАЭ", "Эмират"]),
        ("KZ", ["Kazakhstan", "Казахстан"]),
        ("BY", ["Belarus", "Беларус"]),
        ("MD", ["Moldova", "Молдов"]),
        ("GE", ["Georgia", "Грузи"]),
        ("AM", ["Armenia", "Армени"]),
        ("IL", ["Israel", "Израил"]),
        ("KR", ["South Korea", "Korea", "Коре"]),
        ("HK", ["Hong Kong", "Гонконг"]),
        ("TW", ["Taiwan", "Тайван"]),
        ("AU", ["Australia", "Австрали"]),
        ("NO", ["Norway", "Норвеги"]),
        ("DK", ["Denmark", "Дани"]),
        ("BE", ["Belgium", "Бельги"]),
        ("PT", ["Portugal", "Португали"]),
        ("RO", ["Romania", "Румыни"]),
        ("BG", ["Bulgaria", "Болгари"]),
        ("HU", ["Hungary", "Венгри"]),
        ("RS", ["Serbia", "Серби"]),
        ("HR", ["Croatia", "Хорвати"]),
        ("GR", ["Greece", "Греци"])
    ];
}

internal interface IVpnCoreProcess
{
    Task StartAsync(TimeSpan timeout);
    Task StopAsync(TimeSpan timeout);
    Task WaitForExitAsync();
}

internal sealed record NoraDirectLatencyResult(
    bool Success,
    long? Milliseconds,
    string Status,
    string InterfaceName,
    string Detail);

internal sealed record NoraBackendProbeResult(
    bool Success,
    long Milliseconds,
    string Status,
    string Detail);

internal static class NoraDirectLatencyProbe
{
    // Windows IP_UNICAST_IF. It is deliberately not exposed by the managed
    // SocketOptionName enum, but SetSocketOption forwards the Winsock option.
    // The interface index must be supplied in network byte order.
    private const int IpUnicastInterface = 31;
    private const int MaxEndpointCandidates = 4;

    private sealed record PhysicalRoute(IPAddress LocalAddress, int InterfaceIndex, string InterfaceName);

    public static async Task<NoraDirectLatencyResult> ProbeAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var route = await GetPhysicalRouteAsync(cancellationToken).ConfigureAwait(false);
        if (route is null)
        {
            return new NoraDirectLatencyResult(
                false,
                null,
                "route",
                "",
                "No active physical IPv4 adapter with a gateway is available; a tunnel route was not used as a fallback.");
        }

        var addresses = await ResolveIpv4Async(host, cancellationToken);
        if (addresses.Count == 0)
        {
            return new NoraDirectLatencyResult(
                false,
                null,
                "offline",
                route.InterfaceName,
                "The endpoint could not be resolved to IPv4.");
        }

        var attempts = await Task.WhenAll(addresses
            .Take(MaxEndpointCandidates)
            .Select(address => ProbeEndpointAsync(route, address, port, timeout, cancellationToken)));

        var success = attempts
            .Where(attempt => attempt.Success)
            .OrderBy(attempt => attempt.Milliseconds)
            .FirstOrDefault();
        if (success is not null)
            return success;

        var directFailure = attempts.FirstOrDefault(attempt => attempt.Status == "route");
        if (directFailure is not null)
            return directFailure;

        var timeoutFailure = attempts.FirstOrDefault(attempt => attempt.Status == "timeout");
        return timeoutFailure ?? attempts.FirstOrDefault() ?? new NoraDirectLatencyResult(
            false,
            null,
            "offline",
            route.InterfaceName,
            "The endpoint did not accept a direct TCP connection.");
    }

    internal static int InterfaceOptionValue(int interfaceIndex) => IPAddress.HostToNetworkOrder(interfaceIndex);

    internal static int RunSelfTest(TextWriter output)
    {
        try
        {
            var encoded = InterfaceOptionValue(42);
            var expected = BitConverter.IsLittleEndian ? 0x2A00_0000 : 42;
            var route = GetPhysicalRouteAsync(CancellationToken.None).GetAwaiter().GetResult();
            var passed = encoded == expected && (route is null ||
                (route.InterfaceIndex > 0 && route.LocalAddress.AddressFamily == AddressFamily.InterNetwork));
            output.WriteLine(passed
                ? "DIRECT LATENCY SELF-TEST PASS: physical-interface option is network-order; no tunnel fallback"
                : "DIRECT LATENCY SELF-TEST FAIL: interface option or physical-route selection is invalid");
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            output.WriteLine("DIRECT LATENCY SELF-TEST FAIL: " + ex.GetType().Name);
            return 1;
        }
    }

    private static async Task<NoraDirectLatencyResult> ProbeEndpointAsync(
        PhysicalRoute route,
        IPAddress address,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        try
        {
            socket.SetSocketOption(
                SocketOptionLevel.IP,
                (SocketOptionName)IpUnicastInterface,
                InterfaceOptionValue(route.InterfaceIndex));
            socket.Bind(new IPEndPoint(route.LocalAddress, 0));
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException or InvalidOperationException)
        {
            return new NoraDirectLatencyResult(
                false,
                null,
                "route",
                route.InterfaceName,
                "Windows could not pin the probe socket to the physical interface: " + ex.Message);
        }

        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), timeoutCts.Token);
            return new NoraDirectLatencyResult(
                true,
                Math.Max(1, stopwatch.ElapsedMilliseconds),
                "ok",
                route.InterfaceName,
                "Direct TCP probe pinned to " + route.InterfaceName + ".");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NoraDirectLatencyResult(
                false,
                null,
                "timeout",
                route.InterfaceName,
                "The direct TCP probe timed out.");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
        {
            return new NoraDirectLatencyResult(
                false,
                null,
                "timeout",
                route.InterfaceName,
                "The direct TCP probe timed out.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new NoraDirectLatencyResult(
                false,
                null,
                "offline",
                route.InterfaceName,
                "The endpoint rejected or dropped the direct TCP probe: " + ex.GetBaseException().Message);
        }
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveIpv4Async(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
            return literal.AddressFamily == AddressFamily.InterNetwork ? [literal] : [];

        try
        {
            return (await Dns.GetHostAddressesAsync(host, cancellationToken))
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static async Task<PhysicalRoute?> GetPhysicalRouteAsync(CancellationToken cancellationToken)
    {
        var snapshot = await NoraNetworkInterfaceCache.GetSnapshotAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var candidates = new List<(PhysicalRoute Route, int Priority)>();
        foreach (var network in snapshot.Interfaces)
        {
            try
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                    LooksVirtualOrTunnelLike(network.Name + " " + network.Description))
                    continue;
                var properties = network.GetIPProperties();
                var address = properties.UnicastAddresses
                    .Select(item => item.Address)
                    .FirstOrDefault(IsUsableIpv4);
                var hasGateway = properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !gateway.Address.Equals(IPAddress.Any));
                var index = properties.GetIPv4Properties()?.Index ?? 0;
                if (address is not null && hasGateway && index > 0)
                    candidates.Add((
                        new PhysicalRoute(address, index, network.Name),
                        network.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 ? 0 : 1));
            }
            catch
            {
                // Windows filter bindings can expose NetworkInterface records
                // without a configured IPv4 protocol. Skip them independently.
            }
        }
        return candidates
            .OrderBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Route.InterfaceIndex)
            .Select(candidate => candidate.Route)
            .FirstOrDefault();
    }

    private static bool IsUsableIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            return false;
        var bytes = address.GetAddressBytes();
        return !(bytes[0] == 169 && bytes[1] == 254);
    }

    private static bool LooksVirtualOrTunnelLike(string value)
        => value.Contains("tunnel", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("wintun", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("tap", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("wireguard", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("openvpn", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("hyper-v", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("vmware", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("loopback", StringComparison.OrdinalIgnoreCase);
}

internal static class NoraDataPlaneProbe
{
    public static async Task VerifyAsync(
        IVpnCoreProcess core,
        TimeSpan timeout,
        Action<string> log,
        string expectedExitHost = "",
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        var probeToken = linkedCts.Token;
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(7),
            PooledConnectionLifetime = TimeSpan.Zero
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NORA-VPN/1.0");

        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (core.WaitForExitAsync().IsCompleted)
                    throw new InvalidOperationException("VPN core exited during the data-plane check");

                using var ipResponse = await client.GetAsync("https://1.1.1.1/cdn-cgi/trace", HttpCompletionOption.ResponseContentRead, probeToken);
                ipResponse.EnsureSuccessStatusCode();
                var trace = await ipResponse.Content.ReadAsStringAsync(probeToken);
                var exitIp = trace.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(x => x.StartsWith("ip=", StringComparison.OrdinalIgnoreCase))?[3..].Trim() ?? "unknown";
                if (System.Net.IPAddress.TryParse(expectedExitHost, out var expectedIp) &&
                    (!System.Net.IPAddress.TryParse(exitIp, out var actualIp) || !actualIp.Equals(expectedIp)))
                    throw new InvalidOperationException($"Traffic exited through {exitIp}, not the selected VPN server {expectedExitHost}");

                using var dnsResponse = await client.GetAsync("https://www.cloudflare.com/cdn-cgi/trace", HttpCompletionOption.ResponseHeadersRead, probeToken);
                dnsResponse.EnsureSuccessStatusCode();
                log($"Tunnel probe passed: exit_ip={exitIp}; dns_https={(int)dnsResponse.StatusCode}");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (!timeoutCts.IsCancellationRequested)
            {
                last = ex;
                log($"Tunnel probe attempt {attempt}/3 failed: {ex.GetBaseException().Message}");
                await Task.Delay(TimeSpan.FromSeconds(attempt), probeToken);
            }
        }
        throw new NoraAppException("NORA-CON-2010", "VPN core started, but internet traffic did not pass through it", last);
    }
}

internal sealed class XrayCoreProcess(NoraSubscriptionServer server, Action<string> log, bool discordMode = false) : IVpnCoreProcess, INoraDiscordModeCore
{
    private Process? _xray;
    private Process? _tunFrontend;
    private string _tunConfigPath = "";
    private int _socksPort;
    private readonly List<XrayEndpointBypass.RouteEntry> _endpointRoutes = [];
    private readonly TaskCompletionSource _xrayReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _tunReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task StartAsync(TimeSpan timeout) => StartAsync(timeout, CancellationToken.None);

    // DNS resolution happens before the Xray process is created.  Checking the
    // caller token at each asynchronous boundary prevents a cancelled attempt
    // from launching Xray or sing-box after the UI has already returned to Ready.
    public async Task StartAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await StartBackendForDiagnosticsAsync(timeout, cancellationToken);
        await StartTunFrontendForDiagnosticsAsync(timeout, cancellationToken);
    }

    // The manual Logs diagnostic deliberately separates the local Xray backend
    // from TUN startup.  A HTTPS probe through this SOCKS listener proves the
    // VLESS/Reality path without any NORA routes being able to influence it.
    internal async Task StartBackendForDiagnosticsAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_xray is not null)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        var xrayExe = NoraSubscriptionStore.XrayPath();
        var missing = NoraSubscriptionStore.MissingVlessCorePaths();
        if (missing.Count > 0)
            throw new NoraAppException(
                "NORA-XRY-3101",
                "VLESS components are missing: " + string.Join(", ", missing.Select(Path.GetFileName)) +
                ". Re-extract the complete NORA VPN folder, including its cores directory.");

        var dir = Path.Combine(NoraAppState.DataRoot, "runtime");
        Directory.CreateDirectory(dir);
        var endpointAddresses = await XrayEndpointBypass.ResolveAsync(server.Host);
        cancellationToken.ThrowIfCancellationRequested();
        if (endpointAddresses.Count == 0)
            throw new NoraAppException("NORA-XRY-3105", "Cannot resolve an IPv4 address for the selected VLESS endpoint before TUN routing starts.");

        _socksPort = GetFreeTcpPort();
        var xrayConfigPath = Path.Combine(dir, "xray-" + server.Id + ".json");
        _tunConfigPath = Path.Combine(dir, "xray-tun-" + server.Id + ".json");
        File.WriteAllText(xrayConfigPath, NoraSubscriptionStore.BuildXrayConfig(server, _socksPort, endpointAddresses: endpointAddresses));
        var tunConfig = discordMode
            ? await NoraDiscordRouting.BuildForSocksAsync(_socksPort, cancellationToken).ConfigureAwait(false)
            : NoraSubscriptionStore.BuildXrayTunFrontendConfig(_socksPort, server);
        File.WriteAllText(_tunConfigPath, tunConfig);
        if (!IPAddress.TryParse(server.Host, out _))
            log($"[xray] endpoint DNS pool pinned: {endpointAddresses.Count} IPv4 address(es) for this session");

        var routePolicy = NoraSubscriptionStore.ParseXrayRoutingPolicy(server.RawConfigJson);
        if (discordMode)
        {
            log("Discord-only routing: Discord processes use the selected VLESS server; all other processes stay direct.");
        }
        else if (routePolicy is not null)
        {
            log($"Split routing active: {routePolicy.DirectMatcherCount} direct rule(s) bypass the tunnel" +
                (routePolicy.BlockBittorrent ? ", BitTorrent blocked" : "") +
                (routePolicy.SkippedRuleSets > 0 ? $"; {routePolicy.SkippedRuleSets} geosite/geoip rule(s) skipped (not supported)" : "") +
                ". Everything else goes through the VPN.");
        }
        else
        {
            log("Full-tunnel routing: this config has no provider routing rules, so all traffic uses the VPN.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var xrayPsi = new ProcessStartInfo(xrayExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        xrayPsi.ArgumentList.Add("run");
        xrayPsi.ArgumentList.Add("-config");
        xrayPsi.ArgumentList.Add(xrayConfigPath);

        _xray = new Process { StartInfo = xrayPsi, EnableRaisingEvents = true };
        _xray.OutputDataReceived += (_, e) => HandleXrayLine(e.Data);
        _xray.ErrorDataReceived += (_, e) => HandleXrayLine(e.Data);
        _xray.Exited += (_, _) =>
        {
            if (!_xrayReady.Task.IsCompleted)
                _xrayReady.TrySetException(new NoraAppException("NORA-XRY-3103", "Xray exited before its local backend became ready"));
            _exited.TrySetResult();
        };
        if (!_xray.Start())
            throw new NoraAppException("NORA-XRY-3103", "Cannot start Xray core");
        _xray.BeginOutputReadLine();
        _xray.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        using var registration = linkedCts.Token.Register(() =>
            _xrayReady.TrySetException(new NoraAppException("NORA-XRY-3103", "Xray did not become ready in time")));
        await _xrayReady.Task;
        cancellationToken.ThrowIfCancellationRequested();
        _endpointRoutes.AddRange(await XrayEndpointBypass.InstallAsync(endpointAddresses, log));
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal async Task<NoraBackendProbeResult> ProbeLocalSocksAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_xray is null || _xray.HasExited || _socksPort <= 0)
            return new NoraBackendProbeResult(false, 0, "backend", "The local Xray SOCKS backend is not running.");

        using var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = new WebProxy(new Uri($"socks5://127.0.0.1:{_socksPort}")),
            ConnectTimeout = timeout,
            PooledConnectionLifetime = TimeSpan.Zero
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NORA-Diagnostics/1.0");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync("https://1.1.1.1/cdn-cgi/trace", HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            response.EnsureSuccessStatusCode();
            return new NoraBackendProbeResult(true, Math.Max(1, stopwatch.ElapsedMilliseconds), "ok", "HTTPS passed through the local Xray SOCKS backend before TUN startup.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NoraBackendProbeResult(false, Math.Max(1, stopwatch.ElapsedMilliseconds), "timeout", "The Xray transport did not carry HTTPS before the diagnostic timeout.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new NoraBackendProbeResult(false, Math.Max(1, stopwatch.ElapsedMilliseconds), ex.GetBaseException().GetType().Name, ex.GetBaseException().Message);
        }
    }

    public async Task VerifyDiscordPathAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await ProbeLocalSocksAsync(timeout, cancellationToken);
        if (!result.Success)
            throw new NoraAppException("NORA-DIS-9103", "The selected VLESS server did not carry Discord Mode traffic: " + result.Detail);
        log($"[discord] VLESS selective route ready through local Xray SOCKS; probe_ms={result.Milliseconds}");
    }

    internal async Task StartTunFrontendForDiagnosticsAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_tunFrontend is not null)
            return;
        if (_xray is null || _xray.HasExited || string.IsNullOrWhiteSpace(_tunConfigPath))
            throw new NoraAppException("NORA-XRY-3104", "Cannot start the Xray TUN frontend because the local Xray backend is not running.");
        cancellationToken.ThrowIfCancellationRequested();

        var singBoxExe = NoraSubscriptionStore.SingBoxPath();

        var tunPsi = new ProcessStartInfo(singBoxExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        tunPsi.ArgumentList.Add("run");
        tunPsi.ArgumentList.Add("-c");
        tunPsi.ArgumentList.Add(_tunConfigPath);

        _tunFrontend = new Process { StartInfo = tunPsi, EnableRaisingEvents = true };
        _tunFrontend.OutputDataReceived += (_, e) => HandleTunLine(e.Data);
        _tunFrontend.ErrorDataReceived += (_, e) => HandleTunLine(e.Data);
        _tunFrontend.Exited += (_, _) =>
        {
            if (!_tunReady.Task.IsCompleted)
                _tunReady.TrySetException(new NoraAppException("NORA-XRY-3104", "sing-box TUN frontend exited before it became ready"));
            _exited.TrySetResult();
        };
        if (!_tunFrontend.Start())
            throw new NoraAppException("NORA-XRY-3104", "Cannot start sing-box TUN frontend for Xray");
        _tunFrontend.BeginOutputReadLine();
        _tunFrontend.BeginErrorReadLine();
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        using var registration = linkedCts.Token.Register(() =>
            _tunReady.TrySetException(new NoraAppException("NORA-XRY-3104", "Xray TUN frontend did not become ready in time")));
        await _tunReady.Task;
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        foreach (var process in new[] { _tunFrontend, _xray })
        {
            if (process is null || process.HasExited)
                continue;
            try { process.Kill(entireProcessTree: true); } catch { }
        }
        await XrayEndpointBypass.RemoveAsync(_endpointRoutes, log);
        _endpointRoutes.Clear();
        await Task.WhenAny(_exited.Task, Task.Delay(timeout));
        _exited.TrySetResult();
    }

    public Task WaitForExitAsync() => _exited.Task;

    private void HandleXrayLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        log("[xray] " + line);
        if (line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("panic", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("access is denied", StringComparison.OrdinalIgnoreCase))
            _xrayReady.TrySetException(new NoraAppException("NORA-XRY-3103", line));
        else if ((line.Contains("Xray", StringComparison.OrdinalIgnoreCase) &&
                  line.Contains("started", StringComparison.OrdinalIgnoreCase)) ||
                 line.Contains("listening TCP", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("listening UDP", StringComparison.OrdinalIgnoreCase))
            _xrayReady.TrySetResult();
    }

    private void HandleTunLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        log("[xray-tun] " + line);
        if (line.Contains("panic", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("access is denied", StringComparison.OrdinalIgnoreCase))
            _tunReady.TrySetException(new NoraAppException("NORA-XRY-3104", line));
        else if (line.Contains("started", StringComparison.OrdinalIgnoreCase))
            _tunReady.TrySetResult();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}

internal static class XrayEndpointBypass
{
    internal sealed record RouteEntry(IPAddress Address, IPAddress Gateway, int InterfaceIndex);

    public static async Task<IReadOnlyList<RouteEntry>> InstallAsync(IReadOnlyCollection<IPAddress> addresses, Action<string> log)
    {
        var route = await FindDefaultIpv4RouteAsync().ConfigureAwait(false);
        if (route is null)
            throw new NoraAppException("NORA-XRY-3105", "Cannot install the Xray endpoint bypass route because no physical IPv4 gateway was found.");

        if (addresses.Count == 0)
            throw new NoraAppException("NORA-XRY-3105", "Cannot install endpoint bypass routes because the VLESS endpoint did not resolve to IPv4.");
        var installed = new List<RouteEntry>();
        foreach (var address in addresses)
        {
            var entry = new RouteEntry(address, route.Gateway, route.InterfaceIndex);
            var add = await RunRouteAsync(["ADD", address.ToString(), "MASK", "255.255.255.255", route.Gateway.ToString(), "METRIC", "1", "IF", route.InterfaceIndex.ToString(CultureInfo.InvariantCulture)]);
            if (add.ExitCode != 0)
            {
                var change = await RunRouteAsync(["CHANGE", address.ToString(), "MASK", "255.255.255.255", route.Gateway.ToString(), "METRIC", "1", "IF", route.InterfaceIndex.ToString(CultureInfo.InvariantCulture)]);
                if (change.ExitCode != 0)
                    throw new NoraAppException("NORA-XRY-3105", $"Cannot install endpoint bypass route for {address} via {route.Gateway} if={route.InterfaceIndex}: ADD={add.Output.Trim()} CHANGE={change.Output.Trim()}");
            }
            installed.Add(entry);
            log($"[xray] endpoint bypass route installed: {address} via {route.Gateway} if={route.InterfaceIndex}");
        }
        return installed;
    }

    public static async Task RemoveAsync(IEnumerable<RouteEntry> routes, Action<string> log)
    {
        foreach (var route in routes)
        {
            await RunRouteAsync(["DELETE", route.Address.ToString(), "MASK", "255.255.255.255", route.Gateway.ToString()]);
            log($"[xray] endpoint bypass route removed: {route.Address}");
        }
    }

    public static async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
            return parsed.AddressFamily == AddressFamily.InterNetwork ? [parsed] : [];

        try
        {
            return (await Dns.GetHostAddressesAsync(host))
                .Where(x => x.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static async Task<RouteEntry?> FindDefaultIpv4RouteAsync()
    {
        var snapshot = await NoraNetworkInterfaceCache.GetSnapshotAsync().ConfigureAwait(false);
        foreach (var ni in snapshot.Interfaces
                     .Where(x => x.OperationalStatus == OperationalStatus.Up)
                     .Where(x => x.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
                     .Where(x => !x.Name.Contains("NORA", StringComparison.OrdinalIgnoreCase) &&
                                 !x.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase) &&
                                 !x.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var ip = ni.GetIPProperties();
                var gateway = ip.GatewayAddresses
                    .Select(x => x.Address)
                    .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !x.Equals(IPAddress.Any));
                if (gateway is null)
                    continue;
                var index = ip.GetIPv4Properties()?.Index ?? 0;
                if (index > 0)
                    return new RouteEntry(IPAddress.Any, gateway, index);
            }
            catch { }
        }
        return null;
    }

    private static async Task<(int ExitCode, string Output)> RunRouteAsync(IReadOnlyList<string> arguments)
    {
        var psi = new ProcessStartInfo("route.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start route.exe");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, (await stdout) + Environment.NewLine + (await stderr));
    }
}

internal sealed class AwgCoreProcess(string configPath, Action<string> log) : IVpnCoreProcess
{
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _tunnelName = Path.GetFileNameWithoutExtension(configPath);
    private bool _installed;

    public async Task StartAsync(TimeSpan timeout)
    {
        if (!File.Exists(configPath))
            throw new NoraAppException("NORA-CFG-8001", "The AWG configuration file no longer exists.");
        if (!File.ReadAllText(configPath).Contains("[Interface]", StringComparison.OrdinalIgnoreCase))
            throw new NoraAppException("NORA-AWG-3201", "AWG backend requires a complete [Interface]/[Peer] configuration.");

        var executable = NoraSubscriptionStore.AmneziaWgPath();
        var cli = NoraSubscriptionStore.AmneziaWgCliPath();
        if (!File.Exists(executable) || !File.Exists(cli))
            throw new NoraAppException("NORA-AWG-3202", "The AmneziaWG 2.0 Windows core is missing from the NORA VPN cores directory.");

        await RunAsync(executable, ["/uninstalltunnelservice", _tunnelName], throwOnError: false);
        try
        {
            await RunAsync(executable, ["/installtunnelservice", configPath], throwOnError: true);
        }
        catch (Exception ex)
        {
            throw new NoraAppException("NORA-AWG-3203", "AmneziaWG tunnel service installation failed.", ex);
        }
        _installed = true;

        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            var result = await RunAsync(cli, ["show", _tunnelName], throwOnError: false);
            if (result.ExitCode == 0 && result.Output.Contains("interface:", StringComparison.OrdinalIgnoreCase))
            {
                log("[awg] AmneziaWG 2.0 tunnel service is running");
                return;
            }
            await Task.Delay(500, cts.Token);
        }
        throw new NoraAppException("NORA-AWG-3204", "AmneziaWG tunnel service did not become ready in time");
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        if (_installed)
        {
            var executable = NoraSubscriptionStore.AmneziaWgPath();
            if (File.Exists(executable))
                await RunAsync(executable, ["/uninstalltunnelservice", _tunnelName], throwOnError: false);
            _installed = false;
        }
        _stopped.TrySetResult();
    }

    public Task WaitForExitAsync() => _stopped.Task;

    private async Task<(int ExitCode, string Output)> RunAsync(string file, IReadOnlyList<string> arguments, bool throwOnError)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start " + Path.GetFileName(file));
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await stdout) + Environment.NewLine + (await stderr);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            log("[awg] " + line);
        if (throwOnError && process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(file)} exited with code {process.ExitCode}: {output.Trim()}");
        return (process.ExitCode, output);
    }
}

internal sealed class SingBoxCoreProcess(NoraSubscriptionServer server, Action<string> log) : IVpnCoreProcess
{
    private Process? _process;
    private string _configPath = "";
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task StartAsync(TimeSpan timeout)
    {
        var exe = NoraSubscriptionStore.SingBoxPath();
        if (!File.Exists(exe))
            throw new InvalidOperationException("sing-box.exe was not found. Put it into dist/client/cores/sing-box.exe.");
        var dir = Path.Combine(NoraAppState.DataRoot, "runtime");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "sing-box-" + server.Id + ".json");
        File.WriteAllText(_configPath, NoraSubscriptionStore.BuildSingBoxConfig(server));

        var psi = new ProcessStartInfo(exe)
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
                _ready.TrySetException(new InvalidOperationException("sing-box exited before TUN became ready"));
            _exited.TrySetResult();
        };
        if (!_process.Start())
            throw new InvalidOperationException("Cannot start sing-box core");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        using var _ = cts.Token.Register(() => _ready.TrySetException(new TimeoutException("sing-box did not become ready in time")));
        await _ready.Task;
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        var p = _process;
        if (p is null || p.HasExited)
            return;
        try { p.Kill(entireProcessTree: true); } catch { }
        await Task.WhenAny(_exited.Task, Task.Delay(timeout));
    }

    public Task WaitForExitAsync() => _exited.Task;

    private void HandleLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        log("[sing-box] " + line);
        if (line.Contains("panic", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("access is denied", StringComparison.OrdinalIgnoreCase))
            _ready.TrySetException(new InvalidOperationException(line));
        else if (line.Contains("started", StringComparison.OrdinalIgnoreCase))
            _ready.TrySetResult();
    }
}
