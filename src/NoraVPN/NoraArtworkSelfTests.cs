#if WINDOWS
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

namespace Nvp;

internal sealed partial class NoraWpfWindow
{
    private static bool VerifyAdditionalLocationArtwork(string root, TextWriter output)
    {
        var mapping = new (string Name, string Country, string Key, string Label)[]
        {
            ("NORA Bermuda", "United States", "BM", NoraDisplayRegion.BermudaLabel),
            ("NORA Bermuda1", "United States", "BM", NoraDisplayRegion.BermudaLabel),
            ("Bermudas", "Unknown", "BM", NoraDisplayRegion.BermudaLabel),
            ("Unknown location", "BMU", "BM", NoraDisplayRegion.BermudaLabel),
            ("NORA Бермуды 1", "Somalia", "BM", NoraDisplayRegion.BermudaLabel),
            ("Бермудские острова", "Unknown", "BM", NoraDisplayRegion.BermudaLabel),
            ("Бермудский треугольник", "Unknown", "BM", NoraDisplayRegion.BermudaLabel),
            ("Unknown location", "BM", "BM", NoraDisplayRegion.BermudaLabel),
            ("Vatican City", "Italy", "VA", NoraDisplayRegion.VaticanLabel),
            ("Ватикан", "Unknown", "VA", NoraDisplayRegion.VaticanLabel),
            ("Holy See", "Unknown", "VA", NoraDisplayRegion.VaticanLabel),
            ("Святой престол", "Unknown", "VA", NoraDisplayRegion.VaticanLabel),
            ("Unknown location", "VAT", "VA", NoraDisplayRegion.VaticanLabel),
            ("Unknown location", "VA", "VA", NoraDisplayRegion.VaticanLabel)
        };
        var mappings = mapping.All(item => ResolveLocationArtworkKey(item.Name, item.Country) == item.Key &&
            NoraDisplayRegion.Country(item.Name, item.Country) == item.Label &&
            FlagIcon.ResolveCountryCode(NoraDisplayRegion.Country(item.Name, item.Country)) == item.Key);
        var narrow = new[] { "Bermudaish", "NewBermuda", "Бермудский сервер", "Бермудыville", "Triangle", "🔺", "🇺🇸", "🇧🇲" }
            .All(name => !NoraDisplayRegion.HasBermudaName(name) &&
                NoraDisplayRegion.Country(name, "United States") == "United States");
        var preserved = NoraDisplayRegion.Country("𖣐 NORA 🇸🇴 Антарктида 1", "Somalia") == NoraDisplayRegion.AntarcticaLabel &&
            NoraDisplayRegion.Country("🇸🇴 Somalia", "Somalia") == "Somalia" &&
            NoraDisplayRegion.Name("🇺🇸 Bermuda", "United States") == "🇺🇸 Bermuda" &&
            ServerDisplayName("🇺🇸 Bermuda", "United States") == "Bermuda" &&
            ServerDisplayName("🇷🇺 RU → 🇺🇸 Bermuda", "United States") == "🇷🇺 RU → Bermuda";
        var series = new[] { "BM", "VA" }.All(key =>
            FindLocationPaths(root, key).Select(Path.GetFileNameWithoutExtension).SequenceEqual(new[] { key + "1", key + "2", key + "3" }) &&
            LoadLocationImages(key, true).Count == 3 && LoadLocationImages(key, false).Count == 1 &&
            LoadBitmap(Path.Combine(AppContext.BaseDirectory, "assets", "flags", key.ToLowerInvariant() + ".png"))
                is BitmapSource { PixelWidth: > 0, PixelHeight: > 0 });
        var passed = mappings && narrow && preserved && series;
        output.WriteLine(passed
            ? "ADDITIONAL ARTWORK PASS: narrow BM/VA presentation; official bundled flags; ordered three-frame series; AQ/raw-country fallback preserved"
            : $"ADDITIONAL ARTWORK FAIL: mappings={mappings}; narrow={narrow}; preserved={preserved}; series={series}");
        return passed;
    }

    // This entry point exercises only the artwork helpers on a plain offscreen
    // Window. It must never instantiate NoraWpfWindow or touch the VPN lifecycle.
    internal static int RunLocationMotionSelfTest(TextWriter output)
    {
        var previousInterval = Environment.GetEnvironmentVariable("NORA_LOCATION_SLIDESHOW_TEST_MS");
        var previousReduced = Environment.GetEnvironmentVariable("NORA_REDUCED_MOTION");
        var proofDirectory = Environment.GetEnvironmentVariable("NORA_ARTWORK_PROOF_DIRECTORY");
        Application? ownedApplication = null;
        Window? window = null;
        var total = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        var initialWorkingSet = process.WorkingSet64;
        try
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("Artwork runtime self-test requires an STA thread.");
            if (Application.Current is null)
                ownedApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Environment.SetEnvironmentVariable("NORA_LOCATION_SLIDESHOW_TEST_MS", "700");
            Environment.SetEnvironmentVariable("NORA_REDUCED_MOTION", "0");
            if (!string.IsNullOrWhiteSpace(proofDirectory))
            {
                proofDirectory = System.IO.Path.GetFullPath(proofDirectory);
                Directory.CreateDirectory(proofDirectory);
            }
            var decode = Stopwatch.StartNew();
            var all = LoadLocationImages("AQ", true);
            var first = LoadLocationImages("AQ", false);
            ArtworkRequire(all.Count == 5 && first.Count == 1, "AQ has all five frames and one first-frame image");
            // Force actual decode of every source at the largest tested DPI.
            foreach (var source in all)
            {
                var probe = new Grid { Width = 600, Height = 198 };
                AddPhotoSlideshow(probe, [source], new ScaleTransform(1, 1), new CornerRadius(21));
                _ = CaptureArtwork(probe, 200, null, "decode");
            }
            decode.Stop();
            output.WriteLine($"ARTWORK INFO: load/decode five AQ frames={decode.Elapsed.TotalMilliseconds:F1}ms; initial working set={initialWorkingSet / 1048576d:F1}MiB");

            window = new Window
            {
                Width = 600, Height = 198, Left = -10000, Top = -10000,
                WindowStartupLocation = WindowStartupLocation.Manual,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false,
                Background = System.Windows.Media.Brushes.Black
            };
            var animated = NewArtworkHost(all);
            window.Content = animated;
            window.Show();
            window.UpdateLayout();
            PumpArtworkDispatcher(60);

            var hashes = new List<byte[]>();
            var settled = new HashSet<int>();
            var sawFade = false;
            var nextSecond = 0;
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 6 || nextSecond < 4)
            {
                animated.UpdateLayout();
                // Render each sampling pass so offscreen animation clocks still
                // produce observable pixels, independently of pointer activity.
                var sample = CaptureArtwork(animated, 100, null, "sample");
                var layers = animated.Children.OfType<Border>().ToArray();
                sawFade |= layers.Any(layer => layer.Opacity > 0.01 && layer.Opacity < 0.99);
                if (nextSecond < 4 && clock.Elapsed.TotalSeconds >= nextSecond)
                {
                    hashes.Add(sample);
                    foreach (var dpi in new[] { 100, 150, 200 })
                        CaptureArtwork(animated, dpi, proofDirectory, $"motion-t{nextSecond}s");
                    output.WriteLine($"ARTWORK FRAME: t={nextSecond}s captured at {clock.Elapsed.TotalSeconds:F2}s");
                    nextSecond++;
                }
                var visible = layers.SingleOrDefault(layer => layer.Opacity >= 0.999);
                if (visible?.Background is ImageBrush brush &&
                    layers.Where(layer => !ReferenceEquals(layer, visible)).All(layer => layer.Opacity <= 0.001))
                {
                    for (var index = 0; index < all.Count; index++)
                    {
                        if (!ReferenceEquals(brush.ImageSource, all[index]) || !settled.Add(index)) continue;
                        foreach (var dpi in new[] { 100, 150, 200 })
                            CaptureArtwork(animated, dpi, proofDirectory, $"aq-frame-{index + 1}");
                    }
                }
                if (nextSecond == 4 && settled.Count == 5 && sawFade) break;
                if (clock.Elapsed.TotalSeconds > 10) break;
                PumpArtworkDispatcher(35);
            }
            ArtworkRequire(hashes.Count == 4 && hashes.Zip(hashes.Skip(1)).All(pair => !pair.First.SequenceEqual(pair.Second)),
                "runtime pixels differ between t0, t1, t2 and t3 without pointer input");
            ArtworkRequire(settled.Count == 5 && sawFade, "all five frames settle and intermediate crossfade opacity is observable");
            output.WriteLine("ARTWORK MOTION PASS: distinct t0/t1/t2/t3 frames; all five frames; live crossfade; DPI100/150/200 rendering");

            VerifyAdditionalArtworkMotion(window, output, proofDirectory);

            var staticHost = NewArtworkHost(first);
            window.Content = staticHost;
            window.UpdateLayout();
            PumpArtworkDispatcher(60);
            var staticBefore = CaptureArtwork(staticHost, 100, proofDirectory, "first-frame-start");
            PumpArtworkDispatcher(1600);
            var staticAfter = CaptureArtwork(staticHost, 100, proofDirectory, "first-frame-end");
            ArtworkRequire(staticBefore.SequenceEqual(staticAfter), "first-frame mode remains unchanged across multiple slide intervals");
            foreach (var dpi in new[] { 150, 200 }) CaptureArtwork(staticHost, dpi, proofDirectory, "first-frame");
            output.WriteLine("ARTWORK FIRST-FRAME PASS: no slideshow or pixel change");

            Environment.SetEnvironmentVariable("NORA_REDUCED_MOTION", "1");
            var reduced = NewArtworkHost(all);
            window.Content = reduced;
            window.UpdateLayout();
            PumpArtworkDispatcher(60);
            var reducedBefore = CaptureArtwork(reduced, 100, proofDirectory, "reduced-start");
            var sawReducedFade = false;
            clock.Restart();
            while (clock.Elapsed.TotalMilliseconds < 1100)
            {
                PumpArtworkDispatcher(35);
                _ = CaptureArtwork(reduced, 100, null, "reduced-sample");
                sawReducedFade |= reduced.Children.OfType<Border>().Any(layer => layer.Opacity > 0 && layer.Opacity < 1);
            }
            var reducedAfter = CaptureArtwork(reduced, 100, proofDirectory, "reduced-end");
            ArtworkRequire(!sawReducedFade && !reducedBefore.SequenceEqual(reducedAfter),
                "reduced motion preserves existing instant slide changes without crossfade");
            foreach (var dpi in new[] { 150, 200 }) CaptureArtwork(reduced, dpi, proofDirectory, "reduced");
            output.WriteLine("ARTWORK REDUCED-MOTION PASS: slides still change, with no animated opacity (existing behavior)");
            process.Refresh();
            output.WriteLine($"ARTWORK INFO: elapsed={total.Elapsed.TotalSeconds:F2}s; working set={process.WorkingSet64 / 1048576d:F1}MiB; delta={(process.WorkingSet64 - initialWorkingSet) / 1048576d:F1}MiB; peak={process.PeakWorkingSet64 / 1048576d:F1}MiB; informational only");
            output.WriteLine("LOCATION MOTION SELF-TEST PASS");
            return 0;
        }
        catch (Exception exception)
        {
            output.WriteLine("LOCATION MOTION SELF-TEST FAIL: " + exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
        finally
        {
            window?.Close();
            ownedApplication?.Shutdown();
            Environment.SetEnvironmentVariable("NORA_LOCATION_SLIDESHOW_TEST_MS", previousInterval);
            Environment.SetEnvironmentVariable("NORA_REDUCED_MOTION", previousReduced);
        }
    }

    private static void VerifyAdditionalArtworkMotion(Window window, TextWriter output, string? proofDirectory)
    {
        foreach (var key in new[] { "BM", "VA" })
        {
            var images = LoadLocationImages(key, true);
            ArtworkRequire(images.Count == 3, key + " has three decoded frames");
            var host = NewArtworkHost(images);
            window.Content = host;
            window.UpdateLayout();
            PumpArtworkDispatcher(60);
            var hashes = new List<byte[]>();
            var settled = new HashSet<int>();
            var sawFade = false;
            var nextSecond = 0;
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 8)
            {
                var sample = CaptureArtwork(host, 100, null, key + "-sample");
                var layers = host.Children.OfType<Border>().ToArray();
                sawFade |= layers.Any(layer => layer.Opacity > 0.01 && layer.Opacity < 0.99);
                if (nextSecond < 4 && clock.Elapsed.TotalSeconds >= nextSecond)
                {
                    hashes.Add(sample);
                    foreach (var dpi in new[] { 100, 150, 200 })
                        CaptureArtwork(host, dpi, proofDirectory, $"{key.ToLowerInvariant()}-motion-t{nextSecond}s");
                    nextSecond++;
                }
                var visible = layers.SingleOrDefault(layer => layer.Opacity >= 0.999);
                if (visible?.Background is ImageBrush brush &&
                    layers.Where(layer => !ReferenceEquals(layer, visible)).All(layer => layer.Opacity <= 0.001))
                {
                    for (var index = 0; index < images.Count; index++)
                    {
                        if (!ReferenceEquals(brush.ImageSource, images[index]) || !settled.Add(index)) continue;
                        foreach (var dpi in new[] { 100, 150, 200 })
                            CaptureArtwork(host, dpi, proofDirectory, $"{key.ToLowerInvariant()}-frame-{index + 1}");
                    }
                }
                if (nextSecond == 4 && settled.Count == 3 && sawFade) break;
                PumpArtworkDispatcher(35);
            }
            ArtworkRequire(hashes.Count == 4 && hashes.Zip(hashes.Skip(1)).All(pair => !pair.First.SequenceEqual(pair.Second)),
                key + " runtime pixels differ at t0/t1/t2/t3 without pointer input");
            ArtworkRequire(settled.Count == 3 && sawFade, key + " settles every frame and shows a live crossfade");

            var first = LoadLocationImages(key, false);
            ArtworkRequire(first.Count == 1, key + " has one first-frame image");
            var firstHost = NewArtworkHost(first);
            window.Content = firstHost;
            window.UpdateLayout();
            PumpArtworkDispatcher(60);
            var before = CaptureArtwork(firstHost, 100, proofDirectory, key.ToLowerInvariant() + "-first-start");
            PumpArtworkDispatcher(1600);
            var after = CaptureArtwork(firstHost, 100, proofDirectory, key.ToLowerInvariant() + "-first-end");
            ArtworkRequire(before.SequenceEqual(after), key + " default first-frame mode stays static");
            output.WriteLine($"ARTWORK {key} MOTION PASS: distinct t0/t1/t2/t3; ordered three-frame series; live crossfade; DPI100/150/200; static first-frame mode");
        }
    }

    private static Grid NewArtworkHost(IReadOnlyList<ImageSource> images)
    {
        var host = new Grid { Width = 600, Height = 198, ClipToBounds = true };
        AddPhotoSlideshow(host, images, new ScaleTransform(1, 1, 0.5, 0.5), new CornerRadius(21));
        return host;
    }

    private static byte[] CaptureArtwork(Grid host, int dpiPercent, string? directory, string label)
    {
        host.Measure(new Size(600, 198));
        host.Arrange(new Rect(0, 0, 600, 198));
        host.UpdateLayout();
        var scale = dpiPercent / 100d;
        var width = (int)Math.Ceiling(600 * scale);
        var height = (int)Math.Ceiling(198 * scale);
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        ArtworkRequire(pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0), "rendered frame is not empty");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            using var stream = File.Create(System.IO.Path.Combine(directory, $"{label}-dpi{dpiPercent}.png"));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }
        return SHA256.HashData(pixels);
    }

    private static void PumpArtworkDispatcher(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    private static void ArtworkRequire(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
