#if WINDOWS
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Path = System.IO.Path;
using TextBox = System.Windows.Controls.TextBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Nvp;

// This entrypoint deliberately never constructs NoraWpfWindow. It does not
// register an instance, read profiles/queues, access the network, capture audio,
// create a tray icon, or start a VPN/core. Only these private proof windows are
// shown offscreen; their controls use the production lifecycle/render paths.
internal static class NoraMotionLifecycleSelfTests
{
    internal static int RunInputFeedbackProof(TextWriter output, string proofDirectory)
    {
        Application? owned = null;
        try
        {
            Directory.CreateDirectory(proofDirectory);
            if (Application.Current is null) owned = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var results = new List<object>();
            CheckInputFeedback(proofDirectory, results);
            File.WriteAllText(Path.Combine(proofDirectory, "input-feedback.json"), JsonSerializer.Serialize(new { processId = Environment.ProcessId, results }, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine("INPUT FEEDBACK PASS: own routed hover/press/focus; pointer capture and keyboard focus unchanged");
            return 0;
        }
        catch (Exception exception) { output.WriteLine("INPUT FEEDBACK FAIL: " + exception.GetType().Name + ": " + exception.Message); return 1; }
        finally { owned?.Shutdown(); }
    }
    internal static int Run(TextWriter output, string proofDirectory, string motionMode = "full")
    {
        if (motionMode is not ("full" or "reduced"))
        {
            output.WriteLine("Motion lifecycle mode must be full or reduced.");
            return 2;
        }
        var previousMotion = Environment.GetEnvironmentVariable("NORA_REDUCED_MOTION");
        var previousInterval = Environment.GetEnvironmentVariable("NORA_LOCATION_SLIDESHOW_TEST_MS");
        var directory = Path.GetFullPath(proofDirectory);
        Application? ownedApp = null;
        Window? window = null;
        Window? heartbeatWindow = null;
        var results = new List<object>();
        var audio = new SyntheticAudio();
        var succeeded = false;
        var pointerHoverPressChecked = false;
        var keyboardFocusFeedbackChecked = false;
        string? failure = null;
        long heartbeatFrames = 0;
        EventHandler heartbeat = (_, _) => heartbeatFrames++;
        using var process = Process.GetCurrentProcess();
        try
        {
            Require(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "STA entrypoint");
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable("NORA_REDUCED_MOTION", motionMode == "reduced" ? "1" : "0");
            Environment.SetEnvironmentVariable("NORA_LOCATION_SLIDESHOW_TEST_MS", "1300");
            if (Application.Current is null)
                ownedApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            var rig = CreateRig(audio);
            window = ProofWindow(rig.Scene, 1040, 1080);
            // Independent composition work prevents a stopped WPF compositor
            // from concealing an accidentally retained production callback.
            var pulse = new Border { Background = Brushes.White, Width = 16, Height = 16 };
            pulse.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 0.8, TimeSpan.FromMilliseconds(400))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
            heartbeatWindow = ProofWindow(pulse, 16, 16);
            CompositionTarget.Rendering += heartbeat;
            heartbeatWindow.Show();
            window.Show();
            Pump(250);
            var bindings = Descendants(rig.Scene).SelectMany(NoraMotionLifecycle.For).Distinct().ToArray();
            Require(bindings.Count(binding => binding.Kind == "render") == (motionMode == "full" ? 8 : 7), "same production render controls and intentional reduced-motion parallax policy");
            Require(bindings.Any(binding => binding.Kind == "timer"), "production slideshow timer also supports discrete reduced-motion transitions");
            Require(motionMode == "full" ? bindings.Count(binding => binding.Kind == "clock") >= 5 : bindings.All(binding => binding.Kind != "clock"), "full-motion controllable clocks and static reduced-motion decoration");
            AssertActive(bindings, audio, motionMode);

            var frames = new Dictionary<string, List<byte[]>>();
            var visibleCallbacks = bindings.Sum(binding => binding.CallbackCount);
            for (var second = 0; second <= 3; second++)
            {
                if (second > 0) Pump(1000);
                foreach (var probe in rig.Probes)
                {
                    if (!frames.TryGetValue(probe.Name, out var samples))
                        frames[probe.Name] = samples = [];
                    samples.Add(Capture(probe.Visual, directory, $"visible-{probe.Name}-t{second}"));
                }
            }
            var differences = frames.ToDictionary(pair => pair.Key,
                pair => pair.Value.Zip(pair.Value.Skip(1)).Select(samples => PixelDifference(samples.First, samples.Second)).ToArray());
            if (motionMode == "full")
            {
                foreach (var name in new[] { "cyber-grid", "connect", "spinner", "orbit", "audio", "waiting-traffic", "slideshow", "pulse" })
                    Require(differences[name].Any(difference => difference > 0.00001), name + " has visible runtime motion at t0..t3");
                var lineChanged = differences["welcome-line"].Any(difference => difference > 0.00001);
                if (!lineChanged)
                {
                    // Preserve the existing <=1.15s spark in each 8s cycle. A
                    // slow PNG encoder can miss that early interval, so sample
                    // a real cycle rather than injecting an artificial phase.
                    var line = rig.Probes.Single(probe => probe.Name == "welcome-line").Visual;
                    var before = Capture(line, directory, "welcome-line-cycle-start");
                    var cycle = Stopwatch.StartNew();
                    while (!lineChanged && cycle.Elapsed.TotalSeconds < 8.5)
                    {
                        Pump(100);
                        var sample = Capture(line, directory, "welcome-line-cycle-sample");
                        lineChanged = PixelDifference(before, sample) > 0.00001;
                    }
                    results.Add(new { state = "welcome-line-real-cycle", secondsObserved = cycle.Elapsed.TotalSeconds, lineChanged, phaseInjected = false });
                }
                Require(lineChanged, "welcome-line has visible real-cycle motion without phase injection");
            }
            // Separate matched CPU samples from PNG encoding, after the initial
            // t0..t3 so the sample cannot hide the welcome line's first spark.
            var visibleCpu = process.TotalProcessorTime;
            Pump(3000);
            var visibleCpuMilliseconds = (process.TotalProcessorTime - visibleCpu).TotalMilliseconds;
            results.Add(new
            {
                state = "visible",
                cpuMilliseconds = visibleCpuMilliseconds,
                cpuSampleSeconds = 3,
                cpuSampleIncludesFrameEncoding = false,
                callbacks = bindings.Sum(binding => binding.CallbackCount) - visibleCallbacks,
                pixelChangedFractions = differences,
                bindings = Snapshot(bindings),
                audio = audio.Snapshot()
            });
            output.WriteLine($"MOTION VISIBLE: mode={motionMode}; frames=t0,t1,t2,t3; callbacks={bindings.Sum(binding => binding.CallbackCount) - visibleCallbacks}");

            // Put the slideshow partway through a real crossfade before hiding.
            if (motionMode == "full")
            {
                var wait = Stopwatch.StartNew();
                while (!rig.Slideshow.Children.OfType<Border>().Any(layer => layer.Opacity is > 0.05 and < 0.95) && wait.ElapsedMilliseconds < 2500)
                    Pump(30);
                Require(rig.Slideshow.Children.OfType<Border>().Any(layer => layer.Opacity is > 0.05 and < 0.95), "live crossfade before hide");
            }

            window.Hide();
            CheckInactive("hidden", rig, bindings, audio, process, results, directory, () => heartbeatFrames);
            window.Show();
            CheckResume("shown-again", rig, bindings, audio, motionMode, results, directory);

            window.WindowState = WindowState.Minimized;
            CheckInactive("minimized", rig, bindings, audio, process, results, directory, () => heartbeatFrames);
            window.WindowState = WindowState.Normal;
            CheckResume("restored", rig, bindings, audio, motionMode, results, directory);

            window.Content = new Border { Background = Brushes.Black };
            CheckInactive("off-page", rig, bindings, audio, process, results, directory, () => heartbeatFrames);
            window.Content = rig.Scene;
            CheckResume("reattached", rig, bindings, audio, motionMode, results, directory);

            for (var cycle = 0; cycle < 3; cycle++)
            {
                window.Hide();
                Pump(60);
                Require(bindings.All(binding => !binding.IsActive), "repeat hide stops every binding");
                Require(audio.Leases == 0, "repeat hide releases audio");
                window.Show();
                Pump(80);
                AssertActive(bindings, audio, motionMode);
            }
            foreach (var state in new[] { "ready", "connecting", "connected", "stopping", "failed" })
            {
                rig.Connect.IsProgress = state is "connecting" or "stopping";
                rig.Connect.IsConnected = state == "connected";
                rig.Connect.IsFailed = state == "failed";
                rig.Connect.MainText = state switch
                {
                    "connecting" => "Connecting",
                    "connected" => "Connected",
                    "stopping" => "Stopping",
                    "failed" => "Failed",
                    _ => "Connect"
                };
                rig.Connect.InvalidateVisual();
                Capture(rig.Connect, directory, $"state-{state}-before");
                Pump(120);
                Capture(rig.Connect, directory, $"state-{state}-during");
                Pump(250);
                Capture(rig.Connect, directory, $"state-{state}-after");
                AssertActive(bindings, audio, motionMode);
            }

            CheckInputFeedback(directory, results);
            pointerHoverPressChecked = true;
            keyboardFocusFeedbackChecked = true;

            window.Close();
            Pump(100);
            Require(bindings.All(binding => !binding.IsActive), "closed releases every motion resource");
            Require(audio.Leases == 0 && audio.Attachments == audio.Detachments, "closed balances synthetic audio leases");
            results.Add(new { state = "closed", bindings = Snapshot(bindings), audio = audio.Snapshot() });
            window = null;
            succeeded = true;
            output.WriteLine("MOTION LIFECYCLE PASS: hidden/minimized/off-page callbacks stop; clocks pause; restore uses one subscription; synthetic audio leases balance.");
            return 0;
        }
        catch (Exception exception)
        {
            failure = exception.GetType().Name + ": " + exception.Message;
            output.WriteLine("MOTION LIFECYCLE FAIL: " + failure);
            return 1;
        }
        finally
        {
            CompositionTarget.Rendering -= heartbeat;
            window?.Close();
            heartbeatWindow?.Close();
            ownedApp?.Shutdown();
            Environment.SetEnvironmentVariable("NORA_REDUCED_MOTION", previousMotion);
            Environment.SetEnvironmentVariable("NORA_LOCATION_SLIDESHOW_TEST_MS", previousInterval);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "motion-lifecycle.json"), JsonSerializer.Serialize(new
            {
                passed = succeeded,
                processId = process.Id,
                mode = motionMode,
                osClientAreaAnimation = SystemParameters.ClientAreaAnimation,
                productionWindowCreated = false,
                realAudioCaptureCreated = false,
                pointerHoverPressChecked,
                keyboardFocusFeedbackChecked,
                inputProofMode = "synthetic routed events on owned offscreen controls",
                osPointerMovementUsed = false,
                osKeyboardFocusRequested = false,
                cpuMeasurementScope = "own proof process including independent composition heartbeat",
                gpuMeasured = false,
                heartbeatFrames,
                failure,
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void CheckInactive(string state, Rig rig, NoraMotionBinding[] bindings, SyntheticAudio audio,
        Process process, List<object> results, string directory, Func<long> heartbeat)
    {
        Pump(180);
        Require(bindings.All(binding => !binding.IsActive), state + " has zero active motion bindings");
        Require(audio.Leases == 0, state + " has no audio lease");
        var callbacks = bindings.Select(binding => binding.CallbackCount).ToArray();
        var phases = rig.Probes.Select(probe => ReadPhase(probe.Visual)).ToArray();
        var clockTimes = bindings.Where(binding => binding.Clock is not null).Select(binding => binding.Clock!.CurrentTime).ToArray();
        var opacities = rig.Slideshow.Children.OfType<Border>().Select(layer => layer.Opacity).ToArray();
        var reads = audio.Reads;
        var pulseBefore = heartbeat();
        for (var second = 0; second <= 3; second++)
        {
            if (second > 0) Pump(1000);
            Capture(rig.Scene, directory, $"{state}-t{second}");
        }
        var cpu = process.TotalProcessorTime;
        Pump(3000);
        var cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        var delta = bindings.Select((binding, index) => binding.CallbackCount - callbacks[index]).ToArray();
        Require(delta.All(count => count == 0), state + " callback and timer deltas are exactly zero");
        Require(audio.Reads == reads, state + " performs no spectrum reads");
        Require(phases.SequenceEqual(rig.Probes.Select(probe => ReadPhase(probe.Visual))), state + " preserves cosmetic phase");
        Require(clockTimes.SequenceEqual(bindings.Where(binding => binding.Clock is not null).Select(binding => binding.Clock!.CurrentTime)), state + " pauses animation clocks");
        Require(opacities.Zip(rig.Slideshow.Children.OfType<Border>().Select(layer => layer.Opacity)).All(pair => Math.Abs(pair.First - pair.Second) < 0.000001), state + " pauses in-flight crossfade");
        Require(heartbeat() > pulseBefore, "independent composition heartbeat remains active during " + state);
        results.Add(new
        {
            state,
            durationSeconds = 6,
            cpuSampleSeconds = 3,
            cpuSampleIncludesFrameEncoding = false,
            cpuMilliseconds,
            callbackDeltas = delta,
            phases,
            clockTimes,
            crossfadeOpacities = opacities,
            compositionFrames = heartbeat() - pulseBefore,
            bindings = Snapshot(bindings),
            audio = audio.Snapshot()
        });
    }

    private static void CheckResume(string state, Rig rig, NoraMotionBinding[] bindings, SyntheticAudio audio,
        string motionMode, List<object> results, string directory)
    {
        var before = ReadPhase(rig.Cyber);
        var callbacks = bindings.Sum(binding => binding.CallbackCount);
        var timerCallbacks = bindings.Where(binding => binding.Kind == "timer").Sum(binding => binding.CallbackCount);
        Pump(100);
        AssertActive(bindings, audio, motionMode);
        var advance = ReadPhase(rig.Cyber) - before;
        Require(advance >= 0 && advance < 0.4, state + " has no elapsed-time catch-up");
        Require(bindings.Sum(binding => binding.CallbackCount) > callbacks, state + " resumes callbacks");
        Require(bindings.Where(binding => binding.Kind == "timer").Sum(binding => binding.CallbackCount) == timerCallbacks,
            state + " restarts a fresh slideshow interval without missed-slide catch-up");
        var frames = new List<byte[]>();
        for (var second = 0; second <= 3; second++)
        {
            if (second > 0) Pump(1000);
            frames.Add(Capture(rig.Cyber, directory, $"{state}-cyber-grid-t{second}"));
            Capture(rig.Scene, directory, $"{state}-t{second}");
        }
        var differences = frames.Zip(frames.Skip(1)).Select(pair => PixelDifference(pair.First, pair.Second)).ToArray();
        if (motionMode == "full")
            Require(differences.Any(difference => difference > 0.00001), state + " resumes visible runtime motion at t0..t3");
        results.Add(new
        {
            state,
            first100msCosmeticAdvance = advance,
            pixelChangedFractions = differences,
            bindings = Snapshot(bindings),
            audio = audio.Snapshot()
        });
    }

    private static void CheckInputFeedback(string directory, List<object> results)
    {
        var clicks = 0;
        var button = new NoraFxButton(NoraWpfTheme.Orange, NoraWpfTheme.Orange2, 16, accent: true)
        {
            Content = "Synthetic action", Width = 320, Height = 54,
            Foreground = NoraWpfTheme.BgBrush, FontFamily = NoraWpfTheme.UiFont,
            FontSize = 14, FontWeight = FontWeights.Bold
        };
        button.Click += (_, _) => clicks++;
        // A stationary background preserves the actual inward press geometry
        // when the normalized VisualBrush captures this owned control.
        var buttonHost = new Border
        {
            Width = 360, Height = 86, Padding = new Thickness(20, 16, 20, 16),
            Background = NoraWpfTheme.Brush(NoraWpfTheme.Bg2),
            Child = button, Margin = new Thickness(20, 10, 20, 10)
        };
        var input = new TextBox
        {
            Text = "Synthetic local input 🌐", Background = Brushes.Transparent,
            Foreground = NoraWpfTheme.TextBrush, BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 12, 14, 12), FontFamily = NoraWpfTheme.MonoFont,
            FontSize = 12, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true
        };
        var inputStroke = new SolidColorBrush(NoraWpfTheme.Stroke);
        var inputHost = new Border
        {
            Width = 360, Height = 104, BorderBrush = inputStroke, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Background = NoraWpfTheme.CardBrush,
            Child = input, Margin = new Thickness(20, 6, 20, 10)
        };
        // The same focus-style handler is attached to a non-input proxy here:
        // TextBox's framework class handler can change logical focus even for a
        // synthetic notification. The proxy exercises styling without that path.
        NoraInputFocus.Attach(inputHost, inputStroke);
        var scene = new StackPanel { Width = 400, Height = 240, Background = NoraWpfTheme.BgBrush };
        scene.Children.Add(buttonHost);
        scene.Children.Add(inputHost);
        var window = ProofWindow(scene, 400, 240);
        try
        {
            window.Show();
            window.UpdateLayout();
            Pump(100);
            var originalFocus = Keyboard.FocusedElement;
            var originalCapture = Mouse.Captured;
            var normal = Capture(buttonHost, directory, "input-button-normal");
            button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
            Pump(180);
            var hovered = Capture(buttonHost, directory, "input-button-hover");
            var hoverDifference = PixelDifference(normal, hovered);
            Require(hoverDifference > 0.00001, "actual NoraFxButton OnMouseEnter changes visible fill");

            // Preview-left events invoke the actual press/release overrides.
            // No bubbling down/up event is sent to ButtonBase's capture path.
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            Pump(120);
            var pressed = Capture(buttonHost, directory, "input-button-pressed");
            var fill = button.Template.FindName("PART_Fill", button) as Border;
            Require(fill?.RenderTransform is ScaleTransform { ScaleX: < 0.98, ScaleY: < 0.98 }, "actual preview press scales inward");
            var pressDifference = PixelDifference(hovered, pressed);
            Require(pressDifference > 0.00001, "actual NoraFxButton press changes visible geometry");
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            Pump(260);
            var released = Capture(buttonHost, directory, "input-button-released");
            Require(fill?.RenderTransform is ScaleTransform { ScaleX: > 0.999, ScaleY: > 0.999 }, "actual preview release restores scale");
            var releaseDifference = PixelDifference(pressed, released);
            Require(releaseDifference > 0.00001, "actual NoraFxButton release visibly settles");
            Require(clicks == 0, "synthetic preview feedback invokes no action");
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            Require(clicks == 1, "one explicit Click event invokes exactly one inert fake handler");
            button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
            Pump(270);
            Capture(buttonHost, directory, "input-button-mouse-left");

            var rest = Capture(inputHost, directory, "input-field-rest");
            inputHost.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, null, inputHost)
                { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
            Pump(90);
            Capture(inputHost, directory, "input-field-focus-during");
            Pump(140);
            var focused = Capture(inputHost, directory, "input-field-focused");
            var focusDifference = PixelDifference(rest, focused);
            Require(ColorMatches(inputStroke.Color, Color.FromArgb(190, 255, 156, 38)) && focusDifference > 0.00001,
                 $"shared Add-page focus handler visibly reaches the 180ms focused style (ARGB={inputStroke.Color.A},{inputStroke.Color.R},{inputStroke.Color.G},{inputStroke.Color.B}; changed={focusDifference:F6})");
            inputHost.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, inputHost, null)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Pump(300);
            var blurred = Capture(inputHost, directory, "input-field-blurred");
            var blurDifference = PixelDifference(focused, blurred);
            Require(ColorMatches(inputStroke.Color, NoraWpfTheme.Stroke) && blurDifference > 0.00001,
                "shared Add-page blur handler visibly reaches the 260ms resting style");
            Require(ReferenceEquals(Keyboard.FocusedElement, originalFocus), "synthetic focus notifications do not move keyboard focus");
            Require(ReferenceEquals(Mouse.Captured, originalCapture), "synthetic preview events do not capture the pointer");
            results.Add(new
            {
                state = "input-feedback", mode = "owned offscreen synthetic routed events",
                hoverDifference, pressDifference, releaseDifference, focusDifference, blurDifference,
                fakeClickCount = clicks, pointerCaptureChanged = false, keyboardFocusMoved = false,
                bubblingMouseDownUpSent = false, focusHelper = nameof(NoraInputFocus)
            });
        }
        finally { window.Close(); }
    }

    private static void AssertActive(NoraMotionBinding[] bindings, SyntheticAudio audio, string mode)
    {
        Require(bindings.All(binding => binding.IsActive && binding.Activations - binding.Suspensions == 1), "exactly one active resource per binding");
        Require(audio.Leases == (mode == "full" ? 1 : 0), "single synthetic audio lease in full motion only");
        Require(audio.MaximumLeases <= 1, "audio lease is never duplicated");
    }

    private static object[] Snapshot(IEnumerable<NoraMotionBinding> bindings) => bindings.Select(binding => (object)new
    {
        visual = binding.VisualName,
        binding.Kind,
        binding.IsActive,
        binding.Activations,
        binding.Suspensions,
        binding.CallbackCount,
        clockPaused = binding.Clock?.IsPaused,
        clockTime = binding.Clock?.CurrentTime
    }).ToArray();

    private static Rig CreateRig(SyntheticAudio audio)
    {
        var scene = new Grid { Width = 1020, Height = 1060, Background = NoraWpfTheme.BgBrush };
        scene.ColumnDefinitions.Add(new ColumnDefinition());
        scene.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var height in new[] { 250d, 250, 180, 180, 100 })
            scene.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        var probes = new List<Probe>();
        void Add(string name, FrameworkElement visual, int column, int row)
        {
            visual.Margin = new Thickness(8);
            Grid.SetColumn(visual, column);
            Grid.SetRow(visual, row);
            scene.Children.Add(visual);
            probes.Add(new Probe(name, visual));
        }
        var cyber = new NoraCyberGrid { Width = 490, Height = 230, Energy = 0.7 };
        Add("cyber-grid", cyber, 0, 0);
        Add("audio", new NoraAudioBarBackdrop(audio.Read, audio.Attach, audio.Detach, () => "synthetic local spectrum") { Width = 490, Height = 230 }, 1, 0);
        var connect = new NoraConnectButton { Width = 235, Height = 235 };
        Add("connect", connect, 0, 1);
        Add("waiting-traffic", new NoraTrafficGraph { Width = 490, Height = 208, Active = false }, 1, 1);
        Add("orbit", new NoraWelcomeOrbitOverlay { Width = 490, Height = 160 }, 0, 2);
        Add("spinner", new NoraSpinner { Width = 90, Height = 90 }, 1, 2);
        Add("welcome-line", new NoraWelcomeLine { Width = 490, Height = 30 }, 0, 3);
        var slideshow = new Grid { Width = 490, Height = 160, ClipToBounds = true };
        NoraWpfWindow.AddPhotoSlideshow(slideshow, [Photo(Color.FromRgb(46, 79, 120)), Photo(Color.FromRgb(170, 94, 36)), Photo(Color.FromRgb(40, 130, 98))], new ScaleTransform(1, 1), new CornerRadius(21));
        Add("slideshow", slideshow, 1, 3);
        Add("pulse", new PulsingDot { Width = 40, Height = 40 }, 0, 4);
        var wiggle = new Border { Width = 230, Height = 52, Background = NoraWpfTheme.OrangeBrush, CornerRadius = new CornerRadius(12) };
        NoraWpfWindow.ApplyArrangementWiggle(wiggle, 0);
        Add("arrangement", wiggle, 1, 4);
        // Actual parallax controller uses separate far and near transforms. No
        // synthetic pointer input is sent to any other application.
        NoraWpfWindow.AttachWelcomeParallaxController(scene, new TranslateTransform(), new TranslateTransform());
        return new Rig(scene, probes, cyber, connect, slideshow);
    }

    private static ImageSource Photo(Color color)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(0, 0, 490, 160))));
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new EllipseGeometry(new Point(360, 65), 45, 45)));
        drawing.Freeze();
        return new DrawingImage(drawing);
    }

    private static Window ProofWindow(UIElement content, double width, double height) => new()
    {
        Width = width,
        Height = height,
        Left = -20000,
        Top = -20000,
        WindowStartupLocation = WindowStartupLocation.Manual,
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        ShowActivated = false,
        Background = NoraWpfTheme.BgBrush,
        Content = content
    };

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject visual)
    {
        if (visual is FrameworkElement element) yield return element;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(visual, index)))
                yield return child;
    }

    private static double ReadPhase(FrameworkElement visual)
    {
        var name = visual switch
        {
            NoraCyberGrid => "_t",
            NoraConnectButton => "_pulseT",
            NoraTrafficGraph => "_phase",
            NoraSpinner => "_pulse",
            NoraWelcomeOrbitOverlay or NoraWelcomeLine => "_phase",
            NoraAudioBarBackdrop => "_visibility",
            _ => null
        };
        return name is null ? 0 : (double)visual.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visual)!;
    }

    private static byte[] Capture(FrameworkElement visual, string directory, string label)
    {
        var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        // Rendering a child directly carries its parent-grid layout offset into
        // the bitmap and can clip an entire lower row. A brush snapshots the
        // same live visual in its own local coordinate space.
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(visual) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
        bitmap.Render(drawing);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, label + ".png"));
        encoder.Save(stream);
        return pixels;
    }

    private static double PixelDifference(byte[] first, byte[] second)
    {
        Require(first.Length == second.Length, "frame dimensions stay stable");
        var changed = 0;
        for (var index = 0; index < first.Length; index += 4)
            if (Math.Abs(first[index] - second[index]) + Math.Abs(first[index + 1] - second[index + 1]) +
                Math.Abs(first[index + 2] - second[index + 2]) + Math.Abs(first[index + 3] - second[index + 3]) > 4)
                changed++;
        return changed / Math.Max(1d, first.Length / 4d);
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private static bool ColorMatches(Color actual, Color expected)
        => Math.Abs(actual.A - expected.A) <= 1 && Math.Abs(actual.R - expected.R) <= 1 &&
           Math.Abs(actual.G - expected.G) <= 1 && Math.Abs(actual.B - expected.B) <= 1;

    private sealed record Probe(string Name, FrameworkElement Visual);
    private sealed record Rig(Grid Scene, IReadOnlyList<Probe> Probes, NoraCyberGrid Cyber, NoraConnectButton Connect, Grid Slideshow);

    private sealed class SyntheticAudio
    {
        internal int Leases { get; private set; }
        internal int MaximumLeases { get; private set; }
        internal int Attachments { get; private set; }
        internal int Detachments { get; private set; }
        internal long Reads { get; private set; }
        internal void Attach() { Attachments++; Leases++; MaximumLeases = Math.Max(MaximumLeases, Leases); }
        internal void Detach() { Detachments++; Leases--; Require(Leases >= 0, "synthetic lease underflow"); }
        internal float[] Read()
        {
            Require(Leases == 1, "spectrum reads require one active UI lease");
            Reads++;
            return Enumerable.Range(0, NoraSpectrumAnalyzer.BandCount)
                .Select(band => (float)(0.28 + 0.22 * Math.Sin(Reads * 0.13 + band * 0.45)))
                .ToArray();
        }
        internal object Snapshot() => new { Leases, MaximumLeases, Attachments, Detachments, Reads };
    }
}
#endif
