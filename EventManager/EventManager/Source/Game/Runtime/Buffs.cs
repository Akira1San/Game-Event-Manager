using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>What a second copy of the same buff does (Task 27 Part C).</summary>
    public enum BuffRule
    {
        /// <summary>The running buff gets a fresh timer and the newest modifiers (the default).</summary>
        Refresh,
        /// <summary>A second copy runs alongside the first, so its modifiers add up.</summary>
        Stack,
        /// <summary>Ignored while the buff is already running.</summary>
        Ignore,
    }

    /// <summary>One stat change a buff makes: <c>adding 40 to my Speed</c> / <c>multiplying my Speed by 1.5</c>.</summary>
    public class BuffModifier
    {
        public string Stat = "";
        /// <summary>Added to the captured base value.</summary>
        public double Amount;
        /// <summary>Multiplies the captured base value (1 = no change).</summary>
        public double Multiplier = 1;
        public bool HasAmount;
        public bool HasMultiplier;
    }

    /// <summary>One running buff on one actor.</summary>
    public class BuffInstance
    {
        public string Name = "";
        public double Duration;
        public double Remaining;
        public BuffRule Rule = BuffRule.Refresh;
        /// <summary>The actor that applied it, handed to `when buff ... starts` as `other`.</summary>
        public object Source;
        public List<BuffModifier> Modifiers = new List<BuffModifier>();

        public BuffInstance Copy()
        {
            var copy = new BuffInstance
            {
                Name = Name,
                Duration = Duration,
                Remaining = Remaining,
                Rule = Rule,
                Source = Source,
            };
            foreach (BuffModifier modifier in Modifiers)
            {
                copy.Modifiers.Add(new BuffModifier
                {
                    Stat = modifier.Stat,
                    Amount = modifier.Amount,
                    Multiplier = modifier.Multiplier,
                    HasAmount = modifier.HasAmount,
                    HasMultiplier = modifier.HasMultiplier,
                });
            }
            return copy;
        }
    }

    /// <summary>A buff that ran out (or was removed), so the runtime can push `when buff ... ends`.</summary>
    public class BuffEnd
    {
        public object Actor;
        public string Name = "";
        public object Source;
    }

    /// <summary>The word shape of a buff modifier while it is still being parsed (see ActionWords).</summary>
    public class BuffModifierWord
    {
        public string Stat = "";
        public ValueNode Value;
        public double Number;
        /// <summary>True for <c>multiplying ... by</c>, false for <c>adding ... to</c>.</summary>
        public bool Multiply;
    }

    /// <summary>
    /// Buffs and debuffs (Task 27 Part C): named, timed stat changes with a stack rule.
    ///
    /// The manager owns the aggregate. The first buff that touches a stat records the actor's
    /// current value (<c>my BaseSpeed</c>), and every change rewrites <c>my Speed</c> as
    /// <c>base × Π multipliers + Σ amounts</c> across the buffs running on that actor. When the
    /// last buff on a stat ends the base is written back, so overlapping buffs restore exactly
    /// what was there before instead of fighting over the value. A sheet that never uses a buff
    /// keeps hand-writing <c>my Speed</c> exactly as before — the manager stores nothing for it.
    ///
    /// No Flax types, so the runtime and the tests drive it directly.
    /// </summary>
    public class Buffs
    {
        /// <summary>How long `add buff` lasts when the line gives no duration.</summary>
        public const double DefaultDuration = 8.0;

        /// <summary>Prefix of the value form: <c>my "stim" time left</c> → <c>buff stim time left</c>.</summary>
        public const string TimeLeftPrefix = "buff ";
        public const string TimeLeftSuffix = " time left";

        private readonly Dictionary<object, List<BuffInstance>> _active =
            new Dictionary<object, List<BuffInstance>>();
        /// <summary>Per actor, per stat: the value to return to when the buffs end.</summary>
        private readonly Dictionary<object, Dictionary<string, double>> _bases =
            new Dictionary<object, Dictionary<string, double>>();
        /// <summary>A sheet's own <c>my BaseSpeed</c>, kept so ending a buff restores it too.</summary>
        private readonly Dictionary<object, Dictionary<string, ValueBox>> _savedBases =
            new Dictionary<object, Dictionary<string, ValueBox>>();
        /// <summary>Stats that existed before the first buff — the others are removed again.</summary>
        private readonly Dictionary<object, HashSet<string>> _existed =
            new Dictionary<object, HashSet<string>>();

        /// <summary>The variable the parser builds for <c>my "stim" time left</c>.</summary>
        public static string TimeLeftName(string buffName)
        {
            return TimeLeftPrefix + (buffName ?? "") + TimeLeftSuffix;
        }

        /// <summary>True when a variable name is the computed time-left read, with the buff name out.</summary>
        public static bool TryReadTimeLeft(string variable, out string buffName)
        {
            buffName = "";
            if (string.IsNullOrEmpty(variable))
                return false;
            if (!variable.StartsWith(TimeLeftPrefix, StringComparison.OrdinalIgnoreCase) ||
                !variable.EndsWith(TimeLeftSuffix, StringComparison.OrdinalIgnoreCase))
                return false;
            int length = variable.Length - TimeLeftPrefix.Length - TimeLeftSuffix.Length;
            if (length <= 0)
                return false;
            buffName = variable.Substring(TimeLeftPrefix.Length, length).Trim();
            return buffName.Length > 0;
        }

        /// <summary>Health stats keep one spelling wherever they are read (`Armor`, `MaxHealth`).</summary>
        public static string CanonicalStat(string stat)
        {
            if (string.IsNullOrEmpty(stat))
                return "";
            return HealthManager.TryStat(stat, out string canonical) ? canonical : stat;
        }

        public bool IsActive(object actor, string name)
        {
            return CountOf(actor, name) > 0;
        }

        /// <summary>How many copies of a buff are running on an actor.</summary>
        public int CountOf(object actor, string name)
        {
            if (actor == null || string.IsNullOrEmpty(name) || !_active.TryGetValue(actor, out var list))
                return 0;
            int count = 0;
            foreach (BuffInstance buff in list)
                if (Same(buff.Name, name))
                    count++;
            return count;
        }

        /// <summary>
        /// Seconds left on a buff: the longest-running copy, so a stacked buff's bar shows the
        /// copy that still has time on it. 0 when nothing with that name is running.
        /// </summary>
        public double TimeLeft(object actor, string name)
        {
            if (actor == null || string.IsNullOrEmpty(name) || !_active.TryGetValue(actor, out var list))
                return 0;
            double longest = 0;
            foreach (BuffInstance buff in list)
                if (Same(buff.Name, name) && buff.Remaining > longest)
                    longest = buff.Remaining;
            return longest;
        }

        /// <summary>Every buff name running on an actor, in the order they started.</summary>
        public List<string> ActiveNames(object actor)
        {
            var names = new List<string>();
            if (actor == null || !_active.TryGetValue(actor, out var list))
                return names;
            foreach (BuffInstance buff in list)
                if (!names.Contains(buff.Name))
                    names.Add(buff.Name);
            return names;
        }

        /// <summary>The actors with at least one buff running.</summary>
        public List<object> ActiveActors()
        {
            return new List<object>(_active.Keys);
        }

        /// <summary>
        /// Starts a buff. <paramref name="started"/> answers whether a fresh copy began running —
        /// a refresh of an already-running buff leaves it false, so <c>when buff ... starts</c>
        /// fires for the first stim but not for the second press. Returns false when the buff is
        /// not running afterwards (the ignore rule) or the arguments make no sense.
        /// </summary>
        public bool Add(EventVariables variables, object actor, BuffInstance buff, out bool started)
        {
            started = false;
            if (variables == null || actor == null || buff == null || string.IsNullOrEmpty(buff.Name))
                return false;
            if (buff.Duration <= 0)
                buff.Duration = DefaultDuration;
            if (buff.Remaining <= 0)
                buff.Remaining = buff.Duration;

            if (!_active.TryGetValue(actor, out var list))
            {
                list = new List<BuffInstance>();
                _active[actor] = list;
            }

            var existing = new List<BuffInstance>();
            foreach (BuffInstance running in list)
                if (Same(running.Name, buff.Name))
                    existing.Add(running);

            if (existing.Count > 0)
            {
                if (buff.Rule == BuffRule.Ignore)
                    return false;
                if (buff.Rule == BuffRule.Refresh)
                {
                    // One copy stays: the newest numbers on a fresh timer.
                    for (int i = 1; i < existing.Count; i++)
                        list.Remove(existing[i]);
                    BuffInstance current = existing[0];
                    current.Duration = buff.Duration;
                    current.Remaining = buff.Duration;
                    current.Source = buff.Source;
                    current.Modifiers = buff.Copy().Modifiers;
                    foreach (BuffModifier modifier in current.Modifiers)
                        CaptureStat(variables, actor, modifier.Stat);
                    Recompute(variables, actor);
                    return true;
                }
            }

            list.Add(buff.Copy());
            foreach (BuffModifier modifier in buff.Modifiers)
                CaptureStat(variables, actor, modifier.Stat);
            Recompute(variables, actor);
            started = true;
            return true;
        }

        /// <summary>Drops every copy of one buff, restoring the stats it touched.</summary>
        public bool Remove(EventVariables variables, object actor, string name)
        {
            if (actor == null || string.IsNullOrEmpty(name) || !_active.TryGetValue(actor, out var list))
                return false;
            int removed = list.RemoveAll(buff => Same(buff.Name, name));
            if (removed == 0)
                return false;
            Recompute(variables, actor);
            return true;
        }

        /// <summary>
        /// Advances every buff; each copy that runs out is dropped and reported, and the stats it
        /// touched are restored. Returns the ends so the caller can push `when buff ... ends`
        /// before its own tick's drain.
        /// </summary>
        public List<BuffEnd> Tick(EventVariables variables, double dt)
        {
            var ended = new List<BuffEnd>();
            if (dt <= 0 || _active.Count == 0)
                return ended;

            var actors = new List<object>(_active.Keys);
            foreach (object actor in actors)
            {
                if (!_active.TryGetValue(actor, out var list))
                    continue;
                bool touched = false;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    BuffInstance buff = list[i];
                    buff.Remaining -= dt;
                    if (buff.Remaining > 0)
                        continue;
                    ended.Add(new BuffEnd { Actor = actor, Name = buff.Name, Source = buff.Source });
                    list.RemoveAt(i);
                    touched = true;
                }
                if (touched)
                    Recompute(variables, actor);
            }
            return ended;
        }

        /// <summary>Forgets one actor's buffs, putting every stat it touched back (death, despawn).</summary>
        public void Clear(EventVariables variables, object actor)
        {
            if (actor == null)
                return;
            if (variables != null)
                RestoreAll(variables, actor);
            Forget(actor);
        }

        /// <summary>A scene restart: every buff ends and every actor gets its own numbers back.</summary>
        public void Reset(EventVariables variables)
        {
            if (variables != null)
            {
                var actors = new List<object>(_bases.Keys);
                foreach (object actor in actors)
                    RestoreAll(variables, actor);
            }
            _active.Clear();
            _bases.Clear();
            _savedBases.Clear();
            _existed.Clear();
        }

        /// <summary>
        /// The stat a buff writes on first contact: its current value becomes the base, the base is
        /// also written as <c>my Base&lt;Stat&gt;</c> so a sheet can read it, and a sheet's own
        /// <c>my Base&lt;Stat&gt;</c> is remembered for when the buff ends.
        /// </summary>
        private void CaptureStat(EventVariables variables, object actor, string rawStat)
        {
            string stat = CanonicalStat(rawStat);
            if (stat.Length == 0)
                return;
            if (!_bases.TryGetValue(actor, out var map))
            {
                map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                _bases[actor] = map;
            }
            if (map.ContainsKey(stat))
                return;

            bool existed = variables.HasLocal(actor, stat);
            double current = existed ? variables.GetLocal(actor, stat).AsNumber() : 0;
            map[stat] = current;
            if (existed)
            {
                if (!_existed.TryGetValue(actor, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _existed[actor] = set;
                }
                set.Add(stat);
            }
            string baseName = "Base" + stat;
            if (variables.HasLocal(actor, baseName))
            {
                if (!_savedBases.TryGetValue(actor, out var saved))
                {
                    saved = new Dictionary<string, ValueBox>(StringComparer.OrdinalIgnoreCase);
                    _savedBases[actor] = saved;
                }
                saved[stat] = ValueBox.Copy(variables.GetLocal(actor, baseName));
            }
            variables.SetLocal(actor, baseName, ValueBox.OfNumber(current));
        }

        /// <summary>Rewrites every buffed stat from its captured base, or restores the ones let go.</summary>
        private void Recompute(EventVariables variables, object actor)
        {
            if (variables == null || actor == null)
                return;
            if (!_active.TryGetValue(actor, out var list) || list.Count == 0)
            {
                RestoreAll(variables, actor);
                Forget(actor);
                return;
            }
            if (!_bases.TryGetValue(actor, out var map) || map.Count == 0)
                return;

            var stats = new List<string>(map.Keys);
            foreach (string stat in stats)
            {
                double multiplier = 1;
                double amount = 0;
                bool touched = false;
                foreach (BuffInstance buff in list)
                {
                    foreach (BuffModifier modifier in buff.Modifiers)
                    {
                        if (!Same(CanonicalStat(modifier.Stat), stat))
                            continue;
                        touched = true;
                        if (modifier.HasMultiplier)
                            multiplier *= modifier.Multiplier;
                        if (modifier.HasAmount)
                            amount += modifier.Amount;
                    }
                }
                if (!touched)
                {
                    RestoreStat(variables, actor, stat);
                    continue;
                }
                variables.SetLocal(actor, stat, ValueBox.OfNumber(map[stat] * multiplier + amount));
            }
        }

        private void RestoreAll(EventVariables variables, object actor)
        {
            if (!_bases.TryGetValue(actor, out var map))
                return;
            var stats = new List<string>(map.Keys);
            foreach (string stat in stats)
                RestoreStat(variables, actor, stat);
        }

        /// <summary>Puts one stat back the way it was before its first buff.</summary>
        private void RestoreStat(EventVariables variables, object actor, string stat)
        {
            if (!_bases.TryGetValue(actor, out var map) || !map.TryGetValue(stat, out double baseValue))
                return;
            bool existed = _existed.TryGetValue(actor, out var set) && set.Contains(stat);
            if (existed)
                variables.SetLocal(actor, stat, ValueBox.OfNumber(baseValue));
            else
                variables.RemoveLocal(actor, stat);

            string baseName = "Base" + stat;
            if (_savedBases.TryGetValue(actor, out var saved) && saved.TryGetValue(stat, out ValueBox original))
                variables.SetLocal(actor, baseName, ValueBox.Copy(original));
            else
                variables.RemoveLocal(actor, baseName);
            map.Remove(stat);
        }

        private void Forget(object actor)
        {
            _active.Remove(actor);
            _bases.Remove(actor);
            _savedBases.Remove(actor);
            _existed.Remove(actor);
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Runs the two buff words (Task 27 Part C) against the runtime's <see cref="Buffs"/>: it
    /// resolves the target, turns the parsed modifiers into real numbers, and raises
    /// <c>when buff ... starts</c> / <c>... ends</c> on the buffed actor's sheet. Pure, so tests
    /// drive the whole word-to-stat path without the engine.
    /// </summary>
    public static class BuffCommands
    {
        public static bool ExecuteAdd(EventContext context, ActionSpec spec)
        {
            if (context == null || spec == null)
                return false;
            EventRuntime runtime = context.Runtime;
            if (runtime == null)
                return false;
            object target = ResolveTarget(context, spec.To);
            if (target == null)
                return false;

            var buff = new BuffInstance
            {
                Name = spec.BuffName,
                Duration = spec.Duration > 0 ? spec.Duration : Buffs.DefaultDuration,
                Remaining = spec.Duration > 0 ? spec.Duration : Buffs.DefaultDuration,
                Rule = spec.BuffRule,
                Source = context.Me,
            };
            foreach (BuffModifierWord word in spec.BuffModifiers)
            {
                if (string.IsNullOrEmpty(word.Stat))
                    continue;
                double value = word.Value != null ? context.Evaluate(word.Value).AsNumber() : word.Number;
                var modifier = new BuffModifier { Stat = word.Stat, HasAmount = !word.Multiply, HasMultiplier = word.Multiply };
                if (word.Multiply)
                    modifier.Multiplier = value;
                else
                    modifier.Amount = value;
                buff.Modifiers.Add(modifier);
            }

            if (!runtime.Buffs.Add(context.Variables, target, buff, out bool started))
                return false;
            if (started)
                runtime.RaisePush(target, new PushSignal
                {
                    Kind = TriggerKind.BuffStarts,
                    Name = buff.Name,
                    Other = context.Me,
                });
            return true;
        }

        public static bool ExecuteRemove(EventContext context, ActionSpec spec)
        {
            if (context == null || spec == null)
                return false;
            EventRuntime runtime = context.Runtime;
            if (runtime == null)
                return false;
            object target = ResolveTarget(context, spec.To);
            if (target == null)
                return false;
            if (!runtime.Buffs.Remove(context.Variables, target, spec.BuffName))
                return false;
            runtime.RaisePush(target, new PushSignal
            {
                Kind = TriggerKind.BuffEnds,
                Name = spec.BuffName,
                Other = context.Me,
            });
            return true;
        }

        /// <summary>An empty target (or <c>me</c>) is the acting sheet; otherwise `other` or a named actor.</summary>
        private static object ResolveTarget(EventContext context, string to)
        {
            if (string.IsNullOrEmpty(to) || string.Equals(to, "me", StringComparison.OrdinalIgnoreCase))
                return context.Me;
            if (string.Equals(to, "other", StringComparison.OrdinalIgnoreCase))
                return context.Other;
            return context.Runtime.FindNamedActor(to);
        }
    }
}
