#if WINDOWS
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Directory = System.IO.Directory;
using File = System.IO.File;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using Path = System.IO.Path;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using SearchOption = System.IO.SearchOption;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrushes = System.Drawing.Brushes;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImage = System.Drawing.Image;
using DrawingIcon = System.Drawing.Icon;
using DrawingPen = System.Drawing.Pen;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace Nvp;

internal static class NoraWpfShell
{
    public static int RunPreview()
    {
        var app = new WpfApplication { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new NoraWpfWindow(enableTray: false));
        return 0;
    }

    public static void Run()
    {
        var app = new WpfApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            var incident = NoraErrors.Classify(NoraOperation.ApplicationStart, e.Exception);
            try
            {
                WpfMessageBox.Show(
                    $"{incident.Message}\n\nWhat to do: {incident.Action}\n\nError code: {incident.Code}",
                    incident.Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
            e.Handled = true;
        };
        var window = new NoraWpfWindow(enableTray: true);
        app.SessionEnding += (_, _) => window.RequestSystemExit();
        app.Run(window);
    }

    public static int RenderSnapshot(string outputPath, string state = "ready", int delayMs = 700)
    {
        var previousVisualReady = Environment.GetEnvironmentVariable("NORA_GUI_VISUAL_READY");
        var previousState = Environment.GetEnvironmentVariable("NORA_GUI_SNAPSHOT_STATE");
        var previousPage = Environment.GetEnvironmentVariable("NORA_GUI_SNAPSHOT_PAGE");
        var previousReducedMotion = Environment.GetEnvironmentVariable("NORA_REDUCED_MOTION");
        // state supports "connected", "servers", or combined "connected+servers".
        var page = "";
        var parts = state.Split('+', ':');
        foreach (var part in parts.Select(x => x.Trim().ToLowerInvariant()))
        {
            if (part is "home" or "servers" or "add" or "users" or "logs" or "settings" or "voice")
                page = part;
            else if (part.Length > 0)
                state = part;
        }
        if (parts.Length > 1 || page.Length > 0)
        {
            if (state == page)
                state = "ready";
        }
        Environment.SetEnvironmentVariable("NORA_GUI_VISUAL_READY", "1");
        Environment.SetEnvironmentVariable("NORA_REDUCED_MOTION", "1");
        Environment.SetEnvironmentVariable("NORA_GUI_SNAPSHOT_STATE", state);
        Environment.SetEnvironmentVariable("NORA_GUI_SNAPSHOT_PAGE", page);
        var app = new WpfApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window window = state switch
        {
            "dialog-install" => new NoraInstallWindow(),
            "dialog-key" => new NoraKeyWindow("Connection key created",
                "krot://eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkRlbW8gS2V5IiwiaWF0IjoxNTE2MjM5MDIyfQ.demo-key-material-abcdef1234567890-abcdef1234567890"),
            "dialog-progress" => new NoraProgressWindow("Creating user", "Creating `marina` and restarting KRot on demo-vps.example..."),
            "dialog-discord-error" => CreateDiscordErrorPreview(),
            _ => new NoraWpfWindow(enableTray: false)
        };
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.ShowInTaskbar = false;
        try
        {
            window.Show();
            window.UpdateLayout();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Clamp(delayMs, 100, 5000))
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
            window.UpdateLayout();

            var dpi = VisualTreeHelper.GetDpi(window);
            var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
            var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var stream = File.Create(fullPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            window.Close();
            app.Shutdown();
            Environment.SetEnvironmentVariable("NORA_GUI_VISUAL_READY", previousVisualReady);
            Environment.SetEnvironmentVariable("NORA_REDUCED_MOTION", previousReducedMotion);
            Environment.SetEnvironmentVariable("NORA_GUI_SNAPSHOT_STATE", previousState);
            Environment.SetEnvironmentVariable("NORA_GUI_SNAPSHOT_PAGE", previousPage);
        }
    }

    private static Window CreateDiscordErrorPreview()
    {
        var window = new NoraProgressWindow("Preparing Discord Mode", "Checking the selective routing engine...");
        window.SetError(
            "Discord Mode could not be prepared",
            "A required routing component is missing. Restore the complete NORA portable folder and try again.");
        return window;
    }

    public static int RunTraySmoke(string outputPath)
    {
        var app = new WpfApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new NoraWpfWindow(enableTray: true)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false
        };
        var passed = false;
        Exception? failure = null;

        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(180);
                window.Close();
                await Task.Delay(180);

                var hiddenAfterClose = !window.IsVisible;
                var dispatcherAliveAfterClose = !window.Dispatcher.HasShutdownStarted;
                var trayVisibleAfterClose = window.TrayVisibleForSmoke;

                window.RestoreFromTrayForSmoke();
                await Task.Delay(180);
                var restoredFromTray = window.IsVisible;

                passed = hiddenAfterClose && dispatcherAliveAfterClose &&
                         trayVisibleAfterClose && restoredFromTray;
                WriteResult(new
                {
                    passed,
                    hiddenAfterClose,
                    dispatcherAliveAfterClose,
                    trayVisibleAfterClose,
                    restoredFromTray,
                    exitPathInvoked = true
                });
            }
            catch (Exception ex)
            {
                failure = ex;
                WriteResult(new { passed = false, error = ex.ToString() });
            }
            finally
            {
                await window.ExitForSmokeAsync();
            }
        };

        void WriteResult(object result)
        {
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }

        app.Run(window);
        if (failure is not null)
            Console.Error.WriteLine(failure);
        return passed ? 0 : 1;
    }

    public static int RunConnectCancelSmoke(string outputPath)
    {
        var app = new WpfApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new NoraWpfWindow(enableTray: false)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false
        };
        var passed = false;
        Exception? failure = null;

        window.Loaded += async (_, _) =>
        {
            try
            {
                var result = await window.RunConnectCancelSmokeAsync();
                passed = result.Passed;
                var fullPath = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            catch (Exception ex)
            {
                failure = ex;
                var fullPath = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            finally
            {
                await window.ExitForSmokeAsync();
            }
        };

        app.Run(window);
        if (failure is not null)
            Console.Error.WriteLine(failure);
        return passed ? 0 : 1;
    }
}

internal static class NoraWpfTheme
{
    public static readonly Color Bg = Color.FromRgb(7, 10, 15);
    public static readonly Color Bg2 = Color.FromRgb(12, 16, 23);
    public static readonly Color Card = Color.FromRgb(17, 22, 30);
    public static readonly Color Card2 = Color.FromRgb(21, 26, 36);
    public static readonly Color Stroke = Color.FromRgb(43, 52, 65);
    public static readonly Color StrokeHot = Color.FromRgb(255, 154, 35);
    public static readonly Color Text = Color.FromRgb(238, 241, 247);
    public static readonly Color Muted = Color.FromRgb(146, 157, 176);
    public static readonly Color Dim = Color.FromRgb(92, 101, 116);
    public static readonly Color Orange = Color.FromRgb(255, 156, 38);
    public static readonly Color Orange2 = Color.FromRgb(255, 184, 96);
    public static readonly Color Green = Color.FromRgb(20, 191, 96);
    public static readonly Color Red = Color.FromRgb(235, 82, 82);
    public static readonly Color Blue = Color.FromRgb(104, 141, 255);

    public static SolidColorBrush Brush(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public static Color With(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static readonly SolidColorBrush BgBrush = Brush(Bg);
    public static readonly SolidColorBrush CardBrush = Brush(Card);
    public static readonly SolidColorBrush StrokeBrush = Brush(Stroke);
    public static readonly SolidColorBrush TextBrush = Brush(Text);
    public static readonly SolidColorBrush MutedBrush = Brush(Muted);
    public static readonly SolidColorBrush DimBrush = Brush(Dim);
    public static readonly SolidColorBrush OrangeBrush = Brush(Orange);
    public static readonly SolidColorBrush GreenBrush = Brush(Green);
    public static readonly SolidColorBrush RedBrush = Brush(Red);
    public static readonly SolidColorBrush BlueBrush = Brush(Blue);
    public static readonly SolidColorBrush AmberBrush = Brush(Color.FromRgb(255, 196, 84));

    public static readonly FontFamily UiFont = new("PT Root UI VF, Segoe UI");
    public static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas");

    // Product motion is part of NORA's UI, so Windows/RDP animation settings must
    // not silently turn the interface into a static screenshot. Reduced motion
    // remains available as an explicit application-level override.
    public static bool MotionEnabled
    {
        get
        {
            var reduced = Environment.GetEnvironmentVariable("NORA_REDUCED_MOTION");
            return !string.Equals(reduced, "1", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reduced, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static Color PingColor(long ms) => ms <= 0 ? Red : ms < 90 ? Green : ms < 200 ? Color.FromRgb(255, 196, 84) : Red;
}

internal sealed record NoraConnectCancelSmokeResult(
    bool Passed,
    bool ClickEnteredStopping,
    bool CancellationObserved,
    bool CoreDetached,
    bool StopCalledOnce,
    bool ReturnedToReady,
    bool ButtonRestored);

internal readonly record struct NoraWindowScale(double Factor, double WindowWidth, double WindowHeight);

/// <summary>
/// The NORA scene has one approved 500×940 composition. Smaller displays use
/// one uniform scale factor for that complete canvas; no child control receives
/// its own responsive rule.
/// </summary>
internal static class NoraWindowScalePolicy
{
    // This is the established *outer* r19 window size.  The actual client
    // scene is captured from the native window at runtime because Windows'
    // resize frame is not part of WPF's Content surface.
    public const double DesignWidth = 500;
    // 940 is the approved outer window height. The native Windows caption uses
    // the remaining 40 DIP; only the established 500×900 client scene belongs
    // inside the Viewbox.
    public const double DesignCanvasHeight = 900;
    public const double DesignWindowHeight = 940;
    public const double NativeChromeHeight = DesignWindowHeight - DesignCanvasHeight;
    private const double EdgeReserve = 12;

    public static NoraWindowScale Create(
        double workAreaWidth,
        double workAreaHeight,
        double canvasWidth = DesignWidth,
        double canvasHeight = DesignCanvasHeight,
        double chromeWidth = 0,
        double chromeHeight = NativeChromeHeight)
    {
        var availableCanvasWidth = Math.Max(1, workAreaWidth - EdgeReserve - chromeWidth);
        var availableCanvasHeight = Math.Max(1, workAreaHeight - EdgeReserve - chromeHeight);
        var factor = Math.Min(1, Math.Min(availableCanvasWidth / canvasWidth, availableCanvasHeight / canvasHeight));
        factor = Math.Max(0.1, factor);
        return new NoraWindowScale(
            factor,
            canvasWidth * factor + chromeWidth,
            canvasHeight * factor + chromeHeight);
    }

    public static int RunSelfTest()
    {
        // Typical native Windows 11 frame at 100%: 16×40 DIP. Runtime uses
        // actual HWND client bounds instead of assuming these values.
        const double canvasWidth = 484;
        const double canvasHeight = 900;
        const double chromeWidth = 16;
        const double chromeHeight = 40;
        var cases = new[]
        {
            (Name: "2K", Width: 2560d, Height: 1400d, Scale: 1d),
            (Name: "FHD-125", Width: 1536d, Height: 832d, Scale: 780d / canvasHeight),
            (Name: "HD", Width: 1366d, Height: 720d, Scale: 668d / canvasHeight),
            (Name: "HD-short", Width: 1280d, Height: 680d, Scale: 628d / canvasHeight)
        };

        foreach (var test in cases)
        {
            var scale = Create(test.Width, test.Height, canvasWidth, canvasHeight, chromeWidth, chromeHeight);
            if (scale.WindowWidth > test.Width - EdgeReserve + 0.001 ||
                scale.WindowHeight > test.Height - EdgeReserve + 0.001 ||
                Math.Abs(scale.Factor - test.Scale) > 0.002 ||
                Math.Abs((scale.WindowWidth - chromeWidth) - canvasWidth * scale.Factor) > 0.001)
            {
                Console.Error.WriteLine($"WINDOW SCALE SELF-TEST FAIL: {test.Name}; factor={scale.Factor:0.000}; size={scale.WindowWidth:0}x{scale.WindowHeight:0}");
                return 1;
            }
        }

        Console.WriteLine("WINDOW SCALE SELF-TEST PASS: one uniform canvas scale fits every work area");
        return 0;
    }
}

internal sealed class NoraWpfWindow : Window
{
    private enum PageKind { Home, Servers, Add, Users, Logs, Settings, VoiceMode }
    private enum TunnelState { Ready, Connecting, Connected, Disconnecting, Failed }
    private static readonly Color DiscordModeAccent = Color.FromRgb(0x58, 0x65, 0xF2);
    private enum ToastKind { Info, Success, Error }

    private readonly Grid _contentHost = new();
    private readonly Grid _navHost = new();
    private readonly Grid _toastHost = new() { IsHitTestVisible = false };
    private readonly Grid _homeAtmosphereHost = new() { IsHitTestVisible = false };
    private NoraWindowScale _windowScale = new(1, NoraWindowScalePolicy.DesignWidth, NoraWindowScalePolicy.DesignWindowHeight);
    // The approved r19 scene is the native client area, not the outer HWND.
    // Capturing it before the first downscale avoids clipping it behind the
    // unscaled Windows resize frame on 1366×768 displays.
    private double _designCanvasWidth = NoraWindowScalePolicy.DesignWidth;
    private double _designCanvasHeight = NoraWindowScalePolicy.DesignCanvasHeight;
    private double _nativeChromeWidth;
    private double _nativeChromeHeight = NoraWindowScalePolicy.NativeChromeHeight;
    private bool _designCanvasCaptured;
    private double _captionIconScale = -1;
    private bool _scaleRefreshQueued;
    private bool _applyingScale;
    private UIElement? _shellCanvas;
    private Grid? _scaleHost;
    private ScaleTransform? _shellScaleTransform;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _trafficSampleTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _managedRefreshTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly Stopwatch _connectedFor = new();
    private readonly StringBuilder _logs = new();
    private NoraRunLog? _runLog;
    private readonly Dictionary<string, PingStatus> _ping = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NoraTrafficGraph> _graphs = [];
    private readonly Dictionary<string, TextBlock> _userStatusText = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _userTrafficText = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ImageCacheSync = new();
    private static readonly Dictionary<string, IReadOnlyList<ImageSource>> LocationImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource> NamedImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static Geometry? VoiceModeIconGeometry;
    private static readonly object EmojiImageCacheSync = new();
    private static readonly Dictionary<string, ImageSource?> EmojiImageCache = new(StringComparer.OrdinalIgnoreCase);

    private NoraConnectButton? _connectButton;
    private NoraCyberGrid? _heroGrid;
    private NoraAudioBarBackdrop? _audioBackdrop;
    private TextBlock? _discordModeStatusText;
    private Grid? _welcomeOverlayHost;
    private IVpnCoreProcess? _core;
    // A connection launch owns this token for its entire lifetime.  Cancelling it
    // prevents late continuations (start/probe/AUTO fallback) from reviving a
    // connection after the user has pressed the same button to stop it.
    private CancellationTokenSource? _connectCancellation;
    private CancellationTokenSource? _diagnosticCancellation;
    private bool _diagnosticRunning;
    private string _diagnosticStatus = "";
    private string _latestDiagnosticReport = "";
    private NoraCoreLogLimiter _coreLogLimiter = new();
    private string _activeProfilePath = "";
    private NvpConfig? _activeConfig;
    private NoraSubscriptionServer? _activeSubscriptionServer;
    private string _activeExternalProtocol = "";
    private TunnelState _state = TunnelState.Ready;
    private PageKind _page = PageKind.Home;
    private int _navIndex;
    private bool _navAnimate;
    private string _trafficLabel = "";
    private long _trafficUpRate;
    private long _trafficDownRate;
    private double _trafficUpBytes;
    private double _trafficDownBytes;
    private DateTimeOffset _lastTrafficSample;
    private string _trafficInterfaceHint = "";
    private DateTimeOffset _lastInterfaceTrafficSample;
    private long _lastInterfaceBytesSent = -1;
    private long _lastInterfaceBytesReceived = -1;
    private readonly Queue<(long Up, long Down)> _trafficHistory = [];
    private bool _isPinging;
    private bool _isArrangingServers;
    private bool _showActiveEndpoint;
    private string _selectedManagedServerPath = "";
    private string _expandedManagedServerPath = "";
    private bool _showAddUser;
    private string _generatedUserKey = "";
    private string _expandedSubscriptionId = "";
    private readonly HashSet<string> _refreshingManagedServers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastManagedRefresh = new(StringComparer.OrdinalIgnoreCase);
    private int _toastSequence;
    private string _lastToastText = "";
    private DateTimeOffset _lastToastAt;
    private string _pendingPremiumFeature = "";
    private readonly bool _trayEnabled;
    private FormsNotifyIcon? _trayIcon;
    private IntPtr _taskbarIconHandle;
    private bool _exitRequested;
    private bool _exitInProgress;

    public NoraWpfWindow(bool enableTray = true)
    {
        _trayEnabled = enableTray;
        _runLog = NoraRunLog.StartNew();
        Title = "NORA VPN";
        Icon = LoadBrandWindowIcon();
        Width = NoraWindowScalePolicy.DesignWidth;
        Height = NoraWindowScalePolicy.DesignWindowHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = NoraWpfTheme.BgBrush;
        FontFamily = new FontFamily("PT Root UI VF, Segoe UI");
        Foreground = NoraWpfTheme.TextBrush;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        SourceInitialized += (_, _) =>
        {
            ApplyDarkWindowChrome();
            if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
                source.AddHook(WindowMessageHook);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NoraSingleInstance.AllowActivationMessage(hwnd);
            NoraSingleInstance.PublishWindow(hwnd);
            ApplyWindowScale();
        };
        Loaded += (_, _) => ApplyWindowScale();
        LocationChanged += (_, _) => QueueScaleRefresh();

        _shellCanvas = BuildShell();
        Content = _shellCanvas;
        LoadActiveProfile();
        AppendLog("NORA VPN started");
        AppendLog(NoraPremiumService.GetEqualizerDiagnostic());
        var snapshotState = Environment.GetEnvironmentVariable("NORA_GUI_SNAPSHOT_STATE")?.Trim().ToLowerInvariant() ?? "";
        if (snapshotState == "arrange")
            _isArrangingServers = true;
        if (snapshotState == "endpoint-revealed")
            _showActiveEndpoint = true;
        if (snapshotState is "connected" or "discord-active" or "connecting" or "stopping" or "failed")
        {
            _state = snapshotState switch
            {
                "connecting" => TunnelState.Connecting,
                "stopping" => TunnelState.Disconnecting,
                "failed" => TunnelState.Failed,
                _ => TunnelState.Connected
            };
            if (_state == TunnelState.Connected)
            {
                _connectedFor.Start();
                _trafficUpRate = 2_420_000;
                _trafficDownRate = 6_860_000;
                _trafficUpBytes = 389 * 1024 * 1024d;
                _trafficDownBytes = 1.32 * 1024 * 1024 * 1024d;
                for (var i = 0; i < 48; i++)
                {
                    var wave = 0.42 + Math.Sin(i * 0.36) * 0.18 + Math.Sin(i * 0.77 + 1.4) * 0.13;
                    _trafficHistory.Enqueue(((long)(1_100_000 * Math.Max(0.08, wave)), (long)(5_300_000 * Math.Max(0.08, wave + 0.18))));
                }
            }
        }
        var snapshotPage = Environment.GetEnvironmentVariable("NORA_GUI_SNAPSHOT_PAGE")?.Trim().ToLowerInvariant() ?? "";
        if (snapshotPage.Length > 0)
        {
            if (snapshotPage == "logs")
                SeedDemoLogs();
            var snapshotCollapsed = string.Equals(Environment.GetEnvironmentVariable("NORA_GUI_SNAPSHOT_STATE"), "collapsed", StringComparison.OrdinalIgnoreCase);
            if (snapshotPage == "servers" && NoraSubscriptionStore.LoadAll().FirstOrDefault() is { } firstSub)
            {
                if (!snapshotCollapsed)
                    _expandedSubscriptionId = firstSub.Id;
                // Deterministic fake latencies so badge colors can be visually QA'd.
                var fakePing = new long[] { 41, 58, 36, 132, 118, 64, 0, 244 };
                var i = 0;
                foreach (var s in firstSub.Servers)
                {
                    var ms = fakePing[i++ % fakePing.Length];
                    _ping[s.LocalPath] = ms <= 0 ? new PingStatus(false, "timeout") : new PingStatus(true, $"{ms} ms");
                }
            }
            if (snapshotPage == "users" && DiscoverManagedServers().FirstOrDefault() is { } firstManaged)
            {
                _selectedManagedServerPath = firstManaged.ProfilePath;
                _expandedManagedServerPath = firstManaged.ProfilePath;
            }
        }
        RenderPage(snapshotPage switch
        {
            "servers" => PageKind.Servers,
            "add" => PageKind.Add,
            "users" => PageKind.Users,
            "logs" => PageKind.Logs,
            "settings" => PageKind.Settings,
            "voice" => PageKind.VoiceMode,
            _ => PageKind.Home
        });
        if (snapshotState is "connected" or "discord-active")
        {
            var previewDuration = new TimeSpan(0, 18, 42);
            _connectButton?.SnapConnectedPreview(previewDuration);
            foreach (var graph in _graphs)
                graph.SetTraffic(_trafficUpRate, _trafficDownRate, (long)_trafficUpBytes, (long)_trafficDownBytes, previewDuration);
        }
        if (snapshotState == "error-toast")
            ShowErrorToast(NoraErrors.Create("NORA-CON-2010", NoraOperation.Connect, "Snapshot data-plane verification failure"));
        if (snapshotState == "welcome-info")
            ShowWelcomeInfoSheet();

        Loaded += (_, _) =>
        {
            if (!NoraWpfTheme.MotionEnabled || string.Equals(Environment.GetEnvironmentVariable("NORA_GUI_VISUAL_READY"), "1", StringComparison.Ordinal))
                return;
            if (Content is FrameworkElement fe)
            {
                fe.RenderTransformOrigin = new Point(0.5, 0.5);
                var scale = new ScaleTransform(0.97, 0.97);
                fe.RenderTransform = scale;
                fe.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
            }
        };

        _clock.Tick += (_, _) =>
        {
            if (_state == TunnelState.Connected)
            {
                if (_connectButton is not null)
                {
                    _connectButton.DetailText = _connectedFor.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
                    _connectButton.InvalidateVisual();
                }
                foreach (var graph in _graphs)
                    graph.SetTraffic(_trafficUpRate, _trafficDownRate, (long)_trafficUpBytes, (long)_trafficDownBytes, _connectedFor.Elapsed);
            }
        };
        Loaded += async (_, _) =>
        {
            // Snapshot rendering must stay deterministic: an online refresh can
            // otherwise rebuild the page in the middle of RenderTargetBitmap.
            if (string.Equals(Environment.GetEnvironmentVariable("NORA_GUI_VISUAL_READY"), "1", StringComparison.Ordinal))
                return;
            var wasPremium = NoraPremiumService.IsPremium;
            var hadEqualizer = NoraPremiumService.EqualizerEffective;
            var hadSlideshow = NoraPremiumService.ServerSlideshowEffective;
            await NoraPremiumService.RefreshAsync();
            if (wasPremium != NoraPremiumService.IsPremium ||
                hadEqualizer != NoraPremiumService.EqualizerEffective ||
                hadSlideshow != NoraPremiumService.ServerSlideshowEffective)
                RenderPage(_page);
        };
        _clock.Start();
        _trafficSampleTimer.Tick += (_, _) =>
        {
            if (_state == TunnelState.Connected)
                SampleBackendInterfaceTraffic();
        };
        _trafficSampleTimer.Start();
        _managedRefreshTimer.Tick += (_, _) =>
        {
            if (_page != PageKind.Users || string.IsNullOrWhiteSpace(_expandedManagedServerPath))
                return;
            var server = DiscoverManagedServers().FirstOrDefault(x => string.Equals(x.ProfilePath, _expandedManagedServerPath, StringComparison.OrdinalIgnoreCase));
            if (server is not null)
                MaybeRefreshManagedStats(server, force: true);
        };
        _managedRefreshTimer.Start();
        if (_trayEnabled)
            InitializeTray();
        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            _diagnosticCancellation?.Cancel();
            _trafficSampleTimer.Stop();
            DisposeTray();
            DisposeTaskbarIcon();
        };
    }

    public void RequestSystemExit() => _exitRequested = true;

    internal bool TrayVisibleForSmoke => _trayIcon?.Visible == true;

    internal void RestoreFromTrayForSmoke() => ShowFromTray();

    internal Task ExitForSmokeAsync() => ExitApplicationAsync();

    internal async Task<NoraConnectCancelSmokeResult> RunConnectCancelSmokeAsync()
    {
        if (_connectButton is null)
            return new NoraConnectCancelSmokeResult(false, false, false, false, false, false, false);

        var cancellation = new CancellationTokenSource();
        var core = new CancelSmokeCore();
        _connectCancellation = cancellation;
        _core = core;
        ApplyState(TunnelState.Connecting);

        try
        {
            // Raise the real button event rather than calling the helper directly:
            // the smoke test covers the user path from Connecting -> Stopping.
            _connectButton.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent));
            var stopStarted = await Task.WhenAny(core.StopStarted.Task, Task.Delay(TimeSpan.FromSeconds(1))) == core.StopStarted.Task;
            var clickEnteredStopping = stopStarted && _state == TunnelState.Disconnecting;
            var cancellationObserved = cancellation.IsCancellationRequested;
            var coreDetached = _core is null;

            core.ReleaseStop();
            for (var i = 0; i < 20 && _state != TunnelState.Ready; i++)
                await Task.Delay(10);

            var returnedToReady = _state == TunnelState.Ready;
            var buttonRestored = _connectButton.MainText == "Connect" && !_connectButton.IsProgress && !_connectButton.IsFailed;
            var stopCalledOnce = core.StopCalls == 1;
            var passed = clickEnteredStopping && cancellationObserved && coreDetached &&
                         stopCalledOnce && returnedToReady && buttonRestored;
            return new NoraConnectCancelSmokeResult(
                passed,
                clickEnteredStopping,
                cancellationObserved,
                coreDetached,
                stopCalledOnce,
                returnedToReady,
                buttonRestored);
        }
        finally
        {
            core.ReleaseStop();
            if (ReferenceEquals(_connectCancellation, cancellation))
                _connectCancellation = null;
            cancellation.Dispose();
            _core = null;
        }
    }

    private sealed class CancelSmokeCore : IVpnCoreProcess
    {
        private readonly TaskCompletionSource<bool> _allowStop = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> StopStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCalls { get; private set; }

        public Task StartAsync(TimeSpan timeout) => Task.CompletedTask;
        public Task WaitForExitAsync() => Task.Delay(Timeout.InfiniteTimeSpan);

        public Task StopAsync(TimeSpan timeout)
        {
            StopCalls++;
            StopStarted.TrySetResult(true);
            return _allowStop.Task;
        }

        public void ReleaseStop() => _allowStop.TrySetResult(true);
    }

    private void InitializeTray()
    {
        var open = new FormsToolStripMenuItem("Open NORA VPN");
        var exit = new FormsToolStripMenuItem("Exit");
        var menu = new FormsContextMenuStrip();
        menu.Items.Add(open);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exit);

        _trayIcon = new FormsNotifyIcon
        {
            Text = "NORA VPN",
            Icon = CreateTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        open.Click += (_, _) => Dispatcher.BeginInvoke(ShowFromTray);
        exit.Click += (_, _) => Dispatcher.BeginInvoke(async () => await ExitApplicationAsync());
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowFromTray);
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == FormsMouseButtons.Left)
                Dispatcher.BeginInvoke(ShowFromTray);
        };
    }

    private void ShowFromTray()
    {
        if (_exitRequested)
            return;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private async Task ExitApplicationAsync()
    {
        if (_exitInProgress)
            return;
        _exitInProgress = true;
        _exitRequested = true;
        if (_diagnosticRunning)
        {
            _diagnosticCancellation?.Cancel();
            var waitUntil = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (_diagnosticRunning && DateTime.UtcNow < waitUntil)
                await Task.Delay(100);
        }
        if (_trayIcon is not null)
            _trayIcon.Visible = false;
        if (_state == TunnelState.Connecting)
            await CancelConnectingAsync();
        else if (_core is not null)
            await DisconnectAsync();
        DisposeTray();
        Close();
        WpfApplication.Current?.Shutdown();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_trayEnabled || _exitRequested)
            return;
        e.Cancel = true;
        Hide();
    }

    private void DisposeTray()
    {
        if (_trayIcon is null)
            return;
        _trayIcon.Visible = false;
        _trayIcon.Icon?.Dispose();
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private static DrawingIcon CreateTrayIcon()
    {
        // The .ico is the canonical Windows surface: tray, executable and taskbar
        // all use exactly the same transparent, multi-resolution mark.
        foreach (var iconPath in BrandIconPaths())
        {
            try
            {
                if (File.Exists(iconPath))
                    return new DrawingIcon(iconPath);
            }
            catch
            {
                // Fall through to the rasterized PNG/fallback below.
            }
        }

        // Fallback only for a damaged portable folder. The shipped app should
        // always use assets/logo.ico above.
        using var bitmap = new DrawingBitmap(64, 64, DrawingPixelFormat.Format32bppArgb);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.Clear(DrawingColor.Transparent);
        var logoPath = BrandPngPaths().FirstOrDefault(File.Exists);
        if (logoPath is not null)
        {
            using var logo = DrawingImage.FromFile(logoPath);
            const int inset = 2;
            var scale = Math.Min((64d - inset * 2) / logo.Width, (64d - inset * 2) / logo.Height);
            var width = (int)Math.Round(logo.Width * scale);
            var height = (int)Math.Round(logo.Height * scale);
            graphics.DrawImage(logo, (64 - width) / 2, (64 - height) / 2, width, height);
        }
        else
        {
            using var font = new DrawingFont("Segoe UI", 34, DrawingFontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            using var brush = new System.Drawing.SolidBrush(DrawingColor.FromArgb(255, 245, 247, 250));
            var text = graphics.MeasureString("N", font);
            graphics.DrawString("N", font, brush, (64 - text.Width) / 2, (64 - text.Height) / 2 - 1);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = DrawingIcon.FromHandle(handle);
            return (DrawingIcon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private void ApplyDarkWindowChrome()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        // Windows 10 accepts immersive dark mode; Windows 11 additionally
        // honours caption/text/border colors. Unsupported attributes simply
        // return an HRESULT, leaving the normal non-client behavior intact.
        SetDwmWindowAttribute(hwnd, 20, 1);
        SetDwmWindowAttribute(hwnd, 19, 1); // compatibility with older Win10 builds
        SetDwmWindowAttribute(hwnd, 35, ToColorRef(NoraWpfTheme.Bg));
        SetDwmWindowAttribute(hwnd, 36, ToColorRef(NoraWpfTheme.Text));
        SetDwmWindowAttribute(hwnd, 34, ToColorRef(NoraWpfTheme.Stroke));
        ApplyTaskbarIcon(hwnd);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmDpiChanged = 0x02E0;
        if (message == NoraSingleInstance.ActivationMessage)
        {
            handled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(ShowFromTray));
            return IntPtr.Zero;
        }
        if (message == WmDpiChanged)
            QueueScaleRefresh();
        return IntPtr.Zero;
    }

    private void QueueScaleRefresh()
    {
        if (_scaleRefreshQueued || _applyingScale || _exitRequested)
            return;
        _scaleRefreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _scaleRefreshQueued = false;
            ApplyWindowScale();
        }), DispatcherPriority.Background);
    }

    private void ApplyWindowScale()
    {
        if (_applyingScale)
            return;

        _applyingScale = true;
        try
        {
            CaptureDesignCanvasBounds();
            var workArea = GetWindowWorkArea();
            var next = NoraWindowScalePolicy.Create(
                workArea.WidthDip,
                workArea.HeightDip,
                _designCanvasWidth,
                _designCanvasHeight,
                _nativeChromeWidth,
                _nativeChromeHeight);
            var sizeChanged = Math.Abs(next.WindowWidth - Width) > 0.5 || Math.Abs(next.WindowHeight - Height) > 0.5;
            _windowScale = next;
            ApplyCaptionIconScale(next.Factor);

            if (next.Factor < 0.9995)
                EnableUniformCanvasScale(next.Factor);
            else
                DisableUniformCanvasScale();

            if (sizeChanged)
            {
                Width = next.WindowWidth;
                Height = next.WindowHeight;
            }

            ClampWindowToMonitor(workArea.Pixels);
        }
        finally
        {
            _applyingScale = false;
        }
    }

    private void EnableUniformCanvasScale(double factor)
    {
        if (_shellCanvas is not FrameworkElement shell)
            return;

        if (_shellScaleTransform is null)
        {
            // Keep the approved r19 scene at its original layout size, then
            // scale that one visual tree as a whole. No page or card reflows.
            shell.Width = _designCanvasWidth;
            shell.Height = _designCanvasHeight;
            _shellScaleTransform = new ScaleTransform(factor, factor);
            shell.LayoutTransform = _shellScaleTransform;

            if (ReferenceEquals(Content, shell))
                Content = null;
            _scaleHost = new Grid
            {
                Width = _designCanvasWidth * factor,
                Height = _designCanvasHeight * factor,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                ClipToBounds = true
            };
            _scaleHost.Children.Add(shell);
            Content = _scaleHost;
        }
        else
        {
            _shellScaleTransform.ScaleX = factor;
            _shellScaleTransform.ScaleY = factor;
            if (_scaleHost is not null)
            {
                _scaleHost.Width = _designCanvasWidth * factor;
                _scaleHost.Height = _designCanvasHeight * factor;
            }
        }
    }

    private void CaptureDesignCanvasBounds()
    {
        if (_designCanvasCaptured)
            return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero ||
            !GetWindowRect(hwnd, out var outer) ||
            !GetClientRect(hwnd, out var client))
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = Math.Max(0.01, dpi.DpiScaleX);
        var scaleY = Math.Max(0.01, dpi.DpiScaleY);
        var outerWidth = (outer.Right - outer.Left) / scaleX;
        var outerHeight = (outer.Bottom - outer.Top) / scaleY;
        var clientWidth = (client.Right - client.Left) / scaleX;
        var clientHeight = (client.Bottom - client.Top) / scaleY;

        if (clientWidth < 1 || clientHeight < 1 ||
            outerWidth < clientWidth || outerHeight < clientHeight)
            return;

        _designCanvasWidth = clientWidth;
        _designCanvasHeight = clientHeight;
        _nativeChromeWidth = Math.Max(0, outerWidth - clientWidth);
        _nativeChromeHeight = Math.Max(0, outerHeight - clientHeight);
        _designCanvasCaptured = true;
    }

    private void ApplyCaptionIconScale(double sceneScale)
    {
        // The native caption never participates in a WPF LayoutTransform. On a
        // 1366×768 screen the 16px caption mark would therefore look bigger
        // than the uniformly scaled r19 scene beneath it. Keep the original
        // r19 source at full size and optically scale only this caption mark
        // on compact displays. Tray/taskbar assets are intentionally untouched.
        var effectiveScale = Math.Clamp(sceneScale, 0.72, 1);
        if (Math.Abs(effectiveScale - _captionIconScale) < 0.01)
            return;

        var source = LoadBrandWindowIcon();
        if (source is null)
            return;

        _captionIconScale = effectiveScale;
        if (effectiveScale >= 0.999)
        {
            Icon = source;
        }
        else
        {
            const double slot = 16;
            var side = slot * effectiveScale;
            var inset = (slot - side) * 0.5;
            var group = new DrawingGroup();
            // Preserve a 16×16 viewbox; otherwise WPF uses the child drawing's
            // 12px bounds and scales it straight back up in the title bar.
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, slot, slot))));
            group.Children.Add(new ImageDrawing(source, new Rect(inset, inset, side, side)));
            group.Freeze();
            Icon = new System.Windows.Media.DrawingImage(group);
        }

        // Setting Window.Icon may re-send ICON_BIG. Restore the existing r19
        // taskbar handle; it is the same asset/size as before this compact
        // caption adjustment.
        ApplyTaskbarIcon(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    private void DisableUniformCanvasScale()
    {
        if (_shellCanvas is not FrameworkElement shell || _scaleHost is null)
            return;

        _scaleHost.Children.Remove(shell);
        Content = null;
        shell.ClearValue(FrameworkElement.LayoutTransformProperty);
        shell.Width = double.NaN;
        shell.Height = double.NaN;
        Content = shell;
        _scaleHost = null;
        _shellScaleTransform = null;
    }

    private (System.Drawing.Rectangle Pixels, double WidthDip, double HeightDip) GetWindowWorkArea()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var screen = hwnd == IntPtr.Zero
            ? System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position)
            : System.Windows.Forms.Screen.FromHandle(hwnd);
        var area = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var widthDip = area.Width / Math.Max(0.01, dpi.DpiScaleX);
        var heightDip = area.Height / Math.Max(0.01, dpi.DpiScaleY);

        if (IsSnapshotRender() && TryGetSnapshotWorkArea("NORA_GUI_WORKAREA_WIDTH", out var snapshotWidth))
            widthDip = snapshotWidth;
        if (IsSnapshotRender() && TryGetSnapshotWorkArea("NORA_GUI_WORKAREA_HEIGHT", out var snapshotHeight))
            heightDip = snapshotHeight;

        return (area, widthDip, heightDip);
    }

    private static bool TryGetSnapshotWorkArea(string variable, out double value)
        => double.TryParse(
            Environment.GetEnvironmentVariable(variable),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) && value > 0;

    private static bool IsSnapshotRender()
        => string.Equals(Environment.GetEnvironmentVariable("NORA_GUI_VISUAL_READY"), "1", StringComparison.Ordinal);

    private void ClampWindowToMonitor(System.Drawing.Rectangle workArea)
    {
        if (!IsLoaded || IsSnapshotRender())
            return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var bounds))
            return;

        const int padding = 6;
        var width = Math.Max(1, bounds.Right - bounds.Left);
        var height = Math.Max(1, bounds.Bottom - bounds.Top);
        var maxLeft = Math.Max(workArea.Left + padding, workArea.Right - width - padding);
        var maxTop = Math.Max(workArea.Top + padding, workArea.Bottom - height - padding);
        var left = Math.Clamp(bounds.Left, workArea.Left + padding, maxLeft);
        var top = Math.Clamp(bounds.Top, workArea.Top + padding, maxTop);
        if (left == bounds.Left && top == bounds.Top)
            return;

        _ = SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0, SetWindowPosNoSize | SetWindowPosNoZOrder | SetWindowPosNoActivate);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    private static void SetDwmWindowAttribute(IntPtr hwnd, int attribute, int value)
    {
        try { _ = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    private void ApplyTaskbarIcon(IntPtr hwnd)
    {
        // Keep the established r16 logo in the title bar and tray. Windows taskbar
        // reads the large window icon, so only that slot gets the simplified,
        // optically dense taskbar mark.
        if (_taskbarIconHandle == IntPtr.Zero)
        {
            foreach (var path in BrandTaskbarIconPaths())
            {
                if (!File.Exists(path))
                    continue;
                try
                {
                    using var icon = new DrawingIcon(path, new System.Drawing.Size(48, 48));
                    using var bitmap = icon.ToBitmap();
                    _taskbarIconHandle = bitmap.GetHicon();
                    if (_taskbarIconHandle != IntPtr.Zero)
                        break;
                }
                catch
                {
                    // The normal application icon remains available as the fallback.
                }
            }
        }

        if (_taskbarIconHandle != IntPtr.Zero)
            _ = SendMessage(hwnd, 0x0080 /* WM_SETICON */, new IntPtr(1) /* ICON_BIG */, _taskbarIconHandle);
    }

    private void DisposeTaskbarIcon()
    {
        if (_taskbarIconHandle == IntPtr.Zero)
            return;
        DestroyIcon(_taskbarIconHandle);
        _taskbarIconHandle = IntPtr.Zero;
    }

    private UIElement BuildShell()
    {
        var chrome = new Border
        {
            Background = NoraWpfTheme.BgBrush,
            BorderBrush = NoraWpfTheme.StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(1)
        };

        var shellLayers = new Grid
        {
            Background = NoraWpfTheme.BgBrush,
            ClipToBounds = true,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        shellLayers.SizeChanged += (_, e) =>
            shellLayers.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 17, 17);
        chrome.Child = shellLayers;

        shellLayers.Children.Add(_homeAtmosphereHost);

        var root = new Grid { Margin = new Thickness(23, 25, 23, 17) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shellLayers.Children.Add(root);

        _contentHost.ClipToBounds = true;
        Grid.SetRow(_contentHost, 0);
        root.Children.Add(_contentHost);

        _navHost.Margin = new Thickness(0, 18, 0, 0);
        Grid.SetRow(_navHost, 1);
        root.Children.Add(_navHost);

        _toastHost.Margin = new Thickness(6, 0, 6, 10);
        _toastHost.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetRow(_toastHost, 0);
        root.Children.Add(_toastHost);
        return chrome;
    }

    private void RenderPage(PageKind page)
    {
        var changed = _page != page;
        var forward = (int)page >= (int)_page;
        _page = page;
        _navAnimate = changed;
        _graphs.Clear();
        _userStatusText.Clear();
        _userTrafficText.Clear();
        _connectButton = null;
        _heroGrid = null;
        _discordModeStatusText = null;
        _welcomeOverlayHost = null;
        _contentHost.ClipToBounds = true;
        _contentHost.Children.Clear();
        _homeAtmosphereHost.Children.Clear();
        _homeAtmosphereHost.Visibility = page == PageKind.Home ? Visibility.Visible : Visibility.Collapsed;
        UIElement content = page switch
        {
            PageKind.Home => HomePage(),
            PageKind.Servers => ServersPage(),
            PageKind.Add => AddPage(),
            PageKind.Users => UsersPage(),
            PageKind.Logs => LogsPage(),
            PageKind.Settings => SettingsPage(),
            _ => VoiceModePage()
        };
        _contentHost.Children.Add(content);
        RenderNav();
        ApplyState(_state);
        if (changed)
            AnimatePageIn(content, forward);
    }

    private static void AnimatePageIn(UIElement element, bool forward)
    {
        if (!NoraWpfTheme.MotionEnabled)
            return;
        var shift = new TranslateTransform(forward ? 20 : -20, 0);
        element.RenderTransform = shift;
        if (element is FrameworkElement cached)
            cached.CacheMode = new BitmapCache(1);
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            if (element is FrameworkElement completed)
                completed.CacheMode = null;
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static int PageIndex(PageKind page) => page switch
    {
        PageKind.Home => 0,
        PageKind.Servers => 1,
        PageKind.Add => 2,
        PageKind.Users => 3,
        PageKind.Logs => 4,
        _ => 0
    };

    private void RenderNav()
    {
        _navHost.Children.Clear();
        var nav = new Border
        {
            Height = 82,
            CornerRadius = new CornerRadius(22),
            Background = NoraWpfTheme.Brush(Color.FromArgb(226, 12, 16, 23)),
            BorderBrush = NoraWpfTheme.StrokeBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10)
        };

        var grid = new Grid();
        nav.Child = grid;

        var to = PageIndex(_page);
        var from = _navAnimate ? _navIndex : to;
        _navIndex = to;

        var indicator = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = NoraWpfTheme.Brush(Color.FromArgb(64, 255, 156, 38)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(120, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 3, 0, 3),
            // The + cell hosts a circular FAB; the sliding pill would fight with it.
            Opacity = _page == PageKind.Add ? 0 : 1
        };
        var slide = new TranslateTransform();
        indicator.RenderTransform = slide;
        grid.Children.Add(indicator);

        var buttons = new UniformGrid { Columns = 5 };
        buttons.Children.Add(NavButton("Home", PageKind.Home, NoraIconKind.Home));
        buttons.Children.Add(NavButton("Servers", PageKind.Servers, NoraIconKind.ServerRack));
        buttons.Children.Add(NavButton("+", PageKind.Add, NoraIconKind.Plus));
        buttons.Children.Add(NavButton("Users", PageKind.Users, NoraIconKind.Users));
        buttons.Children.Add(NavButton("Logs", PageKind.Logs, NoraIconKind.Terminal));
        grid.Children.Add(buttons);

        grid.Loaded += (_, _) =>
        {
            var cell = grid.ActualWidth > 0 ? grid.ActualWidth / 5.0 : 86.0;
            indicator.Width = cell - 8;
            slide.X = from * cell + 4;
            if (from != to)
                slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to * cell + 4, TimeSpan.FromMilliseconds(340))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            else
                slide.X = to * cell + 4;
        };

        _navHost.Children.Add(nav);
    }

    private WpfButton NavButton(string label, PageKind page, NoraIconKind icon)
    {
        var selected = _page == page;
        var isPlus = icon == NoraIconKind.Plus;

        if (isPlus)
        {
            // Central action: a raised circular FAB instead of a full-cell block.
            var fab = new NoraFxButton(NoraWpfTheme.Orange, NoraWpfTheme.Orange2, 28, accent: true)
            {
                Width = 56,
                Height = 56,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Content = new NoraIcon { Kind = NoraIconKind.Plus, Width = 24, Height = 24, Stroke = NoraWpfTheme.BgBrush, Weight = 2.3 },
                ToolTip = "Add a server or subscription"
            };
            fab.Click += (_, _) => RenderPage(page);
            var holder = new Grid { Background = Brushes.Transparent };
            holder.Children.Add(new Ellipse
            {
                Width = 66,
                Height = 66,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(70, 255, 156, 38), 0.3),
                        new GradientStop(Color.FromArgb(0, 255, 156, 38), 1)
                    }
                },
                IsHitTestVisible = false
            });
            holder.Children.Add(fab);
            var wrapper = new WpfButton
            {
                OverridesDefaultStyle = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FocusVisualStyle = null,
                Template = new ControlTemplate(typeof(WpfButton)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) },
                Content = holder
            };
            wrapper.Click += (_, _) => RenderPage(page);
            return wrapper;
        }

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new NoraIcon
        {
            Kind = icon,
            Width = icon == NoraIconKind.Users ? 26 : 23,
            Height = icon == NoraIconKind.Users ? 26 : 23,
            Stroke = selected ? NoraWpfTheme.OrangeBrush : NoraWpfTheme.MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Foreground = selected ? NoraWpfTheme.OrangeBrush : NoraWpfTheme.MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        });

        if (selected && _navAnimate)
        {
            var pop = new ScaleTransform(0.92, 0.92);
            stack.RenderTransformOrigin = new Point(0.5, 0.6);
            stack.RenderTransform = pop;
            stack.Loaded += (_, _) =>
            {
                if (!NoraWpfTheme.MotionEnabled)
                {
                    pop.ScaleX = pop.ScaleY = 1;
                    return;
                }
                var anim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
                pop.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                pop.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            };
        }

        var b = new NoraFxButton(Colors.Transparent, Color.FromArgb(34, 255, 156, 38), 18, accent: false, stroke: Brushes.Transparent)
        {
            Content = stack,
            Margin = new Thickness(3)
        };
        b.Click += (_, _) => RenderPage(page);
        return b;
    }

    private UIElement HomePage()
    {
        if (!HasAnyConfiguredAccess())
            return WelcomeHomePage();

        ConfigureHomeAtmosphere();
        var panel = new StackPanel();
        var hero = new Grid { Height = 312, Margin = new Thickness(0, 0, 0, 14), ClipToBounds = false, Background = Brushes.Transparent };

        hero.MouseMove += (_, e) => _heroGrid?.SetPointer(e.GetPosition(_heroGrid));
        hero.MouseLeave += (_, _) => _heroGrid?.SetPointer(null);

        var header = new Grid { Height = 44, VerticalAlignment = VerticalAlignment.Top };
        var brand = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var wordmark = new TextBlock
        {
            FontSize = 29,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        wordmark.Inlines.Add(new System.Windows.Documents.Run("NORA ") { Foreground = NoraWpfTheme.TextBrush });
        wordmark.Inlines.Add(new System.Windows.Documents.Run("VPN") { Foreground = NoraWpfTheme.OrangeBrush });
        brand.Children.Add(wordmark);
        header.Children.Add(brand);

        var voiceButton = new NoraFxButton(Colors.Transparent, Color.FromArgb(42, 255, 156, 38), 14, false, NoraWpfTheme.Brush(Color.FromArgb(72, 146, 157, 176)))
        {
            Width = 44,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Content = CreateVoiceModeIcon(24, NoraWpfTheme.Brush(Color.FromRgb(181, 190, 204))),
            ToolTip = "Routing rules"
        };
        voiceButton.Click += (_, _) => RenderPage(PageKind.VoiceMode);
        header.Children.Add(voiceButton);

        var settingsButton = new NoraFxButton(Colors.Transparent, Color.FromArgb(42, 255, 156, 38), 14, false, NoraWpfTheme.Brush(Color.FromArgb(72, 146, 157, 176)))
        {
            Width = 44,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Content = new NoraIcon { Kind = NoraIconKind.Sliders, Width = 22, Height = 22, Stroke = NoraWpfTheme.Brush(Color.FromRgb(181, 190, 204)) },
            ToolTip = "Appearance settings"
        };
        settingsButton.Click += (_, _) => RenderPage(PageKind.Settings);
        header.Children.Add(settingsButton);
        hero.Children.Add(header);

        var connectHost = new Grid
        {
            Width = 240,
            Height = 240,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 9),
            ClipToBounds = false,
            Background = Brushes.Transparent
        };
        if (NoraPremiumService.EqualizerEffective)
        {
            _audioBackdrop = new NoraAudioBarBackdrop
            {
                Width = 432,
                Height = 216,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 21),
                DiagnosticLog = AppendLog
            };
            _audioBackdrop.SetAccent(ConnectAccentFor(_state));
            hero.Children.Add(_audioBackdrop);
        }

        _connectButton = new NoraConnectButton
        {
            Width = 216,
            Height = 216,
            ConnectedAccent = DiscordModeEnabledForView() ? DiscordModeAccent : NoraWpfTheme.Green,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _connectButton.Click += async (_, _) =>
        {
            if (_state == TunnelState.Connecting)
            {
                await CancelConnectingAsync();
                return;
            }

            if (_state == TunnelState.Disconnecting)
                return;

            _heroGrid?.Excite();
            if (_state == TunnelState.Connected)
                await DisconnectAsync();
            else
                await ConnectAsync();
        };
        connectHost.Children.Add(_connectButton);
        hero.Children.Add(connectHost);
        panel.Children.Add(hero);

        panel.Children.Add(ActiveServerCard());
        if (DiscordModeEnabledForView())
        {
            panel.Children.Add(DiscordModeStatusCard());
        }
        else
        {
            var graph = new NoraTrafficGraph { Height = 208, Margin = new Thickness(0, 14, 0, 0), Active = _state == TunnelState.Connected };
            graph.Seed(_trafficHistory, _trafficUpRate, _trafficDownRate, (long)_trafficUpBytes, (long)_trafficDownBytes, _connectedFor.Elapsed);
            _graphs.Add(graph);
            panel.Children.Add(graph);
        }
        return panel;
    }

    private static bool DiscordModeEnabledForView()
    {
        var snapshot = Environment.GetEnvironmentVariable("NORA_GUI_SNAPSHOT_STATE")?.Trim().ToLowerInvariant();
        return snapshot switch
        {
            "discord-enabled" or "discord-active" => true,
            "discord-disabled" => false,
            _ => NoraDiscordModeSettings.Enabled
        };
    }

    private UIElement DiscordModeStatusCard()
    {
        var slideshowEnabled = NoraPremiumService.ServerSlideshowEffective;
        var images = LoadLocationImages("Discord", slideshowEnabled);
        var imageZoom = new ScaleTransform(1, 1, 0.5, 0.5);
        var card = new Border
        {
            Height = 208,
            Margin = new Thickness(0, 14, 0, 0),
            CornerRadius = new CornerRadius(22),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(118, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            Background = NoraWpfTheme.CardBrush,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };

        var surface = new Grid { ClipToBounds = true };
        AddPhotoSlideshow(surface, images, imageZoom, new CornerRadius(21));
        surface.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(21),
            Background = new LinearGradientBrush(
                Color.FromArgb(238, 6, 9, 14),
                Color.FromArgb(66, 6, 9, 14),
                new Point(0, 0.5),
                new Point(1, 0.5)),
            IsHitTestVisible = false
        });
        surface.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(21),
            Background = new LinearGradientBrush(
                Color.FromArgb(18, 6, 9, 14),
                Color.FromArgb(214, 6, 9, 14),
                new Point(0.5, 0),
                new Point(0.5, 1)),
            IsHitTestVisible = false
        });

        var root = new Grid { Margin = new Thickness(21, 18, 21, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var eyebrow = new StackPanel { Orientation = Orientation.Horizontal };
        eyebrow.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = NoraWpfTheme.GreenBrush,
            Margin = new Thickness(0, 3, 9, 0)
        });
        eyebrow.Children.Add(new TextBlock
        {
            Text = "DISCORD MODE IS ACTIVE",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.OrangeBrush
        });
        root.Children.Add(eyebrow);

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 2) };
        copy.Children.Add(new TextBlock
        {
            Text = "Only Discord uses the VPN",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush
        });
        copy.Children.Add(new TextBlock
        {
            Text = "Games, browsers and everything else keep using your normal internet connection.",
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Foreground = NoraWpfTheme.Brush(Color.FromRgb(204, 213, 226)),
            Margin = new Thickness(0, 8, 0, 0)
        });
        Grid.SetRow(copy, 1);
        root.Children.Add(copy);

        _discordModeStatusText = new TextBlock
        {
            Text = _state == TunnelState.Connected ? "DISCORD IS PROTECTED" : "SELECT A VLESS OR KROT SERVER AND CONNECT",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = _state == TunnelState.Connected ? NoraWpfTheme.GreenBrush : NoraWpfTheme.DimBrush
        };
        Grid.SetRow(_discordModeStatusText, 2);
        root.Children.Add(_discordModeStatusText);
        surface.Children.Add(root);
        card.Child = surface;
        return card;
    }

    private bool HasAnyConfiguredAccess()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("NORA_GUI_EMPTY"), "1", StringComparison.OrdinalIgnoreCase))
            return false;
        return DiscoverProfiles().Any() || NoraSubscriptionStore.LoadAll().Any();
    }

    private UIElement WelcomeHomePage()
    {
        _connectButton = null;
        _heroGrid = null;
        _contentHost.ClipToBounds = false;
        _homeAtmosphereHost.Children.Clear();
        _homeAtmosphereHost.Children.Add(new Border
        {
            Background = NoraWpfTheme.Brush(Color.FromRgb(3, 7, 12)),
            IsHitTestVisible = false
        });

        var root = new Grid
        {
            ClipToBounds = true,
            Background = NoraWpfTheme.Brush(Color.FromRgb(3, 7, 12)),
            Margin = new Thickness(-23, -25, -23, -22)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _welcomeOverlayHost = new Grid();
        Grid.SetRowSpan(_welcomeOverlayHost, 2);
        Grid.SetZIndex(_welcomeOverlayHost, 40);

        var hero = new Grid
        {
            ClipToBounds = true,
            MinHeight = 492,
            Background = Brushes.Transparent
        };
        Grid.SetRow(hero, 0);
        root.Children.Add(hero);

        TranslateTransform? spaceParallax = null;
        TranslateTransform? planetParallax = null;
        var universeImage = LoadNamedVisualAsset("universe");
        var moonImage = LoadNamedVisualAsset("moon");
        if (universeImage is not null)
        {
            var spaceScale = new ScaleTransform(1.04, 1.04);
            var spaceShift = new TranslateTransform();
            spaceParallax = spaceShift;
            var spaceTransform = new TransformGroup();
            spaceTransform.Children.Add(spaceScale);
            spaceTransform.Children.Add(spaceShift);
            var space = new Border
            {
                Margin = new Thickness(-18, -14, -18, -70),
                RenderTransformOrigin = new Point(0.5, 0.26),
                RenderTransform = spaceTransform,
                Background = new ImageBrush(universeImage)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Top
                },
                IsHitTestVisible = false
            };
            hero.Children.Add(space);
            if (NoraWpfTheme.MotionEnabled)
            {
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                spaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.07, TimeSpan.FromSeconds(31)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
                spaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.07, TimeSpan.FromSeconds(31)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            }
        }

        if (moonImage is not null)
        {
            var planetGroup = new TransformGroup();
            var planetFloat = new TranslateTransform(0, -2);
            planetParallax = new TranslateTransform();
            planetGroup.Children.Add(planetFloat);
            planetGroup.Children.Add(planetParallax);

            var planetLayer = new Grid
            {
                Width = 304,
                Height = 304,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 0, 0),
                RenderTransformOrigin = new Point(0.5, 0.52),
                RenderTransform = planetGroup,
                ClipToBounds = false,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };
            var planetDisk = new Grid
            {
                Width = 282,
                Height = 282,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 0, 0),
                Clip = new EllipseGeometry(new Point(141, 141), 141, 141),
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };

            var planetGlow = new Ellipse
            {
                Width = 286,
                Height = 286,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 0),
                Fill = new RadialGradientBrush
                {
                    Center = new Point(0.52, 0.43),
                    GradientOrigin = new Point(0.52, 0.43),
                    RadiusX = 0.58,
                    RadiusY = 0.58,
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(30, 255, 156, 38), 0.58),
                        new GradientStop(Color.FromArgb(22, 255, 156, 38), 0.76),
                        new GradientStop(Colors.Transparent, 1)
                    }
                },
                IsHitTestVisible = false
            };
            Panel.SetZIndex(planetGlow, 1);
            planetLayer.Children.Add(planetGlow);

            var moon = new System.Windows.Controls.Image
            {
                Source = CropWelcomePlanetSource(moonImage),
                Stretch = Stretch.UniformToFill,
                Width = 282,
                Height = 282,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 1,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(moon, BitmapScalingMode.HighQuality);
            planetDisk.Children.Add(moon);
            Panel.SetZIndex(planetDisk, 2);
            planetLayer.Children.Add(planetDisk);
            Panel.SetZIndex(planetLayer, 2);
            hero.Children.Add(planetLayer);

            if (NoraWpfTheme.MotionEnabled)
            {
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                planetFloat.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, TimeSpan.FromSeconds(8.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
                planetGlow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.78, TimeSpan.FromSeconds(5.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            }
        }

        var probe = GetWelcomeParallaxProbe();
        if (probe is not null && planetParallax is not null)
            SetParallax(spaceParallax, planetParallax, probe.Value.X, probe.Value.Y);
        else if (planetParallax is not null)
            AttachWelcomeParallaxController(root, spaceParallax, planetParallax);

        hero.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(26, 2, 5, 9), 0),
                    new GradientStop(Color.FromArgb(34, 2, 5, 9), 0.36),
                    new GradientStop(Color.FromArgb(190, 4, 8, 13), 0.62),
                    new GradientStop(Color.FromArgb(248, 3, 7, 12), 0.84),
                    new GradientStop(Color.FromRgb(3, 7, 12), 1)
                }
            },
            IsHitTestVisible = false
        });

        var brandStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 34)
        };
        hero.Children.Add(brandStack);

        var wordmark = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            FontSize = 45,
            FontWeight = FontWeights.Bold,
            FontFamily = NoraWpfTheme.UiFont,
            Opacity = NoraWpfTheme.MotionEnabled ? 0 : 1
        };
        wordmark.Inlines.Add(new System.Windows.Documents.Run("N O R A  ") { Foreground = NoraWpfTheme.TextBrush });
        wordmark.Inlines.Add(new System.Windows.Documents.Run("V P N") { Foreground = NoraWpfTheme.OrangeBrush });
        brandStack.Children.Add(wordmark);

        var line = new NoraWelcomeLine
        {
            Width = 220,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 13, 0, 21),
            Opacity = NoraWpfTheme.MotionEnabled ? 0 : 1
        };
        brandStack.Children.Add(line);

        var subtitle = new TextBlock
        {
            Text = "Secure connection through your\nsubscription or config",
            FontSize = 20,
            LineHeight = 29,
            TextAlignment = TextAlignment.Center,
            Foreground = NoraWpfTheme.Brush(Color.FromRgb(188, 197, 214)),
            Opacity = NoraWpfTheme.MotionEnabled ? 0 : 1
        };
        brandStack.Children.Add(subtitle);

        if (NoraWpfTheme.MotionEnabled)
        {
            WelcomeFadeIn(wordmark, 260, 410, 10);
            WelcomeFadeIn(line, 720, 340, 0);
            WelcomeFadeIn(subtitle, 930, 380, 8);
        }

        var actionsShell = new Grid
        {
            Background = NoraWpfTheme.Brush(Color.FromRgb(3, 7, 12)),
            Margin = new Thickness(0, -1, 0, 0)
        };
        var actions = new StackPanel { Margin = new Thickness(42, 0, 42, 24) };
        Grid.SetRow(actionsShell, 1);
        actionsShell.Children.Add(actions);
        root.Children.Add(actionsShell);

        var add = WelcomeButton(
            "Add subscription or config",
            NoraIconKind.Plus,
            primary: true,
            async () =>
            {
                RenderPage(PageKind.Add);
                await Task.CompletedTask;
            });
        actions.Children.Add(add);

        var install = WelcomeButton(
            "Install KRot",
            NoraIconKind.Deploy,
            primary: false,
            async () => await ShowInstallDialogAsync());
        install.Margin = new Thickness(0, 14, 0, 0);
        actions.Children.Add(install);
        actions.Children.Add(new TextBlock
        {
            Text = "KRot is our proprietary VPN protocol,\ndesigned to resist DPI and support\nuser management.",
            FontSize = 14,
            LineHeight = 20,
            TextAlignment = TextAlignment.Center,
            Foreground = NoraWpfTheme.Brush(Color.FromRgb(159, 170, 192)),
            Margin = new Thickness(24, 10, 24, 15),
            TextWrapping = TextWrapping.Wrap
        });

        var empty = new NoraFxButton(Color.FromArgb(26, 255, 255, 255), Color.FromArgb(42, 255, 255, 255), 22, accent: false, stroke: NoraWpfTheme.Brush(Color.FromArgb(82, 146, 157, 176)))
        {
            Height = 58,
            Content = new TextBlock
            {
                Text = "I don't have anything yet",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = NoraWpfTheme.Brush(Color.FromRgb(173, 184, 205)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        empty.Click += (_, _) => ShowWelcomeInfoSheet();
        actions.Children.Add(empty);

        root.Children.Add(_welcomeOverlayHost);
        return root;
    }

    private WpfButton WelcomeButton(string text, NoraIconKind icon, bool primary, Func<Task> action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (primary)
        {
            content.Children.Add(new NoraIcon { Kind = icon, Width = 25, Height = 25, Stroke = NoraWpfTheme.BgBrush, Weight = 2.2, Margin = new Thickness(0, 1, 14, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            content.Children.Add(new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                BorderBrush = NoraWpfTheme.OrangeBrush,
                BorderThickness = new Thickness(2),
                Margin = new Thickness(0, 0, 16, 0),
                Child = new TextBlock
                {
                    Text = "K",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = NoraWpfTheme.OrangeBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = primary ? 22 : 20,
            FontWeight = FontWeights.Bold,
            Foreground = primary ? NoraWpfTheme.BgBrush : NoraWpfTheme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        var button = new NoraFxButton(
            primary ? NoraWpfTheme.Orange : Color.FromArgb(16, 255, 156, 38),
            primary ? NoraWpfTheme.Orange2 : Color.FromArgb(34, 255, 156, 38),
            22,
            accent: primary,
            stroke: primary ? NoraWpfTheme.Brush(Color.FromArgb(130, 255, 206, 108)) : NoraWpfTheme.OrangeBrush)
        {
            Height = primary ? 68 : 64,
            Content = content
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void AttachWelcomeParallaxController(FrameworkElement scene, TranslateTransform? spaceShift, TranslateTransform planetShift)
    {
        if (!NoraWpfTheme.MotionEnabled)
            return;

        double targetX = 0;
        double targetY = 0;
        double currentX = 0;
        double currentY = 0;
        var last = TimeSpan.MinValue;

        System.Windows.Input.MouseEventHandler moveHandler = (_, e) =>
        {
            var p = e.GetPosition(scene);
            targetX = scene.ActualWidth <= 0 ? 0 : Math.Clamp((p.X / scene.ActualWidth - 0.5) * 2, -1, 1);
            targetY = scene.ActualHeight <= 0 ? 0 : Math.Clamp((p.Y / scene.ActualHeight - 0.5) * 2, -1, 1);
        };
        System.Windows.Input.MouseEventHandler leaveHandler = (_, _) =>
        {
            targetX = 0;
            targetY = 0;
        };
        scene.AddHandler(UIElement.MouseMoveEvent, moveHandler, true);
        scene.AddHandler(UIElement.MouseLeaveEvent, leaveHandler, true);

        EventHandler? onRendering = null;
        onRendering = (_, args) =>
        {
            var now = args is RenderingEventArgs renderingArgs ? renderingArgs.RenderingTime : TimeSpan.Zero;
            var dt = last == TimeSpan.MinValue ? 1.0 / 60.0 : Math.Clamp((now - last).TotalSeconds, 0, 0.05);
            last = now;

            var settle = 1 - Math.Exp(-dt * 9.5);
            currentX += (targetX - currentX) * settle;
            currentY += (targetY - currentY) * settle;

            if (Math.Abs(currentX - targetX) < 0.0006)
                currentX = targetX;
            if (Math.Abs(currentY - targetY) < 0.0006)
                currentY = targetY;

            SetParallax(spaceShift, planetShift, currentX, currentY);
        };

        scene.Loaded += (_, _) =>
        {
            last = TimeSpan.MinValue;
            CompositionTarget.Rendering += onRendering;
        };
        scene.Unloaded += (_, _) => CompositionTarget.Rendering -= onRendering;
    }

    private static void SetParallax(TranslateTransform? spaceShift, TranslateTransform planetShift, double nx, double ny)
    {
        if (spaceShift is not null)
        {
            spaceShift.X = nx * 3;
            spaceShift.Y = ny * 2;
        }
        planetShift.X = nx * 12;
        planetShift.Y = ny * 8;
    }

    private static Point? GetWelcomeParallaxProbe()
    {
        var probe = Environment.GetEnvironmentVariable("NORA_WELCOME_PARALLAX")?.Trim().ToLowerInvariant();
        return probe switch
        {
            "left" => new Point(-1, 0),
            "right" => new Point(1, 0),
            "top" => new Point(0, -1),
            "bottom" => new Point(0, 1),
            "center" or "centre" or "0" => new Point(0, 0),
            _ => null
        };
    }

    private static ImageSource CropWelcomePlanetSource(ImageSource source)
    {
        if (source is not BitmapSource bitmap || bitmap.PixelWidth < 128 || bitmap.PixelHeight < 128)
            return source;

        var side = Math.Min(bitmap.PixelWidth, bitmap.PixelHeight);
        var inset = Math.Max(0, (int)Math.Round(side * 0.006));
        var rect = new Int32Rect(
            Math.Max(0, (bitmap.PixelWidth - side) / 2 + inset),
            Math.Max(0, (bitmap.PixelHeight - side) / 2 + inset),
            Math.Max(1, side - inset * 2),
            Math.Max(1, side - inset * 2));
        try
        {
            var cropped = new CroppedBitmap(bitmap, rect);
            if (cropped.CanFreeze)
                cropped.Freeze();
            return cropped;
        }
        catch
        {
            return source;
        }
    }

    private static void AnimateTransform(TranslateTransform transform, double x, double y, IEasingFunction ease)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    private static void WelcomeFadeIn(UIElement element, int delayMs, int durationMs, double y)
    {
        var shift = new TranslateTransform(0, y);
        element.RenderTransform = shift;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void ShowWelcomeInfoSheet()
    {
        var overlayHost = _welcomeOverlayHost ?? _contentHost;
        var overlay = new Grid
        {
            Background = NoraWpfTheme.Brush(Color.FromArgb(0, 0, 0, 0)),
            Opacity = NoraWpfTheme.MotionEnabled ? 0 : 1
        };
        Grid.SetZIndex(overlay, 50);

        var dim = new Border { Background = NoraWpfTheme.Brush(Color.FromArgb(116, 0, 0, 0)) };
        overlay.Children.Add(dim);

        var sheet = new Border
        {
            CornerRadius = new CornerRadius(24),
            Background = NoraWpfTheme.Brush(Color.FromArgb(248, 12, 16, 23)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(126, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(7, 0, 7, 10),
            RenderTransformOrigin = new Point(0.5, 1)
        };
        overlay.Children.Add(sheet);

        var body = new StackPanel();
        sheet.Child = body;
        body.Children.Add(new TextBlock
        {
            Text = "NORA VPN is a client application",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush
        });
        body.Children.Add(new TextBlock
        {
            Text = "We do not sell VPN subscriptions, issue access keys, or host public VPN servers. To start using NORA, you need valid connection data: a subscription link, a configuration file, an access key, or access to your own server. Once you have it, add it to the app and connect securely.",
            FontSize = 14,
            LineHeight = 21,
            TextWrapping = TextWrapping.Wrap,
            Foreground = NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 12, 0, 18)
        });

        var gotIt = new NoraFxButton(NoraWpfTheme.Orange, NoraWpfTheme.Orange2, 16, accent: true)
        {
            Height = 48,
            Content = new TextBlock { Text = "Got it", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.BgBrush, HorizontalAlignment = HorizontalAlignment.Center }
        };
        gotIt.Click += (_, _) => overlayHost.Children.Remove(overlay);
        body.Children.Add(gotIt);

        var import = new NoraFxButton(Colors.Transparent, Color.FromArgb(24, 255, 156, 38), 16, accent: false, stroke: Brushes.Transparent)
        {
            Height = 42,
            Margin = new Thickness(0, 8, 0, 0),
            Content = new TextBlock { Text = "Open import screen", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = NoraWpfTheme.OrangeBrush, HorizontalAlignment = HorizontalAlignment.Center }
        };
        import.Click += (_, _) =>
        {
            overlayHost.Children.Remove(overlay);
            RenderPage(PageKind.Add);
        };
        body.Children.Add(import);

        overlayHost.Children.Add(overlay);
        if (!NoraWpfTheme.MotionEnabled)
            return;
        var transform = new TransformGroup();
        var scale = new ScaleTransform(0.98, 0.98);
        var shift = new TranslateTransform(0, 40);
        transform.Children.Add(scale);
        transform.Children.Add(shift);
        sheet.RenderTransform = transform;
        overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, DoubleTo(1, 220));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, DoubleTo(1, 220));
        shift.BeginAnimation(TranslateTransform.YProperty, DoubleTo(0, 220));
    }

    private void ConfigureHomeAtmosphere()
    {
        var atmosphere = new Grid
        {
            ClipToBounds = true,
            IsHitTestVisible = false,
            OpacityMask = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(Colors.White, 0.36),
                    new GradientStop(Color.FromArgb(212, 255, 255, 255), 0.48),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.64)
                }
            }
        };

        _heroGrid = new NoraCyberGrid
        {
            Height = 430,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Energy = HeroEnergyFor(_state)
        };
        atmosphere.Children.Add(_heroGrid);

        var earth = LoadEarthImage();
        if (earth is not null)
        {
            atmosphere.Children.Add(new Border
            {
                Height = 270,
                Margin = new Thickness(0, 72, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = NoraWpfTheme.Brush(Color.FromRgb(255, 139, 32)),
                Opacity = 0.14,
                OpacityMask = new ImageBrush(earth)
                {
                    Stretch = Stretch.Uniform,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                },
                IsHitTestVisible = false
            });
        }

        atmosphere.Children.Add(new Border
        {
            Height = 450,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.34),
                GradientOrigin = new Point(0.5, 0.34),
                RadiusX = 0.78,
                RadiusY = 0.64,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 7, 10, 15), 0.28),
                    new GradientStop(Color.FromArgb(52, 7, 10, 15), 0.64),
                    new GradientStop(Color.FromArgb(184, 7, 10, 15), 1)
                }
            },
            IsHitTestVisible = false
        });

        _homeAtmosphereHost.Children.Add(atmosphere);

        if (_state == TunnelState.Connected &&
            string.Equals(Environment.GetEnvironmentVariable("NORA_GUI_SNAPSHOT_STATE"), "connected", StringComparison.OrdinalIgnoreCase))
        {
            atmosphere.Loaded += (_, _) => _heroGrid?.CelebrateConnected();
        }
    }

    private static double HeroEnergyFor(TunnelState state) => state switch
    {
        TunnelState.Connecting or TunnelState.Disconnecting => 1.0,
        TunnelState.Connected => 0.78,
        TunnelState.Failed => 0.30,
        _ => 0.5
    };

    private UIElement ActiveServerCard()
    {
        var server = CurrentServerInfo();
        var slideshowEnabled = NoraPremiumService.ServerSlideshowEffective;
        var images = LoadLocationImages(server.Country, slideshowEnabled);
        var cardBorder = new SolidColorBrush(Color.FromArgb(118, 255, 156, 38));
        var imageZoom = new ScaleTransform(1, 1, 0.5, 0.5);
        var card = new Border
        {
            Height = 198,
            CornerRadius = new CornerRadius(22),
            BorderBrush = cardBorder,
            BorderThickness = new Thickness(1),
            Background = NoraWpfTheme.CardBrush,
            Cursor = Cursors.Hand,
            ToolTip = "Open the server list",
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        card.MouseLeftButtonUp += (_, _) => RenderPage(PageKind.Servers);
        card.MouseEnter += (_, _) =>
        {
            if (!NoraWpfTheme.MotionEnabled)
                return;
            cardBorder.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(Color.FromArgb(205, 255, 169, 60), 190));
            imageZoom.BeginAnimation(ScaleTransform.ScaleXProperty, DoubleTo(1.05, 2600));
            imageZoom.BeginAnimation(ScaleTransform.ScaleYProperty, DoubleTo(1.05, 2600));
        };
        card.MouseLeave += (_, _) =>
        {
            cardBorder.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(Color.FromArgb(118, 255, 156, 38), 220));
            imageZoom.BeginAnimation(ScaleTransform.ScaleXProperty, DoubleTo(1.0, 900));
            imageZoom.BeginAnimation(ScaleTransform.ScaleYProperty, DoubleTo(1.0, 900));
        };
        var grid = new Grid { ClipToBounds = true };
        card.Child = grid;

        AddPhotoSlideshow(grid, images, imageZoom, new CornerRadius(21));

        // Two-directional scrim keeps every label readable on any photo.
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(21),
            Background = new LinearGradientBrush(
                Color.FromArgb(244, 6, 9, 14),
                Color.FromArgb(110, 6, 9, 14),
                new Point(0, 0.48),
                new Point(1, 0.5))
        });
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(21),
            Background = new LinearGradientBrush(
                Color.FromArgb(30, 6, 9, 14),
                Color.FromArgb(224, 6, 9, 14),
                new Point(0.5, 0),
                new Point(0.5, 1))
        });

        var content = new Grid { Margin = new Thickness(22, 17, 20, 17) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(content);

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var eyebrow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        eyebrow.Children.Add(new PulsingDot
        {
            Tone = server.Online ? NoraWpfTheme.Orange : NoraWpfTheme.Dim,
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(-2, 0, 7, 0)
        });
        eyebrow.Children.Add(new TextBlock
        {
            Text = "A C T I V E   N O D E",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = NoraWpfTheme.Brush(Color.FromRgb(226, 174, 110))
        });
        top.Children.Add(eyebrow);

        var countryPill = Pill(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new FlagIcon { Country = server.Country, Width = 24, Height = 17, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = server.Country, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = NoraWpfTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center }
            }
        });
        countryPill.Padding = new Thickness(10, 6, 12, 6);
        countryPill.Background = NoraWpfTheme.Brush(Color.FromArgb(178, 11, 16, 23));
        countryPill.BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(100, 255, 156, 38));
        countryPill.Margin = new Thickness(12, 0, 0, 0);
        countryPill.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(countryPill, 1);
        top.Children.Add(countryPill);
        content.Children.Add(top);

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        body.Children.Add(EmojiAwareTextBlock(server.Name, new TextBlock
        {
            FontSize = 27,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Left
        }));
        var endpoint = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 8, 0, 0) };
        endpoint.Children.Add(new NoraIcon
        {
            Kind = NoraIconKind.Pin,
            Width = 15,
            Height = 15,
            Stroke = NoraWpfTheme.Brush(Color.FromRgb(174, 185, 202)),
            Margin = new Thickness(0, 1, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var endpointReveal = new NoraFxButton(
            Colors.Transparent,
            Color.FromArgb(34, 255, 156, 38),
            10,
            accent: false,
            stroke: Brushes.Transparent)
        {
            Width = 28,
            Height = 25,
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = _showActiveEndpoint ? "Hide server IP" : "Show server IP",
            Content = new NoraIcon
            {
                Kind = NoraIconKind.Eye,
                Width = 16,
                Height = 16,
                Stroke = NoraWpfTheme.MutedBrush,
                Weight = 1.7
            }
        };
        endpointReveal.Click += (_, e) =>
        {
            e.Handled = true;
            _showActiveEndpoint = !_showActiveEndpoint;
            RenderPage(PageKind.Home);
        };
        endpoint.Children.Add(endpointReveal);
        if (_showActiveEndpoint)
        {
            endpoint.Children.Add(new TextBlock
            {
                Text = $"{server.Host}:{server.Port}",
                FontSize = 14,
                FontFamily = NoraWpfTheme.MonoFont,
                Foreground = NoraWpfTheme.Brush(Color.FromRgb(198, 207, 221)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            });
        }
        body.Children.Add(endpoint);
        Grid.SetRow(body, 1);
        content.Children.Add(body);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusPill = Pill(StatusLine(server.Online ? "Online" : "Offline", server.Online));
        statusPill.Padding = new Thickness(12, 7, 13, 7);
        statusPill.Background = NoraWpfTheme.Brush(Color.FromArgb(178, 11, 16, 23));
        footer.Children.Add(statusPill);

        var protocolStack = new StackPanel { Orientation = Orientation.Horizontal };
        protocolStack.Children.Add(new NoraIcon
        {
            Kind = NoraIconKind.Shield,
            Width = 15,
            Height = 15,
            Stroke = NoraWpfTheme.Brush(Color.FromRgb(244, 185, 105)),
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        protocolStack.Children.Add(new TextBlock
        {
            Text = server.Protocol.Replace("  •  ", " · "),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = NoraWpfTheme.Brush(Color.FromRgb(244, 185, 105)),
            VerticalAlignment = VerticalAlignment.Center
        });
        var protocolPill = Pill(protocolStack);
        protocolPill.Padding = new Thickness(12, 7, 13, 7);
        protocolPill.Background = NoraWpfTheme.Brush(Color.FromArgb(178, 11, 16, 23));
        protocolPill.BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(92, 255, 156, 38));
        protocolPill.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(protocolPill, 2);
        footer.Children.Add(protocolPill);
        Grid.SetRow(footer, 2);
        content.Children.Add(footer);
        return card;
    }

    private static void AddPhotoSlideshow(
        Grid host,
        IReadOnlyList<ImageSource> images,
        ScaleTransform imageZoom,
        CornerRadius cornerRadius)
    {
        if (images.Count == 0)
            return;

        ImageBrush PhotoBrush(ImageSource source) => new(source)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            RelativeTransform = imageZoom
        };

        var visiblePhoto = new Border
        {
            CornerRadius = cornerRadius,
            Background = PhotoBrush(images[0]),
            Opacity = 1,
            IsHitTestVisible = false
        };
        var hiddenPhoto = new Border
        {
            CornerRadius = cornerRadius,
            Background = PhotoBrush(images[0]),
            Opacity = 0,
            IsHitTestVisible = false
        };
        host.Children.Add(visiblePhoto);
        host.Children.Add(hiddenPhoto);

        if (images.Count <= 1)
            return;

        var index = 0;
        var interval = LocationSlideInterval();
        var fadeDuration = TimeSpan.FromMilliseconds(Math.Min(1800, interval.TotalMilliseconds * 0.65));
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) =>
        {
            index = (index + 1) % images.Count;
            hiddenPhoto.Background = PhotoBrush(images[index]);
            hiddenPhoto.Opacity = 0;
            hiddenPhoto.BeginAnimation(OpacityProperty, null);
            visiblePhoto.BeginAnimation(OpacityProperty, null);

            if (!NoraWpfTheme.MotionEnabled)
            {
                hiddenPhoto.Opacity = 1;
                visiblePhoto.Opacity = 0;
                (visiblePhoto, hiddenPhoto) = (hiddenPhoto, visiblePhoto);
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var fadeIn = new DoubleAnimation(0, 1, fadeDuration) { EasingFunction = ease };
            var fadeOut = new DoubleAnimation(1, 0, fadeDuration) { EasingFunction = ease };
            fadeIn.Completed += (_, _) =>
            {
                hiddenPhoto.BeginAnimation(OpacityProperty, null);
                visiblePhoto.BeginAnimation(OpacityProperty, null);
                hiddenPhoto.Opacity = 1;
                visiblePhoto.Opacity = 0;
                (visiblePhoto, hiddenPhoto) = (hiddenPhoto, visiblePhoto);
            };
            hiddenPhoto.BeginAnimation(OpacityProperty, fadeIn);
            visiblePhoto.BeginAnimation(OpacityProperty, fadeOut);
        };
        host.Loaded += (_, _) => timer.Start();
        host.Unloaded += (_, _) => timer.Stop();
    }

    private static Border Pill(UIElement child) => new()
    {
        Child = child,
        CornerRadius = new CornerRadius(18),
        Background = NoraWpfTheme.Brush(Color.FromArgb(174, 11, 16, 23)),
        BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(70, 146, 157, 176)),
        BorderThickness = new Thickness(1)
    };

    private static IReadOnlyList<ImageSource> LoadLocationImages(string country, bool loadAll)
    {
        var normalized = (country ?? "").Trim().ToLowerInvariant();
        var name = normalized switch
        {
            "russia" or "russian federation" => "RUSSIA",
            "united kingdom" or "great britain" or "england" => "UK",
            "united states" or "united states of america" or "usa" => "USA",
            "china" => "China",
            "estonia" => "Estonia",
            "finland" => "Finland",
            "france" => "France",
            "germany" => "Germany",
            "ireland" => "Ireland",
            "italy" => "Italy",
            "lithuania" => "Lithuania",
            "netherlands" or "holland" => "Netherlands",
            "portugal" => "Portugal",
            "spain" => "Spain",
            "universal" => "Universal",
            _ => (country ?? "").Trim()
        };
        var cacheKey = (name.Length == 0 ? "Universal" : name) + (loadAll ? ":all" : ":first");
        lock (ImageCacheSync)
        {
            if (LocationImageCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        IReadOnlyList<ImageSource> loaded = [];
        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "locations"),
            Path.Combine(AppContext.BaseDirectory, "futurelocationphotos"),
            Path.Combine(Directory.GetCurrentDirectory(), "futurelocationphotos")
        };
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var paths = FindLocationPaths(root, name);
            if (paths.Count == 0)
                continue;
            var selected = loadAll ? paths : paths.Take(1);
            loaded = selected.Select(LoadBitmap).Where(x => x is not null).Cast<ImageSource>().ToList();
            break;
        }

        if (loaded.Count == 0)
        {
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fallback = FindLocationPaths(root, "Universal").FirstOrDefault();
                if (fallback is not null && LoadBitmap(fallback) is { } image)
                {
                    loaded = [image];
                    break;
                }
            }
        }
        lock (ImageCacheSync)
            LocationImageCache[cacheKey] = loaded;
        return loaded;
    }

    private static List<string> FindLocationPaths(string root, string countryName)
    {
        if (!Directory.Exists(root) || string.IsNullOrWhiteSpace(countryName))
            return [];
        var matches = Directory.GetFiles(root, "*.png", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                Stem = Path.GetFileNameWithoutExtension(path)
            })
            .Where(x => x.Stem.Equals(countryName, StringComparison.OrdinalIgnoreCase) ||
                        Regex.IsMatch(x.Stem, "^" + Regex.Escape(countryName) + @"\d+$", RegexOptions.IgnoreCase))
            .ToList();
        if (matches.Count == 0)
            return [];

        var numbered = matches
            .Select(x => new
            {
                x.Path,
                x.Stem,
                Match = Regex.Match(x.Stem, "^" + Regex.Escape(countryName) + @"(?<n>\d+)$", RegexOptions.IgnoreCase)
            })
            .Where(x => x.Match.Success)
            .Select(x => new
            {
                x.Path,
                x.Stem,
                Number = int.Parse(x.Match.Groups["n"].Value, CultureInfo.InvariantCulture)
            })
            .ToList();
        if (numbered.Count > 0)
        {
            return numbered
                .OrderBy(x => x.Number)
                .ThenBy(x => x.Stem, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Path)
                .ToList();
        }

        return matches
            .OrderBy(x => x.Stem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Stem, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Path)
            .ToList();
    }

    private static TimeSpan LocationSlideInterval()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("NORA_LOCATION_SLIDESHOW_TEST_MS"), out var testMs))
            return TimeSpan.FromMilliseconds(Math.Clamp(testMs, 500, 10_000));
        return TimeSpan.FromMinutes(1);
    }

    private static ImageSource? LoadEarthImage()
        => LoadNamedVisualAsset("earth");

    private static ImageSource? LoadWelcomeImage()
    {
        return LoadNamedVisualAsset("welocmescreengirl");
    }

    private static ImageSource? LoadNamedVisualAsset(string assetName)
    {
        lock (ImageCacheSync)
        {
            if (NamedImageCache.TryGetValue(assetName, out var cached))
                return cached;
        }
        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "locations"),
            Path.Combine(AppContext.BaseDirectory, "assets", "atmosphere"),
            Path.Combine(AppContext.BaseDirectory, "futurelocationphotos"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "locations"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "atmosphere"),
            Path.Combine(Directory.GetCurrentDirectory(), "futurelocationphotos")
        };
        var extensions = new HashSet<string>(new[] { ".png", ".jpg", ".jpeg", ".webp" }, StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;
            var path = Directory.GetFiles(root)
                .FirstOrDefault(file =>
                    Path.GetFileNameWithoutExtension(file).Equals(assetName, StringComparison.OrdinalIgnoreCase) &&
                    extensions.Contains(Path.GetExtension(file)));
            if (path is not null)
            {
                var image = LoadBitmap(path);
                if (image is not null)
                {
                    lock (ImageCacheSync)
                        NamedImageCache[assetName] = image;
                }
                return image;
            }
        }
        return null;
    }

    private static ImageSource? LoadBitmap(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static UIElement CreateVoiceModeIcon(double size, Brush fill)
    {
        var geometry = LoadVoiceModeIconGeometry();
        if (geometry is null)
        {
            return new NoraIcon
            {
                Kind = NoraIconKind.Users,
                Width = size,
                Height = size,
                Stroke = fill
            };
        }

        return new System.Windows.Shapes.Path
        {
            Data = geometry,
            Fill = fill,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true
        };
    }

    private static Geometry? LoadVoiceModeIconGeometry()
    {
        lock (ImageCacheSync)
        {
            if (VoiceModeIconGeometry is not null)
                return VoiceModeIconGeometry;
        }

        try
        {
            var document = XDocument.Load(NoraDiscordModeSettings.VoiceAssetPath, LoadOptions.None);
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var element in document.Descendants().Where(x => x.Name.LocalName == "path"))
            {
                var data = element.Attribute("d")?.Value;
                if (!string.IsNullOrWhiteSpace(data))
                    group.Children.Add(Geometry.Parse(data));
            }

            if (group.Children.Count == 0)
                return null;

            group.Freeze();
            lock (ImageCacheSync)
                VoiceModeIconGeometry = group;
            return group;
        }
        catch
        {
            return null;
        }
    }

    private UIElement VoiceModePage()
    {
        var enabled = DiscordModeEnabledForView();
        var snapshot = string.Equals(Environment.GetEnvironmentVariable("NORA_GUI_VISUAL_READY"), "1", StringComparison.Ordinal);
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Routing Rules",
            FontSize = 40,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush
        });
        header.Children.Add(heading);
        var home = new NoraFxButton(Colors.Transparent, Color.FromArgb(36, 255, 156, 38), 14, false,
            NoraWpfTheme.Brush(Color.FromArgb(72, 146, 157, 176)))
        {
            Width = 46,
            Height = 46,
            VerticalAlignment = VerticalAlignment.Top,
            Content = new NoraIcon { Kind = NoraIconKind.Home, Width = 20, Height = 20, Stroke = NoraWpfTheme.MutedBrush },
            ToolTip = "Back to Home"
        };
        home.Click += (_, _) => RenderPage(PageKind.Home);
        Grid.SetColumn(home, 1);
        header.Children.Add(home);
        root.Children.Add(header);

        var scene = Card(24, enabled);
        scene.Height = 320;
        scene.Padding = new Thickness(24, 24, 24, 22);
        var sceneRoot = new Grid();
        sceneRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sceneRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sceneRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sceneRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Discord Mode",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush
        };
        sceneRoot.Children.Add(title);

        var explanation = new TextBlock
        {
            Text = "When you connect, only Discord traffic goes through the selected VPN server. Games, Steam, browsers and every other app keep using your normal internet connection.",
            FontSize = 14,
            LineHeight = 21,
            TextWrapping = TextWrapping.Wrap,
            Foreground = NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(explanation, 1);
        sceneRoot.Children.Add(explanation);

        var warning = new Border
        {
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(14, 12, 14, 12),
            VerticalAlignment = VerticalAlignment.Center,
            Background = NoraWpfTheme.Brush(Color.FromArgb(22, 255, 156, 38)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(68, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "AWG is not supported in Discord Mode. Choose a VLESS or KRot server before connecting.",
                FontSize = 12.5,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Foreground = NoraWpfTheme.Brush(Color.FromRgb(232, 190, 126))
            }
        };
        Grid.SetRow(warning, 2);
        sceneRoot.Children.Add(warning);

        var action = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        action.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        action.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var actionCopy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        actionCopy.Children.Add(new TextBlock
        {
            Text = enabled ? "MODE ON" : "MODE OFF",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = enabled ? NoraWpfTheme.GreenBrush : NoraWpfTheme.DimBrush
        });
        actionCopy.Children.Add(new TextBlock
        {
            Text = enabled ? "Turn it off to restore normal full-device VPN." : "Enable before connecting to a server.",
            FontSize = 12,
            Foreground = NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 3, 0, 0)
        });
        action.Children.Add(actionCopy);

        var toggle = BuildModeToggle(enabled);
        toggle.IsEnabled = !snapshot;
        toggle.Click += async (_, _) => await SetDiscordModeAsync(!enabled);
        Grid.SetColumn(toggle, 1);
        action.Children.Add(toggle);
        Grid.SetRow(action, 3);
        sceneRoot.Children.Add(action);

        scene.Child = sceneRoot;
        root.Children.Add(scene);
        return root;
    }

    private static NoraFxButton BuildModeToggle(bool enabled)
    {
        var button = new NoraFxButton(Colors.Transparent, Colors.Transparent, 17, false, Brushes.Transparent)
        {
            Width = 66,
            Height = 40,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = enabled ? "Turn Discord Mode off" : "Turn Discord Mode on"
        };
        var track = new Grid { Width = 60, Height = 34 };
        track.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(17),
            Background = NoraWpfTheme.Brush(enabled ? NoraWpfTheme.Green : Color.FromRgb(45, 53, 66)),
            BorderBrush = NoraWpfTheme.Brush(enabled ? Color.FromRgb(54, 224, 132) : Color.FromRgb(72, 83, 101)),
            BorderThickness = new Thickness(1)
        });
        track.Children.Add(new Ellipse
        {
            Width = 26,
            Height = 26,
            Fill = NoraWpfTheme.Brush(enabled ? NoraWpfTheme.Bg : Color.FromRgb(166, 176, 192)),
            HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(4)
        });
        button.Content = track;
        return button;
    }

    private UIElement SettingsPage()
    {
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var isPremium = NoraPremiumService.IsPremium;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Appearance",
            FontSize = 40,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Visual options for your NORA home screen.",
            FontSize = 15,
            Foreground = NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 6, 0, 0)
        });
        header.Children.Add(heading);
        var home = new NoraFxButton(Colors.Transparent, Color.FromArgb(36, 255, 156, 38), 14, false,
            NoraWpfTheme.Brush(Color.FromArgb(72, 146, 157, 176)))
        {
            Width = 46,
            Height = 46,
            VerticalAlignment = VerticalAlignment.Top,
            Content = new NoraIcon { Kind = NoraIconKind.Home, Width = 20, Height = 20, Stroke = NoraWpfTheme.MutedBrush },
            ToolTip = "Back to Home"
        };
        home.Click += (_, _) => RenderPage(PageKind.Home);
        Grid.SetColumn(home, 1);
        header.Children.Add(home);
        root.Children.Add(header);

        var statusCard = Card(20, isPremium);
        statusCard.Padding = new Thickness(18, 16, 18, 16);
        statusCard.Margin = new Thickness(0, 0, 0, 15);
        var statusGrid = new Grid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusText = new StackPanel();
        statusText.Children.Add(new TextBlock
        {
            Text = isPremium ? "Premium appearance is active" : "NORA Free",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush
        });
        var statusDetail = isPremium
            ? NoraPremiumService.Status == NoraPremiumStatus.OfflineGrace
                ? "Protected offline access · reconnect later to verify"
                : "Verified for this device."
            : "Core VPN functionality is complete and unrestricted.";
        statusText.Children.Add(new TextBlock
        {
            Text = statusDetail,
            FontSize = 12,
            Foreground = NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 5, 0, 0)
        });
        statusGrid.Children.Add(statusText);
        var edition = new Border
        {
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(10, 5, 10, 5),
            Background = NoraWpfTheme.Brush(isPremium ? Color.FromArgb(34, 255, 156, 38) : Color.FromArgb(34, 146, 157, 176)),
            BorderBrush = NoraWpfTheme.Brush(isPremium ? Color.FromArgb(110, 255, 156, 38) : Color.FromArgb(70, 146, 157, 176)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = isPremium ? "PREMIUM" : "FREE",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = isPremium ? NoraWpfTheme.OrangeBrush : NoraWpfTheme.MutedBrush
            }
        };
        Grid.SetColumn(edition, 1);
        statusGrid.Children.Add(edition);
        statusCard.Child = statusGrid;
        root.Children.Add(statusCard);

        root.Children.Add(new TextBlock
        {
            Text = "PREMIUM APPEARANCE",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.OrangeBrush,
            Margin = new Thickness(2, 3, 0, 9)
        });

        var codeField = Field("");
        codeField.Height = 46;
        codeField.FontFamily = NoraWpfTheme.MonoFont;
        codeField.FontSize = 13;
        codeField.CharacterCasing = System.Windows.Controls.CharacterCasing.Upper;
        codeField.ToolTip = "NORA-PRM-XXXX-XXXX-XXXX-XXXX-XXXX";

        UIElement FeatureRow(string key, string title, string description, bool enabled)
        {
            var card = Card(18, false);
            card.Padding = new Thickness(17, 14, 14, 14);
            card.Margin = new Thickness(0, 0, 0, 10);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = NoraWpfTheme.TextBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!isPremium)
            {
                titleRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(7, 3, 7, 3),
                    Margin = new Thickness(9, 0, 0, 0),
                    Background = NoraWpfTheme.Brush(Color.FromArgb(30, 255, 156, 38)),
                    Child = new TextBlock { Text = "PREMIUM", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.OrangeBrush }
                });
            }
            copy.Children.Add(titleRow);
            copy.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = NoraWpfTheme.MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 310,
                Margin = new Thickness(0, 5, 0, 0)
            });
            grid.Children.Add(copy);

            var toggle = new NoraFxButton(Colors.Transparent, Colors.Transparent, 17, false, Brushes.Transparent)
            {
                Width = 58,
                Height = 34,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = isPremium ? (enabled ? "Turn off" : "Turn on") : "Premium feature"
            };
            var track = new Grid { Width = 54, Height = 30 };
            track.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(15),
                Background = NoraWpfTheme.Brush(enabled && isPremium ? NoraWpfTheme.Orange : Color.FromRgb(45, 53, 66)),
                BorderBrush = NoraWpfTheme.Brush(enabled && isPremium ? NoraWpfTheme.Orange2 : Color.FromRgb(72, 83, 101)),
                BorderThickness = new Thickness(1)
            });
            track.Children.Add(new Ellipse
            {
                Width = 24,
                Height = 24,
                Fill = NoraWpfTheme.Brush(enabled && isPremium ? NoraWpfTheme.Bg : Color.FromRgb(166, 176, 192)),
                HorizontalAlignment = enabled && isPremium ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(3)
            });
            toggle.Content = track;
            toggle.Click += (_, _) =>
            {
                if (!NoraPremiumService.IsPremium)
                {
                    _pendingPremiumFeature = key;
                    ShowPremiumGate(title, codeField);
                    return;
                }

                if (key == "equalizer")
                    NoraPremiumService.SetEqualizerEnabled(!enabled);
                else
                    NoraPremiumService.SetServerSlideshowEnabled(!enabled);
                RenderPage(PageKind.Settings);
            };
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
            card.Child = grid;
            return card;
        }

        root.Children.Add(FeatureRow(
            "equalizer",
            "Audio visualizer",
            "Classic spectrum bars behind the Connect button, driven by system audio.",
            NoraPremiumService.EqualizerEnabled));
        root.Children.Add(FeatureRow(
            "slideshow",
            "Active server slideshow",
            "Crossfades the available location photos on the active server card.",
            NoraPremiumService.ServerSlideshowEnabled));

        var activationCard = Card(20, false);
        activationCard.Padding = new Thickness(18, 16, 18, 16);
        activationCard.Margin = new Thickness(0, 5, 0, 0);
        var activation = new StackPanel();
        activation.Children.Add(new TextBlock
        {
            Text = isPremium ? "Use another Premium code" : "Activate Premium appearance",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush
        });
        activation.Children.Add(new TextBlock
        {
            Text = "Enter the code exactly as received. Activation is verified securely online.",
            FontSize = 12,
            Foreground = NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 5, 0, 10)
        });
        activation.Children.Add(codeField);

        var activate = NoraButton(isPremium ? "Verify new code" : "Activate Premium", accent: true);
        activate.Height = 46;
        async Task ActivateCodeAsync()
        {
            activate.IsEnabled = false;
            activate.Content = "Verifying…";
            var result = await NoraPremiumService.ActivateAsync(codeField.Text);
            if (!result.Success)
            {
                activate.IsEnabled = true;
                activate.Content = isPremium ? "Verify new code" : "Activate Premium";
                ShowToast("Premium activation failed", result.Message, ToastKind.Error);
                return;
            }

            if (_pendingPremiumFeature == "equalizer")
                NoraPremiumService.SetEqualizerEnabled(true);
            else if (_pendingPremiumFeature == "slideshow")
                NoraPremiumService.SetServerSlideshowEnabled(true);
            _pendingPremiumFeature = "";
            ShowToast("Premium appearance active", "The visual options are now available on this device.", ToastKind.Success);
            RenderPage(PageKind.Settings);
        }
        activate.Click += async (_, _) => await ActivateCodeAsync();
        codeField.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || !activate.IsEnabled)
                return;
            e.Handled = true;
            await ActivateCodeAsync();
        };
        activation.Children.Add(activate);

        var contact = new NoraFxButton(Colors.Transparent, Color.FromArgb(24, 255, 156, 38), 12, false, Brushes.Transparent)
        {
            Height = 34,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock
            {
                Text = "Get a code: @RunwayResearch",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = NoraWpfTheme.OrangeBrush
            },
            ToolTip = "Copy Telegram contact"
        };
        contact.Click += (_, _) =>
        {
            WpfClipboard.SetText("@RunwayResearch");
            ShowToast("Telegram copied", "@RunwayResearch", ToastKind.Success);
        };
        activation.Children.Add(contact);
        activationCard.Child = activation;
        root.Children.Add(activationCard);

        root.Children.Add(new TextBlock
        {
            Text = "Premium changes appearance only. VPN speed, security, protocols, servers and connection reliability remain identical for every user.",
            FontSize = 11,
            LineHeight = 17,
            TextWrapping = TextWrapping.Wrap,
            Foreground = NoraWpfTheme.DimBrush,
            Margin = new Thickness(7, 12, 7, 8),
            TextAlignment = TextAlignment.Center
        });

        return ScrollPage(root);
    }

    private static ImageSource? LoadBrandWindowIcon()
    {
        const string cacheKey = "nora-brand-window-icon";
        lock (ImageCacheSync)
        {
            if (NamedImageCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        // Prefer the dedicated transparent Windows icon.  The high-resolution
        // PNG remains the fallback/portable visual source; SVG is carried as the
        // canonical vector asset for non-WPF consumers.
        foreach (var path in BrandIconPaths().Concat(BrandPngPaths()))
        {
            if (!File.Exists(path))
                continue;
            var image = LoadBitmap(path);
            if (image is null)
                continue;
            lock (ImageCacheSync)
                NamedImageCache[cacheKey] = image;
            return image;
        }
        return null;
    }

    private static IEnumerable<string> BrandIconPaths()
        => new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "logo.ico"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "logo.ico")
        }.Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> BrandTaskbarIconPaths()
        => new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "logo-taskbar.ico"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "logo-taskbar.ico")
        }.Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> BrandPngPaths()
        => new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "logo.png")
        }.Distinct(StringComparer.OrdinalIgnoreCase);

    // Same accent mapping the Connect button uses, so the audio spectrum and the
    // button always share one color per tunnel state.
    private Color ConnectAccentFor(TunnelState state) => state switch
    {
        TunnelState.Connected when DiscordModeEnabledForView() => DiscordModeAccent,
        TunnelState.Connected => NoraWpfTheme.Green,
        TunnelState.Failed => NoraWpfTheme.Red,
        TunnelState.Connecting or TunnelState.Disconnecting => NoraWpfTheme.Orange2,
        _ => NoraWpfTheme.Orange
    };

    private void ShowPremiumGate(string featureName, TextBox codeField)
    {
        var overlay = new Grid
        {
            Background = NoraWpfTheme.Brush(Color.FromArgb(146, 0, 0, 0)),
            Opacity = NoraWpfTheme.MotionEnabled ? 0 : 1
        };
        Grid.SetZIndex(overlay, 80);

        var sheet = new Border
        {
            CornerRadius = new CornerRadius(24),
            Background = NoraWpfTheme.Brush(Color.FromArgb(252, 12, 16, 23)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(138, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22),
            Margin = new Thickness(8, 0, 8, 12),
            VerticalAlignment = VerticalAlignment.Bottom,
            RenderTransformOrigin = new Point(0.5, 1)
        };
        overlay.Children.Add(sheet);
        var body = new StackPanel();
        sheet.Child = body;
        body.Children.Add(new TextBlock
        {
            Text = featureName + " is a Premium appearance feature",
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = "Premium unlocks optional visuals only. It does not change VPN speed, security, available protocols, servers or connection quality.",
            FontSize = 13,
            LineHeight = 20,
            Foreground = NoraWpfTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 11, 0, 17)
        });
        var enter = NoraButton("Enter a code", accent: true);
        enter.Height = 48;
        enter.Click += (_, _) =>
        {
            _contentHost.Children.Remove(overlay);
            codeField.Focus();
            codeField.SelectAll();
        };
        body.Children.Add(enter);
        var later = new NoraFxButton(Colors.Transparent, Color.FromArgb(24, 255, 255, 255), 14, false, Brushes.Transparent)
        {
            Height = 40,
            Margin = new Thickness(0, 6, 0, 0),
            Content = new TextBlock { Text = "Not now", Foreground = NoraWpfTheme.MutedBrush, FontWeight = FontWeights.SemiBold }
        };
        later.Click += (_, _) =>
        {
            _pendingPremiumFeature = "";
            _contentHost.Children.Remove(overlay);
        };
        body.Children.Add(later);
        _contentHost.Children.Add(overlay);

        if (!NoraWpfTheme.MotionEnabled)
            return;
        var shift = new TranslateTransform(0, 42);
        sheet.RenderTransform = shift;
        overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        shift.BeginAnimation(TranslateTransform.YProperty, DoubleTo(0, 240));
    }

    private UIElement ServersPage()
    {
        var root = new StackPanel();
        var radar = new NoraIcon { Kind = NoraIconKind.Radar, Width = 24, Height = 24, Stroke = NoraWpfTheme.OrangeBrush };
        if (_isPinging && NoraWpfTheme.MotionEnabled)
        {
            var spin = new RotateTransform();
            radar.RenderTransformOrigin = new Point(0.5, 0.53);
            radar.RenderTransform = spin;
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        root.Children.Add(ServersPageTitle(radar));

        var list = new StackPanel { Margin = new Thickness(0, 16, 0, 14) };
        var subscriptions = NoraSubscriptionStore.LoadAll();
        for (var index = 0; index < subscriptions.Count; index++)
            list.Children.Add(SubscriptionCard(subscriptions[index], index, subscriptions.Count));

        var profiles = DiscoverProfiles().ToList();
        if (profiles.Count > 0)
            list.Children.Add(DirectProfilesCard(profiles));
        if (profiles.Count == 0 && subscriptions.Count == 0)
            list.Children.Add(EmptyState("No servers yet. Import a key, subscription URL, or install KRot on a VPS."));
        root.Children.Add(list);

        var use = NoraButton(_isArrangingServers ? "Done arranging" : "Use selected", accent: true);
        use.IsEnabled = !_isArrangingServers;
        use.Opacity = _isArrangingServers ? 0.42 : 1;
        use.Click += (_, _) => RenderPage(PageKind.Home);
        root.Children.Add(use);
        return ScrollPage(root);
    }

    private UIElement ServersPageTitle(NoraIcon radar)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = "Servers", FontSize = 42, FontWeight = FontWeights.Bold });
        text.Children.Add(new TextBlock
        {
            Text = _isArrangingServers
                ? "Move subscriptions and servers. Changes save instantly."
                : _isPinging ? "Measuring latency..." : "Pick a node - the change applies instantly.",
            FontSize = 16,
            Foreground = _isArrangingServers ? NoraWpfTheme.Brush(Color.FromRgb(224, 181, 112)) : NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        grid.Children.Add(text);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var arrange = new NoraFxButton(
            _isArrangingServers ? Color.FromArgb(44, 255, 156, 38) : Color.FromArgb(16, 255, 156, 38),
            _isArrangingServers ? Color.FromArgb(68, 255, 184, 74) : Color.FromArgb(46, 255, 156, 38),
            18,
            accent: _isArrangingServers,
            stroke: NoraWpfTheme.Brush(NoraWpfTheme.With(_isArrangingServers ? NoraWpfTheme.Orange2 : NoraWpfTheme.Orange, 130)))
        {
            Content = new NoraIcon
            {
                Kind = _isArrangingServers ? NoraIconKind.Check : NoraIconKind.Pencil,
                Width = 20,
                Height = 20,
                Stroke = _isArrangingServers ? NoraWpfTheme.BgBrush : NoraWpfTheme.OrangeBrush,
                Weight = 2.0
            },
            Width = 46,
            Height = 50,
            ToolTip = _isArrangingServers ? "Done arranging" : "Arrange subscriptions and servers"
        };
        arrange.Click += (_, _) =>
        {
            _isArrangingServers = !_isArrangingServers;
            RenderPage(PageKind.Servers);
        };
        actions.Children.Add(arrange);

        var ping = new NoraFxButton(Color.FromArgb(20, 255, 156, 38), Color.FromArgb(46, 255, 156, 38), 18, accent: true)
        {
            Content = radar,
            Width = 50,
            Height = 50,
            Foreground = NoraWpfTheme.OrangeBrush,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Check latency"
        };
        ping.Click += async (_, _) => await PingAllAsync();
        actions.Children.Add(ping);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private static UIElement GroupLabel(string text, string? detail = null)
    {
        var grid = new Grid { Margin = new Thickness(4, 0, 4, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.Brush(Color.FromRgb(196, 152, 96)),
            VerticalAlignment = VerticalAlignment.Center
        });
        var rule = new Border
        {
            Height = 1,
            Margin = new Thickness(10, 1, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush(
                Color.FromArgb(80, 255, 156, 38),
                Color.FromArgb(0, 255, 156, 38),
                new Point(0, 0.5), new Point(1, 0.5))
        };
        Grid.SetColumn(rule, 1);
        grid.Children.Add(rule);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var right = new TextBlock
            {
                Text = detail,
                FontSize = 11,
                Foreground = NoraWpfTheme.DimBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
        }
        return grid;
    }

    private UIElement SubscriptionCard(NoraSubscriptionInfo sub, int subscriptionIndex, int subscriptionCount)
    {
        var expanded = string.Equals(_expandedSubscriptionId, sub.Id, StringComparison.OrdinalIgnoreCase);
        var hasActive = _activeSubscriptionServer is not null && string.Equals(_activeSubscriptionServer.SubscriptionId, sub.Id, StringComparison.OrdinalIgnoreCase);
        var card = Card(20, expanded || hasActive);
        card.Margin = new Thickness(0, 0, 0, 14);
        card.Padding = new Thickness(14);
        card.Cursor = Cursors.Hand;

        var panel = new StackPanel();
        card.Child = panel;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(13),
            Background = NoraWpfTheme.Brush(Color.FromArgb(34, 255, 156, 38)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(90, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new NoraIcon { Kind = NoraIconKind.Subscription, Width = 21, Height = 21, Stroke = NoraWpfTheme.OrangeBrush }
        };
        header.Children.Add(badge);

        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(EmojiAwareTextBlock(sub.Title, new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 20,
            MaxHeight = 42,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = sub.Title
        }));

        var meta = $"{sub.Servers.Count} servers";
        if (sub.ExpireUnix > 0)
        {
            var left = DateTimeOffset.FromUnixTimeSeconds(sub.ExpireUnix) - DateTimeOffset.UtcNow;
            meta += left.TotalDays > 0 ? $" · {Math.Max(1, (int)left.TotalDays)} days left" : " · expired";
        }
        if (sub.UpdateIntervalHours > 0)
            meta += $" · refresh {sub.UpdateIntervalHours}h";
        text.Children.Add(new TextBlock
        {
            Text = meta,
            FontSize = 12,
            Foreground = NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(text, 1);
        header.Children.Add(text);

        var chevron = new NoraIcon
        {
            Kind = NoraIconKind.ChevronRight,
            Width = 18,
            Height = 18,
            Stroke = NoraWpfTheme.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 6, 0),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var chevronSpin = new RotateTransform(expanded ? 90 : 0);
        chevron.RenderTransform = chevronSpin;
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };
        if (_isArrangingServers && subscriptionCount > 1)
        {
            actions.Children.Add(ReorderStepper(
                subscriptionIndex > 0,
                subscriptionIndex < subscriptionCount - 1,
                async direction => await MoveSubscriptionAsync(sub, direction),
                "Move subscription"));
        }
        else
        {
            var refresh = IconActionButton(NoraIconKind.Refresh, "Refresh subscription");
            refresh.Click += async (_, e) =>
            {
                e.Handled = true;
                await RefreshSubscriptionAsync(sub);
            };
            actions.Children.Add(refresh);
            var remove = IconActionButton(NoraIconKind.Trash, "Delete subscription", danger: true);
            remove.Click += async (_, e) =>
            {
                e.Handled = true;
                await DeleteSubscriptionAsync(sub);
            };
            actions.Children.Add(remove);
        }
        Grid.SetColumn(actions, 2);
        header.Children.Add(actions);

        if (!_isArrangingServers)
        {
            Grid.SetColumn(chevron, 3);
            header.Children.Add(chevron);
        }
        panel.Children.Add(header);

        // Usage meter
        if (sub.TotalBytes > 0)
        {
            var usedBytes = Math.Max(0, sub.UploadBytes + sub.DownloadBytes);
            var ratio = Math.Clamp((double)usedBytes / sub.TotalBytes, 0, 1);
            var meter = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            meter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            meter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var track = new Border
            {
                Height = 5,
                CornerRadius = new CornerRadius(3),
                Background = NoraWpfTheme.Brush(Color.FromArgb(70, 43, 52, 65)),
                VerticalAlignment = VerticalAlignment.Center
            };
            var fillScale = new ScaleTransform(NoraWpfTheme.MotionEnabled ? 0 : ratio, 1);
            var fill = new Border
            {
                Height = 5,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = fillScale,
                Background = new LinearGradientBrush(
                    ratio > 0.85 ? NoraWpfTheme.Red : NoraWpfTheme.Orange,
                    ratio > 0.85 ? Color.FromRgb(255, 130, 110) : NoraWpfTheme.Orange2,
                    new Point(0, 0.5), new Point(1, 0.5))
            };
            if (NoraWpfTheme.MotionEnabled)
            {
                fill.Loaded += (_, _) => fillScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(ratio, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = TimeSpan.FromMilliseconds(120) });
            }
            var trackHost = new Grid();
            trackHost.Children.Add(track);
            trackHost.Children.Add(fill);
            meter.Children.Add(trackHost);
            var usage = new TextBlock
            {
                Text = $"{FormatBytes(usedBytes)} / {FormatBytes(sub.TotalBytes)}",
                FontSize = 11,
                Foreground = NoraWpfTheme.MutedBrush,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(usage, 1);
            meter.Children.Add(usage);
            panel.Children.Add(meter);
        }

        if (!string.IsNullOrWhiteSpace(sub.Announce))
        {
            var announce = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
            announce.Children.Add(new NoraIcon
            {
                Kind = NoraIconKind.Bolt,
                Width = 13,
                Height = 13,
                Stroke = NoraWpfTheme.OrangeBrush,
                Margin = new Thickness(0, 1, 6, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
            announce.Children.Add(EmojiAwareTextBlock(sub.Announce, new TextBlock
            {
                FontSize = 11.5,
                Foreground = NoraWpfTheme.OrangeBrush,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 360
            }));
            panel.Children.Add(announce);
        }

        card.MouseLeftButtonUp += (_, _) =>
        {
            if (_isArrangingServers)
                return;
            _expandedSubscriptionId = expanded ? "" : sub.Id;
            RenderPage(PageKind.Servers);
        };

        var wiggleSubscription = _isArrangingServers && subscriptionCount > 1;
        if (wiggleSubscription)
            ApplyArrangementWiggle(card, subscriptionIndex, isCard: true);

        if (expanded)
        {
            var listHost = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            listHost.Children.Add(new Border
            {
                Height = 1,
                Background = NoraWpfTheme.Brush(Color.FromArgb(150, 43, 52, 65)),
                Margin = new Thickness(-14, 0, -14, 4)
            });
            var index = 0;
            foreach (var server in sub.Servers)
                listHost.Children.Add(ServerRow(
                    server.Name,
                    $"{server.Host}:{server.Port}",
                    ShortProtocol(server.Protocol, server.Flow),
                    server.Country,
                    _activeSubscriptionServer is not null && string.Equals(_activeSubscriptionServer.LocalPath, server.LocalPath, StringComparison.OrdinalIgnoreCase),
                    _ping.TryGetValue(server.LocalPath, out var p) ? p : PingStatus.Unknown,
                    index++,
                    async () => await ActivateSubscriptionServerAsync(server),
                    async () => await DeleteSubscriptionServerAsync(server),
                    _isArrangingServers && sub.Servers.Count > 1,
                    async direction => await MoveSubscriptionServerAsync(sub, server, direction),
                    index > 1,
                    index < sub.Servers.Count));
            panel.Children.Add(listHost);
        }

        return wiggleSubscription ? ArrangementSafeHost(card) : card;
    }

    private UIElement DirectProfilesCard(List<ProfileListItem> profiles)
    {
        var host = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        host.Children.Add(GroupLabel("DIRECT PROFILES", profiles.Count == 1 ? "1 profile" : $"{profiles.Count} profiles"));
        var index = 0;
        foreach (var item in profiles)
        {
            var row = ServerRow(
                item.Name,
                $"{item.Host}:{item.Port}",
                item.Protocol,
                item.Country,
                string.Equals(item.Path, _activeProfilePath, StringComparison.OrdinalIgnoreCase),
                _ping.TryGetValue(item.Path, out var p) ? p : PingStatus.Unknown,
                index++,
                async () => await ActivateProfileAsync(item.Path),
                async () => await DeleteProfileAsync(item));
            if (row is FrameworkElement fe)
                fe.Margin = new Thickness(0, index == 1 ? 0 : 8, 0, 0);
            host.Children.Add(row);
        }
        return host;
    }

    private async Task RefreshSubscriptionAsync(NoraSubscriptionInfo sub)
    {
        if (_state is TunnelState.Connecting or TunnelState.Disconnecting)
        {
            ShowToast("Action in progress", "Wait until the current tunnel operation finishes.", ToastKind.Info);
            return;
        }

        try
        {
            ShowToast("Refreshing subscription", $"Updating {sub.Title}...", ToastKind.Info);
            var refreshed = await NoraSubscriptionStore.RefreshAsync(sub, AppendLog);
            _expandedSubscriptionId = refreshed.Id;
            if (_activeSubscriptionServer is not null &&
                string.Equals(_activeSubscriptionServer.SubscriptionId, sub.Id, StringComparison.OrdinalIgnoreCase))
            {
                var replacement = refreshed.Servers.FirstOrDefault(x => string.Equals(x.Id, _activeSubscriptionServer.Id, StringComparison.OrdinalIgnoreCase))
                                  ?? refreshed.Servers.FirstOrDefault();
                if (replacement is not null)
                    SetActiveSubscriptionServer(replacement);
            }
            RenderPage(PageKind.Servers);
            ShowToast("Subscription updated", $"{refreshed.Title}: {refreshed.Servers.Count} servers loaded.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.ImportProfile, ex);
        }
    }

    private async Task DeleteSubscriptionAsync(NoraSubscriptionInfo sub)
    {
        if (_state is TunnelState.Connecting or TunnelState.Disconnecting)
        {
            ShowToast("Action in progress", "Wait until the current tunnel operation finishes.", ToastKind.Info);
            return;
        }

        var confirm = WpfMessageBox.Show(
            $"Delete subscription `{sub.Title}`?\n\nThis removes all servers imported from this subscription from NORA VPN.",
            "NORA VPN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var deletingActive = _activeSubscriptionServer is not null &&
                                 string.Equals(_activeSubscriptionServer.SubscriptionId, sub.Id, StringComparison.OrdinalIgnoreCase);
            if (deletingActive && _state == TunnelState.Connected && !await DisconnectAsync(NoraOperation.Disconnect))
                return;

            NoraSubscriptionStore.DeleteSubscription(sub);
            if (string.Equals(_expandedSubscriptionId, sub.Id, StringComparison.OrdinalIgnoreCase))
                _expandedSubscriptionId = "";
            if (deletingActive)
                SelectFirstAvailableProfile();
            RenderPage(PageKind.Servers);
            ShowToast("Subscription deleted", sub.Title, ToastKind.Success);
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.ImportProfile, ex);
        }
    }

    private async Task DeleteSubscriptionServerAsync(NoraSubscriptionServer server)
    {
        if (_state is TunnelState.Connecting or TunnelState.Disconnecting)
        {
            ShowToast("Action in progress", "Wait until the current tunnel operation finishes.", ToastKind.Info);
            return;
        }

        var confirm = WpfMessageBox.Show(
            $"Delete server `{server.Name}`?\n\nThis removes the server from the local subscription cache. It does not change the provider account.",
            "NORA VPN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var deletingActive = _activeSubscriptionServer is not null &&
                                 string.Equals(_activeSubscriptionServer.LocalPath, server.LocalPath, StringComparison.OrdinalIgnoreCase);
            if (deletingActive && _state == TunnelState.Connected && !await DisconnectAsync(NoraOperation.Disconnect))
                return;

            NoraSubscriptionStore.DeleteServer(server);
            _ping.Remove(server.LocalPath);
            if (deletingActive)
                SelectFirstAvailableProfile();
            RenderPage(PageKind.Servers);
            ShowToast("Server deleted", server.Name, ToastKind.Success);
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.ImportProfile, ex);
        }
    }

    private async Task DeleteProfileAsync(ProfileListItem profile)
    {
        if (_state is TunnelState.Connecting or TunnelState.Disconnecting)
        {
            ShowToast("Action in progress", "Wait until the current tunnel operation finishes.", ToastKind.Info);
            return;
        }

        var confirm = WpfMessageBox.Show(
            $"Delete server `{profile.Name}`?\n\nThis removes the local profile from NORA VPN. Remote VPS services are not uninstalled.",
            "NORA VPN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var deletingActive = string.Equals(_activeProfilePath, profile.Path, StringComparison.OrdinalIgnoreCase) &&
                                 _activeSubscriptionServer is null;
            if (deletingActive && _state == TunnelState.Connected && !await DisconnectAsync(NoraOperation.Disconnect))
                return;

            DeleteProfileFiles(profile.Path);
            _ping.Remove(profile.Path);
            if (deletingActive)
                SelectFirstAvailableProfile();
            RenderPage(PageKind.Servers);
            ShowToast("Server deleted", profile.Name, ToastKind.Success);
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.LoadProfile, ex);
        }
    }

    private async Task ActivateSubscriptionServerAsync(NoraSubscriptionServer server)
    {
        if (_state is TunnelState.Connecting or TunnelState.Disconnecting)
        {
            ShowToast("Action in progress", "Wait until the current tunnel operation finishes.", ToastKind.Info);
            return;
        }

        var alreadySelected = _activeSubscriptionServer is not null &&
                              string.Equals(_activeSubscriptionServer.LocalPath, server.LocalPath, StringComparison.OrdinalIgnoreCase);
        if (alreadySelected)
        {
            RenderPage(PageKind.Servers);
            return;
        }

        var shouldReconnect = _state == TunnelState.Connected;
        if (shouldReconnect)
        {
            ShowToast("Switching server", $"Reconnecting to {server.Name}...", ToastKind.Info);
            if (!await DisconnectAsync(NoraOperation.SwitchServer))
                return;
        }

        SetActiveSubscriptionServer(server);
        RenderPage(PageKind.Servers);

        if (shouldReconnect)
            await ConnectAsync();
    }

    private async Task ActivateProfileAsync(string path)
    {
        if (_state is TunnelState.Connecting or TunnelState.Disconnecting)
        {
            ShowToast("Action in progress", "Wait until the current tunnel operation finishes.", ToastKind.Info);
            return;
        }

        var alreadySelected = string.Equals(_activeProfilePath, path, StringComparison.OrdinalIgnoreCase) &&
                              _activeSubscriptionServer is null;
        if (alreadySelected)
        {
            RenderPage(PageKind.Servers);
            return;
        }

        var shouldReconnect = _state == TunnelState.Connected;
        if (shouldReconnect)
        {
            var profileName = ProfileListItem.From(path, _activeProfilePath).Name;
            ShowToast("Switching server", $"Reconnecting to {profileName}...", ToastKind.Info);
            if (!await DisconnectAsync(NoraOperation.SwitchServer))
                return;
        }

        SetActiveProfile(path);
        RenderPage(PageKind.Servers);

        if (shouldReconnect)
            await ConnectAsync();
    }

    private static string ShortProtocol(string protocol, string flow)
    {
        var p = string.IsNullOrWhiteSpace(protocol) ? "VLESS" : protocol;
        if (flow.Contains("vision", StringComparison.OrdinalIgnoreCase))
            p += " · vision";
        return p;
    }

    // One compact selectable server row used by subscriptions and direct profiles.
    private UIElement ServerRow(
        string name,
        string endpoint,
        string protocol,
        string country,
        bool selected,
        PingStatus ping,
        int index,
        Func<Task> select,
        Func<Task>? delete = null,
        bool arranging = false,
        Func<int, Task>? move = null,
        bool canMoveUp = false,
        bool canMoveDown = false)
    {
        var baseBg = selected ? Color.FromArgb(38, 255, 156, 38) : Color.FromArgb(0, 27, 34, 45);
        var hoverBg = selected ? Color.FromArgb(52, 255, 156, 38) : Color.FromArgb(170, 27, 34, 45);
        var bg = new SolidColorBrush(baseBg);
        var row = new Border
        {
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(11, 9, 11, 9),
            Margin = new Thickness(0, 6, 0, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = selected ? NoraWpfTheme.Brush(Color.FromArgb(160, 255, 156, 38)) : Brushes.Transparent,
            Background = bg,
            Cursor = Cursors.Hand
        };
        row.MouseEnter += (_, _) =>
        {
            if (!arranging)
                bg.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(hoverBg, 140));
        };
        row.MouseLeave += (_, _) =>
        {
            if (!arranging)
                bg.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(baseBg, 220));
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Child = grid;

        grid.Children.Add(new FlagIcon
        {
            Country = country,
            Width = 28,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) };
        Grid.SetColumn(info, 1);
        info.Children.Add(EmojiAwareTextBlock(name, new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        }));
        var detail = new TextBlock
        {
            FontSize = 11,
            Foreground = NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 2.5, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        detail.Inlines.Add(new System.Windows.Documents.Run(endpoint) { FontFamily = NoraWpfTheme.MonoFont });
        detail.Inlines.Add(new System.Windows.Documents.Run("   " + protocol) { Foreground = NoraWpfTheme.DimBrush });
        info.Children.Add(detail);
        grid.Children.Add(info);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (selected)
        {
            right.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = NoraWpfTheme.Brush(Color.FromArgb(46, 255, 156, 38)),
                BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(120, 255, 156, 38)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3.5, 8, 3.5),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "ACTIVE", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.OrangeBrush }
            });
        }
        right.Children.Add(PingBadge(ping));
        if (arranging && move is not null)
        {
            right.Children.Add(ReorderStepper(
                canMoveUp,
                canMoveDown,
                move,
                "Move server",
                compact: true));
        }
        else if (delete is not null)
        {
            var remove = IconActionButton(NoraIconKind.Trash, "Delete server", danger: true);
            remove.Width = 30;
            remove.Height = 30;
            remove.Margin = new Thickness(7, 0, 0, 0);
            remove.Click += async (_, e) =>
            {
                e.Handled = true;
                await delete();
            };
            right.Children.Add(remove);
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        row.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            if (arranging)
                return;
            await select();
        };

        if (arranging)
        {
            ApplyArrangementWiggle(row, index, isCard: false);
        }
        else if (NoraWpfTheme.MotionEnabled && index < 14)
        {
            // Staggered entrance: rows cascade in when a group expands.
            row.Opacity = 0;
            var slide = new TranslateTransform(0, 10);
            row.RenderTransform = slide;
            row.Loaded += (_, _) =>
            {
                var delay = TimeSpan.FromMilliseconds(28 * index);
                row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = delay });
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            };
        }
        return row;
    }

    private UIElement ReorderStepper(
        bool canMoveUp,
        bool canMoveDown,
        Func<int, Task> move,
        string label,
        bool compact = false)
    {
        var stack = new StackPanel
        {
            Width = compact ? 26 : 28,
            Height = compact ? 32 : 36,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(compact ? 7 : 2, 0, 0, 0)
        };

        NoraFxButton Arrow(NoraIconKind icon, bool enabled, int direction, string tooltip)
        {
            var button = new NoraFxButton(
                Color.FromArgb(14, 255, 156, 38),
                Color.FromArgb(40, 255, 156, 38),
                7,
                accent: false,
                stroke: NoraWpfTheme.Brush(Color.FromArgb(70, 255, 156, 38)))
            {
                Width = compact ? 26 : 28,
                Height = compact ? 15 : 17,
                Padding = new Thickness(0),
                IsEnabled = enabled,
                Opacity = enabled ? 1 : 0.24,
                ToolTip = tooltip,
                Content = new NoraIcon
                {
                    Kind = icon,
                    Width = compact ? 12 : 13,
                    Height = compact ? 12 : 13,
                    Stroke = NoraWpfTheme.OrangeBrush,
                    Weight = 2.0
                }
            };
            button.Click += async (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                await move(direction);
            };
            return button;
        }

        stack.Children.Add(Arrow(NoraIconKind.ChevronUp, canMoveUp, -1, label + " up"));
        stack.Children.Add(Arrow(NoraIconKind.ChevronDown, canMoveDown, 1, label + " down"));
        return stack;
    }

    private void ApplyArrangementWiggle(FrameworkElement element, int index, bool isCard)
    {
        if (!NoraWpfTheme.MotionEnabled)
            return;

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var translate = new TranslateTransform();
        var rotate = new RotateTransform();
        var transforms = new TransformGroup();
        transforms.Children.Add(translate);
        transforms.Children.Add(rotate);
        element.RenderTransform = transforms;

        var delay = TimeSpan.FromMilliseconds((index % 5) * 72);
        var cycle = TimeSpan.FromMilliseconds(isCard ? 920 : 760);
        var x = isCard ? 0.72 : 0.65;
        var angle = isCard ? 0.22 : 0.22;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimationUsingKeyFrames
        {
            BeginTime = delay,
            RepeatBehavior = RepeatBehavior.Forever,
            KeyFrames =
            {
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero), ease),
                new EasingDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(cycle.TotalMilliseconds * 0.26)), ease),
                new EasingDoubleKeyFrame(-x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(cycle.TotalMilliseconds * 0.70)), ease),
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(cycle), ease)
            }
        });
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimationUsingKeyFrames
        {
            BeginTime = delay,
            RepeatBehavior = RepeatBehavior.Forever,
            KeyFrames =
            {
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero), ease),
                new EasingDoubleKeyFrame(-angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(cycle.TotalMilliseconds * 0.32)), ease),
                new EasingDoubleKeyFrame(angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(cycle.TotalMilliseconds * 0.72)), ease),
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(cycle), ease)
            }
        });
    }

    private static UIElement ArrangementSafeHost(Border card)
    {
        // A transformed subscription used to touch the ScrollViewer's horizontal clip.
        // This host reserves a four-pixel safety lane on both sides, so the same motion
        // remains fully visible instead of shaving off the orange card outline.
        card.Margin = new Thickness(4, 1, 4, 1);
        var host = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12),
            ClipToBounds = false,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        host.Children.Add(card);
        return host;
    }

    private static TextBlock EmojiAwareTextBlock(string? value, TextBlock textBlock)
    {
        textBlock.FontFamily = NoraWpfTheme.UiFont;
        var plain = new StringBuilder();

        void FlushPlain()
        {
            if (plain.Length == 0)
                return;
            textBlock.Inlines.Add(new System.Windows.Documents.Run(plain.ToString()));
            plain.Clear();
        }

        foreach (var token in EmojiTokens(value ?? string.Empty))
        {
            var emoji = LoadEmoji(token);
            if (emoji is null)
            {
                plain.Append(token);
                continue;
            }

            FlushPlain();
            var size = Math.Clamp(textBlock.FontSize * 1.04, 13, 24);
            textBlock.Inlines.Add(new System.Windows.Documents.InlineUIContainer(new System.Windows.Controls.Image
            {
                Source = emoji,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                IsHitTestVisible = false
            })
            {
                BaselineAlignment = System.Windows.BaselineAlignment.Baseline
            });
        }

        FlushPlain();
        return textBlock;
    }

    private static ImageSource? LoadEmoji(string token)
    {
        var key = string.Join('-', token.EnumerateRunes()
            .Select(rune => rune.Value)
            .Where(value => value != 0xFE0F)
            .Select(value => value.ToString("x", CultureInfo.InvariantCulture)));
        if (key.Length == 0)
            return null;

        lock (EmojiImageCacheSync)
        {
            if (EmojiImageCache.TryGetValue(key, out var cached))
                return cached;

            var path = Path.Combine(AppContext.BaseDirectory, "assets", "emoji", key + ".png");
            if (!File.Exists(path))
            {
                EmojiImageCache[key] = null;
                return null;
            }
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                EmojiImageCache[key] = image;
                return image;
            }
            catch
            {
                EmojiImageCache[key] = null;
                return null;
            }
        }
    }

    private static IEnumerable<string> EmojiTokens(string text)
    {
        var runes = text.EnumerateRunes().ToArray();
        for (var index = 0; index < runes.Length;)
        {
            if (!IsEmojiBase(runes[index].Value))
            {
                yield return runes[index++].ToString();
                continue;
            }

            var token = new StringBuilder(runes[index++].ToString());
            if (IsRegionalIndicator(runes[index - 1].Value) && index < runes.Length && IsRegionalIndicator(runes[index].Value))
                token.Append(runes[index++].ToString());

            while (index < runes.Length && IsEmojiSuffix(runes[index].Value))
                token.Append(runes[index++].ToString());

            while (index + 1 < runes.Length && runes[index].Value == 0x200D && IsEmojiBase(runes[index + 1].Value))
            {
                token.Append(runes[index++].ToString());
                token.Append(runes[index++].ToString());
                while (index < runes.Length && IsEmojiSuffix(runes[index].Value))
                    token.Append(runes[index++].ToString());
            }

            yield return token.ToString();
        }
    }

    private static bool IsEmojiBase(int value) =>
        value is >= 0x1F000 and <= 0x1FAFF ||
        value is >= 0x2600 and <= 0x27BF ||
        value is 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x3030 or 0x303D or 0x3297 or 0x3299;

    private static bool IsEmojiSuffix(int value) =>
        value is 0xFE0F or 0x20E3 ||
        value is >= 0x1F3FB and <= 0x1F3FF ||
        value is >= 0xE0020 and <= 0xE007F;

    private static bool IsRegionalIndicator(int value) => value is >= 0x1F1E6 and <= 0x1F1FF;

    private Task MoveSubscriptionAsync(NoraSubscriptionInfo subscription, int direction)
    {
        if (NoraSubscriptionStore.MoveSubscription(subscription.Id, direction))
        {
            AppendLog($"Saved subscription order for {subscription.Title}.");
            RenderPage(PageKind.Servers);
        }
        return Task.CompletedTask;
    }

    private Task MoveSubscriptionServerAsync(NoraSubscriptionInfo subscription, NoraSubscriptionServer server, int direction)
    {
        if (NoraSubscriptionStore.MoveServer(subscription.Id, server.Id, direction))
        {
            AppendLog($"Saved server order for {server.Name}.");
            RenderPage(PageKind.Servers);
        }
        return Task.CompletedTask;
    }

    private UIElement PingBadge(PingStatus ping)
    {
        var isCheck = !ping.Online && ping.Text == "check";
        var ms = 0L;
        if (ping.Online && ping.IsLatency)
        {
            var digits = new string(ping.Text.TakeWhile(char.IsDigit).ToArray());
            _ = long.TryParse(digits, out ms);
        }
        var color = isCheck ? NoraWpfTheme.Dim : ping.Online ? (ping.IsLatency ? NoraWpfTheme.PingColor(ms) : NoraWpfTheme.Orange) : NoraWpfTheme.Red;
        var text = _isPinging && isCheck ? "..." : isCheck ? "- ms" : ping.Text;
        var badge = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = NoraWpfTheme.Brush(NoraWpfTheme.With(color, 26)),
            BorderBrush = NoraWpfTheme.Brush(NoraWpfTheme.With(color, 70)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3.5, 8, 3.5),
            MinWidth = 58,
            VerticalAlignment = VerticalAlignment.Center
        };
        var inner = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        inner.Children.Add(new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = NoraWpfTheme.Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        inner.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            FontFamily = NoraWpfTheme.MonoFont,
            Foreground = NoraWpfTheme.Brush(color),
            VerticalAlignment = VerticalAlignment.Center
        });
        badge.Child = inner;
        return badge;
    }

    private UIElement AddPage()
    {
        var root = new StackPanel();
        root.Children.Add(PageTitle("Add", "Import a key or deploy your own node."));

        // Supported formats, shown as passive chips.
        var formats = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        foreach (var fmt in new[] { "KRot key", "VLESS Reality", "AWG config", "Subscription URL", "Config file" })
        {
            formats.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = NoraWpfTheme.Brush(Color.FromArgb(90, 17, 22, 30)),
                BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(130, 43, 52, 65)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 7, 7),
                Child = new TextBlock { Text = fmt, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = NoraWpfTheme.MutedBrush }
            });
        }
        root.Children.Add(formats);

        var pasteBorder = new SolidColorBrush(NoraWpfTheme.Stroke);
        var pasteHost = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = NoraWpfTheme.Brush(Color.FromArgb(190, 12, 16, 23)),
            BorderBrush = pasteBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 16),
            Height = 236
        };
        var pasteGrid = new Grid();
        pasteHost.Child = pasteGrid;
        var paste = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            Foreground = NoraWpfTheme.TextBrush,
            BorderThickness = new Thickness(0),
            CaretBrush = NoraWpfTheme.OrangeBrush,
            SelectionBrush = NoraWpfTheme.Brush(Color.FromArgb(90, 255, 156, 38)),
            Padding = new Thickness(14, 12, 14, 12),
            FontFamily = NoraWpfTheme.MonoFont,
            FontSize = 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var placeholder = new TextBlock
        {
            Text = "krot://...   vless://...   https://provider/sub   happ://crypt5/...",
            FontSize = 12,
            FontFamily = NoraWpfTheme.MonoFont,
            Foreground = NoraWpfTheme.DimBrush,
            Margin = new Thickness(17, 14, 17, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Visible
        };
        paste.TextChanged += (_, _) => placeholder.Visibility = string.IsNullOrEmpty(paste.Text) ? Visibility.Visible : Visibility.Collapsed;
        paste.GotKeyboardFocus += (_, _) => pasteBorder.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(Color.FromArgb(190, 255, 156, 38), 180));
        paste.LostKeyboardFocus += (_, _) => pasteBorder.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(NoraWpfTheme.Stroke, 260));
        pasteGrid.Children.Add(paste);
        pasteGrid.Children.Add(placeholder);
        root.Children.Add(pasteHost);

        var importContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        importContent.Children.Add(new NoraIcon { Kind = NoraIconKind.Import, Width = 17, Height = 17, Stroke = NoraWpfTheme.BgBrush, Weight = 2.0, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        importContent.Children.Add(new TextBlock { Text = "Add to NORA VPN", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.BgBrush, VerticalAlignment = VerticalAlignment.Center });
        var import = new NoraFxButton(NoraWpfTheme.Orange, NoraWpfTheme.Orange2, 16, accent: true)
        {
            Content = importContent,
            Height = 52
        };
        import.Click += async (_, _) => await ImportKeyAsync(paste.Text);
        root.Children.Add(import);

        var fileContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        fileContent.Children.Add(new NoraIcon { Kind = NoraIconKind.Import, Width = 15, Height = 15, Stroke = NoraWpfTheme.TextBrush, Weight = 1.8, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        fileContent.Children.Add(new TextBlock { Text = "Import from a config file", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = NoraWpfTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center });
        var fileImport = new NoraFxButton(NoraWpfTheme.Card2, Color.FromRgb(31, 38, 49), 14, accent: false)
        {
            Content = fileContent,
            Height = 46,
            Margin = new Thickness(0, 10, 0, 0)
        };
        fileImport.Click += async (_, _) => await ImportFromFileAsync();
        root.Children.Add(fileImport);
        root.Children.Add(new TextBlock
        {
            Text = "Pick a .conf (AmneziaWG) or .json (Xray/VLESS) file. Any routing rules in the file are applied automatically.",
            FontSize = 11,
            Foreground = NoraWpfTheme.DimBrush,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(20, 8, 20, 0)
        });

        var divider = new Grid { Margin = new Thickness(6, 18, 6, 18) };
        divider.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        divider.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        divider.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var leftRule = new Border { Height = 1, Background = NoraWpfTheme.Brush(Color.FromArgb(130, 43, 52, 65)), VerticalAlignment = VerticalAlignment.Center };
        var rightRule = new Border { Height = 1, Background = NoraWpfTheme.Brush(Color.FromArgb(130, 43, 52, 65)), VerticalAlignment = VerticalAlignment.Center };
        var orText = new TextBlock { Text = "OR", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.DimBrush, Margin = new Thickness(12, 0, 12, 0) };
        Grid.SetColumn(orText, 1);
        Grid.SetColumn(rightRule, 2);
        divider.Children.Add(leftRule);
        divider.Children.Add(orText);
        divider.Children.Add(rightRule);
        root.Children.Add(divider);

        var installContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        installContent.Children.Add(new NoraIcon { Kind = NoraIconKind.Deploy, Width = 17, Height = 17, Stroke = NoraWpfTheme.OrangeBrush, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        installContent.Children.Add(new TextBlock { Text = "Install KRot on your VPS", FontSize = 14.5, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center });
        var install = new NoraFxButton(NoraWpfTheme.Card2, Color.FromRgb(31, 38, 49), 16, accent: false)
        {
            Content = installContent,
            Height = 52
        };
        install.Click += async (_, _) => await ShowInstallDialogAsync();
        root.Children.Add(install);
        root.Children.Add(new TextBlock
        {
            Text = "Takes ~2 minutes on a fresh Ubuntu/Debian VPS. You only need the IP and the SSH password.",
            FontSize = 11.5,
            Foreground = NoraWpfTheme.DimBrush,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(20, 10, 20, 0)
        });
        return root;
    }

    private UIElement UsersPage()
    {
        var root = new StackPanel();
        root.Children.Add(PageTitle("Users", "Access keys on your self-hosted servers."));

        var servers = DiscoverManagedServers().ToList();
        if (servers.Count == 0)
        {
            root.Children.Add(EmptyState("No self-hosted KRot servers yet. Install KRot from the + page first.\n\nImported keys and subscriptions are managed by their providers and are not listed here."));
            return ScrollPage(root);
        }

        if (string.IsNullOrWhiteSpace(_selectedManagedServerPath) || servers.All(x => !string.Equals(x.ProfilePath, _selectedManagedServerPath, StringComparison.OrdinalIgnoreCase)))
            _selectedManagedServerPath = servers[0].ProfilePath;

        var list = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        list.Children.Add(GroupLabel("SELF-HOSTED SERVERS", servers.Count == 1 ? "1 server" : $"{servers.Count} servers"));
        foreach (var server in servers)
            list.Children.Add(ManagedServerCard(server));
        root.Children.Add(list);

        return ScrollPage(root);
    }

    private UIElement ManagedServerCard(ManagedServerInfo server)
    {
        var expanded = string.Equals(server.ProfilePath, _expandedManagedServerPath, StringComparison.OrdinalIgnoreCase);
        var card = Card(20, expanded);
        card.Margin = new Thickness(0, 0, 0, 14);
        card.Padding = new Thickness(14);
        card.Cursor = Cursors.Hand;

        var panel = new StackPanel();
        card.Child = panel;

        var users = LoadServerUsers(server.ServerProfilePath)
            .OrderByDescending(x => IsRecentlyOnline(x.LastOnlineAt))
            .ThenByDescending(x => x.UplinkBytes + x.DownlinkBytes)
            .ToList();
        var onlineCount = users.Count(x => IsRecentlyOnline(x.LastOnlineAt));

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var flagHolder = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(13),
            Background = NoraWpfTheme.Brush(Color.FromArgb(120, 27, 34, 45)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(120, 43, 52, 65)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FlagIcon { Country = server.Country, Width = 26, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        header.Children.Add(flagHolder);

        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(EmojiAwareTextBlock(server.Name, new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        }));
        var sub = new TextBlock
        {
            FontSize = 11.5,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        sub.Inlines.Add(new System.Windows.Documents.Run($"{server.Host}:{server.Port}") { FontFamily = NoraWpfTheme.MonoFont, Foreground = NoraWpfTheme.MutedBrush });
        sub.Inlines.Add(new System.Windows.Documents.Run($"   {users.Count} " + (users.Count == 1 ? "key" : "keys")) { Foreground = NoraWpfTheme.DimBrush });
        if (onlineCount > 0)
            sub.Inlines.Add(new System.Windows.Documents.Run($" · {onlineCount} online") { Foreground = NoraWpfTheme.GreenBrush });
        text.Children.Add(sub);
        Grid.SetColumn(text, 1);
        header.Children.Add(text);

        var pill = SmallPill("SELF-HOSTED");
        pill.VerticalAlignment = VerticalAlignment.Center;
        pill.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(pill, 2);
        header.Children.Add(pill);

        var chevron = new NoraIcon
        {
            Kind = NoraIconKind.ChevronRight,
            Width = 18,
            Height = 18,
            Stroke = NoraWpfTheme.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(expanded ? 90 : 0)
        };
        Grid.SetColumn(chevron, 3);
        header.Children.Add(chevron);
        panel.Children.Add(header);

        card.MouseLeftButtonUp += (_, _) =>
        {
            _selectedManagedServerPath = server.ProfilePath;
            _expandedManagedServerPath = expanded ? "" : server.ProfilePath;
            _showAddUser = false;
            RenderPage(PageKind.Users);
        };

        if (!expanded)
            return card;

        MaybeRefreshManagedStats(server);

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = NoraWpfTheme.Brush(Color.FromArgb(150, 43, 52, 65)),
            Margin = new Thickness(-14, 13, -14, 0)
        });

        // Stats strip
        var totalTraffic = users.Sum(x => x.UplinkBytes + x.DownlinkBytes);
        var stats = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, -6, 2) };
        stats.Children.Add(StatBlock("KEYS", users.Count.ToString(CultureInfo.InvariantCulture), NoraWpfTheme.TextBrush));
        stats.Children.Add(StatBlock("ONLINE", onlineCount.ToString(CultureInfo.InvariantCulture), onlineCount > 0 ? NoraWpfTheme.GreenBrush : NoraWpfTheme.MutedBrush));
        stats.Children.Add(StatBlock("TRAFFIC", FormatBytes(totalTraffic), NoraWpfTheme.TextBrush));
        panel.Children.Add(stats);

        var userHeader = new Grid { Margin = new Thickness(0, 12, 0, 4) };
        userHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        userHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(new TextBlock
        {
            Text = "Access keys",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        });
        headerText.Children.Add(new TextBlock
        {
            Text = "auto-refresh every 20 s",
            FontSize = 10.5,
            Foreground = NoraWpfTheme.DimBrush,
            Margin = new Thickness(0, 2, 0, 0)
        });
        userHeader.Children.Add(headerText);

        var addContent = new StackPanel { Orientation = Orientation.Horizontal };
        addContent.Children.Add(new NoraIcon { Kind = NoraIconKind.Plus, Width = 14, Height = 14, Stroke = NoraWpfTheme.BgBrush, Weight = 2.4, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        addContent.Children.Add(new TextBlock { Text = "Add user", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.BgBrush, VerticalAlignment = VerticalAlignment.Center });
        var add = new NoraFxButton(NoraWpfTheme.Orange, NoraWpfTheme.Orange2, 14, accent: true)
        {
            Content = addContent,
            Width = 116,
            Height = 38
        };
        add.Click += (_, e) =>
        {
            e.Handled = true;
            _showAddUser = true;
            RenderPage(PageKind.Users);
        };
        Grid.SetColumn(add, 1);
        userHeader.Children.Add(add);
        panel.Children.Add(userHeader);

        if (users.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No extra keys yet - create the first one.",
                FontSize = 12.5,
                Foreground = NoraWpfTheme.MutedBrush,
                Margin = new Thickness(2, 10, 0, 4)
            });
        }
        var rowIndex = 0;
        foreach (var user in users)
            panel.Children.Add(UserRow(server, user, rowIndex++));

        if (_showAddUser)
            panel.Children.Add(AddUserForm(server));

        panel.Children.Add(ManagedServerMaintenance(server));

        if (!server.HasSshCredentials)
        {
            var warn = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = NoraWpfTheme.Brush(Color.FromArgb(26, 255, 196, 84)),
                BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(70, 255, 196, 84)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 12, 0, 0)
            };
            var warnRow = new Grid();
            warnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            warnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            warnRow.Children.Add(new NoraIcon
            {
                Kind = NoraIconKind.Key,
                Width = 16,
                Height = 16,
                Stroke = NoraWpfTheme.AmberBrush,
                Margin = new Thickness(0, 1, 9, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
            var warnText = new TextBlock
            {
                Text = "SSH credentials are not stored for this server. Reinstall KRot from the + page to manage keys from the app.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = NoraWpfTheme.AmberBrush
            };
            Grid.SetColumn(warnText, 1);
            warnRow.Children.Add(warnText);
            warn.Child = warnRow;
            panel.Children.Add(warn);
        }

        return card;
    }

    private UIElement ManagedServerMaintenance(ManagedServerInfo server)
    {
        var host = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = NoraWpfTheme.Brush(Color.FromArgb(74, 12, 16, 23)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(70, 235, 82, 82)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 13, 0, 0),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.Child = grid;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = "Server maintenance",
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = NoraWpfTheme.TextBrush
        });
        text.Children.Add(new TextBlock
        {
            Text = "Remove KRot core, service, and local management state.",
            FontSize = 11,
            Foreground = NoraWpfTheme.DimBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 12, 0)
        });
        grid.Children.Add(text);

        var dangerContent = new StackPanel { Orientation = Orientation.Horizontal };
        dangerContent.Children.Add(new NoraIcon
        {
            Kind = NoraIconKind.Trash,
            Width = 14,
            Height = 14,
            Stroke = NoraWpfTheme.RedBrush,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        dangerContent.Children.Add(new TextBlock
        {
            Text = "Uninstall",
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.RedBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        var uninstall = new NoraFxButton(Color.FromArgb(12, 235, 82, 82), Color.FromArgb(38, 235, 82, 82), 13, accent: false, stroke: NoraWpfTheme.Brush(Color.FromArgb(78, 235, 82, 82)))
        {
            Content = dangerContent,
            Width = 104,
            Height = 34,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Uninstall KRot from this VPS"
        };
        uninstall.Click += async (_, e) =>
        {
            e.Handled = true;
            await UninstallManagedServerAsync(server);
        };
        Grid.SetColumn(uninstall, 1);
        grid.Children.Add(uninstall);
        return host;
    }

    private static UIElement StatBlock(string label, string value, Brush valueBrush)
    {
        var host = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = NoraWpfTheme.Brush(Color.FromArgb(110, 12, 16, 23)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(110, 43, 52, 65)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 6, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.DimBrush
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = valueBrush,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        host.Child = stack;
        return host;
    }

    private static string RelativeTime(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var seen))
            return "never seen";
        var delta = DateTimeOffset.UtcNow - seen.ToUniversalTime();
        if (delta < TimeSpan.FromSeconds(95))
            return "just now";
        if (delta < TimeSpan.FromMinutes(60))
            return $"{(int)delta.TotalMinutes} min ago";
        if (delta < TimeSpan.FromHours(24))
            return $"{(int)delta.TotalHours} h ago";
        return $"{(int)delta.TotalDays} d ago";
    }

    private UIElement UserRow(ManagedServerInfo server, NvpCredential user, int index)
    {
        var displayName = string.IsNullOrWhiteSpace(user.Name) ? user.Id : user.Name;
        var online = IsRecentlyOnline(user.LastOnlineAt);

        var baseBg = Color.FromArgb(0, 27, 34, 45);
        var hoverBg = Color.FromArgb(150, 27, 34, 45);
        var bg = new SolidColorBrush(baseBg);
        var row = new Border
        {
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(9, 7, 9, 7),
            Margin = new Thickness(-4, 3, -4, 0),
            Background = bg
        };
        row.MouseEnter += (_, _) => bg.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(hoverBg, 140));
        row.MouseLeave += (_, _) => bg.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(baseBg, 220));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Child = grid;

        // Avatar with online dot
        var avatarHost = new Grid { Width = 38, Height = 36, VerticalAlignment = VerticalAlignment.Center };
        avatarHost.Children.Add(new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = NoraWpfTheme.Brush(Color.FromArgb(online ? (byte)46 : (byte)26, 255, 156, 38)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(online ? (byte)120 : (byte)60, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = displayName.Length > 0 ? char.ToUpperInvariant(displayName[0]).ToString() : "?",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = online ? NoraWpfTheme.OrangeBrush : NoraWpfTheme.MutedBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        if (online)
        {
            avatarHost.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = NoraWpfTheme.GreenBrush,
                Stroke = NoraWpfTheme.Brush(NoraWpfTheme.Card),
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(25, 0, 0, 0)
            });
        }
        grid.Children.Add(avatarHost);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 8, 0) };
        Grid.SetColumn(info, 1);
        info.Children.Add(new TextBlock
        {
            Text = displayName,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var status = new TextBlock
        {
            Text = online ? "online now" : RelativeTime(user.LastOnlineAt),
            FontSize = 11,
            Foreground = online ? NoraWpfTheme.GreenBrush : NoraWpfTheme.DimBrush,
            Margin = new Thickness(0, 2, 0, 0)
        };
        info.Children.Add(status);
        grid.Children.Add(info);

        var traffic = new TextBlock
        {
            Text = FormatBytes(user.UplinkBytes + user.DownlinkBytes),
            FontSize = 12,
            FontFamily = NoraWpfTheme.MonoFont,
            Foreground = NoraWpfTheme.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            MinWidth = 62,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(traffic, 2);
        grid.Children.Add(traffic);

        var rowKey = UserRowKey(server, user.Id);
        _userStatusText[rowKey] = status;
        _userTrafficText[rowKey] = traffic;

        var copy = new NoraFxButton(Color.FromArgb(18, 255, 156, 38), Color.FromArgb(44, 255, 156, 38), 11, accent: false, stroke: NoraWpfTheme.Brush(Color.FromArgb(60, 255, 156, 38)))
        {
            Content = new NoraIcon { Kind = NoraIconKind.Copy, Width = 15, Height = 15, Stroke = NoraWpfTheme.OrangeBrush },
            Width = 32,
            Height = 32,
            ToolTip = "Copy connection key",
            VerticalAlignment = VerticalAlignment.Center
        };
        copy.Click += (_, e) =>
        {
            e.Handled = true;
            CopyExistingUserKey(server, user.Id);
        };
        Grid.SetColumn(copy, 3);
        grid.Children.Add(copy);

        var del = new NoraFxButton(Color.FromArgb(16, 235, 82, 82), Color.FromArgb(42, 235, 82, 82), 11, accent: false, stroke: NoraWpfTheme.Brush(Color.FromArgb(56, 235, 82, 82)))
        {
            Content = new NoraIcon { Kind = NoraIconKind.Trash, Width = 15, Height = 15, Stroke = NoraWpfTheme.RedBrush },
            Width = 32,
            Height = 32,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Revoke this key",
            VerticalAlignment = VerticalAlignment.Center
        };
        del.Click += async (_, e) =>
        {
            e.Handled = true;
            var confirm = WpfMessageBox.Show(
                $"Delete user `{displayName}`?\n\nThis will disable the connection key on the server.",
                "NORA VPN",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;
            await DeleteManagedUserAsync(server, user.Id);
        };
        Grid.SetColumn(del, 4);
        grid.Children.Add(del);

        if (NoraWpfTheme.MotionEnabled && index < 12)
        {
            row.Opacity = 0;
            var slide = new TranslateTransform(0, 8);
            row.RenderTransform = slide;
            row.Loaded += (_, _) =>
            {
                var delay = TimeSpan.FromMilliseconds(26 * index);
                row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)) { BeginTime = delay });
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            };
        }
        return row;
    }

    private UIElement AddUserForm(ManagedServerInfo server)
    {
        var form = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = NoraWpfTheme.Brush(Color.FromArgb(140, 12, 16, 23)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(120, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(14)
        };
        var panel = new StackPanel();
        form.Child = panel;
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        title.Children.Add(new NoraIcon { Kind = NoraIconKind.Key, Width = 15, Height = 15, Stroke = NoraWpfTheme.OrangeBrush, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock { Text = "New access key", FontSize = 14.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(title);
        panel.Children.Add(Label("LOGIN"));
        var login = Field("newuser");
        panel.Children.Add(login);
        panel.Children.Add(new TextBlock
        {
            Text = "The key is generated on the server and copied to your clipboard.",
            FontSize = 11,
            Foreground = NoraWpfTheme.DimBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 10)
        });
        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var create = NoraButton("Create key", accent: true);
        create.Height = 42;
        var cancel = NoraButton("Cancel");
        cancel.Height = 42;
        create.Click += async (_, e) =>
        {
            e.Handled = true;
            await CreateManagedUserAsync(server, login.Text);
        };
        cancel.Click += (_, e) =>
        {
            e.Handled = true;
            _showAddUser = false;
            RenderPage(PageKind.Users);
        };
        Grid.SetColumn(cancel, 2);
        actions.Children.Add(create);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);

        if (NoraWpfTheme.MotionEnabled)
        {
            form.Opacity = 0;
            var slide = new TranslateTransform(0, 10);
            form.RenderTransform = slide;
            form.Loaded += (_, _) =>
            {
                form.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            };
        }
        return form;
    }


    private enum LogTone { Info, Ok, Warn, Error, Core }

    private static LogTone ClassifyLogLine(string message)
    {
        bool Has(params string[] words) => words.Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase));
        if (Has("failed", "error", "rejected", "timeout", "denied", "not found", "exception", "unable"))
            return LogTone.Error;
        if (Has("verified", "is ready", "is online", "provisioned", "created", "copied", "complete", "started", "added"))
            return LogTone.Ok;
        if (Has("detected", "warning", "disconnecting", "stopping", "stopped", "retry"))
            return LogTone.Warn;
        if (message.StartsWith("[core]", StringComparison.OrdinalIgnoreCase))
            return LogTone.Core;
        return LogTone.Info;
    }

    private UIElement LogsPage()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var allLines = _logs.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.TrimEnd('\r'))
            .ToList();
        var lines = allLines.Count > 400 ? allLines.Skip(allLines.Count - 400).ToList() : allLines;
        var errorCount = 0;
        var warnCount = 0;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = "Logs", FontSize = 42, FontWeight = FontWeights.Bold });
        var subtitle = new TextBlock { FontSize = 16, Foreground = NoraWpfTheme.MutedBrush, Margin = new Thickness(0, 8, 0, 0) };
        titleStack.Children.Add(subtitle);
        header.Children.Add(titleStack);

        var testColor = _diagnosticRunning ? NoraWpfTheme.Red : NoraWpfTheme.Orange;
        var testContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        testContent.Children.Add(new NoraIcon
        {
            Kind = NoraIconKind.Radar,
            Width = 18,
            Height = 18,
            Stroke = NoraWpfTheme.Brush(testColor),
            Margin = new Thickness(0, 0, 8, 0)
        });
        testContent.Children.Add(new TextBlock
        {
            Text = _diagnosticRunning ? "Cancel" : "Run test",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = NoraWpfTheme.Brush(testColor),
            VerticalAlignment = VerticalAlignment.Center
        });
        var testBtn = new NoraFxButton(
            NoraWpfTheme.With(testColor, 20),
            NoraWpfTheme.With(testColor, 46),
            16,
            accent: true)
        {
            Content = testContent,
            Width = 112,
            Height = 46,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
            ToolTip = _diagnosticRunning ? "Cancel the current connection test" : "Run a complete test for the selected server",
            IsEnabled = _diagnosticRunning || _state is TunnelState.Ready or TunnelState.Failed
        };
        testBtn.Click += async (_, _) =>
        {
            if (_diagnosticRunning)
            {
                _diagnosticStatus = "Cancelling test...";
                _diagnosticCancellation?.Cancel();
                RenderPage(PageKind.Logs);
                return;
            }
            await RunConnectionDiagnosticAsync();
        };
        Grid.SetColumn(testBtn, 1);
        header.Children.Add(testBtn);

        var copyBtn = new NoraFxButton(Color.FromArgb(20, 255, 156, 38), Color.FromArgb(46, 255, 156, 38), 16, accent: true)
        {
            Content = new NoraIcon { Kind = NoraIconKind.Copy, Width = 20, Height = 20, Stroke = NoraWpfTheme.OrangeBrush },
            Width = 46,
            Height = 46,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
            ToolTip = string.IsNullOrWhiteSpace(_latestDiagnosticReport) ? "Copy all logs" : "Copy the latest diagnostic report"
        };
        copyBtn.Click += (_, _) =>
        {
            try
            {
                WpfClipboard.SetText(string.IsNullOrWhiteSpace(_latestDiagnosticReport) ? _logs.ToString() : _latestDiagnosticReport);
                ShowToast("Diagnostic report copied", "Send this report to support without exposing subscription keys.", ToastKind.Success);
            }
            catch { }
        };
        Grid.SetColumn(copyBtn, 2);
        header.Children.Add(copyBtn);

        var clearBtn = new NoraFxButton(Colors.Transparent, Color.FromArgb(40, 235, 82, 82), 16, accent: false)
        {
            Content = new NoraIcon { Kind = NoraIconKind.Broom, Width = 20, Height = 20, Stroke = NoraWpfTheme.MutedBrush },
            Width = 46,
            Height = 46,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "Clear the log view"
        };
        clearBtn.Click += (_, _) =>
        {
            _logs.Clear();
            if (!_diagnosticRunning)
            {
                _latestDiagnosticReport = "";
                _diagnosticStatus = "";
            }
            RenderPage(PageKind.Logs);
        };
        Grid.SetColumn(clearBtn, 3);
        header.Children.Add(clearBtn);
        root.Children.Add(header);

        var console = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = NoraWpfTheme.Brush(Color.FromArgb(216, 10, 13, 19)),
            BorderBrush = NoraWpfTheme.StrokeBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 18, 0, 0),
            ClipToBounds = true
        };
        Grid.SetRow(console, 1);

        var consoleGrid = new Grid();
        console.Child = consoleGrid;
        consoleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        consoleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Console title bar
        var chromeBar = new Grid { Margin = new Thickness(14, 10, 14, 10) };
        chromeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        chromeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in new[] { Color.FromRgb(235, 82, 82), Color.FromRgb(255, 196, 84), Color.FromRgb(20, 191, 96) })
            dots.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = NoraWpfTheme.Brush(NoraWpfTheme.With(c, 190)), Margin = new Thickness(0, 0, 6, 0) });
        chromeBar.Children.Add(dots);
        var consoleTitle = new TextBlock
        {
            Text = "nora · runtime",
            FontSize = 11,
            FontFamily = NoraWpfTheme.MonoFont,
            Foreground = NoraWpfTheme.DimBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(consoleTitle, 1);
        chromeBar.Children.Add(consoleTitle);
        consoleGrid.Children.Add(chromeBar);
        var barRule = new Border { Height = 1, Background = NoraWpfTheme.Brush(Color.FromArgb(130, 43, 52, 65)), VerticalAlignment = VerticalAlignment.Bottom };
        consoleGrid.Children.Add(barRule);

        var list = new StackPanel { Margin = new Thickness(12, 8, 12, 12) };
        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^\[(\d{2}:\d{2}:\d{2})\]\s?(.*)$");
            var time = match.Success ? match.Groups[1].Value : "";
            var message = match.Success ? match.Groups[2].Value : line;
            var tone = ClassifyLogLine(message);
            if (tone == LogTone.Error) errorCount++;
            if (tone == LogTone.Warn) warnCount++;
            var (accent, textBrush) = tone switch
            {
                LogTone.Error => (NoraWpfTheme.Red, NoraWpfTheme.Brush(Color.FromRgb(255, 158, 152))),
                LogTone.Ok => (NoraWpfTheme.Green, NoraWpfTheme.Brush(Color.FromRgb(146, 226, 176))),
                LogTone.Warn => (Color.FromRgb(255, 196, 84), NoraWpfTheme.Brush(Color.FromRgb(255, 214, 140))),
                LogTone.Core => (NoraWpfTheme.Blue, NoraWpfTheme.Brush(Color.FromRgb(148, 166, 214))),
                _ => (NoraWpfTheme.Dim, NoraWpfTheme.Brush(Color.FromRgb(184, 194, 210)))
            };

            var row = new Grid { Margin = new Thickness(0, 1.5, 0, 1.5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = time,
                FontSize = 10.5,
                FontFamily = NoraWpfTheme.MonoFont,
                Foreground = NoraWpfTheme.DimBrush,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 0, 0)
            });
            var marker = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(2),
                Background = NoraWpfTheme.Brush(NoraWpfTheme.With(accent, tone == LogTone.Info ? (byte)60 : (byte)200)),
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(marker, 1);
            row.Children.Add(marker);
            var text = new TextBlock
            {
                Text = message,
                FontSize = 11.5,
                FontFamily = NoraWpfTheme.MonoFont,
                Foreground = textBrush,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(text, 2);
            row.Children.Add(text);
            list.Children.Add(row);
        }
        if (lines.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "No events yet.",
                FontSize = 12,
                FontFamily = NoraWpfTheme.MonoFont,
                Foreground = NoraWpfTheme.DimBrush,
                Margin = new Thickness(4, 8, 0, 0)
            });
        }

        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        consoleGrid.Children.Add(scroll);
        scroll.Loaded += (_, _) => scroll.ScrollToEnd();
        root.Children.Add(console);

        var summary = $"{lines.Count} events";
        if (errorCount > 0)
            summary += $" · {errorCount} errors";
        if (warnCount > 0)
            summary += $" · {warnCount} warnings";
        if (!string.IsNullOrWhiteSpace(_diagnosticStatus))
            summary += " · " + _diagnosticStatus;
        subtitle.Text = summary;

        return root;
    }

    private async Task RunConnectionDiagnosticAsync()
    {
        if (_diagnosticRunning)
            return;
        if (_state is not (TunnelState.Ready or TunnelState.Failed) || _core is not null || _connectCancellation is not null)
        {
            ShowToast("Disconnect first", "The complete test uses a temporary tunnel and cannot run beside an active connection.", ToastKind.Info);
            return;
        }

        var target = BuildDiagnosticTarget();
        if (target is null)
        {
            ShowToast("No server selected", "Add or select a server before running the connection test.", ToastKind.Info);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _diagnosticCancellation = cancellation;
        _diagnosticRunning = true;
        _diagnosticStatus = "Preparing test...";
        _latestDiagnosticReport = "";
        AppendLog("[diag] Manual connection test requested from Logs.");
        RenderPage(PageKind.Logs);

        try
        {
            var runner = new NoraConnectionDiagnosticRunner(
                AppendDiagnosticLine,
                progress => UpdateDiagnosticProgress(progress));
            var result = await runner.RunAsync(
                target,
                coreLog => CreateDiagnosticCore(target, coreLog),
                cancellation.Token);
            _latestDiagnosticReport = result.Report;
            _diagnosticStatus = result.Cancelled
                ? "Test cancelled"
                : result.Success
                    ? "Test passed"
                    : "Test failed · " + result.Code;
            if (!result.Cancelled)
            {
                ShowToast(
                    result.Success ? "Connection test passed" : "Connection test found a problem",
                    result.Summary,
                    result.Success ? ToastKind.Success : ToastKind.Error);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[diag] runner_failure type={ex.GetBaseException().GetType().Name}; detail={NoraDiagnosticRedactor.Sanitize(ex.GetBaseException().Message)}");
            _diagnosticStatus = "Test failed · INTERNAL_ERROR";
            ShowToast("Connection test failed", "The diagnostic runner stopped unexpectedly. Copy the report from Logs.", ToastKind.Error);
        }
        finally
        {
            if (ReferenceEquals(_diagnosticCancellation, cancellation))
                _diagnosticCancellation = null;
            cancellation.Dispose();
            _diagnosticRunning = false;
            if (_page == PageKind.Logs)
                RenderPage(PageKind.Logs);
        }
    }

    private NoraDiagnosticTarget? BuildDiagnosticTarget()
    {
        var server = CurrentServerInfo();
        if (_activeSubscriptionServer is not null)
        {
            return new NoraDiagnosticTarget(
                server.Name,
                server.Protocol,
                server.Host,
                server.Port,
                _activeSubscriptionServer.LocalPath,
                _activeSubscriptionServer,
                ExpectEndpointExit: false);
        }

        if (string.IsNullOrWhiteSpace(_activeProfilePath) || !File.Exists(_activeProfilePath) || server.Host == "0.0.0.0")
            return null;
        var isKrot = string.IsNullOrWhiteSpace(_activeExternalProtocol) ||
                     server.Protocol.Contains("KRot", StringComparison.OrdinalIgnoreCase);
        return new NoraDiagnosticTarget(
            server.Name,
            server.Protocol,
            server.Host,
            server.Port,
            _activeProfilePath,
            SubscriptionServer: null,
            ExpectEndpointExit: isKrot);
    }

    private static IVpnCoreProcess CreateDiagnosticCore(NoraDiagnosticTarget target, Action<string> log)
    {
        if (target.SubscriptionServer is not null)
            return new XrayCoreProcess(target.SubscriptionServer, log);
        if (target.Protocol.Contains("AWG", StringComparison.OrdinalIgnoreCase))
            return new AwgCoreProcess(target.ProfilePath, log);
        return new NvpCoreProcess(target.ProfilePath, log);
    }

    private void AppendDiagnosticLine(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendDiagnosticLine(line));
            return;
        }
        AppendLog(line);
    }

    private void UpdateDiagnosticProgress(NoraDiagnosticProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateDiagnosticProgress(progress));
            return;
        }
        _diagnosticStatus = $"{progress.Step}/{progress.Total} · {progress.Label}";
        if (_page == PageKind.Logs)
            RenderPage(PageKind.Logs);
    }

    private async Task SetDiscordModeAsync(bool enabled)
    {
        if (enabled)
        {
            if (_state is TunnelState.Connecting or TunnelState.Connected or TunnelState.Disconnecting ||
                _core is not null || _connectCancellation is not null)
            {
                ShowToast(
                    "Disconnect first",
                    "Disconnect from the current server before turning on Discord Mode.",
                    ToastKind.Info);
                return;
            }

            var progress = new NoraProgressWindow(
                "Preparing Discord Mode",
                "Checking the selective routing engine…")
            {
                Owner = this
            };
            var progressVisibleFor = Stopwatch.StartNew();
            progress.Show();
            try
            {
                await NoraDiscordModeSettings.PrepareAsync(
                    status => Dispatcher.Invoke(() => progress.SetStatus(status)),
                    AppendLog,
                    CancellationToken.None);
                NoraDiscordModeSettings.SetEnabledOrThrow(true);
                AppendLog("[discord] Discord Mode enabled");
                progress.SetStatus("Discord Mode is ready");
                var remaining = TimeSpan.FromMilliseconds(1300) - progressVisibleFor.Elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining);
                progress.Close();
                RenderPage(PageKind.Home);
                ShowToast("Discord Mode is ready", "Choose a VLESS or KRot server and connect.", ToastKind.Success);
            }
            catch (Exception ex)
            {
                NoraDiscordModeSettings.Enabled = false;
                var incident = ReportFailure(NoraOperation.DiscordMode, ex);
                progress.SetError(incident.Title, incident.Message + Environment.NewLine + "What to do: " + incident.Action);
            }
            return;
        }

        if (_state == TunnelState.Disconnecting)
        {
            ShowToast("Disconnecting", "Wait until the current connection has stopped, then turn off Discord Mode.", ToastKind.Info);
            return;
        }

        if (_state == TunnelState.Connecting)
            await CancelConnectingAsync();
        else if (_core is not null || _state == TunnelState.Connected)
            await DisconnectAsync();

        try
        {
            NoraDiscordModeSettings.SetEnabledOrThrow(false);
            AppendLog("[discord] Discord Mode disabled");
            RenderPage(PageKind.Home);
            ShowToast("Discord Mode is off", "NORA has returned to normal full-device VPN mode.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.DiscordMode, ex);
            RenderPage(PageKind.VoiceMode);
        }
    }

    private async Task ConnectAsync()
    {
        if (_diagnosticRunning)
        {
            ShowToast("Connection test is running", "Cancel the manual test on Logs before connecting.", ToastKind.Info);
            return;
        }
        if (_activeSubscriptionServer is null && (string.IsNullOrWhiteSpace(_activeProfilePath) || !File.Exists(_activeProfilePath)))
        {
            RenderPage(PageKind.Add);
            return;
        }

        if (_state is TunnelState.Connecting or TunnelState.Disconnecting)
            return;

        var discordMode = NoraDiscordModeSettings.Enabled;
        if (discordMode && CurrentServerInfo().Protocol.Contains("AWG", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("[discord] connection blocked because AWG is selected");
            ShowToast(
                "AWG is not supported",
                "Discord Mode works with VLESS and KRot servers. Select another server and try again.",
                ToastKind.Info);
            return;
        }

        var cancellation = new CancellationTokenSource();
        var cancellationToken = cancellation.Token;
        _connectCancellation = cancellation;
        _coreLogLimiter = new NoraCoreLogLimiter();

        ApplyState(TunnelState.Connecting);
        _trafficLabel = "";
        _trafficUpRate = 0;
        _trafficDownRate = 0;
        _trafficUpBytes = 0;
        _trafficDownBytes = 0;
        _lastTrafficSample = default;
        _trafficInterfaceHint = "";
        _lastInterfaceTrafficSample = default;
        _lastInterfaceBytesSent = -1;
        _lastInterfaceBytesReceived = -1;
        _trafficHistory.Clear();
        foreach (var graph in _graphs)
            graph.ResetSamples();
        _connectedFor.Reset();

        if (_activeSubscriptionServer is { } automaticServer && NoraSubscriptionStore.IsAutomaticServer(automaticServer))
        {
            try
            {
                await ConnectAutomaticSubscriptionAsync(automaticServer, cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // CancelConnectingAsync has already stopped the owned core and
                // restored the ready state.  A late start/probe exception is not
                // a connection failure and must not display an error toast.
                _activeSubscriptionServer = automaticServer;
            }
            catch (Exception ex)
            {
                ReportFailure(NoraOperation.Connect, ex);
                _core = null;
                _trafficInterfaceHint = "";
                ApplyState(TunnelState.Failed);
            }
            finally
            {
                ClearConnectionCancellation(cancellation);
            }
            return;
        }

        IVpnCoreProcess? startedCore = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var competing = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up)
                .Select(x => x.Name)
                .Where(x => x.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
                            x.Contains("Wintun", StringComparison.OrdinalIgnoreCase) ||
                            x.Contains("TAP", StringComparison.OrdinalIgnoreCase))
                .Where(x => !x.Contains("NORA", StringComparison.OrdinalIgnoreCase) &&
                            !x.Contains("KRot", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (competing.Length > 0)
                AppendLog("Detected other tunnel adapters: " + string.Join(", ", competing));

            var backend = "KRot";
            if (_activeSubscriptionServer is not null)
            {
                if (!_activeSubscriptionServer.Protocol.Contains("VLESS", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("No backend is available for subscription protocol " + _activeSubscriptionServer.Protocol);
                backend = "Xray";
                _trafficInterfaceHint = "NORA-Xray";
                startedCore = new XrayCoreProcess(_activeSubscriptionServer, HandleCoreLine, discordMode);
            }
            else if (_activeExternalProtocol.Equals("AWG 2.0", StringComparison.OrdinalIgnoreCase))
            {
                backend = "AmneziaWG 2.0";
                _trafficInterfaceHint = Path.GetFileNameWithoutExtension(_activeProfilePath);
                startedCore = new AwgCoreProcess(_activeProfilePath, HandleCoreLine);
            }
            else
            {
                startedCore = discordMode
                    ? new NoraDiscordKrotCoreProcess(_activeProfilePath, HandleCoreLine)
                    : new NvpCoreProcess(_activeProfilePath, HandleCoreLine);
            }
            cancellationToken.ThrowIfCancellationRequested();
            _core = startedCore;
            AppendLog("Starting " + backend + " backend");
            await StartCoreAsync(startedCore, TimeSpan.FromSeconds(50), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (discordMode && startedCore is INoraDiscordModeCore selectiveCore)
            {
                AppendLog(backend + " core is ready; verifying the Discord-only route");
                await selectiveCore.VerifyDiscordPathAsync(TimeSpan.FromSeconds(35), cancellationToken);
            }
            else
            {
                AppendLog(backend + " core is ready; verifying tunneled HTTPS and DNS");
                await VerifyDataPlaneAsync(startedCore, TimeSpan.FromSeconds(35), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_core, startedCore))
                throw new OperationCanceledException(cancellationToken);
            _connectedFor.Restart();
            ApplyState(TunnelState.Connected);
            AppendLog(discordMode ? "Discord-only data path verified; connection is online" : "Data plane verified; connection is online");
            ArmBackendExitWatch();
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // The user deliberately stopped this attempt.  Do not show a failed
            // connection state if an in-flight backend call completes with an
            // exception after it was terminated.
            if (startedCore is not null)
            {
                try { await startedCore.StopAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.Connect, ex);
            if (startedCore is not null && ReferenceEquals(_core, startedCore))
            {
                try { await startedCore.StopAsync(TimeSpan.FromSeconds(5)); } catch { }
                _core = null;
            }
            FlushCoreLogSummary();
            _trafficInterfaceHint = "";
            ApplyState(TunnelState.Failed);
        }
        finally
        {
            ClearConnectionCancellation(cancellation);
        }
    }

    private async Task ConnectAutomaticSubscriptionAsync(NoraSubscriptionServer primary, CancellationToken cancellationToken)
    {
        var candidates = NoraSubscriptionStore.GetAutomaticFailoverCandidates(primary);
        Exception? lastFailure = null;
        var discordMode = NoraDiscordModeSettings.Enabled;

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            _activeSubscriptionServer = candidate;
            _trafficInterfaceHint = "NORA-Xray";
            if (index > 0)
                AppendLog($"AUTO fallback {index + 1}/{candidates.Count}: trying {candidate.Name}.");

            var core = new XrayCoreProcess(candidate, HandleCoreLine, discordMode);
            _core = core;
            try
            {
                AppendLog("Starting Xray backend");
                await StartCoreAsync(core, TimeSpan.FromSeconds(50), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (discordMode)
                {
                    AppendLog("Xray core is ready; verifying the Discord-only route");
                    await core.VerifyDiscordPathAsync(TimeSpan.FromSeconds(35), cancellationToken);
                }
                else
                {
                    AppendLog("Xray core is ready; verifying tunneled HTTPS and DNS");
                    await VerifyDataPlaneAsync(core, TimeSpan.FromSeconds(35), cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_core, core))
                    throw new OperationCanceledException(cancellationToken);
                _connectedFor.Restart();
                ApplyState(TunnelState.Connected);
                if (index > 0)
                    AppendLog($"AUTO route switched to a working endpoint: {candidate.Name}.");
                AppendLog(discordMode ? "Discord-only data path verified; connection is online" : "Data plane verified; connection is online");
                ArmBackendExitWatch();
                return;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try { await core.StopAsync(TimeSpan.FromSeconds(5)); } catch { }
                    throw new OperationCanceledException(cancellationToken);
                }
                lastFailure = ex;
                AppendLog($"AUTO endpoint {index + 1}/{candidates.Count} did not pass the traffic check.");
                if (ReferenceEquals(_core, core))
                {
                    try { await core.StopAsync(TimeSpan.FromSeconds(5)); } catch { }
                    _core = null;
                }
            }
        }

        _activeSubscriptionServer = primary;
        throw lastFailure ?? new InvalidOperationException("No AUTO endpoint could be started.");
    }

    private void ArmBackendExitWatch()
    {
        var core = _core;
        if (core is null)
            return;
        _ = core.WaitForExitAsync().ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_state != TunnelState.Connected || !ReferenceEquals(_core, core))
                    return;
                _connectedFor.Stop();
                _core = null;
                ApplyState(TunnelState.Failed);
                ReportFailure(NoraOperation.BackendRuntime, new InvalidOperationException("VPN backend stopped unexpectedly after the data plane was verified."));
            });
        });
    }

    private async Task VerifyDataPlaneAsync(IVpnCoreProcess core, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Xray and AWG endpoints may relay through a different public egress IP.
        // Only the self-hosted KRot profile guarantees endpoint IP == exit IP.
        var expectedExit = core is NvpCoreProcess ? CurrentServerInfo().Host : "";
        await NoraDataPlaneProbe.VerifyAsync(core, timeout, AppendLog, expectedExit, cancellationToken);
    }

    private static Task StartCoreAsync(IVpnCoreProcess core, TimeSpan timeout, CancellationToken cancellationToken)
        => core is XrayCoreProcess xray
            ? xray.StartAsync(timeout, cancellationToken)
            : core.StartAsync(timeout);

    private async Task CancelConnectingAsync()
    {
        if (_state != TunnelState.Connecting)
            return;

        // Detach first: any continuation from the original attempt will see a
        // cancelled token and cannot turn this user action into Failed/Connected.
        _connectCancellation?.Cancel();
        var core = _core;
        _core = null;
        ApplyState(TunnelState.Disconnecting);
        AppendLog("Connection attempt cancelled by user");

        try
        {
            if (core is not null)
                await core.StopAsync(TimeSpan.FromSeconds(6));

            FlushCoreLogSummary();
            _connectedFor.Stop();
            _trafficLabel = "";
            _trafficInterfaceHint = "";
            ApplyState(TunnelState.Ready);
            AppendLog("Connection attempt stopped");
        }
        catch (Exception ex)
        {
            _connectedFor.Stop();
            _trafficLabel = "";
            _trafficInterfaceHint = "";
            ApplyState(TunnelState.Failed);
            ReportFailure(NoraOperation.Disconnect, ex);
        }
    }

    private void ClearConnectionCancellation(CancellationTokenSource cancellation)
    {
        if (!ReferenceEquals(_connectCancellation, cancellation))
            return;
        _connectCancellation = null;
        cancellation.Dispose();
    }

    private async Task<bool> DisconnectAsync(NoraOperation operation = NoraOperation.Disconnect)
    {
        var core = _core;
        _core = null;
        try
        {
            if (core is not null)
            {
                ApplyState(TunnelState.Disconnecting);
                AppendLog("Disconnecting");
                await core.StopAsync(TimeSpan.FromSeconds(6));
            }
            FlushCoreLogSummary();
            _connectedFor.Stop();
            _trafficLabel = "";
            _trafficInterfaceHint = "";
            ApplyState(TunnelState.Ready);
            return true;
        }
        catch (Exception ex)
        {
            _connectedFor.Stop();
            _trafficLabel = "";
            _trafficInterfaceHint = "";
            ApplyState(TunnelState.Failed);
            ReportFailure(operation, ex);
            return false;
        }
    }

    private void HandleCoreLine(string line)
    {
        Dispatcher.Invoke(() =>
        {
            var accepted = _coreLogLimiter.Accept(line);
            if (!string.IsNullOrWhiteSpace(accepted))
                AppendLog("[core] " + accepted);
            var match = Regex.Match(line, @"traffic:\s+up_bps=(\d+)\s+down_bps=(\d+)");
            if (!match.Success ||
                !long.TryParse(match.Groups[1].Value, out var up) ||
                !long.TryParse(match.Groups[2].Value, out var down))
                return;
            var now = DateTimeOffset.UtcNow;
            var elapsed = _lastTrafficSample == default ? 0.25 : Math.Clamp((now - _lastTrafficSample).TotalSeconds, 0.1, 5.0);
            _lastTrafficSample = now;
            RecordTrafficSample(up, down, elapsed);
        });
    }

    private void FlushCoreLogSummary()
    {
        foreach (var summary in _coreLogLimiter.Summaries())
            AppendLog("[core] " + summary);
        _coreLogLimiter = new NoraCoreLogLimiter();
    }

    private void SampleBackendInterfaceTraffic()
    {
        if (string.IsNullOrWhiteSpace(_trafficInterfaceHint))
            return;
        if (!TryGetInterfaceBytes(_trafficInterfaceHint, out var sent, out var received))
            return;

        var now = DateTimeOffset.UtcNow;
        if (_lastInterfaceTrafficSample == default || _lastInterfaceBytesSent < 0 || _lastInterfaceBytesReceived < 0)
        {
            _lastInterfaceTrafficSample = now;
            _lastInterfaceBytesSent = sent;
            _lastInterfaceBytesReceived = received;
            return;
        }

        var elapsed = Math.Clamp((now - _lastInterfaceTrafficSample).TotalSeconds, 0.2, 5.0);
        var upDelta = Math.Max(0, sent - _lastInterfaceBytesSent);
        var downDelta = Math.Max(0, received - _lastInterfaceBytesReceived);
        _lastInterfaceTrafficSample = now;
        _lastInterfaceBytesSent = sent;
        _lastInterfaceBytesReceived = received;

        RecordTrafficSample((long)(upDelta / elapsed), (long)(downDelta / elapsed), elapsed);
    }

    private static bool TryGetInterfaceBytes(string hint, out long sent, out long received)
    {
        sent = 0;
        received = 0;
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up)
            .Select(x => new
            {
                Interface = x,
                Score = string.Equals(x.Name, hint, StringComparison.OrdinalIgnoreCase) ? 0 :
                    string.Equals(x.Description, hint, StringComparison.OrdinalIgnoreCase) ? 1 :
                    x.Name.Contains(hint, StringComparison.OrdinalIgnoreCase) ? 2 :
                    x.Description.Contains(hint, StringComparison.OrdinalIgnoreCase) ? 3 :
                    99
            })
            .Where(x => x.Score < 99)
            .OrderBy(x => x.Score)
            .ToList();
        var match = interfaces.FirstOrDefault()?.Interface;
        if (match is null)
            return false;
        var stats = match.GetIPv4Statistics();
        sent = stats.BytesSent;
        received = stats.BytesReceived;
        return true;
    }

    private void RecordTrafficSample(long up, long down, double elapsedSeconds)
    {
        _trafficLabel = $"↓ {FormatRate(down)}   ↑ {FormatRate(up)}";
        _trafficUpRate = Math.Max(0, up);
        _trafficDownRate = Math.Max(0, down);
        _trafficUpBytes += Math.Max(0, up) * elapsedSeconds;
        _trafficDownBytes += Math.Max(0, down) * elapsedSeconds;
        if (_trafficHistory.Count >= 64)
            _trafficHistory.Dequeue();
        _trafficHistory.Enqueue((_trafficUpRate, _trafficDownRate));
        foreach (var graph in _graphs)
            graph.AddSample(_trafficUpRate, _trafficDownRate, (long)_trafficUpBytes, (long)_trafficDownBytes, _connectedFor.Elapsed);
    }

    private async Task ImportKeyAsync(string text)
    {
        try
        {
            if (HappCrypt5Decoder.IsCrypt5Link(text) ||
                (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            {
                var subscription = await NoraSubscriptionStore.ImportAsync(text, AppendLog);
                _expandedSubscriptionId = subscription.Id;
                if (subscription.Servers.FirstOrDefault() is { } first)
                    SetActiveSubscriptionServer(first);
                AppendLog($"Subscription added: {subscription.Title}");
                RenderPage(PageKind.Servers);
                return;
            }
            if (text.Trim().StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            {
                var subscription = await NoraSubscriptionStore.ImportAsync(text, AppendLog);
                _expandedSubscriptionId = subscription.Id;
                if (subscription.Servers.FirstOrDefault() is { } first)
                    SetActiveSubscriptionServer(first);
                AppendLog("VLESS Reality server added.");
                RenderPage(PageKind.Servers);
                return;
            }
            var probe = text.TrimStart();
            if (probe.StartsWith('{') || probe.StartsWith('[') ||
                text.Contains("proxies:", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("\"outbounds\"", StringComparison.OrdinalIgnoreCase))
            {
                // Xray/sing-box JSON config file or Clash/Mihomo YAML → config import path,
                // which preserves the provider routing rules for split tunneling.
                var subscription = await NoraSubscriptionStore.ImportAsync(text, AppendLog);
                if (subscription.Servers.Count == 0)
                    throw new NoraAppException("NORA-SUB-4003", "The config file did not contain any supported VLESS servers.");
                _expandedSubscriptionId = subscription.Id;
                if (subscription.Servers.FirstOrDefault() is { } node)
                    SetActiveSubscriptionServer(node);
                AppendLog($"Imported {subscription.Servers.Count} server(s) from config file.");
                RenderPage(PageKind.Servers);
                return;
            }
            var result = await NoraProfileImporter.ImportAsync(text, AppendLog);
            if (!string.IsNullOrWhiteSpace(result.ClientProfilePath))
                SetActiveProfile(result.ClientProfilePath);
            AppendLog(result.Message);
            RenderPage(PageKind.Home);
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.ImportProfile, ex);
        }
    }

    private async Task ImportFromFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a config file",
            Filter = "VPN configs (*.conf;*.json;*.yaml;*.yml;*.txt;*.key)|*.conf;*.json;*.yaml;*.yml;*.txt;*.key|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var text = await File.ReadAllTextAsync(dialog.FileName);
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowToast("Empty file", "The selected file has no readable content.", ToastKind.Error);
                return;
            }
            await ImportKeyAsync(text);
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.ImportProfile, ex);
        }
    }

    private async Task ShowInstallDialogAsync()
    {
        var dialog = new NoraInstallWindow { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        var progress = new NoraProgressWindow("Installing KRot", $"Preparing {dialog.Settings.Host}:{dialog.Settings.Port}...") { Owner = this };
        progress.Show();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        try
        {
            var result = await NoraProvisioner.ProvisionAsync(dialog.Settings, line =>
            {
                AppendLog(line);
                Dispatcher.Invoke(() => progress.SetStatus(line));
            });
            Dispatcher.Invoke(() => progress.SetStatus("Saving local self-hosted profile..."));
            SaveManagedServer(result.ClientProfilePath, result.ServerProfilePath, dialog.Settings);
            SetActiveProfile(result.ClientProfilePath);
            AppendLog("Provisioned " + result.DisplayName);
            progress.Close();
            ShowToast("KRot installed", $"{result.DisplayName} is ready on {dialog.Settings.Host}:{dialog.Settings.Port}.", ToastKind.Success);
            RenderPage(PageKind.Home);
        }
        catch (Exception ex)
        {
            progress.Close();
            ReportFailure(NoraOperation.InstallServer, ex);
        }
    }

    private async Task PingAllAsync()
    {
        if (_isPinging)
            return;
        if (_state is TunnelState.Connecting or TunnelState.Disconnecting)
        {
            ShowToast("Latency unavailable", "Wait until the current connection operation finishes, then measure server latency again.", ToastKind.Info);
            return;
        }
        if (_state == TunnelState.Connected || HasActiveTunnelAdapter())
            AppendLog("Latency probes are pinned to the active physical interface and will not use the VPN tunnel.");
        _isPinging = true;
        RenderPage(PageKind.Servers);
        try
        {
            foreach (var server in NoraSubscriptionStore.LoadAll().SelectMany(x => x.Servers))
            {
                _ping[server.LocalPath] = await ProbeAsync(server.Host, server.Port);
                AppendLog($"Direct latency {server.Host}:{server.Port}: {_ping[server.LocalPath].Text}; {_ping[server.LocalPath].Detail}");
            }
            foreach (var profile in DiscoverProfiles().ToList())
            {
                _ping[profile.Path] = await ProbeProfileAsync(profile);
                AppendLog($"Direct latency {profile.Host}:{profile.Port}: {_ping[profile.Path].Text}; {_ping[profile.Path].Detail}");
            }
        }
        finally
        {
            _isPinging = false;
            RenderPage(PageKind.Servers);
        }
    }

    private static Task<PingStatus> ProbeProfileAsync(ProfileListItem profile)
    {
        if (profile.Protocol.Contains("AWG", StringComparison.OrdinalIgnoreCase) ||
            profile.Protocol.Contains("WireGuard", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new PingStatus(true, "UDP", IsLatency: false, Detail: "UDP endpoints do not expose a TCP handshake for latency probing."));
        return ProbeAsync(profile.Host, profile.Port);
    }

    private static bool HasActiveTunnelAdapter()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up)
            .Any(x =>
                x.Name.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("Wintun", StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("TAP", StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<PingStatus> ProbeAsync(string host, int port)
    {
        var result = await NoraDirectLatencyProbe.ProbeAsync(host, port, TimeSpan.FromMilliseconds(2500));
        return result.Success && result.Milliseconds is { } milliseconds
            ? new PingStatus(true, $"{milliseconds} ms", IsLatency: true, Detail: result.Detail)
            : new PingStatus(false, result.Status, IsLatency: false, Detail: result.Detail);
    }

    private void LoadActiveProfile()
    {
        var designProfile = Environment.GetEnvironmentVariable("NORA_DESIGN_PROFILE");
        if (!string.IsNullOrWhiteSpace(designProfile) && File.Exists(designProfile))
        {
            try
            {
                _activeConfig = NvpConfig.Load(designProfile);
                _activeSubscriptionServer = null;
                _activeExternalProtocol = "";
                _activeProfilePath = designProfile;
                AppendLog("Loaded isolated design preview profile");
                return;
            }
            catch (Exception ex)
            {
                AppendLog("Design preview profile failed: " + ex.Message);
            }
        }
        var saved = NoraAppState.TryLoadActiveProfilePath();
        var portable = Path.Combine(AppContext.BaseDirectory, "client-profile.json");
        if (!string.IsNullOrWhiteSpace(saved) && NoraSubscriptionStore.TryLoadServer(saved, out var server))
            SetActiveSubscriptionServer(server);
        else if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
            SetActiveProfile(saved);
        else if (File.Exists(portable))
            SetActiveProfile(portable);
    }

    private void SetActiveProfile(string path)
    {
        _showActiveEndpoint = false;
        try
        {
            if (NoraSubscriptionStore.TryLoadServer(path, out var server))
            {
                SetActiveSubscriptionServer(server);
                return;
            }
            if (ProfileListItem.TryFromExternal(path, out var external))
            {
                _activeConfig = null;
                _activeSubscriptionServer = null;
                _activeExternalProtocol = external.Protocol;
                _activeProfilePath = path;
                NoraAppState.SaveActiveProfilePath(path);
                _ = ProbeActiveAsync(path);
                AppendLog("Selected " + external.Protocol + " profile " + external.Name);
                return;
            }
            _activeConfig = NvpConfig.Load(path);
            _activeSubscriptionServer = null;
            _activeExternalProtocol = "";
            _activeProfilePath = path;
            NoraAppState.SaveActiveProfilePath(path);
            _ = ProbeActiveAsync(path);
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.LoadProfile, ex);
        }
    }

    private void SetActiveSubscriptionServer(NoraSubscriptionServer server)
    {
        _showActiveEndpoint = false;
        _activeSubscriptionServer = server;
        _activeConfig = null;
        _activeExternalProtocol = "";
        _activeProfilePath = server.LocalPath;
        NoraAppState.SaveActiveProfilePath(server.LocalPath);
        _ = ProbeActiveAsync(server.LocalPath);
        AppendLog("Selected subscription server " + server.Name + " " + server.Host + ":" + server.Port);
    }

    private void SelectFirstAvailableProfile()
    {
        if (NoraSubscriptionStore.LoadAll().SelectMany(x => x.Servers).FirstOrDefault() is { } server)
        {
            SetActiveSubscriptionServer(server);
            return;
        }
        if (DiscoverProfiles().FirstOrDefault() is { } profile)
        {
            SetActiveProfile(profile.Path);
            return;
        }
        _activeConfig = null;
        _activeSubscriptionServer = null;
        _activeExternalProtocol = "";
        _activeProfilePath = "";
        NoraAppState.SaveActiveProfilePath("");
    }

    private static void DeleteProfileFiles(string profilePath)
    {
        var full = Path.GetFullPath(profilePath);
        if (File.Exists(full))
            File.Delete(full);
        var metadata = NoraSubscriptionStore.ExternalProfileMetadataPath(full);
        if (File.Exists(metadata))
            File.Delete(metadata);

        var dataProfilesRoot = Path.GetFullPath(Path.Combine(NoraAppState.DataRoot, "profiles"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var parent = Path.GetDirectoryName(full);
        if (parent is null)
            return;
        var parentFull = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (parentFull.StartsWith(dataProfilesRoot, StringComparison.OrdinalIgnoreCase) &&
            !parentFull.Equals(dataProfilesRoot, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(parent))
            Directory.Delete(parent, recursive: true);
    }

    private ServerInfo CurrentServerInfo()
    {
        if (_activeSubscriptionServer is not null)
        {
            var subOnline = _state is TunnelState.Connected or TunnelState.Connecting || (_ping.TryGetValue(_activeSubscriptionServer.LocalPath, out var sp) && sp.Online);
            return new ServerInfo(_activeSubscriptionServer.Name, _activeSubscriptionServer.Host, _activeSubscriptionServer.Port, _activeSubscriptionServer.Country, subOnline, _activeSubscriptionServer.Protocol);
        }
        if (!string.IsNullOrWhiteSpace(_activeExternalProtocol))
        {
            var external = ProfileListItem.From(_activeProfilePath, _activeProfilePath);
            var externalOnline = _state is TunnelState.Connected or TunnelState.Connecting || (_ping.TryGetValue(_activeProfilePath, out var ep) && ep.Online);
            return new ServerInfo(external.Name, external.Host, external.Port, external.Country, externalOnline, external.Protocol);
        }
        if (_activeConfig is null)
            return new ServerInfo("No server", "0.0.0.0", 443, "Netherlands", false, "No profile");
        var server = _activeConfig.Servers.FirstOrDefault();
        if (server is null)
            return new ServerInfo("KRot profile", "0.0.0.0", 443, "Netherlands", false, "KRot-T");
        var online = _state is TunnelState.Connected or TunnelState.Connecting || (_ping.TryGetValue(_activeProfilePath, out var p) && p.Online);
        return new ServerInfo("Amsterdam VPS", server.Address, server.Port, CountryFor(server.Address), online, "KRot-T  •  Full tunnel");
    }

    private IEnumerable<ProfileListItem> DiscoverProfiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var portable = Path.Combine(AppContext.BaseDirectory, "client-profile.json");

        var root = Path.Combine(NoraAppState.DataRoot, "profiles");
        if (Directory.Exists(root))
        {
            foreach (var path in Directory.GetFiles(root, "client-profile.json", SearchOption.AllDirectories)
                         .OrderByDescending(HasManagedMarker)
                         .ThenByDescending(File.GetLastWriteTimeUtc))
            {
                if (TryYieldProfile(path, seen, endpoints, out var profile))
                    yield return profile;
            }
        }

        if (_activeSubscriptionServer is null && !string.IsNullOrWhiteSpace(_activeProfilePath) && File.Exists(_activeProfilePath))
        {
            if (TryYieldProfile(_activeProfilePath, seen, endpoints, out var profile))
                yield return profile;
        }

        if (File.Exists(portable))
        {
            if (TryYieldProfile(portable, seen, endpoints, out var profile))
                yield return profile;
        }

        var externalRoot = Path.Combine(NoraAppState.DataRoot, "external-profiles");
        if (Directory.Exists(externalRoot))
        {
            foreach (var path in Directory.GetFiles(externalRoot, "*.*", SearchOption.TopDirectoryOnly)
                         .Where(x => x.EndsWith(".conf", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".uri", StringComparison.OrdinalIgnoreCase)))
            {
                if (TryYieldProfile(path, seen, endpoints, out var profile))
                    yield return profile;
            }
        }
    }

    private static bool HasManagedMarker(string path)
    {
        var dir = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "managed-server.json"));
    }

    private static bool TryYieldProfile(string path, HashSet<string> seenPaths, HashSet<string> seenEndpoints, out ProfileListItem profile)
    {
        profile = new ProfileListItem();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || NoraSubscriptionStore.TryLoadServer(path, out _))
            return false;
        var full = Path.GetFullPath(path);
        if (!seenPaths.Add(full))
            return false;
        profile = ProfileListItem.From(full, "");
        var endpointKey = $"{profile.Protocol}|{profile.Host}|{profile.Port}";
        if (!seenEndpoints.Add(endpointKey))
            return false;
        return true;
    }

    private void ApplyState(TunnelState state)
    {
        var previousState = _state;
        _state = state;
        if (_heroGrid is not null)
        {
            _heroGrid.Energy = HeroEnergyFor(state);
            if (state == TunnelState.Connected && previousState != TunnelState.Connected)
                _heroGrid.CelebrateConnected();
        }
        if (_connectButton is not null)
        {
            _connectButton.ConnectedAccent = DiscordModeEnabledForView() ? DiscordModeAccent : NoraWpfTheme.Green;
            _connectButton.IsConnected = state == TunnelState.Connected;
            _connectButton.IsProgress = state is TunnelState.Connecting or TunnelState.Disconnecting;
            _connectButton.IsFailed = state == TunnelState.Failed;
            _connectButton.MainText = state switch
            {
                TunnelState.Connecting => "Connecting",
                TunnelState.Connected => "Connected",
                TunnelState.Disconnecting => "Stopping",
                TunnelState.Failed => "Failed",
                _ => "Connect"
            };
            _connectButton.DetailText = state == TunnelState.Connected
                ? _connectedFor.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
                : "00:00:00";
            _connectButton.InvalidateVisual();
        }
        _audioBackdrop?.SetAccent(ConnectAccentFor(state));
        if (_discordModeStatusText is not null)
        {
            _discordModeStatusText.Text = state switch
            {
                TunnelState.Connected => "DISCORD IS PROTECTED",
                TunnelState.Connecting => "CONNECTING DISCORD TO THE SELECTED SERVER",
                TunnelState.Disconnecting => "STOPPING DISCORD CONNECTION",
                TunnelState.Failed => "DISCORD CONNECTION FAILED · TRY ANOTHER SERVER",
                _ => "SELECT A VLESS OR KROT SERVER AND CONNECT"
            };
            _discordModeStatusText.Foreground = state switch
            {
                TunnelState.Connected => NoraWpfTheme.GreenBrush,
                TunnelState.Failed => NoraWpfTheme.RedBrush,
                TunnelState.Connecting or TunnelState.Disconnecting => NoraWpfTheme.OrangeBrush,
                _ => NoraWpfTheme.DimBrush
            };
        }
        foreach (var graph in _graphs)
        {
            graph.Active = state == TunnelState.Connected;
            graph.SetTraffic(_trafficUpRate, _trafficDownRate, (long)_trafficUpBytes, (long)_trafficDownBytes, _connectedFor.Elapsed);
            graph.InvalidateVisual();
        }
    }

    private void SeedDemoLogs()
    {
        _logs.Clear();
        var t = DateTime.Now.AddMinutes(-4);
        void Add(int seconds, string message) => _logs.AppendLine($"[{t.AddSeconds(seconds):HH:mm:ss}] {message}");
        Add(0, "NORA VPN started");
        Add(1, "Loaded profile Amsterdam VPS");
        Add(3, "Selected subscription server frankfurt.demo.test:443");
        Add(9, "Starting KRot backend");
        Add(10, "[core] wintun adapter NVP-1 created, index 34");
        Add(11, "[core] handshake with frankfurt.demo.test:443 complete in 96 ms");
        Add(12, "[core] traffic: up_bps=182000 down_bps=934000");
        Add(13, "KRot core is ready; verifying tunneled HTTPS and DNS");
        Add(16, "Detected other tunnel adapters: Wintun Userspace Tunnel");
        Add(18, "Data plane verified; connection is online");
        Add(64, "[core] traffic: up_bps=2420000 down_bps=6860000");
        Add(102, "Ping frankfurt.demo.test:443: 41 ms");
        Add(103, "Ping backup.demo.test:443: timeout");
        Add(140, "Refresh users failed: SSH connection timed out after 15 s");
        Add(170, "Disconnecting");
        Add(172, "Core stopped");
        Add(200, "Connect failed: The TLS handshake was rejected by the server");
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logs.AppendLine(line);
        TrimInMemoryLog();
        _runLog?.Append(line);
    }

    private void TrimInMemoryLog()
    {
        const int maximumCharacters = 180_000;
        if (_logs.Length <= maximumCharacters)
            return;

        var removeCount = _logs.Length - maximumCharacters;
        var newline = _logs.ToString().IndexOf('\n', removeCount);
        _logs.Remove(0, newline < 0 ? removeCount : newline + 1);
    }

    private NoraErrorIncident ReportFailure(NoraOperation operation, Exception exception)
    {
        var incident = NoraErrors.Classify(operation, exception);
        AppendLog(incident.ToLogLine());
        if (incident.ShowToast)
            ShowErrorToast(incident);
        return incident;
    }

    private void ShowErrorToast(NoraErrorIncident incident)
    {
        var body = incident.Code + " · " + incident.Message + Environment.NewLine + "What to do: " + incident.Action;
        ShowToast(incident.Title, body, ToastKind.Error);
    }

    private void ShowToast(string title, string body, ToastKind kind)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowToast(title, body, kind));
            return;
        }

        body = string.IsNullOrWhiteSpace(body) ? "Check Logs for details." : body.Trim();
        var dedupe = title + "|" + body;
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(_lastToastText, dedupe, StringComparison.Ordinal) && now - _lastToastAt < TimeSpan.FromSeconds(6))
            return;
        _lastToastText = dedupe;
        _lastToastAt = now;

        var seq = ++_toastSequence;
        _toastHost.Children.Clear();
        var tone = kind switch
        {
            ToastKind.Success => NoraWpfTheme.Green,
            ToastKind.Error => NoraWpfTheme.Red,
            _ => NoraWpfTheme.Orange
        };
        var card = new Border
        {
            MaxWidth = 390,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 12, 14, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = NoraWpfTheme.Brush(Color.FromArgb(242, 13, 18, 26)),
            BorderBrush = NoraWpfTheme.Brush(NoraWpfTheme.With(tone, 160)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 0,
                Opacity = 0.42,
                Color = tone
            },
            Opacity = 0,
            RenderTransform = new TranslateTransform(0, 16)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = grid;

        grid.Children.Add(new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = NoraWpfTheme.Brush(tone),
            Margin = new Thickness(0, 6, 10, 0),
            VerticalAlignment = VerticalAlignment.Top
        });

        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush
        });
        text.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 12,
            Foreground = NoraWpfTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
            MaxWidth = 330
        });
        grid.Children.Add(text);

        _toastHost.Children.Add(card);
        if (NoraWpfTheme.MotionEnabled && card.RenderTransform is TranslateTransform shift)
        {
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
        else
        {
            card.Opacity = 1;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(kind == ToastKind.Error ? 6500 : 4200);
            Dispatcher.Invoke(() =>
            {
                if (seq != _toastSequence || !_toastHost.Children.Contains(card))
                    return;
                if (NoraWpfTheme.MotionEnabled && card.RenderTransform is TranslateTransform shift)
                {
                    card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
                    shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    });
                    var cleanup = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
                    cleanup.Tick += (_, _) =>
                    {
                        cleanup.Stop();
                        if (seq == _toastSequence)
                            _toastHost.Children.Clear();
                    };
                    cleanup.Start();
                }
                else
                {
                    _toastHost.Children.Clear();
                }
            });
        });
    }

    private async Task ProbeActiveAsync(string path)
    {
        if (_state is TunnelState.Connected or TunnelState.Connecting or TunnelState.Disconnecting)
            return;
        if (_activeSubscriptionServer is not null)
        {
            _ping[path] = await ProbeAsync(_activeSubscriptionServer.Host, _activeSubscriptionServer.Port);
            if (_page is PageKind.Home or PageKind.Servers)
                Dispatcher.Invoke(() => RenderPage(_page));
            return;
        }
        if (!string.IsNullOrWhiteSpace(_activeExternalProtocol))
        {
            var external = ProfileListItem.From(path, _activeProfilePath);
            _ping[path] = await ProbeProfileAsync(external);
            if (_page is PageKind.Home or PageKind.Servers)
                Dispatcher.Invoke(() => RenderPage(_page));
            return;
        }
        if (_activeConfig?.Servers.FirstOrDefault() is not { } server)
            return;
        _ping[path] = await ProbeAsync(server.Address, server.Port);
        if (_page is PageKind.Home or PageKind.Servers)
            Dispatcher.Invoke(() => RenderPage(_page));
    }

    private void ReloadUsers(ListBox users)
    {
        users.Items.Clear();
        if (string.IsNullOrWhiteSpace(_activeProfilePath))
            return;
        var serverProfile = KRotUserAdmin.FindServerProfile(_activeProfilePath);
        if (!File.Exists(serverProfile))
            return;
        try
        {
            var cfg = NvpConfig.Load(serverProfile);
            foreach (var credential in cfg.Credentials.Where(x => x.Enabled))
                users.Items.Add($"{credential.Id}  {credential.Name}  last={credential.LastOnlineAt} up={credential.UplinkBytes} down={credential.DownlinkBytes}");
        }
        catch { }
    }

    private IEnumerable<ManagedServerInfo> DiscoverManagedServers()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();
        var dataProfiles = Path.Combine(NoraAppState.DataRoot, "profiles");
        if (Directory.Exists(dataProfiles))
            roots.Add(dataProfiles);
        var repoProfiles = Path.Combine(Directory.GetCurrentDirectory(), "profiles");
        if (Directory.Exists(repoProfiles))
            roots.Add(repoProfiles);
        foreach (var candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "profiles"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "profiles")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "profiles"))
        })
        {
            if (Directory.Exists(candidate))
                roots.Add(candidate);
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var clientProfile in Directory.GetFiles(root, "client-profile.json", SearchOption.AllDirectories))
            {
                if (!seen.Add(clientProfile))
                    continue;
                var dir = Path.GetDirectoryName(clientProfile) ?? "";
                var serverProfile = Path.Combine(dir, "server-profile.json");
                if (!File.Exists(serverProfile))
                    continue;
                ManagedServerMetadata? metadata = null;
                var metadataPath = Path.Combine(dir, "managed-server.json");
                try
                {
                    if (File.Exists(metadataPath))
                        metadata = JsonSerializer.Deserialize<ManagedServerMetadata>(File.ReadAllText(metadataPath));
                }
                catch
                {
                }

                ProfileListItem profile;
                try { profile = ProfileListItem.From(clientProfile, _activeProfilePath); }
                catch { continue; }
                var secret = LoadManagedSecret(metadata, profile);
                yield return new ManagedServerInfo(
                    clientProfile,
                    serverProfile,
                    string.IsNullOrWhiteSpace(metadata?.DisplayName) ? profile.Name : metadata.DisplayName,
                    string.IsNullOrWhiteSpace(metadata?.Host) ? secret.HostOr(profile.Host) : metadata.Host,
                    metadata?.Port > 0 ? metadata.Port : profile.Port,
                    string.IsNullOrWhiteSpace(metadata?.Country) ? profile.Country : metadata.Country,
                    FirstNonEmpty(secret.SshUser, metadata?.SshUser, "root"),
                    FirstNonEmpty(secret.SshPassword, metadata?.SshPassword, ""));
            }
        }
    }

    private static ManagedServerSecret LoadManagedSecret(ManagedServerMetadata? metadata, ProfileListItem profile)
    {
        try
        {
            var path = Path.Combine(NoraAppState.DataRoot, "managed-secrets.json");
            if (!File.Exists(path))
                return ManagedServerSecret.Empty;
            var file = JsonSerializer.Deserialize<ManagedSecretsFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ManagedSecretsFile();
            var host = FirstNonEmpty(metadata?.Host, profile.Host, "");
            return file.Servers.FirstOrDefault(x =>
                string.Equals(x.Host, host, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Host, profile.Host, StringComparison.OrdinalIgnoreCase)) ?? ManagedServerSecret.Empty;
        }
        catch
        {
            return ManagedServerSecret.Empty;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return "";
    }

    private static IEnumerable<NvpCredential> LoadServerUsers(string serverProfilePath)
    {
        try
        {
            var cfg = NvpConfig.Load(serverProfilePath);
            return cfg.Credentials
                .Where(x => x.Enabled)
                .Where(x => !string.Equals(x.Id, cfg.CredentialId, StringComparison.Ordinal))
                .Where(x => !string.Equals(x.Name, "Primary", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private async Task CreateManagedUserAsync(ManagedServerInfo server, string login)
    {
        if (!server.HasSshCredentials)
        {
            ReportFailure(NoraOperation.CreateUser, new NoraAppException("NORA-USR-6001", "This server has no stored SSH credentials."));
            return;
        }
        var progress = new NoraProgressWindow("Creating user", $"Creating `{login}` and restarting KRot on {server.Host}...") { Owner = this };
        progress.Show();
        try
        {
            var cfg = NvpConfig.Load(server.ProfilePath);
            _generatedUserKey = await KRotUserAdmin.CreateUserAsync(server.ProfilePath, cfg, login, server.Host, server.SshUser, server.SshPassword, line =>
            {
                AppendLog(line);
                Dispatcher.Invoke(() => progress.SetStatus(line));
            });
            WpfClipboard.SetText(_generatedUserKey);
            _showAddUser = false;
            progress.Close();
            RenderPage(PageKind.Users);
            new NoraKeyWindow("Connection key created", _generatedUserKey) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            progress.Close();
            ReportFailure(NoraOperation.CreateUser, ex);
        }
    }

    private async Task DeleteManagedUserAsync(ManagedServerInfo server, string credentialId)
    {
        if (!server.HasSshCredentials)
        {
            ReportFailure(NoraOperation.DeleteUser, new NoraAppException("NORA-USR-6001", "This server has no stored SSH credentials."));
            return;
        }
        try
        {
            await KRotUserAdmin.DisableUserAsync(server.ProfilePath, credentialId, server.Host, server.SshUser, server.SshPassword, AppendLog);
            RenderPage(PageKind.Users);
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.DeleteUser, ex);
        }
    }

    private async Task UninstallManagedServerAsync(ManagedServerInfo server)
    {
        if (_state is TunnelState.Connecting or TunnelState.Disconnecting)
        {
            ShowToast("Action in progress", "Wait until the current tunnel operation finishes.", ToastKind.Info);
            return;
        }

        if (!server.HasSshCredentials)
        {
            ReportFailure(NoraOperation.UninstallServer, new NoraAppException("NORA-VPS-5005", "This self-hosted server has no saved SSH credentials for uninstall."));
            return;
        }

        var confirm = WpfMessageBox.Show(
            $"Uninstall KRot from `{server.Name}`?\n\nThis will stop and remove the KRot service on {server.Host}, remove /opt/nvp with all KRot users and keys, and delete this self-hosted server from NORA VPN.\n\nThis cannot be undone except by installing KRot again.",
            "NORA VPN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var progress = new NoraProgressWindow("Uninstalling KRot", $"Removing KRot from {server.Host}...") { Owner = this };
        progress.Show();
        try
        {
            var deletingActive = string.Equals(_activeProfilePath, server.ProfilePath, StringComparison.OrdinalIgnoreCase) &&
                                 _activeSubscriptionServer is null;
            if (deletingActive && _state == TunnelState.Connected && !await DisconnectAsync(NoraOperation.Disconnect))
            {
                progress.Close();
                return;
            }

            await KRotUserAdmin.UninstallServerAsync(server.Host, server.SshUser, server.SshPassword, server.Port, line =>
            {
                AppendLog(line);
                Dispatcher.Invoke(() => progress.SetStatus(line));
            });

            DeleteManagedServerLocalState(server);
            _ping.Remove(server.ProfilePath);
            _lastManagedRefresh.Remove(server.ProfilePath);
            _refreshingManagedServers.Remove(server.ProfilePath);
            if (string.Equals(_expandedManagedServerPath, server.ProfilePath, StringComparison.OrdinalIgnoreCase))
                _expandedManagedServerPath = "";
            if (string.Equals(_selectedManagedServerPath, server.ProfilePath, StringComparison.OrdinalIgnoreCase))
                _selectedManagedServerPath = "";
            if (deletingActive)
                SelectFirstAvailableProfile();

            progress.Close();
            RenderPage(_page);
            ShowToast("KRot uninstalled", $"{server.Name} was removed from the VPS and local self-hosted list.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            progress.Close();
            ReportFailure(NoraOperation.UninstallServer, ex);
        }
    }

    private void MaybeRefreshManagedStats(ManagedServerInfo server, bool force = false)
    {
        if (!server.HasSshCredentials || _refreshingManagedServers.Contains(server.ProfilePath))
            return;
        if (!force && _lastManagedRefresh.TryGetValue(server.ProfilePath, out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(8))
            return;
        _ = RefreshManagedStatsAsync(server);
    }

    private async Task RefreshManagedStatsAsync(ManagedServerInfo server)
    {
        _refreshingManagedServers.Add(server.ProfilePath);
        try
        {
            await KRotUserAdmin.DownloadServerProfileAsync(server.ServerProfilePath, server.Host, server.SshUser, server.SshPassword, AppendLog);
            _lastManagedRefresh[server.ProfilePath] = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.RefreshUsers, ex);
        }
        finally
        {
            _refreshingManagedServers.Remove(server.ProfilePath);
            if (_page == PageKind.Users)
                Dispatcher.Invoke(() => UpdateVisibleUserRows(server));
        }
    }

    private void UpdateVisibleUserRows(ManagedServerInfo server)
    {
        foreach (var user in LoadServerUsers(server.ServerProfilePath))
        {
            var key = UserRowKey(server, user.Id);
            var online = IsRecentlyOnline(user.LastOnlineAt);
            if (_userStatusText.TryGetValue(key, out var status))
            {
                status.Text = online ? "online now" : RelativeTime(user.LastOnlineAt);
                status.Foreground = online ? NoraWpfTheme.GreenBrush : NoraWpfTheme.DimBrush;
            }
            if (_userTrafficText.TryGetValue(key, out var traffic))
                traffic.Text = FormatBytes(user.UplinkBytes + user.DownlinkBytes);
        }
    }

    private static string UserRowKey(ManagedServerInfo server, string credentialId) => server.ServerProfilePath + "|" + credentialId;

    private void CopyExistingUserKey(ManagedServerInfo server, string credentialId)
    {
        try
        {
            var key = KRotUserAdmin.ExportConnectionKey(server.ProfilePath, credentialId);
            WpfClipboard.SetText(key);
            new NoraKeyWindow("Connection key copied", key) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            ReportFailure(NoraOperation.CopyKey, ex);
        }
    }

    private static bool IsRecentlyOnline(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var seen))
            return false;
        return DateTimeOffset.UtcNow - seen.ToUniversalTime() < TimeSpan.FromSeconds(90);
    }

    private static void SaveManagedServer(string clientProfilePath, string serverProfilePath, NoraServerSettings settings)
    {
        var dir = Path.GetDirectoryName(clientProfilePath);
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException("Cannot save self-hosted metadata: profile directory is missing.");
        var metadata = new ManagedServerMetadata
        {
            DisplayName = string.IsNullOrWhiteSpace(settings.DisplayName) ? "KRot VPS" : settings.DisplayName,
            Host = settings.Host,
            Port = settings.Port,
            Country = CountryFor(settings.Host),
            SshUser = "",
            SshPassword = "",
            ServerProfilePath = serverProfilePath,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        File.WriteAllText(Path.Combine(dir, "managed-server.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        SaveManagedSecret(settings.Host, settings.SshUser, settings.SshPassword);
    }

    private static void SaveManagedSecret(string host, string sshUser, string sshPassword)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(sshPassword))
            return;
        var path = Path.Combine(NoraAppState.DataRoot, "managed-secrets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ManagedSecretsFile file;
        try
        {
            file = File.Exists(path)
                ? JsonSerializer.Deserialize<ManagedSecretsFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ManagedSecretsFile()
                : new ManagedSecretsFile();
        }
        catch
        {
            file = new ManagedSecretsFile();
        }

        file.Servers.RemoveAll(x => string.Equals(x.Host, host, StringComparison.OrdinalIgnoreCase));
        file.Servers.Add(new ManagedServerSecret { Host = host.Trim(), SshUser = string.IsNullOrWhiteSpace(sshUser) ? "root" : sshUser.Trim(), SshPassword = sshPassword });
        File.WriteAllText(path, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RemoveManagedSecret(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return;
        try
        {
            var path = Path.Combine(NoraAppState.DataRoot, "managed-secrets.json");
            if (!File.Exists(path))
                return;
            var file = JsonSerializer.Deserialize<ManagedSecretsFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ManagedSecretsFile();
            file.Servers.RemoveAll(x => string.Equals(x.Host, host, StringComparison.OrdinalIgnoreCase));
            if (file.Servers.Count == 0)
                File.Delete(path);
            else
                File.WriteAllText(path, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static void DeleteManagedServerLocalState(ManagedServerInfo server)
    {
        RemoveManagedSecret(server.Host);
        foreach (var portable in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "client-profile.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "client-profile.json")
        })
        {
            DeleteIfSameKRotProfile(portable, server.Host, server.Port);
        }

        var profile = Path.GetFullPath(server.ProfilePath);
        var dir = Path.GetDirectoryName(profile);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;

        var managedMarker = Path.Combine(dir, "managed-server.json");
        var dataProfilesRoot = Path.GetFullPath(Path.Combine(NoraAppState.DataRoot, "profiles"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var dirFull = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var canRemoveWholeFolder = File.Exists(managedMarker) &&
                                   dirFull.StartsWith(dataProfilesRoot, StringComparison.OrdinalIgnoreCase) &&
                                   !dirFull.Equals(dataProfilesRoot, StringComparison.OrdinalIgnoreCase);
        if (canRemoveWholeFolder)
        {
            Directory.Delete(dir, recursive: true);
            return;
        }

        foreach (var path in new[]
        {
            profile,
            Path.GetFullPath(server.ServerProfilePath),
            managedMarker,
            Path.Combine(dir, "connection.key")
        })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        foreach (var keyPath in Directory.GetFiles(dir, "*.connection.key", SearchOption.TopDirectoryOnly))
        {
            try { File.Delete(keyPath); } catch { }
        }
    }

    private static void DeleteIfSameKRotProfile(string path, string host, int port)
    {
        try
        {
            if (!File.Exists(path))
                return;
            var cfg = NvpConfig.Load(path);
            var server = cfg.Servers.FirstOrDefault();
            if (server is null)
                return;
            if (string.Equals(server.Address, host, StringComparison.OrdinalIgnoreCase) && server.Port == port)
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static ScrollViewer ScrollPage(UIElement child) => new()
    {
        Content = child,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(0)
    };

    private static Border Card(double radius, bool highlighted)
    {
        // Keep text on the device-pixel grid. Sub-pixel transforms make ClearType labels
        // visibly soft, so hover depth comes from surface light and border contrast only.
        var baseColor = highlighted ? NoraWpfTheme.Orange : NoraWpfTheme.Stroke;
        var hotColor = highlighted ? NoraWpfTheme.Orange2 : Color.FromRgb(92, 107, 132);
        var borderBrush = new SolidColorBrush(baseColor);
        var baseBackground = Color.FromArgb(224, 17, 22, 30);
        var hotBackground = highlighted ? Color.FromArgb(238, 28, 25, 24) : Color.FromArgb(238, 21, 28, 38);
        var backgroundBrush = new SolidColorBrush(baseBackground);
        var card = new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = backgroundBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(highlighted ? 1.5 : 1),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        card.MouseEnter += (_, _) =>
        {
            borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(hotColor, 160));
            backgroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(hotBackground, 180));
        };
        card.MouseLeave += (_, _) =>
        {
            borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(baseColor, 240));
            backgroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, ColorTo(baseBackground, 240));
        };
        return card;
    }

    private static ColorAnimation ColorTo(Color to, double ms)
        => new(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    private static DoubleAnimation DoubleTo(double to, double ms)
        => new(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    private static UIElement PageTitle(string title, string subtitle)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 42, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = subtitle, FontSize = 16, Foreground = NoraWpfTheme.MutedBrush, Margin = new Thickness(0, 8, 0, 0) });
        return panel;
    }

    private static UIElement PageTitleWithAction(string title, string subtitle, UIElement actionIcon, Func<Task> action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 42, FontWeight = FontWeights.Bold });
        text.Children.Add(new TextBlock { Text = subtitle, FontSize = 16, Foreground = NoraWpfTheme.MutedBrush, Margin = new Thickness(0, 8, 0, 0) });
        grid.Children.Add(text);

        var button = new NoraFxButton(Color.FromArgb(20, 255, 156, 38), Color.FromArgb(46, 255, 156, 38), 18, accent: true)
        {
            Content = actionIcon,
            Width = 50,
            Height = 50,
            Foreground = NoraWpfTheme.OrangeBrush,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0)
        };
        button.Click += async (_, _) => await action();
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private static UIElement EmptyState(string text) => new Border
    {
        CornerRadius = new CornerRadius(18),
        Background = NoraWpfTheme.Brush(NoraWpfTheme.Card2),
        BorderBrush = NoraWpfTheme.StrokeBrush,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(18),
        Margin = new Thickness(0, 22, 0, 0),
        Child = new TextBlock { Text = text, Foreground = NoraWpfTheme.MutedBrush, TextWrapping = TextWrapping.Wrap }
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = NoraWpfTheme.MutedBrush,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 5)
    };

    private static TextBox Field(string text) => new()
    {
        Text = text,
        Height = 38,
        Background = NoraWpfTheme.Brush(NoraWpfTheme.Card2),
        Foreground = NoraWpfTheme.TextBrush,
        BorderBrush = NoraWpfTheme.StrokeBrush,
        CaretBrush = NoraWpfTheme.OrangeBrush,
        Padding = new Thickness(12, 9, 12, 0),
        Margin = new Thickness(0, 0, 0, 8)
    };

    private static void AddField(Panel panel, string label, Control input)
    {
        panel.Children.Add(Label(label));
        panel.Children.Add(input);
    }

    private static UIElement StatusLine(string text, bool ok, Thickness? margin = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin ?? new Thickness(0) };
        if (ok)
        {
            row.Children.Add(new PulsingDot
            {
                Tone = NoraWpfTheme.Green,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(-3, 0, 5, 0)
            });
        }
        else
        {
            row.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = NoraWpfTheme.RedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
        }
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 15,
            Foreground = ok ? NoraWpfTheme.GreenBrush : NoraWpfTheme.RedBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    private static Border SmallPill(string text) => new()
    {
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(9, 4, 9, 4),
        BorderBrush = NoraWpfTheme.OrangeBrush,
        BorderThickness = new Thickness(1),
        Background = NoraWpfTheme.Brush(Color.FromArgb(24, 255, 156, 38)),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 2, 0, 0),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = NoraWpfTheme.OrangeBrush,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center
        }
    };

    private static NoraFxButton IconActionButton(NoraIconKind kind, string tooltip, bool danger = false)
    {
        var tone = danger ? NoraWpfTheme.Red : NoraWpfTheme.Orange;
        var button = new NoraFxButton(
            danger ? Color.FromArgb(12, 235, 82, 82) : Color.FromArgb(16, 255, 156, 38),
            danger ? Color.FromArgb(38, 235, 82, 82) : Color.FromArgb(42, 255, 156, 38),
            11,
            accent: false,
            stroke: NoraWpfTheme.Brush(NoraWpfTheme.With(tone, 62)))
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(5, 0, 0, 0),
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new NoraIcon
            {
                Kind = kind,
                Width = 15,
                Height = 15,
                Stroke = NoraWpfTheme.Brush(tone),
                Weight = 1.9
            }
        };
        return button;
    }

    private static WpfButton NoraButton(string text, bool accent = false)
        => new NoraFxButton(accent ? NoraWpfTheme.Orange : NoraWpfTheme.Card2, accent ? NoraWpfTheme.Orange2 : Color.FromRgb(31, 38, 49), 16, accent)
        {
            Content = text,
            Height = 52,
            Foreground = accent ? NoraWpfTheme.BgBrush : NoraWpfTheme.TextBrush,
            FontWeight = FontWeights.Bold,
            FontSize = 15
        };

    private static string FormatRate(long bytes)
    {
        if (bytes > 1024 * 1024)
            return (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s";
        if (bytes > 1024)
            return (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB/s";
        return bytes + " B/s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes > 1024L * 1024L * 1024L)
            return (bytes / 1024d / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        if (bytes > 1024L * 1024L)
            return (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        if (bytes > 1024)
            return (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        return bytes + " B";
    }

    private static string CountryFor(string host) => "Netherlands";

    private sealed record ServerInfo(string Name, string Host, int Port, string Country, bool Online, string Protocol);
    private sealed record ManagedServerInfo(string ProfilePath, string ServerProfilePath, string Name, string Host, int Port, string Country, string SshUser, string SshPassword)
    {
        public bool HasSshCredentials => !string.IsNullOrWhiteSpace(SshUser) && !string.IsNullOrWhiteSpace(SshPassword);
    }

    private sealed class ManagedServerMetadata
    {
        public string DisplayName { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Country { get; set; } = "";
        public string SshUser { get; set; } = "";
        public string SshPassword { get; set; } = "";
        public string ServerProfilePath { get; set; } = "";
        public string CreatedAt { get; set; } = "";
    }

    private sealed class ManagedSecretsFile
    {
        public List<ManagedServerSecret> Servers { get; set; } = [];
    }

    private sealed class ManagedServerSecret
    {
        public static ManagedServerSecret Empty { get; } = new();
        public string Host { get; set; } = "";
        public string SshUser { get; set; } = "";
        public string SshPassword { get; set; } = "";
        public string HostOr(string fallback) => string.IsNullOrWhiteSpace(Host) ? fallback : Host;
    }

    private sealed record PingStatus(bool Online, string Text, bool IsLatency = true, string Detail = "")
    {
        public static PingStatus Unknown { get; } = new(false, "check");
    }

    private sealed class ProfileListItem
    {
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public string Host { get; init; } = "";
        public int Port { get; init; }
        public string Country { get; init; } = "Netherlands";
        public string Protocol { get; init; } = "KRot-T";

        public static bool TryFromExternal(string path, out ProfileListItem profile)
        {
            profile = new ProfileListItem();
            try
            {
                if (!File.Exists(path))
                    return false;
                var text = File.ReadAllText(path);
                if (!NoraSubscriptionStore.TryReadExternalProfileInfo(path, out var info) &&
                    !NoraSubscriptionStore.TryParseExternalProfile(text, path, "", out info))
                    return false;
                profile = new ProfileListItem
                {
                    Path = path,
                    Name = string.IsNullOrWhiteSpace(info.Name) ? System.IO.Path.GetFileNameWithoutExtension(path) : info.Name,
                    Host = string.IsNullOrWhiteSpace(info.Host) ? "0.0.0.0" : info.Host,
                    Port = info.Port > 0 ? info.Port : 443,
                    Country = string.IsNullOrWhiteSpace(info.Country) ? "Unknown" : info.Country,
                    Protocol = string.IsNullOrWhiteSpace(info.Protocol) ? "External VPN" : info.Protocol
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static ProfileListItem From(string path, string activePath)
        {
            if (TryFromExternal(path, out var external))
                return external;
            try
            {
                var cfg = NvpConfig.Load(path);
                var server = cfg.Servers.FirstOrDefault();
                return new ProfileListItem
                {
                    Path = path,
                    Name = string.Equals(path, activePath, StringComparison.OrdinalIgnoreCase) ? "Active KRot VPS" : "KRot VPS",
                    Host = server?.Address ?? "0.0.0.0",
                    Port = server?.Port ?? 443,
                    Country = CountryFor(server?.Address ?? "")
                };
            }
            catch
            {
                return new ProfileListItem { Path = path, Name = "KRot profile", Host = "0.0.0.0", Port = 443 };
            }
        }
    }
}

internal sealed class NoraFxButton : WpfButton
{
    // Per-instance, NOT placed inside the ControlTemplate factory: WPF freezes Freezables
    // stored in a sealed template, which would make BeginAnimation throw. We attach these
    // to the realized Border in OnApplyTemplate instead, so they stay mutable/animatable.
    private readonly SolidColorBrush _fill;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Color _normal;
    private readonly Color _hover;

    public NoraFxButton(Color normal, Color hover, double radius, bool accent, Brush? stroke = null)
    {
        _normal = normal;
        _hover = hover;
        _fill = new SolidColorBrush(normal);
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        OverridesDefaultStyle = true;
        FocusVisualStyle = null;
        SnapsToDevicePixels = true;

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "PART_Fill";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.BorderBrushProperty, accent ? NoraWpfTheme.OrangeBrush : (stroke ?? NoraWpfTheme.StrokeBrush));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.RenderTransformOriginProperty, new Point(0.5, 0.5));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        Template = new ControlTemplate(typeof(NoraFxButton)) { VisualTree = border };
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Fill") is Border border)
        {
            border.Background = _fill;
            border.RenderTransform = _scale;
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Recolor(_hover, 150);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Recolor(_normal, 220);
        Pop(1.0, 240);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        Pop(0.95, 90);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        Pop(1.0, 220);
    }

    private void Recolor(Color to, double ms)
        => _fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    // Press feedback only scales inward (<= 1.0) with no overshoot, so a full-width button can
    // never grow past the clipped content area and get cropped. Hover feedback is color-only.
    private void Pop(double to, double ms)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }
}

// Composition-thread pulse (Storyboard on real shapes) — no per-frame UI work, perfectly smooth.
internal sealed class PulsingDot : Grid
{
    private readonly Ellipse _ring = new();
    private readonly Ellipse _core = new();
    private readonly ScaleTransform _ringScale = new(1, 1);
    private Color _tone = NoraWpfTheme.Green;

    public Color Tone
    {
        get => _tone;
        set { _tone = value; Apply(); }
    }

    public PulsingDot()
    {
        Width = 16;
        Height = 16;
        _ring.Width = 11;
        _ring.Height = 11;
        _ring.HorizontalAlignment = HorizontalAlignment.Center;
        _ring.VerticalAlignment = VerticalAlignment.Center;
        _ring.RenderTransformOrigin = new Point(0.5, 0.5);
        _ring.RenderTransform = _ringScale;
        _core.Width = 7;
        _core.Height = 7;
        _core.HorizontalAlignment = HorizontalAlignment.Center;
        _core.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_ring);
        Children.Add(_core);
        Apply();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private void Apply()
    {
        _ring.Fill = NoraWpfTheme.Brush(_tone);
        _core.Fill = NoraWpfTheme.Brush(_tone);
    }

    private void Start()
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseOut };
        var scale = new DoubleAnimation(0.75, 2.0, new Duration(TimeSpan.FromMilliseconds(1500))) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        var fade = new DoubleAnimation(0.5, 0.0, new Duration(TimeSpan.FromMilliseconds(1500))) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        _ringScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        _ringScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        _ring.BeginAnimation(OpacityProperty, fade);
    }

    private void Stop()
    {
        _ringScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _ringScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _ring.BeginAnimation(OpacityProperty, null);
    }
}

// Futuristic hero backdrop: a perspective wire grid with scanline scroll, drifting
// particles above the horizon, data pulses running down the grid lanes, and a soft
// parallax + glow response to the pointer. Everything is depth-sorted: particles
// far, grid mid, pulses near.
internal sealed class NoraCyberGrid : FrameworkElement
{
    private readonly record struct Pulse(int Lane, double Speed, double Offset);
    private readonly record struct BurstParticle(int Lane, double Delay, double Duration);
    private readonly record struct Star(double X, double Y, double Size, double Twinkle, double Drift);

    private static readonly Pulse[] Pulses =
    [
        new(-5, 0.08, 0.12),
        new(1, 0.11, 0.56),
        new(6, 0.07, 0.84)
    ];

    private static readonly BurstParticle[] ConnectedBurst =
    [
        new(-9, 0.00, 1.08), new(9, 0.02, 1.14),
        new(-8, 0.05, 1.20), new(8, 0.07, 1.10),
        new(-7, 0.09, 1.16), new(7, 0.11, 1.22),
        new(-5, 0.13, 1.08), new(5, 0.15, 1.18),
        new(-3, 0.17, 1.14), new(3, 0.19, 1.06),
        new(-1, 0.21, 1.18), new(1, 0.23, 1.12),
        new(-6, 0.26, 1.02), new(6, 0.28, 1.08),
        new(-4, 0.31, 1.04), new(4, 0.33, 1.10)
    ];

    private static readonly Star[] Stars = CreateStars();

    private TimeSpan _last = TimeSpan.MinValue;
    private double _t;
    private double _scroll;
    private Point? _pointer;
    private double _pointerX = 0.5, _pointerY = 0.5;
    private double _pointerGlow;
    private double _excite;
    private double _connectedBurstAge = -1;
    private bool _subscribed;

    public double Energy { get; set; } = 0.55; // 0..1: raised while connecting/connected

    public NoraCyberGrid()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (_subscribed)
                return;
            _last = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnRendering;
            _subscribed = true;
        };
        Unloaded += (_, _) =>
        {
            if (!_subscribed)
                return;
            CompositionTarget.Rendering -= OnRendering;
            _subscribed = false;
        };
    }

    private static Star[] CreateStars()
    {
        // Deterministic scatter (no per-run randomness so snapshots are stable).
        var stars = new Star[26];
        for (var i = 0; i < stars.Length; i++)
        {
            var fx = Frac(i * 0.6180339887 + 0.113);
            var fy = Frac(i * 0.7548776662 + 0.351);
            stars[i] = new Star(fx, 0.06 + fy * 0.75, 0.7 + Frac(i * 0.37) * 1.1, i * 1.7, 0.4 + Frac(i * 0.53) * 1.2);
        }
        return stars;
    }

    private static double Frac(double v) => v - Math.Floor(v);

    public void SetPointer(Point? relative)
    {
        _pointer = relative;
    }

    public void Excite()
    {
        _excite = 1;
    }

    public void CelebrateConnected()
    {
        if (!NoraWpfTheme.MotionEnabled)
            return;
        _connectedBurstAge = 0;
        _excite = Math.Max(_excite, 0.72);
        InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!NoraWpfTheme.MotionEnabled)
            return;
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var dt = _last == TimeSpan.MinValue ? 0.016 : Math.Clamp((now - _last).TotalSeconds, 0, 0.1);
        _last = now;
        _t += dt;
        _scroll = Frac(_scroll + dt * (0.055 + Energy * 0.075 + _excite * 0.10));
        var targetGlow = _pointer is null ? 0 : 1;
        _pointerGlow += (targetGlow - _pointerGlow) * Math.Min(1, dt * 6);
        if (_pointer is { } p && ActualWidth > 0 && ActualHeight > 0)
        {
            var tx = Math.Clamp(p.X / ActualWidth, 0, 1);
            var ty = Math.Clamp(p.Y / ActualHeight, 0, 1);
            _pointerX += (tx - _pointerX) * Math.Min(1, dt * 7);
            _pointerY += (ty - _pointerY) * Math.Min(1, dt * 7);
        }
        else
        {
            _pointerX += (0.5 - _pointerX) * Math.Min(1, dt * 2.2);
            _pointerY += (0.5 - _pointerY) * Math.Min(1, dt * 2.2);
        }
        _excite = Math.Max(0, _excite - dt * 0.8);
        if (_connectedBurstAge >= 0)
        {
            _connectedBurstAge += dt;
            if (_connectedBurstAge > 1.62)
                _connectedBurstAge = -1;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        var energy = Math.Clamp(Energy + _excite * 0.35, 0, 1.25);
        var parX = (_pointerX - 0.5) * 14 * (0.4 + _pointerGlow * 0.6);
        var parY = (_pointerY - 0.5) * 6 * (0.4 + _pointerGlow * 0.6);
        var horizonY = h * 0.34 + parY;
        var vp = new Point(w * 0.5 + parX, horizonY);
        var pointerPx = new Point(_pointerX * w, _pointerY * h);

        // --- far layer: drifting particles above (and slightly below) the horizon
        foreach (var star in Stars)
        {
            var sx = Frac(star.X + _t * 0.004 * star.Drift) * w;
            var sy = star.Y * h + Math.Sin(_t * 0.5 + star.Twinkle) * 2.4;
            var tw = (Math.Sin(_t * (0.6 + star.Drift * 0.5) + star.Twinkle) + 1) * 0.5;
            var alpha = (byte)(14 + tw * (sy < horizonY ? 44 : 20) * (0.6 + energy * 0.5));
            dc.DrawEllipse(NoraWpfTheme.Brush(NoraWpfTheme.With(NoraWpfTheme.Orange2, alpha)), null, new Point(sx, sy), star.Size, star.Size);
        }

        // --- horizon glow line
        var horizonBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 255, 156, 38), 0),
                new GradientStop(NoraWpfTheme.With(NoraWpfTheme.Orange, (byte)(46 + energy * 42)), 0.5),
                new GradientStop(Color.FromArgb(0, 255, 156, 38), 1)
            }
        };
        horizonBrush.Freeze();
        dc.DrawRectangle(horizonBrush, null, new Rect(0, horizonY - 0.7, w, 1.4));

        // --- mid layer: perspective grid
        const int lanes = 9;
        var laneSpread = w * 0.135;
        Point LanePoint(int lane, double depth)
        {
            // depth 0 at horizon, 1 at bottom edge
            var bx = vp.X + lane * laneSpread * 2.6;
            var by = h + 26;
            return new Point(vp.X + (bx - vp.X) * depth, vp.Y + (by - vp.Y) * depth);
        }

        for (var lane = -lanes; lane <= lanes; lane++)
        {
            var bottom = LanePoint(lane, 1);
            var near = 1 - Math.Min(1, Math.Abs(bottom.X - pointerPx.X) / (w * 0.34));
            var boost = near * near * _pointerGlow * 60;
            var alpha = (byte)Math.Clamp(10 + (lanes - Math.Abs(lane)) * 2.2 + energy * 14 + boost, 6, 110);
            var pen = new Pen(NoraWpfTheme.Brush(NoraWpfTheme.With(NoraWpfTheme.Orange, alpha)), lane == 0 ? 1.0 : 0.8);
            pen.Freeze();
            dc.DrawLine(pen, LanePoint(lane, 0.02), bottom);
        }

        const int rows = 11;
        for (var row = 0; row < rows; row++)
        {
            var z = Frac((row + _scroll * rows) / rows);
            var depth = Math.Pow(z, 2.05);
            var y = vp.Y + (h + 26 - vp.Y) * depth;
            if (y <= vp.Y + 1)
                continue;
            var fade = Math.Sin(Math.Min(1, z) * Math.PI);
            var nearRow = 1 - Math.Min(1, Math.Abs(y - pointerPx.Y) / (h * 0.30));
            var boost = nearRow * nearRow * _pointerGlow * 46;
            var alpha = (byte)Math.Clamp(6 + fade * (26 + energy * 26) + boost, 4, 120);
            var pen = new Pen(NoraWpfTheme.Brush(NoraWpfTheme.With(NoraWpfTheme.Orange, alpha)), 0.8 + depth * 0.5);
            pen.Freeze();
            var spreadHere = laneSpread * 2.6 * depth * lanes;
            dc.DrawLine(pen, new Point(Math.Max(-30, vp.X - spreadHere), y), new Point(Math.Min(w + 30, vp.X + spreadHere), y));
        }

        // --- near layer: data pulses running down the lanes
        foreach (var pulse in Pulses)
        {
            var z = Frac(pulse.Offset + _t * pulse.Speed * (0.55 + energy * 0.55));
            var depth = Math.Pow(z, 1.9);
            var pos = LanePoint(pulse.Lane, Math.Max(0.03, depth));
            var strength = Math.Sin(Math.Min(1, z) * Math.PI);
            var size = 1.1 + depth * 2.6;
            var alpha = (byte)Math.Clamp(strength * (95 + energy * 90), 0, 210);
            // short trail toward the horizon
            var tail = LanePoint(pulse.Lane, Math.Max(0.02, depth - 0.10 - depth * 0.06));
            var trailBrush = new LinearGradientBrush(
                Color.FromArgb(0, 255, 156, 38),
                NoraWpfTheme.With(NoraWpfTheme.Orange, (byte)(alpha / 2)),
                new Point(0, 0), new Point(1, 1));
            trailBrush.Freeze();
            var trailPen = new Pen(trailBrush, size * 0.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            trailPen.Freeze();
            dc.DrawLine(trailPen, tail, pos);
            dc.DrawEllipse(NoraWpfTheme.Brush(NoraWpfTheme.With(NoraWpfTheme.Orange, (byte)(alpha / 3))), null, pos, size + 3.4, size + 3.4);
            dc.DrawEllipse(NoraWpfTheme.Brush(NoraWpfTheme.Lerp(NoraWpfTheme.Orange, Colors.White, 0.30)), null, pos, size, size);
        }

        // One deliberate success beat: a short front of packets races from the
        // horizon down every side of the perspective field, then ambient motion resumes.
        if (_connectedBurstAge >= 0)
        {
            foreach (var particle in ConnectedBurst)
            {
                var p = (_connectedBurstAge - particle.Delay) / particle.Duration;
                if (p is < 0 or > 1)
                    continue;

                var eased = 1 - Math.Pow(1 - p, 1.72);
                var depth = 0.035 + eased * 0.76;
                var pos = LanePoint(particle.Lane, depth);
                var tailDepth = Math.Max(0.02, depth - 0.06 - eased * 0.10);
                var tail = LanePoint(particle.Lane, tailDepth);
                var envelope = Math.Pow(Math.Sin(p * Math.PI), 0.62);
                var alpha = (byte)Math.Clamp(envelope * 238, 0, 238);
                var size = 1.5 + eased * 4.2;
                var success = NoraWpfTheme.Lerp(NoraWpfTheme.Green, Colors.White, 0.38);
                var trail = new LinearGradientBrush(
                    Color.FromArgb(0, success.R, success.G, success.B),
                    NoraWpfTheme.With(success, (byte)(alpha * 0.72)),
                    new Point(0, 0), new Point(1, 1));
                trail.Freeze();
                var trailPen = new Pen(trail, 1.0 + eased * 2.5)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                trailPen.Freeze();
                dc.DrawLine(trailPen, tail, pos);
                dc.DrawEllipse(NoraWpfTheme.Brush(NoraWpfTheme.With(NoraWpfTheme.Green, (byte)(alpha / 3))), null, pos, size + 4, size + 4);
                dc.DrawEllipse(NoraWpfTheme.Brush(NoraWpfTheme.With(success, alpha)), null, pos, size, size);
            }
        }

        // --- pointer aura
        if (_pointerGlow > 0.02)
        {
            var aura = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
                GradientStops =
                {
                    new GradientStop(NoraWpfTheme.With(NoraWpfTheme.Orange, (byte)(26 * _pointerGlow)), 0),
                    new GradientStop(Color.FromArgb(0, 255, 156, 38), 1)
                }
            };
            aura.Freeze();
            dc.DrawEllipse(aura, null, pointerPx, 110, 110);
        }
    }
}

internal sealed class NoraAudioBarBackdrop : FrameworkElement
{
    private const int DisplayBandCount = 24;
    private const double DecayLerp = 0.075;
    private const float SilenceThreshold = 0.025f;
    private readonly float[] _target = new float[DisplayBandCount];
    private readonly float[] _display = new float[DisplayBandCount];
    private TimeSpan _last = TimeSpan.MinValue;
    private double _visibility;
    private double _silenceAge;
    private bool _subscribed;
    private bool _audioAttached;
    private bool _audioDiagnosticWritten;
    private DateTimeOffset _audioAttachedAt = DateTimeOffset.MinValue;
    private readonly SolidColorBrush _barBrush = new(NoraWpfTheme.Orange);
    private Color _accent = NoraWpfTheme.Orange;
    private Color _accentTarget = NoraWpfTheme.Orange;

    // Keeps the spectrum bars on the same accent as the Connect button:
    // orange when idle, orange2 while connecting/stopping, green when
    // connected, red on failure. The color eases in the render loop.
    public void SetAccent(Color color) => _accentTarget = color;

    public Action<string>? DiagnosticLog { get; set; }

    public NoraAudioBarBackdrop()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (_subscribed)
                return;
            _last = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnRendering;
            _subscribed = true;
            UpdateAudioAttachment();
            InvalidateVisual();
        };
        IsVisibleChanged += (_, _) => UpdateAudioAttachment();
        Unloaded += (_, _) =>
        {
            if (!_subscribed)
                return;
            CompositionTarget.Rendering -= OnRendering;
            _subscribed = false;
            if (_audioAttached)
            {
                NoraSpectrumAnalyzer.Shared.Detach();
                _audioAttached = false;
            }
        };
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            _last = TimeSpan.MinValue;
            return;
        }
        if (!NoraWpfTheme.MotionEnabled)
        {
            if (_visibility > 0)
            {
                _visibility = 0;
                InvalidateVisual();
            }
            return;
        }

        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var dt = _last == TimeSpan.MinValue ? 1.0 / 60.0 : Math.Clamp((now - _last).TotalSeconds, 0, 0.1);
        _last = now;
        _accent = NoraWpfTheme.Lerp(_accent, _accentTarget, Math.Min(1, dt * 6.5));
        var spectrum = NoraSpectrumAnalyzer.Shared.GetSpectrum();
        UpdateSpectrum(spectrum, dt);
        WriteAudioDiagnosticWhenReady();
        InvalidateVisual();
    }

    private void UpdateAudioAttachment()
    {
        var shouldAttach = _subscribed && IsVisible && NoraWpfTheme.MotionEnabled;
        if (shouldAttach && !_audioAttached)
        {
            NoraSpectrumAnalyzer.Shared.Attach();
            _audioAttached = true;
            _audioDiagnosticWritten = false;
            _audioAttachedAt = DateTimeOffset.UtcNow;
            DiagnosticLog?.Invoke("Audio visualizer: initializing playback-output capture.");
        }
        else if (!shouldAttach && _audioAttached)
        {
            NoraSpectrumAnalyzer.Shared.Detach();
            _audioAttached = false;
            _audioAttachedAt = DateTimeOffset.MinValue;
        }
    }

    private void WriteAudioDiagnosticWhenReady()
    {
        if (!_audioAttached || _audioDiagnosticWritten ||
            _audioAttachedAt == DateTimeOffset.MinValue ||
            DateTimeOffset.UtcNow - _audioAttachedAt < TimeSpan.FromSeconds(2))
            return;

        _audioDiagnosticWritten = true;
        DiagnosticLog?.Invoke($"Audio visualizer: {NoraSpectrumAnalyzer.Shared.GetDiagnostic()}.");
    }

    private void UpdateSpectrum(float[] spectrum, double dt)
    {
        var peak = 0f;
        for (var band = 0; band < DisplayBandCount; band++)
        {
            var lo = spectrum[band * 2];
            var hi = spectrum[band * 2 + 1];
            _target[band] = Math.Clamp((lo + hi) * 0.5f, 0, 1);
            peak = Math.Max(peak, _target[band]);
        }

        var displayPeak = 0f;
        for (var band = 0; band < DisplayBandCount; band++)
        {
            var frameBlend = _target[band] > _display[band]
                ? 1 - Math.Pow(1 - 0.34, dt * 60)
                : 1 - Math.Pow(1 - DecayLerp, dt * 60);
            _display[band] +=
                (float)((_target[band] - _display[band]) * frameBlend);
            displayPeak = Math.Max(displayPeak, _display[band]);
        }

        _silenceAge = peak > SilenceThreshold ? 0 : _silenceAge + dt;
        var activityPeak = Math.Max(peak, displayPeak);
        var visibilityTarget = _silenceAge >= 0.32
            ? 0
            : Math.Clamp((activityPeak - SilenceThreshold) / 0.12, 0, 1);
        var visibilityBlend = visibilityTarget > _visibility
            ? 1 - Math.Pow(1 - 0.42, dt * 60)
            : 1 - Math.Pow(1 - 0.24, dt * 60);
        _visibility += (visibilityTarget - _visibility) * visibilityBlend;
        if (visibilityTarget == 0 && _visibility < 0.01)
            _visibility = 0;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_visibility <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var centerX = ActualWidth / 2;
        var centerY = ActualHeight / 2;
        const double protectedHalfWidth = 116;
        const double outerPadding = 6;
        const double gap = 1.55;
        var wingWidth = centerX - protectedHalfWidth - outerPadding;
        var barWidth = (wingWidth - gap * (DisplayBandCount - 1)) / DisplayBandCount;
        var maximumHeight = ActualHeight * 0.78;
        _barBrush.Color = _accent;

        for (var band = 0; band < DisplayBandCount; band++)
        {
            var value = Math.Clamp(_display[band], 0, 1);
            var shaped = Math.Pow(value, 0.72);
            var barHeight = shaped * maximumHeight;
            if (barHeight < 0.75)
                continue;

            var offset = band * (barWidth + gap);
            var left = centerX - protectedHalfWidth - barWidth - offset;
            var right = centerX + protectedHalfWidth + offset;
            var top = centerY - barHeight / 2;
            var opacity = _visibility * (0.24 + shaped * 0.66);
            var radius = Math.Min(1.1, barWidth / 2);

            dc.PushOpacity(opacity);
            dc.DrawRoundedRectangle(_barBrush, null, new Rect(left, top, barWidth, barHeight), radius, radius);
            dc.DrawRoundedRectangle(_barBrush, null, new Rect(right, top, barWidth, barHeight), radius, radius);
            dc.Pop();
        }
    }
}

internal sealed class NoraConnectButton : WpfButton
{
    private static readonly RadialGradientBrush DiscBrush = CreateDiscBrush();
    private static readonly Pen DiscPen = FrozenPen(Color.FromRgb(40, 47, 58), 1);
    private static readonly Pen InnerPen = FrozenPen(Color.FromRgb(8, 10, 14), 2);
    private static readonly Pen TrackPen = FrozenPen(Color.FromRgb(31, 37, 47), 4);

    private readonly SolidColorBrush _accentBrush = new(NoraWpfTheme.Orange);
    private readonly SolidColorBrush _glowBrush = new(NoraWpfTheme.With(NoraWpfTheme.Orange, 40));
    private readonly ScaleTransform _renderScale = new();
    private readonly RotateTransform _renderRotate = new();
    private readonly Pen _ringPen;
    private readonly Pen _iconPen;
    private double _angle;
    private double _pulseT;
    private double _hover;
    private double _press;
    private double _ripple = 1;
    private Color _accent = NoraWpfTheme.Orange;
    private TimeSpan _last = TimeSpan.MinValue;
    private bool _subscribed;

    private double _geoW = -1, _geoH = -1;
    private Geometry? _powerIcon;
    private Geometry? _connectingArc;
    private FormattedText? _mainFt;
    private FormattedText? _previousMainFt;
    private FormattedText? _detailFt;
    private string _mainCache = "\0";
    private string _previousMainCache = "\0";
    private string _detailCache = "\0";
    private string _shownMain = "Connect";
    private string _previousMain = "";
    private double _textTransition = 1;
    public string MainText { get; set; } = "Connect";
    public string DetailText { get; set; } = "00:00:00";
    public bool IsConnected { get; set; }
    public bool IsProgress { get; set; }
    public bool IsFailed { get; set; }
    public Color ConnectedAccent { get; set; } = NoraWpfTheme.Green;

    public NoraConnectButton()
    {
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        OverridesDefaultStyle = true;
        FocusVisualStyle = null;
        _ringPen = RoundPen(_accentBrush, 4.5);
        _iconPen = RoundPen(_accentBrush, 5.2);
        Template = new ControlTemplate(typeof(WpfButton))
        {
            VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
        };
        Loaded += (_, _) =>
        {
            if (_subscribed) return;
            _last = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnRendering;
            _subscribed = true;
        };
        Unloaded += (_, _) =>
        {
            if (!_subscribed) return;
            CompositionTarget.Rendering -= OnRendering;
            _subscribed = false;
        };
        Click += (_, _) => _ripple = 0;
    }

    public void SnapConnectedPreview(TimeSpan duration)
    {
        IsConnected = true;
        IsProgress = false;
        IsFailed = false;
        MainText = "Connected";
        DetailText = duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        _accent = ConnectedAccent;
        _shownMain = "Connected";
        _previousMain = "";
        _textTransition = 1;
        InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            _last = TimeSpan.MinValue;
            return;
        }
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var dt = _last == TimeSpan.MinValue ? 0.016 : Math.Clamp((now - _last).TotalSeconds, 0, 0.1);
        _last = now;

        var motion = NoraWpfTheme.MotionEnabled;
        if (!string.Equals(MainText, _shownMain, StringComparison.Ordinal))
        {
            _previousMain = _shownMain;
            _shownMain = MainText;
            _textTransition = motion ? 0 : 1;
            _mainFt = null;
            _previousMainFt = null;
        }

        var stopping = IsProgress && MainText.StartsWith("Stopping", StringComparison.OrdinalIgnoreCase);
        var spinSpeed = motion && IsProgress ? (stopping ? -150.0 : 230.0) : 0.0;
        _angle = (_angle + spinSpeed * dt) % 360;
        if (motion)
            _pulseT += dt;
        _hover = Approach(_hover, IsMouseOver ? 1 : 0, dt, 12);
        _press = Approach(_press, IsPressed ? 1 : 0, dt, 22);
        var target = IsFailed ? NoraWpfTheme.Red : IsConnected ? ConnectedAccent : IsProgress ? NoraWpfTheme.Orange2 : NoraWpfTheme.Orange;
        _accent = LerpApproach(_accent, target, dt, IsConnected ? 3.4 : 6.5);
        if (_ripple < 1)
            _ripple = Math.Min(1, _ripple + dt * 2.2);
        _textTransition = Math.Min(1, _textTransition + dt / 0.24);
        InvalidateVisual();
    }

    private static double Approach(double cur, double target, double dt, double speed)
        => cur + (target - cur) * Math.Min(1, dt * speed);

    private static Color LerpApproach(Color cur, Color target, double dt, double speed)
        => NoraWpfTheme.Lerp(cur, target, Math.Min(1, dt * speed));

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        var center = new Point(w / 2, h / 2);
        var radius = Math.Min(w, h) / 2 - 15;
        var ringRadius = radius - 18;

        if (_geoW != w || _geoH != h)
        {
            _geoW = w;
            _geoH = h;
            var iconCenter = new Point(center.X, center.Y - 24);
            var icon = new GeometryGroup();
            icon.Children.Add(new LineGeometry(new Point(iconCenter.X, iconCenter.Y - 34), new Point(iconCenter.X, iconCenter.Y - 9)));
            icon.Children.Add(Arc(iconCenter, 36, 132, 276));
            icon.Freeze();
            _powerIcon = icon;
            _connectingArc = Arc(center, ringRadius, -90, 130);
        }

        _accentBrush.Color = _accent;

        // Hover growth is kept subtle so the button never rides over the server card below.
        var scale = 1 + 0.016 * _hover - 0.045 * _press;
        _renderScale.ScaleX = scale;
        _renderScale.ScaleY = scale;
        _renderScale.CenterX = center.X;
        _renderScale.CenterY = center.Y;
        dc.PushTransform(_renderScale);

        var breathing = IsConnected
            ? (Math.Sin(_pulseT * Math.PI * 2 / 4.8) + 1) * 0.5
            : (Math.Sin(_pulseT * Math.PI * 2 / 7.5) + 1) * 0.5;
        var glow = (byte)Math.Clamp(16 + _hover * 24 + breathing * (IsConnected ? 28 : 16), 0, 86);
        _glowBrush.Color = NoraWpfTheme.With(_accent, glow);
        dc.DrawEllipse(_glowBrush, null, center, radius + 21 + breathing * 3, radius + 21 + breathing * 3);
        dc.DrawEllipse(null, new Pen(NoraWpfTheme.Brush(NoraWpfTheme.With(_accent, (byte)(34 + breathing * 36))), 11), center, radius + 10, radius + 10);
        dc.DrawEllipse(null, new Pen(NoraWpfTheme.Brush(NoraWpfTheme.With(_accent, 102)), 1.2), center, radius + 16, radius + 16);

        if (_ripple < 1)
        {
            var rippleRadius = radius * 0.5 + radius * 0.7 * _ripple;
            dc.DrawEllipse(null, new Pen(NoraWpfTheme.Brush(NoraWpfTheme.With(_accent, (byte)(90 * (1 - _ripple)))), 3), center, rippleRadius, rippleRadius);
        }

        dc.DrawEllipse(DiscBrush, DiscPen, center, radius, radius);
        dc.DrawEllipse(null, InnerPen, center, radius - 8, radius - 8);
        dc.DrawEllipse(null, new Pen(NoraWpfTheme.Brush(Color.FromArgb(54, 146, 157, 176)), 1), center, radius - 15, radius - 15);
        dc.DrawEllipse(null, TrackPen, center, ringRadius, ringRadius);

        if (IsProgress)
        {
            dc.PushTransform(Rotate(center));
            dc.DrawGeometry(null, _ringPen, _connectingArc);
            dc.Pop();
        }
        else
        {
            dc.DrawEllipse(null, _ringPen, center, ringRadius, ringRadius);
        }

        dc.DrawGeometry(null, _iconPen, _powerIcon);

        if (_mainFt is null || _mainCache != _shownMain)
        {
            _mainCache = _shownMain;
            _mainFt = MakeText(_shownMain, 21, FontWeights.Bold, NoraWpfTheme.TextBrush);
        }
        if (!string.IsNullOrWhiteSpace(_previousMain) && (_previousMainFt is null || _previousMainCache != _previousMain))
        {
            _previousMainCache = _previousMain;
            _previousMainFt = MakeText(_previousMain, 21, FontWeights.Bold, NoraWpfTheme.TextBrush);
        }
        if (_detailFt is null || _detailCache != DetailText)
        {
            _detailCache = DetailText;
            _detailFt = MakeText(DetailText, 12, FontWeights.Normal, NoraWpfTheme.MutedBrush);
        }
        var textEase = _textTransition * _textTransition * (3 - 2 * _textTransition);
        var textY = center.Y + 36;
        if (_previousMainFt is not null && _textTransition < 1)
        {
            dc.PushOpacity(1 - textEase);
            dc.DrawText(_previousMainFt, new Point((w - _previousMainFt.Width) / 2, textY - 9 * textEase - _previousMainFt.Height / 2));
            dc.Pop();
        }
        dc.PushOpacity(Math.Max(0.01, textEase));
        dc.DrawText(_mainFt, new Point((w - _mainFt.Width) / 2, textY + 9 * (1 - textEase) - _mainFt.Height / 2));
        dc.Pop();
        dc.DrawText(_detailFt, new Point((w - _detailFt.Width) / 2, center.Y + 64 - _detailFt.Height / 2));

        dc.Pop();
    }

    private static Pen RoundPen(Brush brush, double thickness)
        => new(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    private RotateTransform Rotate(Point center)
    {
        _renderRotate.Angle = _angle;
        _renderRotate.CenterX = center.X;
        _renderRotate.CenterY = center.Y;
        return _renderRotate;
    }

    private static RadialGradientBrush CreateDiscBrush()
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.42, 0.35),
            GradientOrigin = new Point(0.36, 0.28),
            RadiusX = 0.74,
            RadiusY = 0.74,
            GradientStops =
            {
                new GradientStop(Color.FromRgb(31, 36, 44), 0),
                new GradientStop(Color.FromRgb(18, 22, 29), 0.56),
                new GradientStop(Color.FromRgb(9, 12, 17), 1)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(NoraWpfTheme.Brush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private FormattedText MakeText(string text, double size, FontWeight weight, Brush brush)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Geometry Arc(Point center, double radius, double startDeg, double sweepDeg)
    {
        var start = PointOnCircle(center, radius, startDeg);
        var end = PointOnCircle(center, radius, startDeg + sweepDeg);
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, Math.Abs(sweepDeg) > 180,
            sweepDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
        var geo = new PathGeometry([fig]);
        geo.Freeze();
        return geo;
    }

    private static Point PointOnCircle(Point center, double radius, double deg)
    {
        var rad = deg * Math.PI / 180;
        return new Point(center.X + Math.Cos(rad) * radius, center.Y + Math.Sin(rad) * radius);
    }
}

internal sealed class NoraTrafficGraph : FrameworkElement
{
    private readonly record struct TrafficSample(double Time, double Up, double Down);
    private const double WindowSeconds = 16;
    private const double ExpectedSampleSeconds = 0.25;
    private readonly Queue<TrafficSample> _samples = new();
    private double _phase;
    private double _lastFrameTime = double.NaN;
    private double _smoothedUp;
    private double _smoothedDown;
    private double _displayScale = 1;
    private bool _renderingAttached;
    private bool _active;
    private long _upRate;
    private long _downRate;
    private long _upBytes;
    private long _downBytes;
    private TimeSpan _duration;
    private double _hover;
    private readonly TranslateTransform _lift = new();

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
                return;
            _active = value;
            InvalidateVisual();
        }
    }

    private static readonly LinearGradientBrush AreaFill = CreateAreaFill();
    private static readonly SolidColorBrush BgBrush = NoraWpfTheme.Brush(Color.FromArgb(224, 17, 22, 30));
    private static readonly SolidColorBrush GlowBrush = NoraWpfTheme.Brush(Color.FromArgb(70, 255, 156, 38));
    private static readonly Pen BgPen = Frozen(new Pen(NoraWpfTheme.StrokeBrush, 1));
    private static readonly Pen GridPen = Frozen(new Pen(NoraWpfTheme.Brush(Color.FromArgb(50, 80, 91, 110)), 0.8) { DashStyle = DashStyles.Dash });
    private static readonly Pen IdlePen = Frozen(new Pen(NoraWpfTheme.Brush(Color.FromArgb(105, 146, 157, 176)), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    private static readonly Pen GlowPen = Frozen(new Pen(GlowBrush, 7) { LineJoin = PenLineJoin.Round });
    private static readonly Pen LinePen = Frozen(new Pen(NoraWpfTheme.OrangeBrush, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
    private static readonly Pen UpPen = Frozen(new Pen(NoraWpfTheme.Brush(Color.FromArgb(190, 104, 141, 255)), 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
    private static readonly Pen IconPen = Frozen(new Pen(NoraWpfTheme.OrangeBrush, 2.5));

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }

    private static LinearGradientBrush CreateAreaFill()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 156, 38), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(30, 255, 156, 38), 0.62));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 156, 38), 1));
        brush.Freeze();
        return brush;
    }

    public NoraTrafficGraph()
    {
        RenderTransform = _lift;
        Loaded += (_, _) => AttachRendering();
        Unloaded += (_, _) => DetachRendering();
    }

    public void Seed(IEnumerable<(long Up, long Down)> history, long upRate, long downRate, long upBytes, long downBytes, TimeSpan duration)
    {
        _samples.Clear();
        var seed = history.TakeLast(64).ToArray();
        var now = NowSeconds();
        for (var index = 0; index < seed.Length; index++)
        {
            var sample = seed[index];
            var time = now - (seed.Length - 1 - index) * ExpectedSampleSeconds;
            _samples.Enqueue(new TrafficSample(time, Math.Max(0, sample.Up), Math.Max(0, sample.Down)));
        }
        if (_samples.TryPeek(out _))
        {
            var latest = _samples.Last();
            _smoothedUp = latest.Up;
            _smoothedDown = latest.Down;
            _displayScale = Math.Max(1, _samples.Max(sample => Math.Max(sample.Up, sample.Down)) * 1.08);
        }
        SetTraffic(upRate, downRate, upBytes, downBytes, duration);
    }

    public void AddSample(long up, long down, long upBytes, long downBytes, TimeSpan duration)
    {
        var now = NowSeconds();
        var rawUp = Math.Max(0, up);
        var rawDown = Math.Max(0, down);
        if (_samples.Count == 0)
        {
            _smoothedUp = rawUp;
            _smoothedDown = rawDown;
        }
        else
        {
            _smoothedUp = SmoothRate(_smoothedUp, rawUp, ExpectedSampleSeconds);
            _smoothedDown = SmoothRate(_smoothedDown, rawDown, ExpectedSampleSeconds);
        }
        _samples.Enqueue(new TrafficSample(now, _smoothedUp, _smoothedDown));
        TrimOldSamples(now);
        SetTraffic(up, down, upBytes, downBytes, duration);
        InvalidateVisual();
    }

    public void SetTraffic(long up, long down, long upBytes, long downBytes, TimeSpan duration)
    {
        _upRate = Math.Max(0, up);
        _downRate = Math.Max(0, down);
        _upBytes = Math.Max(0, upBytes);
        _downBytes = Math.Max(0, downBytes);
        _duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        InvalidateVisual();
    }

    public void ResetSamples()
    {
        _samples.Clear();
        _smoothedUp = 0;
        _smoothedDown = 0;
        _displayScale = 1;
        _upRate = _downRate = _upBytes = _downBytes = 0;
        _duration = TimeSpan.Zero;
        InvalidateVisual();
    }

    private void AttachRendering()
    {
        if (_renderingAttached)
            return;
        _renderingAttached = true;
        _lastFrameTime = double.NaN;
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    private void DetachRendering()
    {
        if (!_renderingAttached)
            return;
        _renderingAttached = false;
        CompositionTarget.Rendering -= OnCompositionRendering;
        _lastFrameTime = double.NaN;
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        var now = NowSeconds();
        var dt = double.IsNaN(_lastFrameTime) ? 1d / 60d : Math.Clamp(now - _lastFrameTime, 1d / 240d, 0.1);
        _lastFrameTime = now;
        Tick(dt, now);
    }

    private void Tick(double dt, double now)
    {
        if (NoraWpfTheme.MotionEnabled)
            _phase = (_phase + dt * 2.4) % (Math.PI * 2);
        var targetHover = IsMouseOver ? 1d : 0d;
        _hover += (targetHover - _hover) * (1 - Math.Exp(-dt * 9));
        _lift.Y = NoraWpfTheme.MotionEnabled ? -3 * _hover : 0;
        TrimOldSamples(now);
        var targetScale = Math.Max(1, Math.Max(
            Math.Max(_smoothedUp, _smoothedDown),
            _samples.Count == 0 ? 0 : _samples.Max(sample => Math.Max(sample.Up, sample.Down))) * 1.08);
        var scaleSpeed = targetScale > _displayScale ? 8.5 : 1.15;
        _displayScale += (targetScale - _displayScale) * (1 - Math.Exp(-dt * scaleSpeed));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        var border = new Pen(NoraWpfTheme.Brush(NoraWpfTheme.Lerp(NoraWpfTheme.Stroke, NoraWpfTheme.Orange, _hover * 0.46)), 1 + _hover * 0.25);
        border.Freeze();
        dc.DrawRoundedRectangle(BgBrush, border, rect, 22, 22);
        if (_hover > 0.01)
            dc.DrawRoundedRectangle(null, new Pen(NoraWpfTheme.Brush(Color.FromArgb((byte)(22 * _hover), 255, 156, 38)), 5), new Rect(3, 3, ActualWidth - 6, ActualHeight - 6), 20, 20);
        DrawText(dc, "LIVE TRAFFIC", 14, FontWeights.SemiBold, NoraWpfTheme.Brush(Color.FromRgb(244, 185, 105)), new Point(52, 18));
        DrawGraphIcon(dc, new Point(22, 24));

        if (Active)
        {
            // Rates are color-coded to match the curves: orange = down, blue = up.
            var upText = MakeText($"↑ {FormatRate(_upRate)}", 11, FontWeights.SemiBold, NoraWpfTheme.Brush(Color.FromRgb(150, 172, 235)));
            var downText = MakeText($"↓ {FormatRate(_downRate)}", 11, FontWeights.SemiBold, NoraWpfTheme.Brush(Color.FromRgb(255, 186, 110)));
            dc.DrawText(upText, new Point(ActualWidth - 20 - upText.Width, 20));
            dc.DrawText(downText, new Point(ActualWidth - 20 - upText.Width - 14 - downText.Width, 20));
        }

        var plot = new Rect(22, 50, ActualWidth - 44, 78);
        for (var i = 0; i < 5; i++)
        {
            var y = plot.Top + i * plot.Height / 4;
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
        for (var i = 1; i < 6; i++)
        {
            var x = plot.Left + i * plot.Width / 6;
            dc.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        var now = NowSeconds();
        var (upPoints, downPoints) = BuildCurvePoints(plot, now);
        var live = Active && downPoints.Count >= 2;
        if (live)
        {
            DrawCurve(dc, upPoints, plot, UpPen, null, null, false);
            DrawCurve(dc, downPoints, plot, LinePen, GlowPen, AreaFill, true);
        }
        else
        {
            DrawWaitingState(dc, plot);
        }

        var metricsTop = 145d;
        var col = ActualWidth / 3;
        DrawMetric(dc, "DATA IN", FormatBytes(_downBytes), new Point(22, metricsTop));
        DrawMetric(dc, "DURATION", FormatDuration(_duration), new Point(col + 8, metricsTop));
        DrawMetric(dc, "DATA OUT", FormatBytes(_upBytes), new Point(col * 2 + 4, metricsTop));
        dc.DrawLine(Frozen(new Pen(NoraWpfTheme.Brush(Color.FromArgb(46, 146, 157, 176)), 1)), new Point(col, metricsTop + 1), new Point(col, ActualHeight - 18));
        dc.DrawLine(Frozen(new Pen(NoraWpfTheme.Brush(Color.FromArgb(46, 146, 157, 176)), 1)), new Point(col * 2, metricsTop + 1), new Point(col * 2, ActualHeight - 18));
    }

    private (IReadOnlyList<Point> Up, IReadOnlyList<Point> Down) BuildCurvePoints(Rect plot, double now)
    {
        var up = new List<Point>(_samples.Count + 1);
        var down = new List<Point>(_samples.Count + 1);
        foreach (var sample in _samples)
        {
            var x = MapX(sample.Time, now, plot.Left, plot.Width);
            if (x < plot.Left - 2 || x > plot.Right + 2)
                continue;
            up.Add(new Point(x, MapY(sample.Up, plot)));
            down.Add(new Point(x, MapY(sample.Down, plot)));
        }

        if (_samples.Count > 0)
        {
            var headX = plot.Right;
            if (up.Count == 0 || headX - up[^1].X > 0.25)
            {
                up.Add(new Point(headX, MapY(_smoothedUp, plot)));
                down.Add(new Point(headX, MapY(_smoothedDown, plot)));
            }
        }
        return (up, down);
    }

    private double MapY(double value, Rect plot)
    {
        var normalized = Math.Clamp(value / Math.Max(1, _displayScale), 0, 1);
        return plot.Bottom - 4 - normalized * (plot.Height - 10);
    }

    private static double MapX(double sampleTime, double renderTime, double left, double width)
        => left + width - (renderTime - sampleTime) / WindowSeconds * width;

    private static double SmoothRate(double current, double target, double dt)
    {
        var speed = target > current ? 5.5 : 2.2;
        return current + (target - current) * (1 - Math.Exp(-Math.Max(0, dt) * speed));
    }

    private void TrimOldSamples(double now)
    {
        while (_samples.Count > 0 && now - _samples.Peek().Time > WindowSeconds + ExpectedSampleSeconds)
            _samples.Dequeue();
    }

    private static double NowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private void DrawCurve(DrawingContext dc, IReadOnlyList<Point> points, Rect plot, Pen pen, Pen? glowPen, Brush? fill, bool headDot)
    {
        if (points.Count < 2)
            return;

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            for (var i = 0; i < points.Count - 1; i++)
            {
                var p0 = points[Math.Max(0, i - 1)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Math.Min(points.Count - 1, i + 2)];
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                ctx.BezierTo(c1, c2, p2, true, true);
            }
        }
        line.Freeze();

        if (fill is not null)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(points[0].X, plot.Bottom), true, true);
                ctx.LineTo(points[0], true, true);
                for (var i = 0; i < points.Count - 1; i++)
                {
                    var p0 = points[Math.Max(0, i - 1)];
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    var p3 = points[Math.Min(points.Count - 1, i + 2)];
                    var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                    var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                    ctx.BezierTo(c1, c2, p2, true, true);
                }
                ctx.LineTo(new Point(points[^1].X, plot.Bottom), true, true);
            }
            area.Freeze();
            dc.DrawGeometry(fill, null, area);
        }

        if (glowPen is not null)
            dc.DrawGeometry(null, glowPen, line);
        dc.DrawGeometry(null, pen, line);

        if (headDot)
        {
            var head = points[^1];
            var headPulse = 4 + (Math.Sin(_phase) + 1) / 2 * 2.5;
            dc.DrawEllipse(GlowBrush, null, head, headPulse + 3, headPulse + 3);
            dc.DrawEllipse(NoraWpfTheme.OrangeBrush, null, head, 3.5, 3.5);
        }
    }

    private void DrawWaitingState(DrawingContext dc, Rect plot)
    {
        var baseline = plot.Top + plot.Height * 0.64;
        dc.DrawLine(IdlePen, new Point(plot.Left, baseline), new Point(plot.Right, baseline));
        var shimmer = NoraWpfTheme.MotionEnabled ? (_phase / (Math.PI * 2)) : 0.44;
        var centerX = plot.Left + shimmer * plot.Width;
        var half = 34d;
        var start = Math.Max(plot.Left, centerX - half);
        var end = Math.Min(plot.Right, centerX + half);
        if (end > start)
        {
            var shimmerBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 156, 38), 0),
                    new GradientStop(Color.FromArgb(150, 255, 156, 38), 0.5),
                    new GradientStop(Color.FromArgb(0, 255, 156, 38), 1)
                }
            };
            dc.DrawLine(new Pen(shimmerBrush, 2.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(start, baseline), new Point(end, baseline));
        }
        var label = MakeText("Waiting for secure traffic", 11, FontWeights.Normal, NoraWpfTheme.MutedBrush);
        dc.DrawText(label, new Point(plot.Left + (plot.Width - label.Width) / 2, plot.Top + 12));
    }

    internal static int RunMotionSelfTest(System.IO.TextWriter output)
    {
        var firstX = MapX(10, 10.1, 0, 400);
        var nextX = MapX(10, 10.2, 0, 400);
        var attack = SmoothRate(0, 1000, ExpectedSampleSeconds);
        var release = SmoothRate(1000, 0, ExpectedSampleSeconds);
        var continuousScroll = nextX < firstX && Math.Abs(nextX - firstX) > 0.01;
        var boundedSmoothing = attack is > 0 and < 1000 && release is > 0 and < 1000;
        var asymmetricEnvelope = attack > 1000 - release;
        var passed = continuousScroll && boundedSmoothing && asymmetricEnvelope;
        output.WriteLine($"TRAFFIC MOTION SELF-TEST: continuous_scroll={continuousScroll}; attack={attack:0.0}; release={release:0.0}; asymmetric={asymmetricEnvelope}");
        return passed ? 0 : 1;
    }

    private void DrawMetric(DrawingContext dc, string label, string value, Point point)
    {
        DrawText(dc, label, 10, FontWeights.SemiBold, NoraWpfTheme.MutedBrush, point);
        DrawText(dc, value, 15, FontWeights.SemiBold, NoraWpfTheme.TextBrush, new Point(point.X, point.Y + 20));
    }

    private static string FormatRate(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s";
        if (bytes >= 1024)
            return (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB/s";
        return bytes + " B/s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
            return (bytes / 1024d / 1024d / 1024d).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        if (bytes >= 1024L * 1024L)
            return (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        if (bytes >= 1024L)
            return (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        return bytes + " B";
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 100 ? duration.ToString(@"d\.hh\:mm", CultureInfo.InvariantCulture) : duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    private static void DrawGraphIcon(DrawingContext dc, Point p)
    {
        var pen = IconPen;
        dc.DrawLine(pen, new Point(p.X, p.Y + 16), new Point(p.X, p.Y - 2));
        dc.DrawLine(pen, new Point(p.X, p.Y + 16), new Point(p.X + 22, p.Y + 16));
        dc.DrawLine(pen, new Point(p.X + 4, p.Y + 10), new Point(p.X + 11, p.Y + 4));
        dc.DrawLine(pen, new Point(p.X + 11, p.Y + 4), new Point(p.X + 18, p.Y + 8));
        dc.DrawLine(pen, new Point(p.X + 18, p.Y + 8), new Point(p.X + 28, p.Y - 4));
    }

    private void DrawText(DrawingContext dc, string text, double size, FontWeight weight, Brush brush, Point point)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("PT Root UI VF, Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, point);
    }

    private FormattedText MakeText(string text, double size, FontWeight weight, Brush brush)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("PT Root UI VF, Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawRightText(DrawingContext dc, string text, double size, FontWeight weight, Brush brush, Point rightTop)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("PT Root UI VF, Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(Math.Max(62, rightTop.X - ft.Width), rightTop.Y));
    }
}

internal sealed class NoraWelcomeOrbitOverlay : FrameworkElement
{
    private TimeSpan _last = TimeSpan.MinValue;
    private double _phase;

    public NoraWelcomeOrbitOverlay()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            _last = TimeSpan.MinValue;
            return;
        }
        if (!NoraWpfTheme.MotionEnabled)
            return;
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var dt = _last == TimeSpan.MinValue ? 0.016 : Math.Clamp((now - _last).TotalSeconds, 0, 0.1);
        _last = now;
        _phase = (_phase + dt) % 1000;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        var center = new Point(w * 0.58, h * 0.31);
        var glow = new RadialGradientBrush
        {
            Center = new Point(0.58, 0.31),
            GradientOrigin = new Point(0.58, 0.31),
            RadiusX = 0.58,
            RadiusY = 0.34,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(76, 255, 156, 38), 0),
                new GradientStop(Color.FromArgb(20, 255, 156, 38), 0.46),
                new GradientStop(Colors.Transparent, 1)
            }
        };
        dc.DrawRectangle(glow, null, new Rect(0, 0, w, h));

        DrawOrbit(dc, center, w * 0.33, h * 0.085, _phase * 10, 0.65, 96);
        DrawOrbit(dc, center, w * 0.42, h * 0.125, -_phase * 6.5 + 60, 0.45, 60);
        DrawOrbit(dc, center, w * 0.50, h * 0.15, _phase * 2.4 + 140, 0.28, 38);
    }

    private static void DrawOrbit(DrawingContext dc, Point center, double rx, double ry, double phaseDeg, double tilt, byte alpha)
    {
        var pen = new Pen(NoraWpfTheme.Brush(Color.FromArgb((byte)(alpha * 0.55), 255, 156, 38)), 1.05)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var start = PointOnOrbit(center, rx, ry, phaseDeg, tilt);
            ctx.BeginFigure(start, false, false);
            for (var i = 1; i <= 72; i++)
                ctx.LineTo(PointOnOrbit(center, rx, ry, phaseDeg + i * 2.45, tilt), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        var dot = PointOnOrbit(center, rx, ry, phaseDeg + 145, tilt);
        dc.DrawEllipse(NoraWpfTheme.Brush(Color.FromArgb(alpha, 255, 177, 73)), null, dot, 2.5, 2.5);
        dc.DrawEllipse(NoraWpfTheme.Brush(Color.FromArgb((byte)(alpha / 3), 255, 156, 38)), null, dot, 7, 7);
    }

    private static Point PointOnOrbit(Point center, double rx, double ry, double deg, double tilt)
    {
        var a = deg * Math.PI / 180.0;
        var x = Math.Cos(a) * rx;
        var y = Math.Sin(a) * ry;
        return new Point(center.X + x, center.Y + y + x * tilt * 0.12);
    }
}

internal sealed class NoraWelcomeLine : FrameworkElement
{
    private TimeSpan _last = TimeSpan.MinValue;
    private double _phase;

    public NoraWelcomeLine()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!NoraWpfTheme.MotionEnabled)
            return;
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var dt = _last == TimeSpan.MinValue ? 0.016 : Math.Clamp((now - _last).TotalSeconds, 0, 0.1);
        _last = now;
        _phase = (_phase + dt) % 8.0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        var y = h / 2;
        var basePen = new Pen(NoraWpfTheme.Brush(Color.FromArgb(54, 255, 156, 38)), 1);
        basePen.Freeze();
        dc.DrawLine(basePen, new Point(0, y), new Point(w, y));

        var centerPen = new Pen(NoraWpfTheme.OrangeBrush, 3)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        centerPen.Freeze();
        dc.DrawLine(centerPen, new Point(w / 2 - 18, y), new Point(w / 2 + 18, y));

        if (!NoraWpfTheme.MotionEnabled || _phase > 1.15)
            return;
        var t = _phase / 1.15;
        var x = w * t;
        var sparkBrush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(190, 255, 192, 91), 0),
                new GradientStop(Color.FromArgb(70, 255, 156, 38), 0.45),
                new GradientStop(Colors.Transparent, 1)
            }
        };
        dc.DrawEllipse(sparkBrush, null, new Point(x, y), 18, 5.5);
    }
}

internal enum NoraIconKind
{
    Home, Globe, ServerRack, Plus, Users, Terminal, Radar, Copy, Trash, ChevronRight, Shield,
    Bolt, Key, Import, Sliders, Clock, Deploy, Pin, Refresh, Power, Broom, Subscription,
    Pencil, Check, ChevronUp, ChevronDown, Eye
}

// Unified vector icon set. All icons are drawn in a 24x24 design grid and scale
// with the element size; stroke width stays visually constant relative to size.
internal sealed class NoraIcon : FrameworkElement
{
    private NoraIconKind _kind;
    private Brush _stroke = NoraWpfTheme.TextBrush;
    private double _weight = 1.8;

    public NoraIconKind Kind
    {
        get => _kind;
        set { _kind = value; InvalidateVisual(); }
    }

    public Brush Stroke
    {
        get => _stroke;
        set { _stroke = value; InvalidateVisual(); }
    }

    public double Weight
    {
        get => _weight;
        set { _weight = value; InvalidateVisual(); }
    }

    public NoraIcon()
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0)
            return;
        var u = s / 24.0;
        var ox = (ActualWidth - s) / 2;
        var oy = (ActualHeight - s) / 2;
        Point P(double x, double y) => new(ox + x * u, oy + y * u);
        var pen = new Pen(_stroke, _weight * u) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        var thin = new Pen(_stroke, _weight * 0.72 * u) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        thin.Freeze();

        void Line(double x1, double y1, double x2, double y2, Pen? p = null) => dc.DrawLine(p ?? pen, P(x1, y1), P(x2, y2));
        void Poly(Pen p, bool close, params double[] xy)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(P(xy[0], xy[1]), false, close);
                for (var i = 2; i < xy.Length; i += 2)
                    ctx.LineTo(P(xy[i], xy[i + 1]), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, p, geo);
        }
        void Circle(double cx, double cy, double r, Pen? p = null, Brush? fill = null)
            => dc.DrawEllipse(fill, p, P(cx, cy), r * u, r * u);
        void ArcSeg(double cx, double cy, double r, double a0, double sweep, Pen? p = null)
        {
            Point On(double deg)
            {
                var rad = deg * Math.PI / 180;
                return new Point(P(cx, cy).X + Math.Cos(rad) * r * u, P(cx, cy).Y + Math.Sin(rad) * r * u);
            }
            var fig = new PathFigure { StartPoint = On(a0), IsClosed = false };
            fig.Segments.Add(new ArcSegment(On(a0 + sweep), new Size(r * u, r * u), 0, Math.Abs(sweep) > 180,
                sweep >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
            var geo = new PathGeometry([fig]);
            geo.Freeze();
            dc.DrawGeometry(null, p ?? pen, geo);
        }

        switch (_kind)
        {
            case NoraIconKind.Home:
                Poly(pen, false, 4, 11.4, 12, 4.6, 20, 11.4);
                Poly(pen, false, 6.2, 10.4, 6.2, 19.4, 17.8, 19.4, 17.8, 10.4);
                Poly(pen, false, 10.2, 19.4, 10.2, 14.6, 13.8, 14.6, 13.8, 19.4);
                break;
            case NoraIconKind.Globe:
                Circle(12, 12, 8);
                dc.DrawEllipse(null, thin, P(12, 12), 3.6 * u, 8 * u);
                Line(4.6, 9.2, 19.4, 9.2, thin);
                Line(4.6, 14.8, 19.4, 14.8, thin);
                break;
            case NoraIconKind.ServerRack:
                dc.DrawRoundedRectangle(null, pen, new Rect(P(4, 4.5), new Size(16 * u, 6.2 * u)), 1.8 * u, 1.8 * u);
                dc.DrawRoundedRectangle(null, pen, new Rect(P(4, 13.3), new Size(16 * u, 6.2 * u)), 1.8 * u, 1.8 * u);
                Circle(7.1, 7.6, 0.9, null, _stroke);
                Circle(7.1, 16.4, 0.9, null, _stroke);
                Line(10, 7.6, 16.9, 7.6, thin);
                Line(10, 16.4, 16.9, 16.4, thin);
                break;
            case NoraIconKind.Subscription:
                dc.DrawRoundedRectangle(null, pen, new Rect(P(4.4, 5.0), new Size(15.2 * u, 5.4 * u)), 1.7 * u, 1.7 * u);
                dc.DrawRoundedRectangle(null, pen, new Rect(P(4.4, 13.6), new Size(15.2 * u, 5.4 * u)), 1.7 * u, 1.7 * u);
                Circle(7.5, 7.7, 0.9, null, _stroke);
                Circle(7.5, 16.3, 0.9, null, _stroke);
                Line(10.3, 7.7, 16.4, 7.7, thin);
                Line(10.3, 16.3, 16.4, 16.3, thin);
                break;
            case NoraIconKind.Plus:
                Line(12, 5.2, 12, 18.8);
                Line(5.2, 12, 18.8, 12);
                break;
            case NoraIconKind.Users:
                Circle(8, 7.7, 2.55);
                Circle(16, 7.7, 2.55);
                var leftUser = new StreamGeometry();
                using (var ctx = leftUser.Open())
                {
                    ctx.BeginFigure(P(3.2, 19.1), false, false);
                    ctx.BezierTo(P(3.7, 15.4), P(5.3, 13.4), P(8, 13.4), true, true);
                    ctx.BezierTo(P(10.4, 13.4), P(11.7, 15.0), P(12.1, 17.2), true, true);
                }
                leftUser.Freeze();
                dc.DrawGeometry(null, pen, leftUser);
                var rightUser = new StreamGeometry();
                using (var ctx = rightUser.Open())
                {
                    ctx.BeginFigure(P(11.9, 17.2), false, false);
                    ctx.BezierTo(P(12.3, 15.0), P(13.6, 13.4), P(16, 13.4), true, true);
                    ctx.BezierTo(P(18.7, 13.4), P(20.3, 15.4), P(20.8, 19.1), true, true);
                }
                rightUser.Freeze();
                dc.DrawGeometry(null, pen, rightUser);
                break;
            case NoraIconKind.Terminal:
                dc.DrawRoundedRectangle(null, pen, new Rect(P(3.6, 5), new Size(16.8 * u, 14 * u)), 2.4 * u, 2.4 * u);
                Poly(pen, false, 7.2, 10, 9.8, 12.2, 7.2, 14.4);
                Line(12.2, 15.4, 16.4, 15.4);
                break;
            case NoraIconKind.Radar:
                ArcSeg(12, 12.6, 7.6, -196, 210, thin);
                ArcSeg(12, 12.6, 4.4, -160, 130, thin);
                Line(12, 12.6, 17.4, 6.6);
                Circle(12, 12.6, 1.35, null, _stroke);
                Circle(16.2, 9.4, 1.05, null, _stroke);
                break;
            case NoraIconKind.Copy:
                dc.DrawRoundedRectangle(null, pen, new Rect(P(8.6, 8.6), new Size(10.6 * u, 10.6 * u)), 2 * u, 2 * u);
                Poly(pen, false, 15.2, 5.2, 6.8, 5.2, 4.8, 7.2, 4.8, 15.6);
                break;
            case NoraIconKind.Trash:
                Line(4.8, 7.6, 19.2, 7.6);
                Poly(pen, false, 9.6, 7.4, 9.6, 5.2, 14.4, 5.2, 14.4, 7.4);
                Poly(pen, false, 6.6, 7.8, 7.6, 19.6, 16.4, 19.6, 17.4, 7.8);
                Line(10.4, 10.8, 10.7, 16.6, thin);
                Line(13.6, 10.8, 13.3, 16.6, thin);
                break;
            case NoraIconKind.ChevronRight:
                Poly(pen, false, 9.4, 6.4, 15, 12, 9.4, 17.6);
                break;
            case NoraIconKind.ChevronUp:
                Poly(pen, false, 6.4, 14.6, 12, 9, 17.6, 14.6);
                break;
            case NoraIconKind.ChevronDown:
                Poly(pen, false, 6.4, 9.4, 12, 15, 17.6, 9.4);
                break;
            case NoraIconKind.Shield:
                var shield = new StreamGeometry();
                using (var ctx = shield.Open())
                {
                    ctx.BeginFigure(P(12, 4.2), false, true);
                    ctx.LineTo(P(18.8, 6.6), true, true);
                    ctx.LineTo(P(18.8, 11.8), true, true);
                    ctx.BezierTo(P(18.8, 16.6), P(15.8, 18.8), P(12, 20.2), true, true);
                    ctx.BezierTo(P(8.2, 18.8), P(5.2, 16.6), P(5.2, 11.8), true, true);
                    ctx.LineTo(P(5.2, 6.6), true, true);
                }
                shield.Freeze();
                dc.DrawGeometry(null, pen, shield);
                Poly(pen, false, 9, 11.9, 11.2, 14.1, 15.2, 9.9);
                break;
            case NoraIconKind.Bolt:
                var bolt = new StreamGeometry();
                using (var ctx = bolt.Open())
                {
                    ctx.BeginFigure(P(13.2, 3.8), true, true);
                    ctx.LineTo(P(6.6, 13.4), true, true);
                    ctx.LineTo(P(11, 13.4), true, true);
                    ctx.LineTo(P(10.2, 20.2), true, true);
                    ctx.LineTo(P(17.4, 10.4), true, true);
                    ctx.LineTo(P(12.6, 10.4), true, true);
                }
                bolt.Freeze();
                dc.DrawGeometry(_stroke, null, bolt);
                break;
            case NoraIconKind.Key:
                Circle(8.2, 15.6, 3.3);
                Line(10.8, 13.2, 19.2, 4.8);
                Line(15.8, 8.2, 18.2, 10.6, thin);
                Line(13.2, 10.8, 15.2, 12.8, thin);
                break;
            case NoraIconKind.Import:
                Line(12, 4.4, 12, 13.6);
                Poly(pen, false, 8.4, 10.2, 12, 13.8, 15.6, 10.2);
                Poly(pen, false, 4.6, 15.4, 4.6, 18, 6.2, 19.6, 17.8, 19.6, 19.4, 18, 19.4, 15.4);
                break;
            case NoraIconKind.Sliders:
                Line(4.4, 7.2, 19.6, 7.2, thin);
                Line(4.4, 12, 19.6, 12, thin);
                Line(4.4, 16.8, 19.6, 16.8, thin);
                Circle(14.6, 7.2, 2.1, pen, NoraWpfTheme.BgBrush);
                Circle(8.6, 12, 2.1, pen, NoraWpfTheme.BgBrush);
                Circle(16.2, 16.8, 2.1, pen, NoraWpfTheme.BgBrush);
                break;
            case NoraIconKind.Clock:
                Circle(12, 12, 8);
                Poly(pen, false, 12, 7.6, 12, 12.2, 15.4, 13.8);
                break;
            case NoraIconKind.Deploy:
                Line(12, 13.4, 12, 4.6);
                Poly(pen, false, 8.4, 8.2, 12, 4.6, 15.6, 8.2);
                Poly(pen, false, 4.6, 15.4, 4.6, 18, 6.2, 19.6, 17.8, 19.6, 19.4, 18, 19.4, 15.4);
                break;
            case NoraIconKind.Pin:
                var pin = new StreamGeometry();
                using (var ctx = pin.Open())
                {
                    ctx.BeginFigure(P(12, 20.4), false, true);
                    ctx.BezierTo(P(7.2, 15.2), P(5.2, 12.4), P(5.2, 9.8), true, true);
                    ctx.ArcTo(P(18.8, 9.8), new Size(6.8 * u, 6.8 * u), 0, true, SweepDirection.Clockwise, true, true);
                    ctx.BezierTo(P(18.8, 12.4), P(16.8, 15.2), P(12, 20.4), true, true);
                }
                pin.Freeze();
                dc.DrawGeometry(null, pen, pin);
                Circle(12, 9.9, 2.1, thin);
                break;
            case NoraIconKind.Refresh:
                ArcSeg(12, 12, 7.4, -60, 300);
                Poly(pen, false, 14.6, 3.0, 15.8, 5.9, 12.9, 7.2);
                break;
            case NoraIconKind.Power:
                Line(12, 4.6, 12, 11.4);
                ArcSeg(12, 12.8, 7.2, -68, 316);
                break;
            case NoraIconKind.Broom:
                Line(14.2, 4.6, 9.8, 12.4);
                Poly(pen, false, 6.4, 14.4, 9.8, 12.2, 13.2, 14.2, 12.4, 19.4, 5.2, 19.4);
                Line(9.4, 16.2, 8.9, 19.2, thin);
                break;
            case NoraIconKind.Pencil:
                Poly(pen, false, 5.1, 18.9, 6.9, 14.2, 15.8, 5.3, 18.7, 8.2, 9.8, 17.1, 5.1, 18.9);
                Line(14.4, 6.7, 17.3, 9.6, thin);
                break;
            case NoraIconKind.Check:
                Poly(pen, false, 5.2, 12.6, 9.6, 16.8, 18.9, 7.4);
                break;
            case NoraIconKind.Eye:
                var eye = new StreamGeometry();
                using (var ctx = eye.Open())
                {
                    ctx.BeginFigure(P(3.5, 12), false, false);
                    ctx.BezierTo(P(6.1, 7.8), P(9.1, 5.7), P(12, 5.7), true, true);
                    ctx.BezierTo(P(14.9, 5.7), P(17.9, 7.8), P(20.5, 12), true, true);
                    ctx.BezierTo(P(17.9, 16.2), P(14.9, 18.3), P(12, 18.3), true, true);
                    ctx.BezierTo(P(9.1, 18.3), P(6.1, 16.2), P(3.5, 12), true, true);
                }
                eye.Freeze();
                dc.DrawGeometry(null, pen, eye);
                Circle(12, 12, 2.45, pen);
                break;
        }
    }
}

// Indeterminate spinner used in the progress dialog: a rotating arc with a
// breathing outer glow.
internal sealed class NoraSpinner : FrameworkElement
{
    private TimeSpan _last = TimeSpan.MinValue;
    private double _angle;
    private double _pulse;
    private bool _subscribed;

    public NoraSpinner()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (_subscribed) return;
            _last = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnRendering;
            _subscribed = true;
        };
        Unloaded += (_, _) =>
        {
            if (!_subscribed) return;
            CompositionTarget.Rendering -= OnRendering;
            _subscribed = false;
        };
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!NoraWpfTheme.MotionEnabled)
            return;
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var dt = _last == TimeSpan.MinValue ? 0.016 : Math.Clamp((now - _last).TotalSeconds, 0, 0.1);
        _last = now;
        _angle = (_angle + 240 * dt) % 360;
        _pulse += dt;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0)
            return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = s / 2 - 4;
        var breathe = (Math.Sin(_pulse * Math.PI * 2 / 2.4) + 1) * 0.5;
        dc.DrawEllipse(null, new Pen(NoraWpfTheme.Brush(NoraWpfTheme.With(NoraWpfTheme.Orange, (byte)(26 + breathe * 26))), 5), center, radius, radius);
        var pen = new Pen(NoraWpfTheme.OrangeBrush, 3.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        Point On(double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(center.X + Math.Cos(rad) * radius, center.Y + Math.Sin(rad) * radius);
        }
        var fig = new PathFigure { StartPoint = On(_angle), IsClosed = false };
        fig.Segments.Add(new ArcSegment(On(_angle + 100), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        dc.DrawGeometry(null, pen, new PathGeometry([fig]));
        dc.DrawEllipse(NoraWpfTheme.Brush(NoraWpfTheme.Lerp(NoraWpfTheme.Orange, Colors.White, 0.4)), null, On(_angle + 100), 2.6, 2.6);
    }
}

// Shared building blocks for the dialog windows so they match the shell design.
internal static class NoraFormUi
{
    public static Border FieldShell(Control input)
    {
        input.Background = Brushes.Transparent;
        input.Foreground = NoraWpfTheme.TextBrush;
        input.BorderThickness = new Thickness(0);
        input.Padding = new Thickness(12, 9, 12, 9);
        input.FontSize = 13.5;
        input.VerticalContentAlignment = VerticalAlignment.Center;
        if (input is TextBox tb)
        {
            tb.CaretBrush = NoraWpfTheme.OrangeBrush;
            tb.SelectionBrush = NoraWpfTheme.Brush(Color.FromArgb(90, 255, 156, 38));
        }
        if (input is PasswordBox pb)
        {
            pb.CaretBrush = NoraWpfTheme.OrangeBrush;
            pb.SelectionBrush = NoraWpfTheme.Brush(Color.FromArgb(90, 255, 156, 38));
        }
        var borderBrush = new SolidColorBrush(NoraWpfTheme.Stroke);
        var shell = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = NoraWpfTheme.Brush(Color.FromArgb(200, 12, 16, 23)),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Child = input
        };
        input.GotKeyboardFocus += (_, _) => borderBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Color.FromArgb(190, 255, 156, 38), TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        input.LostKeyboardFocus += (_, _) => borderBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(NoraWpfTheme.Stroke, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        return shell;
    }

    public static UIElement SectionHeader(NoraIconKind icon, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 6) };
        row.Children.Add(new NoraIcon
        {
            Kind = icon,
            Width = 15,
            Height = 15,
            Stroke = NoraWpfTheme.OrangeBrush,
            Margin = new Thickness(0, 1, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.Brush(Color.FromRgb(196, 152, 96)),
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    public static void AddField(Panel panel, string label, string helper, Control input)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = NoraWpfTheme.MutedBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 8, 0, 4)
        });
        panel.Children.Add(FieldShell(input));
        if (!string.IsNullOrWhiteSpace(helper))
        {
            panel.Children.Add(new TextBlock
            {
                Text = helper,
                Foreground = NoraWpfTheme.DimBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 4, 0, 0)
            });
        }
    }
}

internal sealed class NoraProgressWindow : Window
{
    private readonly TextBlock _status = new();
    private readonly TextBlock _heading = new();
    private readonly NoraSpinner _spinner = new() { Width = 50, Height = 50, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 20, 0) };
    private readonly StackPanel _panel = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _errorShown;

    public NoraProgressWindow(string title, string status)
    {
        Title = "NORA VPN";
        Width = 520;
        Height = 240;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = NoraWpfTheme.BgBrush;
        Foreground = NoraWpfTheme.TextBrush;
        FontFamily = NoraWpfTheme.UiFont;
        var card = new Border
        {
            Margin = new Thickness(20),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(20),
            Background = NoraWpfTheme.Brush(Color.FromArgb(235, 17, 22, 30)),
            BorderBrush = NoraWpfTheme.StrokeBrush,
            BorderThickness = new Thickness(1)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = grid;

        grid.Children.Add(_spinner);

        Grid.SetColumn(_panel, 1);
        grid.Children.Add(_panel);
        _heading.Text = title;
        _heading.FontSize = 23;
        _heading.FontWeight = FontWeights.Bold;
        _panel.Children.Add(_heading);
        _status.Text = status;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = NoraWpfTheme.MutedBrush;
        _status.FontSize = 13.5;
        _status.Margin = new Thickness(0, 10, 0, 0);
        _panel.Children.Add(_status);
        Content = card;
    }

    public void SetStatus(string status)
    {
        _status.Text = status;
    }

    public void SetError(string title, string message)
    {
        _heading.Text = title;
        _heading.Foreground = NoraWpfTheme.RedBrush;
        _status.Text = message;
        _status.Foreground = NoraWpfTheme.Brush(Color.FromRgb(217, 184, 190));
        _spinner.Visibility = Visibility.Collapsed;
        Height = 290;
        if (_errorShown)
            return;
        _errorShown = true;
        var close = new NoraFxButton(NoraWpfTheme.Card2, Color.FromRgb(31, 38, 49), 14, false, NoraWpfTheme.StrokeBrush)
        {
            Width = 128,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 16, 0, 0),
            Content = new TextBlock
            {
                Text = "Close",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = NoraWpfTheme.TextBrush
            }
        };
        close.Click += (_, _) => Close();
        _panel.Children.Add(close);
    }
}

internal sealed class NoraKeyWindow : Window
{
    public NoraKeyWindow(string title, string key)
    {
        Title = "NORA VPN";
        Width = 620;
        Height = 520;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NoraWpfTheme.BgBrush;
        Foreground = NoraWpfTheme.TextBrush;
        FontFamily = NoraWpfTheme.UiFont;

        var root = new Grid { Margin = new Thickness(24) };
        Content = root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(14),
            Background = NoraWpfTheme.Brush(Color.FromArgb(34, 255, 156, 38)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(90, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new NoraIcon { Kind = NoraIconKind.Key, Width = 21, Height = 21, Stroke = NoraWpfTheme.OrangeBrush }
        });
        titleRow.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(titleRow);

        var helper = new TextBlock
        {
            Text = "Paste this key into NORA VPN on another device. It is already in your clipboard.",
            FontSize = 13,
            Foreground = NoraWpfTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 14)
        };
        Grid.SetRow(helper, 1);
        root.Children.Add(helper);

        var boxHost = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = NoraWpfTheme.Brush(Color.FromArgb(216, 10, 13, 19)),
            BorderBrush = NoraWpfTheme.StrokeBrush,
            BorderThickness = new Thickness(1)
        };
        var box = new TextBox
        {
            Text = key,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            Foreground = NoraWpfTheme.Brush(Color.FromRgb(214, 222, 235)),
            BorderThickness = new Thickness(0),
            SelectionBrush = NoraWpfTheme.Brush(Color.FromArgb(90, 255, 156, 38)),
            Padding = new Thickness(14),
            FontFamily = NoraWpfTheme.MonoFont,
            FontSize = 12
        };
        boxHost.Child = box;
        Grid.SetRow(boxHost, 2);
        root.Children.Add(boxHost);

        var buttons = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var copyText = new TextBlock { Text = "Copy key", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.BgBrush, VerticalAlignment = VerticalAlignment.Center };
        var copyContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        copyContent.Children.Add(new NoraIcon { Kind = NoraIconKind.Copy, Width = 16, Height = 16, Stroke = NoraWpfTheme.BgBrush, Weight = 2.0, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        copyContent.Children.Add(copyText);
        var copy = new NoraFxButton(NoraWpfTheme.Orange, NoraWpfTheme.Orange2, 15, accent: true) { Content = copyContent, Height = 46 };
        var copyResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        copyResetTimer.Tick += (_, _) =>
        {
            copyResetTimer.Stop();
            copyText.Text = "Copy key";
        };
        copy.Click += (_, _) =>
        {
            try { WpfClipboard.SetText(key); } catch { }
            copyText.Text = "Copied";
            copyResetTimer.Stop();
            copyResetTimer.Start();
        };

        var close = new NoraFxButton(NoraWpfTheme.Card2, Color.FromRgb(31, 38, 49), 15, accent: false)
        {
            Content = new TextBlock { Text = "Close", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.TextBrush },
            Height = 46
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        buttons.Children.Add(copy);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
    }
}

internal sealed class FlagIcon : FrameworkElement
{
    public string Country { get; set; } = "Netherlands";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> CountryNameToCode = new(BuildCountryNameMap);
    private static readonly object FlagCacheSync = new();
    private static readonly Dictionary<string, ImageSource> FlagCache = new(StringComparer.OrdinalIgnoreCase);

    internal static int RunSelfTest(System.IO.TextWriter output)
    {
        var expected = new[]
        {
            ("Germany", "DE"),
            ("CH", "CH"),
            ("Brazil", "BR"),
            ("Europe", "EU"),
            ("United Kingdom", "GB"),
            ("Kosovo", "XK")
        };
        var passed = expected.All(item =>
            ResolveCountryCode(item.Item1) == item.Item2 &&
            File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "flags", item.Item2.ToLowerInvariant() + ".png")));
        output.WriteLine(passed
            ? "COUNTRY FLAG SELF-TEST PASS: ISO/EU aliases resolve to bundled PNG assets"
            : "COUNTRY FLAG SELF-TEST FAIL: a country code or asset is missing");
        return passed ? 0 : 1;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        var rect = new Rect(0, 0, w, h);
        var radius = Math.Min(w, h) * 0.24;
        dc.DrawRoundedRectangle(
            NoraWpfTheme.Brush(Color.FromArgb(42, 23, 29, 38)),
            new Pen(NoraWpfTheme.Brush(Color.FromArgb(62, 255, 255, 255)), 1),
            rect,
            radius,
            radius);
        var flag = LoadFlag(ResolveCountryCode(Country));
        if (flag is not null)
        {
            dc.PushClip(new RectangleGeometry(rect, radius, radius));
            dc.DrawImage(flag, rect);
            dc.Pop();
        }
        else
        {
            var globePen = new Pen(NoraWpfTheme.Brush(Color.FromArgb(160, 146, 157, 176)), Math.Max(1, h * 0.055));
            var center = new Point(w / 2, h / 2);
            var globeRadius = h * 0.30;
            dc.DrawEllipse(null, globePen, center, globeRadius, globeRadius);
            dc.DrawEllipse(null, globePen, center, globeRadius * 0.45, globeRadius);
            dc.DrawLine(globePen, new Point(center.X - globeRadius, center.Y), new Point(center.X + globeRadius, center.Y));
        }
        dc.DrawRoundedRectangle(null, new Pen(NoraWpfTheme.Brush(Color.FromArgb(56, 255, 255, 255)), 1), rect, radius, radius);
    }

    private static ImageSource? LoadFlag(string code)
    {
        if (code.Length != 2)
            return null;
        lock (FlagCacheSync)
        {
            if (FlagCache.TryGetValue(code, out var cached))
                return cached;
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "flags", code.ToLowerInvariant() + ".png");
            if (!File.Exists(path))
                return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                FlagCache[code] = image;
                return image;
            }
            catch
            {
                return null;
            }
        }
    }

    private static string ResolveCountryCode(string? country)
    {
        var value = country?.Trim() ?? "";
        if (value.Length == 2 && value.All(char.IsLetter))
        {
            value = value.ToUpperInvariant();
            return value == "UK" ? "GB" : value;
        }
        if (value.Equals("Europe", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("European Union", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("EU", StringComparison.OrdinalIgnoreCase))
            return "EU";
        if (value.Equals("Kosovo", StringComparison.OrdinalIgnoreCase))
            return "XK";
        return CountryNameToCode.Value.TryGetValue(value, out var code) ? code : "";
    }

    private static IReadOnlyDictionary<string, string> BuildCountryNameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Holland"] = "NL",
            ["Britain"] = "GB",
            ["Great Britain"] = "GB",
            ["England"] = "GB",
            ["USA"] = "US",
            ["UAE"] = "AE",
            ["South Korea"] = "KR",
            ["North Korea"] = "KP",
            ["Russia"] = "RU",
            ["Czechia"] = "CZ",
            ["Türkiye"] = "TR"
        };
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                var code = region.TwoLetterISORegionName.ToUpperInvariant();
                if (code.Length != 2 || !code.All(char.IsLetter))
                    continue;
                map.TryAdd(region.EnglishName, code);
                map.TryAdd(region.NativeName, code);
                map.TryAdd(region.DisplayName, code);
            }
            catch { }
        }
        return map;
    }
}

internal sealed class NoraInstallWindow : Window
{
    private readonly TextBox _name = new() { Text = "My VPS" };
    private readonly TextBox _host = new();
    private readonly TextBox _port = new() { Text = "443" };
    private readonly TextBox _sshUser = new() { Text = "root" };
    private readonly PasswordBox _password = new();
    private readonly TextBox _tls = new();
    private readonly TextBox _cover = new();

    public NoraServerSettings Settings { get; private set; } = new();

    public NoraInstallWindow()
    {
        Title = "Install KRot";
        Width = 470;
        Height = 906;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NoraWpfTheme.BgBrush;
        Foreground = NoraWpfTheme.TextBrush;
        FontFamily = NoraWpfTheme.UiFont;

        var panel = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        Content = scroll;

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(14),
            Background = NoraWpfTheme.Brush(Color.FromArgb(34, 255, 156, 38)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(90, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new NoraIcon { Kind = NoraIconKind.Deploy, Width = 22, Height = 22, Stroke = NoraWpfTheme.OrangeBrush }
        });
        var titleText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleText.Children.Add(new TextBlock { Text = "Install KRot", FontSize = 27, FontWeight = FontWeights.Bold });
        titleText.Children.Add(new TextBlock
        {
            Text = "Turn a fresh VPS into your private VPN node.",
            FontSize = 12.5,
            Foreground = NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 3, 0, 0)
        });
        titleRow.Children.Add(titleText);
        panel.Children.Add(titleRow);

        panel.Children.Add(NoraFormUi.SectionHeader(NoraIconKind.Globe, "SERVER"));
        NoraFormUi.AddField(panel, "Name", "Display name inside NORA VPN.", _name);
        NoraFormUi.AddField(panel, "Server IP", "Public VPS IP or DNS name, e.g. vpn.example.net.", _host);
        NoraFormUi.AddField(panel, "Port", "KRot listen port. 443 blends in best with HTTPS.", _port);

        panel.Children.Add(NoraFormUi.SectionHeader(NoraIconKind.Key, "SSH ACCESS"));
        NoraFormUi.AddField(panel, "SSH user", "Account used to install and restart the service. Usually root.", _sshUser);
        NoraFormUi.AddField(panel, "SSH password", "Used for provisioning; stored locally for user management.", _password);

        panel.Children.Add(NoraFormUi.SectionHeader(NoraIconKind.Shield, "ADVANCED (OPTIONAL)"));
        NoraFormUi.AddField(panel, "TLS name", "Domain pointed at this VPS. Empty = <ip>.sslip.io.", _tls);
        NoraFormUi.AddField(panel, "Cover host", "HTTPS cover hostname. Empty = same as TLS name.", _cover);

        var buttons = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cancel = new NoraFxButton(NoraWpfTheme.Card2, Color.FromRgb(31, 38, 49), 15, accent: false)
        {
            Content = new TextBlock { Text = "Cancel", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.TextBrush },
            Height = 46
        };
        cancel.Click += (_, _) => Close();
        var installContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        installContent.Children.Add(new NoraIcon { Kind = NoraIconKind.Deploy, Width = 16, Height = 16, Stroke = NoraWpfTheme.BgBrush, Weight = 2.0, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        installContent.Children.Add(new TextBlock { Text = "Install", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.BgBrush, VerticalAlignment = VerticalAlignment.Center });
        var install = new NoraFxButton(NoraWpfTheme.Orange, NoraWpfTheme.Orange2, 15, accent: true)
        {
            Content = installContent,
            Height = 46
        };
        install.Click += (_, _) =>
        {
            Settings = new NoraServerSettings
            {
                DisplayName = _name.Text.Trim(),
                Host = _host.Text.Trim(),
                Port = int.TryParse(_port.Text, out var p) ? p : 443,
                SshUser = _sshUser.Text.Trim(),
                SshPassword = _password.Password,
                TlsName = _tls.Text.Trim(),
                CoverHost = _cover.Text.Trim(),
                ProvisionRemote = true
            };
            DialogResult = true;
            Close();
        };
        Grid.SetColumn(install, 2);
        buttons.Children.Add(cancel);
        buttons.Children.Add(install);
        panel.Children.Add(buttons);
    }
}

// The technical log is intentionally a single bounded session file. Starting
// the real GUI creates a clean nora.log, so a long-lived portable folder never
// accumulates days of unrelated launches.
internal sealed class NoraRunLog
{
    private const int MaximumBytes = 1_250_000;
    private readonly object _gate = new();
    private readonly string _path;
    private long _length;

    private NoraRunLog(string path)
    {
        _path = path;
    }

    public static NoraRunLog? StartNew()
    {
        try
        {
            var directory = Path.Combine(NoraAppState.DataRoot, "logs");
            return StartNewInDirectory(directory);
        }
        catch
        {
            // Disk logging must never prevent the desktop client from starting.
            return null;
        }
    }

    internal static int RunSelfTest(System.IO.TextWriter output)
    {
        var directory = Path.Combine(Path.GetTempPath(), "nora-run-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = StartNewInDirectory(directory)!;
            first.Append("first launch");
            var next = StartNewInDirectory(directory)!;
            next.Append("second launch");
            var path = Path.Combine(directory, "nora.log");
            var reset = !File.ReadAllText(path, Encoding.UTF8).Contains("first launch", StringComparison.Ordinal) &&
                        File.ReadAllText(path, Encoding.UTF8).Contains("second launch", StringComparison.Ordinal);
            for (var index = 0; index < 20_000; index++)
                next.Append("bounded session entry " + index.ToString("D5") + " " + new string('x', 72));

            var size = new System.IO.FileInfo(path).Length;
            var bounded = size <= MaximumBytes + 4_096;
            output.WriteLine($"RUN-LOG SELFTEST: reset={reset}; bounded={bounded}; bytes={size}");
            return reset && bounded ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static NoraRunLog StartNewInDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "nora.log");
        File.WriteAllText(path, "", Encoding.UTF8);
        return new NoraRunLog(path);
    }

    public void Append(string line)
    {
        try
        {
            var payload = line + Environment.NewLine;
            var payloadBytes = Encoding.UTF8.GetByteCount(payload);
            lock (_gate)
            {
                if (_length + payloadBytes > MaximumBytes)
                {
                    const string trimmed = "[NORA] Earlier entries from this launch were trimmed to keep the log bounded.";
                    File.WriteAllText(_path, trimmed + Environment.NewLine, Encoding.UTF8);
                    _length = Encoding.UTF8.GetByteCount(trimmed + Environment.NewLine);
                }
                File.AppendAllText(_path, payload, Encoding.UTF8);
                _length += payloadBytes;
            }
        }
        catch
        {
            // Logging is best effort only.
        }
    }
}
#endif
