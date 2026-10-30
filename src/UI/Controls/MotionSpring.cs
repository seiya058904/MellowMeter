namespace LiteMonitor.src.UI.Controls;

// Analytic damped spring, independent of timer cadence. Velocity is in units/second,
// so an interrupted interaction can keep its momentum instead of restarting at rest.
internal readonly record struct MotionSpring(float From, float Target, float Velocity, double Duration, bool Bounce)
{
    internal (float Value, float Velocity) Sample(double seconds)
    {
        if (seconds >= Duration) return (Target, 0);
        double t = Math.Max(0, seconds) / Duration;
        double displacement = From - Target;
        double initialVelocity = Velocity * Duration;
        const double frequency = 12;
        double x, v;
        if (Bounce)
        {
            const double damping = 0.78;
            double decay = damping * frequency;
            double oscillation = frequency * Math.Sqrt(1 - damping * damping);
            double b = (initialVelocity + decay * displacement) / oscillation;
            double envelope = Math.Exp(-decay * t);
            double sin = Math.Sin(oscillation * t), cos = Math.Cos(oscillation * t);
            x = envelope * (displacement * cos + b * sin);
            v = envelope * ((b * oscillation - decay * displacement) * cos -
                (displacement * oscillation + decay * b) * sin);
        }
        else
        {
            double b = initialVelocity + frequency * displacement;
            double envelope = Math.Exp(-frequency * t);
            x = (displacement + b * t) * envelope;
            v = (b - frequency * (displacement + b * t)) * envelope;
        }
        return ((float)(Target + x), (float)(v / Duration));
    }
}
