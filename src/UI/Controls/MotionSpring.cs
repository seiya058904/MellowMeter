namespace LiteMonitor.src.UI.Controls;

// A spring in iOS terms: Response is the period of the undamped oscillation in seconds and
// Damping the damping ratio (1 = critically damped, below 1 overshoots). Resolution is the
// distance and speed-per-frame below which the motion is imperceptible and may stop; it is in
// the units of the animated value (0..1 channels and pixel channels use different presets).
internal readonly record struct SpringSpec(double Response, double Damping, double Resolution = 0.004)
{
    internal static readonly SpringSpec Hover = new(0.16, 1);
    internal static readonly SpringSpec PressIn = new(0.07, 1);
    internal static readonly SpringSpec PressOut = new(0.26, 1);
    internal static readonly SpringSpec Selection = new(0.20, 1);
    internal static readonly SpringSpec Fade = new(0.18, 1);
    internal static readonly SpringSpec Toggle = new(0.30, 0.76);
    internal static readonly SpringSpec Stretch = new(0.16, 1);
    internal static readonly SpringSpec Popup = new(0.24, 0.84);
    internal static readonly SpringSpec Lift = new(0.20, 0.80);
    internal static readonly SpringSpec Disclosure = new(0.32, 1, 0.5);
    internal static readonly SpringSpec Indicator = new(0.30, 0.82, 0.25);
    internal static readonly SpringSpec Scroll = new(0.22, 1, 0.3);
    internal static readonly SpringSpec Edge = new(0.42, 0.72, 0.3);
    internal static readonly SpringSpec Reorder = new(0.25, 0.88, 0.3);
    internal static readonly SpringSpec PageRise = new(0.30, 1, 0.3);
}

// Analytic damped spring, independent of timer cadence. Velocity is in units/second, so an
// interrupted interaction keeps its momentum instead of restarting at rest.
internal readonly struct MotionSpring
{
    private readonly double _displacement, _omega, _decay, _omegaD, _b;
    internal readonly float Target;
    private readonly double _resolution;

    internal MotionSpring(float from, float target, float velocity, SpringSpec spec)
    {
        Target = target;
        _resolution = spec.Resolution;
        _displacement = from - target;
        _omega = 2 * Math.PI / Math.Max(0.01, spec.Response);
        double damping = Math.Clamp(spec.Damping, 0.05, 1);
        _decay = damping * _omega;
        _omegaD = _omega * Math.Sqrt(Math.Max(0, 1 - damping * damping));
        _b = _omegaD > 1e-6 ? (velocity + _decay * _displacement) / _omegaD : velocity + _omega * _displacement;
    }

    internal bool IsAtRest(float velocity) => Math.Abs(_displacement) < _resolution && Math.Abs(velocity) < _resolution * 8;

    internal (float Value, float Velocity) Sample(double seconds)
    {
        double t = Math.Max(0, seconds), envelope = Math.Exp(-_decay * t), x, v;
        if (_omegaD > 1e-6)
        {
            double sin = Math.Sin(_omegaD * t), cos = Math.Cos(_omegaD * t);
            x = envelope * (_displacement * cos + _b * sin);
            v = envelope * ((_b * _omegaD - _decay * _displacement) * cos - (_displacement * _omegaD + _decay * _b) * sin);
        }
        else
        {
            x = (_displacement + _b * t) * envelope;
            v = (_b - _omega * (_displacement + _b * t)) * envelope;
        }
        return ((float)(Target + x), (float)v);
    }

    internal bool Settled(double seconds)
    {
        var (value, velocity) = Sample(seconds);
        return Math.Abs(value - Target) < _resolution && Math.Abs(velocity) < _resolution * 8;
    }
}
