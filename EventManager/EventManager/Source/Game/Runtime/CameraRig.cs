using System;

namespace Game
{
    /// <summary>
    /// RTS free camera (Task 26): zoom clamps, edge and key panning, and the pan speed that
    /// scales with zoom. No Flax types live here — the bridge reads the mouse, keyboard and
    /// scene camera and applies what these rules decide, so the rules are unit tested.
    ///
    /// Free-pan mode is armed by <c>camera free</c>, which is also what keeps older scenes
    /// untouched: unless an event frees the camera, none of this runs and the follow / snap /
    /// aim verbs behave exactly as before.
    ///
    /// State lives in globals so a sheet can read and author it:
    /// <c>CameraZoom</c> (1 = as authored, larger = closer), <c>CameraFree</c> (0/1),
    /// <c>CameraPanX</c> / <c>CameraPanZ</c> (the ground point the camera is centred on),
    /// <c>CameraEdgePan</c> (1 = pan while the mouse rests at a screen edge), and
    /// <c>CameraEdgeThickness</c> / <c>CameraPanSpeed</c> for the feel.
    /// </summary>
    public static class CameraRig
    {
        public const string ZoomVariable = "CameraZoom";
        public const string FreeVariable = "CameraFree";
        public const string PanXVariable = "CameraPanX";
        public const string PanZVariable = "CameraPanZ";
        public const string EdgePanVariable = "CameraEdgePan";
        public const string EdgeThicknessVariable = "CameraEdgeThickness";
        public const string PanSpeedVariable = "CameraPanSpeed";

        /// <summary>As authored: the camera sits at the scene's own height and depth.</summary>
        public const double DefaultZoom = 1.0;

        /// <summary>Furthest out: four times the authored height.</summary>
        public const double MinZoom = 0.25;

        /// <summary>Closest in: a quarter of the authored height.</summary>
        public const double MaxZoom = 4.0;

        /// <summary>One wheel notch, or <c>camera zoom in</c>.</summary>
        public const double ZoomStep = 0.1;

        /// <summary>How many pixels from the screen edge count as "at the edge".</summary>
        public const double DefaultEdgeThickness = 12;

        /// <summary>Ground units a free camera pans per second at zoom 1.</summary>
        public const double DefaultPanSpeed = 600;

        /// <summary>Never let zoom push the camera into the floor, whatever the scene height.</summary>
        public const double MinHeight = 30;

        public static double ClampZoom(double zoom)
        {
            if (zoom < MinZoom)
                return MinZoom;
            if (zoom > MaxZoom)
                return MaxZoom;
            return zoom;
        }

        /// <summary>The zoom this game uses right now (1 when never set).</summary>
        public static double ZoomFrom(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(ZoomVariable))
                return DefaultZoom;
            return ClampZoom(variables.GetGlobal(ZoomVariable).AsNumber());
        }

        /// <summary><c>zoom camera by &lt;n&gt;</c>: positive moves closer, negative further out.</summary>
        public static double ZoomBy(double zoom, double delta)
            => ClampZoom(ClampZoom(zoom) + delta);

        /// <summary><c>camera zoom in</c>.</summary>
        public static double ZoomedIn(double zoom) => ZoomBy(zoom, ZoomStep);

        /// <summary><c>camera zoom out</c>.</summary>
        public static double ZoomedOut(double zoom) => ZoomBy(zoom, -ZoomStep);

        /// <summary>Multiplier for the camera's height and depth offsets: closer when zoomed in.</summary>
        public static double ScaleFor(double zoom) => 1.0 / ClampZoom(zoom);

        /// <summary>The height the camera sits above its focus at this zoom, never into the floor.</summary>
        public static double HeightFor(double baseHeight, double zoom)
        {
            if (baseHeight <= 0)
                return baseHeight; // a scene that authored no height keeps none
            double height = baseHeight * ScaleFor(zoom);
            // The floor is MinHeight, but never above the height the scene authored: a scene
            // that sits its camera low keeps its framing instead of being pushed up.
            return height < MinHeight ? Math.Min(baseHeight, MinHeight) : height;
        }

        /// <summary>The depth offset at this zoom (negative: behind the focus).</summary>
        public static double DepthFor(double baseDepth, double zoom) => baseDepth * ScaleFor(zoom);

        /// <summary>
        /// How far the free camera pans per second while zoomed in. Zooming in shrinks what is
        /// on screen, so the camera crosses the ground more slowly and a nudge stays a nudge.
        /// </summary>
        public static double PanSpeedFor(double baseSpeed, double zoom)
        {
            double speed = baseSpeed <= 0 ? DefaultPanSpeed : baseSpeed;
            return speed * ScaleFor(zoom);
        }

        /// <summary>The ground direction a written pan word means (<c>left</c>, <c>right</c>,
        /// <c>forward</c>/<c>up</c>, <c>backward</c>/<c>back</c>/<c>down</c>). The camera has no
        /// yaw in this project, so screen forward is world +Z and screen right is world +X.</summary>
        public static bool TryDirection(string written, out double dx, out double dz)
        {
            dx = 0;
            dz = 0;
            if (string.IsNullOrWhiteSpace(written))
                return false;
            switch (written.Trim().ToLowerInvariant())
            {
                case "left":
                    dx = -1;
                    return true;
                case "right":
                    dx = 1;
                    return true;
                case "forward":
                case "up":
                    dz = 1;
                    return true;
                case "backward":
                case "back":
                case "down":
                    dz = -1;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Which way the mouse being parked near a screen edge should pan the ground, as a unit
        /// vector (<paramref name="dx"/> across, <paramref name="dz"/> into the screen). Both
        /// edges at once pan along the diagonal.
        /// </summary>
        public static bool EdgeDirection(double mouseX, double mouseY, double width, double height,
            double thickness, out double dx, out double dz)
        {
            dx = 0;
            dz = 0;
            if (width <= 0 || height <= 0)
                return false;
            if (thickness <= 0)
                thickness = DefaultEdgeThickness;

            if (mouseX < thickness)
                dx = -1;
            else if (mouseX > width - thickness)
                dx = 1;
            // Screen Y grows downward, and world +Z runs up the screen.
            if (mouseY < thickness)
                dz = 1;
            else if (mouseY > height - thickness)
                dz = -1;

            if (dx == 0 && dz == 0)
                return false;
            double length = Math.Sqrt(dx * dx + dz * dz);
            dx /= length;
            dz /= length;
            return true;
        }

        /// <summary>The direction the held pan keys ask for, combining whatever is down.</summary>
        public static bool KeyDirection(bool left, bool right, bool forward, bool backward,
            out double dx, out double dz)
        {
            dx = (right ? 1 : 0) - (left ? 1 : 0);
            dz = (forward ? 1 : 0) - (backward ? 1 : 0);
            if (dx == 0 && dz == 0)
                return false;
            double length = Math.Sqrt(dx * dx + dz * dz);
            dx /= length;
            dz /= length;
            return true;
        }

        /// <summary>Moves the pan focus, normalising a diagonal so it is no faster than a straight pan.</summary>
        public static void Step(double focusX, double focusZ, double dx, double dz, double distance,
            out double x, out double z)
        {
            x = focusX;
            z = focusZ;
            if (distance == 0 || (dx == 0 && dz == 0))
                return;
            double length = Math.Sqrt(dx * dx + dz * dz);
            x = focusX + dx / length * distance;
            z = focusZ + dz / length * distance;
        }
    }
}
