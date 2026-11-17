#if WINDOWS
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nvp;

internal sealed record NoraSubscriptionMetadataState
{
    public bool Protected { get; init; }
    public string PublicLabel { get; init; } = "";
    public long Revision { get; init; }
    public long IssuedAtUnix { get; init; }
    public int TtlSeconds { get; init; }
    public DateTimeOffset AcceptedAt { get; init; }
    public bool HasV1Policy { get; init; }
    public bool V1Protected { get; init; }
    public bool LegacyProtected { get; init; }
    // Local-only bindings; never forwarded to a core configuration.
    public string SourceBinding { get; init; } = "";
    public string ScopeId { get; init; } = "";
    public long LocalGeneration { get; init; }
}

internal static class NoraSubscriptionMetadata
{
    private const long MaximumRevision = 9_007_199_254_740_991;
    private static readonly ConcurrentDictionary<string, NoraSubscriptionMetadataState> Committed = new(StringComparer.Ordinal);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void AddNegotiationHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("X-Nora-Client", "nora-windows");
        request.Headers.TryAddWithoutValidation("X-Nora-Metadata-Accept", "1");
        request.Headers.TryAddWithoutValidation("X-Nora-Capabilities", "display-v1");
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
           string.Equals(authorizedSource.IdnHost, finalSource.IdnHost, StringComparison.OrdinalIgnoreCase) && authorizedSource.Port == finalSource.Port;
    internal static bool HasResponseHeader(HttpResponseMessage response)
        => response.Headers.Contains("X-Nora-Metadata") || response.Content.Headers.Contains("X-Nora-Metadata");
    internal static NoraSubscriptionMetadataState? ReadResponse(HttpResponseMessage response, Uri authorizedSource, string source, DateTimeOffset now)
    {
        if (response.StatusCode != HttpStatusCode.OK || !SameTrustedOrigin(authorizedSource, response.RequestMessage?.RequestUri ?? authorizedSource)) return null;
        var values = new List<string>();
        if (response.Headers.TryGetValues("X-Nora-Metadata", out var headers)) values.AddRange(headers);
        if (response.Content.Headers.TryGetValues("X-Nora-Metadata", out var contentHeaders)) values.AddRange(contentHeaders);
        return values.Count == 1 ? ParseHeader(values[0], BindSource(source), now) : null;
    }
    internal static NoraSubscriptionMetadataState? ParseHeader(string value, string sourceBinding, DateTimeOffset now)
    {
        try
        {
            if (value.Length is 0 or > 4096 || value.Any(character => !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) || value.Length % 4 == 1) return null;
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
                !display.TryGetProperty("protected", out var protection) || protection.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !NullableText(display, "public_label", out var label) || label is not null && !ValidText(label, 80, 256, false)) return null;
            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issued);
            if (issuedAt > now.AddSeconds(300) || issuedAt.AddSeconds(ttl) <= now) return null;
            return new NoraSubscriptionMetadataState
            {
                Protected = protection.GetBoolean(), V1Protected = protection.GetBoolean(), PublicLabel = label ?? "", Revision = revision,
                IssuedAtUnix = issued, TtlSeconds = (int)ttl, AcceptedAt = now, HasV1Policy = true, SourceBinding = sourceBinding
            };
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or InvalidOperationException) { return null; }
    }
    internal static bool ValidText(string text, int scalarLimit, int byteLimit, bool allowEmpty)
    {
        if ((!allowEmpty && text.Length == 0) || !text.IsNormalized(NormalizationForm.FormC) || StrictUtf8.GetByteCount(text) > byteLimit) return false;
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
            if (++count > scalarLimit || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) return false;
        return true;
    }
    private static bool HasDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) if (!names.Add(property.Name) || HasDuplicateKeys(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) if (HasDuplicateKeys(item)) return true;
        return false;
    }
    private static bool Integer(JsonElement parent, string name, out long value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0 && element.TryGetInt64(out value);
    }
    private static bool Text(JsonElement parent, string name, out string value)
    {
        value = "";
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString()!; return true;
    }
    private static bool NullableText(JsonElement parent, string name, out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var element)) return false;
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString(); return true;
    }
    internal static NoraSubscriptionMetadataState Merge(NoraSubscriptionMetadataState? previous, NoraSubscriptionMetadataState incoming,
        string sourceBinding, DateTimeOffset now, out bool acceptedV1)
    {
        previous = previous?.SourceBinding == sourceBinding ? previous : null;
        var retained = previous ?? new NoraSubscriptionMetadataState { SourceBinding = sourceBinding };
        acceptedV1 = false;
        if (incoming.HasV1Policy && incoming.SourceBinding == sourceBinding && IsFresh(incoming, now))
        {
            var samePolicy = incoming.V1Protected == retained.V1Protected && incoming.PublicLabel == retained.PublicLabel;
            if (!retained.HasV1Policy || incoming.Revision >= retained.Revision && incoming.IssuedAtUnix >= retained.IssuedAtUnix &&
                (incoming.Revision != retained.Revision || samePolicy) &&
                (incoming.Revision != retained.Revision || incoming.IssuedAtUnix != retained.IssuedAtUnix || samePolicy && incoming.TtlSeconds == retained.TtlSeconds))
            {
                retained = incoming with { ScopeId = retained.ScopeId, LegacyProtected = false };
                acceptedV1 = true;
            }
        }
        if (!retained.HasV1Policy && !incoming.HasV1Policy)
            retained = incoming with { Protected = retained.Protected || incoming.Protected, SourceBinding = sourceBinding, ScopeId = retained.ScopeId };
        if (incoming.LegacyProtected) retained = retained with { Protected = true, LegacyProtected = true };
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
    internal static void RegisterCommitted(NoraSubscriptionInfo subscription)
    {
        var state = subscription.AppMetadata ?? new NoraSubscriptionMetadataState();
        var snapshotGeneration = state.LocalGeneration;
        if (state.SourceBinding.Length == 0) state = state with { SourceBinding = BindSource(subscription.SourceUrl) };
        if (state.ScopeId.Length == 0) state = state with { ScopeId = "legacy-" + subscription.Id };
        state = Committed.AddOrUpdate(RegistryKey(subscription.Id, state), state, (_, current) =>
            current.LocalGeneration > state.LocalGeneration || current.HasV1Policy && (!state.HasV1Policy || current.Revision > state.Revision || current.Revision == state.Revision && current.IssuedAtUnix > state.IssuedAtUnix) ? current : state);
        subscription.AppMetadata = state;
        foreach (var server in subscription.Servers.Concat(subscription.RetainedServers)) server.AppMetadata = state;
        if (state.LocalGeneration == snapshotGeneration)
            NoraSubscriptionStore.RegisterCurrentRows(subscription);
        NoraProtectedDisplay.Register(subscription);
    }
    internal static void Forget(NoraSubscriptionInfo subscription) => Committed.TryRemove(RegistryKey(subscription.Id, subscription.AppMetadata), out _);
    private static string RegistryKey(string subscriptionId, NoraSubscriptionMetadataState state)
        => NoraAppState.DataRoot + "\0" + subscriptionId + "\0" + state.ScopeId + "\0" + state.SourceBinding;
    internal static NoraSubscriptionMetadataState EffectiveFor(NoraSubscriptionServer server, DateTimeOffset? now = null)
        => Committed.TryGetValue(RegistryKey(server.SubscriptionId, server.AppMetadata), out var committed) ? committed : server.AppMetadata;
    internal static NoraSubscriptionMetadataState EffectiveFor(NoraSubscriptionInfo subscription, DateTimeOffset? now = null)
        => Committed.TryGetValue(RegistryKey(subscription.Id, subscription.AppMetadata), out var committed) ? committed : subscription.AppMetadata;
    internal static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static int RunSelfTest(TextWriter output)
    {
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var binding = BindSource("https://subscription.example.test/display-fixture");
        string Header(bool protect = true, long revision = 5, long? issued = null, int ttl = 300, string? label = "Рабочая сеть 🌐")
            => Encode(StrictUtf8.GetBytes(JsonSerializer.Serialize(new { v = 1, mode = "full", revision, issued_at = issued ?? now.AddSeconds(-20).ToUnixTimeSeconds(), ttl_seconds = ttl, display = new { @protected = protect, public_label = label } })));
        try
        {
            var first = ParseHeader(Header(), binding, now)!;
            var invalid = new[] { Header(ttl: 29), Header(ttl: 86401), Header(revision: -1), Header(label: ""), Header(label: "bad\u200Btext"), Header(issued: now.AddSeconds(-301).ToUnixTimeSeconds()), Header(issued: now.AddSeconds(301).ToUnixTimeSeconds()), Header() + "=" };
            var parsed = first is { Protected: true, Revision: 5 } && invalid.All(value => ParseHeader(value, binding, now) is null) && ParseHeader(Encode([0xff]), binding, now) is null;
            var repeated = Merge(first, first with { AcceptedAt = now.AddSeconds(1) }, binding, now, out var repeatedAccepted);
            _ = Merge(first, first with { TtlSeconds = 600 }, binding, now, out var ttlConflict);
            _ = Merge(first, first with { Protected = false, V1Protected = false, IssuedAtUnix = first.IssuedAtUnix + 1 }, binding, now, out var policyConflict);
            _ = Merge(first, first with { Revision = 4 }, binding, now, out var replay);
            _ = Merge(first, first with { Revision = 6, IssuedAtUnix = first.IssuedAtUnix - 1 }, binding, now, out var older);
            _ = Merge(first, first with { SourceBinding = BindSource("https://other.example.test/fixture"), Revision = 6 }, binding, now, out var otherSource);
            var expiredRetained = Merge(first, new NoraSubscriptionMetadataState(), binding, now.AddDays(1), out _);
            var cleared = Merge(first, ParseHeader(Header(false, 6, now.ToUnixTimeSeconds(), label: null), binding, now)!, binding, now, out var clearAccepted);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://subscription.example.test/display-fixture");
            AddNegotiationHeaders(request);
            var negotiation = request.Headers.GetValues("X-Nora-Capabilities").Single() == "display-v1";
            var passed = parsed && repeatedAccepted && repeated.IssuedAtUnix == first.IssuedAtUnix && !ttlConflict && !policyConflict && !replay && !older && !otherSource &&
                expiredRetained.Protected && expiredRetained.PublicLabel == first.PublicLabel && clearAccepted && !cleared.Protected && cleared.PublicLabel == "" && negotiation;
            output.WriteLine($"SUBSCRIPTION DISPLAY METADATA SELF-TEST {(passed ? "PASS" : "FAIL")}: schema={parsed}; sticky_replay_source_ttl={passed}; display_only={negotiation}");
            return passed ? 0 : 1;
        }
        catch (Exception exception) { output.WriteLine("SUBSCRIPTION DISPLAY METADATA SELF-TEST FAIL: " + exception.GetType().Name); return 1; }
    }
}
#endif
