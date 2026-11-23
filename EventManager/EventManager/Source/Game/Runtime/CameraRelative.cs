using System;

namespace Game
{
    /// <summary>
    /// Pure rules behind the opt-in <c>Move Relative To Camera</c> sheet flag (Task_FPS Phase 2):
    /// `move forward/back/left/right` resolves against where the view looks instead of where the
    /// body faces, so strafing never turns the actor. Kept free of Flax types so the axis math is
    /// unit tested; the bridge supplies the camera's flattened heading.
    /// </summary>
    public static class CameraRelative
    {
        /// <summary>
        /// Flattens a world direction onto the ground plane and normalises it, giving the heading a
        /// `move` word resolves against. False when the direction is straight up or down (a camera
        /// pointing at its own feet has no forward to walk along).
        /// </summary>
        public static bool TryHeading(double x, double y, double z,
            out double headingX, out double headingZ)
        {
            headingX = 0.0;
            headingZ = 0.0;
            double length = Math.Sqrt(x * x + z * z);
            if (length <= 1e-6)
                return false;
            headingX = x / length;
            headingZ = z / length;
            return true;
        }

        /// <summary>
        /// Resolves the four planar move words against a view heading: forward walks where the
        /// camera looks, back reverses, and left/right strafe perpendicular to it. Returns false
        /// for anything else (up/down, a target move), so the caller keeps the actor-relative
        /// answer it already had.
        /// </summary>
        public static bool TryResolve(string dir, double forwardX, double forwardZ,
            out double x, out double z)
        {
            x = 0.0;
            z = 0.0;
            switch (dir)
            {
                case "forward":
                    x = forwardX;
                    z = forwardZ;
                    return true;
                case "back":
                case "backward":
                    x = -forwardX;
                    z = -forwardZ;
                    return true;
                case "right":
                    // Right of a heading in Flax's +Y-up space is the heading turned a quarter
                    // turn clockwise seen from above: (0,0,1) faces +Z, whose right is +X.
                    x = forwardZ;
                    z = -forwardX;
                    return true;
                case "left":
                    x = -forwardZ;
                    z = forwardX;
                    return true;
                default:
                    return false;
            }
        }
    }
}
