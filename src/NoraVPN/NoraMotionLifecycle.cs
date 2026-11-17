#if WINDOWS
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Nvp;

// A loaded visual can remain loaded when its Window is minimized or hidden in
// the tray. Motion owns resources only while both the visual and its Window are
// presented. VPN state, traffic sampling and other business timers do not use
// this lifecycle.
internal static class NoraMotionLifecycle
{
    private static readonly ConditionalWeakTable<FrameworkElement, List<NoraMotionBinding>> Bindings = new();

    internal static NoraMotionBinding Observe(
        FrameworkElement visual,
        string kind,
        Action resume,
        Action suspend)
    {
        var binding = new NoraMotionBinding(visual, kind, resume, suspend);
        Bindings.GetOrCreateValue(visual).Add(binding);
        binding.Initialize();
        return binding;
    }

    internal static NoraMotionBinding BindRendering(
        FrameworkElement visual,
        EventHandler rendering,
        Action resetFrameTime)
    {
        NoraMotionBinding? binding = null;
        EventHandler counted = (sender, args) =>
        {
            binding!.RecordCallback();
            rendering(sender, args);
        };
        binding = new NoraMotionBinding(visual, "render", () =>
        {
            resetFrameTime();
            CompositionTarget.Rendering += counted;
        }, () =>
        {
            CompositionTarget.Rendering -= counted;
            resetFrameTime();
        });
        Bindings.GetOrCreateValue(visual).Add(binding);
        binding.Initialize();
        return binding;
    }

    internal static NoraMotionBinding BindTimer(FrameworkElement visual, DispatcherTimer timer, Func<bool>? isEligible = null)
    {
        NoraMotionBinding? binding = null;
        timer.Tick += (_, _) => binding!.RecordCallback();
        binding = new NoraMotionBinding(visual, "timer", timer.Start, timer.Stop, isEligible);
        Bindings.GetOrCreateValue(visual).Add(binding);
        binding.Initialize();
        return binding;
    }

    internal static AnimationClock BindAnimation(
        FrameworkElement visual,
        IAnimatable target,
        DependencyProperty property,
        AnimationTimeline animation)
    {
        var clock = (AnimationClock)animation.CreateClock(true);
        // A clock is paused before it reaches the target, including for visuals
        // constructed while their parent Window is hidden.
        clock.Controller!.Pause();
        target.ApplyAnimationClock(property, clock);
        var binding = Observe(visual, "clock", clock.Controller.Resume, clock.Controller.Pause);
        binding.Clock = clock;
        return clock;
    }

    // Diagnostics are internal and queried only by the isolated proof harness.
    // The weak keys do not keep detached controls alive.
    internal static IReadOnlyList<NoraMotionBinding> For(FrameworkElement visual)
        => Bindings.TryGetValue(visual, out var bindings) ? bindings : [];
}

internal sealed class NoraMotionBinding
{
    private readonly FrameworkElement _visual;
    private readonly Action _resume;
    private readonly Action _suspend;
    private readonly Func<bool>? _isEligible;
    private Window? _window;
    private bool _initialized;

    internal NoraMotionBinding(FrameworkElement visual, string kind, Action resume, Action suspend, Func<bool>? isEligible = null)
    {
        _visual = visual;
        Kind = kind;
        _resume = resume;
        _suspend = suspend;
        _isEligible = isEligible;
    }

    internal string Kind { get; }
    internal string VisualName => _visual.GetType().Name;
    internal bool IsActive { get; private set; }
    internal long Activations { get; private set; }
    internal long Suspensions { get; private set; }
    internal long CallbackCount { get; private set; }
    internal AnimationClock? Clock { get; set; }

    internal void RecordCallback() => CallbackCount++;

    internal void Refresh() => Update();

    internal void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        _visual.Loaded += OnLoaded;
        _visual.Unloaded += OnUnloaded;
        _visual.IsVisibleChanged += OnVisibilityChanged;
        if (_visual.IsLoaded)
            OnLoaded(_visual, new RoutedEventArgs());
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        var window = _visual as Window ?? Window.GetWindow(_visual);
        if (!ReferenceEquals(window, _window))
        {
            SetActive(false);
            DetachWindow();
            _window = window;
            if (_window is not null)
            {
                _window.IsVisibleChanged += OnVisibilityChanged;
                _window.StateChanged += OnWindowStateChanged;
                _window.Closed += OnWindowClosed;
            }
        }
        Update();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        SetActive(false);
        DetachWindow();
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => Update();

    private void OnWindowStateChanged(object? sender, EventArgs args) => Update();

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        SetActive(false);
        DetachWindow();
    }

    private void Update() => SetActive(
        _visual.IsLoaded && _visual.IsVisible &&
        _window is { IsVisible: true } && _window.WindowState != WindowState.Minimized &&
        (_isEligible?.Invoke() ?? true));

    private void SetActive(bool active)
    {
        if (IsActive == active)
            return;
        IsActive = active;
        if (active)
        {
            Activations++;
            _resume();
        }
        else
        {
            Suspensions++;
            _suspend();
        }
    }

    private void DetachWindow()
    {
        if (_window is null)
            return;
        _window.IsVisibleChanged -= OnVisibilityChanged;
        _window.StateChanged -= OnWindowStateChanged;
        _window.Closed -= OnWindowClosed;
        _window = null;
    }
}
#endif
