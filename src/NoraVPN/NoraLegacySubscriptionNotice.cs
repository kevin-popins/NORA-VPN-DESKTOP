#if WINDOWS
using System.Text.Json;

namespace Nvp;

// Exact existing provider records are display rows, never transport. Names,
// arbitrary loopback endpoints and blackhole routing are not technical rows.
internal static class NoraLegacySubscriptionNotice
{
    internal const string MarkerId = "00000000-0000-4000-8000-000000000001";
    internal const string ExpiredMarkerId = "00000000-0000-4000-8000-000000000000";

    internal static bool IsTechnicalEntry(NoraSubscriptionServer server)
        => server.IsTechnical ||
           ((server.Host == "127.0.0.1" && string.Equals(server.Uuid, MarkerId, StringComparison.OrdinalIgnoreCase)) ||
            (server.Host == "192.0.2.1" && string.Equals(server.Uuid, ExpiredMarkerId, StringComparison.OrdinalIgnoreCase))) &&
           server.Port == 1 && server.Protocol.StartsWith("VLESS", StringComparison.OrdinalIgnoreCase) &&
           (server.Network.Length == 0 || server.Network.Equals("tcp", StringComparison.OrdinalIgnoreCase) || server.Network.Equals("raw", StringComparison.OrdinalIgnoreCase)) &&
           (server.Security.Length == 0 || server.Security.Equals("none", StringComparison.OrdinalIgnoreCase)) &&
           (server.Encryption.Length == 0 || server.Encryption.Equals("none", StringComparison.OrdinalIgnoreCase)) &&
           string.IsNullOrWhiteSpace(server.Flow);

    internal static void NormalizeRows(NoraSubscriptionInfo subscription)
    {
        foreach (var server in subscription.Servers)
        {
            server.IsTechnical = IsTechnicalEntry(server);
            server.IsRetained = false;
            if (server.IsTechnical) PreserveProviderName(server);
        }
        subscription.RetainedServers = subscription.RetainedServers.Where(server => !IsTechnicalEntry(server)).ToList();
        foreach (var server in subscription.RetainedServers) server.IsRetained = true;
    }

    internal static void PreserveProviderName(NoraSubscriptionServer server)
    {
        if (!IsTechnicalEntry(server)) return;
        server.IsTechnical = true;
        if (server.SourceLink.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            // One URI decoding pass; no Trim, normalization, flag stripping,
            // second decoding pass or canonical Uri.Fragment transformation.
            var delimiter = server.SourceLink.IndexOf('#');
            server.Name = delimiter < 0 ? "" : Uri.UnescapeDataString(server.SourceLink[(delimiter + 1)..]);
        }
        else if (!string.IsNullOrWhiteSpace(server.RawConfigJson))
        {
            using var document = JsonDocument.Parse(server.RawConfigJson);
            if (document.RootElement.TryGetProperty("remarks", out var remarks) && remarks.ValueKind == JsonValueKind.String)
                server.Name = remarks.GetString()!;
        }
    }
}
#endif
