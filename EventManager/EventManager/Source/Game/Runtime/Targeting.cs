using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// What a target-pick mode accepts (Task 27 Part A): a ground point, a unit, the caster
    /// itself, or any combination. Written as qualifiers on <c>aim</c> — <c>at a point</c>,
    /// <c>at a unit</c>, <c>at me</c> — and defaulting to a point or a unit, which is what a
    /// clicked ability usually wants.
    /// </summary>
    [Flags]
    public enum TargetAccepts
    {
        None = 0,
        Point = 1,
        Unit = 2,
        Me = 4,
        Any = Point | Unit | Me,
    }

    /// <summary>
    /// One caster waiting for a click. Pure state: who is aiming, which ability, what it
    /// accepts, how far, how long it stays armed, and the pick once it lands. No Flax types,
    /// so the runtime and the tests drive it directly; the bridge turns a mouse click into
    /// <see cref="TargetingManager.TryPick(object, object, double, double)"/> and reads the
    /// pick back through <c>my aim x</c> / <c>my aim z</c> / <c>my aim target</c>.
    /// </summary>
    public sealed class TargetingSession
    {
        public object Caster;
        /// <summary>The ability name as written in quotes (<c>aim "psi storm"</c>).</summary>
        public string Ability = "";
        public TargetAccepts Accepts = TargetAccepts.Point | TargetAccepts.Unit;
        /// <summary>Reach in world units, measured from the caster on the XZ plane. 0 = anywhere.</summary>
        public double Range;
        public double Timeout = TargetingManager.DefaultTimeout;
        /// <summary>Seconds left before the mode lapses.</summary>
        public double Remaining;

        public double CasterX;
        public double CasterZ;

        /// <summary>
        /// Extra per-unit rule the bridge supplies (matchup table, fog visibility, …).
        /// Null accepts every actor that is in reach and of an accepted kind, which is what
        /// the pure tests use.
        /// </summary>
        public Func<object, bool> UnitValid;

        /// <summary>True once a click landed. The pick stays readable after that.</summary>
        public bool HasPick;
        public bool PickIsUnit;
        public object PickActor;
        public double PickX;
        public double PickZ;

        /// <summary>A landed pick ends the interactive mode but keeps the pick readable.</summary>
        public bool Finished;

        public bool AcceptsPoint => (Accepts & TargetAccepts.Point) != 0;

        public bool AcceptsUnits => (Accepts & (TargetAccepts.Unit | TargetAccepts.Me)) != 0;

        /// <summary>True when a world point is inside the caster's reach.</summary>
        public bool InRange(double x, double z)
        {
            if (Range <= 0)
                return true;
            double dx = x - CasterX;
            double dz = z - CasterZ;
            return Math.Sqrt(dx * dx + dz * dz) <= Range;
        }

        public void ClearPick()
        {
            HasPick = false;
            PickIsUnit = false;
            PickActor = null;
            PickX = 0;
            PickZ = 0;
            Finished = false;
        }

        /// <summary>One line for warnings and HUDs: ability, kind and reach.</summary>
        public string Describe()
        {
            string kind = Accepts == TargetAccepts.Point ? "a point"
                : Accepts == TargetAccepts.Unit ? "a unit"
                : Accepts == TargetAccepts.Me ? "me"
                : "a point or a unit";
            string range = Range > 0 ? $" within {Range:0.##}" : "";
            return $"\"{Ability}\" ({kind}{range})";
        }
    }

    /// <summary>
    /// RTS target-pick mode (Task 27 Part A): <c>aim "psi storm"</c> arms it, a left click
    /// resolves it, <c>cancel aim</c> / a right click / Escape drop it, and a timeout lapses it.
    /// One session at a time — the newest <c>aim</c> replaces whatever was armed, which is what
    /// a command card does when the player presses another ability button.
    /// </summary>
    public class TargetingManager
    {
        /// <summary>How long an aim stays armed without a click, in seconds.</summary>
        public const double DefaultTimeout = 8.0;

        private readonly Dictionary<object, TargetingSession> _sessions =
            new Dictionary<object, TargetingSession>();

        /// <summary>The most recent session, picked or not. Null when nothing is armed.</summary>
        public TargetingSession Current { get; private set; }

        /// <summary>The session still waiting for a click, or null.</summary>
        public TargetingSession Aiming => Current != null && !Current.Finished ? Current : null;

        public bool IsAiming => Aiming != null;

        /// <summary>
        /// Arms the mode for <paramref name="caster"/>. Any earlier aim — this caster's or
        /// another's — is replaced, so a second ability button never leaves two pickers live.
        /// </summary>
        public TargetingSession Begin(object caster, string ability, TargetAccepts accepts,
            double range, double timeout, double casterX, double casterZ,
            Func<object, bool> unitValid = null)
        {
            Clear();
            var session = new TargetingSession
            {
                Caster = caster,
                Ability = ability ?? "",
                Accepts = accepts == TargetAccepts.None
                    ? TargetAccepts.Point | TargetAccepts.Unit
                    : accepts,
                Range = range > 0 ? range : 0,
                Timeout = timeout > 0 ? timeout : DefaultTimeout,
                CasterX = casterX,
                CasterZ = casterZ,
                UnitValid = unitValid,
            };
            session.Remaining = session.Timeout;
            if (caster != null)
                _sessions[caster] = session;
            Current = session;
            return session;
        }

        /// <summary>Keeps the reach honest while the caster walks (or is pushed) around.</summary>
        public void MoveCaster(object caster, double x, double z)
        {
            TargetingSession session = SessionOf(caster);
            if (session == null)
                return;
            session.CasterX = x;
            session.CasterZ = z;
        }

        public TargetingSession SessionOf(object caster)
        {
            if (caster == null)
                return null;
            return _sessions.TryGetValue(caster, out TargetingSession session) ? session : null;
        }

        public bool IsAimingFor(object caster)
        {
            TargetingSession session = SessionOf(caster);
            return session != null && !session.Finished;
        }

        /// <summary>
        /// A click on a unit. Accepts it only when a unit is wanted (or it is the caster and
        /// <c>me</c> is), it is in reach, and the bridge's rule agrees. On success the pick is
        /// recorded and the mode stops waiting; the caller raises
        /// <c>when my aim picks a target</c>.
        /// </summary>
        public TargetingSession TryPick(object caster, object actor, double x, double z)
        {
            TargetingSession session = SessionOf(caster);
            if (session == null || session.Finished || actor == null)
                return null;
            bool self = ReferenceEquals(actor, caster);
            if (self ? (session.Accepts & TargetAccepts.Me) == 0 : (session.Accepts & TargetAccepts.Unit) == 0)
                return null;
            if (!session.InRange(x, z))
                return null;
            if (session.UnitValid != null && !session.UnitValid(actor))
                return null;

            session.HasPick = true;
            session.PickIsUnit = true;
            session.PickActor = actor;
            session.PickX = x;
            session.PickZ = z;
            session.Finished = true;
            return session;
        }

        /// <summary>A click on bare ground: needs <see cref="TargetAccepts.Point"/> and reach.</summary>
        public TargetingSession TryPick(object caster, double x, double z)
        {
            TargetingSession session = SessionOf(caster);
            if (session == null || session.Finished || !session.AcceptsPoint)
                return null;
            if (!session.InRange(x, z))
                return null;

            session.HasPick = true;
            session.PickIsUnit = false;
            session.PickActor = null;
            session.PickX = x;
            session.PickZ = z;
            session.Finished = true;
            return session;
        }

        /// <summary><c>cancel aim</c>, a right click or Escape. Keeps nothing readable.</summary>
        public bool Cancel(object caster)
        {
            TargetingSession session = SessionOf(caster);
            if (session == null)
                return false;
            _sessions.Remove(caster);
            if (ReferenceEquals(Current, session))
                Current = null;
            return true;
        }

        /// <summary>Drops a destroyed caster's aim.</summary>
        public void Forget(object actor)
        {
            if (actor == null)
                return;
            if (_sessions.TryGetValue(actor, out TargetingSession session))
            {
                _sessions.Remove(actor);
                if (ReferenceEquals(Current, session))
                    Current = null;
            }
            if (Current != null && ReferenceEquals(Current.Caster, actor))
                Current = null;
        }

        /// <summary>Clears everything for a scene restart / new session.</summary>
        public void Clear()
        {
            _sessions.Clear();
            Current = null;
        }

        /// <summary>
        /// Advances every armed aim; returns the casters whose mode just timed out (in
        /// dictionary order) so the caller can warn or react. A landed pick never times out —
        /// it keeps its result readable until the next <c>aim</c> or <c>cancel aim</c>.
        /// </summary>
        public List<object> Tick(double dt)
        {
            var expired = new List<object>();
            if (_sessions.Count == 0 || dt <= 0)
                return expired;

            var done = new List<object>();
            foreach (var pair in _sessions)
            {
                TargetingSession session = pair.Value;
                if (session.Finished)
                    continue;
                session.Remaining -= dt;
                if (session.Remaining > 0)
                    continue;
                done.Add(pair.Key);
            }
            foreach (object caster in done)
            {
                _sessions.Remove(caster);
                expired.Add(caster);
            }
            if (Current != null && Current.Remaining <= 0 && !Current.Finished &&
                !_sessions.ContainsKey(Current.Caster))
                Current = null;
            return expired;
        }
    }
}
