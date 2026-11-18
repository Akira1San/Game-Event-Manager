using System;

namespace Game
{
    /// <summary>
    /// What the NavMesh said about one walk (Task 29 play-check follow-up). The bridge used to
    /// treat every failed query as a blocked path, which both stopped units in levels whose mesh
    /// was missing or still baking and fired <c>when path blocked</c> for a walk that carried on.
    /// The three answers are the whole distinction:
    /// <list type="bullet">
    /// <item><see cref="Found"/> — there is a route, so the unit walks it.</item>
    /// <item><see cref="Blocked"/> — the mesh answered and there is no way through: a wall, a prop,
    /// water, or a refused landing. This is the only answer that fires <c>when path blocked</c>,
    /// because it is the only one where the unit really stops.</item>
    /// <item><see cref="Unavailable"/> — the mesh cannot answer at all: nothing is baked, Flax is
    /// still building it, or one of the points is off it. Nothing is blocked here, so the unit
    /// walks the straight line and the sheet hears no trigger.</item>
    /// </list>
    /// Pure logic with no Flax types, so the tests drive the decision table directly; the bridge
    /// (<c>FlaxActions.QueryNavRoute</c>) runs the actual <c>Navigation</c> queries and hands the
    /// results in.
    /// </summary>
    public enum NavRouteAnswer
    {
        /// <summary>A route exists.</summary>
        Found,
        /// <summary>The mesh answered and there is no way through — the unit stops here.</summary>
        Blocked,
        /// <summary>The mesh cannot answer (nothing baked, still building, a point off it).</summary>
        Unavailable,
    }

    /// <summary>The rules behind <see cref="NavRouteAnswer"/>.</summary>
    public static class NavRoute
    {
        /// <summary>
        /// How far, horizontally, a query point may sit from the nearest point on the mesh and
        /// still count as on it. Height is deliberately ignored: a unit's pivot sits above the
        /// floor it stands on, and a click lands on the floor's surface, so a vertical offset says
        /// nothing about whether the mesh covers the spot.
        /// </summary>
        public const double OnMeshTolerance = 1.0;

        /// <summary>
        /// Classifies one walk from what the queries returned.
        /// </summary>
        /// <param name="building">Flax is still baking the mesh (<c>Navigation.IsBuildingNavMesh</c>).</param>
        /// <param name="pathFound">The plain query found a route.</param>
        /// <param name="startOnMesh">The start point snapped onto the mesh at all.</param>
        /// <param name="startOff">How far the start point sits from the mesh, horizontally.</param>
        /// <param name="endOnMesh">The end point snapped onto the mesh at all.</param>
        /// <param name="endOff">How far the end point sits from the mesh, horizontally.</param>
        /// <param name="snappedPathFound">The query between the two snapped points found a route.</param>
        public static NavRouteAnswer Classify(bool building, bool pathFound, bool startOnMesh,
            double startOff, bool endOnMesh, double endOff, bool snappedPathFound)
        {
            if (pathFound)
                return NavRouteAnswer.Found;
            // Nothing to answer with yet: a mesh that is still baking finishes shortly, and one
            // that was never baked may never come. Either way this is not a wall.
            if (building)
                return NavRouteAnswer.Unavailable;
            // No nearest point at all means there is no mesh under that spot; a nearest point far
            // from the spot means the spot is off the mesh (the edge of the floor, a click on a
            // prop, a body pushed onto geometry). Neither is the mesh refusing a route.
            if (!startOnMesh || !endOnMesh)
                return NavRouteAnswer.Unavailable;
            if (startOff > OnMeshTolerance || endOff > OnMeshTolerance)
                return NavRouteAnswer.Unavailable;
            // Both ends are on the mesh and it found a route once they were snapped there.
            if (snappedPathFound)
                return NavRouteAnswer.Found;
            // Both ends are on a baked mesh and it still has no route between them: the way is
            // really closed, so the unit stops and the sheet hears `when path blocked`.
            return NavRouteAnswer.Blocked;
        }

        /// <summary>True when the walk carries on along the straight line — the mesh had no say.</summary>
        public static bool WalksStraight(NavRouteAnswer answer)
        {
            return answer == NavRouteAnswer.Unavailable;
        }

        /// <summary>True when the unit really stops and <c>when path blocked</c> fires.</summary>
        public static bool Stops(NavRouteAnswer answer)
        {
            return answer == NavRouteAnswer.Blocked;
        }

        /// <summary>Horizontal distance between a query point and the mesh point it snapped to.</summary>
        public static double OffMesh(double askedX, double askedZ, double meshX, double meshZ)
        {
            double dx = askedX - meshX;
            double dz = askedZ - meshZ;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
