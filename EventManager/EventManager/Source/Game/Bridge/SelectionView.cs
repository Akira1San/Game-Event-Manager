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
    /// - Put a child actor named <c>SelectionRing</c> under each selectable unit.
    /// - Optionally add a label actor named <c>ControlGroups</c> (shows "1 2 5") and/or
    ///   actors named <c>ControlGroup0</c> … <c>ControlGroup9</c> (shown while assigned).
    /// The keys themselves live in <see cref="EventsManager"/>: 1–0 recalls a group and
    /// Ctrl+1–0 assigns the current selection to it.
    /// </summary>
    public static class SelectionView
    {
        public const string RingActorName = "SelectionRing";
        public const string ControlGroupsLabel = "ControlGroups";

        /// <summary>Shows or hides the selection ring under one actor.</summary>
        public static void SetRing(object target, bool on)
        {
            var actor = target as Actor;
            if (actor == null || !actor)
                return;
            Actor ring = FindRing(actor);
            if (ring != null)
                ring.IsActive = on;
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
