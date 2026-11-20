using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// One line being revealed by <c>type text "…" into &lt;thing&gt; speed &lt;n&gt;</c> (Task 35
    /// Part A). Pure characters and seconds: the runtime owns the clock, the bridge only writes
    /// <see cref="Visible"/> into the label each time it grows.
    /// </summary>
    public class TypingSession
    {
        /// <summary>The actor whose sheet started the line, so a destroyed sheet takes it along.</summary>
        public object Actor;
        /// <summary>The sheet/context that asked for the line — what `when text finishes` runs on.</summary>
        public EventContext Context;
        /// <summary>The label actor the words appear in.</summary>
        public string Thing = "";
        public string Text = "";
        /// <summary>Characters per second; 0 (or less) means the whole line at once.</summary>
        public double Speed;
        public double Elapsed;
        /// <summary>How many characters of <see cref="Text"/> are showing right now.</summary>
        public int Revealed;
        /// <summary>True once the last character is out.</summary>
        public bool Done;
        /// <summary>True once `when text finishes` was raised for this line, so it fires once.</summary>
        public bool Announced;

        public int Length => Text.Length;

        /// <summary>The part of the line that is showing.</summary>
        public string Visible => Revealed >= Text.Length ? Text : Text.Substring(0, Revealed);

        /// <summary>Advances the reveal by <paramref name="deltaSeconds"/>; true once the line
        /// is complete (a zero/negative time or speed completes immediately).</summary>
        public bool Advance(double deltaSeconds)
        {
            if (Done)
                return true;
            if (Speed <= 0)
            {
                RevealAll();
                return true;
            }
            if (deltaSeconds > 0)
                Elapsed += deltaSeconds;
            int reveal = (int)Math.Floor(Elapsed * Speed);
            if (reveal >= Text.Length)
                RevealAll();
            else if (reveal > Revealed)
                Revealed = reveal;
            return Done;
        }

        /// <summary>Shows the whole line now (`click to skip`).</summary>
        public void RevealAll()
        {
            Revealed = Text.Length;
            Done = true;
        }
    }

    /// <summary>
    /// Every line currently typing (Task 35 Part A). Typewriter lines run side by side, one per
    /// label: starting a new line on a label replaces the old one, because a second
    /// <c>type text</c> into the same box is the next thing the speaker says.
    /// </summary>
    public class Typewriter
    {
        private readonly List<TypingSession> sessions = new List<TypingSession>();

        public IReadOnlyList<TypingSession> Sessions => sessions;

        /// <summary>True while any line is still revealing.</summary>
        public bool IsTyping => sessions.Count > 0;

        /// <summary>Starts (or restarts) the line on <paramref name="thing"/>. The caller checks
        /// <see cref="TypingSession.Done"/> for the instant case and announces it.</summary>
        public TypingSession Start(object actor, EventContext context, string thing, string text, double speed)
        {
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                if (sessions[i].Thing == thing)
                    sessions.RemoveAt(i);
            }
            var session = new TypingSession
            {
                Actor = actor,
                Context = context,
                Thing = thing,
                Text = text ?? "",
                Speed = speed,
            };
            if (session.Text.Length == 0 || session.Speed <= 0)
            {
                // Nothing to reveal: complete it here so the caller raises the finish now.
                session.RevealAll();
                return session;
            }
            sessions.Add(session);
            return session;
        }

        /// <summary>Advances every line. Returns the sessions whose visible text just grew, plus
        /// each completed session exactly once (it leaves the list when it finishes).</summary>
        public List<TypingSession> Tick(double deltaSeconds)
        {
            var changed = new List<TypingSession>();
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                var session = sessions[i];
                int before = session.Revealed;
                session.Advance(deltaSeconds);
                if (session.Done)
                {
                    sessions.RemoveAt(i);
                    changed.Add(session);
                }
                else if (session.Revealed != before)
                {
                    changed.Add(session);
                }
            }
            changed.Reverse();
            return changed;
        }

        /// <summary><c>click to skip</c>: shows every line that is still revealing and hands them
        /// back for the caller to announce. Empty when nothing was typing.</summary>
        public List<TypingSession> Skip()
        {
            var skipped = new List<TypingSession>(sessions);
            foreach (var session in skipped)
                session.RevealAll();
            sessions.Clear();
            return skipped;
        }

        /// <summary>Drops every line (a new scene, a hot reload).</summary>
        public void Clear()
        {
            sessions.Clear();
        }

        /// <summary>Drops the lines an actor's sheet started (the actor went away).</summary>
        public void ClearActor(object actor)
        {
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(sessions[i].Actor, actor))
                    sessions.RemoveAt(i);
            }
        }
    }
}
