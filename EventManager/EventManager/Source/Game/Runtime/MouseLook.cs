using System;

namespace Game
{
    /// <summary>
    /// First-person mouse look (Task_FPS Phase 1): one frame of look input — yaw/pitch
    /// accumulation from the mouse delta, the pitch clamp, and the sensitivity/invert knobs.
    /// No Flax types live here, so the rules are unit tested; the bridge reads
    /// <c>Input.MousePositionDelta</c> and applies the answer to the player and its camera child.
    ///
    /// Sign convention, chosen to match the engine: yaw grows to the right (a positive Y Euler
    /// turns the body clockwise seen from above, the same as <c>turn right</c>), and pitch grows
    /// downward (a positive X Euler tilts the camera down, the same as the <c>CameraPitch</c>
    /// framing). Mouse deltas grow right and down, so the default is uninverted.
    ///
    /// Feel lives in globals, so a sheet tunes it with the ordinary words — `set LookSensitivity
    /// to 0.2`, `set LookInvertY to 1`:
    /// <c>LookSensitivity</c> (degrees per pixel, default 0.12), <c>LookInvertX</c> /
    /// <c>LookInvertY</c> (1 flips that axis).
    /// </summary>
    public static class MouseLook
    {
        public const string SensitivityVariable = "LookSensitivity";
        public const string InvertXVariable = "LookInvertX";
        public const string InvertYVariable = "LookInvertY";

        /// <summary>Degrees of turn per pixel: a 400-pixel flick is about 48°.</summary>
        public const double DefaultSensitivity = 0.12;

        /// <summary>How far up or down the view may tilt before it flips over.</summary>
        public const double MaxPitch = 89.0;

        /// <summary>The sensitivity this game uses right now (the default when unset, and a
        /// non-positive authored value falls back to it so the look can never freeze).</summary>
        public static double SensitivityFrom(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(SensitivityVariable))
                return DefaultSensitivity;
            double value = variables.GetGlobal(SensitivityVariable).AsNumber();
            return value > 0.0 ? value : DefaultSensitivity;
        }

        /// <summary>True when an invert global is authored non-zero.</summary>
        public static bool InvertFrom(EventVariables variables, string variable)
        {
            return variables != null && variables.HasGlobal(variable) &&
                   variables.GetGlobal(variable).AsNumber() != 0.0;
        }

        /// <summary>
        /// One frame of look: adds this frame's mouse movement (in pixels, growing right and down)
        /// scaled by <paramref name="sensitivity"/>, wraps yaw and clamps pitch. A zero delta is
        /// exactly a no-op, so a still mouse never drifts.
        /// </summary>
        public static void Step(double yaw, double pitch, double deltaX, double deltaY,
            double sensitivity, bool invertX, bool invertY,
            out double newYaw, out double newPitch)
        {
            if (sensitivity <= 0.0)
                sensitivity = DefaultSensitivity;
            double turnX = invertX ? -deltaX : deltaX;
            double turnY = invertY ? -deltaY : deltaY;
            newYaw = WrapYaw(yaw + turnX * sensitivity);
            newPitch = ClampPitch(pitch + turnY * sensitivity);
        }

        /// <summary>Keeps yaw in (-180, 180], so a long session cannot grow the angle forever.</summary>
        public static double WrapYaw(double yaw)
        {
            if (yaw > 180.0 || yaw <= -180.0)
                yaw -= Math.Floor((yaw + 180.0) / 360.0) * 360.0;
            return yaw;
        }

        /// <summary>Keeps the view from looking past straight up or straight down.</summary>
        public static double ClampPitch(double pitch)
        {
            if (pitch > MaxPitch)
                return MaxPitch;
            if (pitch < -MaxPitch)
                return -MaxPitch;
            return pitch;
        }
    }
}
