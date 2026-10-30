using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LiteMonitor.src.UI.Controls;

// One UI-thread clock for all settings animations. No timer runs at rest.
internal static class InterfaceMotion
{
    private sealed record Track(Control Owner, long Started, int Duration, Action<float> Frame, Action? Completed, MotionSpring? Spring = null)
    {
        internal float EndValue => Spring?.Target ?? 1;
    }
    private static readonly Dictionary<(Control, string), Track> Tracks = new();
    private static readonly System.Windows.Forms.Timer Clock = new() { Interval = 15 };
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Hooked = new();
    private static bool _requested = true;
    private static bool _systemEnabled = true;
    internal static int ActiveCount => Tracks.Count;
    internal static bool ClockRunning => Clock.Enabled;
    internal static bool Enabled => _requested && _systemEnabled && !SystemInformation.HighContrast;
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);

    static InterfaceMotion() { Clock.Tick += (_, _) => Tick(); }

    internal static void Configure(Settings settings)
    {
        _requested = settings.InterfaceAnimationsEnabled;
        _systemEnabled = !SystemParametersInfo(0x1042, 0, out int enabled, 0) || enabled != 0; // SPI_GETCLIENTAREAANIMATION
        if (!Enabled)
        {
            var pending = Tracks.Values.ToArray();
            Tracks.Clear(); Clock.Stop();
            foreach (var track in pending)
                if (!track.Owner.IsDisposed && !track.Owner.Disposing) { track.Frame(track.EndValue); if (!track.Owner.IsDisposed && !track.Owner.Disposing) track.Completed?.Invoke(); }
        }
    }

    internal static void Cancel(Control owner, string? channel = null)
    {
        foreach (var key in Tracks.Keys.Where(k => k.Item1 == owner && (channel == null || k.Item2 == channel)).ToArray()) Tracks.Remove(key);
        if (Tracks.Count == 0) Clock.Stop();
    }

    internal static void Animate(Control owner, string channel, int duration, Action<float> frame, Action? completed = null)
    {
        Start(owner, channel, new Track(owner, Stopwatch.GetTimestamp(), Math.Max(1, duration), frame, completed));
    }

    internal static void AnimateValue(Control owner, string channel, int duration, float from, float target,
        Action<float> frame, Action? completed = null, bool bounce = false)
    {
        float velocity = 0;
        if (Tracks.TryGetValue((owner, channel), out var previous) && previous.Spring is MotionSpring prior)
        {
            // Repeated focus/capture/key-repeat events must not restart the same destination.
            if (prior.Target == target && Enabled && owner.Visible && !owner.IsDisposed && !owner.Disposing) return;
            velocity = prior.Sample(Stopwatch.GetElapsedTime(previous.Started).TotalSeconds).Velocity;
        }
        var spring = new MotionSpring(from, target, velocity, Math.Max(1, duration) / 1000d, bounce);
        Start(owner, channel, new Track(owner, Stopwatch.GetTimestamp(), Math.Max(1, duration), frame, completed, spring));
    }

    private static void Start(Control owner, string channel, Track track)
    {
        Cancel(owner, channel);
        if (owner.IsDisposed || owner.Disposing) return;
        if (!Enabled || !owner.IsHandleCreated || !owner.Visible ||
            (track.Spring is MotionSpring spring && spring.From == spring.Target && Math.Abs(spring.Velocity) < 0.001f))
        {
            track.Frame(track.EndValue);
            if (!owner.IsDisposed && !owner.Disposing) track.Completed?.Invoke();
            return;
        }
        if (!Hooked.TryGetValue(owner, out _))
        {
            Hooked.Add(owner, new object());
            owner.Disposed += (_, _) => Cancel(owner);
            owner.VisibleChanged += (_, _) => { if (!owner.Visible) Finish(owner); };
        }
        Tracks[(owner, channel)] = track;
        Clock.Start();
    }

    private static void Finish(Control owner)
    {
        var pending = Tracks.Values.Where(t => t.Owner == owner).ToArray();
        Cancel(owner);
        foreach (var track in pending)
            if (!owner.IsDisposed && !owner.Disposing) { track.Frame(track.EndValue); if (!track.Owner.IsDisposed && !track.Owner.Disposing) track.Completed?.Invoke(); }
    }

    private static void Tick()
    {
        foreach (var pair in Tracks.ToArray())
        {
            if (!Tracks.TryGetValue(pair.Key, out var current) || !ReferenceEquals(current, pair.Value)) continue;
            var track = pair.Value;
            if (track.Owner.IsDisposed || track.Owner.Disposing) { Tracks.Remove(pair.Key); continue; }
            double elapsed = Stopwatch.GetElapsedTime(track.Started).TotalSeconds;
            float t = Math.Clamp((float)(elapsed * 1000 / track.Duration), 0, 1);
            float eased = t >= 1 ? track.EndValue : track.Spring?.Sample(elapsed).Value ?? (1 - MathF.Pow(1 - t, 3));
            if (t >= 1) Tracks.Remove(pair.Key);
            track.Frame(eased);
            if (t >= 1 && !track.Owner.IsDisposed && !track.Owner.Disposing) track.Completed?.Invoke();
        }
        if (Tracks.Count == 0) Clock.Stop();
    }
}
