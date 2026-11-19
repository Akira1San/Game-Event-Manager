using System.Collections.Generic;
using FlaxEngine;

namespace Game
{
    /// <summary>
    /// Engine-side presentation for RTS selection (Task 18 Part A). The pure
    /// <see cref="Selection"/> class owns the state; this helper mirrors it onto the scene:
    /// a "SelectionRing" child actor per unit and optional control-group indicators.
    ///
    /// Authoring:
    /// - Put a child actor named <c>SelectionRing</c> under each selectable unit when the scene
    ///   wants the ground-ring look. A unit may instead (or also) be shown by the screen-space
    ///   boxes of Task 32 — <c>RTS_Test_0.scene</c> uses the boxes only, because this project has
    ///   no ring mesh or unlit material to author a ring with. A selection with neither look is
    ///   the one case worth a warning.
    /// - Optionally add a label actor named <c>ControlGroups</c> (shows "1 2 5") and/or
    ///   actors named <c>ControlGroup0</c> … <c>ControlGroup9</c> (shown while assigned).
    /// The keys themselves live in <see cref="EventsManager"/>: 1–0 recalls a group and
    /// Ctrl+1–0 assigns the current selection to it.
    /// </summary>
    public static class SelectionView
    {
        public const string RingActorName = "SelectionRing";
        public const string ControlGroupsLabel = "ControlGroups";

        /// <summary>Set once, the first time a selected unit has neither look.</summary>
        private static bool _warnedNoVisual;

        /// <summary>Shows or hides the selection ring under one actor.</summary>
        public static void SetRing(object target, bool on)
        {
            var actor = target as Actor;
            if (actor == null || !actor)
                return;
            Actor ring = FindRing(actor);
            if (ring != null)
            {
                ring.IsActive = on;
                return;
            }
            // No ring under this unit — which is fine when the scene draws the screen-space
            // boxes (Task 32), because the selection is still visible. With neither look a
            // click would seem to do nothing, so say once what to author.
            if (on && !SelectionBoxesView.CanDraw())
                WarnNoVisual(actor);
        }

        /// <summary>Refreshes the control-group indicators from the current selection state.</summary>
        public static void UpdateControlGroupIndicators(Selection selection)
        {
            if (selection == null)
                return;
            List<int> assigned = selection.AssignedGroups();

            Actor labelActor = Level.FindActor(ControlGroupsLabel);
            var label = labelActor == null ? null : labelActor as UIControl;
            var text = label == null ? null : label.Control as FlaxEngine.GUI.Label;
            if (text != null)
            {
                var parts = new List<string>();
                foreach (int group in assigned)
                    parts.Add(group.ToString());
                text.Text = string.Join(" ", parts);
                text.Visible = parts.Count > 0;
            }

            for (int group = 0; group < Selection.ControlGroupCount; group++)
            {
                Actor indicator = Level.FindActor("ControlGroup" + group);
                if (indicator != null)
                    indicator.IsActive = selection.IsControlGroupAssigned(group);
            }
        }

        /// <summary>
        /// Says once that a selected unit shows nothing: it has no <c>SelectionRing</c> child and
        /// the scene has no usable <c>SelectionBox</c> panel. One line per session, not one per
        /// unit — a box select of eight Marines should not print eight of these.
        /// </summary>
        private static void WarnNoVisual(Actor actor)
        {
            if (_warnedNoVisual)
                return;
            _warnedNoVisual = true;
            Debug.LogWarning("Selection: \"" + actor.Name + "\" has no " + RingActorName +
                " child and the scene has no \"" + SelectionBoxesView.ActorName + "\" panel, so " +
                "selecting it shows nothing. Add a " + RingActorName + " child (a thin plate under " +
                "the unit) or the " + SelectionBoxesView.ActorName + " UIControl.");
        }

        /// <summary>The first descendant named <c>SelectionRing</c>, or null.</summary>
        private static Actor FindRing(Actor actor)
        {
            foreach (Actor child in actor.Children)
            {
                if (child == null)
                    continue;
                if (child.Name == RingActorName)
                    return child;
                Actor nested = FindRing(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }
    }
}
