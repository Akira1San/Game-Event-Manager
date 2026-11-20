using System;

namespace Game
{
    /// <summary>
    /// A place a sheet can come back to (Task 36 Part F). The engine-free runtime cannot read a
    /// transform, so the bridge fills one of these from the acting actor and hands it to
    /// <see cref="Checkpoints.Save"/>.
    /// </summary>
    public struct CheckpointPose
    {
        public double X;
        public double Y;
        public double Z;
        /// <summary>Heading in degrees around the actor's own up axis, -180 to 180.</summary>
        public double Yaw;
    }

    /// <summary>
    /// <c>save checkpoint</c>: remembers the acting actor's own transform as four ordinary
    /// per-actor variables, so Task 30's save file carries the checkpoint for free and a new game
    /// (which never runs the word) has none. The names are one word each on purpose — the event
    /// language reads <c>my CheckpointX</c> like any other variable.
    /// </summary>
    public static class Checkpoints
    {
        public const string XVariable = "CheckpointX";
        public const string YVariable = "CheckpointY";
        public const string ZVariable = "CheckpointZ";
        public const string YawVariable = "CheckpointYaw";

        /// <summary>Writes all four variables in one go, so a half-written checkpoint is never read.</summary>
        public static void Save(EventVariables variables, object host, CheckpointPose pose)
        {
            if (variables == null || host == null)
                return;
            variables.SetLocal(host, XVariable, ValueBox.OfNumber(pose.X));
            variables.SetLocal(host, YVariable, ValueBox.OfNumber(pose.Y));
            variables.SetLocal(host, ZVariable, ValueBox.OfNumber(pose.Z));
            variables.SetLocal(host, YawVariable, ValueBox.OfNumber(pose.Yaw));
        }

        /// <summary>
        /// The checkpoint this host saved, or false when it never saved one (or only part of the
        /// four survived). Asks <see cref="EventVariables.HasLocal"/> first, so looking for a
        /// checkpoint that was never written does not warn about an unknown variable.
        /// </summary>
        public static bool TryRead(EventVariables variables, object host, out CheckpointPose pose)
        {
            pose = new CheckpointPose();
            if (variables == null || host == null)
                return false;
            if (!variables.HasLocal(host, XVariable) || !variables.HasLocal(host, YVariable) ||
                !variables.HasLocal(host, ZVariable) || !variables.HasLocal(host, YawVariable))
                return false;
            pose.X = variables.GetLocal(host, XVariable).AsNumber();
            pose.Y = variables.GetLocal(host, YVariable).AsNumber();
            pose.Z = variables.GetLocal(host, ZVariable).AsNumber();
            pose.Yaw = variables.GetLocal(host, YawVariable).AsNumber();
            return true;
        }

        /// <summary>
        /// The heading a rotation quaternion points along, in degrees. Flax's
        /// <c>Actor.Orientation</c> is a quaternion and nothing in the bridge reads Euler angles off
        /// it, so the yaw is decomposed here where it can be tested.
        /// </summary>
        public static double YawOf(double x, double y, double z, double w)
        {
            double sin = 2 * (w * y + x * z);
            double cos = 1 - 2 * (y * y + z * z);
            if (Math.Abs(sin) < 1e-12 && Math.Abs(cos) < 1e-12)
                return 0;
            return Math.Atan2(sin, cos) * 180.0 / Math.PI;
        }
    }
}
