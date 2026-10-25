using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game
{
    public class FlaxWorld : IEventWorld
    {
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
            return actor.Name == tag;
        }
    }
}
