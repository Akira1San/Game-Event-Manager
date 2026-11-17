using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// Shared, per-actor named cooldown timers (Task 27 Part E).
    ///
    /// An ability says `put "stim" on cooldown for 10 seconds`, gates itself with
    /// `my "stim" is ready`, and a HUD can read `my "stim" cooldown` for the seconds left. Every
    /// timer is advanced in one loop by <see cref="Tick"/>, so a sheet no longer needs a
    /// `when every 1 seconds` poll per ability — that polling still works, nothing was removed.
    ///
    /// Timers are stored as seconds remaining rather than absolute times, and keyed by actor plus
    /// name (case-insensitive), so two abilities on one sheet and one ability on two units cool
    /// down independently. No Flax types, so the runtime and the tests drive it directly.
    /// </summary>
    public class Cooldowns
    {
        /// <summary>Prefix of the value form: <c>my "stim" cooldown</c> → <c>cooldown stim</c>.</summary>
        public const string VariablePrefix = "cooldown ";

        private readonly Dictionary<object, Dictionary<string, double>> _timers =
            new Dictionary<object, Dictionary<string, double>>();

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
        /// <c>for 0 seconds</c> means "ready right away" rather than a negative timer.
        /// </summary>
        public void Start(object actor, string timer, double seconds)
        {
            if (actor == null || string.IsNullOrWhiteSpace(timer))
                return;
            if (seconds <= 0)
            {
                Forget(actor, timer);
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
            if (actor != null)
                _timers.Remove(actor);
        }

        /// <summary>Clears every timer for a scene restart / new session.</summary>
        public void Reset()
        {
            _timers.Clear();
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
                    timers.Remove(name);
                if (timers.Count == 0)
                    (idle ??= new List<object>()).Add(pair.Key);
            }

            if (idle != null)
                foreach (object actor in idle)
                    _timers.Remove(actor);
        }
    }
}
