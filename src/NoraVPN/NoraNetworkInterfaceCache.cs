using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;

namespace Nvp;

internal sealed record NoraNetworkInterfaceSnapshot(
    IReadOnlyList<NetworkInterface> Interfaces,
    DateTimeOffset CapturedAt,
    TimeSpan EnumerationDuration,
    long NetworkGeneration);

/// <summary>
/// NetworkInterface.GetAllNetworkInterfaces can become very expensive on Windows
/// machines that have accumulated many hidden Wintun/WFP adapters. All desktop
/// callers share this background snapshot so adapter discovery can never stall
/// the WPF dispatcher.
/// </summary>
internal static class NoraNetworkInterfaceCache
{
    private static readonly object Sync = new();
    private static readonly TimeSpan MaxSnapshotAge = TimeSpan.FromMinutes(5);
    private static NoraNetworkInterfaceSnapshot _snapshot = new(
        Array.Empty<NetworkInterface>(),
        DateTimeOffset.MinValue,
        TimeSpan.Zero,
        0);
    private static Task<NoraNetworkInterfaceSnapshot>? _refreshTask;
    private static long _networkGeneration = 1;
    private static int _enumerationCount;

    static NoraNetworkInterfaceCache()
    {
        try
        {
            NetworkChange.NetworkAddressChanged += (_, _) => Invalidate();
            NetworkChange.NetworkAvailabilityChanged += (_, _) => Invalidate();
        }
        catch
        {
            // Snapshot expiry still keeps the cache correct on platforms where
            // network-change notifications are unavailable.
        }
    }

    public static int EnumerationCount => Volatile.Read(ref _enumerationCount);

    public static IReadOnlyList<NetworkInterface> CachedInterfaces
        => Volatile.Read(ref _snapshot).Interfaces;

    public static void Invalidate()
        => Interlocked.Increment(ref _networkGeneration);

    public static async Task WarmAsync()
    {
        try { await GetSnapshotAsync().ConfigureAwait(false); }
        catch { }
    }

    public static async Task<NoraNetworkInterfaceSnapshot> GetSnapshotAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        Task<NoraNetworkInterfaceSnapshot> refresh;
        lock (Sync)
        {
            var current = _snapshot;
            var generation = Volatile.Read(ref _networkGeneration);
            var valid = current.Interfaces.Count > 0 &&
                        current.NetworkGeneration == generation &&
                        DateTimeOffset.UtcNow - current.CapturedAt < MaxSnapshotAge;
            if (!forceRefresh && valid)
                return current;

            if (_refreshTask is null || _refreshTask.IsCompleted)
                _refreshTask = Task.Run(CaptureSnapshot);
            refresh = _refreshTask;
        }

        return await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public static NetworkInterface? FindByHint(
        IReadOnlyList<NetworkInterface> interfaces,
        string hint,
        bool requireUp = true)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return null;

        NetworkInterface? best = null;
        var bestScore = int.MaxValue;
        foreach (var networkInterface in interfaces)
        {
            try
            {
                if (requireUp && networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;
                var score = string.Equals(networkInterface.Name, hint, StringComparison.OrdinalIgnoreCase) ? 0 :
                    string.Equals(networkInterface.Description, hint, StringComparison.OrdinalIgnoreCase) ? 1 :
                    networkInterface.Name.Contains(hint, StringComparison.OrdinalIgnoreCase) ? 2 :
                    networkInterface.Description.Contains(hint, StringComparison.OrdinalIgnoreCase) ? 3 :
                    int.MaxValue;
                if (score >= bestScore)
                    continue;
                best = networkInterface;
                bestScore = score;
            }
            catch
            {
                // Broken filter pseudo-interfaces must not poison discovery of
                // a healthy physical or NORA tunnel adapter.
            }
        }
        return best;
    }

    public static bool HasActiveTunnelAdapterCached()
    {
        foreach (var networkInterface in CachedInterfaces)
        {
            try
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;
                var label = networkInterface.Name + " " + networkInterface.Description;
                if (label.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("Wintun", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("TAP", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
        }
        return false;
    }

    public static async Task<int> RunSelfTestAsync(TextWriter output)
    {
        try
        {
            Invalidate();
            var before = EnumerationCount;
            var dispatchWatch = Stopwatch.StartNew();
            var first = GetSnapshotAsync(forceRefresh: true);
            var waiters = Enumerable.Range(0, 15).Select(_ => GetSnapshotAsync()).Prepend(first).ToArray();
            var dispatchMilliseconds = dispatchWatch.ElapsedMilliseconds;
            var snapshots = await Task.WhenAll(waiters).ConfigureAwait(false);
            var enumerationDelta = EnumerationCount - before;

            var cacheWatch = Stopwatch.StartNew();
            var cached = await GetSnapshotAsync().ConfigureAwait(false);
            var cacheMilliseconds = cacheWatch.ElapsedMilliseconds;
            var sameSnapshot = snapshots.All(item => ReferenceEquals(item, snapshots[0]));
            var passed = dispatchMilliseconds < 250 &&
                         cacheMilliseconds < 100 &&
                         enumerationDelta == 1 &&
                         sameSnapshot &&
                         cached.Interfaces.Count > 0;

            output.WriteLine(
                $"NETWORK CACHE SELF-TEST {(passed ? "PASS" : "FAIL")}: " +
                $"interfaces={cached.Interfaces.Count}; dispatch_ms={dispatchMilliseconds}; " +
                $"enumeration_ms={cached.EnumerationDuration.TotalMilliseconds:F0}; " +
                $"cached_ms={cacheMilliseconds}; enumerations={enumerationDelta}; shared={sameSnapshot}");
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            output.WriteLine("NETWORK CACHE SELF-TEST FAIL: " + ex.GetBaseException().Message);
            return 1;
        }
    }

    private static NoraNetworkInterfaceSnapshot CaptureSnapshot()
    {
        Interlocked.Increment(ref _enumerationCount);
        var stopwatch = Stopwatch.StartNew();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            interfaces = [];
        }
        stopwatch.Stop();

        lock (Sync)
        {
            if (interfaces.Length == 0 && _snapshot.Interfaces.Count > 0)
                return _snapshot;
            var captured = new NoraNetworkInterfaceSnapshot(
                interfaces,
                DateTimeOffset.UtcNow,
                stopwatch.Elapsed,
                Volatile.Read(ref _networkGeneration));
            _snapshot = captured;
            return captured;
        }
    }
}
