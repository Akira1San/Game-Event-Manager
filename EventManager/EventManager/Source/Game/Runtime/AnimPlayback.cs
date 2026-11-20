using System;

namespace Game
{
    /// <summary>
    /// Animation playback rules (Task 33 Part A) — engine-free, so the tests drive the same
    /// arithmetic the bridge uses. The bridge writes the resulting speed to
    /// <c>AnimatedModel.UpdateSpeed</c>, which Flax applies to the running clip live; nothing here
    /// touches Flax.
    /// </summary>
    public static class AnimPlayback
    {
        /// <summary>Speed a clip plays at until <c>set anim speed</c> says otherwise.</summary>
        public const float DefaultSpeed = 1f;

        /// <summary>Slowest playback. Zero would freeze the clip with no way to read its time,
        /// so an attempt to stop belongs in <c>stop anim</c>.</summary>
        public const float MinSpeed = 0.05f;

        /// <summary>Fastest playback — beyond this a one-shot is over in a frame or two.</summary>
        public const float MaxSpeed = 10f;

        /// <summary>Keeps a playback speed inside the range Flax can actually render.</summary>
        public static float ClampSpeed(double speed)
        {
            if (double.IsNaN(speed) || speed < MinSpeed)
                return MinSpeed;
            if (speed > MaxSpeed)
                return MaxSpeed;
            return (float)speed;
        }

        /// <summary>When a one-shot started at <paramref name="now"/> finishes at this speed.</summary>
        public static double EndTime(double now, double length, double speed)
            => now + length / ClampSpeed(speed);

        /// <summary>
        /// Where a one-shot that started at <paramref name="startTime"/> now ends, after its speed
        /// changed from <paramref name="oldSpeed"/> to <paramref name="newSpeed"/>. The part already
        /// played keeps the time it took, so <c>when anim ... ends</c> still fires when the clip
        /// really finishes instead of at the old speed's estimate.
        /// </summary>
        public static double RetimedEnd(double now, double startTime, double length,
            double oldSpeed, double newSpeed)
        {
            double played = Math.Max(0, now - startTime) * ClampSpeed(oldSpeed);
            double remaining = Math.Max(0, length - played);
            return now + remaining / ClampSpeed(newSpeed);
        }
    }
}
