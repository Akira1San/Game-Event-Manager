using FlaxEngine;
using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// Attach this to an actor and point it at an .events file (e.g. "Player"), like assigning
    /// a script in any engine. Blank Event File = the actor's own name (so an actor named
    /// Player keeps playing Player.events with zero setup). Several actors can share one file;
    /// `my` variables stay per-object.
    /// </summary>
    public class EventSheet : Script
    {
        /// <summary>.events file to run (without extension or with it — both work). Blank = actor name.</summary>
        [Tooltip(".events file name (with or without the extension). Blank = this actor's name.")]
        public string EventFile = "";

        /// <summary>
        /// Marks this file as the project's self-test sheet (Task 51). The marking is per file,
        /// not per actor — tick it on the one actor that plays `SelfTest.events`, or rely on the
        /// filename convention. Cases still come from the `expect` lines, so a sheet without
        /// them behaves exactly as before.
        /// </summary>
        [Tooltip("This .events file is the project's self-test sheet. A file named SelfTest.events is one automatically.")]
        public bool SelfTestSheet = false;

        /// <summary>True when this component's file is a self-test sheet: the explicit flag, or
        /// the `SelfTest.events` filename convention.</summary>
        public bool IsSelfTestSheet()
        {
            return SelfTestSheet || string.Equals(SourceName(), "SelfTest", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Material → clip-name pairs for <c>play step sound "&lt;material&gt;"</c>
        /// (Task 13 C). E.g. Grass → footstep. The sheet reads these when it starts; changing
        /// them at runtime has no effect until the sheet is re-attached.</summary>
        [Serializable]
        public struct StepSoundEntry
        {
            public string Material;   // physics-material Tag, e.g. "Grass"
            public string Clip;       // audio-clip name, e.g. "footstep"
        }
        [Tooltip("Material → clip-name pairs for `play step sound \"<material>\"` (Task 13 C). E.g. Grass → footstep.")]
        public List<StepSoundEntry> StepSoundMap = new List<StepSoundEntry>();

        /// <summary>
        /// Opt-in physics-native movement: drive this actor's body instead of assigning Position
        /// every tick. The body picks the flavour — a <c>CharacterController</c> gets
        /// collide-and-slide walking, a dynamic <c>RigidBody</c> gets LinearVelocity, and a
        /// kinematic RigidBody gets a swept move. Put the EventSheet on the body actor itself
        /// (mesh and colliders under it) so the body's motion carries the visuals.
        /// Off by default — transform movement stays normal.
        /// </summary>
        [Tooltip("Move through this actor's body instead of setting Position. Works with a CharacterController (walking, slopes, steps) or a RigidBody (velocity / swept move). Off = transform movement.")]
        public bool UsePhysicsMovement = false;

        /// <summary>
        /// Opt-in first-person controls (Task_FPS Phase 1): tick this on the player actor and the
        /// mouse turns it — yaw on the actor's world Y, pitch on a camera child's local X — and
        /// that child is the camera the view uses. The rest of the camera rigs (follow, at, aims,
        /// free-pan) stay off while this is on, so a top-down sheet is completely unaffected.
        /// The feel knobs are the globals `LookSensitivity`, `LookInvertX` and `LookInvertY`.
        /// </summary>
        [Tooltip("Drive this actor as a first-person player: the mouse turns the actor (yaw) and tilts its camera child (pitch). Put a Camera actor under the player as the eyes. Off = the normal top-down controls.")]
        public bool FirstPerson = false;

        /// <summary>
        /// Opt-in (Task_FPS Phase 2): resolve `move forward/back/left/right` from the camera's view
        /// direction instead of the actor's own facing. That is what lets a first-person player
        /// strafe without turning, and it also keeps a body facing one way while its view looks
        /// another. Off by default, so every top-down sheet keeps today's actor-relative movement.
        /// </summary>
        [Tooltip("Resolve move forward/back/left/right from the camera's view direction instead of the actor's facing. Off = movement follows the body.")]
        public bool MoveRelativeToCamera = false;

        /// <summary>
        /// Opt-in (Task_FPS Phase 3): let the bridge own this sheet's <c>my Stamina</c> — spend it
        /// at 50/s while sprinting and moving, refill it at 16/s otherwise, and force <c>walk</c>
        /// when it reaches 0. A sheet that uses <c>sprint</c> / <c>walk</c> / <c>toggle sprint</c>
        /// is managed automatically; tick this for one whose events keep their own pool by hand.
        /// Unticked, a sheet that never says a pace word keeps every number its lines wrote.
        /// </summary>
        [Tooltip("Let the event runner spend and refill this sheet's `my Stamina`. Automatic for a sheet that uses `sprint` / `walk`; tick this for one that manages stamina with its own lines.")]
        public bool SprintStamina = false;

        /// <summary>
        /// Ticks the capsule settings below onto the actor's <c>CharacterController</c> when the
        /// sheet starts. Leave unticked to use whatever the CharacterController inspector says.
        /// </summary>
        [Tooltip("Apply the capsule settings below to this actor's CharacterController on start. Untick to keep the values authored in the CharacterController inspector.")]
        public bool ConfigureCharacterBody = false;

        /// <summary>Character capsule radius (Flax units; the engine default is 50).</summary>
        [Tooltip("Character capsule radius, in Flax units. Engine default: 50.")]
        public float CharacterRadius = 50f;

        /// <summary>Character capsule height between the sphere centres (Flax units; engine default 150).</summary>
        [Tooltip("Character capsule height (between the two sphere centres), in Flax units. Engine default: 150.")]
        public float CharacterHeight = 150f;

        /// <summary>Ledge height the character can step up (Flax units; engine default 30).</summary>
        [Tooltip("How high a stair or ledge the character steps up, in Flax units. Engine default: 30.")]
        public float CharacterStepOffset = 30f;

        /// <summary>Steepest slope the character can walk up, in degrees (engine default 45).</summary>
        [Tooltip("Steepest walkable slope in degrees. Engine default: 45.")]
        public float CharacterSlopeLimit = 45f;

        /// <summary>How far below the feet still counts as ground (the grounded-check distance).</summary>
        [Tooltip("How far below the feet still counts as standing on ground, in Flax units. Used by the character's grounded check.")]
        public float CharacterGroundCheckDistance = 10f;

        public override void OnStart()
        {
            ApplyCharacterBody();
            SubscribeTree(Actor);
            EventsManager.Instance?.RegisterSheet(this);
        }

        /// <summary>The capsule settings authored on this component, already range-clamped.</summary>
        public CharacterBodySettings ReadCharacterBodySettings()
        {
            return new CharacterBodySettings
            {
                Radius = CharacterRadius,
                Height = CharacterHeight,
                StepOffset = CharacterStepOffset,
                SlopeLimit = CharacterSlopeLimit,
                GroundCheckDistance = CharacterGroundCheckDistance,
            }.Sanitized();
        }

        /// <summary>
        /// Applies the optional capsule configuration to this actor's CharacterController.
        /// Does nothing when the box is unticked; warns when it is ticked but there is no
        /// controller to configure.
        /// </summary>
        public void ApplyCharacterBody()
        {
            if (!ConfigureCharacterBody)
                return;
            CharacterController controller = FindCharacterController(Actor);
            if (controller == null)
            {
                Debug.LogWarning($"Event sheet: \"{Actor?.Name}\" has Configure Character Body ticked, but there is no CharacterController on it (or under it). Add one in the editor, or untick the box.");
                return;
            }
            CharacterBodySettings settings = ReadCharacterBodySettings();
            controller.Radius = settings.Radius;
            controller.Height = settings.Height;
            controller.StepOffset = settings.StepOffset;
            controller.SlopeLimit = settings.SlopeLimit;
            // The event runtime keeps the character's vertical speed (gravity/ground snap), so
            // the controller must not apply its own gravity on top of it (Task 12 C).
            controller.AutoGravity = false;
            // A steep face should be a slide, not a wall the character can stand on.
            controller.NonWalkableMode = CharacterController.NonWalkableModes.PreventClimbingAndForceSliding;
        }

        /// <summary>
        /// The CharacterController that moves this actor: the actor itself (recommended), a
        /// parent, or a child. Shared with the movement bridge so detection and configuration
        /// always agree.
        /// </summary>
        public static CharacterController FindCharacterController(Actor actor)
        {
            if (actor == null)
                return null;
            for (Actor node = actor; node != null && !(node is Scene); node = node.Parent)
                if (node is CharacterController controller)
                    return controller;
            return actor.FindActor<CharacterController>();
        }

        public override void OnDestroy()
        {
            EventsManager.Instance?.UnregisterSheet(this);
            UnsubscribeTree(Actor);
        }

        /// <summary>The .events source this sheet points at (always without the extension).</summary>
        public string SourceName()
        {
            string wanted = EventFile == null ? "" : EventFile.Trim();
            if (wanted.EndsWith(".events", System.StringComparison.OrdinalIgnoreCase))
                wanted = wanted.Substring(0, wanted.Length - ".events".Length);
            return wanted.Length > 0 ? wanted : Actor.Name;
        }

        private void SubscribeTree(Actor actor)
        {
            if (actor == null)
                return;
            if (actor is Collider collider)
                Subscribe(collider);
            foreach (Actor child in actor.Children)
                SubscribeTree(child);
        }

        private void UnsubscribeTree(Actor actor)
        {
            if (actor == null)
                return;
            if (actor is Collider collider)
                Unsubscribe(collider);
            foreach (Actor child in actor.Children)
                UnsubscribeTree(child);
        }

        private void Subscribe(Collider collider)
        {
            if (collider != null)
            {
                collider.TriggerEnter += HandleTriggerEnter;
                collider.TriggerExit += HandleTriggerExit;
            }
        }

        private void Unsubscribe(Collider collider)
        {
            if (collider != null)
            {
                collider.TriggerEnter -= HandleTriggerEnter;
                collider.TriggerExit -= HandleTriggerExit;
            }
        }

        private void HandleTriggerEnter(PhysicsColliderActor collider)
        {
            EventsManager.Instance?.HandleTouch(Actor, OwnerOf(collider), false);
        }

        private void HandleTriggerExit(PhysicsColliderActor collider)
        {
            EventsManager.Instance?.HandleTouch(Actor, OwnerOf(collider), true);
        }

        private static Actor OwnerOf(PhysicsColliderActor collider)
        {
            if (collider == null)
                return null;
            // Walk up from the collider (Bullet → RigidBody → SphereCollider) until we find the
            // actor that actually carries the event sheet; fall back to the topmost actor below Scene.
            Actor topmost = null;
            Actor node = collider;
            while (node != null && !(node is Scene))
            {
                if (node.GetScript<EventSheet>() != null)
                    return node;
                topmost = node;
                node = node.Parent;
            }
            return topmost ?? collider;
        }
    }
}
