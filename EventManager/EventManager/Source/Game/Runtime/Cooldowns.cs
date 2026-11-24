using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// Shared, per-actor named cooldown timers (Task 27 Part E; generalised in Task 37 F5).
    ///
    /// An ability says `put "stim" on cooldown for 10 seconds`, gates itself with
    /// `my "stim" is ready`, and a HUD can read `my "stim" cooldown` for the seconds left. Every
    /// timer is advanced in one loop by <see cref="Tick"/>, so a sheet no longer needs a
    /// `when every 1 seconds` poll per ability — that polling still works, nothing was removed.
    ///
    /// Timers are stored as seconds remaining rather than absolute times, and keyed by actor plus
    /// name (case-insensitive), so two abilities on one sheet and one ability on two units cool
    /// down independently. No Flax types, so the runtime and the tests drive it directly.
    ///
    /// Since Task 37 F5 this is the **only** timer table: `put "stim" on cooldown`,
    /// <c>reload</c> (under <see cref="ReloadTimer"/>) and <c>cast</c> (under the spell's name) all
    /// start here, and `when "stim" is ready` / `when reload completes` are the expiry edges.
    /// </summary>
    public class Cooldowns
    {
        /// <summary>Prefix of the value form: <c>my "stim" cooldown</c> → <c>cooldown stim</c>.</summary>
        public const string VariablePrefix = "cooldown ";

        /// <summary>The timer name <c>reload</c> uses, so a weapon reload is an ordinary named
        /// timer next to spells and abilities (Task 37 F5).</summary>
        public const string ReloadTimer = "reload";

        private readonly Dictionary<object, Dictionary<string, double>> _timers =
            new Dictionary<object, Dictionary<string, double>>();

        /// <summary>How many times each timer has run out, per actor, so `when "stim" is ready`
        /// is a one-shot edge however many sheets are polling.</summary>
        private readonly Dictionary<object, Dictionary<string, int>> _completions =
            new Dictionary<object, Dictionary<string, int>>();

        /// <summary>The variable name the parser builds for <c>my "stim" cooldown</c>.</summary>
        public static string VariableName(string timer)
        {
            return VariablePrefix + (timer ?? "");
        }

        /// <summary>True when a variable name is the computed cooldown read, with the timer name out.</summary>
        public static bool TryReadVariable(string variable, out string timer)
        {
            timer = "";
            if (string.IsNullOrEmpty(variable) ||
                !variable.StartsWith(VariablePrefix, StringComparison.OrdinalIgnoreCase))
                return false;
            timer = variable.Substring(VariablePrefix.Length).Trim();
            return timer.Length > 0;
        }

        /// <summary>
        /// Starts (or restarts) a named timer. A duration of zero or less clears it instead, so
        /// <c>for 0 seconds</c> means "ready right away" rather than a negative timer — and it
        /// counts as one completion, which is what keeps a zero-second <c>reload</c> raising
        /// `when reload completes` exactly as the old weapon clock did.
        /// </summary>
        public void Start(object actor, string timer, double seconds)
        {
            if (actor == null || string.IsNullOrWhiteSpace(timer))
                return;
            if (seconds <= 0)
            {
                Forget(actor, timer);
                RecordCompletion(actor, timer.Trim());
                return;
            }
            if (!_timers.TryGetValue(actor, out Dictionary<string, double> timers))
            {
                timers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                _timers[actor] = timers;
            }
            timers[timer.Trim()] = seconds;
        }

        /// <summary>True when the timer has run out (or was never started).</summary>
        public bool Ready(object actor, string timer)
        {
            return Remaining(actor, timer) <= 0;
        }

        /// <summary>Seconds left on a timer, or 0 when it is ready.</summary>
        public double Remaining(object actor, string timer)
        {
            if (actor == null || string.IsNullOrWhiteSpace(timer) ||
                !_timers.TryGetValue(actor, out Dictionary<string, double> timers) ||
                !timers.TryGetValue(timer.Trim(), out double remaining))
                return 0;
            return remaining > 0 ? remaining : 0;
        }

        /// <summary>
        /// The countdown a HUD shows for a timer (Task 37 F7): whole seconds, rounded up, 0 when
        /// ready. `bind CooldownLabel to my "Fireball" cooldown` expands to a `when ... changes`
        /// watch, so a sub-second value would repaint the label every frame; whole seconds repaint
        /// it once per second. <see cref="Remaining"/> keeps the exact number for code.
        /// </summary>
        public static double DisplaySeconds(double remaining)
        {
            return remaining > 0 ? Math.Ceiling(remaining) : 0;
        }

        /// <summary>
        /// How many times a named timer has run out on an actor, counting one the first time it is
        /// asked after the expiry. Comparing that count against the one a sheet saw last makes
        /// `when "&lt;name&gt;" is ready` a one-shot edge, the same way the old weapon clock made
        /// `when reload completes` one.
        /// </summary>
        public int Completion(object actor, string timer)
        {
            if (actor == null || string.IsNullOrWhiteSpace(timer))
                return 0;
            if (!_completions.TryGetValue(actor, out Dictionary<string, int> counts))
                return 0;
            return counts.TryGetValue(timer.Trim(), out int count) ? count : 0;
        }

        /// <summary>Counts one expiry of a named timer on an actor.</summary>
        private void RecordCompletion(object actor, string timer)
        {
            if (!_completions.TryGetValue(actor, out Dictionary<string, int> counts))
            {
                counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _completions[actor] = counts;
            }
            counts.TryGetValue(timer, out int done);
            counts[timer] = done + 1;
        }

        /// <summary>How many timers are still running on an actor (0 for an idle one).</summary>
        public int Count(object actor)
        {
            if (actor == null || !_timers.TryGetValue(actor, out Dictionary<string, double> timers))
                return 0;
            return timers.Count;
        }

        /// <summary>Forgets one timer on one actor.</summary>
        public void Forget(object actor, string timer)
        {
            if (actor == null || string.IsNullOrWhiteSpace(timer))
                return;
            if (_timers.TryGetValue(actor, out Dictionary<string, double> timers))
            {
                timers.Remove(timer.Trim());
                if (timers.Count == 0)
                    _timers.Remove(actor);
            }
        }

        /// <summary>Forgets an actor entirely (it was destroyed) — every timer it had running.</summary>
        public void Clear(object actor)
        {
            if (actor == null)
                return;
            _timers.Remove(actor);
            _completions.Remove(actor);
        }

        /// <summary>Clears every timer for a scene restart / new session.</summary>
        public void Reset()
        {
            _timers.Clear();
            _completions.Clear();
        }

        /// <summary>
        /// Advances every running timer by <paramref name="delta"/> seconds in one pass, dropping
        /// the ones that ran out. Called once per tick from <see cref="EventRuntime.Tick"/>.
        /// </summary>
        public void Tick(double delta)
        {
            if (delta <= 0 || _timers.Count == 0)
                return;

            List<object> idle = null;
            foreach (var pair in _timers)
            {
                Dictionary<string, double> timers = pair.Value;
                List<string> expired = null;
                foreach (var timer in timers)
                {
                    double remaining = timer.Value - delta;
                    if (remaining <= 0)
                        (expired ??= new List<string>()).Add(timer.Key);
                    else
                        timers[timer.Key] = remaining;
                }
                if (expired == null)
                    continue;
                foreach (string name in expired)
                {
                    timers.Remove(name);
                    RecordCompletion(pair.Key, name);
                }
                if (timers.Count == 0)
                    (idle ??= new List<object>()).Add(pair.Key);
            }

            if (idle != null)
                foreach (object actor in idle)
                    _timers.Remove(actor);
        }
    }
}
