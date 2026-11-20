using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// What each sheet's dialogue has put on screen, so <c>clear dialogue</c> can take it all
    /// back in one word (Task 35 Part D): the labels a <c>type text</c> line wrote and whether
    /// that sheet set the speaker. The choices and the reveal itself live on
    /// <see cref="ChoiceManager"/> and <see cref="Typewriter"/>; this only remembers the targets.
    /// </summary>
    public class DialogueState
    {
        private readonly Dictionary<object, List<string>> labels = new Dictionary<object, List<string>>();
        private readonly HashSet<object> speakers = new HashSet<object>();

        /// <summary>Remembers a label this dialogue typed into (once each, in written order).</summary>
        public void RememberLabel(object owner, string thing)
        {
            if (owner == null || string.IsNullOrEmpty(thing))
                return;
            if (!labels.TryGetValue(owner, out var list))
            {
                list = new List<string>();
                labels[owner] = list;
            }
            if (!list.Contains(thing))
                list.Add(thing);
        }

        /// <summary>The labels that sheet's dialogue wrote, in the order they were used.</summary>
        public IReadOnlyList<string> LabelsFor(object owner)
        {
            if (owner != null && labels.TryGetValue(owner, out var list))
                return list;
            return Array.Empty<string>();
        }

        /// <summary>Records that this sheet wrote the speaker, so `clear dialogue` clears it
        /// without wiping a speaker another sheet is still showing.</summary>
        public void MarkSpeaker(object owner)
        {
            if (owner != null)
                speakers.Add(owner);
        }

        public bool Spoke(object owner)
        {
            return owner != null && speakers.Contains(owner);
        }

        /// <summary>Forgets one sheet's dialogue after it was cleared.</summary>
        public void Forget(object owner)
        {
            if (owner == null)
                return;
            labels.Remove(owner);
            speakers.Remove(owner);
        }

        /// <summary>Forgets every sheet (a new scene, a hot reload).</summary>
        public void Clear()
        {
            labels.Clear();
            speakers.Clear();
        }
    }
}
