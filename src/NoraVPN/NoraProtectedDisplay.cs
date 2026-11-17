#if WINDOWS
using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace Nvp;

internal static class NoraProtectedDisplay
{
    private static readonly ConcurrentDictionary<string, string[]> Sensitive = new(StringComparer.OrdinalIgnoreCase);
    public const string FallbackLabel = "Protected server";

    internal static void Register(NoraSubscriptionInfo subscription)
    {
        if (!subscription.AppMetadata.Protected)
        {
            Sensitive.TryRemove(subscription.Id, out _);
            return;
        }
        var values = new List<string> { subscription.SourceUrl, subscription.WebPageUrl };
        foreach (var server in subscription.Servers.Concat(subscription.RetainedServers))
            values.AddRange([server.Host, $"{server.Host}:{server.Port}", server.SourceLink, server.Uuid, server.Sni, server.PublicKey, server.ShortId]);
        foreach (var url in new[] { subscription.SourceUrl, subscription.WebPageUrl })
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                values.Add(uri.AbsoluteUri);
                values.Add(uri.IdnHost);
            }
        var tokens = values.Where(value => !string.IsNullOrWhiteSpace(value) && value.Length >= 4).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Keep previous endpoint tokens while protection is active, including
        // lines already held in the diagnostic view when the provider refreshes.
        Sensitive.AddOrUpdate(subscription.Id, tokens, (_, previous) => previous.Concat(tokens).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal static bool IsProtected(NoraSubscriptionServer server) => NoraSubscriptionMetadata.EffectiveFor(server).Protected;
    internal static void RegisterResolvedEndpoints(NoraSubscriptionServer server, IEnumerable<IPAddress> addresses)
    {
        if (!IsProtected(server)) return;
        var tokens = addresses.Select(address => address.ToString()).ToArray();
        Sensitive.AddOrUpdate(server.SubscriptionId, tokens, (_, previous) => previous.Concat(tokens).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal static void RequireConnectable(NoraSubscriptionServer server)
    {
        if (!NoraSubscriptionStore.IsUsableServer(server))
            throw new NoraAppException("NORA-SUB-4003", "This entry cannot be used to start a connection or connection diagnostic.");
    }
    internal static string EndpointText(NoraSubscriptionServer server)
        => IsProtected(server) ? PublicLabel(NoraSubscriptionMetadata.EffectiveFor(server).PublicLabel) : $"{server.Host}:{server.Port}";
    internal static string PublicLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return FallbackLabel;
        var safe = Redact(label);
        return safe == label ? label : FallbackLabel;
    }

    // The guard runs before invoking a clipboard, file, QR, raw editor or
    // serializer callback. Protected policy never modifies the transport itself.
    internal static bool TryDisclose(NoraSubscriptionServer server, Action disclose)
    {
        if (IsProtected(server) || NoraLegacySubscriptionNotice.IsTechnicalEntry(server)) return false;
        disclose();
        return true;
    }
    internal static void RequireDisclosure(NoraSubscriptionServer server)
    {
        if (NoraLegacySubscriptionNotice.IsTechnicalEntry(server))
            throw new NoraAppException("NORA-SUB-4001", "This technical entry does not provide exportable connection details.");
        if (IsProtected(server))
            throw new NoraAppException("NORA-SUB-4001", "This subscription does not allow revealing, copying or exporting connection details.");
    }

    internal static string Redact(string? value)
    {
        var safe = value ?? "";
        foreach (var token in Sensitive.Values.SelectMany(tokens => tokens).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(token => token.Length))
            safe = safe.Replace(token, "[protected]", StringComparison.OrdinalIgnoreCase);
        return safe;
    }
    internal static string SanitizeLog(string value) => Redact(NoraDiagnosticRedactor.Sanitize(value));

    internal static string SanitizeTargetLog(NoraSubscriptionServer server, string value)
    {
        var safe = SanitizeLog(value);
        if (!IsProtected(server) || server.Host.Length is 0 or >= 4) return safe;
        // Short local names are replaced only in this target's diagnostic
        // context, never as a global substring rule for other subscriptions.
        return Regex.Replace(safe, @"(?<![\p{L}\p{N}_.])" + Regex.Escape(server.Host) + @"(?![\p{L}\p{N}_.])",
            "[protected]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static string SanitizeSubscriptionLog(NoraSubscriptionInfo subscription, string value)
    {
        var safe = SanitizeLog(value);
        foreach (var server in subscription.Servers.Concat(subscription.RetainedServers))
            safe = SanitizeTargetLog(server, safe);
        return safe;
    }

    internal static int RunSelfTest(TextWriter output)
    {
        var protectedServer = new NoraSubscriptionServer { Id = "protected-node", SubscriptionId = "protected-fixture", Host = "private.example.com", Port = 443, Uuid = "11111111-1111-4111-8111-111111111111" };
        var plainServer = new NoraSubscriptionServer { Id = "plain-node", SubscriptionId = "plain-fixture", Host = "ordinary.example.com", Port = 443, Uuid = "22222222-2222-4222-8222-222222222222" };
        var subscription = new NoraSubscriptionInfo
        {
            Id = protectedServer.SubscriptionId, SourceUrl = "https://provider.example.com/sub/fixture-token",
            AppMetadata = new NoraSubscriptionMetadataState { Protected = true }, Servers = [protectedServer]
        };
        protectedServer.AppMetadata = subscription.AppMetadata;
        NoraSubscriptionMetadata.RegisterCommitted(subscription);
        var effects = 0;
        var passed = !TryDisclose(protectedServer, () => effects++) && effects == 0 &&
                     TryDisclose(plainServer, () => effects++) && effects == 1 &&
                     EndpointText(protectedServer) == FallbackLabel && EndpointText(plainServer) == "ordinary.example.com:443" &&
                     !Redact(subscription.SourceUrl + " " + protectedServer.Host).Contains("example.com", StringComparison.Ordinal);
        try { RequireDisclosure(protectedServer); passed = false; } catch (NoraAppException) { }
        var technical = new NoraSubscriptionServer
        {
            Host = "127.0.0.1", Port = 1, Uuid = "00000000-0000-4000-8000-000000000001",
            Network = "tcp", Security = "none", Encryption = "none", Name = "Проводятся работы 🌐"
        };
        passed &= !TryDisclose(technical, () => effects++) && effects == 1;
        try { RequireDisclosure(technical); passed = false; } catch (NoraAppException) { }
        var shortServer = new NoraSubscriptionServer
        {
            Id = "short-host-node", SubscriptionId = "short-host-fixture", Host = "vpn", Port = 443,
            Uuid = "33333333-3333-4333-8333-333333333333"
        };
        var shortSubscription = new NoraSubscriptionInfo
        {
            Id = shortServer.SubscriptionId, SourceUrl = "https://provider.example.com/short-fixture",
            AppMetadata = new NoraSubscriptionMetadataState { Protected = true }, Servers = [shortServer]
        };
        NoraSubscriptionMetadata.RegisterCommitted(shortSubscription);
        passed &= !SanitizeTargetLog(shortServer, "dial vpn:443; hostname vpn").Contains("vpn", StringComparison.OrdinalIgnoreCase) &&
                  Redact("unrelated vpn label").Contains("vpn", StringComparison.Ordinal);
        Sensitive.TryRemove(shortSubscription.Id, out _);
        Sensitive.TryRemove(subscription.Id, out _);
        output.WriteLine(passed ? "PASS: per-subscription disclosure guards precede side effects; source URL/endpoint redacted; safe empty label" : "FAIL: protected disclosure policy");
        return passed ? 0 : 1;
    }
}
#endif
