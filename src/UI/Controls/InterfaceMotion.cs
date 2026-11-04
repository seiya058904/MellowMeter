using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LiteMonitor.src.UI.Controls;

// A presentation value bound to one control channel (hover, press, selection...).
// Retargeting continues from the current value and velocity; with motion off it jumps.
internal sealed class MotionValue
{
    private readonly Control _owner;
    private readonly string _channel;
    private readonly SpringSpec _spec;
    private readonly Action<float> _frame;
    internal float Value { get; private set; }

    internal MotionValue(Control owner, string channel, SpringSpec spec, float initial = 0)
    {
        _owner = owner; _channel = channel; _spec = spec; Value = initial;
        _frame = v => { Value = v; _owner.Invalidate(); };
    }

    internal void To(float target, SpringSpec? spec = null) =>
        InterfaceMotion.Spring(_owner, _channel, spec ?? _spec, Value, target, _frame);

    // Immediate tactile feedback: jump to a first step and paint it inside the input event, so
    // the press is visible even when the UI thread is about to be busy, then spring on.
    internal void Press(float firstStep = 0.7f)
    {
        if (Value < firstStep)
        {
            InterfaceMotion.Cancel(_owner, _channel);
            Value = firstStep;
            _owner.Invalidate();
            if (_owner.IsHandleCreated) _owner.Update();
        }
        To(1, SpringSpec.PressIn);
    }

    internal void Set(float value)
    {
        InterfaceMotion.Cancel(_owner, _channel);
        Value = value;
        _owner.Invalidate();
    }
}

// One clock for all settings animations. Nothing runs at rest: no timer, no wake-ups.
internal static class InterfaceMotion
{
    private sealed class Track
    {
        internal Control Owner = null!;
        internal string Channel = "";
        internal long Started;
        internal MotionSpring Spring;
        internal Action<float> Frame = null!;
        internal Action? Completed;
    }

    private static readonly Dictionary<(Control, string), Track> Tracks = new();
    private static readonly List<Track> Active = new();
    private static readonly List<Track> FrameTracks = new();
    private static readonly List<Control> Roots = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Hooked = new();
    private static bool _requested = true;
    private static bool _systemEnabled = true;

    internal static int ActiveCount => Tracks.Count;
    internal static bool ClockRunning => FrameClock.Running;
    internal static bool Enabled => _requested && _systemEnabled && !UIColors.HighContrast;
    // True when Windows has client-area animation off, whatever this app's own switch says.
    internal static bool SystemAnimationsOff => !_systemEnabled;

    // Diagnostics only: lets a test or a hands-on comparison exercise motion for this one process
    // without touching the machine-wide preference. Never set by the application itself.
    internal static bool? SystemPreferenceOverride { get; set; } =
        Environment.GetEnvironmentVariable("MELLOWMETER_FORCE_MOTION") == "1" ? true : null;

    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr handle, IntPtr rect, IntPtr region, uint flags);

    internal static void Configure(Settings settings)
    {
        _requested = settings.InterfaceAnimationsEnabled;
        RefreshSystemPreference();
    }

    internal static void RefreshSystemPreference()
    {
        _systemEnabled = SystemPreferenceOverride ?? (!SystemParametersInfo(0x1042, 0, out int enabled, 0) || enabled != 0); // SPI_GETCLIENTAREAANIMATION
        if (!Enabled) FinishAll();
    }

    private static void FinishAll()
    {
        var pending = Active.ToArray();
        Tracks.Clear(); Active.Clear();
        FrameClock.Stop();
        foreach (var track in pending) Complete(track);
    }

    private static void Complete(Track track)
    {
        if (track.Owner.IsDisposed || track.Owner.Disposing) return;
        track.Frame(track.Spring.Target);
        if (!track.Owner.IsDisposed && !track.Owner.Disposing) track.Completed?.Invoke();
    }

    internal static bool IsAnimating(Control owner, string channel) => Tracks.ContainsKey((owner, channel));

    internal static float VelocityOf(Control owner, string channel) =>
        Tracks.TryGetValue((owner, channel), out var track)
            ? track.Spring.Sample(Stopwatch.GetElapsedTime(track.Started).TotalSeconds).Velocity : 0;

    internal static void Cancel(Control owner, string? channel = null)
    {
        if (channel != null)
        {
            if (Tracks.Remove((owner, channel), out var track)) Active.Remove(track);
        }
        else
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Active[i].Owner, owner))
                {
                    Tracks.Remove((owner, Active[i].Channel));
                    Active.RemoveAt(i);
                }
        }
        if (Tracks.Count == 0) FrameClock.Stop();
    }

    internal static void Spring(Control owner, string channel, SpringSpec spec, float from, float target,
        Action<float> frame, Action? completed = null, float? initialVelocity = null)
    {
        float velocity = initialVelocity ?? 0;
        if (Tracks.TryGetValue((owner, channel), out var previous))
        {
            // Repeated focus/capture/key-repeat events must not restart the same destination.
            if (initialVelocity == null && previous.Spring.Target == target && Enabled && owner.Visible && !owner.IsDisposed && !owner.Disposing) return;
            if (initialVelocity == null) velocity = previous.Spring.Sample(Stopwatch.GetElapsedTime(previous.Started).TotalSeconds).Velocity;
        }
        Cancel(owner, channel);
        if (owner.IsDisposed || owner.Disposing) return;
        var spring = new MotionSpring(from, target, velocity, spec);
        if (!Enabled || !owner.IsHandleCreated || !owner.Visible || spring.IsAtRest(velocity))
        {
            frame(target);
            if (!owner.IsDisposed && !owner.Disposing) completed?.Invoke();
            return;
        }
        if (!Hooked.TryGetValue(owner, out _))
        {
            Hooked.Add(owner, new object());
            owner.Disposed += (_, _) => Cancel(owner);
            owner.VisibleChanged += (_, _) => { if (!owner.Visible) Finish(owner); };
        }
        var track = new Track { Owner = owner, Channel = channel, Started = Stopwatch.GetTimestamp(), Spring = spring, Frame = frame, Completed = completed };
        Tracks[(owner, channel)] = track;
        Active.Add(track);
        FrameClock.Start(Tick);
    }

    private static void Finish(Control owner)
    {
        var pending = Active.Where(t => ReferenceEquals(t.Owner, owner)).ToArray();
        Cancel(owner);
        foreach (var track in pending) Complete(track);
    }

    private static void Tick()
    {
        FrameTracks.Clear();
        FrameTracks.AddRange(Active);
        Roots.Clear();
        foreach (var track in FrameTracks)
        {
            if (!Tracks.TryGetValue((track.Owner, track.Channel), out var current) || !ReferenceEquals(current, track)) continue;
            if (track.Owner.IsDisposed || track.Owner.Disposing) { Tracks.Remove((track.Owner, track.Channel)); Active.Remove(track); continue; }
            double elapsed = Stopwatch.GetElapsedTime(track.Started).TotalSeconds;
            var (value, velocity) = track.Spring.Sample(elapsed);
            bool done = track.Spring.Settled(elapsed);
            if (done) { Tracks.Remove((track.Owner, track.Channel)); Active.Remove(track); value = track.Spring.Target; }
            var root = track.Owner.TopLevelControl;
            if (root != null && !Roots.Contains(root)) Roots.Add(root);
            track.Frame(value);
            if (done && !track.Owner.IsDisposed && !track.Owner.Disposing) track.Completed?.Invoke();
        }
        // Present every change of this frame together instead of waiting for WM_PAINT's turn.
        foreach (var root in Roots)
            if (root.IsHandleCreated && !root.IsDisposed) RedrawWindow(root.Handle, IntPtr.Zero, IntPtr.Zero, 0x0100 | 0x0080); // RDW_UPDATENOW | RDW_ALLCHILDREN
        if (Tracks.Count == 0) FrameClock.Stop();
    }
}

// Paces animation frames to display composition: a worker thread blocks in DwmFlush and posts one
// tick at a time to the UI thread, so a slow frame drops ticks instead of queueing them and sampling
// stays on the wall clock. The thread parks when no track exists and exits after a short idle time.
internal static class FrameClock
{
    internal enum Mode { Composition, Timer }
    // Diagnostics only (off by default); chosen by the performance probe.
    internal static Mode Source { get; set; } = Mode.Composition;
    internal static bool TraceEnabled { get; set; }
    internal static readonly List<(long Start, long End)> Trace = new();

    private const int TickMessage = 0x0400 + 0x4D4D; // WM_USER range, private to this message-only window
    private const int IdleExitMilliseconds = 5000;
    private static readonly object Gate = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static TickWindow? _window;
    private static IntPtr _handle;
    private static Thread? _thread;
    private static Action? _tick;
    private static System.Windows.Forms.Timer? _timer;
    private static volatile bool _running;
    private static int _posted;

    internal static bool Running => _running;

    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, IntPtr name, uint flags, uint access);
    [DllImport("kernel32.dll")] private static extern bool SetWaitableTimer(IntPtr timer, ref long due, int period, IntPtr completion, IntPtr arg, bool resume);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    internal static void Start(Action tick)
    {
        _tick = tick;
        if (_running) return;
        _running = true;
        if (Source == Mode.Timer)
        {
            if (_timer == null)
            {
                _timer = new System.Windows.Forms.Timer { Interval = 8 };
                _timer.Tick += (_, _) => RunTick();
            }
            _timer.Start();
            return;
        }
        _window ??= new TickWindow();
        if (_handle == IntPtr.Zero)
        {
            _window.CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
            _handle = _window.Handle;
        }
        Interlocked.Exchange(ref _posted, 0);
        lock (Gate)
        {
            if (_thread == null)
            {
                _thread = new Thread(Worker) { IsBackground = true, Name = "MellowMeter frame clock", Priority = ThreadPriority.AboveNormal };
                _thread.Start();
            }
        }
        Wake.Set();
    }

    internal static void Stop()
    {
        _running = false;
        _timer?.Stop();
    }

    // Called when the last settings window closes; the next animation recreates these on demand.
    internal static void ReleaseIdle()
    {
        if (_running) return;
        _window?.DestroyHandle();
        _handle = IntPtr.Zero;
        _timer?.Dispose();
        _timer = null;
    }

    private static void RunTick()
    {
        long start = Stopwatch.GetTimestamp();
        try { _tick?.Invoke(); }
        finally
        {
            if (TraceEnabled) Trace.Add((start, Stopwatch.GetTimestamp()));
            Volatile.Write(ref _posted, 0);
        }
    }

    private static void Worker()
    {
        IntPtr timer = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, 0x00000002 /* HIGH_RESOLUTION */, 0x1F0003 /* TIMER_ALL_ACCESS */);
        double period = RefreshPeriodSeconds();
        long last = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                if (!_running)
                {
                    if (Wake.WaitOne(IdleExitMilliseconds) || _running) { last = Stopwatch.GetTimestamp(); continue; }
                    lock (Gate) { if (!_running) { _thread = null; return; } }
                    continue;
                }
                int result = DwmFlush();
                // DwmFlush returns at once when nothing is being composed; never let that spin.
                double elapsed = Stopwatch.GetElapsedTime(last).TotalSeconds;
                if (result < 0 || elapsed < period * 0.5) Pause(timer, Math.Max(0.001, period - elapsed));
                last = Stopwatch.GetTimestamp();
                IntPtr handle = _handle;
                if (_running && handle != IntPtr.Zero && Interlocked.CompareExchange(ref _posted, 1, 0) == 0)
                    if (!PostMessage(handle, TickMessage, IntPtr.Zero, IntPtr.Zero)) Volatile.Write(ref _posted, 0);
            }
        }
        finally { if (timer != IntPtr.Zero) CloseHandle(timer); }
    }

    private static void Pause(IntPtr timer, double seconds)
    {
        if (timer == IntPtr.Zero) { Thread.Sleep(1); return; }
        long due = -(long)(seconds * 10_000_000); // relative, 100 ns units
        if (SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) WaitForSingleObject(timer, 100);
        else Thread.Sleep(1);
    }

    private static double RefreshPeriodSeconds()
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        int hertz = dc == IntPtr.Zero ? 0 : GetDeviceCaps(dc, 116); // VREFRESH
        if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
        return hertz > 1 ? Math.Clamp(1d / hertz, 1 / 360d, 1 / 24d) : 1 / 60d;
    }

    private sealed class TickWindow : NativeWindow
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TickMessage) { if (_running) RunTick(); else Volatile.Write(ref _posted, 0); return; }
            base.WndProc(ref m);
        }
    }
}
