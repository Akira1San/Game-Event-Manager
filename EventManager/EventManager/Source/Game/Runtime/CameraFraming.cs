using System;

namespace Game
{
    /// <summary>
    /// Pure decisions behind the top-down camera framing globals — <c>CameraHeight</c>,
    /// <c>CameraDepth</c> and <c>CameraPitch</c>. No Flax types live here, so the rules are
    /// unit tested; the bridge applies them to the real camera.
    ///
    /// The camera sits at <c>target + (0, height, depth)</c> and looks along its own forward
    /// axis, pitched down by <c>pitch</c> degrees. The target lands in the middle of the screen
    /// only when <c>depth = -height / tan(pitch)</c> — <see cref="CenteredDepth"/>. A 90° view
    /// looks straight down, so it needs no backward offset at all (that is the TK4 framing).
    /// </summary>
    public static class CameraFraming
    {
        /// <summary>Default pitch: the oblique top-down view every existing scene already uses.</summary>
        public const double DefaultPitch = 45.0;

        /// <summary>A flat view cannot be squared up with the world; 1° keeps a little depth.</summary>
        public const double MinPitch = 1.0;

        /// <summary>Straight down.</summary>
        public const double MaxPitch = 90.0;

        /// <summary>Keeps an authored pitch inside what a top-down camera can use.</summary>
        public static double ClampPitch(double pitch)
        {
            if (pitch < MinPitch)
                return MinPitch;
            if (pitch > MaxPitch)
                return MaxPitch;
            return pitch;
        }

        /// <summary>How far behind the target the camera must sit so the target is centred:
        /// <c>height / tan(pitch)</c>. Straight down (90°) needs none.</summary>
        public static double BackOffset(double height, double pitch)
        {
            double tangent = Math.Tan(ClampPitch(pitch) * Math.PI / 180.0);
            if (tangent <= 1e-9)
                return 0.0;
            return Math.Max(0.0, height) / tangent;
        }

        /// <summary>The <c>CameraDepth</c> value that centres the target for this height/pitch
        /// (negative: behind the target, since Flax forward is +Z).</summary>
        public static double CenteredDepth(double height, double pitch)
            => -BackOffset(height, pitch);
    }
}
