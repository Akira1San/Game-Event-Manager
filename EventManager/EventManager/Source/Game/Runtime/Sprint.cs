using System;

namespace Game
{
    /// <summary>How hard an actor is pushing itself (Task_FPS Phase 3).</summary>
    public enum BodyPace
    {
        /// <summary>Full authored pace, no stamina cost — the default.</summary>
        Walk,
        /// <summary>A multiple of the authored pace, paid for out of <c>my Stamina</c>.</summary>
        Sprint,
    }

    /// <summary>
    /// Pure rules behind <c>sprint</c> / <c>walk</c> / <c>toggle sprint</c> and the
    /// <c>my Stamina</c> pool. No Flax types live here, so the arithmetic is unit tested; the
    /// bridge multiplies the move speeds and runs the drain once per tick.
    ///
    /// Stamina is opt-in twice over: the pool is only spent and refilled on a sheet that actually
    /// used <c>sprint</c> / <c>walk</c> / <c>toggle sprint</c> (or whose EventSheet ticked
    /// <c>Sprint Stamina</c>), and an actor that never sets <c>my Stamina</c> sprints for free
    /// (the word still multiplies its pace). Inventing or topping up a variable on every sheet
    /// that happens to press Shift — or that keeps its own <c>my Stamina</c> counter, like the
    /// TK4 sheet does by hand — would be a surprise.
    /// </summary>
    public static class Sprint
    {
        /// <summary>The per-actor flag the pace is mirrored into (1 sprinting, 0 walking), so
        /// <c>my Sprinting is 1</c> can drive an anim or a HUD.</summary>
        public const string Variable = "Sprinting";

        /// <summary>The pool the drain and refill are written back to.</summary>
        public const string StaminaVariable = "Stamina";

        /// <summary>Optional ceiling for the pool; the default is <see cref="DefaultStamina"/>.</summary>
        public const string MaxStaminaVariable = "MaxStamina";

        /// <summary>The multiplier a sheet that never sets <c>my SprintSpeed</c> gets.</summary>
        public const string SpeedVariable = "SprintSpeed";

        public const double DefaultSpeedFactor = 2.0;

        /// <summary>A sprint never walks slower than a walk, so a `my SprintSpeed` of 0.5 cannot
        /// be used to make sprinting a stealth crouch.</summary>
        public const double MinSpeedFactor = 1.0;

        public const double DefaultStamina = 100.0;

        /// <summary>5 stamina per 0.1 s of sprinting.</summary>
        public const double DrainPerSecond = 50.0;

        /// <summary>4 stamina per 0.25 s of walking, i.e. a full bar back in about six seconds.</summary>
        public const double RegenPerSecond = 16.0;

        /// <summary>How far an actor must have moved since the last tick to count as moving: a
        /// sprinting actor that is standing still does not pay for the stance.</summary>
        public const double MovedEpsilon = 0.01;

        /// <summary>
        /// Whether the bridge owns this sheet's <c>my Stamina</c>: only a sheet that actually used
        /// a pace word, or one whose EventSheet ticked <c>Sprint Stamina</c>, is drained and
        /// refilled. A sheet that keeps its own <c>Stamina</c> counter with `add` / `set` lines is
        /// left exactly as its events wrote it.
        /// </summary>
        public static bool ManagesStamina(bool usedPace, bool optedIn) => usedPace || optedIn;

        /// <summary><c>toggle sprint</c>: sprint from walking, walk from sprinting.</summary>
        public static BodyPace Toggle(BodyPace current)
            => current == BodyPace.Sprint ? BodyPace.Walk : BodyPace.Sprint;

        /// <summary>Whether there is stamina left to start a sprint.</summary>
        public static bool CanSprint(double stamina) => stamina > 0.0;

        /// <summary>
        /// The multiplier a pace asks for: 1 walking, the authored <paramref name="authored"/>
        /// sprinting (or <see cref="DefaultSpeedFactor"/> when the sheet set nothing usable),
        /// never below <see cref="MinSpeedFactor"/>.
        /// </summary>
        public static double SpeedFactor(BodyPace pace, double authored)
        {
            if (pace != BodyPace.Sprint)
                return 1.0;
            double factor = authored > 0.0 ? authored : DefaultSpeedFactor;
            return Math.Max(MinSpeedFactor, factor);
        }

        /// <summary>
        /// One tick of the pool: sprinting while moving drains, everything else refills, and the
        /// result is clamped to 0..<paramref name="max"/> (a non-positive max means the default).
        /// </summary>
        public static double Tick(double stamina, BodyPace pace, bool moving, double max, double deltaTime)
        {
            double ceiling = max > 0.0 ? max : DefaultStamina;
            double value = Math.Max(0.0, Math.Min(ceiling, stamina));
            if (deltaTime <= 0.0)
                return value;
            if (pace == BodyPace.Sprint && moving)
                value -= DrainPerSecond * deltaTime;
            else
                value += RegenPerSecond * deltaTime;
            return Math.Max(0.0, Math.Min(ceiling, value));
        }
    }
}
