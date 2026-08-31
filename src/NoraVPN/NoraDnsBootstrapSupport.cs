using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

#if WINDOWS
namespace Nvp;

internal sealed record NoraEndpointResolution(
    IReadOnlyList<IPAddress> Addresses,
    string Source,
    bool AuthenticatedDns,
    bool UsedLastKnownGood,
    bool SystemNameError);

internal enum NoraBootstrapChoice
{
    Wait,
    AuthenticatedDns,
    SystemDns,
    LastKnownGood,
    Unavailable
}

/// <summary>
/// Resolves a tunnel endpoint before full-tunnel routes exist. Authenticated
/// DNS-over-HTTPS is connected by pinned resolver IP, so a poisoned UDP/53
/// answer cannot redirect the DoH TLS session. A successful tunnel can then
/// persist a DPAPI-protected, hostname-free last-known-good address pool.
/// </summary>
internal static class NoraDnsBootstrap
{
    private sealed record DohProvider(string Host, string Path, string[] PinnedIpv4);
    private sealed record DnsAnswer(IReadOnlyList<IPAddress> Addresses, bool NameError, string Source);
    private sealed class CacheFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, CacheEntry> Entries { get; set; } = new(StringComparer.Ordinal);
    }
    private sealed class CacheEntry
    {
        public string[] Addresses { get; set; } = [];
        public DateTimeOffset ValidatedUtc { get; set; }
    }

    private static readonly byte[] CacheEntropy = Encoding.UTF8.GetBytes("NORA VPN endpoint bootstrap cache v1");
    private static readonly object CacheGate = new();
    private static readonly HashSet<string> SuspiciousEndpoints = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);
    private static readonly DohProvider[] Providers =
    [
        new("cloudflare-dns.com", "/dns-query", ["1.1.1.1", "1.0.0.1"]),
        new("dns.google", "/dns-query", ["8.8.8.8", "8.8.4.4"]),
        new("dns.quad9.net", "/dns-query", ["9.9.9.9", "149.112.112.112"])
    ];

    public static async Task<NoraEndpointResolution> ResolveIpv4Async(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        host = NormalizeHost(host);
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (IPAddress.TryParse(host, out var literal))
        {
            var addresses = literal.AddressFamily == AddressFamily.InterNetwork ? new[] { literal } : [];
            return new NoraEndpointResolution(addresses, "literal", true, false, false);
        }

        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var cached = LoadCached(host, port, DateTimeOffset.UtcNow);
        var overallBudget = timeout < TimeSpan.FromMilliseconds(1800)
            ? timeout
            : TimeSpan.FromMilliseconds(1800);
        var stopwatch = Stopwatch.StartNew();
        var systemBudget = overallBudget < TimeSpan.FromMilliseconds(400)
            ? overallBudget
            : TimeSpan.FromMilliseconds(400);
        using var systemCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        systemCts.CancelAfter(systemBudget);
        DnsAnswer system;
        try
        {
            system = await ResolveSystemAsync(host, systemCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            system = new DnsAnswer([], false, "system-timeout");
        }
        cancellationToken.ThrowIfCancellationRequested();

        var cachedSet = cached.Select(address => address.ToString()).ToHashSet(StringComparer.Ordinal);
        var validatedSystem = system.Addresses.Where(address => cachedSet.Contains(address.ToString())).ToArray();
        if (validatedSystem.Length > 0)
            return new NoraEndpointResolution(validatedSystem, "system-confirmed-lkg", false, false, system.NameError);

        var key = EndpointKey(host, port);
        bool suspicious;
        lock (CacheGate)
            suspicious = SuspiciousEndpoints.Contains(key);

        if (system.Addresses.Count > 0 && cached.Count == 0 && !suspicious)
            return new NoraEndpointResolution(system.Addresses, "system-fallback", false, false, system.NameError);
        if (system.Addresses.Count == 0 && cached.Count > 0 && !suspicious)
            return new NoraEndpointResolution(cached, "last-known-good", false, true, system.NameError);

        var remaining = overallBudget - stopwatch.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            var authenticated = await ResolveAuthenticatedAsync(host, remaining, cancellationToken).ConfigureAwait(false);
            if (authenticated.Addresses.Count > 0)
                return new NoraEndpointResolution(authenticated.Addresses, "authenticated-doh", true, false, system.NameError);
            if (cached.Count > 0)
                return new NoraEndpointResolution(cached, "last-known-good", false, true, system.NameError);
            if (system.Addresses.Count > 0)
                return new NoraEndpointResolution(system.Addresses, "system-fallback", false, false, system.NameError);
            return new NoraEndpointResolution([], authenticated.NameError ? "authenticated-nxdomain" : "unavailable", false, false, system.NameError);
        }

        if (cached.Count > 0)
            return new NoraEndpointResolution(cached, "last-known-good", false, true, system.NameError);
        return new NoraEndpointResolution(system.Addresses, system.Addresses.Count > 0 ? "system-fallback" : "unavailable", false, false, system.NameError);
    }

    private static async Task<DnsAnswer> ResolveAuthenticatedAsync(
        string host,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var tasks = Providers.Select(provider => ResolveDohAsync(provider, host, deadline.Token)).ToList();
        var sawNameError = false;
        while (tasks.Count > 0 && !deadline.IsCancellationRequested)
        {
            Task<DnsAnswer> completed;
            try { completed = await Task.WhenAny(tasks).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { break; }
            tasks.Remove(completed);
            if (completed.Status != TaskStatus.RanToCompletion)
                continue;
            var answer = completed.Result;
            sawNameError |= answer.NameError;
            if (answer.Addresses.Count == 0)
                continue;
            deadline.Cancel();
            return answer;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new DnsAnswer([], sawNameError, "authenticated-doh");
    }

    /// <summary>
    /// Call only after the tunnel backend has authenticated and become ready.
    /// Merely receiving a system DNS answer is never enough to poison the cache.
    /// </summary>
    public static void RememberSuccessful(string host, int port, IEnumerable<IPAddress> addresses)
    {
        host = NormalizeHost(host);
        if (IPAddress.TryParse(host, out _))
            return;
        var validated = addresses.Where(IsUsableIpv4).Distinct().Take(12).Select(x => x.ToString()).ToArray();
        if (validated.Length == 0)
            return;

        lock (CacheGate)
        {
            var path = CachePath();
            SuspiciousEndpoints.Remove(EndpointKey(host, port));
            var cache = ReadCache(path);
            cache.Entries[EndpointKey(host, port)] = new CacheEntry
            {
                Addresses = validated,
                ValidatedUtc = DateTimeOffset.UtcNow
            };
            foreach (var key in cache.Entries
                         .Where(pair => DateTimeOffset.UtcNow - pair.Value.ValidatedUtc > CacheLifetime + TimeSpan.FromDays(15))
                         .Select(pair => pair.Key)
                         .ToArray())
                cache.Entries.Remove(key);
            foreach (var key in cache.Entries.OrderByDescending(x => x.Value.ValidatedUtc).Skip(256).Select(x => x.Key).ToArray())
                cache.Entries.Remove(key);
            WriteCache(path, cache);
        }
    }

    public static void Forget(string host, int port)
    {
        host = NormalizeHost(host);
        if (IPAddress.TryParse(host, out _))
            return;
        lock (CacheGate)
        {
            var key = EndpointKey(host, port);
            SuspiciousEndpoints.Add(key);
            var path = CachePath();
            var cache = ReadCache(path);
            if (!cache.Entries.Remove(key))
                return;
            WriteCache(path, cache);
        }
    }

    internal static NoraBootstrapChoice ChooseSource(
        IReadOnlyCollection<IPAddress> authenticated,
        IReadOnlyCollection<IPAddress> system,
        IReadOnlyCollection<IPAddress> cached,
        bool canWait)
    {
        if (authenticated.Count > 0)
            return NoraBootstrapChoice.AuthenticatedDns;
        // A recently authenticated tunnel endpoint is safer than a conflicting
        // positive answer from the interceptable Windows resolver. If it has
        // become stale, the failed attempt removes it and the next attempt can
        // use the fresh system result.
        if (cached.Count > 0)
            return NoraBootstrapChoice.LastKnownGood;
        if (system.Count > 0)
            return NoraBootstrapChoice.SystemDns;
        return canWait ? NoraBootstrapChoice.Wait : NoraBootstrapChoice.Unavailable;
    }

    private static NoraEndpointResolution CreateResult(
        NoraBootstrapChoice choice,
        IReadOnlyList<IPAddress> authenticated,
        DnsAnswer? system,
        IReadOnlyList<IPAddress> cached)
        => choice switch
        {
            NoraBootstrapChoice.AuthenticatedDns => new(authenticated, "authenticated-doh", true, false, system?.NameError == true),
            NoraBootstrapChoice.SystemDns => new(system?.Addresses ?? [], "system-fallback", false, false, system?.NameError == true),
            NoraBootstrapChoice.LastKnownGood => new(cached, "last-known-good", false, true, system?.NameError == true),
            _ => new([], "unavailable", false, false, system?.NameError == true)
        };

    private static IReadOnlyList<IPAddress> CompletedAuthenticated(IEnumerable<Task<DnsAnswer>> tasks)
        => tasks.Where(task => task.Status == TaskStatus.RanToCompletion)
            .Select(task => task.Result)
            .Where(answer => answer.Addresses.Count > 0)
            .SelectMany(answer => answer.Addresses)
            .Where(IsUsableIpv4)
            .Distinct()
            .Take(12)
            .ToArray();

    private static DnsAnswer? CompletedAnswer(Task<DnsAnswer> task)
        => task.Status == TaskStatus.RanToCompletion ? task.Result : null;

    private static async Task<DnsAnswer> ResolveSystemAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = (await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false))
                // Only one system-derived address is used and later cached after
                // that exact single-address pool has passed tunnel authentication.
                .Where(IsUsableIpv4).Distinct().Take(1).ToArray();
            return new DnsAnswer(addresses, false, "system");
        }
        catch (SocketException exception) when (exception.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
        {
            return new DnsAnswer([], true, "system");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DnsAnswer([], false, "system");
        }
        catch
        {
            return new DnsAnswer([], false, "system");
        }
    }

    private static async Task<DnsAnswer> ResolveDohAsync(DohProvider provider, string host, CancellationToken cancellationToken)
    {
        var query = BuildDnsQuery(host, out var queryId);
        var sawNameError = false;
        foreach (var pinnedText in provider.PinnedIpv4)
        {
            try
            {
                var pinned = IPAddress.Parse(pinnedText);
                using var handler = new SocketsHttpHandler
                {
                    UseProxy = false,
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.Zero,
                    ConnectTimeout = TimeSpan.FromMilliseconds(1200),
                    ConnectCallback = async (_, token) =>
                    {
                        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            await socket.ConnectAsync(new IPEndPoint(pinned, 443), token).ConfigureAwait(false);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    }
                };
                using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"https://{provider.Host}{provider.Path}"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
                request.Content = new ByteArrayContent(query);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;
                var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (payload.Length is < 12 or > 16384)
                    continue;
                var parsed = ParseDnsResponse(payload, queryId, provider.Host);
                sawNameError |= parsed.NameError;
                if (parsed.Addresses.Count > 0)
                    return parsed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Retry once through the provider's second pinned anycast IP.
            }
        }
        return new DnsAnswer([], sawNameError, provider.Host);
    }

    private static byte[] BuildDnsQuery(string host, out ushort id)
    {
        Span<byte> random = stackalloc byte[2];
        RandomNumberGenerator.Fill(random);
        id = BinaryPrimitives.ReadUInt16BigEndian(random);
        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100); // recursion desired
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);
        stream.Write(header);
        foreach (var label in host.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63)
                throw new ArgumentException("Endpoint hostname contains an invalid DNS label.", nameof(host));
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        stream.WriteByte(0);
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(tail, 1);       // A
        BinaryPrimitives.WriteUInt16BigEndian(tail[2..], 1); // IN
        stream.Write(tail);
        return stream.ToArray();
    }

    private static DnsAnswer ParseDnsResponse(ReadOnlySpan<byte> payload, ushort expectedId, string source)
    {
        if (payload.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(payload) != expectedId)
            return new DnsAnswer([], false, source);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(payload[2..]);
        if ((flags & 0x8000) == 0)
            return new DnsAnswer([], false, source);
        var rcode = flags & 0x000f;
        if (rcode == 3)
            return new DnsAnswer([], true, source);
        if (rcode != 0)
            return new DnsAnswer([], false, source);
        var questions = BinaryPrimitives.ReadUInt16BigEndian(payload[4..]);
        var answers = BinaryPrimitives.ReadUInt16BigEndian(payload[6..]);
        var offset = 12;
        for (var i = 0; i < questions; i++)
        {
            if (!SkipDnsName(payload, ref offset) || offset + 4 > payload.Length)
                return new DnsAnswer([], false, source);
            offset += 4;
        }
        var addresses = new List<IPAddress>();
        for (var i = 0; i < answers; i++)
        {
            if (!SkipDnsName(payload, ref offset) || offset + 10 > payload.Length)
                break;
            var type = BinaryPrimitives.ReadUInt16BigEndian(payload[offset..]);
            var dnsClass = BinaryPrimitives.ReadUInt16BigEndian(payload[(offset + 2)..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(payload[(offset + 8)..]);
            offset += 10;
            if (offset + length > payload.Length)
                break;
            if (type == 1 && dnsClass == 1 && length == 4)
                addresses.Add(new IPAddress(payload.Slice(offset, 4)));
            offset += length;
        }
        return new DnsAnswer(addresses.Where(IsUsableIpv4).Distinct().ToArray(), false, source);
    }

    private static bool SkipDnsName(ReadOnlySpan<byte> payload, ref int offset)
    {
        var labels = 0;
        while (offset < payload.Length && labels++ < 128)
        {
            var length = payload[offset++];
            if (length == 0)
                return true;
            if ((length & 0xc0) == 0xc0)
            {
                if (offset >= payload.Length)
                    return false;
                offset++;
                return true;
            }
            if ((length & 0xc0) != 0 || offset + length > payload.Length)
                return false;
            offset += length;
        }
        return false;
    }

    private static IReadOnlyList<IPAddress> LoadCached(string host, int port, DateTimeOffset now)
    {
        lock (CacheGate)
        {
            var cache = ReadCache(CachePath());
            if (!cache.Entries.TryGetValue(EndpointKey(host, port), out var entry) ||
                now < entry.ValidatedUtc || now - entry.ValidatedUtc > CacheLifetime)
                return [];
            return (entry.Addresses ?? [])
                .Select(value => IPAddress.TryParse(value, out var parsed) ? parsed : null)
                .Where(address => address is not null && IsUsableIpv4(address))
                .Cast<IPAddress>().Distinct().Take(12).ToArray();
        }
    }

    private static CacheFile ReadCache(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new CacheFile();
            var protectedBytes = File.ReadAllBytes(path);
            if (protectedBytes.Length is 0 or > 262144)
                return new CacheFile();
            var plaintext = ProtectedData.Unprotect(protectedBytes, CacheEntropy, DataProtectionScope.CurrentUser);
            var cache = JsonSerializer.Deserialize<CacheFile>(plaintext, JsonOptions) ?? new CacheFile();
            cache.Entries ??= new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
            return cache;
        }
        catch
        {
            return new CacheFile();
        }
    }

    private static void WriteCache(string path, CacheFile cache)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(cache, JsonOptions);
        var protectedBytes = ProtectedData.Protect(plaintext, CacheEntropy, DataProtectionScope.CurrentUser);
        var temporary = path + ".tmp-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        try
        {
            File.WriteAllBytes(temporary, protectedBytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string EndpointKey(string host, int port)
    {
        var material = Encoding.UTF8.GetBytes(host.ToLowerInvariant() + ":" + port.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(material));
    }

    private static string NormalizeHost(string host)
    {
        host = (host ?? "").Trim().TrimEnd('.');
        if (host.Length == 0 || host.Length > 253)
            throw new ArgumentException("Endpoint hostname is empty or too long.", nameof(host));
        if (IPAddress.TryParse(host, out _))
            return host;
        return new IdnMapping().GetAscii(host).ToLowerInvariant();
    }

    private static bool IsUsableIpv4(IPAddress? address)
        => address is not null && address.AddressFamily == AddressFamily.InterNetwork &&
           !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.Broadcast);

    private static string CachePath() => Path.Combine(NoraAppState.DataRoot, "dns-bootstrap.dat");

    internal static int RunSelfTest(TextWriter output)
    {
        try
        {
            var query = BuildDnsQuery("vpn.example", out var id);
            var response = new byte[query.Length + 16];
            query.CopyTo(response, 0);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 0x8180);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(6), 1);
            var offset = query.Length;
            response[offset++] = 0xc0; response[offset++] = 0x0c;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset), 1); offset += 2;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset), 1); offset += 2;
            BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(offset), 60); offset += 4;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(offset), 4); offset += 2;
            response[offset++] = 203; response[offset++] = 0; response[offset++] = 113; response[offset++] = 17;
            var parsed = ParseDnsResponse(response, id, "fixture");
            var authenticatedFixture = new[] { IPAddress.Parse("203.0.113.1") };
            var systemFixture = new[] { IPAddress.Parse("203.0.113.2") };
            var cacheFixture = new[] { IPAddress.Parse("203.0.113.3") };
            var policyPassed =
                ChooseSource(authenticatedFixture, systemFixture, cacheFixture, true) == NoraBootstrapChoice.AuthenticatedDns &&
                ChooseSource([], systemFixture, cacheFixture, true) == NoraBootstrapChoice.LastKnownGood &&
                ChooseSource([], systemFixture, [], true) == NoraBootstrapChoice.SystemDns &&
                ChooseSource([], [], cacheFixture, true) == NoraBootstrapChoice.LastKnownGood &&
                ChooseSource([], [], [], true) == NoraBootstrapChoice.Wait &&
                ChooseSource([], [], [], false) == NoraBootstrapChoice.Unavailable;

            var originalRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
            var testRoot = Path.Combine(Path.GetTempPath(), "nora-dns-bootstrap-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            try
            {
                Environment.SetEnvironmentVariable("NORA_DATA_ROOT", testRoot);
                RememberSuccessful("private-vpn.example", 443, [IPAddress.Parse("203.0.113.9")]);
                var protectedBytes = File.ReadAllBytes(CachePath());
                var cache = LoadCached("private-vpn.example", 443, DateTimeOffset.UtcNow);
                var wrongPort = LoadCached("private-vpn.example", 8443, DateTimeOffset.UtcNow);
                var noPlaintextHost = !Encoding.UTF8.GetString(protectedBytes).Contains("private-vpn.example", StringComparison.Ordinal);
                Forget("private-vpn.example", 443);
                var forgotten = LoadCached("private-vpn.example", 443, DateTimeOffset.UtcNow).Count == 0;
                var passed = parsed.Addresses.Select(x => x.ToString()).SequenceEqual(["203.0.113.17"]) &&
                             cache.Select(x => x.ToString()).SequenceEqual(["203.0.113.9"]) &&
                             wrongPort.Count == 0 && noPlaintextHost && forgotten && policyPassed;
                output.WriteLine(passed
                    ? "DNS BOOTSTRAP SELF-TEST PASS: RFC8484 parsing; authenticated > LKG > system policy; endpoint-key isolation; DPAPI-protected hostname-free cache"
                    : "DNS BOOTSTRAP SELF-TEST FAIL");
                return passed ? 0 : 1;
            }
            finally
            {
                Environment.SetEnvironmentVariable("NORA_DATA_ROOT", originalRoot);
                try { Directory.Delete(testRoot, recursive: true); } catch { }
            }
        }
        catch (Exception exception)
        {
            output.WriteLine("DNS BOOTSTRAP SELF-TEST FAIL: " + exception.GetBaseException().Message);
            return 1;
        }
    }
}
#endif
