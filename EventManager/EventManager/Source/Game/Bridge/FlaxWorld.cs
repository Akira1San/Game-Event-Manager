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

        static bool TryParseKey(string name, out KeyboardKeys keys)
        {
            keys = default;
            // Flax uses KeyboardKeys.Return, not KeyboardKeys.Enter
            if (name == "Enter") name = "Return";
            foreach (var candidate in new[] { name, name + "Bar", "SpaceBar", "Space_Key", "SpaceKey" })
            {
                if (Enum.TryParse(candidate, true, out keys))
                    return true;
            }
            return false;
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

            // The asset reference may not be loaded into memory yet — try loading it by asset id.
            if (material == null && materialRef != null && materialRef.Id != Guid.Empty)
                material = Content.Load<PhysicalMaterial>(materialRef.Id);

            if (material == null)
            {
                if (act != null && _missingMaterialWarned.Add(act.Name))
                    Debug.LogWarning($"Event sheet: \"{act.Name}\" has no physics material — `when me touches <material>` will not match it.");
                return "";
            }

            // PhysicalMaterial.Tag is the gameplay surface identifier (e.g. "Grass", "Stone").
            string name = material.Tag.ToString();
            if (string.IsNullOrEmpty(name))
                name = materialRef.Path;
            return name;
        }

        public bool IsMoving(object actor)
        {
            return FlaxActions.IsMoving(actor as Actor);
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
                // RayCastHit.Collider is the collider that was hit; its Actor is the scene actor.
                // Only me or the target itself may be hit; anything else (a wall, ...) blocks sight.
                var hitActor = hit.Collider as Actor;
                if (hitActor == null)
                    return false; // hit something without an actor — blocks sight
                if (!ReferenceEquals(hitActor, first) && !ReferenceEquals(hitActor, second))
                    return false; // hit something else (wall, ...) — blocks sight
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
            if (!TryGetMouseRay(Input.MousePosition, out Ray ray))
                return false;
            if (!Physics.LineCastAll(ray.Position, ray.Position + ray.Direction * 10000f, out var hits, ~0u, true))
                return false;

            // Only the nearest hit counts: a hotspot behind a wall must not read as hovered.
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
    }
}
