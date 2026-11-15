using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>Which side of the bearing to the target the steering is committed to.
    /// Positive deviations turn toward the actor's right (+X when its forward is +Z).</summary>
    public enum SteerSide
    {
        None,
        Left,
        Right,
    }

    /// <summary>
    /// One actor's wall-negotiation memory: where it was last tick (for the stuck test), which
    /// side it committed to and for how much longer that commitment holds. Kept per actor so two
    /// chasers never share a decision.
    /// </summary>
    public class SteerState
    {
        public bool HasLastPosition;
        public double LastX;
        public double LastZ;
        /// <summary>How long the actor has been failing to move (a blocked step adds up).</summary>
        public double StillSeconds;
        /// <summary>Committed side and the seconds of hysteresis left on it.</summary>
        public SteerSide Side = SteerSide.None;
        public double SideSeconds;
        /// <summary>The deviation chosen last tick (degrees from the bearing to the target).</summary>
        public double DeviationDegrees;
        /// <summary>True while the last tick returned a steered heading.</summary>
        public bool Steering;
    }

    /// <summary>An input bundle for one steering tick. Numbers only, so the rule stays Flax-free.</summary>
    public class SteerQuery
    {
        /// <summary>Identity of the chasing actor (the key for its <see cref="SteerState"/>).</summary>
        public object Actor;
        public double DeltaTime;
        /// <summary>Current ground position (XZ).</summary>
        public double X;
        public double Z;
        /// <summary>The actor's current heading, flattened to XZ. Used only to break a
        /// left/right tie (the smaller turn wins) — never to steer by itself.</summary>
        public double ForwardX;
        public double ForwardZ = 1;
        /// <summary>Ground-plane bearing from the actor to its target (normalized XZ).</summary>
        public double BearingX = 0;
        public double BearingZ = 1;
        /// <summary>Result of the caller's straight probe toward the target (1 ray, already cast).</summary>
        public bool DirectClear;
        /// <summary>The surface normal of whatever blocked that probe, when there was a hit.</summary>
        public bool HasHitNormal;
        public double HitNormalX;
        public double HitNormalZ;
        /// <summary>Cast one fan ray at a signed deviation (degrees) and say whether it is clear.
        /// Null disables the fan (the caller has no world to cast into).</summary>
        public Func<double, bool> IsClear;
        /// <summary>Movement below this per tick counts as "did not move" (the caller scales it
        /// from speed × delta — a slow actor still moves less than a fast one).</summary>
        public double MinStep = 0.01;
        /// <summary>Ray budget for this tick, including the direct probe. Capped at
        /// <see cref="WallSteering.MaxRaysPerTick"/>.</summary>
        public int MaxRays = WallSteering.MaxRaysPerTick;
    }

    /// <summary>What one steering tick decided, plus the exact ray count it spent.</summary>
    public class SteerChoice
    {
        /// <summary>True when the caller should move along the steered heading instead of the
        /// straight bearing to the target.</summary>
        public bool Steer;
        /// <summary>Signed turn away from the bearing (negative = left, positive = right).</summary>
        public double DeviationDegrees;
        public SteerSide Side;
        /// <summary>The chosen heading as a normalized XZ vector.</summary>
        public double HeadingX;
        public double HeadingZ;
        /// <summary>True when the heading came from the wall tangent fallback rather than a
        /// clear fan ray.</summary>
        public bool Tangent;
        /// <summary>True when the stuck test forced this sampling cycle.</summary>
        public bool Stuck;
        /// <summary>Rays cast this tick: the direct probe (1) plus every fan ray
        /// (never more than <see cref="WallSteering.MaxRaysPerTick"/>).</summary>
        public int RaysCast;
    }

    /// <summary>
    /// Corner sampling and wall tangents behind <c>move toward &lt;thing&gt; ... around walls</c>
    /// (Task 7 Part G2). This is the C# counterpart to Part G1's events-only <c>hunt</c> arc: it
    /// lets a direct chase round an obstacle instead of walking into it, without a NavMesh and
    /// without replacing <c>goto</c> for long routes.
    ///
    /// Rules, all free of engine types so they are unit tested:
    /// - The caller casts one straight probe toward the target. When it is clear, no fan ray is
    ///   cast at all (unless the actor is stuck — see below).
    /// - When it is blocked, a fan at ±15°, ±30°, … up to ±90° is sampled in ascending deviation.
    ///   The first clear ray wins, so the path stays tight.
    /// - The whole tick spends at most <see cref="MaxRaysPerTick"/> rays (the probe included), so
    ///   only the innermost deviations fit: 15, 30 and 45 degrees on each side.
    /// - A chosen side sticks for <see cref="HysteresisSeconds"/>: while both sides are equally
    ///   clear, the actor keeps its side and cannot vibrate left/right.
    /// - A stuck actor (barely moved for <see cref="StuckSeconds"/> while still being told to
    ///   move) forces one sampling cycle even when the probe reads clear, which is what gets a
    ///   chase out of a shallow concave corner.
    /// - If no fan ray is clear, the tangent of the surface that blocked the probe is driven
    ///   instead (the fallback for a flat wall wider than the fan).
    ///
    /// Documented limit: a deep U-shaped pocket can be "clear" along a lateral ray that never
    /// leaves the pocket; the chase then stalls and Part G1's <c>hunt</c> / patrol takes over.
    /// That is a limit, not a bug.
    /// </summary>
    public class WallSteering
    {
        /// <summary>Spacing between fan rays, in degrees.</summary>
        public const double DeviationStepDegrees = 15;
        /// <summary>Widest deviation the fan will consider.</summary>
        public const double MaxDeviationDegrees = 90;
        /// <summary>Hard ray budget per blocked tick, including the caller's direct probe.</summary>
        public const int MaxRaysPerTick = 7;
        /// <summary>How far a fan ray looks ahead (engine units, i.e. cm).</summary>
        public const double Lookahead = 80;
        /// <summary>How far the straight probe toward the target looks (or the remaining
        /// distance when that is shorter).</summary>
        public const double Probe = 40;
        /// <summary>How long a chosen side is kept before the other side may win a tie.</summary>
        public const double HysteresisSeconds = 0.3;
        /// <summary>How long an actor must fail to move before the stuck fallback fires.</summary>
        public const double StuckSeconds = 0.4;

        private readonly Dictionary<object, SteerState> states = new Dictionary<object, SteerState>();

        /// <summary>One actor's live steering memory (created on first use).</summary>
        public SteerState StateOf(object actor)
        {
            if (actor == null)
                return null;
            if (!states.TryGetValue(actor, out SteerState state))
            {
                state = new SteerState();
                states[actor] = state;
            }
            return state;
        }

        /// <summary>True while this actor's last tick steered off the direct bearing.</summary>
        public bool IsSteering(object actor)
        {
            return actor != null && states.TryGetValue(actor, out SteerState state) && state.Steering;
        }

        /// <summary>Forgets one actor (a plain move, <c>stop moving</c>, or a destroyed actor).</summary>
        public void Clear(object actor)
        {
            if (actor != null)
                states.Remove(actor);
        }

        /// <summary>
        /// One tick of negotiation for one actor. The caller has already cast the direct probe
        /// (that is the "1" in <see cref="SteerChoice.RaysCast"/>); this only decides whether to
        /// keep the straight bearing, sample the fan, or drive a wall tangent.
        /// </summary>
        public SteerChoice Step(SteerQuery query)
        {
            var choice = new SteerChoice { RaysCast = 1 };
            if (query == null || query.Actor == null)
                return choice;
            SteerState state = StateOf(query.Actor);

            // ---- stuck bookkeeping: failing to move while told to move is its own signal ----
            if (state.HasLastPosition)
            {
                double dx = query.X - state.LastX;
                double dz = query.Z - state.LastZ;
                double moved = Math.Sqrt(dx * dx + dz * dz);
                if (moved < Math.Max(0.0, query.MinStep))
                    state.StillSeconds += Math.Max(0.0, query.DeltaTime);
                else
                    state.StillSeconds = 0;
            }
            else
            {
                state.HasLastPosition = true;
            }
            state.LastX = query.X;
            state.LastZ = query.Z;
            state.SideSeconds = Math.Max(0.0, state.SideSeconds - Math.Max(0.0, query.DeltaTime));

            bool stuck = state.StillSeconds >= StuckSeconds;
            choice.Stuck = stuck;

            // Nothing in the way: no fan rays, and the actor is free to chase straight again.
            if (query.DirectClear && !stuck)
            {
                state.Steering = false;
                state.Side = SteerSide.None;
                state.SideSeconds = 0;
                state.DeviationDegrees = 0;
                return choice;
            }

            // ---- corner sampling: the smallest clear deviation wins ----
            double bearingDegrees = Bearing(query.BearingX, query.BearingZ);
            SteerSide bias = state.SideSeconds > 0 ? state.Side : TieBreakSide(query);
            double chosen = 0;
            bool found = false;
            foreach (double angle in FanCandidates(bias, query.MaxRays))
            {
                choice.RaysCast++;
                if (query.IsClear != null && query.IsClear(angle))
                {
                    chosen = angle;
                    found = true;
                    break;
                }
            }
            if (found)
            {
                Commit(state, choice, bearingDegrees, chosen, false);
                return choice;
            }

            // ---- fallback: follow the wall tangent of the hit that blocked the probe ----
            if (query.HasHitNormal)
            {
                WallTangent(query.HitNormalX, query.HitNormalZ, query.BearingX, query.BearingZ,
                    out double tangentX, out double tangentZ);
                double deviation = DeviationDegrees(query.BearingX, query.BearingZ, tangentX, tangentZ);
                Commit(state, choice, bearingDegrees, deviation, true);
                return choice;
            }

            state.Steering = false;
            state.Side = SteerSide.None;
            state.DeviationDegrees = 0;
            return choice;
        }

        private static void Commit(
            SteerState state, SteerChoice choice, double bearingDegrees, double deviation, bool tangent)
        {
            Heading(bearingDegrees + deviation, out double headingX, out double headingZ);
            choice.Steer = true;
            choice.DeviationDegrees = deviation;
            choice.Tangent = tangent;
            choice.HeadingX = headingX;
            choice.HeadingZ = headingZ;
            choice.Side = deviation > 0.0001 ? SteerSide.Right
                : deviation < -0.0001 ? SteerSide.Left
                : SteerSide.None;

            state.Steering = true;
            state.DeviationDegrees = deviation;
            if (choice.Side != SteerSide.None)
            {
                state.Side = choice.Side;
                state.SideSeconds = HysteresisSeconds;
            }
        }

        /// <summary>
        /// The fan's candidate deviations in cast order, smallest first, both sides of each
        /// magnitude. At most <paramref name="maxRays"/> minus one entries, because the caller's
        /// direct probe is one of the rays. With the default budget that is 15, 30 and 45 degrees
        /// on each side — the far 60/75/90 rays only exist when a caller raises the budget.
        /// </summary>
        public static List<double> FanCandidates(SteerSide bias, int maxRays)
        {
            var angles = new List<double>();
            int budget = Math.Max(0, Math.Min(maxRays, MaxRaysPerTick) - 1);
            if (budget == 0)
                return angles;
            bool rightFirst = bias != SteerSide.Left;
            for (double magnitude = DeviationStepDegrees;
                 magnitude <= MaxDeviationDegrees + 1e-9 && angles.Count < budget;
                 magnitude += DeviationStepDegrees)
            {
                if (rightFirst)
                {
                    angles.Add(magnitude);
                    if (angles.Count < budget)
                        angles.Add(-magnitude);
                }
                else
                {
                    angles.Add(-magnitude);
                    if (angles.Count < budget)
                        angles.Add(magnitude);
                }
            }
            return angles;
        }

        /// <summary>Which side turns less: the actor's own heading is already angled that way.</summary>
        public static SteerSide TieBreakSide(SteerQuery query)
        {
            double forward = DeviationDegrees(query.BearingX, query.BearingZ, query.ForwardX, query.ForwardZ);
            return forward < 0 ? SteerSide.Left : SteerSide.Right;
        }

        /// <summary>
        /// The wall tangent at a surface: <c>cross(up, normal)</c>, flipped so it points more
        /// toward the heading. Returns the heading unchanged when the normal is degenerate
        /// (looking straight down/up, which a ground-plane chase never does).
        /// </summary>
        public static void WallTangent(
            double normalX, double normalZ,
            double headingX, double headingZ,
            out double tangentX, out double tangentZ)
        {
            // cross((0,1,0), normal) = (normal.Z, 0, -normal.X)
            double tx = normalZ;
            double tz = -normalX;
            double length = Math.Sqrt(tx * tx + tz * tz);
            if (length <= 1e-9)
            {
                tangentX = headingX;
                tangentZ = headingZ;
                return;
            }
            tx /= length;
            tz /= length;
            if (tx * headingX + tz * headingZ < 0)
            {
                tx = -tx;
                tz = -tz;
            }
            tangentX = tx;
            tangentZ = tz;
        }

        /// <summary>The ground bearing (0 = +Z, 90 = +X) as a normalized direction.</summary>
        public static void Heading(double degrees, out double x, out double z)
        {
            double radians = degrees * Math.PI / 180.0;
            x = Math.Sin(radians);
            z = Math.Cos(radians);
        }

        /// <summary>Bearing of a ground direction in degrees (0 = +Z, 90 = +X).</summary>
        public static double Bearing(double x, double z)
        {
            return Math.Atan2(x, z) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Signed turn from one ground direction to another, in degrees (-180..180).
        /// Positive means the second direction lies to the right of the first.
        /// </summary>
        public static double DeviationDegrees(double fromX, double fromZ, double toX, double toZ)
        {
            double cross = fromZ * toX - fromX * toZ;
            double dot = fromX * toX + fromZ * toZ;
            return Math.Atan2(cross, dot) * 180.0 / Math.PI;
        }
    }
}
