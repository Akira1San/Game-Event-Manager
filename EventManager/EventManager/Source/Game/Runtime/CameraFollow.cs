using System;

namespace Game
{
    /// <summary>
    /// The maths behind <c>camera follows &lt;thing&gt;</c> (Task 52): the offset a follow keeps, and
    /// the frame-rate independent easing that carries the camera back into it. No Flax types live
    /// here, so both rules are unit tested; the bridge owns the real camera.
    ///
    /// The offset comes from where the scene *placed* the camera — the camera's position minus the
    /// subject's — so a camera authored to the side of the player stays to the side instead of
    /// jumping on top of them. While following, the camera closes <c>1 - exp(-damping * dt)</c> of
    /// the remaining gap each tick: a big damping follows hard, a small one drifts, and the same
    /// damping feels identical at 30, 60 and 144 fps. Damping is authored with the
    /// <c>CameraFollowDamping</c> global (per second, default 6); 0 means snap, which is what the
    /// follow did before this task.
    /// </summary>
    public static class CameraFollow
    {
        /// <summary>The global that tunes how hard the camera follows (per second).</summary>
        public const string DampingVariable = "CameraFollowDamping";

        /// <summary>The authored feel: the camera covers about 63% of the gap every 1/6 s.</summary>
        public const double DefaultDamping = 6.0;

        /// <summary>The offset a follow keeps: where the camera was placed relative to its
        /// subject, captured the moment the follow starts.</summary>
        public static CameraPoint Offset(CameraPoint camera, CameraPoint target)
            => new CameraPoint(camera.X - target.X, camera.Y - target.Y, camera.Z - target.Z);

        /// <summary>
        /// How much of the remaining gap to close this tick, in 0..1. A zero or negative damping
        /// snaps (the old behaviour), and a zero-length tick moves nothing at all.
        /// </summary>
        public static double Alpha(double damping, double delta)
        {
            if (delta <= 0.0)
                return 0.0;
            if (damping <= 0.0)
                return 1.0;
            double alpha = 1.0 - Math.Exp(-damping * delta);
            if (alpha < 0.0)
                return 0.0;
            if (alpha > 1.0)
                return 1.0;
            return alpha;
        }

        /// <summary>The damping this game uses right now (the default when the sheet never set
        /// <c>CameraFollowDamping</c>).</summary>
        public static double DampingFrom(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(DampingVariable))
                return DefaultDamping;
            return variables.GetGlobal(DampingVariable).AsNumber();
        }
    }
}
