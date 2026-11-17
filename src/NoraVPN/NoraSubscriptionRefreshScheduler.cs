#if WINDOWS
using System.Collections.Concurrent;
using System.IO;

namespace Nvp;

internal sealed record NoraSubscriptionRefreshResult(
    NoraSubscriptionInfo Subscription,
    NoraSubscriptionInfo? Refreshed,
    Exception? Error);

internal sealed record NoraSubscriptionRefreshPassResult(
    bool Startup,
    int CandidateCount,
    int Succeeded,
    int Failed,
    int Removed);

internal sealed class NoraSubscriptionRemovedException(string subscriptionId)
    : OperationCanceledException($"Subscription {subscriptionId} no longer exists.");

/// <summary>
/// Coalesces refreshes for the same subscription and serializes subscription
/// store writes. Manual refresh and the background scheduler share this gate.
/// </summary>
internal sealed class NoraSubscriptionRefreshCoordinator(
    Func<NoraSubscriptionInfo, CancellationToken, Task<NoraSubscriptionInfo>> refresh)
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Task<NoraSubscriptionInfo>> _inFlight =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _serial = new(1, 1);

    public Task<NoraSubscriptionInfo> RefreshAsync(
        NoraSubscriptionInfo subscription,
        CancellationToken cancellationToken = default)
    {
        var key = RefreshKey(subscription);
        lock (_sync)
        {
            if (_inFlight.TryGetValue(key, out var existing))
                return cancellationToken.CanBeCanceled ? existing.WaitAsync(cancellationToken) : existing;

            var completion = new TaskCompletionSource<NoraSubscriptionInfo>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight[key] = completion.Task;
            _ = CompleteAsync(key, subscription, cancellationToken, completion);
            return completion.Task;
        }
    }

    public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> mutation, CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await mutation().ConfigureAwait(false);
        }
        finally
        {
            _serial.Release();
        }
    }

    public Task<T> RunExclusiveSyncAsync<T>(Func<T> mutation, CancellationToken cancellationToken = default)
        => RunExclusiveAsync(() => Task.FromResult(mutation()), cancellationToken);

    private async Task CompleteAsync(
        string key,
        NoraSubscriptionInfo subscription,
        CancellationToken cancellationToken,
        TaskCompletionSource<NoraSubscriptionInfo> completion)
    {
        try
        {
            await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                completion.TrySetResult(await refresh(subscription, cancellationToken).ConfigureAwait(false));
            }
            finally
            {
                _serial.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            lock (_sync)
            {
                if (_inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, completion.Task))
                    _inFlight.Remove(key);
            }
        }
    }

    private static string RefreshKey(NoraSubscriptionInfo subscription)
    {
        if (!string.IsNullOrWhiteSpace(subscription.Id))
            return subscription.Id.Trim();
        if (!string.IsNullOrWhiteSpace(subscription.SourceUrl))
            return subscription.SourceUrl.Trim();
        return "subscription-instance-" + subscription.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Refreshes every URL-backed subscription once at application startup, then
/// refreshes it again whenever its provider-supplied interval has elapsed.
/// The loop runs on the thread pool and never starts or stops the VPN tunnel.
/// </summary>
internal sealed class NoraSubscriptionRefreshScheduler : IDisposable
{
    private static readonly TimeSpan FailedRefreshRetryDelay = TimeSpan.FromMinutes(5);
    private readonly Func<IReadOnlyList<NoraSubscriptionInfo>> _loadSubscriptions;
    private readonly NoraSubscriptionRefreshCoordinator _coordinator;
    private readonly Action<NoraSubscriptionRefreshResult> _completed;
    private readonly Action<NoraSubscriptionRefreshPassResult>? _passCompleted;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly TimeSpan _scanInterval;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _retryAfter =
        new(StringComparer.OrdinalIgnoreCase);
    private int _started;
    private int _disposed;
    private bool _startupScanPending;
    private Task? _worker;

    public NoraSubscriptionRefreshScheduler(
        Func<IReadOnlyList<NoraSubscriptionInfo>> loadSubscriptions,
        NoraSubscriptionRefreshCoordinator coordinator,
        Action<NoraSubscriptionRefreshResult> completed,
        Func<DateTimeOffset>? utcNow = null,
        TimeSpan? scanInterval = null,
        Action<NoraSubscriptionRefreshPassResult>? passCompleted = null)
    {
        _loadSubscriptions = loadSubscriptions;
        _coordinator = coordinator;
        _completed = completed;
        _passCompleted = passCompleted;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _scanInterval = scanInterval ?? TimeSpan.FromMinutes(1);
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;
        _worker = Task.Run(() => RunLoopAsync(_cancellation.Token));
    }

    internal async Task RunPassAsync(bool startup, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _utcNow();
        var subscriptions = _loadSubscriptions()
            .Where(subscription => ShouldRefresh(subscription, now, startup, RetryTime(subscription)))
            .ToArray();

        var succeeded = 0;
        var failed = 0;
        var removed = 0;

        foreach (var subscription in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var refreshed = await _coordinator.RefreshAsync(subscription, cancellationToken).ConfigureAwait(false);
                _retryAfter.TryRemove(RefreshKey(subscription), out _);
                NotifyCompleted(new NoraSubscriptionRefreshResult(subscription, refreshed, null));
                succeeded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (NoraSubscriptionRemovedException)
            {
                _retryAfter.TryRemove(RefreshKey(subscription), out _);
                removed++;
            }
            catch (Exception ex)
            {
                _retryAfter[RefreshKey(subscription)] = _utcNow() + FailedRefreshRetryDelay;
                NotifyCompleted(new NoraSubscriptionRefreshResult(subscription, null, ex));
                failed++;
            }
        }

        NotifyPassCompleted(new NoraSubscriptionRefreshPassResult(startup, subscriptions.Length, succeeded, failed, removed));
    }

    internal static bool ShouldRefresh(
        NoraSubscriptionInfo subscription,
        DateTimeOffset now,
        bool startup,
        DateTimeOffset? retryAfter = null)
    {
        if (!HasRefreshSource(subscription))
            return false;
        // A failed startup refresh remains due even when the provider has no
        // periodic interval. Once its backoff expires, retry that failed work.
        if (retryAfter is { } retry)
            return retry <= now;
        if (startup)
            return true;
        if (subscription.UpdateIntervalHours <= 0)
            return false;
        if (subscription.UpdatedAt == default)
            return true;
        try
        {
            return subscription.UpdatedAt.AddHours(subscription.UpdateIntervalHours) <= now;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunPassSafelyAsync(startup: true, cancellationToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(_scanInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RunPassSafelyAsync(startup: false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunPassSafelyAsync(bool startup, CancellationToken cancellationToken)
    {
        startup |= _startupScanPending;
        try
        {
            await RunPassAsync(startup, cancellationToken).ConfigureAwait(false);
            _startupScanPending = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // If startup could not even enumerate the store, keep its launch
            // refresh pending; interval-zero subscriptions still need a turn.
            _startupScanPending |= startup;
            NotifyCompleted(new NoraSubscriptionRefreshResult(
                new NoraSubscriptionInfo { Title = "Subscription scheduler" },
                null,
                ex));
            NotifyPassCompleted(new NoraSubscriptionRefreshPassResult(startup, 0, 0, 1, 0));
        }
    }

    private void NotifyCompleted(NoraSubscriptionRefreshResult result)
    {
        // Observers run after persistence. Their failures cannot undo a commit
        // or manufacture a provider failure/backoff for a successful refresh.
        try { _completed(result); }
        catch { }
    }

    private void NotifyPassCompleted(NoraSubscriptionRefreshPassResult result)
    {
        try { _passCompleted?.Invoke(result); }
        catch { }
    }

    private DateTimeOffset? RetryTime(NoraSubscriptionInfo subscription)
        => _retryAfter.TryGetValue(RefreshKey(subscription), out var retry) ? retry : null;

    private static bool HasRefreshSource(NoraSubscriptionInfo subscription)
    {
        var source = subscription.SourceUrl?.Trim() ?? "";
        if (HappCrypt5Decoder.IsCrypt5Link(source))
            return true;
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    }

    private static string RefreshKey(NoraSubscriptionInfo subscription)
        => !string.IsNullOrWhiteSpace(subscription.Id)
            ? subscription.Id.Trim()
            : subscription.SourceUrl?.Trim() ?? "";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _cancellation.Cancel();
        if (_worker is null)
        {
            _cancellation.Dispose();
            return;
        }
        _ = _worker.ContinueWith(
            _ => _cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static int RunSelfTest(TextWriter output)
    {
        var now = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var fresh = TestSubscription("fresh", now.AddHours(-1), 6);
        var due = TestSubscription("due", now.AddHours(-7), 6);
        var launchOnly = TestSubscription("launch", now.AddDays(-2), 0);
        var localOnly = TestSubscription("local", now.AddDays(-2), 1);
        localOnly.SourceUrl = "vless://local-profile";

        var policyPassed =
            !ShouldRefresh(fresh, now, startup: false) &&
            ShouldRefresh(due, now, startup: false) &&
            !ShouldRefresh(launchOnly, now, startup: false) &&
            ShouldRefresh(fresh, now, startup: true) &&
            ShouldRefresh(launchOnly, now, startup: true) &&
            !ShouldRefresh(localOnly, now, startup: true) &&
            !ShouldRefresh(due, now, startup: false, retryAfter: now.AddMinutes(1)) &&
            ShouldRefresh(launchOnly, now, startup: false, retryAfter: now) &&
            !ShouldRefresh(TestSubscription("overflow", DateTimeOffset.MaxValue, int.MaxValue), now, startup: false);

        var calls = 0;
        var active = 0;
        var maximumActive = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new NoraSubscriptionRefreshCoordinator(async (subscription, cancellationToken) =>
        {
            Interlocked.Increment(ref calls);
            var current = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, current);
            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Decrement(ref active);
            return subscription;
        });

        var duplicateOne = coordinator.RefreshAsync(due);
        var duplicateTwo = coordinator.RefreshAsync(due);
        var other = coordinator.RefreshAsync(fresh);
        var coalesced = ReferenceEquals(duplicateOne, duplicateTwo);
        var firstStarted = SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, TimeSpan.FromSeconds(1));
        release.TrySetResult();
        Task.WhenAll(duplicateOne, duplicateTwo, other).GetAwaiter().GetResult();
        var coordinatorPassed = coalesced && firstStarted && calls == 2 && maximumActive == 1;

        var sharedRelease = new TaskCompletionSource<NoraSubscriptionInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sharedCalls = 0;
        var cancellationCoordinator = new NoraSubscriptionRefreshCoordinator((_, _) =>
        {
            sharedCalls++;
            return sharedRelease.Task;
        });
        var owner = cancellationCoordinator.RefreshAsync(due);
        using var waiterCancellation = new CancellationTokenSource();
        var waiter = cancellationCoordinator.RefreshAsync(due, waiterCancellation.Token);
        waiterCancellation.Cancel();
        var waiterCanceled = false;
        try { waiter.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { waiterCanceled = true; }
        var ownerPreserved = !owner.IsCompleted;
        sharedRelease.TrySetResult(due);
        var waiterCancellationPassed = waiterCanceled && ownerPreserved &&
                                      owner.GetAwaiter().GetResult() == due && sharedCalls == 1;

        var refreshedIds = new List<string>();
        var passResults = new List<NoraSubscriptionRefreshPassResult>();
        var deterministicCoordinator = new NoraSubscriptionRefreshCoordinator((subscription, _) =>
        {
            refreshedIds.Add(subscription.Id);
            return Task.FromResult(subscription);
        });
        using var scheduler = new NoraSubscriptionRefreshScheduler(
            () => [fresh, due, launchOnly, localOnly],
            deterministicCoordinator,
            _ => { },
            () => now,
            passCompleted: passResults.Add);
        scheduler.RunPassAsync(startup: false).GetAwaiter().GetResult();
        var duePassPassed = refreshedIds.SequenceEqual(["due"], StringComparer.Ordinal) &&
                            passResults.Count == 1 &&
                            passResults[0] == new NoraSubscriptionRefreshPassResult(false, 1, 1, 0, 0);
        refreshedIds.Clear();
        scheduler.RunPassAsync(startup: true).GetAwaiter().GetResult();
        var startupPassPassed = refreshedIds.SequenceEqual(["fresh", "due", "launch"], StringComparer.Ordinal) &&
                                passResults.Count == 2 &&
                                passResults[1] == new NoraSubscriptionRefreshPassResult(true, 3, 3, 0, 0);

        var retryNow = now;
        var retryCalls = 0;
        var retryResults = new List<NoraSubscriptionRefreshResult>();
        var failingCoordinator = new NoraSubscriptionRefreshCoordinator((_, _) =>
        {
            retryCalls++;
            return Task.FromException<NoraSubscriptionInfo>(new IOException("fixture refresh failure"));
        });
        using var retryScheduler = new NoraSubscriptionRefreshScheduler(
            () => [due],
            failingCoordinator,
            retryResults.Add,
            () => retryNow);
        retryScheduler.RunPassAsync(startup: false).GetAwaiter().GetResult();
        retryNow = retryNow.AddMinutes(4);
        retryScheduler.RunPassAsync(startup: false).GetAwaiter().GetResult();
        retryNow = retryNow.AddMinutes(1);
        retryScheduler.RunPassAsync(startup: false).GetAwaiter().GetResult();
        var retryPassed = retryCalls == 2 && retryResults.Count == 2 && retryResults.All(result => result.Error is IOException);

        var launchRetryNow = now;
        var launchRetryCalls = 0;
        var launchRetryResults = new List<NoraSubscriptionRefreshResult>();
        var launchRetryCoordinator = new NoraSubscriptionRefreshCoordinator((subscription, _) =>
            ++launchRetryCalls == 1
                ? Task.FromException<NoraSubscriptionInfo>(new IOException("fixture startup failure"))
                : Task.FromResult(subscription));
        using var launchRetryScheduler = new NoraSubscriptionRefreshScheduler(
            () => [launchOnly], launchRetryCoordinator, launchRetryResults.Add, () => launchRetryNow);
        launchRetryScheduler.RunPassAsync(startup: true).GetAwaiter().GetResult();
        launchRetryNow = launchRetryNow.AddMinutes(4);
        launchRetryScheduler.RunPassAsync(startup: false).GetAwaiter().GetResult();
        var launchBackoffPassed = launchRetryCalls == 1;
        launchRetryNow = launchRetryNow.AddMinutes(1);
        launchRetryScheduler.RunPassAsync(startup: false).GetAwaiter().GetResult();
        launchRetryNow = launchRetryNow.AddMinutes(5);
        launchRetryScheduler.RunPassAsync(startup: false).GetAwaiter().GetResult();
        var launchRetryPassed = launchBackoffPassed && launchRetryCalls == 2 && launchRetryResults.Count == 2 &&
                                launchRetryResults[0].Error is IOException && launchRetryResults[1].Refreshed == launchOnly;

        var schedulerFailures = new List<NoraSubscriptionRefreshResult>();
        var schedulerFailurePasses = new List<NoraSubscriptionRefreshPassResult>();
        var storeReadable = false;
        var recoveredLaunchCalls = 0;
        var recoveredLaunchCoordinator = new NoraSubscriptionRefreshCoordinator((subscription, _) =>
        {
            recoveredLaunchCalls++;
            return Task.FromResult(subscription);
        });
        using var resilientScheduler = new NoraSubscriptionRefreshScheduler(
            () => storeReadable ? [launchOnly] : throw new IOException("fixture store read failure"),
            recoveredLaunchCoordinator,
            schedulerFailures.Add,
            () => now,
            passCompleted: schedulerFailurePasses.Add);
        resilientScheduler.RunPassSafelyAsync(startup: true, CancellationToken.None).GetAwaiter().GetResult();
        var resilientPassed = schedulerFailures.Count == 1 && schedulerFailures[0].Error is IOException &&
                              schedulerFailurePasses.Count == 1 && schedulerFailurePasses[0].Failed == 1;
        storeReadable = true;
        resilientScheduler.RunPassSafelyAsync(startup: false, CancellationToken.None).GetAwaiter().GetResult();
        resilientPassed &= recoveredLaunchCalls == 1 && schedulerFailures.Count == 2 && schedulerFailures[1].Refreshed == launchOnly &&
                           schedulerFailurePasses.Count == 2 && schedulerFailurePasses[1].Startup && schedulerFailurePasses[1].Succeeded == 1;

        var observerCalls = 0;
        var observerPasses = new List<NoraSubscriptionRefreshPassResult>();
        var observerCoordinator = new NoraSubscriptionRefreshCoordinator((subscription, _) =>
        {
            observerCalls++;
            return Task.FromResult(subscription);
        });
        using var observerScheduler = new NoraSubscriptionRefreshScheduler(
            () => [launchOnly], observerCoordinator,
            _ => throw new InvalidOperationException("fixture observer failure"),
            () => now, passCompleted: observerPasses.Add);
        observerScheduler.RunPassSafelyAsync(startup: true, CancellationToken.None).GetAwaiter().GetResult();
        observerScheduler.RunPassSafelyAsync(startup: false, CancellationToken.None).GetAwaiter().GetResult();
        var observerPassed = observerCalls == 1 && observerPasses.Count == 2 &&
                             observerPasses[0] == new NoraSubscriptionRefreshPassResult(true, 1, 1, 0, 0) &&
                             observerPasses[1] == new NoraSubscriptionRefreshPassResult(false, 0, 0, 0, 0);

        var removedCalls = 0;
        var removedResults = new List<NoraSubscriptionRefreshResult>();
        var removedCoordinator = new NoraSubscriptionRefreshCoordinator((subscription, _) =>
        {
            removedCalls++;
            return Task.FromException<NoraSubscriptionInfo>(new NoraSubscriptionRemovedException(subscription.Id));
        });
        using var removedScheduler = new NoraSubscriptionRefreshScheduler(
            () => [due],
            removedCoordinator,
            removedResults.Add,
            () => now);
        removedScheduler.RunPassAsync(startup: false).GetAwaiter().GetResult();
        removedScheduler.RunPassAsync(startup: false).GetAwaiter().GetResult();
        var removedPassed = removedCalls == 2 && removedResults.Count == 0;

        var passed = policyPassed && coordinatorPassed && duePassPassed && startupPassPassed &&
                     waiterCancellationPassed && retryPassed && launchRetryPassed && resilientPassed && observerPassed && removedPassed;
        output.WriteLine(
            $"SUBSCRIPTION REFRESH SCHEDULER {(passed ? "PASS" : "FAIL")}: " +
            $"policy={policyPassed}; coalesced={coalesced}; serialized={maximumActive == 1}; " +
            $"waiter_cancellation={waiterCancellationPassed}; due_pass={duePassPassed}; startup_pass={startupPassPassed}; retry={retryPassed}; " +
            $"launch_retry={launchRetryPassed}; resilient={resilientPassed}; observer={observerPassed}; removed={removedPassed}; requests={calls}");
        return passed ? 0 : 1;
    }

    private static NoraSubscriptionInfo TestSubscription(string id, DateTimeOffset updatedAt, int hours)
        => new()
        {
            Id = id,
            Title = id,
            SourceUrl = $"https://subscriptions.example/{id}",
            UpdatedAt = updatedAt,
            UpdateIntervalHours = hours
        };
}
#endif
