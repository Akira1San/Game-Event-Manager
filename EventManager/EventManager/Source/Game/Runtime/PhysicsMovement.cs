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
    /// Pure decisions behind the physics-native movement opt-in (Task 4 Part C). No Flax types
    /// live here so the rules are unit tested; the bridge applies them to the real API.
    /// </summary>
    public static class PhysicsMovement
    {
        /// <summary>
        /// Transform movement stays the default: physics only runs when the actor's EventSheet
        /// opted in ("Use Physics Movement") and the actor actually has a RigidBody.
        /// </summary>
        public static PhysicsMoveMode ModeFor(bool optedIn, bool hasRigidBody, bool bodyIsKinematic)
        {
            if (!optedIn || !hasRigidBody)
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
        /// Kinematic bodies ignore forces, so they keep the old one-tick transform nudge.
        /// </summary>
        public static PhysicsPushMode PushModeFor(bool hasRigidBody, bool bodyIsKinematic)
        {
            if (!hasRigidBody || bodyIsKinematic)
                return PhysicsPushMode.Transform;
            return PhysicsPushMode.Impulse;
        }
    }
}
