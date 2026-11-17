#if WINDOWS
using System.IO;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nvp;

internal sealed record NoraSubscriptionMetadataState
{
    public bool Protected { get; init; }
    public string PublicLabel { get; init; } = "";
    public string Access { get; init; } = "unknown";
    public string Availability { get; init; } = "unknown";
    public string Message { get; init; } = "";
    public bool MessageIsNull { get; init; } = true;
    public bool CachedOnly { get; init; }
    public long Revision { get; init; }
    public long IssuedAtUnix { get; init; }
    public int TtlSeconds { get; init; }
    public DateTimeOffset AcceptedAt { get; init; }
    public bool HasV1Policy { get; init; }
    public bool V1Protected { get; init; }
    public bool LegacyProtected { get; init; }
    public bool LegacyNotice { get; init; }
    public string LegacyMessage { get; init; } = "";
    public string LegacyAccess { get; init; } = "unknown";
    public string LegacyAvailability { get; init; } = "unknown";
    public long LegacyNoticeAtUnix { get; init; }
    // Local-only bindings; never enter a generated core configuration.
    public string SourceBinding { get; init; } = "";
    public string ScopeId { get; init; } = "";
    public long LocalGeneration { get; init; }
}

internal static class NoraSubscriptionMetadata
{
    private const long MaximumRevision = 9_007_199_254_740_991;
    private static readonly ConcurrentDictionary<string, NoraSubscriptionMetadataState> Committed = new(StringComparer.Ordinal);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> AccessValues = new(StringComparer.Ordinal)
        { "active", "expired", "quota_exhausted", "disabled", "pending", "unknown" };
    private static readonly HashSet<string> AvailabilityValues = new(StringComparer.Ordinal)
        { "available", "degraded", "node_maintenance", "outage", "no_nodes", "unknown" };

    public static void AddNegotiationHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("X-Nora-Client", "nora-windows");
        request.Headers.TryAddWithoutValidation("X-Nora-Metadata-Accept", "1");
        request.Headers.TryAddWithoutValidation("X-Nora-Capabilities", "display-v1,status-v1");
    }

    internal static string BindSource(string source)
    {
        var normalized = source.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            normalized = uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    internal static bool SameTrustedOrigin(Uri authorizedSource, Uri finalSource)
        => authorizedSource.Scheme == Uri.UriSchemeHttps && finalSource.Scheme == Uri.UriSchemeHttps &&
           string.Equals(authorizedSource.IdnHost, finalSource.IdnHost, StringComparison.OrdinalIgnoreCase) &&
           authorizedSource.Port == finalSource.Port;

    internal static NoraSubscriptionMetadataState? ReadResponse(
        HttpResponseMessage response, Uri authorizedSource, string source, DateTimeOffset now)
    {
        if (response.StatusCode is not (System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.NoContent)) return null;
        var finalSource = response.RequestMessage?.RequestUri ?? authorizedSource;
        if (!SameTrustedOrigin(authorizedSource, finalSource))
            return null;
        var values = new List<string>();
        if (response.Headers.TryGetValues("X-Nora-Metadata", out var headerValues)) values.AddRange(headerValues);
        if (response.Content.Headers.TryGetValues("X-Nora-Metadata", out var contentValues)) values.AddRange(contentValues);
        return values.Count == 1 ? ParseHeader(values[0], BindSource(source), now) : null;
    }

    internal static bool HasResponseHeader(HttpResponseMessage response)
        => response.Headers.Contains("X-Nora-Metadata") || response.Content.Headers.Contains("X-Nora-Metadata");

    internal static NoraSubscriptionMetadataState? ParseHeader(string value, string sourceBinding, DateTimeOffset now)
    {
        try
        {
            if (value.Length is 0 or > 4096 || value.Any(character =>
                    !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) || value.Length % 4 == 1)
                return null;
            var encoded = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='));
            if (bytes.Length > 3072 || Encode(bytes) != value) return null;
            using var document = JsonDocument.Parse(StrictUtf8.GetString(bytes), new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateKeys(root) ||
                !Integer(root, "v", out var version) || version != 1 || !Text(root, "mode", out var mode) || mode != "full" ||
                !Integer(root, "revision", out var revision) || revision < 0 || revision > MaximumRevision ||
                !Integer(root, "issued_at", out var issued) || !Integer(root, "ttl_seconds", out var ttl) || ttl is < 30 or > 86400 ||
                !root.TryGetProperty("display", out var display) || display.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object ||
                !display.TryGetProperty("protected", out var protection) || protection.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !NullableText(display, "public_label", out var label) || (label is not null && !ValidText(label, 80, 256, allowEmpty: false)) ||
                !Text(status, "access", out var access) || !AccessValues.Contains(access) ||
                !Text(status, "availability", out var availability) || !AvailabilityValues.Contains(availability) ||
                !NullableText(status, "message", out var message) || (message is not null && !ValidText(message, 512, 1024, allowEmpty: true)))
                return null;
            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issued);
            if (issuedAt > now.AddSeconds(300) || issuedAt.AddSeconds(ttl) <= now) return null;
            return new NoraSubscriptionMetadataState
            {
                Protected = protection.GetBoolean(), V1Protected = protection.GetBoolean(), PublicLabel = label ?? "",
                Access = access, Availability = availability, Message = message ?? "", MessageIsNull = message is null, Revision = revision,
                IssuedAtUnix = issued, TtlSeconds = (int)ttl, AcceptedAt = now, HasV1Policy = true, SourceBinding = sourceBinding
            };
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static bool ValidText(string text, int scalarLimit, int byteLimit, bool allowEmpty)
    {
        if ((!allowEmpty && text.Length == 0) || !text.IsNormalized(NormalizationForm.FormC) || StrictUtf8.GetByteCount(text) > byteLimit)
            return false;
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (++count > scalarLimit || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                return false;
        }
        return true;
    }

    private static bool HasDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateKeys(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) if (HasDuplicateKeys(item)) return true;
        return false;
    }

    private static bool Integer(JsonElement parent, string name, out long value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number &&
               element.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0 && element.TryGetInt64(out value);
    }
    private static bool Text(JsonElement parent, string name, out string value)
    {
        value = "";
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString()!;
        return true;
    }
    private static bool NullableText(JsonElement parent, string name, out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var element)) return false;
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return true;
    }

    internal static NoraSubscriptionMetadataState Merge(
        NoraSubscriptionMetadataState? previous,
        NoraSubscriptionMetadataState incoming,
        string sourceBinding,
        DateTimeOffset now,
        out bool acceptedV1)
    {
        previous = previous?.SourceBinding == sourceBinding ? previous : null;
        var retained = previous ?? new NoraSubscriptionMetadataState { SourceBinding = sourceBinding };
        acceptedV1 = false;
        if (incoming.HasV1Policy && incoming.SourceBinding == sourceBinding && IsFresh(incoming, now))
        {
            var samePolicy = incoming.V1Protected == retained.V1Protected && incoming.PublicLabel == retained.PublicLabel;
            var sameStatus = incoming.Access == retained.Access && incoming.Availability == retained.Availability &&
                             incoming.Message == retained.Message && incoming.MessageIsNull == retained.MessageIsNull;
            var sameSnapshot = samePolicy && sameStatus && incoming.TtlSeconds == retained.TtlSeconds;
            if (!retained.HasV1Policy || (incoming.Revision >= retained.Revision && incoming.IssuedAtUnix >= retained.IssuedAtUnix &&
                (incoming.Revision != retained.Revision || samePolicy) &&
                (incoming.Revision != retained.Revision || incoming.IssuedAtUnix != retained.IssuedAtUnix || sameSnapshot)))
            {
                retained = incoming with { ScopeId = retained.ScopeId, LegacyProtected = false };
                acceptedV1 = true;
            }
        }
        if (!retained.HasV1Policy && !incoming.HasV1Policy)
            retained = incoming with { Protected = retained.Protected || incoming.Protected, SourceBinding = sourceBinding, ScopeId = retained.ScopeId };
        if (incoming.LegacyProtected)
            retained = retained with { Protected = true, LegacyProtected = true };
        if (incoming.LegacyNotice && !acceptedV1)
            retained = retained with
            {
                LegacyNotice = true, LegacyMessage = incoming.LegacyMessage, LegacyNoticeAtUnix = incoming.LegacyNoticeAtUnix,
                LegacyAccess = incoming.LegacyAccess, LegacyAvailability = incoming.LegacyAvailability,
                CachedOnly = retained.CachedOnly || incoming.CachedOnly
            };
        if (acceptedV1)
            retained = retained with
            {
                LegacyNotice = false, LegacyMessage = "", LegacyAccess = "unknown", LegacyAvailability = "unknown", LegacyNoticeAtUnix = 0,
                CachedOnly = IsLiveState(retained) ? false : retained.CachedOnly
            };
        return retained;
    }

    private static bool IsFresh(NoraSubscriptionMetadataState state, DateTimeOffset now)
    {
        try
        {
            var issued = DateTimeOffset.FromUnixTimeSeconds(state.IssuedAtUnix);
            return state.TtlSeconds is >= 30 and <= 86400 && issued <= now.AddSeconds(300) && issued.AddSeconds(state.TtlSeconds) > now;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    internal static bool IsNoticeState(NoraSubscriptionMetadataState state)
        => (state.Access is "expired" or "quota_exhausted" or "disabled" or "pending" or "unknown") ||
           state.Access == "active" && (state.Availability is "node_maintenance" or "outage" or "no_nodes" or "unknown");

    internal static bool IsLiveState(NoraSubscriptionMetadataState state)
        => state.Access == "active" && (state.Availability is "available" or "degraded");

    internal static void RegisterCommitted(NoraSubscriptionInfo subscription)
    {
        var binding = BindSource(subscription.SourceUrl);
        var state = subscription.AppMetadata ?? new NoraSubscriptionMetadataState();
        if (state.SourceBinding.Length == 0) state = state with { SourceBinding = binding };
        if (state.ScopeId.Length == 0) state = state with { ScopeId = "legacy-" + subscription.Id };
        state = Committed.AddOrUpdate(RegistryKey(subscription.Id, state), state, (_, current) =>
            current.LocalGeneration > state.LocalGeneration || current.HasV1Policy && (!state.HasV1Policy || current.Revision > state.Revision ||
                current.Revision == state.Revision && current.IssuedAtUnix > state.IssuedAtUnix) ? current : state);
        subscription.AppMetadata = state;
        foreach (var server in subscription.Servers) server.AppMetadata = state;
        NoraProtectedDisplay.Register(subscription);
    }

    internal static void Forget(NoraSubscriptionInfo subscription)
        => Committed.TryRemove(RegistryKey(subscription.Id, subscription.AppMetadata), out _);

    private static string RegistryKey(string subscriptionId, NoraSubscriptionMetadataState state)
        => NoraAppState.DataRoot + "\0" + subscriptionId + "\0" + state.ScopeId + "\0" + state.SourceBinding;

    public static NoraSubscriptionMetadataState EffectiveFor(NoraSubscriptionServer server, DateTimeOffset? now = null)
    {
        var state = server.AppMetadata ?? new NoraSubscriptionMetadataState();
        if (Committed.TryGetValue(RegistryKey(server.SubscriptionId, state), out var committed)) state = committed;
        return ProjectFreshness(state, now ?? DateTimeOffset.UtcNow);
    }

    public static NoraSubscriptionMetadataState EffectiveFor(NoraSubscriptionInfo subscription, DateTimeOffset? now = null)
    {
        var state = subscription.AppMetadata ?? new NoraSubscriptionMetadataState();
        if (Committed.TryGetValue(RegistryKey(subscription.Id, state), out var committed)) state = committed;
        return ProjectFreshness(state, now ?? DateTimeOffset.UtcNow);
    }

    private static NoraSubscriptionMetadataState ProjectFreshness(NoraSubscriptionMetadataState state, DateTimeOffset now)
    {
        var fresh = IsFresh(state, now);
        var effective = fresh ? state : state with { Access = "unknown", Availability = "unknown", Message = "" };
        if (state.HasV1Policy && fresh)
            return effective;
        if (state.LegacyNotice && state.LegacyNoticeAtUnix <= now.ToUnixTimeSeconds() + 300 && state.LegacyNoticeAtUnix > now.ToUnixTimeSeconds() - 300)
            effective = effective with
            {
                Access = state.LegacyAccess,
                Availability = state.LegacyAvailability,
                Message = state.LegacyMessage
            };
        return effective;
    }

    public static int RunSelfTest(TextWriter output)
    {
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var issued = now.AddSeconds(-20).ToUnixTimeSeconds();
        var source = "https://subscription.example.test/metadata-fixture";
        var sourceUri = new Uri(source);
        var binding = BindSource(source);
        const string body = "vless://11111111-1111-4111-8111-111111111111@198.51.100.9:443?security=tls&type=ws&path=%2Ffixture#Fixture";
        var originalRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
        var temporaryRoot = System.IO.Path.Combine(NoraSelfTestPaths.BaseDirectory, "nora-metadata-selftest-" + Guid.NewGuid().ToString("N"));
        string Json(bool protect = true, long revision = 5, long? stamp = null, int ttl = 300,
            string access = "active", string availability = "available", string? message = null, string? label = "Рабочая сеть 🌐")
            => JsonSerializer.Serialize(new
            {
                v = 1, mode = "full", revision, issued_at = stamp ?? issued, ttl_seconds = ttl,
                display = new { @protected = protect, public_label = label },
                status = new { access, availability, message }
            });
        string Header(string json) => Encode(StrictUtf8.GetBytes(json));
        try
        {
            var initial = ParseHeader(Header(Json()), binding, now)!;
            var parserPassed = initial is { Protected: true, HasV1Policy: true, Revision: 5 } &&
                initial.PublicLabel == "Рабочая сеть 🌐";
            var invalidJson = new[]
            {
                Json().Replace("\"v\":1", "\"v\":1,\"v\":1", StringComparison.Ordinal),
                Json().Replace("\"v\":1", "\"v\":2", StringComparison.Ordinal),
                Json().Replace("\"mode\":\"full\"", "\"mode\":\"patch\"", StringComparison.Ordinal),
                Json().Replace("\"protected\":true", "\"protected\":\"true\"", StringComparison.Ordinal),
                Json().Replace("\"protected\":true,", "", StringComparison.Ordinal),
                Json(stamp: now.AddSeconds(301).ToUnixTimeSeconds()), Json(stamp: now.AddSeconds(-301).ToUnixTimeSeconds()),
                Json(ttl: 29), Json(ttl: 86401), Json(revision: -1), Json(revision: MaximumRevision + 1),
                Json(access: "invented"), Json(availability: "invented"), Json(label: ""),
                Json(label: new string('x', 81)), Json(label: "bad\r\ntext"), Json(label: "bad\u202Etext"),
                Json(label: "bad\u200Btext"), Json(message: new string('x', 513)), Json(message: "bad\ntext")
            };
            parserPassed &= invalidJson.All(json => ParseHeader(Header(json), binding, now) is null) &&
                ParseHeader(Header(Json()) + "=", binding, now) is null &&
                ParseHeader(new string('A', 4097), binding, now) is null &&
                ParseHeader(Encode([0xff, 0xfe]), binding, now) is null &&
                ParseHeader(Header(Json(label: null, message: "")), binding, now) is not null;

            var idempotent = Merge(initial, initial with { AcceptedAt = now.AddSeconds(1) }, binding, now, out var idempotentAccepted);
            var ttlConflict = Merge(initial, initial with { TtlSeconds = 600 }, binding, now, out var ttlAccepted);
            var statusConflict = Merge(initial, initial with { Message = "different", MessageIsNull = false }, binding, now, out var statusAccepted);
            var sameRevisionClear = Merge(initial, initial with { Protected = false, V1Protected = false, IssuedAtUnix = issued + 1 }, binding, now, out var conflictAccepted);
            var replay = Merge(initial, initial with { Revision = 4 }, binding, now, out var replayAccepted);
            var olderStatus = Merge(initial, initial with { Revision = 6, IssuedAtUnix = issued - 1 }, binding, now, out var oldAccepted);
            var crossSource = Merge(initial, initial with { SourceBinding = BindSource(source + "-other"), Protected = false, V1Protected = false, Revision = 6 }, binding, now, out var crossAccepted);
            var orderingPassed = idempotentAccepted && idempotent.Protected && !ttlAccepted && ttlConflict.TtlSeconds == 300 &&
                !statusAccepted && statusConflict.Message == "" && !conflictAccepted && sameRevisionClear.Protected &&
                !replayAccepted && replay.Protected && !oldAccepted && olderStatus.Protected && !crossAccepted && crossSource.Protected;

            async Task<NoraSubscriptionInfo> Fetch(System.Net.HttpStatusCode status, string payload, string? metadata,
                Uri? finalUri = null, bool duplicate = false, string? requestSource = null, bool legacyExpired = false)
            {
                var fetchSource = requestSource ?? source;
                using var handler = new MetadataFixtureHandler(request =>
                {
                    if (request.Headers.GetValues("X-Nora-Client").Single() != "nora-windows" ||
                        request.Headers.GetValues("X-Nora-Metadata-Accept").Single() != "1" ||
                        request.Headers.GetValues("X-Nora-Capabilities").Single() != "display-v1,status-v1")
                        throw new InvalidOperationException("fixture negotiation missing");
                    var response = new HttpResponseMessage(status)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "text/plain"),
                        RequestMessage = finalUri is null ? request : new HttpRequestMessage(HttpMethod.Get, finalUri)
                    };
                    if (metadata is not null)
                        response.Headers.TryAddWithoutValidation("X-Nora-Metadata", duplicate ? [metadata, metadata] : [metadata]);
                    if (legacyExpired)
                        response.Headers.TryAddWithoutValidation("x-nora-subscription-expired", "true");
                    return response;
                });
                using var http = new HttpClient(handler);
                return await NoraSubscriptionStore.FetchUrlCandidateAsync(http, new Uri(fetchSource), _ => { }, "fixture-hwid", "fixture-device",
                    utcNow: () => now).ConfigureAwait(false);
            }
            bool Rejects(System.Net.HttpStatusCode status, string payload, string? metadata)
            {
                try { _ = Fetch(status, payload, metadata).GetAwaiter().GetResult(); return false; }
                catch (NoraAppException) { return true; }
            }
            async Task<NoraSubscriptionInfo> RedirectChain(bool externalHop, bool downgradeHop, bool includeEnvelope)
            {
                using var handler = new MetadataFixtureHandler(request =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found) { RequestMessage = request };
                    if (path == sourceUri.AbsolutePath)
                    {
                        response.Headers.Location = new Uri(externalHop ? "https://external.example.test/hop" :
                            downgradeHop ? "http://subscription.example.test/hop" : "https://subscription.example.test/hop");
                    }
                    else if (path == "/hop")
                    {
                        response.Headers.Location = new Uri("https://subscription.example.test/returned");
                    }
                    else
                    {
                        response.StatusCode = System.Net.HttpStatusCode.OK;
                        response.Content = new StringContent(body, Encoding.UTF8, "text/plain");
                        if (includeEnvelope)
                            response.Headers.TryAddWithoutValidation("X-Nora-Metadata", Header(Json(false, 6, issued + 1)));
                    }
                    return response;
                });
                using var http = new HttpClient(handler);
                return await NoraSubscriptionStore.FetchUrlCandidateAsync(http, sourceUri, _ => { }, "fixture-hwid", "fixture-device",
                    utcNow: () => now).ConfigureAwait(false);
            }

            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", temporaryRoot);
            var imported = Fetch(System.Net.HttpStatusCode.OK, body, Header(Json())).GetAwaiter().GetResult();
            NoraSubscriptionStore.Store(imported, source, utcNow: now);
            var oldActiveNode = imported.Servers.Single();
            var selectedPath = oldActiveNode.LocalPath;
            var identity = NoraSubscriptionStore.RuntimeIdentity(oldActiveNode);
            var raw = oldActiveNode.RawConfigJson;
            var sourceLink = oldActiveNode.SourceLink;
            var committedPath = Path.Combine(NoraSubscriptionStore.Root, imported.Id, "subscription.json");
            var beforeInvalidTransport = File.ReadAllText(committedPath);
            const string rawVless = """{"outbounds":[{"protocol":"vless","settings":{"vnext":[{"address":"198.51.100.9","port":443,"users":[{"id":"provider-custom-identity"}]}]}}]}""";
            const string modernVless = """{"remarks":"Modern VLESS fixture","outbounds":[{"protocol":"vless","settings":{"address":"198.51.100.9","port":443,"id":"provider-modern-custom-identity","encryption":"none"},"streamSettings":{"network":"ws","security":"tls","wsSettings":{"path":"/modern%2Fexact"}}}]}""";
            const string rawTrojan = """{"outbounds":[{"protocol":"trojan","settings":{"servers":[{"address":"198.51.100.9","port":443,"password":"exact credential %+ value"}]}}]}""";
            var brokenTransports = new (string Name, string Payload)[]
            {
                ("vless_port_zero", rawVless.Replace("\"port\":443", "\"port\":0", StringComparison.Ordinal)),
                ("vless_port_large", rawVless.Replace("\"port\":443", "\"port\":65536", StringComparison.Ordinal)),
                ("vless_port_missing", rawVless.Replace("\"port\":443,", "", StringComparison.Ordinal)),
                ("vless_port_wrong_type", rawVless.Replace("\"port\":443", "\"port\":\"443\"", StringComparison.Ordinal)),
                ("modern_vless_port_zero", modernVless.Replace("\"port\":443", "\"port\":0", StringComparison.Ordinal)),
                ("modern_vless_port_large", modernVless.Replace("\"port\":443", "\"port\":65536", StringComparison.Ordinal)),
                ("modern_vless_id_missing", modernVless.Replace(",\"id\":\"provider-modern-custom-identity\"", "", StringComparison.Ordinal)),
                ("modern_vless_id_wrong_type", modernVless.Replace("\"id\":\"provider-modern-custom-identity\"", "\"id\":1", StringComparison.Ordinal)),
                ("modern_vless_malformed_vnext", modernVless.Replace("\"settings\":{", "\"settings\":{\"vnext\":null,", StringComparison.Ordinal)),
                ("vmess_port_zero", rawVless.Replace("\"protocol\":\"vless\"", "\"protocol\":\"vmess\"", StringComparison.Ordinal).Replace("\"port\":443", "\"port\":0", StringComparison.Ordinal)),
                ("vmess_credential_missing", rawVless.Replace("\"protocol\":\"vless\"", "\"protocol\":\"vmess\"", StringComparison.Ordinal).Replace("\"id\":\"provider-custom-identity\"", "", StringComparison.Ordinal)),
                ("trojan_port_zero", rawTrojan.Replace("\"port\":443", "\"port\":0", StringComparison.Ordinal)),
                ("trojan_port_large", rawTrojan.Replace("\"port\":443", "\"port\":65536", StringComparison.Ordinal)),
                ("trojan_credential_missing", rawTrojan.Replace(",\"password\":\"exact credential %+ value\"", "", StringComparison.Ordinal)),
                ("trojan_credential_empty", rawTrojan.Replace("exact credential %+ value", "", StringComparison.Ordinal)),
                ("trojan_credential_wrong_type", rawTrojan.Replace("\"password\":\"exact credential %+ value\"", "\"password\":1", StringComparison.Ordinal)),
                ("trojan_second_endpoint_missing_credential", """{"outbounds":[{"protocol":"trojan","settings":{"servers":[{"address":"198.51.100.9","port":443,"password":"first-valid"},{"address":"198.51.100.10","port":443}]}}]}"""),
                ("vless_second_endpoint_port_zero", """{"outbounds":[{"protocol":"vless","settings":{"vnext":[{"address":"198.51.100.9","port":443,"users":[{"id":"first-valid"}]},{"address":"198.51.100.10","port":0,"users":[{"id":"second-valid"}]}]}}]}"""),
                ("vless_uri_port_zero", body.Replace(":443?", ":0?", StringComparison.Ordinal)),
                ("trojan_uri_port_zero", "trojan://exact-credential@198.51.100.9:0?security=tls#Fixture"),
                ("trojan_uri_credential_missing", "trojan://198.51.100.9:443?security=tls#Fixture")
            };
            var acceptedBrokenTransports = new List<string>();
            foreach (var fixture in brokenTransports)
            {
                try
                {
                    var broken = Fetch(System.Net.HttpStatusCode.OK, fixture.Payload, Header(Json(false, 6, issued + 1))).GetAwaiter().GetResult();
                    NoraSubscriptionStore.Store(broken, source, utcNow: now);
                    acceptedBrokenTransports.Add(fixture.Name);
                }
                catch (NoraAppException) { }
            }
            var validTrojan = NoraSubscriptionStore.ParsePayload(rawTrojan, source, new Dictionary<string, string>(), _ => { }).Servers.Single();
            using var validTrojanConfig = JsonDocument.Parse(NoraSubscriptionStore.BuildXrayConfig(validTrojan));
            var invalidTransportPassed = acceptedBrokenTransports.Count == 0 &&
                beforeInvalidTransport == File.ReadAllText(committedPath) && EffectiveFor(oldActiveNode, now).Protected &&
                EffectiveFor(oldActiveNode, now).Revision == 5 && EffectiveFor(oldActiveNode, now).IssuedAtUnix == issued &&
                NoraSubscriptionStore.RuntimeIdentity(NoraSubscriptionStore.LoadAll().Single().Servers.Single()) == identity &&
                validTrojanConfig.RootElement.GetProperty("outbounds")[0].GetProperty("settings").GetProperty("servers")[0].GetProperty("password").GetString() == "exact credential %+ value";
            if (acceptedBrokenTransports.Count != 0)
                output.WriteLine("UNUSABLE TRANSPORT FIXTURE FAIL: " + string.Join(",", acceptedBrokenTransports));

            var beforeRedirects = File.ReadAllText(committedPath);
            var redirectChainPassed = true;
            foreach (var external in new[] { true, false })
            {
                try
                {
                    var tainted = RedirectChain(externalHop: external, downgradeHop: !external, includeEnvelope: true).GetAwaiter().GetResult();
                    NoraSubscriptionStore.Store(tainted, source, utcNow: now);
                    redirectChainPassed = false;
                }
                catch (NoraAppException) { }
            }
            var sameOrigin = RedirectChain(externalHop: false, downgradeHop: false, includeEnvelope: true).GetAwaiter().GetResult();
            redirectChainPassed &= sameOrigin.AppMetadata.HasV1Policy && !sameOrigin.AppMetadata.Protected &&
                sameOrigin.AppMetadata.Revision == 6 && sameOrigin.SourceUrl == source &&
                File.ReadAllText(committedPath) == beforeRedirects && EffectiveFor(oldActiveNode, now).Protected;
            var legacyRedirect = RedirectChain(externalHop: true, downgradeHop: false, includeEnvelope: false).GetAwaiter().GetResult();
            NoraSubscriptionStore.Store(legacyRedirect, source, utcNow: now);
            redirectChainPassed &= EffectiveFor(oldActiveNode, now).Protected && EffectiveFor(oldActiveNode, now).Revision == 5;

            var safeLogs = new List<string>();
            var logBufferedBeforeSuccess = false;
            _ = NoraSubscriptionStore.RunWithProtectedLogAsync(bufferedLog =>
            {
                bufferedLog("Resolved 198.51.100.9 country as Fixture");
                logBufferedBeforeSuccess = safeLogs.Count == 0;
                return Task.FromResult(imported);
            }, safeLogs.Add).GetAwaiter().GetResult();
            var failedLogs = new List<string>();
            try
            {
                _ = NoraSubscriptionStore.RunWithProtectedLogAsync(bufferedLog =>
                {
                    bufferedLog("Resolved 198.51.100.9 before fixture failure");
                    return Task.FromException<NoraSubscriptionInfo>(new IOException("fixture store failure"));
                }, failedLogs.Add).GetAwaiter().GetResult();
            }
            catch (IOException) { }
            var logPrivacyPassed = logBufferedBeforeSuccess && safeLogs.Count == 1 &&
                                   !safeLogs[0].Contains("198.51.100.9", StringComparison.Ordinal) && failedLogs.Count == 0;
            var failedBodyPassed = Rejects(System.Net.HttpStatusCode.OK, "{malformed", Header(Json(false, 6, issued + 1))) &&
                EffectiveFor(oldActiveNode, now).Protected;
            var invalid204Passed = Rejects(System.Net.HttpStatusCode.NoContent, "", null) &&
                Rejects(System.Net.HttpStatusCode.NoContent, "", Header(Json())) &&
                Rejects(System.Net.HttpStatusCode.NoContent, "unexpected", Header(Json(availability: "node_maintenance"))) &&
                Rejects(System.Net.HttpStatusCode.NotModified, "", Header(Json(false, 6, issued + 1))) &&
                Rejects(System.Net.HttpStatusCode.Forbidden, body, Header(Json(false, 6, issued + 1))) &&
                Rejects(System.Net.HttpStatusCode.InternalServerError, body, Header(Json(false, 6, issued + 1)));

            var absent = Fetch(System.Net.HttpStatusCode.OK, body, null).GetAwaiter().GetResult();
            NoraSubscriptionStore.Store(absent, source, utcNow: now);
            var invalidEnvelopesFailed = Rejects(System.Net.HttpStatusCode.OK, body,
                Header(Json(false, 6, now.AddSeconds(-301).ToUnixTimeSeconds())));
            foreach (var candidate in new[]
            {
                Fetch(System.Net.HttpStatusCode.OK, body, Header(Json(false, 4, issued + 1))).GetAwaiter().GetResult(),
                Fetch(System.Net.HttpStatusCode.OK, body, Header(Json(false, 5, issued + 1))).GetAwaiter().GetResult()
            })
            {
                try { NoraSubscriptionStore.Store(candidate, source, utcNow: now); invalidEnvelopesFailed = false; }
                catch (NoraAppException) { }
            }
            foreach (var invalidOrigin in new[] { new Uri("https://other.example.test/redirect"), new Uri("http://subscription.example.test/downgrade") })
            {
                try { _ = Fetch(System.Net.HttpStatusCode.OK, body, Header(Json(false, 6, issued + 1)), finalUri: invalidOrigin).GetAwaiter().GetResult(); invalidEnvelopesFailed = false; }
                catch (NoraAppException) { }
            }
            try { _ = Fetch(System.Net.HttpStatusCode.OK, body, Header(Json(false, 6, issued + 1)), duplicate: true).GetAwaiter().GetResult(); invalidEnvelopesFailed = false; }
            catch (NoraAppException) { }
            var retainedPassed = invalidEnvelopesFailed && EffectiveFor(oldActiveNode, now).Protected &&
                                 NoraSubscriptionStore.LoadAll().Single().AppMetadata.Revision == 5;

            var notice = Fetch(System.Net.HttpStatusCode.NoContent, "", Header(Json(stamp: issued + 2, availability: "node_maintenance", message: "Проводятся работы"))).GetAwaiter().GetResult();
            NoraSubscriptionStore.Store(notice, source, utcNow: now);
            var noticePassed = notice.Servers.Count == 1 && notice.Servers[0].LocalPath == selectedPath &&
                EffectiveFor(oldActiveNode, now) is { Protected: true, CachedOnly: true, Availability: "node_maintenance" } &&
                NoraSubscriptionStore.RuntimeIdentity(notice.Servers[0]) == identity && notice.Servers[0].SourceLink == sourceLink;
            var expired = EffectiveFor(oldActiveNode, now.AddSeconds(301));
            var expirationPassed = expired.Protected && expired.CachedOnly && expired.Access == "unknown" && expired.Availability == "unknown" && expired.Message == "";

            var recovered = Fetch(System.Net.HttpStatusCode.OK, body, Header(Json(stamp: issued + 3))).GetAwaiter().GetResult();
            NoraSubscriptionStore.Store(recovered, source, utcNow: now);
            var recoveryPassed = !EffectiveFor(oldActiveNode, now).CachedOnly && EffectiveFor(oldActiveNode, now).Availability == "available" &&
                recovered.Servers[0].LocalPath == selectedPath && recovered.Servers[0].RawConfigJson == raw &&
                NoraSubscriptionStore.RuntimeIdentity(recovered.Servers[0]) == identity &&
                !NoraSubscriptionStore.BuildXrayConfig(recovered.Servers[0]).Contains("AppMetadata", StringComparison.OrdinalIgnoreCase);

            var sourceB = source + "-second";
            var other = Fetch(System.Net.HttpStatusCode.OK, body, Header(Json(false, 1)), requestSource: sourceB).GetAwaiter().GetResult();
            NoraSubscriptionStore.Store(other, sourceB, utcNow: now);
            var isolationPassed = !EffectiveFor(other.Servers.Single(), now).Protected && EffectiveFor(oldActiveNode, now).Protected;
            var firstNoticeSource = source + "-notice-first";
            var firstNotice = Fetch(System.Net.HttpStatusCode.NoContent, "", Header(Json(access: "expired", availability: "no_nodes")), requestSource: firstNoticeSource).GetAwaiter().GetResult();
            NoraSubscriptionStore.Store(firstNotice, firstNoticeSource, utcNow: now);
            noticePassed &= firstNotice.Servers.Count == 0 && EffectiveFor(firstNotice, now).CachedOnly;

            var cleared = Fetch(System.Net.HttpStatusCode.OK, body, Header(Json(false, 6, issued + 4, label: null))).GetAwaiter().GetResult();
            NoraSubscriptionStore.Store(cleared, source, utcNow: now);
            var clearPassed = !EffectiveFor(oldActiveNode, now).Protected && EffectiveFor(oldActiveNode, now).PublicLabel == "";

            var legacySource = source + "-legacy";
            var marker = "vless://" + NoraLegacySubscriptionNotice.MarkerId + "@127.0.0.1:1?encryption=none&security=none&type=tcp#Maintenance";
            var mixed = NoraSubscriptionStore.ParsePayload(body + "\n" + marker, legacySource,
                new Dictionary<string, string> { ["hide-settings"] = "1" }, _ => { }, utcNow: now);
            NoraSubscriptionStore.Store(mixed, legacySource, utcNow: now);
            var legacyId = mixed.Servers.Single().Id;
            var markerOnly = NoraSubscriptionStore.ParsePayload(marker, legacySource, new Dictionary<string, string>(), _ => { }, utcNow: now);
            NoraSubscriptionStore.Store(markerOnly, legacySource, utcNow: now);
            var legacyPassed = mixed.Servers.Count == 1 && mixed.AppMetadata.LegacyNotice && mixed.AppMetadata.Availability == "degraded" &&
                markerOnly.Servers.Count == 1 && markerOnly.Servers.Single().Id == legacyId && markerOnly.AppMetadata.CachedOnly && markerOnly.AppMetadata.Protected &&
                markerOnly.Servers.All(server => !NoraLegacySubscriptionNotice.IsMaintenanceMarker(server));
            var legacyRecovered = NoraSubscriptionStore.ParsePayload(body, legacySource, new Dictionary<string, string>(), _ => { }, utcNow: now);
            NoraSubscriptionStore.Store(legacyRecovered, legacySource, utcNow: now);
            legacyPassed &= !legacyRecovered.AppMetadata.CachedOnly && legacyRecovered.AppMetadata.Protected && legacyRecovered.AppMetadata.Message.Length == 0;

            var expiredSource = source + "-legacy-expired";
            const string expiredLabel = "Срок подписки истёк 🍄";
            var expiredLink = "vless://" + NoraLegacySubscriptionNotice.ExpiredMarkerId + "@192.0.2.1:1?encryption=none&security=none&type=tcp#" + Uri.EscapeDataString(expiredLabel);
            var expiredJson = JsonSerializer.Serialize(new
            {
                remarks = expiredLabel,
                outbounds = new[] { new
                {
                    protocol = "vless", settings = new
                    {
                        vnext = new[] { new { address = "192.0.2.1", port = 1, users = new[] { new { id = NoraLegacySubscriptionNotice.ExpiredMarkerId, encryption = "none" } } } }
                    },
                    streamSettings = new { network = "tcp", security = "none" }
                } }
            });
            var expiredHeaders = new Dictionary<string, string> { ["x-nora-subscription-expired"] = "true" };
            var expiredSeed = NoraSubscriptionStore.ParsePayload(body, expiredSource,
                new Dictionary<string, string> { ["hide-settings"] = "1" }, _ => { }, utcNow: now);
            NoraSubscriptionStore.Store(expiredSeed, expiredSource, utcNow: now);
            var expiredActive = expiredSeed.Servers.Single();
            var expiredIdentity = NoraSubscriptionStore.RuntimeIdentity(expiredActive);
            var expiredPath = expiredActive.LocalPath;
            var legacyExpiredPassed = true;
            foreach (var payload in new[] { expiredLink, Convert.ToBase64String(Encoding.UTF8.GetBytes(expiredLink)), expiredJson })
            {
                var expiredNotice = NoraSubscriptionStore.ParsePayload(payload, expiredSource, expiredHeaders, _ => { }, utcNow: now);
                legacyExpiredPassed &= expiredNotice.Servers.Count == 0 && expiredNotice.AppMetadata.Access == "expired" && expiredNotice.AppMetadata.Message == expiredLabel;
                NoraSubscriptionStore.Store(expiredNotice, expiredSource, utcNow: now);
                legacyExpiredPassed &= expiredNotice.Servers.Count == 1 && expiredNotice.Servers.Single().Id == expiredActive.Id &&
                    expiredNotice.Servers.Single().LocalPath == expiredPath && NoraSubscriptionStore.RuntimeIdentity(expiredNotice.Servers.Single()) == expiredIdentity &&
                    EffectiveFor(expiredActive, now) is { Access: "expired", Availability: "unknown", CachedOnly: true, Protected: true } &&
                    EffectiveFor(expiredActive, now).Message == expiredLabel &&
                    expiredNotice.Servers.All(server => !NoraLegacySubscriptionNotice.IsNoticeMarker(server));
            }
            var unknownSentinel = NoraSubscriptionStore.ParsePayload(expiredLink, expiredSource, new Dictionary<string, string>(), _ => { }, utcNow: now);
            legacyExpiredPassed &= unknownSentinel.Servers.Count == 0 &&
                EffectiveFor(unknownSentinel, now) is { Access: "unknown", Availability: "unknown", CachedOnly: true } &&
                EffectiveFor(unknownSentinel, now).Message == NoraLegacySubscriptionNotice.ExpiredFallbackMessage &&
                !NoraSubscriptionStore.TryParseExternalProfile(expiredLink, "fixture-direct.txt", "", out _);
            foreach (var ordinaryPayload in new[]
            {
                expiredLink.Replace("@192.0.2.1:1", "@198.51.100.9:1", StringComparison.Ordinal),
                expiredLink.Replace(NoraLegacySubscriptionNotice.ExpiredMarkerId, "00000000-0000-4000-8000-000000000002", StringComparison.Ordinal),
                body
            })
            {
                var ordinary = NoraSubscriptionStore.ParsePayload(ordinaryPayload, expiredSource, expiredHeaders, _ => { }, utcNow: now);
                legacyExpiredPassed &= ordinary.Servers.Count == 1 && !ordinary.AppMetadata.LegacyNotice && ordinary.AppMetadata.Access == "unknown";
            }
            var realWithBlackhole = System.Text.Json.Nodes.JsonNode.Parse(rawVless)!.AsObject();
            realWithBlackhole["outbounds"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["protocol"] = "blackhole", ["tag"] = "expired" });
            // A blackhole routing outbound is not either legacy sentinel.
            var blackhole = NoraSubscriptionStore.ParsePayload(realWithBlackhole.ToJsonString(), expiredSource, expiredHeaders, _ => { }, utcNow: now);
            legacyExpiredPassed &= blackhole.Servers.Count == 1 && !blackhole.AppMetadata.LegacyNotice;
            var expiredManifest = Path.Combine(NoraSubscriptionStore.Root, expiredSeed.Id, "subscription.json");
            var beforeExpiredFailure = File.ReadAllText(expiredManifest);
            foreach (var failedPayload in new[] { "{malformed", expiredLink })
            {
                try
                {
                    var rejectedNotice = Fetch(System.Net.HttpStatusCode.OK, failedPayload,
                        Header(Json(false, 1, issued + 1)), requestSource: expiredSource, legacyExpired: true).GetAwaiter().GetResult();
                    NoraSubscriptionStore.Store(rejectedNotice, expiredSource, utcNow: now);
                    legacyExpiredPassed = false;
                }
                catch (NoraAppException) { }
            }
            legacyExpiredPassed &= File.ReadAllText(expiredManifest) == beforeExpiredFailure && EffectiveFor(expiredActive, now).Access == "expired";
            var expiredRecovered = NoraSubscriptionStore.ParsePayload(body, expiredSource, new Dictionary<string, string>(), _ => { }, utcNow: now);
            NoraSubscriptionStore.Store(expiredRecovered, expiredSource, utcNow: now);
            legacyExpiredPassed &= !EffectiveFor(expiredActive, now).CachedOnly && EffectiveFor(expiredActive, now).Access == "unknown" &&
                EffectiveFor(expiredActive, now).Message.Length == 0 && expiredRecovered.Servers.Single().LocalPath == expiredPath;
            var authoritativeRecovery = Fetch(System.Net.HttpStatusCode.OK, body + "\n" + expiredLink,
                Header(Json(true, 1, issued + 2)), requestSource: expiredSource, legacyExpired: true).GetAwaiter().GetResult();
            NoraSubscriptionStore.Store(authoritativeRecovery, expiredSource, utcNow: now);
            legacyExpiredPassed &= authoritativeRecovery.Servers.Count == 1 &&
                EffectiveFor(expiredActive, now) is { Access: "active", Availability: "available", CachedOnly: false, HasV1Policy: true } &&
                !authoritativeRecovery.AppMetadata.LegacyNotice && EffectiveFor(expiredActive, now.AddSeconds(301)).Access == "unknown" &&
                EffectiveFor(expiredActive, now.AddSeconds(301)).Message.Length == 0 && !EffectiveFor(legacyRecovered.Servers.Single(), now).CachedOnly;
            var offlineCachePassed = RunOfflineLegacyCacheSelfTest(now, body);
            var modernSource = source + "-modern-vless";
            var modernImported = Fetch(System.Net.HttpStatusCode.OK, modernVless, Header(Json(true, 12, issued + 1)), requestSource: modernSource).GetAwaiter().GetResult();
            var modernNode = modernImported.Servers.Single();
            var modernRaw = modernNode.RawConfigJson;
            var modernIdentity = NoraSubscriptionStore.RuntimeIdentity(modernNode);
            NoraSubscriptionStore.Store(modernImported, modernSource, utcNow: now);
            var modernReloaded = NoraSubscriptionStore.LoadAll().Single(subscription => subscription.Id == modernImported.Id).Servers.Single();
            var modernBuilt = NoraSubscriptionStore.BuildXrayConfig(modernReloaded);
            using var modernBuiltJson = JsonDocument.Parse(modernBuilt);
            var modernSettings = modernBuiltJson.RootElement.GetProperty("outbounds")[0].GetProperty("settings");
            var modernRoundtrip = NoraSubscriptionStore.ParsePayload(modernBuilt, modernSource + "-roundtrip", new Dictionary<string, string>(), _ => { }, utcNow: now).Servers.Single();
            var modernVlessPassed = modernImported.AppMetadata is { Protected: true, HasV1Policy: true, Revision: 12 } &&
                modernNode.Uuid == "provider-modern-custom-identity" && modernNode.Path == "/modern%2Fexact" &&
                modernReloaded.RawConfigJson == modernRaw && modernReloaded.Id == modernNode.Id && modernReloaded.LocalPath == modernNode.LocalPath &&
                modernSettings.GetProperty("id").GetString() == modernNode.Uuid && modernSettings.GetProperty("port").GetInt32() == 443 &&
                !modernSettings.TryGetProperty("vnext", out _) && NoraSubscriptionStore.RuntimeIdentity(modernReloaded) == modernIdentity &&
                NoraSubscriptionStore.RuntimeIdentity(modernRoundtrip) == modernIdentity;

            var invalidFields = new (string Name, string Text)[]
            {
                ("uuid_encoded_control", body.Replace("11111111@", "11111111%0A@", StringComparison.Ordinal)),
                ("uuid_raw_control", body.Replace("11111111@", "11111111\n@", StringComparison.Ordinal)),
                ("key_encoded_format", body.Replace("security=tls", "security=reality&pbk=Abc%E2%80%8Bdef", StringComparison.Ordinal)),
                ("key_raw_format", body.Replace("security=tls", "security=reality&pbk=Abc\u200Bdef", StringComparison.Ordinal)),
                ("identity_missing", "vless://198.51.100.9:443?security=tls#Fixture")
            };
            var acceptedInvalidFields = new List<string>();
            foreach (var fixture in invalidFields)
            {
                try { _ = NoraSubscriptionStore.ParsePayload(fixture.Text, source, new Dictionary<string, string>(), _ => { }); acceptedInvalidFields.Add(fixture.Name); }
                catch (NoraAppException) { }
            }
            var customIdentity = body.Replace("11111111-1111-4111-8111-111111111111", "provider-custom-identity", StringComparison.Ordinal);
            var customNode = NoraSubscriptionStore.ParsePayload(customIdentity, source, new Dictionary<string, string>(), _ => { }).Servers.Single();
            var invalidFieldsPassed = acceptedInvalidFields.Count == 0 && customNode.Uuid == "provider-custom-identity" && customNode.Path == "/fixture";
            if (acceptedInvalidFields.Count != 0)
                output.WriteLine("CONSTRAINED FIELD FIXTURE FAIL: " + string.Join(",", acceptedInvalidFields));
            var passed = parserPassed && orderingPassed && failedBodyPassed && invalid204Passed && retainedPassed && noticePassed &&
                         expirationPassed && recoveryPassed && isolationPassed && clearPassed && legacyPassed && legacyExpiredPassed && offlineCachePassed && modernVlessPassed && invalidFieldsPassed && invalidTransportPassed && logPrivacyPassed && redirectChainPassed;
            output.WriteLine($"SUBSCRIPTION METADATA SELF-TEST {(passed ? "PASS" : "FAIL")}: parser={parserPassed}; ordering={orderingPassed}; malformed_body={failedBodyPassed}; invalid_transport={invalidTransportPassed}; modern_vless={modernVlessPassed}; redirect_chain={redirectChainPassed}; log_privacy={logPrivacyPassed}; invalid204_errors304={invalid204Passed}; retained={retainedPassed}; notice={noticePassed}; ttl={expirationPassed}; recovery_identity={recoveryPassed}; isolation={isolationPassed}; explicit_clear={clearPassed}; legacy={legacyPassed}; legacy_expired={legacyExpiredPassed}; offline_cache={offlineCachePassed}; constrained_fields={invalidFieldsPassed}");
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            output.WriteLine("SUBSCRIPTION METADATA SELF-TEST FAIL: " + exception.GetType().Name);
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", originalRoot);
            try { if (System.IO.Directory.Exists(temporaryRoot)) System.IO.Directory.Delete(temporaryRoot, recursive: true); }
            catch { }
        }
    }

    private static bool RunOfflineLegacyCacheSelfTest(DateTimeOffset now, string realBody)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        NoraSubscriptionServer Marker(bool expired) => new()
        {
            Id = expired ? "old-expired-marker" : "old-maintenance-marker", Name = expired ? "Legacy expired" : "Legacy maintenance",
            Host = expired ? "192.0.2.1" : "127.0.0.1", Port = 1,
            Uuid = expired ? NoraLegacySubscriptionNotice.ExpiredMarkerId : NoraLegacySubscriptionNotice.MarkerId,
            Network = "tcp", Security = "none", Encryption = "none"
        };
        (string Manifest, string[] Paths, string Source) WriteOld(string id, DateTimeOffset stamp,
            NoraSubscriptionServer[] children, NoraSubscriptionMetadataState? metadata = null)
        {
            var directory = Path.Combine(NoraSubscriptionStore.Root, id);
            Directory.CreateDirectory(directory);
            var source = "https://subscription.example.test/offline-" + id;
            var servers = new System.Text.Json.Nodes.JsonArray();
            var paths = new List<string>();
            foreach (var child in children)
            {
                child.SubscriptionId = id;
                child.SubscriptionTitle = "Offline fixture";
                child.LocalPath = Path.Combine(directory, "server-" + child.Id + ".json");
                var node = JsonSerializer.SerializeToNode(child, options)!.AsObject();
                node.Remove("app_metadata");
                File.WriteAllText(child.LocalPath, node.ToJsonString());
                paths.Add(child.LocalPath);
                servers.Add(node);
            }
            var snapshot = new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = id, ["title"] = "Offline fixture", ["source_url"] = source,
                ["updated_at"] = JsonSerializer.SerializeToNode(stamp), ["servers"] = servers
            };
            if (metadata is not null)
                snapshot["app_metadata"] = JsonSerializer.SerializeToNode(metadata with { SourceBinding = BindSource(source) }, options);
            var manifest = Path.Combine(directory, "subscription.json");
            File.WriteAllText(manifest, snapshot.ToJsonString());
            return (manifest, paths.ToArray(), source);
        }

        var stale = now.AddDays(-1);
        var expired = WriteOld("offline-expired", stale, [Marker(true)]);
        var maintenance = WriteOld("offline-maintenance", now.AddMinutes(-1), [Marker(false)]);
        var real = NoraSubscriptionStore.ParsePayload(realBody, "offline-fixture", new Dictionary<string, string>(), _ => { }, utcNow: stale).Servers.Single();
        var identity = NoraSubscriptionStore.RuntimeIdentity(real);
        var mixed = WriteOld("offline-mixed", stale, [real, Marker(true), Marker(false)]);
        var authoritative = WriteOld("offline-authoritative", now.AddMinutes(-1), [Marker(true)], new NoraSubscriptionMetadataState
        {
            HasV1Policy = true, Protected = true, V1Protected = true, PublicLabel = "Retained policy", Access = "expired", Availability = "no_nodes",
            Revision = 9, IssuedAtUnix = stale.ToUnixTimeSeconds(), TtlSeconds = 300, AcceptedAt = stale
        });
        var files = new[] { expired.Manifest, maintenance.Manifest, mixed.Manifest, authoritative.Manifest }
            .Concat(expired.Paths).Concat(maintenance.Paths).Concat(mixed.Paths).Concat(authoritative.Paths)
            .ToDictionary(path => path, File.ReadAllText);
        var loaded = NoraSubscriptionStore.LoadAll();
        var loadedExpired = loaded.Single(subscription => subscription.Id == "offline-expired");
        var loadedMaintenance = loaded.Single(subscription => subscription.Id == "offline-maintenance");
        var loadedMixed = loaded.Single(subscription => subscription.Id == "offline-mixed");
        var loadedAuthority = loaded.Single(subscription => subscription.Id == "offline-authoritative");
        var passed = loadedExpired.Servers.Count == 0 && EffectiveFor(loadedExpired, now) is { CachedOnly: true, Access: "unknown", Availability: "unknown", Message: "" } &&
            loadedExpired.AppMetadata.LegacyNoticeAtUnix == stale.ToUnixTimeSeconds() && loadedExpired.UpdatedAt == stale &&
            loadedMaintenance.Servers.Count == 0 && EffectiveFor(loadedMaintenance, now) is { CachedOnly: true, Availability: "node_maintenance" } &&
            loadedMaintenance.AppMetadata.LegacyNoticeAtUnix == now.AddMinutes(-1).ToUnixTimeSeconds() &&
            loadedMixed.Servers.Count == 1 && loadedMixed.Servers[0].Id == real.Id && loadedMixed.Servers[0].LocalPath == mixed.Paths[0] &&
            NoraSubscriptionStore.RuntimeIdentity(loadedMixed.Servers[0]) == identity && !EffectiveFor(loadedMixed, now).CachedOnly &&
            loadedMixed.Servers.All(server => !NoraLegacySubscriptionNotice.IsNoticeMarker(server)) &&
            loadedAuthority.Servers.Count == 0 && loadedAuthority.AppMetadata.Revision == 9 && loadedAuthority.AppMetadata.Access == "expired" &&
            loadedAuthority.AppMetadata.IssuedAtUnix == stale.ToUnixTimeSeconds() && loadedAuthority.AppMetadata.AcceptedAt == stale &&
            EffectiveFor(loadedAuthority, now) is { Protected: true, CachedOnly: true, Access: "unknown", Availability: "unknown", Message: "" };
        foreach (var markerPath in expired.Paths.Concat(maintenance.Paths).Concat(mixed.Paths.Skip(1)).Concat(authoritative.Paths))
            passed &= !NoraSubscriptionStore.TryLoadServer(markerPath, out _);
        passed &= NoraSubscriptionStore.TryLoadServer(mixed.Paths[0], out var loadedReal) && loadedReal.Id == real.Id &&
                  NoraSubscriptionStore.RuntimeIdentity(loadedReal) == identity && files.All(file => File.ReadAllText(file.Key) == file.Value);
        var standalone = Path.Combine(NoraSubscriptionStore.Root, "offline-standalone", "server-old-expired.json");
        Directory.CreateDirectory(Path.GetDirectoryName(standalone)!);
        File.WriteAllText(standalone, JsonSerializer.Serialize(Marker(true), options));
        passed &= !NoraSubscriptionStore.TryLoadServer(standalone, out _);
        var external = Path.Combine(NoraSelfTestPaths.BaseDirectory, "nora-offline-marker-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(external, JsonSerializer.Serialize(Marker(false), options));
            passed &= !NoraSubscriptionStore.TryLoadServer(external, out _);
        }
        finally { try { File.Delete(external); } catch { } }
        foreach (var fixture in new[] { expired, mixed })
        {
            var notice = new NoraSubscriptionInfo
            {
                Id = Path.GetFileName(Path.GetDirectoryName(fixture.Manifest))!, SourceUrl = fixture.Source, Title = "Notice fixture", UpdatedAt = now,
                AppMetadata = new NoraSubscriptionMetadataState
                {
                    HasV1Policy = true, Protected = true, V1Protected = true, Access = "expired", Availability = "no_nodes", CachedOnly = true,
                    Revision = 1, IssuedAtUnix = now.ToUnixTimeSeconds(), TtlSeconds = 300, AcceptedAt = now, SourceBinding = BindSource(fixture.Source)
                }
            };
            NoraSubscriptionStore.Store(notice, fixture.Source, utcNow: now);
            passed &= notice.Servers.All(server => !NoraLegacySubscriptionNotice.IsNoticeMarker(server)) && notice.AppMetadata.CachedOnly &&
                (fixture.Source == expired.Source ? notice.Servers.Count == 0 : notice.Servers.Count == 1 && notice.Servers[0].Id == real.Id &&
                 NoraSubscriptionStore.RuntimeIdentity(notice.Servers[0]) == identity);
        }
        return passed;
    }

    private sealed class MetadataFixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    internal static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
#endif
