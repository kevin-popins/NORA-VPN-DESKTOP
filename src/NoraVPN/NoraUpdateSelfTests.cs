#if WINDOWS
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Nvp;

internal static partial class NoraUpdateService
{
    private static async Task<bool> RunRetrySelfTestsAsync(string releaseJson, TextWriter output)
    {
        var delays = new List<TimeSpan>();
        Task RecordDelay(TimeSpan delay, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            delays.Add(delay);
            return Task.CompletedTask;
        }
        HttpResponseMessage Release() => new(HttpStatusCode.OK)
        {
            Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
        };
        void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Update retry: " + label);
            output.WriteLine("UPDATE RETRY PASS: " + label);
        }

        // The first HTTP attempt never supplies headers; its own budget must
        // expire, then the same startup check must return the recovered release.
        using (var handler = new UpdateTestHandler(async (attempt, token) =>
        {
            if (attempt == 1) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Release();
        }))
        using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
        {
            var result = await CheckWithRetryAsync(
                token => CheckOnceAsync(client, "0.1.0-pre.5", TimeSpan.FromMilliseconds(30), token),
                RecordDelay, CancellationToken.None);
            Require(result?.Version == "0.1.0-pre.6" && handler.Attempts == 2 &&
                handler.MaximumConcurrent == 1 && delays.SequenceEqual([TimeSpan.FromSeconds(30)]),
                "header timeout recovers once without overlapping requests");
        }

        delays.Clear();
        using (var handler = new UpdateTestHandler((attempt, _) => Task.FromResult(attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledUpdateStream()) }
            : Release())))
        using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
        {
            var result = await CheckWithRetryAsync(
                token => CheckOnceAsync(client, "0.1.0-pre.5", TimeSpan.FromMilliseconds(30), token),
                RecordDelay, CancellationToken.None);
            Require(result is not null && handler.Attempts == 2 && delays.Count == 1,
                "stalled JSON body also times out and recovers");
        }

        delays.Clear();
        var attempts = 0;
        var noUpdate = await CheckWithRetryAsync(_ =>
        {
            attempts++;
            return attempts <= 8
                ? Task.FromException<NoraUpdateInfo?>(new HttpRequestException("Synthetic offline failure"))
                : Task.FromResult<NoraUpdateInfo?>(null);
        }, RecordDelay, CancellationToken.None);
        Require(noUpdate is null && attempts == 9 && delays.SequenceEqual(
            new[] { 30, 120, 600, 1800, 3600, 3600, 3600, 3600 }.Select(seconds => TimeSpan.FromSeconds(seconds))),
            "offline retries back off to hourly; successful no-update response stops");

        delays.Clear();
        using (var handler = new UpdateTestHandler((attempt, _) =>
        {
            var response = attempt == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Release();
            if (attempt == 1) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(90));
            return Task.FromResult(response);
        }))
        using (var client = new HttpClient(handler))
        {
            var result = await CheckWithRetryAsync(
                token => CheckOnceAsync(client, "0.1.0-pre.5", RequestTimeout, token), RecordDelay, CancellationToken.None);
            Require(result is not null && delays.SequenceEqual([TimeSpan.FromMinutes(90)]),
                "GitHub Retry-After is honored");
        }

        delays.Clear();
        var permanentStopped = false;
        try
        {
            await CheckWithRetryAsync(_ => Task.FromException<NoraUpdateInfo?>(
                new HttpRequestException("Synthetic missing endpoint", null, HttpStatusCode.NotFound)),
                RecordDelay, CancellationToken.None);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            permanentStopped = true;
        }
        Require(permanentStopped && delays.Count == 0, "permanent HTTP errors are not retried");

        using (var cancellation = new CancellationTokenSource())
        {
            attempts = 0;
            var cancelled = false;
            try
            {
                await CheckWithRetryAsync(_ =>
                {
                    attempts++;
                    return Task.FromException<NoraUpdateInfo?>(new TaskCanceledException("Synthetic request timeout"));
                }, (_, token) =>
                {
                    cancellation.Cancel();
                    return Task.Delay(Timeout.InfiniteTimeSpan, token);
                }, cancellation.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled && attempts == 1, "closing during backoff prevents further requests");
        }

        using (var cancellation = new CancellationTokenSource())
        using (var handler = new UpdateTestHandler((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Release());
        }))
        using (var client = new HttpClient(handler))
        {
            delays.Clear();
            var cancelled = false;
            try
            {
                await CheckWithRetryAsync(
                    token => CheckOnceAsync(client, "0.1.0-pre.5", RequestTimeout, token),
                    RecordDelay, cancellation.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled && handler.Attempts == 1 && delays.Count == 0,
                "closing during HTTP request cancels without retry");
        }
        return true;
    }

    private sealed class UpdateTestHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        private int _active;
        public int Attempts { get; private set; }
        public int MaximumConcurrent { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Attempts++;
            MaximumConcurrent = Math.Max(MaximumConcurrent, ++_active);
            try { return await send(Attempts, token).ConfigureAwait(false); }
            finally { _active--; }
        }
    }

    private sealed class StalledUpdateStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }
}
#endif