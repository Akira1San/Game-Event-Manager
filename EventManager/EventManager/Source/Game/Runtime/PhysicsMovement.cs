using System;

namespace Game
{
    /// <summary>How an actor is moved for one tick (chosen from the opt-in flag and its body).</summary>
    public enum PhysicsMoveMode
    {
        /// <summary>Assign <c>Position</c> directly — the default, works with no physics at all.</summary>
        Transform,
        /// <summary>Drive the body's <c>LinearVelocity</c> (a dynamic <c>RigidBody</c>).</summary>
        Dynamic,
        /// <summary>Sweep the body with <c>AddMovement</c> (a kinematic <c>RigidBody</c>).</summary>
        Kinematic,
        /// <summary>Collide-and-slide with a <c>CharacterController</c> (the walking-character body).</summary>
        Character,
    }

    /// <summary>How the <c>push</c> word reaches an actor.</summary>
    public enum PhysicsPushMode
    {
        /// <summary>Nudge the transform for one tick (no body, or physics movement not opted in).</summary>
        Transform,
        /// <summary>A real impulse on a dynamic body.</summary>
        Impulse,
        /// <summary>A swept <c>AddMovement</c> on a kinematic body, so it cannot pass through walls.</summary>
        Sweep,
        /// <summary>A collide-and-slide <c>Move</c> on a walking CharacterController.</summary>
        Character,
    }

    /// <summary>
    /// Capsule and motion limits applied to an actor's <c>CharacterController</c> when the
    /// EventSheet's "Configure Character Body" box is ticked. Values use Flax's native units,
    /// which are centimetres — the defaults match the engine's own CharacterController defaults.
    /// </summary>
    public class CharacterBodySettings
    {
        /// <summary>Capsule radius (local units).</summary>
        public float Radius = 50f;
        /// <summary>Capsule height measured between the sphere centres (local units).</summary>
        public float Height = 150f;
        /// <summary>How high a stair/ledge the character can step up.</summary>
        public float StepOffset = 30f;
        /// <summary>Steepest walkable slope, in degrees.</summary>
        public float SlopeLimit = 45f;
        /// <summary>How far below the feet counts as standing on ground (used by the grounded check).</summary>
        public float GroundCheckDistance = 10f;

        /// <summary>
        /// Keeps editor values inside ranges PhysX accepts: positive radius, step offset never
        /// taller than the capsule, slope limit a real angle. Returns a copy, so the same
        /// settings can be applied repeatedly without drifting.
        /// </summary>
        public CharacterBodySettings Sanitized()
        {
            var clean = new CharacterBodySettings
            {
                Radius = Math.Max(1f, Radius),
                Height = Math.Max(1f, Height),
                StepOffset = Math.Max(0f, StepOffset),
                SlopeLimit = Math.Min(100f, Math.Max(0f, SlopeLimit)),
                GroundCheckDistance = Math.Max(0f, GroundCheckDistance),
            };
            if (clean.StepOffset > clean.Height)
                clean.StepOffset = clean.Height;
            return clean;
        }
    }

    /// <summary>
    /// Pure decisions behind the physics-native movement opt-in (Task 4 Part C, extended by
    /// Task 12 Part A with the CharacterController body). No Flax types live here so the rules
    /// are unit tested; the bridge applies them to the real API.
    /// </summary>
    public static class PhysicsMovement
    {
        /// <summary>
        /// Flax's default gravity magnitude in engine units (cm/s²) — 9.81 m/s². Used when the
        /// event runtime feeds a CharacterController's vertical motion (its own <c>AutoGravity</c>
        /// stays off so gravity is applied exactly once).
        /// </summary>
        public const double DefaultGravity = 981.0;

        /// <summary>
        /// Downward speed (cm/s) used to keep a grounded character glued to the floor while it
        /// walks down slopes and off small steps. Small enough to be invisible, large enough to
        /// beat the collision system's contact offset.
        /// </summary>
        public const double GroundStickSpeed = 2.0;

        /// <summary>Default distance below the feet that still counts as ground (cm).</summary>
        public const double DefaultGroundCheckDistance = 10.0;

        /// <summary>
        /// Transform movement stays the default: physics only runs when the actor's EventSheet
        /// opted in ("Use Physics Movement") and the actor actually has a body.
        /// </summary>
        public static PhysicsMoveMode ModeFor(bool optedIn, bool hasRigidBody, bool bodyIsKinematic)
            => ModeFor(optedIn, false, hasRigidBody, bodyIsKinematic);

        /// <summary>
        /// Full mode pick. A <c>CharacterController</c> wins over a <c>RigidBody</c>: it is the
        /// purpose-built walking body, so an actor that carries both still walks like a character.
        /// </summary>
        public static PhysicsMoveMode ModeFor(
            bool optedIn, bool hasCharacterController, bool hasRigidBody, bool bodyIsKinematic)
        {
            if (!optedIn)
                return PhysicsMoveMode.Transform;
            if (hasCharacterController)
                return PhysicsMoveMode.Character;
            if (!hasRigidBody)
                return PhysicsMoveMode.Transform;
            return bodyIsKinematic ? PhysicsMoveMode.Kinematic : PhysicsMoveMode.Dynamic;
        }

        /// <summary>
        /// The velocity to give a dynamic body: the move direction times the speed. A move with
        /// no vertical part keeps the body's current vertical speed, so gravity and falling
        /// still work while horizontal movement is driven by the events.
        /// </summary>
        public static void DynamicVelocity(
            double directionX, double directionY, double directionZ,
            double speed, double currentVelocityY,
            out double velocityX, out double velocityY, out double velocityZ)
        {
            velocityX = directionX * speed;
            velocityZ = directionZ * speed;
            velocityY = directionY != 0 ? directionY * speed : currentVelocityY;
        }

        /// <summary>
        /// <c>stop moving</c> as a brake: kill horizontal speed, keep vertical speed (a falling
        /// body keeps falling).
        /// </summary>
        public static void BrakeVelocity(
            double currentVelocityY,
            out double velocityX, out double velocityY, out double velocityZ)
        {
            velocityX = 0;
            velocityY = currentVelocityY;
            velocityZ = 0;
        }

        /// <summary>
        /// <c>push</c> is a real impulse only where physics can take one: a dynamic body.
        /// A CharacterController and a kinematic body ignore forces, so they are moved with the
        /// same collision-aware step they use for their own motion. No body (or physics movement
        /// not opted in) keeps the old one-tick transform nudge.
        /// </summary>
        public static PhysicsPushMode PushModeFor(bool hasRigidBody, bool bodyIsKinematic)
            => PushModeFor(true, false, hasRigidBody, bodyIsKinematic);

        /// <summary>
        /// Full push mode pick, matching the move-mode rules: the target's EventSheet must opt
        /// in, a CharacterController wins over a RigidBody, and kinematic bodies are swept.
        /// </summary>
        public static PhysicsPushMode PushModeFor(
            bool optedIn, bool hasCharacterController, bool hasRigidBody, bool bodyIsKinematic)
        {
            if (!optedIn)
                return PhysicsPushMode.Transform;
            if (hasCharacterController)
                return PhysicsPushMode.Character;
            if (!hasRigidBody)
                return PhysicsPushMode.Transform;
            return bodyIsKinematic ? PhysicsPushMode.Sweep : PhysicsPushMode.Impulse;
        }

        /// <summary>Whether a <c>turn</c> word names a horizontal turn (only left/right are valid).</summary>
        public static bool IsTurnDirection(string direction)
            => direction == "left" || direction == "right";

        /// <summary>
        /// Signed yaw for <c>turn left/right degrees &lt;n&gt;</c>. In Flax a positive yaw turns
        /// forward (+Z) toward +X, i.e. to the actor's right, so left is negative and right is
        /// positive. Unknown directions return 0 so a malformed line cannot spin an actor.
        /// </summary>
        public static double SignedTurnDegrees(string direction, double degrees)
        {
            if (direction == "left")
                return -degrees;
            if (direction == "right")
                return degrees;
            return 0.0;
        }

        /// <summary>
        /// One tick of character motion. The controller's collide-and-slide <c>Move</c> already
        /// respects walls, slopes and step offset; the event language only supplies direction and
        /// speed. Kept pure so the distance math is testable without Flax.
        /// </summary>
        public static void CharacterDisplacement(
            double directionX, double directionY, double directionZ,
            double speed, double deltaTime,
            out double x, out double y, out double z)
        {
            double step = speed * deltaTime;
            x = directionX * step;
            y = directionY * step;
            z = directionZ * step;
        }

        /// <summary>
        /// Whether this tick's movement reaches (or overshoots) the current path waypoint.
        /// Transform moves snap on this; physics bodies land exactly instead of being teleported.
        /// </summary>
        public static bool ReachesWaypoint(double distance, double speed, double deltaTime)
        {
            double step = Math.Abs(speed) * Math.Max(0.0, deltaTime);
            return distance <= step;
        }

        /// <summary>
        /// The speed to apply for one <c>goto</c> tick. Full speed while the waypoint is still
        /// ahead; on the tick that reaches it, a speed that lands exactly on the waypoint (never
        /// faster than the authored speed). This removes the one-frame stall and the velocity
        /// overshoot both body types used to have at each waypoint.
        /// </summary>
        public static double PathSpeed(double distance, double speed, double deltaTime)
        {
            if (deltaTime <= 0.0 || !ReachesWaypoint(distance, speed, deltaTime))
                return speed;
            return distance / deltaTime;
        }

        /// <summary>
        /// World-space height of a character's feet. In Flax's default <c>CapsuleCenter</c>
        /// origin mode the actor position is the capsule centre, so the feet sit half the capsule
        /// below it (height between the sphere centres plus one radius). In <c>Base</c> mode the
        /// actor position already is the feet.
        /// </summary>
        public static double FeetY(double originY, bool originAtBase, double height, double radius)
        {
            if (originAtBase)
                return originY;
            return originY - (Math.Max(0.0, height) * 0.5 + Math.Max(0.0, radius));
        }

        /// <summary>
        /// Whether a surface normal is walkable under a slope limit in degrees. This is the same
        /// rule the CharacterController's own <c>SlopeLimit</c> uses, exposed so the ground probe
        /// can reject a steep face before the character starts climbing it.
        /// </summary>
        public static bool IsWalkableSlope(double normalY, double slopeLimitDegrees)
        {
            double cos = Math.Min(1.0, Math.Max(-1.0, normalY));
            double angle = Math.Acos(cos) * 180.0 / Math.PI;
            double limit = Math.Min(90.0, Math.Max(0.0, slopeLimitDegrees));
            return angle <= limit + 1e-9;
        }

        /// <summary>
        /// Next vertical speed for a character. Grounded characters rest (no accumulating fall),
        /// airborne ones accelerate downward by gravity. An explicit up/down move bypasses this
        /// and is handled by <see cref="CharacterStep"/>.
        /// </summary>
        public static double FallSpeed(double currentVelocityY, bool grounded, double gravity, double deltaTime)
        {
            if (grounded)
                return 0.0;
            return currentVelocityY - Math.Max(0.0, gravity) * Math.Max(0.0, deltaTime);
        }

        /// <summary>
        /// One tick of character motion with gravity and ground contact. Horizontal movement only
        /// applies while grounded (no air steering); airborne ticks fall straight down. An
        /// explicit up/down move (<paramref name="directionY"/> ≠ 0) still wins over gravity, so
        /// elevators and flying characters keep working. The controller's collide-and-slide
        /// <c>Move</c> then resolves walls, slopes up to its limit and step offsets.
        /// </summary>
        public static void CharacterStep(
            double directionX, double directionY, double directionZ,
            double speed, double currentFallSpeed, bool grounded,
            double gravity, double groundStickSpeed, double deltaTime,
            out double x, out double y, out double z, out double fallSpeed)
        {
            double dt = Math.Max(0.0, deltaTime);
            double horizontal = grounded ? speed * dt : 0.0;
            x = directionX * horizontal;
            z = directionZ * horizontal;

            if (directionY != 0.0)
            {
                y = directionY * speed * dt;
                fallSpeed = 0.0;
                return;
            }

            fallSpeed = grounded
                ? -Math.Max(0.0, groundStickSpeed)
                : FallSpeed(currentFallSpeed, false, gravity, dt);
            y = fallSpeed * dt;
        }

        /// <summary>
        /// One in-flight moving-platform sweep. A platform pings back and forth along a world
        /// axis by a signed distance, always ping-pong: each leg eases 0→1 (or 1→0 on the
        /// return). Pure numbers only so the timing and interpolation are unit tested without
        /// the engine; the bridge applies the per-tick delta to the actor through its
        /// <see cref="PhysicsMoveMode"/>.
        /// </summary>
        public class OngoingPlatform
        {
            public object Subject;
            public string Axis = "";            // "x", "y" or "z"
            public double Distance;             // signed distance from start (sign = starting direction)
            public double Duration;             // seconds per leg
            public TweenEasing Easing = TweenEasing.Linear;
            public double Elapsed;              // seconds into the current leg
            public int Direction = 1;           // 1 = moving away from start, -1 = returning
            public double StartX, StartY, StartZ;
            public PhysicsMoveMode Mode = PhysicsMoveMode.Transform;

            /// <summary>Interpolated progress on the current leg: 0 at start, 1 at end.
            /// Direction flips each leg, so the return leg reads 1→0.</summary>
            public double Progress
            {
                get
                {
                    if (Duration <= 0)
                        return Direction > 0 ? 1.0 : 0.0;
                    double raw = Math.Min(1.0, Elapsed / Duration);
                    double eased = TweenMath.Ease(raw, Easing);
                    return Direction > 0 ? eased : 1.0 - eased;
                }
            }

            /// <summary>The offset value along the axis for the current progress.</summary>
            public double AxisOffset => Distance * Progress;

            /// <summary>Advances the clock by <paramref name="deltaSeconds"/> and returns true
            /// on the tick a leg just completed (direction flips, elapsed resets).</summary>
            public bool Advance(double deltaSeconds)
            {
                if (Duration <= 0)
                    return true;
                Elapsed += deltaSeconds;
                if (Elapsed >= Duration)
                {
                    Elapsed = 0.0;
                    Direction = -Direction;
                    return true;
                }
                return false;
            }
        }
    }
}
