#if WINDOWS
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Nvp;

// Windows system-output spectrum analyzer. Audio is never persisted or sent
// anywhere; consumers receive only a short-lived normalized float array.
internal sealed class NoraSpectrumAnalyzer : IDisposable
{
    public const int BandCount = 48;

    private const int FftExponent = 11;
    private const int FftSize = 1 << FftExponent;
    private const double MinimumFrequency = 38.0;
    private const double MaximumFrequency = 16_000.0;
    private readonly object _gate = new();
    private readonly float[] _bands = new float[BandCount];
    private readonly List<CaptureBinding> _captures = [];
    private int _leases;
    private DateTimeOffset _lastSpectrumAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPcmSpectrumAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastReadAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextSessionMeterReadAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextStartAt = DateTimeOffset.MinValue;
    private float _sessionMeterPeak;
    private int _activeMeterSessions;
    private bool _usingSessionMeterFallback;

    public static NoraSpectrumAnalyzer Shared { get; } = new();

    private NoraSpectrumAnalyzer()
    {
    }

    public void Attach()
    {
        lock (_gate)
        {
            _leases++;
            StartIfNeededLocked();
        }
    }

    public void Detach()
    {
        List<CaptureBinding>? captures = null;
        lock (_gate)
        {
            if (_leases > 0)
                _leases--;
            if (_leases != 0 || _captures.Count == 0)
                return;

            captures = [.. _captures];
            _captures.Clear();
        }

        StopAndDispose(captures);
    }

    // The analyzer intentionally exposes spectrum data only as float[].
    public float[] GetSpectrum()
    {
        lock (_gate)
        {
            StartIfNeededLocked();
            var now = DateTimeOffset.UtcNow;
            var elapsed = _lastReadAt == DateTimeOffset.MinValue
                ? 0
                : Math.Clamp((now - _lastReadAt).TotalSeconds, 0, 0.12);
            _lastReadAt = now;

            // WASAPI loopback may stop delivering callbacks during silence. Keep
            // decay deterministic so the visual never snaps from energy to zero.
            if (now - _lastSpectrumAt > TimeSpan.FromMilliseconds(110) && elapsed > 0)
            {
                var decay = (float)(elapsed * 0.72);
                for (var i = 0; i < _bands.Length; i++)
                    _bands[i] = Math.Max(0, _bands[i] - decay);
            }

            RefreshSessionMeterLocked(now, elapsed);
            ApplySessionMeterFallbackLocked(now);
            return [.. _bands];
        }
    }

    public void Dispose()
    {
        List<CaptureBinding> captures;
        lock (_gate)
        {
            _leases = 0;
            captures = [.. _captures];
            _captures.Clear();
            Array.Clear(_bands);
        }

        StopAndDispose(captures);
    }

    internal static int RunCaptureSmoke(TextWriter output)
    {
        var analyzer = Shared;
        analyzer.Attach();
        try
        {
            Thread.Sleep(350);
            var spectrum = analyzer.GetSpectrum();
            int captureCount;
            lock (analyzer._gate)
                captureCount = analyzer._captures.Count;
            output.WriteLine($"AUDIO CAPTURE SMOKE: active render captures={captureCount}; bands={spectrum.Length}; {analyzer.GetDiagnostic()}");
            return captureCount > 0 && spectrum.Length == BandCount ? 0 : 1;
        }
        finally
        {
            analyzer.Detach();
        }
    }

    internal static int RunSessionMeterFallbackSelfTest(TextWriter output)
    {
        var bands = new float[BandCount];
        BlendSessionMeterEnvelope(bands, 0.08f);
        var minimum = bands.Min();
        var maximum = bands.Max();
        output.WriteLine($"AUDIO SESSION-METER SELFTEST: bands={bands.Length}; min={minimum:0.000}; max={maximum:0.000}");
        return bands.Length == BandCount && minimum > 0 && maximum is > 0.025f and < 0.72f ? 0 : 1;
    }

    private void StartIfNeededLocked()
    {
        if (_leases == 0 || _captures.Count > 0 || DateTimeOffset.UtcNow < _nextStartAt)
            return;

        foreach (var device in DiscoverPlaybackDevices())
        {
            CaptureBinding? binding = null;
            try
            {
                var capture = new WasapiLoopbackCapture(device);
                binding = new CaptureBinding(device, capture);
                binding.DataHandler = (_, e) => OnDataAvailable(binding, e);
                binding.StopHandler = (_, e) => OnRecordingStopped(binding, e);
                capture.DataAvailable += binding.DataHandler;
                capture.RecordingStopped += binding.StopHandler;
                _captures.Add(binding);
                capture.StartRecording();
            }
            catch
            {
                if (binding is not null)
                {
                    _captures.Remove(binding);
                    StopAndDispose(binding);
                }
                else
                {
                    device.Dispose();
                }
            }
        }

        if (_captures.Count == 0)
            _nextStartAt = DateTimeOffset.UtcNow.AddSeconds(3);
    }

    // A laptop can send music through a Bluetooth/HDMI/DAC endpoint while its
    // Windows Multimedia default remains the internal speakers. Capture each
    // active render endpoint so the visualizer follows what is actually heard.
    private static IEnumerable<MMDevice> DiscoverPlaybackDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var preferredIds = new List<string>(3);
        foreach (var role in new[] { Role.Multimedia, Role.Console, Role.Communications })
        {
            try
            {
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                if (!preferredIds.Contains(device.ID, StringComparer.OrdinalIgnoreCase))
                    preferredIds.Add(device.ID);
            }
            catch
            {
                // A role may have no default endpoint; active devices below are
                // still valid loopback targets.
            }
        }

        var ordered = enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .OrderBy(device =>
            {
                var index = preferredIds.FindIndex(id => string.Equals(id, device.ID, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(device => device.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // A browser can route a tab to a virtual, HDMI or Bluetooth output
        // that happens to sort after the common defaults. Keep every active
        // endpoint: the individual captures stay isolated below, so one quiet
        // device cannot drown out the one playing media.
        return ordered;
    }

    private void OnRecordingStopped(CaptureBinding binding, StoppedEventArgs e)
    {
        lock (_gate)
        {
            if (!_captures.Remove(binding))
                return;
            if (_captures.Count == 0)
                _nextStartAt = DateTimeOffset.UtcNow.AddSeconds(3);
        }

        StopAndDispose(binding);
    }

    private void OnDataAvailable(CaptureBinding binding, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
            return;

        lock (_gate)
        {
            if (_leases == 0 || !_captures.Contains(binding))
                return;

            binding.CallbackCount++;
            binding.LastCallbackAt = DateTimeOffset.UtcNow;
            var format = binding.Capture.WaveFormat;
            var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
            var frameSize = Math.Max(bytesPerSample, format.BlockAlign);
            var channels = Math.Max(1, format.Channels);
            var end = Math.Min(e.BytesRecorded, e.Buffer.Length);
            for (var offset = 0; offset + frameSize <= end; offset += frameSize)
            {
                double mixed = 0;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sampleOffset = offset + channel * bytesPerSample;
                    if (sampleOffset + bytesPerSample > end)
                        break;
                    mixed += ReadSample(e.Buffer, sampleOffset, format);
                }

                var sample = (float)(mixed / channels);
                binding.LastFramePeak = Math.Max(binding.LastFramePeak * 0.92f, Math.Abs(sample));
                binding.SampleBuffer[binding.SampleCount++] = sample;
                if (binding.SampleCount != FftSize)
                    continue;

                PublishSpectrumLocked(binding, format.SampleRate);
                binding.SampleCount = 0;
            }
        }
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        return format.BitsPerSample switch
        {
            16 => BitConverter.ToInt16(buffer, offset) / 32768f,
            24 => ReadPcm24(buffer, offset) / 8388608f,
            32 when IsIeeeFloat(format) => BitConverter.ToSingle(buffer, offset),
            32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0f
        };
    }

    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");

    private static bool IsIeeeFloat(WaveFormat format) =>
        format.Encoding == WaveFormatEncoding.IeeeFloat ||
        format is WaveFormatExtensible extensible &&
        extensible.SubFormat == IeeeFloatSubFormat;

    private static int ReadPcm24(byte[] buffer, int offset)
    {
        var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        return (value & 0x00800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    // Each playback output owns an FFT history. Mixing callback streams from
    // several WASAPI endpoints into one buffer makes silent devices overwrite
    // the endpoint that is playing music, so the displayed spectrum is the
    // maximum energy of the independent endpoint spectra instead.
    private void PublishSpectrumLocked(CaptureBinding binding, int sampleRate)
    {
        for (var i = 0; i < FftSize; i++)
        {
            binding.FftBuffer[i].X = binding.SampleBuffer[i] * (float)FastFourierTransform.HannWindow(i, FftSize);
            binding.FftBuffer[i].Y = 0;
        }

        FastFourierTransform.FFT(true, FftExponent, binding.FftBuffer);
        Span<float> next = stackalloc float[BandCount];
        Span<double> bandDecibels = stackalloc double[BandCount];
        var nyquist = sampleRate / 2.0;
        var highLimit = Math.Min(MaximumFrequency, nyquist * 0.94);
        var frequencyRange = highLimit / MinimumFrequency;
        var framePeakDecibels = -180.0;
        for (var band = 0; band < BandCount; band++)
        {
            var lowHz = MinimumFrequency * Math.Pow(frequencyRange, (double)band / BandCount);
            var highHz = MinimumFrequency * Math.Pow(frequencyRange, (double)(band + 1) / BandCount);
            var firstBin = Math.Clamp((int)Math.Floor(lowHz * FftSize / sampleRate), 1, FftSize / 2 - 1);
            var lastBin = Math.Clamp((int)Math.Ceiling(highHz * FftSize / sampleRate), firstBin + 1, FftSize / 2);
            var peakMagnitude = 0.0;
            for (var bin = firstBin; bin < lastBin; bin++)
            {
                var real = binding.FftBuffer[bin].X;
                var imaginary = binding.FftBuffer[bin].Y;
                peakMagnitude = Math.Max(peakMagnitude, Math.Sqrt(real * real + imaginary * imaginary));
            }

            var normalizedMagnitude = peakMagnitude / (FftSize / 2.0);
            var decibels = 20 * Math.Log10(normalizedMagnitude + 1e-9);
            bandDecibels[band] = decibels;
            framePeakDecibels = Math.Max(framePeakDecibels, decibels);
        }

        var frameLevel = Math.Clamp((framePeakDecibels + 96) / 50, 0, 1);
        var pcmPeak = 0f;
        for (var band = 0; band < BandCount; band++)
        {
            var relative = Math.Clamp(1 - (framePeakDecibels - bandDecibels[band]) / 48, 0, 1);
            var highFrequencyCompensation = 1 + 0.28 * band / (BandCount - 1.0);
            next[band] = (float)Math.Clamp(
                Math.Pow(relative, 0.76) * Math.Pow(frameLevel, 0.72) * highFrequencyCompensation,
                0,
                1);
            pcmPeak = Math.Max(pcmPeak, next[band]);
        }

        if (_leases == 0)
            return;
        for (var band = 0; band < BandCount; band++)
        {
            var current = binding.Bands[band];
            var target = next[band];
            binding.Bands[band] = target >= current
                ? current + (target - current) * 0.68f
                : Math.Max(target, current - 0.055f);
        }

        for (var band = 0; band < BandCount; band++)
        {
            var target = 0f;
            foreach (var source in _captures)
                target = Math.Max(target, source.Bands[band]);
            var current = _bands[band];
            _bands[band] = target >= current
                ? current + (target - current) * 0.68f
                : Math.Max(target, current - 0.055f);
        }
        var now = DateTimeOffset.UtcNow;
        _lastSpectrumAt = now;
        if (pcmPeak > 0.018f)
        {
            _lastPcmSpectrumAt = now;
            _usingSessionMeterFallback = false;
        }
    }

    // Some Windows audio paths deliberately expose only a session peak meter,
    // not PCM data to a loopback client. The meter contains no recordable
    // audio; it is only a 0..1 playback level. Use it solely when FFT data is
    // unavailable, so normal outputs retain their real spectrum.
    private void RefreshSessionMeterLocked(DateTimeOffset now, double elapsed)
    {
        if (now < _nextSessionMeterReadAt)
        {
            _sessionMeterPeak = Math.Max(0, _sessionMeterPeak - (float)(elapsed * 1.2));
            return;
        }

        _nextSessionMeterReadAt = now.AddMilliseconds(50);
        var peak = 0f;
        var activeSessions = 0;
        foreach (var binding in _captures)
        {
            try
            {
                var sessions = binding.Device.AudioSessionManager.Sessions;
                for (var index = 0; index < sessions.Count; index++)
                {
                    AudioSessionControl? session = null;
                    try
                    {
                        session = sessions[index];
                        if (session.IsSystemSoundsSession)
                            continue;
                        var level = session.AudioMeterInformation.MasterPeakValue;
                        if (level <= 0.001f)
                            continue;
                        activeSessions++;
                        peak = Math.Max(peak, level);
                    }
                    catch
                    {
                        // Sessions can disappear while a browser changes a
                        // track or output route. The next 50 ms poll retries.
                    }
                    finally
                    {
                        session?.Dispose();
                    }
                }
            }
            catch
            {
                // A removed endpoint is handled by RecordingStopped; do not
                // let one transient COM failure stop the other outputs.
            }
        }

        _activeMeterSessions = activeSessions;
        _sessionMeterPeak = Math.Max(peak, _sessionMeterPeak * 0.72f);
    }

    private void ApplySessionMeterFallbackLocked(DateTimeOffset now)
    {
        if (_sessionMeterPeak <= 0.006f || now - _lastPcmSpectrumAt < TimeSpan.FromMilliseconds(180))
            return;

        BlendSessionMeterEnvelope(_bands, _sessionMeterPeak);
        _lastSpectrumAt = now;
        _usingSessionMeterFallback = true;
    }

    // This is intentionally a broad level envelope, not a fabricated FFT:
    // it expresses real playback intensity while leaving frequency analysis
    // to the PCM path whenever Windows makes it available.
    private static void BlendSessionMeterEnvelope(Span<float> bands, float sessionMeterPeak)
    {
        var level = (float)Math.Clamp(Math.Pow(sessionMeterPeak, 0.54) * 0.56, 0, 0.72);
        for (var band = 0; band < BandCount; band++)
        {
            var position = band / (BandCount - 1.0);
            var contour = 0.78 + 0.22 * Math.Cos((position - 0.45) * Math.PI);
            var target = (float)(level * contour);
            bands[band] = Math.Max(bands[band] * 0.78f, target);
        }
    }

    internal string GetDiagnostic()
    {
        lock (_gate)
        {
            StartIfNeededLocked();
            var callbacks = _captures.Sum(capture => capture.CallbackCount);
            var peak = _captures.Count == 0 ? 0 : _captures.Max(capture => capture.LastFramePeak);
            var lastFrame = _captures
                .Where(capture => capture.LastCallbackAt != DateTimeOffset.MinValue)
                .Select(capture => capture.LastCallbackAt)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max();
            var frameAge = lastFrame == DateTimeOffset.MinValue
                ? "no frames"
                : $"{Math.Max(0, (DateTimeOffset.UtcNow - lastFrame).TotalSeconds):0.0}s ago";
            var source = _usingSessionMeterFallback ? "session-meter fallback" : "PCM spectrum";
            return $"playback endpoints={_captures.Count}; callbacks={callbacks}; peak={peak:0.000}; session_peak={_sessionMeterPeak:0.000}; active_sessions={_activeMeterSessions}; source={source}; latest frame={frameAge}";
        }
    }

    private static void StopAndDispose(IEnumerable<CaptureBinding>? captures)
    {
        if (captures is null)
            return;
        foreach (var capture in captures)
            StopAndDispose(capture);
    }

    private static void StopAndDispose(CaptureBinding? binding)
    {
        if (binding is null)
            return;
        try
        {
            binding.Capture.DataAvailable -= binding.DataHandler;
            binding.Capture.RecordingStopped -= binding.StopHandler;
            binding.Capture.StopRecording();
        }
        catch
        {
        }
        finally
        {
            binding.Capture.Dispose();
            binding.Device.Dispose();
        }
    }

    private sealed class CaptureBinding(MMDevice device, WasapiLoopbackCapture capture)
    {
        public MMDevice Device { get; } = device;
        public WasapiLoopbackCapture Capture { get; } = capture;
        public EventHandler<WaveInEventArgs>? DataHandler { get; set; }
        public EventHandler<StoppedEventArgs>? StopHandler { get; set; }
        public float[] SampleBuffer { get; } = new float[FftSize];
        public Complex[] FftBuffer { get; } = new Complex[FftSize];
        public float[] Bands { get; } = new float[BandCount];
        public int SampleCount { get; set; }
        public long CallbackCount { get; set; }
        public float LastFramePeak { get; set; }
        public DateTimeOffset LastCallbackAt { get; set; }
    }
}
#endif
