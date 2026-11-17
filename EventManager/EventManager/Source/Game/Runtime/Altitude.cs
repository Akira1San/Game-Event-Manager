using System;

namespace Game
{
    /// <summary>
    /// RTS flight height (Task 28 Part A): a per-unit <c>my Altitude</c> — how far above the point
    /// the unit was placed it holds itself. The number is an ordinary per-object variable, so a
    /// sheet writes it once (<c>set my Altitude to 120</c>) and the bridge keeps the actor there:
    /// ground moves no longer change its height, and writing it back to 0 puts the unit down.
    ///
    /// Both spellings are read as one value, so a sheet that wrote <c>my altitude</c> and a HUD
    /// that reads <c>my Altitude</c> can never end up with two heights. A unit that never wrote
    /// one is on the ground (0), which keeps every existing sheet's Y exactly as it was —
    /// <c>jump</c>, <c>platform</c> and physics still own the vertical axis of those actors.
    ///
    /// The movement half lives in the bridge (<c>FlaxActions.StepAltitude</c>), because positions
    /// are Flax types; this class is the pure part the tests drive.
    /// </summary>
    public static class Altitude
    {
        /// <summary>One spelling of the per-unit variable.</summary>
        public const string Variable = "altitude";

        /// <summary>Every spelling the language accepts, most common first.</summary>
        public static readonly string[] VariableNames = { "Altitude", "altitude" };

        /// <summary>True for any spelling of the per-unit altitude variable.</summary>
        public static bool IsVariableName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (string candidate in VariableNames)
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// The unit's height above its ground, or 0 when it never wrote one. A negative or
        /// unreadable value reads as 0 rather than pushing the actor underground.
        /// </summary>
        public static double Of(EventVariables variables, object unit)
        {
            if (variables == null || unit == null)
                return 0;
            foreach (string name in VariableNames)
            {
                if (!variables.HasLocal(unit, name))
                    continue;
                double height = variables.GetLocal(unit, name).AsNumber();
                return height > 0 ? height : 0;
            }
            return 0;
        }

        /// <summary>True when the unit is flying (its altitude is above 0).</summary>
        public static bool IsAirborne(EventVariables variables, object unit)
        {
            return Of(variables, unit) > 0;
        }

        /// <summary>The ground height under an actor at <paramref name="positionY"/>.</summary>
        public static double GroundY(double positionY, double altitude)
        {
            return positionY - Math.Max(0, altitude);
        }

        /// <summary>The world Y an actor holds when it flies <paramref name="altitude"/> above
        /// <paramref name="groundY"/>.</summary>
        public static double PinY(double groundY, double altitude)
        {
            return groundY + Math.Max(0, altitude);
        }
    }
}
