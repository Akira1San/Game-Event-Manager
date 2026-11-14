using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>What one fog-of-war cell currently shows to a player.</summary>
    public enum FogState
    {
        /// <summary>Never seen — solid fog.</summary>
        Unexplored = 0,
        /// <summary>Seen before but not in active vision — remembered terrain, no enemy units.</summary>
        Explored = 1,
        /// <summary>Inside a live vision source right now.</summary>
        Visible = 2,
    }

    /// <summary>Where one actor is and how much fog it clears.</summary>
    public sealed class FogActor
    {
        public int Player;
        public double X;
        public double Z;
        /// <summary>Circle radius revealed around the actor. Zero reveals nothing.</summary>
        public double Radius;
        /// <summary>Half-width of an extra rectangle revealed around the actor (buildings).</summary>
        public double FootprintX;
        /// <summary>Half-depth of an extra rectangle revealed around the actor (buildings).</summary>
        public double FootprintZ;
    }

    /// <summary>
    /// RTS fog of war and visibility (Task 18 Part F): a sparse per-player grid with three
    /// states (unexplored / explored / visible), vision circles for units, footprint
    /// rectangles for buildings, and the reveal/hide/enter-fog edges. Pure logic with no
    /// Flax types, so the event runtime and the tests drive it directly; the bridge feeds
    /// actor positions and hides unseen models.
    /// </summary>
    public class FogOfWarManager
    {
        /// <summary>World units per fog cell. Matches the construction grid default.</summary>
        public const double DefaultCellSize = 64.0;

        /// <summary>Global variable that turns fog off when set to 0.</summary>
        public const string EnabledVariable = "FogOfWar";
        /// <summary>Per-actor variable holding the player number (0 = local, 1 = enemy).</summary>
        public const string PlayerVariable = "Player";
        /// <summary>Per-actor variable holding the vision radius.</summary>
        public const string VisionRadiusVariable = "VisionRadius";
        /// <summary>Per-actor variable holding a building footprint half-size.</summary>
        public const string FootprintVariable = "Footprint";

        public bool Enabled = true;
        public double CellSize = DefaultCellSize;
        /// <summary>The player whose view the bridge renders (0 = local).</summary>
        public int LocalPlayer = 0;

        private readonly Dictionary<object, FogActor> _actors = new Dictionary<object, FogActor>();
        private readonly Dictionary<int, Dictionary<(int X, int Z), FogState>> _states =
            new Dictionary<int, Dictionary<(int X, int Z), FogState>>();
        /// <summary>Cells that were visible at the end of the previous tick.</summary>
        private readonly Dictionary<int, HashSet<(int X, int Z)>> _wasVisible =
            new Dictionary<int, HashSet<(int X, int Z)>>();
        /// <summary>Cells explicitly revealed by `reveal area around`.</summary>
        private readonly Dictionary<int, HashSet<(int X, int Z)>> _manual =
            new Dictionary<int, HashSet<(int X, int Z)>>();
        /// <summary>Cells explicitly hidden by `hide area around`; they stay unexplored.</summary>
        private readonly Dictionary<int, HashSet<(int X, int Z)>> _hidden =
            new Dictionary<int, HashSet<(int X, int Z)>>();
        private readonly HashSet<int> _players = new HashSet<int>();
        /// <summary>Was each actor visible to the local player at the previous tick?</summary>
        private readonly Dictionary<object, bool> _wasVisibleToLocal = new Dictionary<object, bool>();

        /// <summary>Registers or updates one actor's position and vision source.</summary>
        public void SetActor(object actor, int player, double x, double z, double radius,
            double footprintX = 0, double footprintZ = 0)
        {
            if (actor == null)
                return;
            if (!_actors.TryGetValue(actor, out FogActor info))
            {
                info = new FogActor();
                _actors[actor] = info;
            }
            info.Player = player;
            info.X = x;
            info.Z = z;
            info.Radius = Math.Max(0, radius);
            info.FootprintX = Math.Max(0, footprintX);
            info.FootprintZ = Math.Max(0, footprintZ);
            EnsurePlayer(player);
        }

        /// <summary>Drops one actor from the visibility map.</summary>
        public void RemoveActor(object actor)
        {
            if (actor == null)
                return;
            _actors.Remove(actor);
            _wasVisibleToLocal.Remove(actor);
        }

        /// <summary>Drops actors that are no longer alive (the bridge passes the live set).</summary>
        public void Prune(HashSet<object> alive)
        {
            if (alive == null)
                return;
            var stale = new List<object>();
            foreach (var pair in _actors)
                if (!alive.Contains(pair.Key))
                    stale.Add(pair.Key);
            foreach (object actor in stale)
                RemoveActor(actor);
        }

        /// <summary>Clears every grid, actor and edge for a scene restart / new session.</summary>
        public void Reset()
        {
            _actors.Clear();
            _states.Clear();
            _wasVisible.Clear();
            _manual.Clear();
            _hidden.Clear();
            _players.Clear();
            _wasVisibleToLocal.Clear();
        }

        public bool IsKnown(object actor)
        {
            return actor != null && _actors.ContainsKey(actor);
        }

        public bool TryGetActor(object actor, out FogActor info)
        {
            if (actor == null)
            {
                info = null;
                return false;
            }
            return _actors.TryGetValue(actor, out info);
        }

        public int PlayerOf(object actor)
        {
            return TryGetActor(actor, out FogActor info) ? info.Player : 0;
        }

        public double VisionRadiusOf(object actor)
        {
            return TryGetActor(actor, out FogActor info) ? info.Radius : 0;
        }

        /// <summary>`set visibility to &lt;radius&gt;` updates the live source immediately;
        /// the next tick reveals the new area.</summary>
        public void SetVisionRadius(object actor, double radius)
        {
            if (TryGetActor(actor, out FogActor info))
                info.Radius = Math.Max(0, radius);
        }

        /// <summary>The fog state one world point shows to <paramref name="player"/>.</summary>
        public FogState StateAt(int player, double x, double z)
        {
            if (!_states.TryGetValue(player, out var cells))
                return FogState.Unexplored;
            return cells.TryGetValue(CellOf(x, z), out FogState state) ? state : FogState.Unexplored;
        }

        public bool IsExplored(int player, double x, double z)
        {
            return StateAt(player, x, z) != FogState.Unexplored;
        }

        /// <summary>
        /// True when <paramref name="player"/> can see <paramref name="actor"/>. A player
        /// always sees its own units; an enemy is visible only while its centre cell is in
        /// active vision.
        /// </summary>
        public bool IsVisibleTo(int player, object actor)
        {
            if (!TryGetActor(actor, out FogActor info))
                return false;
            if (info.Player == player)
                return true;
            return StateAt(player, info.X, info.Z) == FogState.Visible;
        }

        /// <summary>True when any other player can see the actor.</summary>
        public bool IsVisibleToEnemy(object actor)
        {
            if (!TryGetActor(actor, out FogActor info))
                return false;
            foreach (int player in _players)
                if (player != info.Player && IsVisibleTo(player, actor))
                    return true;
            return false;
        }

        /// <summary>True when no other player can see the actor — the `me is hidden` condition.</summary>
        public bool IsHidden(object actor)
        {
            return !IsVisibleToEnemy(actor);
        }

        /// <summary>
        /// Explicitly reveals a circle for one player. The cells stay visible until
        /// `hide area around` or a reset, and are also remembered as explored.
        /// </summary>
        public bool RevealArea(int player, double x, double z, double radius)
        {
            if (!Enabled)
                return false;
            EnsurePlayer(player);
            bool changed = false;
            foreach (var cell in CellsInCircle(x, z, radius))
            {
                if (_hidden[player].Remove(cell))
                    changed = true;
                _manual[player].Add(cell);
                if (!_states[player].TryGetValue(cell, out FogState state) || state != FogState.Visible)
                {
                    _states[player][cell] = FogState.Visible;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Explicitly hides a circle for one player: the cells become unexplored again, even
        /// if they had been explored before.
        /// </summary>
        public bool HideArea(int player, double x, double z, double radius)
        {
            EnsurePlayer(player);
            bool changed = false;
            foreach (var cell in CellsInCircle(x, z, radius))
            {
                _manual[player].Remove(cell);
                _hidden[player].Add(cell);
                if (!_states[player].TryGetValue(cell, out FogState state) || state != FogState.Unexplored)
                {
                    _states[player][cell] = FogState.Unexplored;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary><c>reveal area around &lt;actor&gt; by &lt;radius&gt;</c>.</summary>
        public bool RevealAround(object actor, double radius)
        {
            return TryGetActor(actor, out FogActor info) && RevealArea(info.Player, info.X, info.Z, radius);
        }

        /// <summary><c>hide area around &lt;actor&gt; by &lt;radius&gt;</c>.</summary>
        public bool HideAround(object actor, double radius)
        {
            return TryGetActor(actor, out FogActor info) && HideArea(info.Player, info.X, info.Z, radius);
        }

        /// <summary>
        /// Recomputes each player's visibility grid, remembers newly explored cells and raises
        /// <c>AreaRevealed</c> / <c>AreaHidden</c> / <c>EntersFog</c> before the runtime drains
        /// its push queues.
        /// </summary>
        public void Tick(EventRuntime runtime)
        {
            if (!Enabled)
                return;

            var players = new List<int>(_players);
            foreach (int player in players)
            {
                EnsurePlayer(player);
                var current = new HashSet<(int X, int Z)>();
                foreach (var pair in _actors)
                {
                    FogActor info = pair.Value;
                    if (info.Player != player)
                        continue;
                    if (info.Radius > 0)
                        AddCircle(current, info.X, info.Z, info.Radius);
                    if (info.FootprintX > 0 || info.FootprintZ > 0)
                        AddRectangle(current, info.X, info.Z, info.FootprintX, info.FootprintZ);
                }
                foreach (var cell in _manual[player])
                    current.Add(cell);
                // Live vision always wins over an earlier `hide area`.
                foreach (var cell in current)
                    _hidden[player].Remove(cell);

                var was = _wasVisible[player];
                bool revealed = false;
                bool hidden = false;
                foreach (var cell in current)
                {
                    if (!was.Contains(cell))
                        revealed = true;
                    _states[player][cell] = FogState.Visible;
                }
                foreach (var cell in was)
                {
                    if (current.Contains(cell))
                        continue;
                    hidden = true;
                    _states[player][cell] = _hidden[player].Contains(cell)
                        ? FogState.Unexplored
                        : FogState.Explored;
                }
                foreach (var cell in _hidden[player])
                    if (!current.Contains(cell))
                        _states[player][cell] = FogState.Unexplored;

                _wasVisible[player] = current;
                if (revealed)
                    runtime?.RaisePushAll(new PushSignal
                    {
                        Kind = TriggerKind.AreaRevealed,
                        PlayerA = player,
                        Name = player.ToString(),
                    });
                if (hidden)
                    runtime?.RaisePushAll(new PushSignal
                    {
                        Kind = TriggerKind.AreaHidden,
                        PlayerA = player,
                        Name = player.ToString(),
                    });
            }

            // `when <unit> enters fog` is about the local player losing sight of a unit.
            foreach (var pair in _actors)
            {
                bool visible = IsVisibleTo(LocalPlayer, pair.Key);
                if (_wasVisibleToLocal.TryGetValue(pair.Key, out bool wasVisible) && wasVisible && !visible)
                {
                    runtime?.RaisePushAll(new PushSignal
                    {
                        Kind = TriggerKind.EntersFog,
                        Name = runtime.NameOfActor(pair.Key),
                        Other = pair.Key,
                        PlayerA = LocalPlayer,
                    });
                }
                _wasVisibleToLocal[pair.Key] = visible;
            }
        }

        private void EnsurePlayer(int player)
        {
            _players.Add(player);
            if (!_states.ContainsKey(player))
                _states[player] = new Dictionary<(int X, int Z), FogState>();
            if (!_wasVisible.ContainsKey(player))
                _wasVisible[player] = new HashSet<(int X, int Z)>();
            if (!_manual.ContainsKey(player))
                _manual[player] = new HashSet<(int X, int Z)>();
            if (!_hidden.ContainsKey(player))
                _hidden[player] = new HashSet<(int X, int Z)>();
        }

        private double Size => CellSize <= 0 ? DefaultCellSize : CellSize;

        private (int X, int Z) CellOf(double x, double z)
        {
            return ((int)Math.Floor(x / Size), (int)Math.Floor(z / Size));
        }

        private HashSet<(int X, int Z)> CellsInCircle(double x, double z, double radius)
        {
            var cells = new HashSet<(int X, int Z)>();
            AddCircle(cells, x, z, radius);
            return cells;
        }

        private void AddCircle(HashSet<(int X, int Z)> cells, double x, double z, double radius)
        {
            if (radius <= 0)
            {
                cells.Add(CellOf(x, z));
                return;
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
        }

        private void AddRectangle(HashSet<(int X, int Z)> cells, double x, double z,
            double halfWidth, double halfDepth)
        {
            double size = Size;
            int minX = (int)Math.Floor((x - halfWidth) / size);
            int maxX = (int)Math.Floor((x + halfWidth) / size);
            int minZ = (int)Math.Floor((z - halfDepth) / size);
            int maxZ = (int)Math.Floor((z + halfDepth) / size);
            for (int cx = minX; cx <= maxX; cx++)
                for (int cz = minZ; cz <= maxZ; cz++)
                    cells.Add((cx, cz));
        }
    }
}
