using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace Nvp;

internal enum NoraDeepLinkKind
{
    Subscription,
    Profile
}

internal sealed record NoraDeepLinkRequest(NoraDeepLinkKind Kind, string Payload, string? Name);

internal static partial class NoraDeepLinkParser
{
    private const int MaximumLinkBytes = 384 * 1024;
    private const int MaximumProfileBytes = 256 * 1024;
    private const int MaximumSubscriptionUrlBytes = 8 * 1024;
    private const int MaximumNameBytes = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool LooksLikeDeepLink(string? value)
        => value?.StartsWith("noravpn:", StringComparison.OrdinalIgnoreCase) == true;

    public static NoraDeepLinkRequest Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid("The link is empty.");
        EnsureUtf8Limit(value, MaximumLinkBytes, "The link is too large.");
        if (value.IndexOf('#') >= 0)
            throw Invalid("Fragments are not supported in NORA links.");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("noravpn", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("import", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != -1)
        {
            throw Invalid("Expected noravpn://import/subscription or noravpn://import/profile.");
        }

        var path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        var segments = path.Split('/', StringSplitOptions.None);
        if (segments.Length is < 1 or > 2 || segments.Any(string.IsNullOrEmpty))
            throw Invalid("The import path is malformed.");

        var kind = segments[0].ToLowerInvariant() switch
        {
            "subscription" => NoraDeepLinkKind.Subscription,
            "profile" => NoraDeepLinkKind.Profile,
            _ => throw Invalid("The requested import type is not supported.")
        };

        var query = ParseQuery(uri.GetComponents(UriComponents.Query, UriFormat.UriEscaped));
        var allowed = kind == NoraDeepLinkKind.Subscription
            ? new HashSet<string>(["v", "url", "payload", "name"], StringComparer.Ordinal)
            : new HashSet<string>(["v", "data", "payload"], StringComparer.Ordinal);
        if (query.Keys.Any(key => !allowed.Contains(key)))
            throw Invalid("The link contains an unknown parameter.");
        if (query.TryGetValue("v", out var version) && version != "1")
            throw Invalid("This NORA link version is not supported.");

        var queryPayloadKey = kind == NoraDeepLinkKind.Subscription ? "url" : "data";
        var sources = (segments.Length == 2 ? 1 : 0) +
                      (query.ContainsKey(queryPayloadKey) ? 1 : 0) +
                      (query.ContainsKey("payload") ? 1 : 0);
        if (sources != 1)
            throw Invalid("The link must contain exactly one payload.");

        string payload;
        if (segments.Length == 2)
            payload = DecodeBase64Url(segments[1]);
        else if (query.TryGetValue("payload", out var encoded))
            payload = DecodeBase64Url(encoded);
        else
            payload = query[queryPayloadKey];

        if (string.IsNullOrWhiteSpace(payload))
            throw Invalid("The import payload is empty.");
        ValidateText(payload, "The import payload contains unsupported control characters.");
        var payloadLimit = kind == NoraDeepLinkKind.Subscription ? MaximumSubscriptionUrlBytes : MaximumProfileBytes;
        EnsureUtf8Limit(payload, payloadLimit, "The import payload is too large.");

        string? name = null;
        if (query.TryGetValue("name", out var candidateName))
        {
            ValidateText(candidateName, "The subscription name contains unsupported control characters.");
            EnsureUtf8Limit(candidateName, MaximumNameBytes, "The subscription name is too large.");
            name = string.IsNullOrWhiteSpace(candidateName) ? null : candidateName.Trim();
        }

        if (kind == NoraDeepLinkKind.Subscription)
            ValidateSubscriptionUrl(payload);
        return new NoraDeepLinkRequest(kind, payload, name);
    }

    private static Dictionary<string, string> ParseQuery(string escapedQuery)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (escapedQuery.Length == 0)
            return result;
        foreach (var field in escapedQuery.Split('&', StringSplitOptions.None))
        {
            if (field.Length == 0)
                throw Invalid("The query string is malformed.");
            var separator = field.IndexOf('=');
            var encodedKey = separator >= 0 ? field[..separator] : field;
            var encodedValue = separator >= 0 ? field[(separator + 1)..] : "";
            var key = DecodeFormComponent(encodedKey);
            var decoded = DecodeFormComponent(encodedValue);
            if (key.Length == 0 || !result.TryAdd(key, decoded))
                throw Invalid("The link contains a duplicate or empty parameter.");
        }
        return result;
    }

    private static string DecodeFormComponent(string input)
    {
        try
        {
            var bytes = new List<byte>(input.Length);
            for (var index = 0; index < input.Length;)
            {
                if (input[index] == '%')
                {
                    if (index + 2 >= input.Length || !TryHex(input[index + 1], out var high) || !TryHex(input[index + 2], out var low))
                        throw Invalid("The link contains malformed percent-encoding.");
                    bytes.Add((byte)((high << 4) | low));
                    index += 3;
                    continue;
                }
                if (input[index] == '+')
                {
                    bytes.Add((byte)' ');
                    index++;
                    continue;
                }
                var start = index;
                while (index < input.Length && input[index] is not ('%' or '+'))
                    index++;
                bytes.AddRange(StrictUtf8.GetBytes(input[start..index]));
            }
            return StrictUtf8.GetString(bytes.ToArray());
        }
        catch (NoraAppException)
        {
            throw;
        }
        catch (Exception exception) when (exception is DecoderFallbackException or EncoderFallbackException)
        {
            throw Invalid("The link payload is not valid UTF-8.");
        }
    }

    private static string DecodeBase64Url(string encoded)
    {
        if (encoded.Length == 0 || encoded.Contains('=') || !Base64UrlCharacters().IsMatch(encoded))
            throw Invalid("The Base64URL payload is malformed.");
        try
        {
            var standard = encoded.Replace('-', '+').Replace('_', '/');
            standard += new string('=', (4 - standard.Length % 4) % 4);
            return StrictUtf8.GetString(Convert.FromBase64String(standard));
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            throw Invalid("The Base64URL payload is malformed or is not valid UTF-8.");
        }
    }

    private static void ValidateSubscriptionUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw Invalid("The subscription address must be an HTTP or HTTPS URL without credentials or a fragment.");
        }
    }

    private static void ValidateText(string value, string message)
    {
        foreach (var character in value)
        {
            if (character == '\0' || (char.IsControl(character) && character is not ('\t' or '\r' or '\n')))
                throw Invalid(message);
        }
    }

    private static void EnsureUtf8Limit(string value, int maximum, string message)
    {
        try
        {
            if (StrictUtf8.GetByteCount(value) > maximum)
                throw Invalid(message);
        }
        catch (EncoderFallbackException)
        {
            throw Invalid("The link payload is not valid UTF-8.");
        }
    }

    private static bool TryHex(char value, out int result)
    {
        if (value is >= '0' and <= '9') { result = value - '0'; return true; }
        if (value is >= 'a' and <= 'f') { result = value - 'a' + 10; return true; }
        if (value is >= 'A' and <= 'F') { result = value - 'A' + 10; return true; }
        result = 0;
        return false;
    }

    private static NoraAppException Invalid(string message) => new("NORA-SUB-4001", message);

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Base64UrlCharacters();

#if WINDOWS
    internal static int RunSelfTest(TextWriter output)
    {
        var originalRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "nora-deep-link-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", root);
            var subscription = Parse("noravpn://import/subscription?v=1&url=https%3A%2F%2Fexample.com%2Fsub%3Fx%3D1&name=%D0%A2%D0%B5%D1%81%D1%82");
            var profileText = "vless://id@example.com:443?security=reality#Москва";
            var profileEncoded = Convert.ToBase64String(StrictUtf8.GetBytes(profileText)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var profile = Parse($"noravpn://import/profile/{profileEncoded}?v=1");
            NoraDeepLinkQueue.Enqueue($"noravpn://import/profile?v=1&payload={profileEncoded}");
            var queued = NoraDeepLinkQueue.Drain();

            var rejected = new[]
            {
                "noravpn://import/subscription?v=2&url=https%3A%2F%2Fexample.com",
                "noravpn://import/subscription?v=1&url=https%3A%2F%2Fa.test&url=https%3A%2F%2Fb.test",
                "noravpn://import/subscription?v=1&url=https%3A%2F%2Fuser%3Apass%40example.com",
                "noravpn://import/subscription?v=1&url=https%3A%2F%2Fexample.com%2Fsub%23secret",
                "noravpn://import/profile?v=1&data=a&payload=Yg",
                "noravpn://import/profile?v=1&data=%FF",
                "noravpn://import/profile?v=1&data=",
                "noravpn://import/profile?v=1&data=ok&unknown=1"
            }.All(link => Throws(link));

            var passed = subscription.Kind == NoraDeepLinkKind.Subscription &&
                         subscription.Payload == "https://example.com/sub?x=1" && subscription.Name == "Тест" &&
                         profile.Kind == NoraDeepLinkKind.Profile && profile.Payload == profileText &&
                         queued.Count == 1 && queued[0].Payload == profileText && rejected;
            output.WriteLine($"DEEP LINK SELF-TEST: subscription={subscription.Kind}; profile={profile.Kind}; encrypted_queue={queued.Count}; invalid_rejected={rejected}");
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            output.WriteLine("DEEP LINK SELF-TEST FAIL: " + exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", originalRoot);
            try { Directory.Delete(root, recursive: true); } catch { }
        }

        static bool Throws(string link)
        {
            try { _ = Parse(link); return false; }
            catch (NoraAppException) { return true; }
        }
    }
#endif
}

#if WINDOWS
internal static class NoraDeepLinkQueue
{
    private const int MaximumPendingRequests = 32;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NORA.VPN.DeepLink.Queue.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Enqueue(string deepLink)
    {
        var request = NoraDeepLinkParser.Parse(deepLink);
        var directory = QueueDirectory();
        Directory.CreateDirectory(directory);
        RemoveExpiredAndExcessFiles(directory);
        if (Directory.GetFiles(directory, "*.nora-link").Length >= MaximumPendingRequests)
            throw new NoraAppException("NORA-SUB-4001", "Too many NORA links are waiting to be imported.");

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        var protectedPayload = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}.nora-link");
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, protectedPayload);
        File.Move(temporary, path);
    }

    public static IReadOnlyList<NoraDeepLinkRequest> Drain()
    {
        var directory = QueueDirectory();
        if (!Directory.Exists(directory))
            return [];
        RemoveExpiredAndExcessFiles(directory);
        var requests = new List<NoraDeepLinkRequest>();
        foreach (var path in Directory.GetFiles(directory, "*.nora-link").OrderBy(item => item, StringComparer.Ordinal).Take(MaximumPendingRequests))
        {
            try
            {
                var protectedPayload = File.ReadAllBytes(path);
                var plaintext = ProtectedData.Unprotect(protectedPayload, Entropy, DataProtectionScope.CurrentUser);
                var request = JsonSerializer.Deserialize<NoraDeepLinkRequest>(plaintext, JsonOptions);
                if (request is null)
                    continue;
                // Recreate a canonical link-equivalent validation boundary for
                // data loaded from the user-writable queue before elevated import.
                ValidateQueuedRequest(request);
                requests.Add(request);
            }
            catch
            {
                // Corrupt or foreign-user queue entries are discarded without
                // ever printing a potentially sensitive payload to the log.
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
        return requests;
    }

    private static void ValidateQueuedRequest(NoraDeepLinkRequest request)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var name = request.Kind == NoraDeepLinkKind.Subscription && request.Name is not null
            ? "&name=" + Uri.EscapeDataString(request.Name)
            : "";
        var type = request.Kind == NoraDeepLinkKind.Subscription ? "subscription" : "profile";
        _ = NoraDeepLinkParser.Parse($"noravpn://import/{type}?v=1&payload={encoded}{name}");
    }

    private static void RemoveExpiredAndExcessFiles(string directory)
    {
        var files = Directory.GetFiles(directory, "*.nora-link")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .ToArray();
        foreach (var file in files.Where((file, index) => index >= MaximumPendingRequests || file.CreationTimeUtc < DateTime.UtcNow.AddDays(-1)))
        {
            try { file.Delete(); } catch { }
        }
        foreach (var temporary in Directory.GetFiles(directory, "*.tmp"))
        {
            try
            {
                if (File.GetCreationTimeUtc(temporary) < DateTime.UtcNow.AddHours(-1))
                    File.Delete(temporary);
            }
            catch { }
        }
    }

    private static string QueueDirectory() => Path.Combine(NoraAppState.DataRoot, "runtime", "deep-links");
}
#endif
