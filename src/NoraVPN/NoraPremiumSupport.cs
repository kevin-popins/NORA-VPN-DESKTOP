#if WINDOWS
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nvp;

internal enum NoraPremiumStatus
{
    Free,
    Active,
    OfflineGrace,
    Invalid
}

internal sealed record NoraPremiumOperationResult(bool Success, string Status, string Message);

internal static class NoraPremiumService
{
    private const string ApiBase = "https://norapremium.kevinrobertson.ru/";
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEov8f+q/A2OdTrX6aZx7l4oES3J6f
        wQ274l5jD2SJBT++qPkYP4WEbubEubwAA0usk2eZmmnlDWkR7wix1qpPcQ==
        -----END PUBLIC KEY-----
        """;
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("NORA Premium local state v1"));
    private static readonly object Sync = new();
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static PremiumLocalState? _state;
    private static AppearanceSettings? _appearance;
    private static PremiumTokenPayload? _payload;
    private static NoraPremiumStatus _status;

    public static event Action? Changed;

    public static NoraPremiumStatus Status
    {
        get { EnsureLoaded(); return _status; }
    }

    public static bool IsPremium
    {
        get { EnsureLoaded(); return _status is NoraPremiumStatus.Active or NoraPremiumStatus.OfflineGrace; }
    }

    public static bool EqualizerEnabled
    {
        get { EnsureLoaded(); return _appearance!.EqualizerEnabled; }
    }

    public static bool ServerSlideshowEnabled
    {
        get { EnsureLoaded(); return _appearance!.ServerSlideshowEnabled; }
    }

    public static bool EqualizerEffective
        => IsPremium && EqualizerEnabled && HasEntitlement("appearance.equalizer");

    public static bool ServerSlideshowEffective
        => IsPremium && ServerSlideshowEnabled && HasEntitlement("appearance.server_slideshow");

    // This stays in the local technical log rather than the Appearance page.
    // It makes a failed visualizer diagnosis actionable without exposing
    // activation identifiers, codes, or token data.
    internal static string GetEqualizerDiagnostic()
    {
        EnsureLoaded();
        var entitled = HasEntitlement("appearance.equalizer");
        return $"Audio visualizer: Premium={_status}; setting={(EqualizerEnabled ? "on" : "off")}; entitlement={(entitled ? "present" : "missing")}; renderer={(EqualizerEffective ? "enabled" : "disabled")}.";
    }

    public static DateTimeOffset? ValidUntil
    {
        get { EnsureLoaded(); return _payload is null ? null : DateTimeOffset.FromUnixTimeSeconds(_payload.ValidUntil); }
    }

    public static void SetEqualizerEnabled(bool enabled)
    {
        EnsureLoaded();
        lock (Sync)
        {
            _appearance!.EqualizerEnabled = enabled;
            SaveAppearance();
        }
        Changed?.Invoke();
    }

    public static void SetServerSlideshowEnabled(bool enabled)
    {
        EnsureLoaded();
        lock (Sync)
        {
            _appearance!.ServerSlideshowEnabled = enabled;
            SaveAppearance();
        }
        Changed?.Invoke();
    }

    public static async Task<NoraPremiumOperationResult> ActivateAsync(string code, CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        code = NormalizeCode(code);
        if (code.Length == 0)
            return new NoraPremiumOperationResult(false, "invalid_code", "Enter a valid NORA Premium code.");
        try
        {
            var request = new ActivationRequest(code, _state!.InstallationId, AppVersion());
            using var response = await Http.PostAsJsonAsync("api/v1/activate", request, JsonOptions, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ParseError(body, "Premium activation failed. Try again.");
            var token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions);
            if (token is null || !TryValidateToken(token.Token, out var payload))
                return new NoraPremiumOperationResult(false, "invalid_token", "The server response could not be verified.");
            lock (Sync)
            {
                _state.Token = token.Token;
                _payload = payload;
                _status = NoraPremiumStatus.Active;
                SaveState();
            }
            Changed?.Invoke();
            return new NoraPremiumOperationResult(true, "active", "Premium appearance is active on this device.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NoraPremiumOperationResult(false, "timeout", "The activation server did not respond in time.");
        }
        catch
        {
            return new NoraPremiumOperationResult(false, "offline", "Connect to the internet for the first Premium activation.");
        }
    }

    public static async Task<NoraPremiumOperationResult> RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        if (_payload is null || string.IsNullOrWhiteSpace(_state!.Token))
            return new NoraPremiumOperationResult(false, "free", "No Premium activation is stored.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!force && now < _payload.RefreshAfter)
            return new NoraPremiumOperationResult(true, "current", "Premium access is current.");
        try
        {
            var request = new RefreshRequest(_state.Token, _state.InstallationId, AppVersion());
            using var response = await Http.PostAsJsonAsync("api/v1/refresh", request, JsonOptions, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode is 400 or 403)
                {
                    lock (Sync)
                    {
                        _state.Token = "";
                        _payload = null;
                        _status = NoraPremiumStatus.Invalid;
                        SaveState();
                    }
                    Changed?.Invoke();
                }
                return ParseError(body, "Premium access could not be refreshed.");
            }
            var token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions);
            if (token is null || !TryValidateToken(token.Token, out var payload))
                return new NoraPremiumOperationResult(false, "invalid_token", "The refreshed token could not be verified.");
            lock (Sync)
            {
                _state.Token = token.Token;
                _payload = payload;
                _status = NoraPremiumStatus.Active;
                SaveState();
            }
            Changed?.Invoke();
            return new NoraPremiumOperationResult(true, "active", "Premium access was refreshed.");
        }
        catch
        {
            EvaluateStoredToken();
            return new NoraPremiumOperationResult(IsPremium, "offline", IsPremium
                ? "Premium is using its protected offline entitlement."
                : "Premium could not be refreshed while offline.");
        }
    }

    private static void EnsureLoaded()
    {
        if (_state is not null)
            return;
        lock (Sync)
        {
            if (_state is not null)
                return;
            _appearance = LoadAppearance();
            _state = LoadState();
            if (string.IsNullOrWhiteSpace(_state.InstallationId) || !Guid.TryParse(_state.InstallationId, out _))
            {
                _state.InstallationId = Guid.NewGuid().ToString("D");
                _state.Token = "";
                SaveState();
            }
            EvaluateStoredToken();
        }
    }

    private static void EvaluateStoredToken()
    {
        _payload = null;
        _status = NoraPremiumStatus.Free;
        if (_state is null || string.IsNullOrWhiteSpace(_state.Token))
            return;
        if (!TryValidateToken(_state.Token, out var payload))
        {
            _status = NoraPremiumStatus.Invalid;
            return;
        }
        _payload = payload;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now <= payload.ValidUntil)
            _status = NoraPremiumStatus.Active;
        else if (now <= payload.ValidUntil + (long)TimeSpan.FromDays(30).TotalSeconds)
            _status = NoraPremiumStatus.OfflineGrace;
        else
            _status = NoraPremiumStatus.Invalid;
    }

    private static bool TryValidateToken(string token, out PremiumTokenPayload payload)
    {
        payload = null!;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2)
                return false;
            var data = Encoding.ASCII.GetBytes(parts[0]);
            var signature = FromBase64Url(parts[1]);
            using var verifier = ECDsa.Create();
            verifier.ImportFromPem(PublicKeyPem);
            if (!verifier.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return false;
            payload = JsonSerializer.Deserialize<PremiumTokenPayload>(FromBase64Url(parts[0]), JsonOptions)!;
            return payload is not null &&
                   payload.Version == 1 &&
                   payload.Edition == "visual_premium" &&
                   _state is not null &&
                   string.Equals(payload.InstallationId, _state.InstallationId, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasEntitlement(string entitlement)
    {
        EnsureLoaded();
        return _payload?.Entitlements.Contains(entitlement, StringComparer.Ordinal) == true;
    }

    private static PremiumLocalState LoadState()
    {
        try
        {
            var path = StatePath();
            if (!File.Exists(path))
                return new PremiumLocalState { InstallationId = Guid.NewGuid().ToString("D") };
            var protectedBytes = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<PremiumLocalState>(bytes, JsonOptions) ?? new PremiumLocalState();
        }
        catch
        {
            return new PremiumLocalState { InstallationId = Guid.NewGuid().ToString("D") };
        }
    }

    private static AppearanceSettings LoadAppearance()
    {
        try
        {
            var path = AppearancePath();
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path), JsonOptions) ?? new AppearanceSettings()
                : new AppearanceSettings();
        }
        catch
        {
            return new AppearanceSettings();
        }
    }

    private static void SaveState()
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(_state, JsonOptions);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            AtomicWrite(StatePath(), protectedBytes);
        }
        catch { }
    }

    private static void SaveAppearance()
    {
        try
        {
            AtomicWrite(AppearancePath(), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_appearance, JsonOptions)));
        }
        catch { }
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    private static NoraPremiumOperationResult ParseError(string body, string fallback)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ApiError>(body, JsonOptions);
            return new NoraPremiumOperationResult(false, error?.Status ?? "error", error?.Message ?? fallback);
        }
        catch
        {
            return new NoraPremiumOperationResult(false, "error", fallback);
        }
    }

    private static string NormalizeCode(string value)
    {
        var canonical = new string((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (!canonical.StartsWith("NORAPRM", StringComparison.Ordinal) || canonical.Length != 27)
            return "";
        var body = canonical[7..];
        return $"NORA-PRM-{body[..4]}-{body[4..8]}-{body[8..12]}-{body[12..16]}-{body[16..20]}";
    }

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(ApiBase), Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NORA-VPN-Premium/1.0");
        return client;
    }

    private static string AppVersion() => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    private static string PremiumDataRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("NORA_PREMIUM_DATA_ROOT");
        return string.IsNullOrWhiteSpace(overrideRoot) ? NoraAppState.DataRoot : Path.GetFullPath(overrideRoot);
    }

    private static string StatePath() => Path.Combine(PremiumDataRoot(), "premium.dat");
    private static string AppearancePath() => Path.Combine(PremiumDataRoot(), "appearance.json");

    private sealed class PremiumLocalState
    {
        public string InstallationId { get; set; } = "";
        public string Token { get; set; } = "";
    }

    private sealed class AppearanceSettings
    {
        public bool EqualizerEnabled { get; set; }
        public bool ServerSlideshowEnabled { get; set; }
    }

    private sealed record ActivationRequest(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("installation_id")] string InstallationId,
        [property: JsonPropertyName("app_version")] string AppVersion);

    private sealed record RefreshRequest(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("installation_id")] string InstallationId,
        [property: JsonPropertyName("app_version")] string AppVersion);

    private sealed record TokenResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("valid_until")] long ValidUntil,
        [property: JsonPropertyName("entitlements")] string[] Entitlements);

    private sealed record ApiError(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("message")] string Message);

    private sealed record PremiumTokenPayload(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("license_id")] string LicenseId,
        [property: JsonPropertyName("activation_id")] string ActivationId,
        [property: JsonPropertyName("installation_id")] string InstallationId,
        [property: JsonPropertyName("edition")] string Edition,
        [property: JsonPropertyName("entitlements")] string[] Entitlements,
        [property: JsonPropertyName("issued_at")] long IssuedAt,
        [property: JsonPropertyName("refresh_after")] long RefreshAfter,
        [property: JsonPropertyName("valid_until")] long ValidUntil,
        [property: JsonPropertyName("key_id")] string KeyId);
}
#endif
