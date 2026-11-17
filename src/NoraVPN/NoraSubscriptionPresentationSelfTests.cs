#if WINDOWS
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Path = System.IO.Path;
using Size = System.Windows.Size;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Nvp;

// Plain-Window proof of the actual presentation primitives. There is no product
// window, instance registration, profile load, network, audio, tray or VPN here.
internal static class NoraSubscriptionPresentationSelfTests
{
    internal static int Run(TextWriter output, string proofDirectory)
    {
        var directory = Path.GetFullPath(proofDirectory);
        var previousRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
        Application? ownedApp = null;
        Window? window = null;
        var fixtures = new List<NoraSubscriptionInfo>();
        var results = new List<object>();
        var passed = false;
        string? failure = null;
        var protectedSideEffects = 0;
        var ownWindowHwnd = 0L;
        var windowIconPresent = false;
        var canonicalIconSha256 = "";
        NoraBrandingProofResult? nativeBranding = null;
        try
        {
            Require(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "STA entrypoint");
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", Path.Combine(directory, "synthetic-state"));
            if (Application.Current is null) ownedApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            window = new Window
            {
                Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual,
                WindowStyle = WindowStyle.SingleBorderWindow, ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowActivated = false, ShowInTaskbar = false, Background = NoraWpfTheme.BgBrush,
                Icon = NoraWpfWindow.LoadBrandWindowIcon()
            };
            windowIconPresent = window.Icon is not null;
            Require(windowIconPresent, "plain proof Window has the canonical NORA icon");
            var iconPath = new[] { Path.Combine(Directory.GetCurrentDirectory(), "assets", "logo.ico"), Path.Combine(AppContext.BaseDirectory, "assets", "logo.ico") }.FirstOrDefault(File.Exists);
            Require(iconPath is not null, "canonical assets/logo.ico is present");
            canonicalIconSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(iconPath!))).ToLowerInvariant();

            NoraSubscriptionInfo Fixture(string name, NoraSubscriptionMetadataState state, bool empty = false)
            {
                var id = "presentation-" + name + "-" + Guid.NewGuid().ToString("N");
                var subscription = new NoraSubscriptionInfo
                {
                    Id = id, Title = "Synthetic subscription",
                    SourceUrl = "https://provider.example.test/presentation/" + id,
                    AppMetadata = state with
                    {
                        IssuedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), TtlSeconds = 300,
                        HasV1Policy = true, V1Protected = state.Protected
                    }
                };
                if (!empty) subscription.Servers.Add(new NoraSubscriptionServer
                {
                    Id = id + "-node", SubscriptionId = id, Name = "Synthetic node",
                    Host = "private-node.fixture.invalid", Port = 443, Uuid = "fixture-secret-identifier"
                });
                NoraSubscriptionMetadata.RegisterCommitted(subscription);
                fixtures.Add(subscription);
                return subscription;
            }

            var healthy = new NoraSubscriptionMetadataState
            {
                Protected = true, PublicLabel = "Рабочая сеть 🌐", Access = "active", Availability = "available",
                Message = "General provider information. Сервис работает 🌐", MessageIsNull = false
            };
            Require(NoraSubscriptionNoticeView.Create(healthy) is null, "healthy generic message creates no banner");
            var cases = new[]
            {
                ("healthy", healthy, (string?)null),
                ("maintenance", healthy with { Availability = "node_maintenance" }, "Servers under maintenance"),
                ("outage", healthy with { Availability = "outage" }, "VPN temporarily unavailable"),
                ("degraded", healthy with { Availability = "degraded" }, "Some servers are unavailable"),
                ("expired-precedence", healthy with { Access = "expired", Availability = "node_maintenance" }, "Subscription expired"),
                ("quota-precedence", healthy with { Access = "quota_exhausted", Availability = "outage" }, "Traffic allowance used"),
                ("disabled-precedence", healthy with { Access = "disabled", Availability = "node_maintenance" }, "Subscription unavailable"),
                ("pending-precedence", healthy with { Access = "pending", Availability = "outage" }, "Subscription not active yet"),
                ("no-nodes", healthy with { Availability = "no_nodes" }, "No servers available"),
                ("cached-empty", healthy with { Access = "unknown", Availability = "unknown", CachedOnly = true, Message = "" }, "Refresh required"),
                ("long-ru", healthy with { Availability = "node_maintenance", Message = string.Concat(Enumerable.Repeat("Проводятся технические работы 🌐 Серверы временно недоступны; сохранённые настройки останутся на устройстве. ", 3)) }, "Servers under maintenance"),
                ("long-en", healthy with { Availability = "outage", Message = string.Concat(Enumerable.Repeat("Service restoration is in progress 🌐 Saved connection settings remain available on this device. Please check again later. ", 3)) }, "VPN temporarily unavailable")
            };
            foreach (var (name, state, heading) in cases)
            {
                var subscription = Fixture(name, state, name == "cached-empty");
                var projected = NoraSubscriptionMetadata.EffectiveFor(subscription);
                var notice = NoraSubscriptionNoticeView.Create(projected);
                Require((notice is null) == (heading is null), name + " notice visibility");
                if (heading is not null) Require(NoraSubscriptionNoticeView.Heading(projected) == heading, name + " heading precedence");
                if (name == "cached-empty")
                    Require(subscription.Servers.Count == 0 && notice is not null && !Elements(notice).OfType<ButtonBase>().Any(), "cached-only notice creates no fake nodes or connectable actions");
                Render(name, projected, subscription.Servers.FirstOrDefault(), window, directory, results);
            }

            foreach (var (name, label) in new[] { ("protected-empty-label", ""), ("protected-public-label", "Рабочая сеть 🌐 / Public network") })
            {
                var subscription = Fixture(name, healthy with { PublicLabel = label });
                var server = subscription.Servers.Single();
                var expected = label.Length == 0 ? NoraProtectedDisplay.FallbackLabel : label;
                Require(NoraProtectedDisplay.EndpointText(server) == expected, name + " endpoint label");
                foreach (var operation in new[] { "QR", "copy", "file export" })
                    Require(!NoraProtectedDisplay.TryDisclose(server, () => protectedSideEffects++) && protectedSideEffects == 0, operation + " guard precedes side effects");
                try { NoraProtectedDisplay.RequireDisclosure(server); throw new InvalidOperationException("protected disclosure accepted"); }
                catch (NoraAppException) { }
                Render(name, NoraSubscriptionMetadata.EffectiveFor(subscription), server, window, directory, results);
            }
            var ordinary = Fixture("ordinary", healthy with { Protected = false, PublicLabel = "" }).Servers.Single();
            Require(NoraProtectedDisplay.EndpointText(ordinary) == "private-node.fixture.invalid:443", "unprotected endpoint unchanged");
            var allowedEffects = 0;
            Require(NoraProtectedDisplay.TryDisclose(ordinary, () => allowedEffects++) && allowedEffects == 1, "ordinary inert callback remains allowed");
            Render("ordinary", NoraSubscriptionMetadata.EffectiveFor(ordinary), ordinary, window, directory, results);
            ownWindowHwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle.ToInt64();
            Require(ownWindowHwnd != 0, "plain proof Window owns a real HWND");
            nativeBranding = NoraBrandingProof.Capture(window, iconPath!, directory);
            Require(nativeBranding.CanonicalIconSha256 == canonicalIconSha256 && nativeBranding.Passed,
                "own HWND native BIG and SMALL icons match canonical ICO frames");
            passed = true;
            output.WriteLine("SUBSCRIPTION PRESENTATION PASS: actual notices and endpoint labels; healthy banner suppressed; precedence/cached-empty/guard checks; 100/150/200% DPI narrow layout.");
            return 0;
        }
        catch (Exception exception)
        {
            failure = exception.GetType().Name + ": " + exception.Message;
            output.WriteLine("SUBSCRIPTION PRESENTATION FAIL: " + failure);
            return 1;
        }
        finally
        {
            window?.Close();
            foreach (var fixture in fixtures) NoraSubscriptionMetadata.Forget(fixture);
            ownedApp?.Shutdown();
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", previousRoot);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "subscription-presentation.json"), JsonSerializer.Serialize(new
            {
                passed, failure, protectedSideEffects, productionWindowCreated = false,
                clipboardQrOrExportInvoked = false, syntheticOnly = true,
                processId = Environment.ProcessId, ownWindowHwnd, windowIconPresent, canonicalIconSha256,
                brandingScope = "own plain offscreen HWND only", nativeBranding, results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void Render(string name, NoraSubscriptionMetadataState state, NoraSubscriptionServer? server,
        Window window, string directory, List<object> results)
    {
        foreach (var width in new[] { 260d, 340d })
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Synthetic subscription", FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var notice = NoraSubscriptionNoticeView.Create(state);
            if (notice is not null) stack.Children.Add(notice);
            if (server is not null) stack.Children.Add(new TextBlock
            {
                Text = NoraProtectedDisplay.EndpointText(server), FontSize = 12.5,
                Foreground = NoraWpfTheme.MutedBrush, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            var root = new Border
            {
                Width = width, Padding = new Thickness(14), Background = NoraWpfTheme.BgBrush,
                Child = stack
            };
            root.SetValue(TextElement.FontFamilyProperty, NoraWpfTheme.UiFont);
            root.SetValue(TextElement.ForegroundProperty, NoraWpfTheme.TextBrush);
            root.Measure(new Size(width, double.PositiveInfinity));
            var height = Math.Ceiling(root.DesiredSize.Height);
            window.Content = root;
            if (!window.IsVisible) window.Show();
            window.UpdateLayout(); Pump();
            var rectangles = new List<Rect>();
            foreach (var text in Elements(root).OfType<TextBlock>())
            {
                var bounds = text.TransformToAncestor(root).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight));
                Require(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= width + 1 && bounds.Bottom <= height + 1, name + " text stays inside scene");
                var actualHeight = text.ActualHeight;
                text.Measure(new Size(text.ActualWidth + text.Margin.Left + text.Margin.Right, double.PositiveInfinity));
                var naturalTextHeight = Math.Max(0, text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom);
                Require(naturalTextHeight <= actualHeight + 1, name + " text has no vertical clipping");
                Require(rectangles.All(previous => !previous.IntersectsWith(bounds)), name + " text blocks do not overlap");
                rectangles.Add(bounds);
            }
            foreach (var dpi in new[] { 100, 150, 200 })
            {
                var scale = dpi / 100d;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, $"{name}-w{width:F0}-dpi{dpi}.png"));
                encoder.Save(stream);
            }
            results.Add(new { name, width, height, notice = notice is not null, heading = notice is null ? "" : NoraSubscriptionNoticeView.Heading(state), endpoint = server is null ? "" : NoraProtectedDisplay.EndpointText(server), textBlocks = rectangles.Count, clipping = false, overlap = false });
        }
    }

    private static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
