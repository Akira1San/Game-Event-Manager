using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One line of dialogue history (Task 35 Part D).</summary>
    public class BacklogLine
    {
        /// <summary>The actor whose sheet added the line.</summary>
        public object Owner;
        public string Text = "";
    }

    /// <summary>
    /// The game's dialogue history, behind <c>add to backlog "…"</c> (Task 35 Part D). It is
    /// runtime state, not a control: the lines survive a scene change and last until something
    /// clears them, so a conversation can be replayed from the history. The bridge renders the
    /// newest lines into the scene's authored row labels.
    /// </summary>
    public class BacklogManager
    {
        /// <summary>The global that caps the history; `BacklogLimit 50` keeps the newest 50.</summary>
        public const string LimitGlobal = "BacklogLimit";

        /// <summary>How many lines are kept when the sheet never wrote `BacklogLimit`.</summary>
        public const int DefaultLimit = 100;

        private readonly List<BacklogLine> lines = new List<BacklogLine>();

        /// <summary>Oldest first, so the newest line is the last one.</summary>
        public IReadOnlyList<BacklogLine> Lines => lines;

        public int Count => lines.Count;

        /// <summary>The cap in force: `BacklogLimit` when a sheet wrote a positive number, else
        /// <see cref="DefaultLimit"/>.</summary>
        public static int LimitOf(EventVariables variables)
        {
            if (variables != null && variables.HasGlobal(LimitGlobal))
            {
                double limit = variables.GetGlobal(LimitGlobal).AsNumber();
                if (limit > 0)
                    return (int)limit;
            }
            return DefaultLimit;
        }

        /// <summary>Appends a line, dropping the oldest once the cap is reached.</summary>
        public BacklogLine Add(EventVariables variables, object owner, string text)
        {
            var line = new BacklogLine { Owner = owner, Text = text ?? "" };
            lines.Add(line);
            int limit = LimitOf(variables);
            while (lines.Count > limit)
                lines.RemoveAt(0);
            return line;
        }

        /// <summary>The newest <paramref name="count"/> lines, oldest first.</summary>
        public List<BacklogLine> Recent(int count)
        {
            var recent = new List<BacklogLine>();
            if (count <= 0)
                return recent;
            int start = Math.Max(0, lines.Count - count);
            for (int i = start; i < lines.Count; i++)
                recent.Add(lines[i]);
            return recent;
        }

        /// <summary>Forgets the whole history (a new game, or a test).</summary>
        public void Clear()
        {
            lines.Clear();
        }
    }
}
