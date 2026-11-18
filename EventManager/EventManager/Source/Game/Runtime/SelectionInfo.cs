using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// Which part of the selection an order runs on: <c>selected units ...</c>,
    /// <c>selected buildings ...</c>, or every selected actor for a bare <c>selected ...</c>.
    /// </summary>
    public enum SelectionOrderScope
    {
        All,
        Units,
        Buildings,
    }

    /// <summary>
    /// The selection's shape as data (Task 29 Part D): who is in it, which of them is a unit or a
    /// building, and whether they are all one kind. Pure logic with no Flax types, so a command
    /// card can branch on it from the event language and the tests can drive it directly.
    /// A "building" is whatever the runtime's build registry, lift-off state or production queue
    /// knows about — this class only asks the predicate it is given.
    /// </summary>
    public static class SelectionInfo
    {
        /// <summary>The first selected actor that is not a building, or null.</summary>
        public static object FirstUnit(IReadOnlyList<object> selected, Func<object, bool> isBuilding)
        {
            if (selected == null)
                return null;
            foreach (object actor in selected)
            {
                if (actor == null)
                    continue;
                if (isBuilding != null && isBuilding(actor))
                    continue;
                return actor;
            }
            return null;
        }

        /// <summary>The first selected actor that is a building, or null.</summary>
        public static object FirstBuilding(IReadOnlyList<object> selected, Func<object, bool> isBuilding)
        {
            if (selected == null || isBuilding == null)
                return null;
            foreach (object actor in selected)
            {
                if (actor != null && isBuilding(actor))
                    return actor;
            }
            return null;
        }

        /// <summary>
        /// The actors an order prefixed with <c>selected</c> applies to, in selection order.
        /// <see cref="SelectionOrderScope.All"/> keeps both units and buildings, so a mixed
        /// selection can take one command.
        /// </summary>
        public static List<object> ForScope(IReadOnlyList<object> selected, SelectionOrderScope scope,
            Func<object, bool> isBuilding)
        {
            var targets = new List<object>();
            if (selected == null)
                return targets;
            foreach (object actor in selected)
            {
                if (actor == null || targets.Contains(actor))
                    continue;
                bool building = isBuilding != null && isBuilding(actor);
                if (scope == SelectionOrderScope.Units && building)
                    continue;
                if (scope == SelectionOrderScope.Buildings && !building)
                    continue;
                targets.Add(actor);
            }
            return targets;
        }

        /// <summary>
        /// True when something is selected and none of it is a building — the "show the unit
        /// card" test. An empty selection answers false, so a card never draws for nothing.
        /// </summary>
        public static bool AllUnits(IReadOnlyList<object> selected, Func<object, bool> isBuilding)
        {
            if (selected == null || selected.Count == 0)
                return false;
            foreach (object actor in selected)
            {
                if (actor == null)
                    return false;
                if (isBuilding != null && isBuilding(actor))
                    return false;
            }
            return true;
        }

        /// <summary>True when something is selected and every part of it is a building.</summary>
        public static bool AllBuildings(IReadOnlyList<object> selected, Func<object, bool> isBuilding)
        {
            if (selected == null || selected.Count == 0 || isBuilding == null)
                return false;
            foreach (object actor in selected)
            {
                if (actor == null || !isBuilding(actor))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// True when something is selected and every part of it is the same kind — a shared card
        /// can then show that kind's buttons. <paramref name="kindOf"/> answers an actor's kind
        /// (the sheet's <c>.events</c> name), so two Marines match however they are named.
        /// </summary>
        public static bool AllKind(IReadOnlyList<object> selected, Func<object, string> kindOf, string kind)
        {
            if (selected == null || selected.Count == 0 || kindOf == null || string.IsNullOrEmpty(kind))
                return false;
            foreach (object actor in selected)
            {
                if (actor == null || !SameKind(kindOf(actor), kind))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Two kind names compared the way a player writes them: case-insensitively and without a
        /// trailing <c>.events</c>, so <c>selection is all Marine</c> matches a sheet loaded from
        /// <c>Marine.events</c>. An empty kind never matches.
        /// </summary>
        public static bool SameKind(string kindA, string kindB)
        {
            string a = NormalizeKind(kindA);
            string b = NormalizeKind(kindB);
            return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The bare kind name: trimmed, with a trailing <c>.events</c> removed.</summary>
        public static string NormalizeKind(string kind)
        {
            string text = kind == null ? "" : kind.Trim();
            if (text.EndsWith(".events", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - ".events".Length).Trim();
            return text;
        }

        /// <summary>
        /// <c>selected units</c> / <c>selected buildings</c> reading for the word after
        /// <c>selected</c>; false for anything else, which leaves the bare <c>selected &lt;order&gt;</c>
        /// (the whole selection) form.
        /// </summary>
        public static bool TryScopeWord(string word, out SelectionOrderScope scope)
        {
            scope = SelectionOrderScope.All;
            if (string.Equals(word, "units", StringComparison.OrdinalIgnoreCase))
            {
                scope = SelectionOrderScope.Units;
                return true;
            }
            if (string.Equals(word, "buildings", StringComparison.OrdinalIgnoreCase))
            {
                scope = SelectionOrderScope.Buildings;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The order verbs <c>selected ...</c> may prefix. Anything else (a <c>set</c>, a
        /// <c>print</c>, a camera move) has no per-actor meaning, so the parser refuses it with a
        /// message rather than running it once per selected actor.
        /// </summary>
        public static bool AllowsOrderScope(ActionVerb verb)
        {
            switch (verb)
            {
                case ActionVerb.Move:
                case ActionVerb.Goto:
                case ActionVerb.StopOrder:
                case ActionVerb.HoldPosition:
                case ActionVerb.AttackOrder:
                case ActionVerb.AttackMove:
                case ActionVerb.Gather:
                case ActionVerb.Repair:
                case ActionVerb.Train:
                case ActionVerb.CancelTraining:
                case ActionVerb.Research:
                case ActionVerb.CancelResearch:
                case ActionVerb.SetRallyPoint:
                case ActionVerb.ClearOrders:
                case ActionVerb.AddOrderToQueue:
                case ActionVerb.Fly:
                case ActionVerb.LiftOff:
                case ActionVerb.Land:
                case ActionVerb.Cast:
                    return true;
            }
            return false;
        }
    }
}
