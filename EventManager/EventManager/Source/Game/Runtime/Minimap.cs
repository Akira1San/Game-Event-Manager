using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One sampled square of the minimap.</summary>
    public struct MinimapCell
    {
        /// <summary>What the fog shows there for the local player.</summary>
        public FogState Fog;
        /// <summary>True when the ground is water (land units may not cross it).</summary>
        public bool Water;
    }

    /// <summary>One unit dot: where it stands and who owns it.</summary>
    public struct MinimapUnit
    {
        public double X;
        public double Z;
        /// <summary>Player number (0 = local, 1 = enemy), as <c>my Player</c> declares it.</summary>
        public int Player;
        public bool Selected;

        public MinimapUnit(double x, double z, int player, bool selected)
        {
            X = x;
            Z = z;
            Player = player;
            Selected = selected;
        }
    }

    /// <summary>Everything one minimap frame needs to draw, in plain data.</summary>
    public class MinimapSnapshot
    {
        public int Columns;
        public int Rows;
        /// <summary>World point at the middle of the panel.</summary>
        public double CenterX;
        public double CenterZ;
        /// <summary>World half-size the panel covers.</summary>
        public double Extent;
        /// <summary>Panel side in the units the view rect and dots are expressed in.</summary>
        public double PanelSize;
        /// <summary>Row-major, top row first.</summary>
        public MinimapCell[] Cells = new MinimapCell[0];
        public readonly List<MinimapUnit> Units = new List<MinimapUnit>();
        /// <summary>The main camera's footprint on the panel, in panel units.</summary>
        public double ViewX;
        public double ViewY;
        public double ViewWidth;
        public double ViewHeight;

        public MinimapCell CellAt(int column, int row)
        {
            if (column < 0 || row < 0 || column >= Columns || row >= Rows)
                return new MinimapCell();
            return Cells[row * Columns + column];
        }
    }

    /// <summary>
    /// RTS minimap model (Task 24): the panel/world mapping, the per-frame snapshot of terrain,
    /// fog and units, and the drag rectangle. No Flax types live here — the bridge draws the
    /// snapshot in a UI control and feeds mouse input back in, so all the maths is unit tested.
    ///
    /// The map is drawn from data rather than from a second camera and render target: the terrain
    /// grid and the fog grid already know exactly what a player may see, so sampling them keeps
    /// the minimap honest (unexplored stays hidden) without another camera, texture or material.
    ///
    /// Globals: <c>Minimap</c> (0 turns the panel off), <c>MinimapExtent</c> (world half-size the
    /// panel covers, default 2000), <c>MinimapSize</c> (panel side in pixels, default 220) and
    /// <c>MinimapGrid</c> (cells per side, default 32).
    /// </summary>
    public static class Minimap
    {
        public const string EnabledVariable = "Minimap";
        public const string ExtentVariable = "MinimapExtent";
        public const string SizeVariable = "MinimapSize";
        public const string GridVariable = "MinimapGrid";

        public const double DefaultExtent = 2000;
        public const double DefaultSize = 220;
        public const int DefaultGrid = 32;
        public const int MinGrid = 4;
        public const int MaxGrid = 96;

        /// <summary>True unless a sheet turned the minimap off.</summary>
        public static bool Enabled(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(EnabledVariable))
                return true;
            return variables.GetGlobal(EnabledVariable).AsNumber() != 0;
        }

        /// <summary>Half the world width the panel covers.</summary>
        public static double ExtentFrom(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(ExtentVariable))
                return DefaultExtent;
            double extent = variables.GetGlobal(ExtentVariable).AsNumber();
            return extent > 0 ? extent : DefaultExtent;
        }

        /// <summary>The panel's side, in pixels.</summary>
        public static double SizeFrom(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(SizeVariable))
                return DefaultSize;
            double size = variables.GetGlobal(SizeVariable).AsNumber();
            return size > 0 ? size : DefaultSize;
        }

        /// <summary>How many cells per side the panel samples.</summary>
        public static int GridFrom(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(GridVariable))
                return DefaultGrid;
            double grid = Math.Round(variables.GetGlobal(GridVariable).AsNumber());
            if (grid < MinGrid)
                return MinGrid;
            if (grid > MaxGrid)
                return MaxGrid;
            return (int)grid;
        }

        /// <summary>World point → panel pixel. Panel Y grows downward, world +Z runs up it.</summary>
        public static void WorldToPanel(double worldX, double worldZ, double centerX, double centerZ,
            double panelSize, double extent, out double panelX, out double panelY)
        {
            double scale = Scale(panelSize, extent);
            panelX = panelSize * 0.5 + (worldX - centerX) * scale;
            panelY = panelSize * 0.5 - (worldZ - centerZ) * scale;
        }

        /// <summary>Panel pixel → world point (the inverse of <see cref="WorldToPanel"/>).</summary>
        public static void PanelToWorld(double panelX, double panelY, double centerX, double centerZ,
            double panelSize, double extent, out double worldX, out double worldZ)
        {
            double scale = Scale(panelSize, extent);
            worldX = centerX + (panelX - panelSize * 0.5) / scale;
            worldZ = centerZ - (panelY - panelSize * 0.5) / scale;
        }

        /// <summary>True when a panel point is inside the panel's square.</summary>
        public static bool InPanel(double panelX, double panelY, double panelSize)
            => panelX >= 0 && panelY >= 0 && panelX <= panelSize && panelY <= panelSize;

        /// <summary>
        /// The world rectangle a drag on the panel covers. Either corner may come first, and a
        /// drag that wandered outside the panel is clipped back to what the panel shows.
        /// </summary>
        public static void DragRect(double fromX, double fromY, double toX, double toY,
            double centerX, double centerZ, double panelSize, double extent,
            out double minX, out double minZ, out double maxX, out double maxZ)
        {
            PanelToWorld(ClampPanel(fromX, panelSize), ClampPanel(fromY, panelSize),
                centerX, centerZ, panelSize, extent, out double ax, out double az);
            PanelToWorld(ClampPanel(toX, panelSize), ClampPanel(toY, panelSize),
                centerX, centerZ, panelSize, extent, out double bx, out double bz);
            minX = Math.Min(ax, bx);
            maxX = Math.Max(ax, bx);
            minZ = Math.Min(az, bz);
            maxZ = Math.Max(az, bz);
        }

        /// <summary>True when a world point is inside a world rectangle.</summary>
        public static bool InRect(double minX, double minZ, double maxX, double maxZ,
            double x, double z)
            => x >= minX && x <= maxX && z >= minZ && z <= maxZ;

        /// <summary>
        /// One minimap frame: terrain and fog sampled per cell, the units still visible to the
        /// local player, and where the main camera is looking.
        /// </summary>
        public static MinimapSnapshot Build(EventVariables variables, TerrainMap terrain,
            FogOfWarManager fog, int player, double focusX, double focusZ,
            double viewWorldWidth, double viewWorldDepth, IReadOnlyList<MinimapUnit> units)
        {
            double extent = ExtentFrom(variables);
            double size = SizeFrom(variables);
            int grid = GridFrom(variables);
            var snapshot = new MinimapSnapshot
            {
                Columns = grid,
                Rows = grid,
                CenterX = focusX,
                CenterZ = focusZ,
                Extent = extent,
                PanelSize = size,
                Cells = new MinimapCell[grid * grid],
            };

            double cellPixels = size / grid;
            for (int row = 0; row < grid; row++)
            {
                for (int column = 0; column < grid; column++)
                {
                    PanelToWorld((column + 0.5) * cellPixels, (row + 0.5) * cellPixels,
                        focusX, focusZ, size, extent, out double x, out double z);
                    snapshot.Cells[row * grid + column] = new MinimapCell
                    {
                        Fog = fog == null ? FogState.Visible : fog.StateAt(player, x, z),
                        Water = terrain != null && terrain.KindAt(x, z) != UnitTypes.Ground.Land,
                    };
                }
            }

            if (units != null)
            {
                foreach (MinimapUnit unit in units)
                {
                    if (Math.Abs(unit.X - focusX) > extent || Math.Abs(unit.Z - focusZ) > extent)
                        continue;
                    snapshot.Units.Add(unit);
                }
            }

            double scale = Scale(size, extent);
            snapshot.ViewWidth = Math.Max(0, viewWorldWidth) * scale;
            snapshot.ViewHeight = Math.Max(0, viewWorldDepth) * scale;
            snapshot.ViewX = size * 0.5 - snapshot.ViewWidth * 0.5;
            snapshot.ViewY = size * 0.5 - snapshot.ViewHeight * 0.5;
            return snapshot;
        }

        private static double Scale(double panelSize, double extent)
        {
            double span = extent > 0 ? extent * 2 : DefaultExtent * 2;
            return (panelSize > 0 ? panelSize : DefaultSize) / span;
        }

        private static double ClampPanel(double value, double panelSize)
        {
            if (value < 0)
                return 0;
            if (value > panelSize)
                return panelSize;
            return value;
        }
    }
}
