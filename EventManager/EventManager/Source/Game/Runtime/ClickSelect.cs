using System;

namespace Game
{
    /// <summary>
    /// What a click does to the selection (Task 29 Part A).
    /// </summary>
    public enum ClickSelectMode
    {
        /// <summary>Nothing was selected by the click: the actor has no event sheet, it opted
        /// out with <c>my Selectable</c>, or the click only wants to clear the selection.</summary>
        None,
        /// <summary>Select just this actor, dropping everyone else (a plain left click).</summary>
        Replace,
        /// <summary>Add this actor to the selection (Shift + left click).</summary>
        Add,
        /// <summary>Remove this actor from the selection (Shift + left click on a selected one).</summary>
        Remove,
    }

    /// <summary>
    /// StarCraft-style click selection (Task 29 Part A): the decision half, kept free of Flax
    /// types so the tests drive it directly. The bridge does the picking (UI in screen space,
    /// everything else with a camera raycast) and then hands the actor here.
    ///
    /// The rule is content-driven, never a name list: an actor is clickable when it has an
    /// event sheet, because that sheet is what reacts to being selected — a ground plane, a
    /// prop, a bullet or a UIControl has no sheet and is never selected by a click. A sheet
    /// that wants to be scenery with a sheet writes <c>set my Selectable to 0</c>.
    ///
    /// The click trigger still fires exactly as before (the bridge raises it right after the
    /// selection is applied), so an authored <c>when me is clicked</c> keeps working — and it
    /// already sees <c>me is selected</c>, which is what a hand-written click handler would
    /// have needed to write itself.
    /// </summary>
    public static class ClickSelect
    {
        /// <summary>The per-actor variable that opts a sheet out of click selection.</summary>
        public const string SelectableVariable = "Selectable";

        /// <summary>Every spelling the language accepts.</summary>
        public static readonly string[] VariableNames = { "Selectable", "selectable" };

        /// <summary>True for either spelling of the per-actor variable.</summary>
        public static bool IsVariableName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (string candidate in VariableNames)
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Whether a click on this actor may select it. An actor with a sheet is selectable;
        /// one without a sheet never is (that is the ground, a prop, a bullet, a UIControl).
        /// A sheet that wrote <c>my Selectable</c> overrules that default in either direction.
        /// </summary>
        public static bool IsSelectable(EventVariables variables, object actor, bool hasSheet)
        {
            if (actor == null)
                return false;
            if (variables != null)
            {
                foreach (string name in VariableNames)
                {
                    if (!variables.HasLocal(actor, name))
                        continue;
                    return ReadFlag(variables.GetLocal(actor, name));
                }
            }
            return hasSheet;
        }

        /// <summary>
        /// What a click should do. <paramref name="shift"/> is Shift held at click time, which
        /// is the StarCraft add/remove modifier; a click on an actor that may not be selected
        /// leaves the selection to the caller (which clears it, as clicking the ground does).
        /// </summary>
        public static ClickSelectMode Resolve(bool hasSheet, bool selectable, bool selected, bool shift)
        {
            if (!hasSheet || !selectable)
                return ClickSelectMode.None;
            if (shift)
                return selected ? ClickSelectMode.Remove : ClickSelectMode.Add;
            return ClickSelectMode.Replace;
        }

        /// <summary>True when a click that selected nothing should clear the selection: a plain
        /// left click on bare ground does, Shift + click keeps the selection for an add.</summary>
        public static bool ClearsOnGround(bool shift)
        {
            return !shift;
        }

        /// <summary>
        /// True when two actors are the same kind of unit for a double click — the same
        /// <c>.events</c> file drives both sheets, which is what "all Marines on screen" means
        /// without a hard-coded unit table.
        /// </summary>
        public static bool SameKind(string sourceA, string sourceB)
        {
            if (string.IsNullOrEmpty(sourceA) || string.IsNullOrEmpty(sourceB))
                return false;
            return string.Equals(sourceA, sourceB, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A written <c>my Selectable</c> read as a flag: 0, false, "no" and "off"
        /// all mean "not selectable", everything else means selectable.</summary>
        private static bool ReadFlag(ValueBox value)
        {
            if (value == null || value.IsNone())
                return false;
            if (value.Kind == ValueKind.Text)
            {
                string text = (value.AsText() ?? "").Trim();
                if (text.Length == 0)
                    return false;
                return !string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(text, "off", StringComparison.OrdinalIgnoreCase);
            }
            return value.AsBoolean();
        }
    }
}
