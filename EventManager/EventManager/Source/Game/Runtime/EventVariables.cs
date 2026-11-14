using System;
using System.Collections.Generic;

namespace Game
{
    public class EventVariables
    {
        public Action<string> OnWarning = message => { };

        private readonly Dictionary<string, ValueBox> globals = new Dictionary<string, ValueBox>();
        private readonly Dictionary<object, Dictionary<string, ValueBox>> locals = new Dictionary<object, Dictionary<string, ValueBox>>();
        private readonly HashSet<string> warnedGlobalNames = new HashSet<string>();
        private readonly HashSet<string> warnedLocalKeys = new HashSet<string>();
        /// <summary>The checkpoint written by <c>save stats</c> (Task 16 Part G).</summary>
        private Dictionary<string, ValueBox> savedStats;

        public ValueBox GetGlobal(string name)
        {
            ValueBox value;
            if (globals.TryGetValue(name, out value))
                return value;
            WarnOnce(warnedGlobalNames, name, name, "a plain global");
            return new ValueBox();
        }

        public void SetGlobal(string name, ValueBox value)
        {
            globals[name] = ValueBox.Copy(value);
        }

        public bool HasGlobal(string name)
        {
            return globals.ContainsKey(name);
        }

        /// <summary><c>save stats</c>: keeps a copy of every global, so a later
        /// <c>load stats</c> can put the run back the way it was.</summary>
        public void SaveStats()
        {
            savedStats = new Dictionary<string, ValueBox>();
            foreach (var pair in globals)
                savedStats[pair.Key] = ValueBox.Copy(pair.Value);
        }

        /// <summary><c>load stats</c>: replaces the globals with the last saved checkpoint.
        /// Returns false when nothing has been saved yet.</summary>
        public bool LoadStats()
        {
            if (savedStats == null)
                return false;
            globals.Clear();
            foreach (var pair in savedStats)
                globals[pair.Key] = ValueBox.Copy(pair.Value);
            warnedGlobalNames.Clear();
            return true;
        }

        /// <summary><c>wipe game variables</c>: forgets every global, for a new game.</summary>
        public void WipeGlobals()
        {
            globals.Clear();
            warnedGlobalNames.Clear();
        }

        public ValueBox GetLocal(object host, string name)
        {
            var bucket = FindLocals(host);
            ValueBox value;
            if (bucket.TryGetValue(name, out value))
                return value;
            WarnOnce(warnedLocalKeys, LocalKey(host, name), name, DescribeHost(host));
            return new ValueBox();
        }

        public void SetLocal(object host, string name, ValueBox value)
        {
            FindLocals(host)[name] = ValueBox.Copy(value);
        }

        /// <summary>Removes one per-object variable (used when construction state ends).</summary>
        public void RemoveLocal(object host, string name)
        {
            if (locals.TryGetValue(host, out var bucket))
                bucket.Remove(name);
        }

        public bool HasLocal(object host, string name)
        {
            Dictionary<string, ValueBox> bucket;
            return locals.TryGetValue(host, out bucket) && bucket.ContainsKey(name);
        }

        public EventVariables CarryValuesFrom(EventVariables previous, Func<string, bool> nameSurvives)
        {
            foreach (var pair in previous.globals)
            {
                if (nameSurvives(pair.Key))
                    globals[pair.Key] = ValueBox.Copy(pair.Value);
            }
            foreach (var pair in previous.locals)
            {
                foreach (var nameValue in pair.Value)
                {
                    if (nameSurvives(nameValue.Key))
                        FindLocals(pair.Key)[nameValue.Key] = ValueBox.Copy(nameValue.Value);
                }
            }
            foreach (var name in previous.warnedGlobalNames)
                warnedGlobalNames.Add(name);
            foreach (var key in previous.warnedLocalKeys)
                warnedLocalKeys.Add(key);
            return this;
        }

        private Dictionary<string, ValueBox> FindLocals(object host)
        {
            Dictionary<string, ValueBox> bucket;
            if (!locals.TryGetValue(host, out bucket))
            {
                bucket = new Dictionary<string, ValueBox>();
                locals[host] = bucket;
            }
            return bucket;
        }

        private static string LocalKey(object host, string name)
        {
            return DescribeHost(host) + " :: " + name;
        }

        private static string DescribeHost(object host)
        {
            string described = host == null ? "an unknown actor" : host.ToString();
            return described + "'s";
        }

        private void WarnOnce(HashSet<string> seen, string key, string name, string where)
        {
            if (seen.Contains(key))
                return;
            seen.Add(key);
            OnWarning($"the variable \u201C{name}\u201D was read before it was ever set (\u201C{where}\u201D) — it counts as 0 / \"\" / false");
        }
    }
}
