#if WINDOWS
using System.IO;
using System.Globalization;
using System.Text;

namespace Nvp;

internal enum NoraImportInputKind { Subscription, Profile, DeepLink }
internal sealed record NoraClassifiedImport(string Text, NoraImportInputKind Kind, NoraDeepLinkRequest? DeepLink = null);

// All entry points remove only external presentation wrappers. Credential bytes,
// percent escapes, query ordering and whitespace inside configuration bodies survive.
internal static class NoraImportInput
{
    public static string Normalize(string? input)
    {
        var value = TrimBoundary(input ?? "");
        if (value.Length >= 2 && ((value[0] == '<' && value[^1] == '>') ||
            (value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'') ||
            (value[0] == '`' && value[^1] == '`')))
        {
            var inner = TrimBoundary(value[1..^1]);
            if (HasLinkScheme(inner))
                value = inner;
        }
        if (value.Length == 0)
            throw Invalid("The import is empty. Enter a subscription URL, key or configuration.");
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(value) > 2 * 1024 * 1024)
                throw Invalid("The import is too large.");
        }
        catch (EncoderFallbackException) { throw Invalid("The import is not valid Unicode text."); }
        return value;
    }

    public static NoraClassifiedImport Classify(string input)
    {
        var text = Normalize(input);
        var linkLines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (linkLines.Length > 1 && linkLines.All(line => Starts(line, "vless://", "vmess://", "trojan://")))
        {
            foreach (var line in linkLines) ValidateUriText(line, allowShareFragment: true);
            return new(text, NoraImportInputKind.Subscription);
        }
        if (HasLinkScheme(text))
        {
            // A URI must be one uninterrupted value. Never silently delete an
            // invisible character from a token, path, UUID or encryption key.
            ValidateUriText(text, allowShareFragment: Starts(text, "vless:", "vmess:", "trojan:"));
            if (NoraDeepLinkParser.LooksLikeDeepLink(text))
                return new(text, NoraImportInputKind.DeepLink, NoraDeepLinkParser.Parse(text));
            if (text.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
                    throw Invalid("The subscription URL is malformed or contains unsupported user credentials.");
                return new(text, NoraImportInputKind.Subscription);
            }
            if (HappCrypt5Decoder.IsCrypt5Link(text) || Starts(text, "vless:", "vmess:", "trojan:"))
                return new(text, NoraImportInputKind.Subscription);
        }
        if (text.StartsWith('{') || text.StartsWith('[') && !text.Contains("[Interface]", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("proxies:", StringComparison.OrdinalIgnoreCase) || LooksLikeEncodedSubscription(text))
            return new(text, NoraImportInputKind.Subscription);
        var shareLines = linkLines.Where(line => Starts(line, "vless://", "vmess://", "trojan://")).ToArray();
        if (shareLines.Length > 0)
        {
            foreach (var line in shareLines) ValidateUriText(line, allowShareFragment: true);
            return new(text, NoraImportInputKind.Subscription);
        }
        return new(text, NoraImportInputKind.Profile);
    }

    private static bool LooksLikeEncodedSubscription(string text)
    {
        try
        {
            var encoded = string.Concat(text.Where(c => !char.IsWhiteSpace(c))).Replace('-', '+').Replace('_', '/');
            encoded += new string('=', (4 - encoded.Length % 4) % 4);
            var decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded));
            var start = decoded.TrimStart();
            if (Starts(start, "vless://", "vmess://", "trojan://"))
                foreach (var line in decoded.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    ValidateUriText(line, allowShareFragment: true);
            return Starts(start, "vless://", "vmess://", "trojan://", "{", "[") || start.Contains("proxies:", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException) { return false; }
    }

    internal static void ValidateUriText(string text, bool allowShareFragment = false)
    {
        var fragment = allowShareFragment ? text.IndexOf('#') : -1;
        var transport = fragment >= 0 ? text[..fragment] : text;
        if (transport.EnumerateRunes().Any(rune => Rune.IsWhiteSpace(rune) || Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) ||
            fragment >= 0 && Uri.UnescapeDataString(text[(fragment + 1)..]).Any(c => char.IsControl(c)))
            throw Invalid("The link contains whitespace or invisible characters inside it. Copy the complete original link again.");
    }

    private static bool HasLinkScheme(string value)
        => Starts(value, "noravpn:", "http:", "https:", "happ:", "vless:", "vmess:", "trojan:", "awg:", "wireguard:");
    private static bool Starts(string value, params string[] prefixes)
        => prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    private static string TrimBoundary(string value)
    {
        var first = 0;
        var last = value.Length;
        while (first < last && IsBoundary(value[first])) first++;
        while (last > first && IsBoundary(value[last - 1])) last--;
        return value[first..last];
    }
    private static bool IsBoundary(char c) => char.IsWhiteSpace(c) || c is '\uFEFF' or '\u200B' or '\u2060';
    private static NoraAppException Invalid(string message) => new("NORA-SUB-4001", message);

    internal static int RunSelfTest(TextWriter output)
    {
        const string url = "https://example.com/MiXeD/AbC%2F+def?token=Aa%2Bb&x=%D0%AF";
        var wrappers = new[] { url, " \r\n" + url + "\u00a0", "\uFEFF\u200B" + url + "\u2060", "<" + url + ">", "\"" + url + "\"", "`" + url + "`" };
        var failures = new List<string>();
        foreach (var wrapper in wrappers)
        {
            var actual = Classify(wrapper);
            if (actual.Text != url || actual.Kind != NoraImportInputKind.Subscription) failures.Add("URL boundary parity");
            var deep = "noravpn://import/subscription?v=1&url=" + Uri.EscapeDataString(url);
            if (Classify(wrapper.Replace(url, deep)).DeepLink?.Payload != url) failures.Add("Deep-link boundary parity");
        }
        foreach (var invalid in new[] { "https://example.com/to\u200Bken", "https://example.com/to ken", "https://user:pass@example.com/", "noravpn://import/subscription?v=2&url=https%3A%2F%2Fexample.com" })
        {
            try { _ = Classify(invalid); failures.Add("Unsafe input accepted"); }
            catch (NoraAppException exception) { if (exception.Message.Contains(invalid, StringComparison.Ordinal)) failures.Add("Input leaked into error"); }
        }
        const string json = "{\n  \"outbounds\": []\n}";
        if (Classify(json).Text != json || Classify("[Interface]\nPrivateKey = fixture\n").Kind != NoraImportInputKind.Profile) failures.Add("Config whitespace/profile classification");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("vless://00000000-0000-4000-8000-000000000000@example.com:443#Fixture"));
        if (Classify(encoded).Kind != NoraImportInputKind.Subscription) failures.Add("Base64 subscription classification");
        if (Classify(encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_')).Kind != NoraImportInputKind.Subscription) failures.Add("Base64URL subscription classification");
        if (!NoraDeepLinkParser.LooksLikeDeepLink("\u2003noravpn://import/profile?v=1&data=nora1.fixture\u2003")) failures.Add("OS Unicode boundary recognition");
        var badUri = "vless://00000000-0000-4000-8000-000000000000@exa\u200Bmple.com:443#Fixture";
        foreach (var invalid in new[] { badUri + "\n" + "vless://00000000-0000-4000-8000-000000000000@example.com:443#Fixture", Convert.ToBase64String(Encoding.UTF8.GetBytes(badUri)), "https://example.com/to\U000E0001ken" })
        {
            try { _ = Classify(invalid); failures.Add("Wrapped unsafe URI accepted"); }
            catch (NoraAppException) { }
        }
        try { _ = NoraDeepLinkParser.Parse("noravpn://import/subscription?v=1&url=https://exa\tmple.com"); failures.Add("OS embedded tab accepted"); }
        catch (NoraAppException) { }
        foreach (var profileReference in new[] { "https://example.com/sub", "<https://example.com/sub>", "\"https://example.com/sub\"", "noravpn://import/profile?v=1&data=nora1.fixture" })
        {
            try { _ = NoraDeepLinkParser.Parse("noravpn://import/profile?v=1&data=" + Uri.EscapeDataString(profileReference)); failures.Add("Remote reference accepted as a local profile"); }
            catch (NoraAppException) { }
        }
        try { _ = Classify("vless://00000000-0000-4000-8000-000000000000@example.com#Name%0ASecondLine"); failures.Add("Encoded control accepted in display fragment"); }
        catch (NoraAppException) { }
        if (Classify("vless://00000000-0000-4000-8000-000000000000@example.com:443#Мой сервер 👩‍💻").Kind != NoraImportInputKind.Subscription) failures.Add("Unicode display title rejected");
        foreach (var failure in failures) output.WriteLine("FAIL: " + failure);
        if (failures.Count == 0) output.WriteLine("PASS: input boundary, credential preservation, strict deep-link and configuration parity");
        return failures.Count == 0 ? 0 : 1;
    }
}
#endif
