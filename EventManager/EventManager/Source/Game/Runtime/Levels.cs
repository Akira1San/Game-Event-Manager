using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// Level progression and run statistics (Task 16 Part G). A level is a scene, and its
    /// number is 1-based inside the level order. Every statistic is an ordinary global
    /// variable, so `set` / `add` / `when ... changes` / `save stats` treat them like any
    /// other variable — nothing here needs engine state.
    ///
    /// The variables this module owns:
    ///   LevelName, LevelNumber        the current level (name and 1-based number)
    ///   Kills Total, Kills &lt;type&gt;   run kills, overall and per enemy type
    ///   Shots, Hits, Accuracy         shots fired, shots landed, hits/shots as 0-100
    ///   Time Elapsed                  seconds spent in the current level
    ///
    /// Kills survive a scene load (a run spans levels); Time Elapsed restarts with each level.
    /// </summary>
    public class Levels
    {
        public const string NameVariable = "LevelName";
        public const string NumberVariable = "LevelNumber";
        public const string KillsTotalVariable = "Kills Total";
        public const string ShotsVariable = "Shots";
        public const string HitsVariable = "Hits";
        public const string AccuracyVariable = "Accuracy";
        public const string TimeElapsedVariable = "Time Elapsed";
        public const string KillPrefix = "Kills ";

        private readonly List<string> order = new List<string>();
        private bool started;
        private double startedAt;

        /// <summary>The level order `next level` walks: scene names, in build order.</summary>
        public IReadOnlyList<string> Order => order;

        /// <summary>The current level's name, or "" when the scene has no name yet.</summary>
        public string Current { get; private set; } = "";

        /// <summary>The current level's 1-based number, or 0 when it isn't in the order.</summary>
        public int Number { get; private set; }

        public bool Started => started;

        public static string KillsVariable(string enemyType)
        {
            return KillPrefix + enemyType;
        }

        /// <summary>Seeds the level order. The bridge fills it with the project's scene list.</summary>
        public void SetOrder(IEnumerable<string> names)
        {
            order.Clear();
            if (names == null)
                return;
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                bool known = false;
                foreach (string existing in order)
                    if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                    {
                        known = true;
                        break;
                    }
                if (!known)
                    order.Add(name);
            }
        }

        /// <summary>
        /// A scene finished loading: remember it as the current level, restart this level's
        /// clock and seed the statistics once (kills keep counting across levels).
        /// </summary>
        public void Start(string levelName, EventVariables variables, double time)
        {
            Current = levelName ?? "";
            Number = IndexOf(Current) + 1;
            startedAt = time;
            started = true;

            variables.SetGlobal(NameVariable, ValueBox.OfText(Current));
            variables.SetGlobal(NumberVariable, ValueBox.OfNumber(Number));
            variables.SetGlobal(TimeElapsedVariable, ValueBox.OfNumber(0));
            SeedOnce(variables, KillsTotalVariable);
            SeedOnce(variables, ShotsVariable);
            SeedOnce(variables, HitsVariable);
            SeedOnce(variables, AccuracyVariable);
        }

        private static void SeedOnce(EventVariables variables, string name)
        {
            if (!variables.HasGlobal(name))
                variables.SetGlobal(name, ValueBox.OfNumber(0));
        }

        /// <summary>
        /// <c>set level to &lt;n&gt;</c>: records the level the run is on without loading
        /// anything. Pair it with `load scene` when the sheet should travel.
        /// </summary>
        public void SetNumber(double value, EventVariables variables)
        {
            int number = (int)Math.Round(value);
            Number = number;
            Current = number >= 1 && number <= order.Count ? order[number - 1] : "";
            variables.SetGlobal(NumberVariable, ValueBox.OfNumber(Number));
            variables.SetGlobal(NameVariable, ValueBox.OfText(Current));
        }

        /// <summary>The scene name after the current one, or null at the end of the order.</summary>
        public string NextName()
        {
            int index = IndexOf(Current);
            if (index < 0)
                index = Number - 1; // `set level to <n>` with no matching scene name
            if (index < 0 || index + 1 >= order.Count)
                return null;
            return order[index + 1];
        }

        /// <summary><c>add 1 to kills &lt;type&gt;</c>: counts one kill for that enemy type.</summary>
        public void CountKill(string enemyType, double amount, EventVariables variables)
        {
            if (string.IsNullOrEmpty(enemyType) || amount == 0)
                return;
            Add(variables, KillsVariable(enemyType), amount);
            Add(variables, KillsTotalVariable, amount);
        }

        /// <summary>One `spawn Bullet` from a player's sheet — the denominator of `accuracy`.</summary>
        public void CountShot(EventVariables variables)
        {
            Add(variables, ShotsVariable, 1);
        }

        /// <summary>One landed `"damage"` on an enemy — the numerator of `accuracy`.</summary>
        public void CountHit(EventVariables variables)
        {
            Add(variables, HitsVariable, 1);
        }

        /// <summary>Refreshes the clock and the derived accuracy once a tick.</summary>
        public void Tick(double time, EventVariables variables)
        {
            if (!started)
                return;
            variables.SetGlobal(TimeElapsedVariable, ValueBox.OfNumber(Math.Max(0, time - startedAt)));
            double shots = Read(variables, ShotsVariable);
            double hits = Read(variables, HitsVariable);
            variables.SetGlobal(AccuracyVariable, ValueBox.OfNumber(shots <= 0 ? 0 : hits / shots * 100.0));
        }

        /// <summary>
        /// <c>wipe game variables</c> cleared every global; the level state is not a game
        /// variable a new run should forget, so re-seed it (with fresh statistics).
        /// </summary>
        public void Reseed(EventVariables variables)
        {
            variables.SetGlobal(NameVariable, ValueBox.OfText(Current));
            variables.SetGlobal(NumberVariable, ValueBox.OfNumber(Number));
            variables.SetGlobal(TimeElapsedVariable, ValueBox.OfNumber(0));
            variables.SetGlobal(KillsTotalVariable, ValueBox.OfNumber(0));
            variables.SetGlobal(ShotsVariable, ValueBox.OfNumber(0));
            variables.SetGlobal(HitsVariable, ValueBox.OfNumber(0));
            variables.SetGlobal(AccuracyVariable, ValueBox.OfNumber(0));
        }

        /// <summary>
        /// True for a statistic name. Used by value reads so `kills Zombie` answers 0 before
        /// the first zombie dies instead of warning about an unset variable.
        /// </summary>
        public static bool IsStatName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name == NameVariable || name == NumberVariable || name == KillsTotalVariable ||
                   name == ShotsVariable || name == HitsVariable || name == AccuracyVariable ||
                   name == TimeElapsedVariable || name.StartsWith(KillPrefix, StringComparison.Ordinal);
        }

        /// <summary>What a statistic reads as before anything wrote it.</summary>
        public static ValueBox DefaultFor(string name)
        {
            return name == NameVariable ? ValueBox.OfText("") : ValueBox.OfNumber(0);
        }

        /// <summary>
        /// Reads a statistic in a value position: <c>kills total</c>, <c>kills Zombie</c>,
        /// <c>accuracy</c>, <c>time elapsed</c>, <c>level number</c> and <c>level name</c>.
        /// Returns how many words the name used.
        /// </summary>
        public static bool TryReadStatName(List<Token> words, int index, out string name, out int used)
        {
            name = "";
            used = 0;
            if (words == null || index < 0 || index >= words.Count || words[index].Type != TokenType.Word)
                return false;

            string first = words[index].Text;
            string second = index + 1 < words.Count && words[index + 1].Type == TokenType.Word
                ? words[index + 1].Text
                : null;

            if (string.Equals(first, "kills", StringComparison.OrdinalIgnoreCase))
            {
                if (second == null)
                    return false;
                name = string.Equals(second, "total", StringComparison.OrdinalIgnoreCase)
                    ? KillsTotalVariable
                    : KillsVariable(second);
                used = 2;
                return true;
            }
            if (string.Equals(first, "level", StringComparison.OrdinalIgnoreCase) && second != null)
            {
                if (string.Equals(second, "number", StringComparison.OrdinalIgnoreCase))
                {
                    name = NumberVariable;
                    used = 2;
                    return true;
                }
                if (string.Equals(second, "name", StringComparison.OrdinalIgnoreCase))
                {
                    name = NameVariable;
                    used = 2;
                    return true;
                }
                return false;
            }
            if (string.Equals(first, "time", StringComparison.OrdinalIgnoreCase) &&
                second != null && string.Equals(second, "elapsed", StringComparison.OrdinalIgnoreCase))
            {
                name = TimeElapsedVariable;
                used = 2;
                return true;
            }
            if (string.Equals(first, "accuracy", StringComparison.OrdinalIgnoreCase))
            {
                name = AccuracyVariable;
                used = 1;
                return true;
            }
            return false;
        }

        private int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name))
                return -1;
            for (int i = 0; i < order.Count; i++)
                if (string.Equals(order[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static double Read(EventVariables variables, string name)
        {
            return variables.HasGlobal(name) ? variables.GetGlobal(name).AsNumber() : 0;
        }

        private static void Add(EventVariables variables, string name, double amount)
        {
            variables.SetGlobal(name, ValueBox.OfNumber(Read(variables, name) + amount));
        }
    }
}
