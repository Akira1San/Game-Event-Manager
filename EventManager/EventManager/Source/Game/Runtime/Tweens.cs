using System;

namespace Game
{
    public enum TweenKind
    {
        Position,
        Scale,
        Alpha,
        Color,
    }

    public enum TweenEasing
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut,
    }

    public static class TweenMath
    {
        /// <summary>Maps raw 0-1 progress through an easing curve (0-1 result).</summary>
        public static double Ease(double t, TweenEasing easing)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            switch (easing)
            {
                case TweenEasing.EaseIn:
                    return t * t;
                case TweenEasing.EaseOut:
                    return 1 - (1 - t) * (1 - t);
                case TweenEasing.EaseInOut:
                    return t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);
                default:
                    return t;
            }
        }

        public static double Lerp(double from, double to, double t)
        {
            return from + (to - from) * t;
        }
    }

    /// <summary>
    /// One in-flight tween. Pure numbers only (no Flax types) so the timing and
    /// interpolation can be unit tested without the engine; the bridge reads the
    /// interpolated values and writes them to the actor/control each tick.
    /// </summary>
    public class TweenTrack
    {
        public object Subject;
        public string Name = "";
        public TweenKind Kind;
        public TweenEasing Easing = TweenEasing.Linear;
        public double Duration;
        public double Elapsed;
        public bool Done;

        // Position and scale.
        public double FromX, FromY, FromZ;
        public double ToX, ToY, ToZ;
        // Alpha.
        public double FromValue, ToValue;
        // Color.
        public double FromR, FromG, FromB, FromA;
        public double ToR = 1, ToG = 1, ToB = 1, ToA = 1;

        public double RawProgress => Duration <= 0 ? 1 : Math.Min(1, Elapsed / Duration);

        public double Progress => TweenMath.Ease(RawProgress, Easing);

        public double X => TweenMath.Lerp(FromX, ToX, Progress);
        public double Y => TweenMath.Lerp(FromY, ToY, Progress);
        public double Z => TweenMath.Lerp(FromZ, ToZ, Progress);
        public double Value => TweenMath.Lerp(FromValue, ToValue, Progress);
        public double R => TweenMath.Lerp(FromR, ToR, Progress);
        public double G => TweenMath.Lerp(FromG, ToG, Progress);
        public double B => TweenMath.Lerp(FromB, ToB, Progress);
        public double A => TweenMath.Lerp(FromA, ToA, Progress);

        /// <summary>Advances the clock by <paramref name="deltaSeconds"/> and returns true
        /// on the tick the tween completes (including a zero/negative duration).</summary>
        public bool Advance(double deltaSeconds)
        {
            if (Done)
                return true;
            if (Duration <= 0)
            {
                Elapsed = 0;
                Done = true;
                return true;
            }
            if (deltaSeconds > 0)
                Elapsed += deltaSeconds;
            if (Elapsed >= Duration)
            {
                Elapsed = Duration;
                Done = true;
            }
            return Done;
        }
    }
}
