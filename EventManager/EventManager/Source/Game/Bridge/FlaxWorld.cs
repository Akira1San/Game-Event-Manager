using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game
{
    public class FlaxWorld : IEventWorld
    {
        /// <summary>Slack added to the two bounding-sphere radii so grazing hits count.</summary>
        private const float ContactMargin = 2f;
        /// <summary>Actor names already warned about a missing physics material (warn once each).</summary>
        private readonly HashSet<string> _missingMaterialWarned = new HashSet<string>();

        public bool IsKeyHeld(string key)
        {
            string name = key.Trim();
            if (name.EndsWith(" Mouse", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("Mouse", StringComparison.OrdinalIgnoreCase))
            {
                string buttonName = name.EndsWith(" Mouse", StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(0, name.Length - " Mouse".Length)
                    : name.Substring(0, name.Length - "Mouse".Length);
                if (Enum.TryParse(buttonName, true, out MouseButton button))
                    return Input.GetMouseButton(button);
                return false;
            }
            if (name.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase))
                return false;
            if (TryParseKey(name.Replace(" ", ""), out KeyboardKeys keys))
                return Input.GetKey(keys);
            return false;
        }

        /// <summary>Written gamepad button names → Flax's <c>GamepadButton</c>. A name we don't
        /// know fails rather than answering for another button.</summary>
        private static readonly Dictionary<string, GamepadButton> GamepadButtonNames =
            new Dictionary<string, GamepadButton>(StringComparer.OrdinalIgnoreCase)
            {
                { "A", GamepadButton.A },
                { "B", GamepadButton.B },
                { "X", GamepadButton.X },
                { "Y", GamepadButton.Y },
                { "LeftShoulder", GamepadButton.LeftShoulder },
                { "RightShoulder", GamepadButton.RightShoulder },
                { "Back", GamepadButton.Back },
                { "Start", GamepadButton.Start },
                { "DPadUp", GamepadButton.DPadUp },
                { "DPadDown", GamepadButton.DPadDown },
                { "DPadLeft", GamepadButton.DPadLeft },
                { "DPadRight", GamepadButton.DPadRight },
                { "LeftStickUp", GamepadButton.LeftStickUp },
                { "LeftStickDown", GamepadButton.LeftStickDown },
                { "LeftStickLeft", GamepadButton.LeftStickLeft },
                { "LeftStickRight", GamepadButton.LeftStickRight },
                { "RightStickUp", GamepadButton.RightStickUp },
                { "RightStickDown", GamepadButton.RightStickDown },
                { "RightStickLeft", GamepadButton.RightStickLeft },
                { "RightStickRight", GamepadButton.RightStickRight },
            };

        /// <summary>Thumb stick buttons need a dead zone before they count as pressed.</summary>
        public const float GamepadDeadZone = 0.3f;

        public static bool TryGetGamepadButton(string name, out GamepadButton button)
        {
            button = default;
            if (string.IsNullOrEmpty(name))
                return false;
            return GamepadButtonNames.TryGetValue(name.Trim(), out button);
        }

        public bool IsGamepadButtonHeld(int gamepad, string button)
        {
            if (!TryGetGamepadButton(button, out GamepadButton mapped))
                return false;
            return Input.GetGamepadButton(gamepad, mapped, GamepadDeadZone);
        }

        static bool TryParseKey(string name, out KeyboardKeys keys)
        {
            keys = default;
            // Map the written key onto Flax's own enum name (`Space` -> `Spacebar`, `1` ->
            // `Alpha1`, `Up` -> `ArrowUp`, `Left Shift` -> `Shift`). Never try other keys:
            // a name we don't know has to fail instead of answering for a different key.
            string engineName = ActionWords.EngineKeyName(name.Replace(" ", ""));
            if (engineName == null)
                return false;
            return Enum.TryParse(engineName, true, out keys);
        }

        public double Distance(object from, object to)
        {
            Actor first = from as Actor;
            Actor second = to as Actor;
            if (first == null || second == null)
                return 100000;
            return Vector3.Distance(first.Position, second.Position);
        }

        /// <summary>Horizontal (XZ) distance, ignoring height. Ground-plane triggers
        /// (<c>near</c>, <c>arrives</c>) use this so a tall prop's pivot doesn't keep it
        /// perpetually "far" from a short actor like the Player.</summary>
        public double Distance2D(object from, object to)
        {
            Actor first = from as Actor;
            Actor second = to as Actor;
            if (first == null || second == null)
                return 100000;
            double dx = first.Position.X - second.Position.X;
            double dz = first.Position.Z - second.Position.Z;
            return System.Math.Sqrt(dx * dx + dz * dz);
        }

        public bool HasTag(object target, string tag)
        {
            Actor actor = target as Actor;
            if (actor == null)
                return false;
            return actor.HasTag(tag);
        }

        public string NameOf(object actor)
        {
            return (actor as Actor)?.Name ?? actor?.ToString() ?? "";
        }

        public bool IsTouching(object a, object b)
        {
            var first = a as Actor;
            var second = b as Actor;
            if (first == null || second == null || !first || !second)
                return false;
            if (!first.IsActive || !second.IsActive)
                return false;
            float radius = first.BoundingSphere.Radius + second.BoundingSphere.Radius + ContactMargin;
            return Vector3.Distance(first.Position, second.Position) <= radius;
        }

        public string MaterialOf(object actor)
        {
            // The physical material lives on Collider (a PhysicsColliderActor subclass), not on
            // PhysicsColliderActor itself.
            Collider collider = null;
            var act = actor as Actor;
            if (act != null)
                collider = act.FindActor<Collider>() ?? (act as Collider);
            else
                collider = actor as Collider;
            if (collider == null)
                return "";

            PhysicalMaterial material = null;
            var materialRef = collider.Material;
            if (materialRef != null)
                material = materialRef.Instance as PhysicalMaterial;

            // The asset reference may not be loaded into memory yet. We cannot load it by id
            // here because JsonAssetReference<PhysicalMaterial> does not expose its id/path in
            // this Flax version, so if Instance is null we warn and skip.
            if (material == null)
            {
                if (act != null && _missingMaterialWarned.Add(act.Name))
                    Debug.LogWarning($"Event sheet: \"{act.Name}\" has no physics material — `when me touches <material>` will not match it.");
                return "";
            }

            // PhysicalMaterial.Tag is the gameplay surface identifier (e.g. "Grass", "Stone").
            string name = material.Tag.ToString();
            if (string.IsNullOrEmpty(name))
                name = act?.Name ?? "";
            return name;
        }

        public bool IsMoving(object actor)
        {
            return FlaxActions.IsMoving(actor as Actor);
        }

        public bool IsOnGround(object actor)
        {
            return FlaxActions.IsGrounded(actor as Actor);
        }

        public bool IsClimbing(object actor)
        {
            return FlaxActions.IsClimbing(actor as Actor);
        }

        public bool TryGetAnimProgress(object actor, string clip, out double progress, out int play)
        {
            return FlaxActions.TryGetAnimProgress(actor, clip, out progress, out play);
        }

        public bool IsParticlePlaying(object actor, string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            Actor act = actor as Actor;
            if (act == null)
                return false;
            string wanted = AssetNames.Key(name);
            if (wanted.Length == 0)
                return false;
            foreach (ParticleEffect effect in CollectParticleEffects(act))
            {
                if (effect == null || effect.ParticlesCount <= 0)
                    continue;
                ParticleSystem sys = effect.ParticleSystem;
                if (sys == null)
                    continue;
                string key = AssetNames.Key(sys.Path);
                if (key.Length > 0 && string.Equals(key, wanted, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public bool IsButtonPressed(object actor)
        {
            var button = FindButton(actor as Actor);
            return button != null && button.IsPressed;
        }

        public bool IsButtonDisabled(object actor)
        {
            var button = FindButton(actor as Actor);
            return button == null || !button.Enabled;
        }

        public bool IsFocused(object actor)
        {
            var control = FindControl(actor as Actor);
            return control != null && control.IsFocused;
        }

        public string TextOf(object actor)
        {
            var box = FindTextBox(actor as Actor);
            return box == null ? "" : box.Text ?? "";
        }

        public double ProgressOf(object actor)
        {
            var bar = FindProgressBar(actor as Actor);
            return bar != null && bar.Maximum > 0 ? bar.Value / bar.Maximum : 0;
        }

        public double SliderOf(object actor)
        {
            var slider = FindSlider(actor as Actor);
            return slider != null ? slider.Value : 0;
        }

        /// <summary>The <see cref="FlaxEngine.GUI.TextBox"/> on this actor's UIControl, or on a
        /// child UIControl.</summary>
        private static FlaxEngine.GUI.TextBox FindTextBox(Actor actor)
        {
            if (actor == null)
                return null;
            var ui = actor as UIControl;
            if (ui != null)
                return ui.Control as FlaxEngine.GUI.TextBox;
            return actor.FindActor<UIControl>()?.Control as FlaxEngine.GUI.TextBox;
        }

        /// <summary>Any GUI control on this actor's UIControl (focus targets include TextBox,
        /// Button and Slider).</summary>
        private static FlaxEngine.GUI.Control FindControl(Actor actor)
        {
            if (actor == null)
                return null;
            var ui = actor as UIControl;
            if (ui != null)
                return ui.Control;
            return actor.FindActor<UIControl>()?.Control;
        }

        /// <summary>The <see cref="FlaxEngine.GUI.Button"/> on this actor's UIControl, or on a
        /// child UIControl (a button's mesh/image can sit under the actor).</summary>
        private static FlaxEngine.GUI.Button FindButton(Actor actor)
        {
            if (actor == null)
                return null;
            var ui = actor as UIControl;
            if (ui != null)
                return ui.Control as FlaxEngine.GUI.Button;
            return actor.FindActor<UIControl>()?.Control as FlaxEngine.GUI.Button;
        }

        /// <summary>The <see cref="FlaxEngine.GUI.ProgressBar"/> on this actor's UIControl, or on a
        /// child UIControl.</summary>
        private static FlaxEngine.GUI.ProgressBar FindProgressBar(Actor actor)
        {
            if (actor == null)
                return null;
            var ui = actor as UIControl;
            if (ui != null)
                return ui.Control as FlaxEngine.GUI.ProgressBar;
            return actor.FindActor<UIControl>()?.Control as FlaxEngine.GUI.ProgressBar;
        }

        /// <summary>The <see cref="FlaxEngine.GUI.Slider"/> on this actor's UIControl, or on a
        /// child UIControl.</summary>
        private static FlaxEngine.GUI.Slider FindSlider(Actor actor)
        {
            if (actor == null)
                return null;
            var ui = actor as UIControl;
            if (ui != null)
                return ui.Control as FlaxEngine.GUI.Slider;
            return actor.FindActor<UIControl>()?.Control as FlaxEngine.GUI.Slider;
        }

        public bool HasLineOfSight(object from, object to)
        {
            var first = from as Actor;
            var second = to as Actor;
            if (first == null || second == null || !first || !second)
                return false;
            if (!first.IsActive || !second.IsActive)
                return false;
            if (!Physics.LineCastAll(first.Position, second.Position, out var hits, ~0u, true))
                return true; // nothing between the two actors — sight is clear
            foreach (var hit in hits)
            {
                // RayCastHit.Collider is the collider actor that was hit. A collider is almost
                // always a CHILD of the body it belongs to (a BoxCollider on the zombie, a hull on
                // a vehicle), so comparing it with the actor passed in only ever matched colliders
                // sitting on the actor itself. When it did not match, a body blocked its own sight:
                // a level chaser's foot-level ray clips the bottom of its own box and `me sees`
                // went blind the instant `facing forward` levelled the model.
                var hitActor = hit.Collider as Actor;
                if (hitActor == null)
                    return false; // hit something without an actor — blocks sight
                if (BelongsToBody(hitActor, first) || BelongsToBody(hitActor, second))
                    continue; // the seer's or the target's own collider (or a child's)
                return false; // hit something else (wall, ...) — blocks sight
            }
            return true;
        }

        /// <summary>True when <paramref name="part"/> is <paramref name="body"/> itself or a
        /// descendant of it (a collider/hull child actor).</summary>
        private static bool BelongsToBody(Actor part, Actor body)
        {
            for (Actor node = part; node != null && !(node is Scene); node = node.Parent)
                if (ReferenceEquals(node, body))
                    return true;
            return false;
        }

        public double DistanceToPoint(object from, double x, double z)
        {
            var actor = from as Actor;
            if (actor == null || !actor)
                return double.MaxValue;
            double dx = actor.Position.X - x;
            double dz = actor.Position.Z - z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        public bool HasLineOfSightToPoint(object from, double x, double z)
        {
            var actor = from as Actor;
            if (actor == null || !actor || !actor.IsActive)
                return false;
            // Sound travels at the listener's height, so a low wall still blocks it.
            var target = new Vector3((float)x, actor.Position.Y, (float)z);
            if (!Physics.LineCastAll(actor.Position, target, out var hits, ~0u, true))
                return true; // nothing in the way
            foreach (var hit in hits)
            {
                var hitActor = hit.Collider as Actor;
                if (hitActor == null)
                    return false; // hit geometry without an actor — blocks the sound
                if (BelongsToBody(hitActor, actor))
                    continue; // the listener's own collider (or a child's)
                return false; // a wall, a crate, anything else: the shot is muffled
            }
            return true;
        }

        public object FindActor(string name)
        {
            return Level.FindActor(name);
        }

        public IReadOnlyList<object> Actors()
        {
            var found = new List<object>();
            var scenes = Level.Scenes;
            for (int s = 0; s < scenes.Length; s++)
            {
                var scene = scenes[s];
                if (scene == null)
                    continue;
                Collect(scene, found);
            }
            return found;
        }

        /// <summary>The active scene camera. Flax's <c>Level.FindActor</c> is case-sensitive
        /// while scenes name the camera "Camera" or "camera", so fall back to any active
        /// <see cref="FlaxEngine.Camera"/> actor.</summary>
        public static FlaxEngine.Camera FindCamera()
        {
            Actor named = Level.FindActor("Camera");
            if (named is FlaxEngine.Camera namedCamera && namedCamera.IsActive)
                return namedCamera;

            FlaxEngine.Camera fallback = null;
            Scene[] scenes = Level.Scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] == null)
                    continue;
                FlaxEngine.Camera found = FindCamera(scenes[i], ref fallback);
                if (found != null)
                    return found;
            }
            return fallback;
        }

        private static FlaxEngine.Camera FindCamera(Actor node, ref FlaxEngine.Camera fallback)
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                if (child is FlaxEngine.Camera camera)
                {
                    if (camera.IsActive)
                        return camera;
                    if (fallback == null)
                        fallback = camera;
                }
                FlaxEngine.Camera found = FindCamera(child, ref fallback);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>A ray from the active scene camera through the mouse position. Uses the
        /// camera's own projection so the ray keeps its downward pitch (a flattened ray would
        /// run parallel to the ground and never hit it).</summary>
        public static bool TryGetMouseRay(Float2 mousePos, out Ray ray)
        {
            ray = new Ray();
            var camera = FindCamera();
            if (camera == null)
                return false;
            ray = camera.ConvertMouseToRay(mousePos);
            return true;
        }

        /// <summary>True when the cursor is over <paramref name="targetActor"/>. Casts a ray from the
        /// scene camera through the mouse position and walks up from the first hit collider to
        /// its actor, so a hotspot's collider (or a child collider) counts as hovered.</summary>
        public bool IsHovered(object targetActor)
        {
            var target = targetActor as Actor;
            if (target == null || !target.IsActive)
                return false;
            PhysicsColliderActor nearest = NearestColliderUnderMouse();
            if (nearest == null)
                return false;

            Actor node = nearest;
            while (node != null && !(node is Scene))
            {
                if (ReferenceEquals(node, target))
                    return true;
                node = node.Parent;
            }
            return false;
        }

        /// <summary>
        /// The scene actor directly under the pointer, or null (Task 48 Part D). One raycast for
        /// the whole frame, unlike <see cref="IsHovered"/> which answers for one named actor: a
        /// cursor rule has to ask "what is hovered" before it can pick a name.
        ///
        /// The answer is the top-level actor - the child of the scene - so a unit whose mesh
        /// carries the collider still reads as the unit and not as its mesh. A pointer over a
        /// <see cref="UIControl"/> answers null: the command card is not a world hotspot, and a
        /// hover cursor over a button would compete with the button's own look. Inactive (fog-
        /// hidden or deactivated) actors answer null too, the same rule the marquee uses.
        /// </summary>
        public object HoveredActor()
        {
            PhysicsColliderActor nearest = NearestColliderUnderMouse();
            if (nearest == null)
                return null;

            Actor top = null;
            Actor node = nearest;
            while (node != null && !(node is Scene))
            {
                if (node is UIControl)
                    return null;
                top = node;
                node = node.Parent;
            }
            return top != null && top.IsActive ? top : null;
        }

        /// <summary>
        /// The nearest collider a ray from the scene camera through the mouse hits, or null.
        /// Only the nearest hit counts: a hotspot behind a wall must not read as hovered.
        /// </summary>
        private static PhysicsColliderActor NearestColliderUnderMouse()
        {
            if (!TryGetMouseRay(Input.MousePosition, out Ray ray))
                return null;
            if (!Physics.LineCastAll(ray.Position, ray.Position + ray.Direction * 10000f, out var hits, ~0u, true))
                return null;

            PhysicsColliderActor nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.Collider != null && hit.Distance < nearestDistance)
                {
                    nearestDistance = hit.Distance;
                    nearest = hit.Collider;
                }
            }
            return nearest;
        }

        /// <summary>True when <paramref name="child"/> is <paramref name="root"/> or a
        /// descendant in the scene hierarchy. A contact poll uses this to skip the actor's
        /// own child colliders so they are never tested as foreign contact targets (which
        /// would spuriously warn about a missing physics material on a body collider).</summary>
        public bool IsDescendantOf(object child, object root)
        {
            var c = child as Actor;
            var r = root as Actor;
            if (c == null || r == null)
                return ReferenceEquals(child, root);
            for (Actor node = c; node != null; node = node.Parent)
            {
                if (ReferenceEquals(node, r))
                    return true;
                if (node is Scene)
                    break;
            }
            return false;
        }

        // ---- RTS state (Task 18 Part H) ----
        // The live EventRuntime owns the selection, resources, fog, construction progress and
        // order queues, so the world forwards these queries to it. Before the manager exists
        // (a bare scene, a tool window) every query answers empty.

        private static EventRuntime LiveRuntime => EventsManager.Instance == null ? null : EventsManager.Instance.Runtime;

        public IReadOnlyList<object> GetSelection()
        {
            EventRuntime runtime = LiveRuntime;
            return runtime == null ? Array.Empty<object>() : runtime.SelectedActors;
        }

        public int GetSelectedCount()
        {
            EventRuntime runtime = LiveRuntime;
            return runtime == null ? 0 : runtime.SelectedCount;
        }

        public double GetResource(string name)
        {
            EventRuntime runtime = LiveRuntime;
            return runtime == null ? 0 : runtime.ResourceValue(name);
        }

        public bool IsRevealed(object actor, int playerId)
        {
            EventRuntime runtime = LiveRuntime;
            return runtime != null && runtime.IsRevealedFor(actor, playerId);
        }

        public void RevealArea(int playerId, double x, double z, double radius)
        {
            LiveRuntime?.RevealArea(playerId, x, z, radius);
        }

        public void HideArea(int playerId, double x, double z, double radius)
        {
            LiveRuntime?.HideArea(playerId, x, z, radius);
        }

        public double GetBuildProgress(object building)
        {
            EventRuntime runtime = LiveRuntime;
            return runtime == null ? 0 : runtime.BuildProgressOf(building);
        }

        public IReadOnlyList<string> GetOrderQueue(object unit)
        {
            EventRuntime runtime = LiveRuntime;
            return runtime == null ? Array.Empty<string>() : runtime.OrderQueueOf(unit);
        }

        private static void Collect(Actor node, List<object> found)
        {
            foreach (Actor child in node.Children)
            {
                if (child == null || !child.IsActive)
                    continue; // templates are inactive and must never be contact targets
                found.Add(child);
                Collect(child, found);
            }
        }

        /// <summary>This actor and every descendant ParticleEffect, depth-first.</summary>
        private static IEnumerable<ParticleEffect> CollectParticleEffects(Actor root)
        {
            var stack = new Stack<Actor>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Actor node = stack.Pop();
                if (node == null)
                    continue;
                ParticleEffect effect = node as ParticleEffect;
                if (effect != null)
                    yield return effect;
                foreach (Actor child in node.Children)
                    if (child != null)
                        stack.Push(child);
            }
        }
    }
}
