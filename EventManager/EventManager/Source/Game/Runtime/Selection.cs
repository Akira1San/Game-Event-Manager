using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// Per-session RTS selection state: which actors are selected and which control
    /// groups (1–0) they belong to. This is pure logic with no Flax types, so the event
    /// runtime and the tests can drive it directly; the engine bridge subscribes to the
    /// callbacks to show/hide selection rings and control-group indicators.
    /// </summary>
    public class Selection
    {
        /// <summary>How many control groups exist (keys 1–9 and 0).</summary>
        public const int ControlGroupCount = 10;

        private readonly EventRuntime runtime;
        private readonly List<object> selected = new List<object>();
        private readonly List<object>[] controlGroups = new List<object>[ControlGroupCount];
        /// <summary>Key state from the previous tick, used to raise <c>ControlGroupPressed</c> once.</summary>
        private readonly bool[] controlGroupHeldLastTick = new bool[ControlGroupCount];

        /// <summary>An actor joined the selection (the bridge turns its ring on).</summary>
        public Action<object> OnActorSelected;
        /// <summary>An actor left the selection (the bridge turns its ring off).</summary>
        public Action<object> OnActorDeselected;
        /// <summary>The selection identity or count changed.</summary>
        public Action OnSelectionChanged;
        /// <summary>A control group was assigned, cleared, or had members added/removed.</summary>
        public Action OnControlGroupsChanged;

        public Selection(EventRuntime runtime)
        {
            this.runtime = runtime;
            for (int group = 0; group < ControlGroupCount; group++)
                controlGroups[group] = new List<object>();
        }

        /// <summary>Selected actors, in the order they were selected.</summary>
        public IReadOnlyList<object> Selected => selected;

        /// <summary>How many actors are selected.</summary>
        public int Count => selected.Count;

        public bool IsSelected(object actor)
        {
            return actor != null && selected.Contains(actor);
        }

        /// <summary>Adds one actor to the selection. Returns true when it was newly selected.</summary>
        public bool Select(object actor)
        {
            if (actor == null || actor is GlobalHost || selected.Contains(actor))
                return false;
            selected.Add(actor);
            RaiseSelected(actor);
            RaiseSelectionChanged();
            return true;
        }

        /// <summary>Removes one actor from the selection. Returns true when it was selected.</summary>
        public bool Deselect(object actor)
        {
            if (actor == null || !selected.Remove(actor))
                return false;
            RaiseDeselected(actor);
            RaiseSelectionChanged();
            return true;
        }

        /// <summary>Clears the selection, raising Deselected for every actor that was in it.</summary>
        public void Clear()
        {
            if (selected.Count == 0)
                return;
            var previous = selected.ToArray();
            selected.Clear();
            foreach (object actor in previous)
                RaiseDeselected(actor);
            RaiseSelectionChanged();
        }

        /// <summary>
        /// Replaces the whole selection with <paramref name="actors"/>. Used by box select,
        /// <c>select all &lt;unit type&gt;</c>, <c>select units near me</c>, and control
        /// group recall. Raises Selected/Deselected only for the difference, then one
        /// SelectionChanged for the whole operation.
        /// </summary>
        public void ReplaceSelection(IEnumerable<object> actors)
        {
            var wanted = new List<object>();
            if (actors != null)
            {
                foreach (object actor in actors)
                {
                    if (actor == null || actor is GlobalHost || wanted.Contains(actor))
                        continue;
                    wanted.Add(actor);
                }
            }

            bool changed = false;
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                if (wanted.Contains(selected[i]))
                    continue;
                object removed = selected[i];
                selected.RemoveAt(i);
                changed = true;
                RaiseDeselected(removed);
            }
            foreach (object actor in wanted)
            {
                if (selected.Contains(actor))
                    continue;
                selected.Add(actor);
                changed = true;
                RaiseSelected(actor);
            }
            if (changed)
                RaiseSelectionChanged();
        }

        /// <summary>Forgets an actor entirely (destroyed/removed): deselects it and drops it
        /// from every control group without raising selection events for the dead object.</summary>
        public void Forget(object actor)
        {
            if (actor == null)
                return;
            selected.Remove(actor);
            bool groupsChanged = false;
            for (int group = 0; group < ControlGroupCount; group++)
                groupsChanged |= controlGroups[group].Remove(actor);
            if (groupsChanged)
                OnControlGroupsChanged?.Invoke();
        }

        /// <summary>True for a control group number in the supported 0–9 range.</summary>
        public static bool IsValidGroup(int group)
        {
            return group >= 0 && group < ControlGroupCount;
        }

        /// <summary>The members of one control group (1–0).</summary>
        public IReadOnlyList<object> ControlGroup(int group)
        {
            return IsValidGroup(group) ? controlGroups[group] : Array.Empty<object>();
        }

        public bool IsControlGroupAssigned(int group)
        {
            return IsValidGroup(group) && controlGroups[group].Count > 0;
        }

        public bool IsInControlGroup(object actor, int group)
        {
            return actor != null && IsValidGroup(group) && controlGroups[group].Contains(actor);
        }

        /// <summary>Replaces a control group's members (what <c>set my control group to &lt;n&gt;</c>
        /// does on the global sheet: the current selection becomes the group).</summary>
        public void SetControlGroup(int group, IEnumerable<object> actors)
        {
            if (!IsValidGroup(group))
                return;
            var members = controlGroups[group];
            members.Clear();
            if (actors != null)
            {
                foreach (object actor in actors)
                {
                    if (actor == null || actor is GlobalHost || members.Contains(actor))
                        continue;
                    members.Add(actor);
                }
            }
            OnControlGroupsChanged?.Invoke();
        }

        /// <summary>Adds one actor to a group (what <c>set my control group to &lt;n&gt;</c>
        /// does on a unit's own sheet).</summary>
        public void AddToControlGroup(int group, object actor)
        {
            if (!IsValidGroup(group) || actor == null || actor is GlobalHost)
                return;
            var members = controlGroups[group];
            if (members.Contains(actor))
                return;
            members.Add(actor);
            OnControlGroupsChanged?.Invoke();
        }

        public void RemoveFromControlGroup(int group, object actor)
        {
            if (!IsValidGroup(group) || actor == null)
                return;
            if (controlGroups[group].Remove(actor))
                OnControlGroupsChanged?.Invoke();
        }

        public void ClearControlGroup(int group)
        {
            if (!IsValidGroup(group) || controlGroups[group].Count == 0)
                return;
            controlGroups[group].Clear();
            OnControlGroupsChanged?.Invoke();
        }

        /// <summary>Group numbers that currently hold at least one actor, in 1–0 order.</summary>
        public List<int> AssignedGroups()
        {
            var found = new List<int>();
            for (int group = 0; group < ControlGroupCount; group++)
                if (controlGroups[group].Count > 0)
                    found.Add(group);
            return found;
        }

        /// <summary>Recalls a control group: replaces the selection with its members.
        /// An empty group leaves the selection alone (pressing an unassigned key does nothing).</summary>
        public bool SelectControlGroup(int group)
        {
            if (!IsValidGroup(group) || controlGroups[group].Count == 0)
                return false;
            ReplaceSelection(controlGroups[group]);
            return true;
        }

        /// <summary>The Flax key name for a control group (group 1 → "D1", group 0 → "D0").</summary>
        public static string KeyForGroup(int group)
        {
            return "D" + group;
        }

        /// <summary>Parses "D1"/"1" into a control group number.</summary>
        public static bool TryGroupKey(string key, out int group)
        {
            group = -1;
            if (string.IsNullOrEmpty(key))
                return false;
            string name = key.Trim();
            if (name.Length == 2 && (name[0] == 'D' || name[0] == 'd') && char.IsDigit(name[1]))
            {
                group = name[1] - '0';
                return true;
            }
            if (name.Length == 1 && char.IsDigit(name[0]))
            {
                group = name[0] - '0';
                return true;
            }
            return false;
        }

        /// <summary>
        /// Watches the 1–0 keys once per tick and raises <c>ControlGroupPressed</c> on the
        /// enter edge. Held is a separate, polled trigger (<c>ControlGroupHeld</c>).
        /// </summary>
        public void PollControlGroupKeys(IEventWorld world)
        {
            if (world == null)
                return;
            for (int group = 0; group < ControlGroupCount; group++)
            {
                bool held = world.IsKeyHeld(KeyForGroup(group));
                if (held && !controlGroupHeldLastTick[group])
                {
                    runtime?.RaisePushAll(new PushSignal
                    {
                        Kind = TriggerKind.ControlGroupPressed,
                        Name = group.ToString(),
                    });
                }
                controlGroupHeldLastTick[group] = held;
            }
        }

        /// <summary>Silently clears every selection and control group (scene teardown).</summary>
        public void Reset()
        {
            selected.Clear();
            for (int group = 0; group < ControlGroupCount; group++)
            {
                controlGroups[group].Clear();
                controlGroupHeldLastTick[group] = false;
            }
        }

        /// <summary>Notifies the engine view and broadcasts <c>when selection changes</c>.</summary>
        private void RaiseSelectionChanged()
        {
            OnSelectionChanged?.Invoke();
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.SelectionChanged,
                Name = "selection",
            });
        }

        private void RaiseSelected(object actor)
        {
            OnActorSelected?.Invoke(actor);
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.Selected,
                Other = actor,
                Name = runtime.World.NameOf(actor),
            });
        }

        private void RaiseDeselected(object actor)
        {
            OnActorDeselected?.Invoke(actor);
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.Deselected,
                Other = actor,
                Name = runtime.World.NameOf(actor),
            });
        }
    }
}
