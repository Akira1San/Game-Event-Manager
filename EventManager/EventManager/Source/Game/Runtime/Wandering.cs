using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One straight leg of a wander: a heading, its speed and the ticks left on it.</summary>
    public class WanderLeg
    {
        public double AngleDegrees;
        public double Speed = Wanderer.DefaultSpeed;
        public double TicksRemaining;
    }

    /// <summary>
    /// Random-angle wander and still mode (Task 16 Part A).
    ///
    /// <c>wander speed &lt;n&gt;</c> picks a random heading (0-359°) and walks it straight for
    /// 20-60 ticks, then picks another — so a wandering actor never looks like it is following
    /// a path. <c>still</c> parks the actor: the bridge stops any move and turns it toward its
    /// target (nearest Player by default). Both modes are per actor and are cleared by a real
    /// move, so a sheet can switch between them freely.
    ///
    /// The leg bookkeeping lives here, free of engine types, so the timing is unit-testable;
    /// <see cref="FlaxActions"/> only turns the angle into world movement each tick.
    /// </summary>
    public class Wanderer
    {
        public const double DefaultSpeed = 5;
        public const double MinTicks = 20;
        public const double MaxTicks = 60;
        /// <summary>Ticks per second, used to turn a <c>duration &lt;seconds&gt;</c> qualifier into ticks.</summary>
        public const double TicksPerSecond = 60;

        private readonly Dictionary<object, WanderLeg> legs = new Dictionary<object, WanderLeg>();
        private readonly HashSet<object> still = new HashSet<object>();

        public bool IsWandering(object actor)
        {
            return actor != null && legs.ContainsKey(actor);
        }

        public bool IsStill(object actor)
        {
            return actor != null && still.Contains(actor);
        }

        public bool TryGetLeg(object actor, out WanderLeg leg)
        {
            leg = null;
            return actor != null && legs.TryGetValue(actor, out leg);
        }

        /// <summary>
        /// <c>wander</c>: start (or restart) a leg. A <paramref name="durationSeconds"/> of 0
        /// keeps the random 20-60 tick length; a speed of 0 keeps the default 5.
        /// </summary>
        public WanderLeg Start(object actor, double speed, double durationSeconds)
        {
            if (actor == null)
                return null;
            still.Remove(actor);
            var leg = new WanderLeg
            {
                AngleDegrees = RandomAngle(),
                Speed = speed > 0 ? speed : DefaultSpeed,
                TicksRemaining = TickCount(durationSeconds),
            };
            legs[actor] = leg;
            return leg;
        }

        /// <summary><c>still</c>: stop wandering and stand fast — the caller stops the move and
        /// faces the target.</summary>
        public void SetStill(object actor)
        {
            if (actor == null)
                return;
            legs.Remove(actor);
            still.Add(actor);
        }

        /// <summary>Any real move clears both modes.</summary>
        public void Clear(object actor)
        {
            if (actor == null)
                return;
            legs.Remove(actor);
            still.Remove(actor);
        }

        /// <summary>
        /// Advances one tick. Returns true when this tick began a new leg, so the caller reads
        /// the fresh angle. An actor that is not wandering is left alone.
        /// </summary>
        public bool Advance(object actor, double deltaTime)
        {
            if (actor == null || !legs.TryGetValue(actor, out WanderLeg leg))
                return false;
            leg.TicksRemaining -= 1;
            if (leg.TicksRemaining > 0)
                return false;
            leg.AngleDegrees = RandomAngle();
            leg.TicksRemaining = TickCount(0);
            return true;
        }

        /// <summary>Every actor currently wandering (the bridge walks them each tick).</summary>
        public List<object> ActiveActors()
        {
            return new List<object>(legs.Keys);
        }

        /// <summary>A whole-degree heading, 0-359 (0 = forward).</summary>
        public static double RandomAngle()
        {
            int degrees = (int)(Rng.NextDouble() * 360);
            return degrees >= 360 ? 359 : degrees;
        }

        /// <summary>Ticks for one leg: the authored duration, or a random 20-60 tick stretch.</summary>
        public static double TickCount(double durationSeconds)
        {
            if (durationSeconds <= 0)
                return MinTicks + (int)(Rng.NextDouble() * (MaxTicks - MinTicks + 1));
            double ticks = Math.Round(durationSeconds * TicksPerSecond);
            return ticks < 1 ? 1 : ticks;
        }
    }
}
