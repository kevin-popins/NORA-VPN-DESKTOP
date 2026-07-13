using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoraPremium.Service;

internal sealed class PremiumCrypto : IDisposable
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly byte[] _pepper;
    private readonly ECDsa _signer;

    public PremiumCrypto(string dataRoot, string pepperBase64)
    {
        try
        {
            _pepper = Convert.FromBase64String(pepperBase64);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("NORA_PREMIUM_PEPPER must be valid base64.", ex);
        }
        if (_pepper.Length < 32)
            throw new InvalidOperationException("NORA_PREMIUM_PEPPER must contain at least 32 random bytes.");

        var keyDir = Path.Combine(dataRoot, "keys");
        Directory.CreateDirectory(keyDir);
        var keyPath = Path.Combine(keyDir, "signing-private.pem");
        _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (File.Exists(keyPath))
        {
            _signer.ImportFromPem(File.ReadAllText(keyPath));
        }
        else
        {
            File.WriteAllText(keyPath, _signer.ExportPkcs8PrivateKeyPem());
            TryRestrict(keyPath);
        }
    }

    public string PublicKeyPem => _signer.ExportSubjectPublicKeyInfoPem();

    public string KeyId
    {
        get
        {
            var hash = SHA256.HashData(_signer.ExportSubjectPublicKeyInfo());
            return Convert.ToHexString(hash)[..12].ToLowerInvariant();
        }
    }

    public string GenerateCode()
    {
        Span<byte> random = stackalloc byte[20];
        RandomNumberGenerator.Fill(random);
        Span<char> chars = stackalloc char[20];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = CodeAlphabet[random[i] % CodeAlphabet.Length];
        return $"NORA-PRM-{new string(chars[..4])}-{new string(chars[4..8])}-{new string(chars[8..12])}-{new string(chars[12..16])}-{new string(chars[16..20])}";
    }

    public static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "";
        var canonical = new string(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return canonical.StartsWith("NORAPRM", StringComparison.Ordinal) && canonical.Length == 27
            ? canonical
            : "";
    }

    public string DigestCode(string normalizedCode) => Hmac("code:" + normalizedCode);
    public string DigestInstallation(string installationId) => Hmac("install:" + installationId.Trim());

    public string Sign(PremiumTokenPayload payload)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var payloadPart = Base64Url(payloadBytes);
        var data = Encoding.ASCII.GetBytes(payloadPart);
        var signature = _signer.SignData(
            data,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return payloadPart + "." + Base64Url(signature);
    }

    public bool TryVerify(string token, out PremiumTokenPayload payload)
    {
        payload = null!;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2)
                return false;
            var data = Encoding.ASCII.GetBytes(parts[0]);
            var signature = FromBase64Url(parts[1]);
            if (!_signer.VerifyData(
                    data,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return false;
            payload = JsonSerializer.Deserialize<PremiumTokenPayload>(FromBase64Url(parts[0]), JsonOptions)!;
            return payload is not null && payload.Version == 1 && payload.KeyId == KeyId;
        }
        catch
        {
            return false;
        }
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        const int iterations = 310_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"v1.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split('.');
            if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations))
                return false;
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private string Hmac(string value)
    {
        using var hmac = new HMACSHA256(_pepper);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    private static void TryRestrict(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { }
        }
    }

    public void Dispose() => _signer.Dispose();
}
