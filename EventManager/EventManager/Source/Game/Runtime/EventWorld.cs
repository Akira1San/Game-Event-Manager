using System.Collections.Generic;

namespace Game
{
    public interface IEventWorld
    {
        bool IsKeyHeld(string key);
        double Distance(object from, object to);
        /// <summary>Horizontal (XZ) distance ignoring height. Ground-plane triggers
        /// (<c>near</c>, <c>arrives</c>) use this so a tall prop's pivot can't keep it
        /// perpetually "far" from a short actor like the Player.</summary>
        double Distance2D(object from, object to);
        bool HasTag(object target, string tag);
        /// <summary>The actor's real name. Never use ToString() for logic — Flax adds type/id noise.</summary>
        string NameOf(object actor);
        /// <summary>Distance-based contact test, used when no colliders are present.</summary>
        bool IsTouching(object a, object b);
        /// <summary>True when <paramref name="actor"/> is actively moving this tick (an ongoing
        /// <c>goto</c>/<c>face</c> move, or a <c>move</c> word that ran this tick).</summary>
        bool IsMoving(object actor);
        /// <summary>The physics-material name of <paramref name="actor"/>'s collider (e.g.
        /// "Grass", "Stone"), or empty when it has no collider or no material assigned.</summary>
        string MaterialOf(object actor);
        /// <summary>True when nothing (walls included) blocks the straight line from <paramref name="from"/> to <paramref name="to"/>.</summary>
        bool HasLineOfSight(object from, object to);
        /// <summary>Finds a live scene actor by name (waypoints, goto targets, ...), or null.</summary>
        object FindActor(string name);
        /// <summary>Every live actor a contact poll may consider (active only, no templates).</summary>
        IReadOnlyList<object> Actors();
        /// <summary>True when the cursor is currently over the actor (point &amp; click hover).</summary>
        bool IsHovered(object actor);
        /// <summary>True when <paramref name="actor"/> carries a UI Button that is currently
        /// pressed (mouse held down on it).</summary>
        bool IsButtonPressed(object actor);
        /// <summary>True when <paramref name="actor"/> carries a UI Button that is disabled.</summary>
        bool IsButtonDisabled(object actor);
        /// <summary>The current fill of the ProgressBar on <paramref name="actor"/>, 0 to 1
        /// (0 when there is no progress bar).</summary>
        double ProgressOf(object actor);
        /// <summary>The current value of the Slider on <paramref name="actor"/>, 0 to 100
        /// (0 when there is no slider).</summary>
        double SliderOf(object actor);
        /// <summary>True when a particle named <paramref name="name"/> is playing on
        /// <paramref name="actor"/> (a child or the actor itself), used by
        /// <c>particle "&lt;name&gt;" is playing</c>.</summary>
        bool IsParticlePlaying(object actor, string name);
        /// <summary>True when <paramref name="child"/> is <paramref name="root"/> itself or a
        /// descendant in the scene hierarchy — so a contact poll skips the actor's own child
        /// colliders instead of testing them as foreign contact targets.</summary>
        bool IsDescendantOf(object child, object root);
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

        public double Distance2D(object from, object to)
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

        public bool IsMoving(object actor)
        {
            return false;
        }

        public string MaterialOf(object actor)
        {
            return "";
        }

        public bool HasLineOfSight(object from, object to)
        {
            return true;
        }

        public object FindActor(string name)
        {
            return null;
        }

        public IReadOnlyList<object> Actors()
        {
            return new object[0];
        }

        public bool IsHovered(object actor)
        {
            return false;
        }

        public bool IsButtonPressed(object actor)
        {
            return false;
        }

        public bool IsButtonDisabled(object actor)
        {
            return false;
        }

        public double ProgressOf(object actor)
        {
            return 0;
        }

        public double SliderOf(object actor)
        {
            return 0;
        }

        public bool IsParticlePlaying(object actor, string name)
        {
            return false;
        }

        public bool IsDescendantOf(object child, object root)
        {
            return ReferenceEquals(child, root);
        }
    }
}
