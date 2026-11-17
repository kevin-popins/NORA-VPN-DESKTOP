#if WINDOWS
using System.IO;
namespace Nvp;

// Compatibility with exact inert legacy maintenance and expired records. A
// display name, reserved host alone, or blackhole routing outbound is no notice.
internal static class NoraLegacySubscriptionNotice
{
    internal const string MarkerId = "00000000-0000-4000-8000-000000000001";
    internal const string ExpiredMarkerId = "00000000-0000-4000-8000-000000000000";
    internal const string ExpiredFallbackMessage = "Subscription is unavailable. Refresh its status to continue.";

    public static bool IsMaintenanceMarker(NoraSubscriptionServer server)
        => server.Host == "127.0.0.1" && server.Port == 1 &&
           string.Equals(server.Uuid, MarkerId, StringComparison.OrdinalIgnoreCase) && HasInertTransport(server);

    public static bool IsExpiredMarker(NoraSubscriptionServer server)
        => server.Host == "192.0.2.1" && server.Port == 1 &&
           string.Equals(server.Uuid, ExpiredMarkerId, StringComparison.OrdinalIgnoreCase) && HasInertTransport(server);

    public static bool IsNoticeMarker(NoraSubscriptionServer server)
        => IsMaintenanceMarker(server) || IsExpiredMarker(server);

    private static bool HasInertTransport(NoraSubscriptionServer server)
        => server.Protocol.StartsWith("VLESS", StringComparison.OrdinalIgnoreCase) &&
           (server.Network.Length == 0 || server.Network.Equals("tcp", StringComparison.OrdinalIgnoreCase) || server.Network.Equals("raw", StringComparison.OrdinalIgnoreCase)) &&
           (server.Security.Length == 0 || server.Security.Equals("none", StringComparison.OrdinalIgnoreCase)) &&
           (server.Encryption.Length == 0 || server.Encryption.Equals("none", StringComparison.OrdinalIgnoreCase)) &&
           string.IsNullOrWhiteSpace(server.Flow);

    public static string Message(NoraSubscriptionServer server)
    {
        if (!IsNoticeMarker(server)) return "";
        var text = server.Name.Trim();
        if (text.Length == 0 || text.Any(c => c == '\0' || char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            return IsExpiredMarker(server) ? ExpiredFallbackMessage :
                "VPN is temporarily unavailable. Please try again later.";
        return string.Concat(text.EnumerateRunes().Take(1024).Select(rune => rune.ToString()));
    }

    // Deterministic upgrade normalization: no I/O, current-time reads, policy
    // adoption, or mutation of transport children. Callers apply the result to
    // their freshly deserialized snapshot before selection or cached fallback.
    internal static (List<NoraSubscriptionServer> Servers, NoraSubscriptionMetadataState Metadata) FilterCachedSnapshot(NoraSubscriptionInfo subscription)
    {
        var markers = subscription.Servers.Where(IsNoticeMarker).ToArray();
        var state = subscription.AppMetadata ?? new NoraSubscriptionMetadataState();
        if (markers.Length == 0)
            return (subscription.Servers, state);
        var real = subscription.Servers.Where(server => !IsNoticeMarker(server)).ToList();
        if (state.HasV1Policy || state.LegacyNotice)
            return (real, state with { CachedOnly = state.CachedOnly || real.Count == 0 });

        var maintenance = markers.FirstOrDefault(IsMaintenanceMarker);
        var availability = maintenance is null ? "unknown" : real.Count == 0 ? "node_maintenance" : "degraded";
        var message = maintenance is null ? ExpiredFallbackMessage : Message(maintenance);
        var receipt = subscription.UpdatedAt == default ? 0 : subscription.UpdatedAt.ToUnixTimeSeconds();
        return (real, state with
        {
            Access = "unknown", Availability = availability, Message = message,
            CachedOnly = state.CachedOnly || real.Count == 0,
            LegacyNotice = true, LegacyAccess = "unknown", LegacyAvailability = availability,
            LegacyMessage = message, LegacyNoticeAtUnix = receipt,
            IssuedAtUnix = receipt, TtlSeconds = 300, AcceptedAt = subscription.UpdatedAt,
            SourceBinding = NoraSubscriptionMetadata.BindSource(subscription.SourceUrl)
        });
    }

    internal static int RunSelfTest(TextWriter output)
    {
        NoraSubscriptionServer Marker() => new()
        {
            Name = "Проводятся работы — скоро вернёмся 🌐",
            Host = "127.0.0.1", Port = 1, Uuid = MarkerId,
            Network = "tcp", Security = "none", Encryption = "none"
        };
        var marker = Marker();
        var passed = IsMaintenanceMarker(marker) && Message(marker) == marker.Name;
        var ordinary = Marker(); ordinary.Host = "node.example.com";
        passed &= !IsMaintenanceMarker(ordinary) && Message(ordinary).Length == 0;
        ordinary = Marker(); ordinary.Uuid = "00000000-0000-4000-8000-000000000002";
        passed &= !IsMaintenanceMarker(ordinary);
        ordinary = Marker(); ordinary.Port = 443;
        passed &= !IsMaintenanceMarker(ordinary);
        ordinary = Marker(); ordinary.Security = "reality";
        passed &= !IsMaintenanceMarker(ordinary);
        ordinary = Marker(); ordinary.Protocol = "Trojan";
        passed &= !IsMaintenanceMarker(ordinary);
        var expired = Marker(); expired.Host = "192.0.2.1"; expired.Uuid = ExpiredMarkerId; expired.Name = "Срок подписки истёк";
        passed &= IsExpiredMarker(expired) && IsNoticeMarker(expired) && !IsMaintenanceMarker(expired) && Message(expired) == expired.Name;
        expired.Host = "node.example.com";
        passed &= !IsExpiredMarker(expired);
        expired.Host = "192.0.2.1"; expired.Uuid = MarkerId;
        passed &= !IsExpiredMarker(expired);
        expired.Uuid = ExpiredMarkerId; expired.Port = 443;
        passed &= !IsExpiredMarker(expired);
        expired.Port = 1; expired.Security = "reality";
        passed &= !IsExpiredMarker(expired);
        output.WriteLine(passed ? "PASS: exact distinct legacy maintenance/expired shapes and provider text; ordinary endpoints preserved" : "FAIL: legacy notice recognition");
        return passed ? 0 : 1;
    }
}
#endif
