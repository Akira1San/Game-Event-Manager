using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game
{
    public class FlaxWorld : IEventWorld
    {
        /// <summary>Slack added to the two bounding-sphere radii so grazing hits count.</summary>
        private const float ContactMargin = 2f;

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
