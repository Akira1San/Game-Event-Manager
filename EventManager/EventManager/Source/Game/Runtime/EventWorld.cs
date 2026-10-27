using System.Collections.Generic;

namespace Game
{
    public interface IEventWorld
    {
        bool IsKeyHeld(string key);
        double Distance(object from, object to);
        bool HasTag(object target, string tag);
        /// <summary>The actor's real name. Never use ToString() for logic — Flax adds type/id noise.</summary>
        string NameOf(object actor);
        /// <summary>Distance-based contact test, used when no colliders are present.</summary>
        bool IsTouching(object a, object b);
        /// <summary>Every live actor a contact poll may consider (active only, no templates).</summary>
        IReadOnlyList<object> Actors();
    }

    public class DefaultEventWorld : IEventWorld
    {
        public bool IsKeyHeld(string key)
        {
            return false;
        }

        public double Distance(object from, object to)
        {
            return double.MaxValue;
        }

        public bool HasTag(object target, string tag)
        {
            return false;
        }

        public string NameOf(object actor)
        {
            return actor == null ? "" : actor.ToString();
        }

        public bool IsTouching(object a, object b)
        {
            return false;
        }

        public IReadOnlyList<object> Actors()
        {
            return new object[0];
        }
    }
}
