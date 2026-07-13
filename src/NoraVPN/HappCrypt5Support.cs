using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nvp;

internal static class HappCrypt5Decoder
{
    internal const string Prefix = "happ://crypt5/";
    private const string KeyResourceName = "Nvp.HappCrypt5Keys.json";
    private const int MaximumPayloadLength = 256 * 1024;

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Keys = new(LoadKeys);

    public static bool IsCrypt5Link(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string Decrypt(string value)
    {
        try
        {
            var normalized = value.Trim();
            var fragment = normalized.IndexOf('#');
            if (fragment >= 0)
                normalized = normalized[..fragment];
            if (!normalized.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("The value is not a HAPP crypt5 link.");

            var payload = normalized[Prefix.Length..];
            if (payload.Length is 0 or > MaximumPayloadLength || payload.Any(character => character > 0x7f))
                throw new FormatException("The HAPP crypt5 payload is empty or invalid.");

            // Keep the same public/JNI boundary as HAPP Android: Java applies the
            // six-character shuffle and the native routine reverses it before parsing.
            var nativeInput = ShuffleSix(payload);
            var original = InverseShuffleSix(nativeInput);
            var shuffled = PermuteFour(original);
            if (shuffled.Length < 8)
                throw new FormatException("The HAPP crypt5 payload is truncated.");

            var marker = shuffled[..4] + shuffled[^4..];
            if (!Keys.Value.TryGetValue(marker, out var encodedPrivateKey))
                throw new FormatException("The HAPP crypt5 key marker is not supported.");

            var body = shuffled[4..^4];
            if (body.Length < 13)
                throw new FormatException("The HAPP crypt5 body is truncated.");

            var nonce = Encoding.ASCII.GetBytes(body[..12]);
            var rest = body[12..];
            var digitCount = 0;
            while (digitCount < rest.Length && char.IsAsciiDigit(rest[digitCount]))
                digitCount++;
            if (digitCount == 0 || !int.TryParse(rest[..digitCount], out var segmentLength) || segmentLength <= 0)
                throw new FormatException("The HAPP crypt5 segment length is invalid.");

            var packed = rest[digitCount..];
            if (packed.Length < 1 + segmentLength)
                throw new FormatException("The HAPP crypt5 encrypted segment is truncated.");
            var encryptedSegment = packed.Substring(1, segmentLength);
            var rsaSegment = packed[(1 + segmentLength)..];
            if (rsaSegment.Length == 0)
                throw new FormatException("The HAPP crypt5 RSA segment is missing.");

            using var rsa = RSA.Create();
            var privateKey = DecodeBase64(encodedPrivateKey);
            rsa.ImportPkcs8PrivateKey(privateKey, out var bytesRead);
            if (bytesRead != privateKey.Length)
                throw new CryptographicException("The HAPP crypt5 key is malformed.");
            var rsaPlain = Encoding.UTF8.GetString(
                rsa.Decrypt(DecodeBase64(rsaSegment), RSAEncryptionPadding.Pkcs1));
            var chachaKey = DecodeBase64(SwapPairs(rsaPlain));
            if (chachaKey.Length != 32)
                throw new CryptographicException("The HAPP crypt5 session key is malformed.");

            var encrypted = DecodeBase64(encryptedSegment);
            if (encrypted.Length <= 16)
                throw new FormatException("The HAPP crypt5 encrypted data is truncated.");
            var ciphertext = encrypted.AsSpan(0, encrypted.Length - 16);
            var tag = encrypted.AsSpan(encrypted.Length - 16, 16);
            var plaintext = new byte[ciphertext.Length];
            using (var cipher = new ChaCha20Poly1305(chachaKey))
                cipher.Decrypt(nonce, ciphertext, tag, plaintext);

            var wrappedBase64 = SwapPairs(Encoding.UTF8.GetString(plaintext));
            var result = Encoding.UTF8.GetString(DecodeBase64(wrappedBase64)).Trim();
            if (string.IsNullOrWhiteSpace(result))
                throw new FormatException("The HAPP crypt5 result is empty.");
            return result;
        }
        catch (NoraAppException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            throw new NoraAppException(
                "NORA-SUB-4002",
                "The HAPP crypt5 link is damaged, expired, or uses an unsupported key.",
                exception);
        }
    }

    public static int RunSelfTest(TextWriter output)
    {
        try
        {
            const string expected = "https://example.test/nora/crypt5";
            var marker = Keys.Value.Keys.Order(StringComparer.Ordinal).First();
            var link = BuildSyntheticLink(marker, Keys.Value[marker], expected);
            var shuffleFixture = "abcdefghijklmnopqrstuvwxyz0123456789";
            var passed = Decrypt(link) == expected &&
                         InverseShuffleSix(ShuffleSix(shuffleFixture)) == shuffleFixture &&
                         SwapPairs(SwapPairs(shuffleFixture)) == shuffleFixture &&
                         PermuteFour(PermuteFour(shuffleFixture)) == shuffleFixture;

            var malformedRejected = false;
            try { _ = Decrypt(Prefix + "broken"); }
            catch (NoraAppException) { malformedRejected = true; }
            passed &= malformedRejected;

            output.WriteLine(passed
                ? $"HAPP CRYPT5 SELF-TEST PASS: keys={Keys.Value.Count}; synthetic=ok; malformed=rejected"
                : "HAPP CRYPT5 SELF-TEST FAIL");
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            output.WriteLine("HAPP CRYPT5 SELF-TEST FAIL: " + exception.GetType().Name);
            return 1;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadKeys()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(KeyResourceName)
            ?? throw new InvalidOperationException("The embedded HAPP crypt5 key map is missing.");
        var keys = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("The embedded HAPP crypt5 key map is empty.");
        if (keys.Count != 34 || keys.Any(pair => pair.Key.Length != 8 || string.IsNullOrWhiteSpace(pair.Value)))
            throw new InvalidOperationException("The embedded HAPP crypt5 key map is invalid.");
        return keys;
    }

    private static string BuildSyntheticLink(string marker, string encodedPrivateKey, string expected)
    {
        var chachaKey = SHA256.HashData(Encoding.ASCII.GetBytes(marker));
        var nonceText = marker + "test";
        var nonce = Encoding.ASCII.GetBytes(nonceText);
        var chachaPlaintext = Encoding.UTF8.GetBytes(SwapPairs(Convert.ToBase64String(Encoding.UTF8.GetBytes(expected))));
        var ciphertext = new byte[chachaPlaintext.Length];
        var tag = new byte[16];
        using (var cipher = new ChaCha20Poly1305(chachaKey))
            cipher.Encrypt(nonce, chachaPlaintext, ciphertext, tag);
        var encrypted = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, encrypted, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, encrypted, ciphertext.Length, tag.Length);
        var encryptedSegment = Convert.ToBase64String(encrypted);

        using var rsa = RSA.Create();
        var privateKey = DecodeBase64(encodedPrivateKey);
        rsa.ImportPkcs8PrivateKey(privateKey, out _);
        var rsaPlaintext = Encoding.UTF8.GetBytes(SwapPairs(Convert.ToBase64String(chachaKey)));
        var rsaSegment = Convert.ToBase64String(rsa.Encrypt(rsaPlaintext, RSAEncryptionPadding.Pkcs1));

        var body = nonceText + encryptedSegment.Length + "f" + encryptedSegment + rsaSegment;
        var shuffled = marker[..4] + body + marker[4..];
        return Prefix + PermuteFour(shuffled);
    }

    private static byte[] DecodeBase64(string value)
    {
        var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        try { return Convert.FromBase64String(normalized); }
        catch (FormatException exception) { throw new FormatException("The HAPP crypt5 payload contains invalid Base64.", exception); }
    }

    private static string ShuffleSix(string value) => ShuffleBlocks(value, 6, [1, 3, 5, 0, 2, 4]);
    private static string InverseShuffleSix(string value) => ShuffleBlocks(value, 6, [3, 0, 4, 1, 5, 2]);
    private static string SwapPairs(string value) => ShuffleBlocks(value, 2, [1, 0]);
    private static string PermuteFour(string value) => ShuffleBlocks(value, 4, [2, 3, 0, 1]);

    private static string ShuffleBlocks(string value, int blockSize, int[] order)
    {
        var builder = new StringBuilder(value.Length);
        var fullLength = value.Length / blockSize * blockSize;
        for (var offset = 0; offset < fullLength; offset += blockSize)
            foreach (var index in order)
                builder.Append(value[offset + index]);
        if (fullLength < value.Length)
            builder.Append(value.AsSpan(fullLength));
        return builder.ToString();
    }
}
