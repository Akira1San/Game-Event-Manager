using System;
using System.Collections.Generic;

namespace Game
{
    public interface IEventWorld
    {
        bool IsKeyHeld(string key);
        /// <summary>True while the named <c>GamepadButton</c> ("A", "LeftStickUp", …) is held on
        /// that gamepad. Player 1 reads pad 0 and player 2 pad 1, so two controllers drive the
        /// two local players (Task 16 input follow-ups).</summary>
        bool IsGamepadButtonHeld(int gamepad, string button);
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
        /// <summary>True when <paramref name="actor"/> is standing on walkable ground this tick:
        /// the character ground probe for a CharacterController, a short downward ray for any
        /// other body, and false while a `jump` is still in the air. Backs `me is on ground` /
        /// `me is airborne` (Task 7 G4 phase 3).</summary>
        bool IsOnGround(object actor);
        /// <summary>The physics-material name of <paramref name="actor"/>'s collider (e.g.
        /// "Grass", "Stone"), or empty when it has no collider or no material assigned.</summary>
        string MaterialOf(object actor);
        /// <summary>True when nothing (walls included) blocks the straight line from <paramref name="from"/> to <paramref name="to"/>.</summary>
        bool HasLineOfSight(object from, object to);
        /// <summary>Horizontal (XZ) distance from an actor to a world point. Sound positions have
        /// no actor, so `when me hears` measures to the point instead of to a body.</summary>
        double DistanceToPoint(object from, double x, double z);
        /// <summary>True when nothing solid blocks the line from <paramref name="from"/> to a world
        /// point — sound occlusion (a gunshot does not travel through a wall).</summary>
        bool HasLineOfSightToPoint(object from, double x, double z);
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
        /// <summary>True when the UI control on <paramref name="actor"/> currently has keyboard focus.</summary>
        bool IsFocused(object actor);
        /// <summary>The current text of the TextBox on <paramref name="actor"/> (empty when
        /// there is no text box).</summary>
        string TextOf(object actor);
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

        // ---- RTS state (Task 18 Part H) ----
        // The same live state the event language reads, exposed as one query surface: a HUD,
        // minimap or AI layer asks the world instead of reaching into Selection,
        // ResourceManager, FogOfWarManager, ConstructionManager or OrderManager directly.

        /// <summary>The actors currently selected, in click order.</summary>
        IReadOnlyList<object> GetSelection();

        /// <summary>How many actors are selected (0 when nothing is).</summary>
        int GetSelectedCount();

        /// <summary>One RTS resource's value by name (`Minerals`, `Supply Used`, …); an
        /// unset resource reads as 0, like a fresh scene.</summary>
        double GetResource(string name);

        /// <summary>True when <paramref name="playerId"/> can see <paramref name="actor"/> in
        /// the fog grid. Player 0 is the local player's grid (the view the bridge renders).</summary>
        bool IsRevealed(object actor, int playerId);

        /// <summary>Explicitly reveals a world circle for one player. The cells stay visible
        /// until <see cref="HideArea"/> or a reset, and are remembered as explored.</summary>
        void RevealArea(int playerId, double x, double z, double radius);

        /// <summary>Explicitly hides a world circle for one player.</summary>
        void HideArea(int playerId, double x, double z, double radius);

        /// <summary>Construction progress 0–100, or 0 for an actor that is not building.</summary>
        double GetBuildProgress(object building);

        /// <summary>The unit's order names, active order first (empty when it has none).</summary>
        IReadOnlyList<string> GetOrderQueue(object unit);
    }

    public class DefaultEventWorld : IEventWorld
    {
        public bool IsKeyHeld(string key)
        {
            return false;
        }

        public bool IsGamepadButtonHeld(int gamepad, string button)
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

        public bool IsOnGround(object actor)
        {
            // A world with no physics cannot answer, so it reports grounded — an on-ground gate
            // then behaves like a plain "is this thing there" instead of blocking the sheet.
            return true;
        }

        public string MaterialOf(object actor)
        {
            return "";
        }

        public bool HasLineOfSight(object from, object to)
        {
            return true;
        }

        public double DistanceToPoint(object from, double x, double z)
        {
            return double.MaxValue;
        }

        public bool HasLineOfSightToPoint(object from, double x, double z)
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

        public bool IsFocused(object actor)
        {
            return false;
        }

        public string TextOf(object actor)
        {
            return "";
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

        // ---- RTS state (Task 18 Part H) ----
        // A bare world has no economy, selection or vision of its own: every query answers
        // empty. Tests and tools that need real state implement IEventWorld (or drive an
        // EventRuntime, which owns it).

        public IReadOnlyList<object> GetSelection()
        {
            return Array.Empty<object>();
        }

        public int GetSelectedCount()
        {
            return 0;
        }

        public double GetResource(string name)
        {
            return 0;
        }

        public bool IsRevealed(object actor, int playerId)
        {
            return false;
        }

        public void RevealArea(int playerId, double x, double z, double radius)
        {
        }

        public void HideArea(int playerId, double x, double z, double radius)
        {
        }

        public double GetBuildProgress(object building)
        {
            return 0;
        }

        public IReadOnlyList<string> GetOrderQueue(object unit)
        {
            return Array.Empty<string>();
        }
    }
}
