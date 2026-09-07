#if WINDOWS
using System.IO;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace Nvp;

internal sealed record NoraUpdateInfo(
    string Version,
    Uri ReleasePage,
    Uri SetupDownload,
    string SetupAssetName,
    long SetupAssetSize,
    string Sha256Digest);

internal static partial class NoraUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/kevin-popins/NORA-VPN-DESKTOP/releases/latest";
    private const int MaximumInstallerBytes = 512 * 1024 * 1024;
    private static readonly HttpClient Http = CreateHttpClient();

    public static string CurrentVersion
    {
        get
        {
            var assembly = typeof(NoraUpdateService).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return NormalizeVersion(informational ?? assembly.GetName().Version?.ToString() ?? "0.0.0");
        }
    }

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30), TimeSpan.FromHours(1)
    ];

    public static Task<NoraUpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
        => CheckWithRetryAsync(
            token => CheckOnceAsync(Http, CurrentVersion, RequestTimeout, token),
            (delay, token) => Task.Delay(delay, token), cancellationToken);

    private static async Task<NoraUpdateInfo?> CheckOnceAsync(
        HttpClient client, string currentVersion, TimeSpan requestTimeout, CancellationToken cancellationToken)
    {
        // HttpClient's timeout with ResponseHeadersRead does not cover the body.
        // Keep one budget around headers AND JSON so a stalled response can retry.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(requestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && IsTransientStatus(response.StatusCode))
        {
            var retryAfter = response.Headers.RetryAfter;
            var delay = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow);
            // GitHub also uses a rate-limit reset epoch when the primary limit is exhausted.
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues) &&
                long.TryParse(resetValues.FirstOrDefault(), out var resetSeconds) &&
                resetSeconds is >= 0 and <= 253402300799L)
            {
                var resetDelay = DateTimeOffset.FromUnixTimeSeconds(resetSeconds) - DateTimeOffset.UtcNow;
                if (delay is null || resetDelay > delay) delay = resetDelay;
            }
            throw new RetryableUpdateException(response.StatusCode, delay);
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(attempt.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: attempt.Token).ConfigureAwait(false);
        return ParseLatestRelease(document.RootElement, currentVersion);
    }

    internal static async Task<NoraUpdateInfo?> CheckWithRetryAsync(
        Func<CancellationToken, Task<NoraUpdateInfo?>> check,
        Func<TimeSpan, CancellationToken, Task> wait,
        CancellationToken cancellationToken)
    {
        var retryIndex = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan? serverDelay = null;
            try
            {
                // A successful check, even with no newer version, ends this startup
                // check. Only transient failures retry; there is one request at a time.
                return await check(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (HttpRequestException exception) when (
                !cancellationToken.IsCancellationRequested &&
                (exception.StatusCode is null || IsTransientStatus(exception.StatusCode.Value)))
            {
                serverDelay = (exception as RetryableUpdateException)?.RetryDelay;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested) { }

            var delay = RetryDelays[retryIndex];
            if (retryIndex < RetryDelays.Length - 1) retryIndex++;
            if (serverDelay > delay) delay = serverDelay.Value;
            // Task.Delay supports at most about 49 days; wait long Retry-After
            // values in cancellable chunks without making an early HTTP request.
            while (delay > TimeSpan.Zero)
            {
                var chunk = delay > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : delay;
                await wait(chunk, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                delay -= chunk;
            }
        }
    }

    private static bool IsTransientStatus(HttpStatusCode status)
        => status is HttpStatusCode.Forbidden or HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests || (int)status >= 500;

    private sealed class RetryableUpdateException(HttpStatusCode status, TimeSpan? retryDelay)
        : HttpRequestException("Update endpoint temporarily unavailable.", null, status)
    {
        public TimeSpan? RetryDelay { get; } = retryDelay;
    }
    internal static NoraUpdateInfo? ParseLatestRelease(JsonElement root, string currentVersion)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("tag_name", out var tagElement) ||
            !root.TryGetProperty("html_url", out var pageElement))
            return null;

        var version = NormalizeVersion(tagElement.GetString() ?? "");
        if (version.Length == 0 || CompareVersions(version, currentVersion) <= 0)
            return null;
        if (!Uri.TryCreate(pageElement.GetString(), UriKind.Absolute, out var releasePage) ||
            releasePage.Scheme != Uri.UriSchemeHttps ||
            !releasePage.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var expectedName = $"NORA-VPN-Setup-v{version}-win-x64.exe";
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement) ||
                !string.Equals(nameElement.GetString(), expectedName, StringComparison.Ordinal))
                continue;
            if (!asset.TryGetProperty("browser_download_url", out var downloadElement) ||
                !Uri.TryCreate(downloadElement.GetString(), UriKind.Absolute, out var download) ||
                download.Scheme != Uri.UriSchemeHttps ||
                !download.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return null;
            var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize)
                ? parsedSize
                : 0;
            var digest = asset.TryGetProperty("digest", out var digestElement)
                ? (digestElement.GetString() ?? "")
                : "";
            if (size is <= 0 or > MaximumInstallerBytes || !IsSha256Digest(digest))
                return null;
            return new NoraUpdateInfo(version, releasePage, download, expectedName, size, digest.ToLowerInvariant());
        }
        return null;
    }

    internal static int CompareVersions(string left, string right)
    {
        var leftVersion = ParsedVersion.Parse(left);
        var rightVersion = ParsedVersion.Parse(right);
        for (var index = 0; index < 3; index++)
        {
            var comparison = leftVersion.Numbers[index].CompareTo(rightVersion.Numbers[index]);
            if (comparison != 0)
                return comparison;
        }
        if (leftVersion.PreRelease.Count == 0 || rightVersion.PreRelease.Count == 0)
            return leftVersion.PreRelease.Count == rightVersion.PreRelease.Count
                ? 0
                : leftVersion.PreRelease.Count == 0 ? 1 : -1;
        for (var index = 0; index < Math.Max(leftVersion.PreRelease.Count, rightVersion.PreRelease.Count); index++)
        {
            if (index >= leftVersion.PreRelease.Count) return -1;
            if (index >= rightVersion.PreRelease.Count) return 1;
            var comparison = CompareIdentifier(leftVersion.PreRelease[index], rightVersion.PreRelease[index]);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    internal static int RunSelfTest(TextWriter output)
    {
        const string json = """
        {
          "tag_name": "0.1.0-pre.6",
          "html_url": "https://github.com/kevin-popins/NORA-VPN-DESKTOP/releases/tag/0.1.0-pre.6",
          "assets": [{
            "name": "NORA-VPN-Setup-v0.1.0-pre.6-win-x64.exe",
            "browser_download_url": "https://github.com/kevin-popins/NORA-VPN-DESKTOP/releases/download/0.1.0-pre.6/NORA-VPN-Setup-v0.1.0-pre.6-win-x64.exe",
            "size": 240000000,
            "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
          }]
        }
        """;
        try
        {
            using var document = JsonDocument.Parse(json);
            var available = ParseLatestRelease(document.RootElement, "0.1.0-pre.5");
            var sameHidden = ParseLatestRelease(document.RootElement, "0.1.0-pre.6") is null;
            var newerFinal = CompareVersions("0.1.0", "0.1.0-pre.99") > 0;
            var newerPreview = CompareVersions("0.1.0-pre.10", "0.1.0-pre.9") > 0;
            var retriesPassed = RunRetrySelfTestsAsync(json, output).GetAwaiter().GetResult();
            var passed = available?.Version == "0.1.0-pre.6" && sameHidden && newerFinal && newerPreview && retriesPassed;
            output.WriteLine(passed
                ? "UPDATE SELF-TEST PASS: release parsing, exact Setup asset, SHA-256 metadata and semantic version ordering are valid"
                : "UPDATE SELF-TEST FAIL");
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            output.WriteLine("UPDATE SELF-TEST FAIL: " + exception.GetBaseException().Message);
            return 1;
        }
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = int.TryParse(left, out var leftNumber);
        var rightNumeric = int.TryParse(right, out var rightNumber);
        if (leftNumeric && rightNumeric) return leftNumber.CompareTo(rightNumber);
        if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];
        var metadata = normalized.IndexOf('+');
        return metadata >= 0 ? normalized[..metadata] : normalized;
    }

    private static bool IsSha256Digest(string value)
        => value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
           value.Length == "sha256:".Length + 64 &&
           value["sha256:".Length..].All(Uri.IsHexDigit);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NORA-VPN-Desktop/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        return client;
    }

    private sealed record ParsedVersion(int[] Numbers, IReadOnlyList<string> PreRelease)
    {
        public static ParsedVersion Parse(string value)
        {
            var normalized = NormalizeVersion(value);
            var dash = normalized.IndexOf('-');
            var core = dash >= 0 ? normalized[..dash] : normalized;
            var pre = dash >= 0 ? normalized[(dash + 1)..] : "";
            var parts = core.Split('.');
            if (parts.Length < 2 || parts.Length > 3)
                throw new FormatException("Unsupported NORA version: " + value);
            var numbers = new int[3];
            for (var index = 0; index < parts.Length; index++)
                if (!int.TryParse(parts[index], out numbers[index]) || numbers[index] < 0)
                    throw new FormatException("Unsupported NORA version: " + value);
            var identifiers = pre.Length == 0
                ? Array.Empty<string>()
                : pre.Split(['.', '-'], StringSplitOptions.RemoveEmptyEntries);
            return new ParsedVersion(numbers, identifiers);
        }
    }
}
#endif
