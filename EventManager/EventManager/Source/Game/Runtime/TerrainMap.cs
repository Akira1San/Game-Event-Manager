using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// RTS terrain passability (Task 22 Part B): a sparse grid of cells whose ground is
    /// <c>land</c> (the default), <c>water</c>, or <c>air</c> — open space that only air units
    /// cross.
    ///
    /// Nothing is stored until a sheet marks a cell (<c>mark water around Mouse by 300</c>), so a
    /// scene that never marks terrain behaves exactly as it did before this existed. Movement asks
    /// <see cref="CanStep"/> with the moving unit's own type (<see cref="UnitTypes.TypeOf"/>):
    /// ground units are stopped by water, sea units by land, and air units by neither.
    ///
    /// Pure logic with no Flax types, so the tests drive it directly; the bridge supplies world
    /// coordinates and asks before each step.
    /// </summary>
    public class TerrainMap
    {
        /// <summary>World units per terrain cell. Matches the fog and construction grids.</summary>
        public const double DefaultCellSize = 64.0;

        /// <summary>Global that overrides the cell size, like <c>BuildGridSize</c>.</summary>
        public const string CellSizeVariable = "TerrainCellSize";

        /// <summary>Marked terrain; an unmarked cell is land.</summary>
        private readonly Dictionary<(int X, int Z), UnitTypes.Ground> _cells =
            new Dictionary<(int X, int Z), UnitTypes.Ground>();

        /// <summary>Turns terrain blocking off when set to false (a debug switch).</summary>
        public bool Enabled = true;

        public double CellSize = DefaultCellSize;

        /// <summary>Number of cells that are not plain land (water + air), for HUDs and tests.</summary>
        public int MarkedCells => _cells.Count;

        public void Reset()
        {
            _cells.Clear();
            CellSize = DefaultCellSize;
        }

        /// <summary>Reads the optional <c>TerrainCellSize</c> global before marking or asking.</summary>
        public void ResolveCellSize(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(CellSizeVariable))
                return;
            double size = variables.GetGlobal(CellSizeVariable).AsNumber();
            if (size > 0)
                CellSize = size;
        }

        public double Size => CellSize <= 0 ? DefaultCellSize : CellSize;

        public (int X, int Z) CellOf(double x, double z)
        {
            return ((int)Math.Floor(x / Size), (int)Math.Floor(z / Size));
        }

        /// <summary>What the ground at a world point is (land where nothing was marked).</summary>
        public UnitTypes.Ground KindAt(double x, double z)
        {
            return _cells.TryGetValue(CellOf(x, z), out UnitTypes.Ground ground)
                ? ground
                : UnitTypes.Ground.Land;
        }

        /// <summary>`mark water at Mouse` — one cell.</summary>
        public void Mark(double x, double z, UnitTypes.Ground ground)
        {
            (int cellX, int cellZ) = CellOf(x, z);
            if (ground == UnitTypes.Ground.Land)
                _cells.Remove((cellX, cellZ));
            else
                _cells[(cellX, cellZ)] = ground;
        }

        /// <summary>
        /// `mark water around Mouse by 300` — every cell whose centre is inside the circle. A
        /// radius of 0 or less marks just the cell under the point.
        /// </summary>
        public int MarkArea(double x, double z, double radius, UnitTypes.Ground ground)
        {
            var cells = CellsInCircle(x, z, radius);
            foreach (var cell in cells)
            {
                if (ground == UnitTypes.Ground.Land)
                    _cells.Remove(cell);
                else
                    _cells[cell] = ground;
            }
            return cells.Count;
        }

        /// <summary>True when a unit of this type may stand at the point.</summary>
        public bool IsPassable(string unitType, double x, double z)
        {
            if (!Enabled)
                return true;
            return UnitTypes.CanCross(unitType, KindAt(x, z));
        }

        /// <summary>True when this actor (by its own <c>my unit type</c>) may stand there.</summary>
        public bool IsPassableFor(object unit, EventVariables variables, double x, double z)
        {
            return IsPassable(UnitTypes.TypeOf(variables, unit), x, z);
        }

        /// <summary>
        /// Whether a unit may take one step. Entering impassable ground is refused — unless the
        /// unit is already standing on some (terrain marked under it), so it can always walk out
        /// instead of being stuck for good.
        /// </summary>
        public bool CanStep(string unitType, double fromX, double fromZ, double toX, double toZ)
        {
            if (!Enabled || IsPassable(unitType, toX, toZ))
                return true;
            return !IsPassable(unitType, fromX, fromZ);
        }

        private HashSet<(int X, int Z)> CellsInCircle(double x, double z, double radius)
        {
            var cells = new HashSet<(int X, int Z)>();
            if (radius <= 0)
            {
                cells.Add(CellOf(x, z));
                return cells;
            }
            double size = Size;
            int minX = (int)Math.Floor((x - radius) / size);
            int maxX = (int)Math.Floor((x + radius) / size);
            int minZ = (int)Math.Floor((z - radius) / size);
            int maxZ = (int)Math.Floor((z + radius) / size);
            double radiusSquared = radius * radius;
            for (int cx = minX; cx <= maxX; cx++)
            {
                double centerX = (cx + 0.5) * size;
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    double centerZ = (cz + 0.5) * size;
                    double dx = centerX - x;
                    double dz = centerZ - z;
                    if (dx * dx + dz * dz <= radiusSquared)
                        cells.Add((cx, cz));
                }
            }
            return cells;
        }
    }
}
