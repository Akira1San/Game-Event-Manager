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

        /// <summary>
        /// How far through a clip that started at <paramref name="startTime"/> playback has got,
        /// where 1 is the clip's own length (and 2 means two passes). A one-shot stops at 1 — it
        /// holds its last frame — so `reaches 100%` fires at the end; a loop keeps counting, which
        /// lets a 100% trigger mean "the first pass is over". Speed scales the clock, so the
        /// percent follows what is on screen, not wall time.
        /// </summary>
        public static double Progress(double now, double startTime, double length, double speed, bool loop)
        {
            if (length <= 0)
                return loop ? 0 : 1;
            double played = PlayedTime(now, startTime, speed);
            double progress = played / length;
            return !loop && progress > 1 ? 1 : progress;
        }

        /// <summary>
        /// True once playback has reached <paramref name="percent"/> (0–100). The small epsilon
        /// keeps a 0% trigger from being skipped by floating-point noise on the first poll.
        /// </summary>
        public static bool Crossed(double progress, double percent)
            => progress * 100.0 >= percent - 1e-9;

        /// <summary>Clip seconds played by <paramref name="now"/> since the clip's anchor started.</summary>
        public static double PlayedTime(double now, double startTime, double speed)
            => Math.Max(0, now - startTime) * ClampSpeed(speed);

        /// <summary>
        /// Which play a <c>reaches</c> poll should read: the whole-body clip when it is the one asked
        /// for, otherwise the upper-body layer (Task 33 Part C). A <c>play anim "Swing" upper body</c>
        /// has no whole-body state at all — its only clock is the slot's — so without the fallback the
        /// layered attack could never fire on the clip's impact moment.
        /// </summary>
        /// <param name="wanted">The normalized clip key the trigger names.</param>
        /// <param name="bodyClip">The whole-body play's clip key (null when nothing is playing).</param>
        /// <param name="bodyLength">Its clip length; 0 means there is no clock to read.</param>
        /// <param name="upperClip">The layered play's clip key (null when the slot is idle).</param>
        /// <param name="upperLength">Its clip length; 0 means no clock either.</param>
        /// <param name="useUpper">True when the answer came from the layer.</param>
        public static bool PickForPercent(string wanted, string bodyClip, double bodyLength,
            string upperClip, double upperLength, out bool useUpper)
        {
            useUpper = false;
            if (bodyLength > 0 && bodyClip == wanted)
                return true;
            if (upperLength > 0 && upperClip == wanted)
            {
                useUpper = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The start time a running clip must be treated as having after its speed changes, so the
        /// time already played at <paramref name="oldSpeed"/> still counts. Progress is measured from
        /// that anchor, so without this a percent trigger would jump: half a clip played at 1×, then
        /// switched to 2×, would read 100% instead of continuing from 50%. Mirrors
        /// <see cref="RetimedEnd"/>, which keeps the one-shot end clock on the same footing.
        /// </summary>
        public static double ReanchoredStart(double now, double startTime, double length,
            double oldSpeed, double newSpeed)
        {
            double played = PlayedTime(now, startTime, oldSpeed);
            if (length > 0 && played > length)
                played = length;
            return now - played / ClampSpeed(newSpeed);
        }
    }
}
