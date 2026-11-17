using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// RTS construction state (Task 18 Part C): per-building progress, the completion edge,
    /// and the optional grid-occupancy map used by <c>place … on grid</c>. Pure logic with no
    /// Flax types, so the event runtime and the tests drive it directly.
    /// </summary>
    public class ConstructionManager
    {
        /// <summary>Progress at which a building counts as finished.</summary>
        public const double CompleteAt = 100.0;

        /// <summary>Global variable that sets the world units per grid cell (default 64).</summary>
        public const string GridSizeVariable = "BuildGridSize";
        public const double DefaultGridSize = 64.0;

        private sealed class BuildState
        {
            public string Building = "";
            public double Progress;
            public bool CompletionRaised;
        }

        private readonly Dictionary<object, BuildState> _builds = new Dictionary<object, BuildState>();
        /// <summary>Occupied cell → the building actor that reserved it.</summary>
        private readonly Dictionary<(int X, int Z), object> _cells = new Dictionary<(int X, int Z), object>();

        /// <summary>
        /// Starts construction of <paramref name="building"/> on a freshly placed actor:
        /// progress 0, mirrored to the actor's <c>BuildProgress</c> local, no event raised
        /// (the placement action raises <c>BuildingPlaced</c>).
        /// </summary>
        public void RegisterPlaced(object actor, string building, EventVariables variables)
        {
            if (actor == null)
                return;
            _builds[actor] = new BuildState { Building = building ?? "", Progress = 0 };
            Mirror(variables, actor, 0);
        }

        public bool IsUnderConstruction(object actor)
        {
            return actor != null && _builds.ContainsKey(actor);
        }

        public bool IsBuilding(object actor)
        {
            return actor != null && _builds.ContainsKey(actor);
        }

        /// <summary>The template name stored when the building was placed, or "" when unknown.</summary>
        public string BuildingOf(object actor)
        {
            return actor != null && _builds.TryGetValue(actor, out BuildState state) ? state.Building : "";
        }

        public double ProgressOf(object actor)
        {
            return actor != null && _builds.TryGetValue(actor, out BuildState state) ? state.Progress : 0;
        }

        public void SetProgress(EventVariables variables, object actor, double value)
        {
            if (actor == null)
                return;
            value = Math.Max(0, value);
            if (_builds.TryGetValue(actor, out BuildState state))
                state.Progress = value;
            // Mirror even without a build state so `my build progress` and the
            // `build progress is higher than <n>` condition read the same number.
            Mirror(variables, actor, value);
        }

        public void AddProgress(EventVariables variables, object actor, double delta)
        {
            if (actor == null)
                return;
            double current = _builds.TryGetValue(actor, out BuildState state) ? state.Progress : 0;
            SetProgress(variables, actor, current + delta);
        }

        /// <summary>
        /// Stops construction on <paramref name="actor"/> and frees its grid footprint.
        /// Returns true when it was actually under construction. This does not raise
        /// <c>BuildingDestroyed</c>; the bridge removes the placed actor silently, so
        /// "cancelled" and "destroyed" stay distinct events.
        /// </summary>
        public bool Cancel(object actor)
        {
            return Cancel(null, actor);
        }

        /// <summary>As <see cref="Cancel(object)"/>, also removing the mirrored
        /// <c>BuildProgress</c> local when <paramref name="variables"/> is supplied.</summary>
        public bool Cancel(EventVariables variables, object actor)
        {
            if (actor == null || !_builds.Remove(actor))
                return false;
            variables?.RemoveLocal(actor, "BuildProgress");
            ReleaseCells(actor);
            return true;
        }

        /// <summary>Clears construction state and grid cells for a destroyed actor.</summary>
        public void Destroyed(object actor)
        {
            Destroyed(null, actor);
        }

        /// <summary>As <see cref="Destroyed(object)"/>, also removing the mirrored
        /// <c>BuildProgress</c> local when <paramref name="variables"/> is supplied.</summary>
        public void Destroyed(EventVariables variables, object actor)
        {
            if (actor == null)
                return;
            _builds.Remove(actor);
            variables?.RemoveLocal(actor, "BuildProgress");
            ReleaseCells(actor);
        }

        /// <summary>Clears all construction state for a scene restart / new session.</summary>
        public void Reset()
        {
            _builds.Clear();
            _cells.Clear();
        }

        /// <summary>Centre of the cell containing <paramref name="value"/> (no snap when size ≤ 0).</summary>
        public static double SnapToGrid(double value, double size)
        {
            if (size <= 0)
                return value;
            return (Math.Floor(value / size) + 0.5) * size;
        }

        /// <summary>
        /// The cell size for an <c>on grid</c> placement: an explicit <c>by &lt;n&gt;</c> wins,
        /// then the <see cref="GridSizeVariable"/> global, then <see cref="DefaultGridSize"/>.
        /// A missing/invalid global never silently disables snapping.
        /// </summary>
        public static double ResolveGridSize(EventVariables variables, bool hasGridSize, double gridSize)
        {
            if (hasGridSize && gridSize > 0)
                return gridSize;
            if (variables != null && variables.HasGlobal(GridSizeVariable))
            {
                double global = variables.GetGlobal(GridSizeVariable).AsNumber();
                if (global > 0)
                    return global;
            }
            return DefaultGridSize;
        }

        /// <summary>The cell coordinate that contains a world position (no snap when size ≤ 0).</summary>
        public static (int X, int Z) CellOf(double x, double z, double size)
        {
            if (size <= 0)
                return (0, 0);
            return ((int)Math.Floor(x / size), (int)Math.Floor(z / size));
        }

        /// <summary>
        /// Reserves the building's footprint, anchored at the cell containing
        /// (<paramref name="x"/>, <paramref name="z"/>). Reads <c>&lt;Building&gt;GridWidth</c>
        /// and <c>&lt;Building&gt;GridDepth</c> (default 1, minimum 1). Returns false without
        /// reserving anything when any of those cells is held by another actor.
        /// </summary>
        public bool TryReserveCells(object actor, string building, EventVariables variables,
                                    double x, double z, double size)
        {
            if (actor == null || size <= 0)
                return false;

            int width = Footprint(variables, building + "GridWidth");
            int depth = Footprint(variables, building + "GridDepth");
            var (anchorX, anchorZ) = CellOf(x, z, size);

            if (!CanReserveCells(actor, building, variables, x, z, size))
                return false; // atomic: reserve nothing on failure

            for (int dx = 0; dx < width; dx++)
            {
                for (int dz = 0; dz < depth; dz++)
                    _cells[(anchorX + dx, anchorZ + dz)] = actor;
            }
            return true;
        }

        /// <summary>
        /// Whether <see cref="TryReserveCells"/> would succeed, without reserving anything — the
        /// dry run a landing needs before it decides to come down (Task 28 Part C). Cells the
        /// actor already holds count as free, so a building that kept its footprint while
        /// airborne (raised by <c>fly to … at …</c> rather than <c>lift off</c>) can still land
        /// on it.
        /// </summary>
        public bool CanReserveCells(object actor, string building, EventVariables variables,
                                    double x, double z, double size)
        {
            if (actor == null || size <= 0)
                return false;

            int width = Footprint(variables, building + "GridWidth");
            int depth = Footprint(variables, building + "GridDepth");
            var (anchorX, anchorZ) = CellOf(x, z, size);

            for (int dx = 0; dx < width; dx++)
            {
                for (int dz = 0; dz < depth; dz++)
                {
                    if (_cells.TryGetValue((anchorX + dx, anchorZ + dz), out object owner) &&
                        !ReferenceEquals(owner, actor))
                        return false;
                }
            }
            return true;
        }

        /// <summary>True when <paramref name="actor"/> currently holds grid cells.</summary>
        public bool OwnsCells(object actor)
        {
            if (actor == null)
                return false;
            foreach (var pair in _cells)
                if (ReferenceEquals(pair.Value, actor))
                    return true;
            return false;
        }

        /// <summary>Removes every cell owned by <paramref name="actor"/>.</summary>
        public void ReleaseCells(object actor)
        {
            if (actor == null)
                return;
            var owned = new List<(int X, int Z)>();
            foreach (var pair in _cells)
            {
                if (ReferenceEquals(pair.Value, actor))
                    owned.Add(pair.Key);
            }
            foreach (var cell in owned)
                _cells.Remove(cell);
        }

        /// <summary>
        /// Raises <c>ConstructionCompleted</c> once per building when progress reaches
        /// <see cref="CompleteAt"/>. Called at the top of <see cref="EventRuntime.Tick"/>.
        /// </summary>
        public void Tick(EventRuntime runtime)
        {
            if (runtime == null)
                return;
            List<object> completed = null;
            foreach (var pair in _builds)
            {
                BuildState state = pair.Value;
                if (state.CompletionRaised || state.Progress < CompleteAt)
                    continue;
                state.CompletionRaised = true;
                (completed ??= new List<object>()).Add(pair.Key);
            }
            if (completed == null)
                return;
            foreach (object actor in completed)
            {
                runtime.RaisePushAll(new PushSignal
                {
                    Kind = TriggerKind.ConstructionCompleted,
                    Name = _builds.TryGetValue(actor, out BuildState state) ? state.Building : "",
                    Other = actor,
                });
            }
        }

        private static void Mirror(EventVariables variables, object actor, double value)
        {
            variables?.SetLocal(actor, "BuildProgress", ValueBox.OfNumber(value));
        }

        private static int Footprint(EventVariables variables, string name)
        {
            if (variables == null || !variables.HasGlobal(name))
                return 1;
            double value = variables.GetGlobal(name).AsNumber();
            return value >= 1 ? (int)value : 1;
        }
    }
}
