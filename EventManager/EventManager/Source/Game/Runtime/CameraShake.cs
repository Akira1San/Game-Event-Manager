using System;

namespace Game
{
    /// <summary>
    /// The maths behind <c>shake camera strength &lt;n&gt; for &lt;s&gt;</c> (Task 21 Part E).
    /// A shake is a strength and a duration, and its amplitude falls linearly to zero over that
    /// duration, so the view always settles on exactly the framing it had. The per-tick offset is
    /// a deterministic pseudo-random point inside the current amplitude, derived from a seed and
    /// a tick number instead of a random generator: the same shake always wobbles the same way,
    /// and the tests need no luck. Engine-free on purpose — the bridge owns the camera.
    /// </summary>
    public static class CameraShake
    {
        /// <summary>Amplitude of a bare <c>shake camera</c>, in centimetres.</summary>
        public const double DefaultStrength = 6.0;

        /// <summary>How long a bare <c>shake camera</c> lasts, in seconds.</summary>
        public const double DefaultDuration = 0.3;

        /// <summary>
        /// How far the camera may still move <paramref name="elapsed"/> seconds into a shake of
        /// <paramref name="strength"/> lasting <paramref name="duration"/>. Full strength at the
        /// start, zero from the duration on, so a finished shake contributes no offset at all.
        /// A zero or negative duration is a shake that is already over.
        /// </summary>
        public static double Amplitude(double strength, double duration, double elapsed)
        {
            if (duration <= 0.0 || elapsed >= duration)
                return 0.0;
            if (elapsed <= 0.0)
                return Math.Abs(strength);
            return Math.Abs(strength) * (1.0 - elapsed / duration);
        }

        /// <summary>
        /// The offset for one tick, in the camera's own axes: every component lies within
        /// ±<paramref name="amplitude"/>. A zero amplitude is exactly zero, never a flicker.
        /// </summary>
        public static void Offset(double amplitude, int seed, long tick,
            out double x, out double y, out double z)
        {
            if (amplitude <= 0.0)
            {
                x = 0.0;
                y = 0.0;
                z = 0.0;
                return;
            }
            x = amplitude * Jitter(seed, tick, 1);
            y = amplitude * Jitter(seed, tick, 2);
            z = amplitude * Jitter(seed, tick, 3);
        }

        /// <summary>
        /// What a second <c>shake camera</c> does while one is already running: the stronger
        /// strength wins and the timer takes the longer of the two, so two slams in a row lengthen
        /// the wobble instead of stacking into a bigger one. <paramref name="duration"/> is the
        /// new total length counted from the same start (add the elapsed time to it).
        /// </summary>
        public static void Merge(double runningStrength, double runningElapsed, double runningDuration,
            double newStrength, double newDuration,
            out double strength, out double duration)
        {
            strength = Math.Max(Math.Abs(runningStrength), Math.Abs(newStrength));
            double remaining = Math.Max(0.0, runningDuration - runningElapsed);
            duration = Math.Max(remaining, Math.Abs(newDuration));
        }

        /// <summary>One deterministic value in [-1, 1] for a (seed, tick, axis) triple.</summary>
        private static double Jitter(int seed, long tick, int axis)
        {
            unchecked
            {
                uint hash = (uint)seed * 2654435761u;
                hash ^= (uint)tick * 2246822519u;
                hash ^= (uint)axis * 3266489917u;
                hash ^= hash >> 15;
                hash *= 2246822519u;
                hash ^= hash >> 13;
                return hash / (double)uint.MaxValue * 2.0 - 1.0;
            }
        }
    }
}
