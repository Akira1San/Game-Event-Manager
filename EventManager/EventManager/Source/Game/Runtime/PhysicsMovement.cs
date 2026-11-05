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
        /// <summary>Nudge the transform for one tick (no body, or a kinematic one that ignores forces).</summary>
        Transform,
        /// <summary>A real impulse on a dynamic body.</summary>
        Impulse,
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
        /// Kinematic bodies and CharacterControllers ignore forces, so they keep the old one-tick
        /// transform nudge.
        /// </summary>
        public static PhysicsPushMode PushModeFor(bool hasRigidBody, bool bodyIsKinematic)
        {
            if (!hasRigidBody || bodyIsKinematic)
                return PhysicsPushMode.Transform;
            return PhysicsPushMode.Impulse;
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
    }
}
