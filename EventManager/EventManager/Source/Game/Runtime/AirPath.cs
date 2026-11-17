using System;

namespace Game
{
    /// <summary>
    /// RTS air pathing (Task 28 Part B): a flying unit travels a straight XZ line at the height it
    /// holds instead of asking the NavMesh for a route, so it crosses terrain marks, props and
    /// buildings and can never report <c>when path blocked</c>.
    ///
    /// A unit flies when its own type is <c>air</c> (Task 22 Part A) — even at height 0, since the
    /// type is what the terrain rules read — or when it keeps an <c>my Altitude</c> above 0
    /// (Task 28 Part A). The second case is what lets a lifted-off building, which is still a
    /// <c>land</c> type, clear the ground while it is up.
    ///
    /// Pure logic with no Flax types, so the tests drive it directly; the bridge
    /// (<c>FlaxActions</c>) builds the one-waypoint route, skips the terrain gate and keeps the
    /// height. Nothing changes for a unit that never wrote a type or an altitude.
    /// </summary>
    public static class AirPath
    {
        /// <summary>
        /// True when the unit flies: <c>me is air</c>, or an altitude above 0 is keeping it up.
        /// </summary>
        public static bool IsFlying(EventVariables variables, object unit)
        {
            if (variables == null || unit == null)
                return false;
            if (string.Equals(UnitTypes.TypeOf(variables, unit), UnitTypes.Air,
                StringComparison.OrdinalIgnoreCase))
                return true;
            return Altitude.Of(variables, unit) > 0;
        }

        /// <summary>
        /// <c>when path blocked</c> is a ground report — a flyer has no route to lose, so it never
        /// raises it (Task 28 Part B).
        /// </summary>
        public static bool ReportsBlocked(EventVariables variables, object unit)
        {
            return !IsFlying(variables, unit);
        }

        /// <summary>
        /// The height a flyer holds while chasing: it climbs to a flying target's altitude, so an
        /// air-versus-air chase ends at one height, and keeps its own over anything on the ground.
        /// </summary>
        public static double ChaseAltitude(double myAltitude, double targetAltitude, bool targetFlying)
        {
            if (targetFlying && targetAltitude > 0)
                return targetAltitude;
            return myAltitude > 0 ? myAltitude : 0;
        }

        /// <summary>Both actors' own type and altitude decide <see cref="ChaseAltitude"/>.</summary>
        public static double ChaseAltitudeFor(EventVariables variables, object mover, object target)
        {
            if (variables == null || mover == null || target == null)
                return 0;
            return ChaseAltitude(Altitude.Of(variables, mover), Altitude.Of(variables, target),
                IsFlying(variables, target));
        }
    }
}
