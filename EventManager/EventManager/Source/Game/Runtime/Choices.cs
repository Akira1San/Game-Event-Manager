using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One numbered choice a dialogue is showing (Task 35 Part C).</summary>
    public class ChoiceOption
    {
        /// <summary>The number this choice answers with (`my picked choice`).</summary>
        public int Index;
        public string Text = "";
        /// <summary>The scene actor acting as the button: the authored `Choice1` (or `ChoiceA`).</summary>
        public string Button = "";

        /// <summary>What the button's label shows: the number, then the words.</summary>
        public string Label => Index + ". " + Text;
    }

    /// <summary>The choices one sheet is currently showing, in the order they were written.</summary>
    public class ChoiceSet
    {
        public object Owner;
        public EventContext Context;
        public readonly List<ChoiceOption> Options = new List<ChoiceOption>();
    }

    /// <summary>
    /// The numbered choices behind <c>show choice "…"</c> (Task 35 Part C). The buttons stay
    /// ordinary scene actors — an authored <c>Choice1</c> (or <c>ChoiceA</c>) with a Button on a
    /// UIControl — so their art, font and layout are editor content. The manager only finds the
    /// next slot by name, numbers it, and remembers which index that button stands for.
    /// </summary>
    public class ChoiceManager
    {
        /// <summary>How many numbered choices one dialogue may show at once.</summary>
        public const int MaxChoices = 9;

        /// <summary>The message a pick sends, so `when choice picked` and a sheet's own
        /// `send message "choice picked"` are the same trigger.</summary>
        public const string PickedMessage = "choice picked";

        /// <summary>The per-actor variable `my picked choice` reads (a sheet may write it too,
        /// which is how a hand-wired `ChoiceA` sets its index).</summary>
        public const string PickedVariable = "picked choice";

        private readonly List<ChoiceSet> sets = new List<ChoiceSet>();

        /// <summary>Every set currently on screen (one per sheet that showed choices).</summary>
        public IReadOnlyList<ChoiceSet> Sets => sets;

        /// <summary>What <paramref name="owner"/> is showing, or null when it has no choice set.</summary>
        public ChoiceSet For(object owner)
        {
            foreach (var set in sets)
            {
                if (ReferenceEquals(set.Owner, owner))
                    return set;
            }
            return null;
        }

        /// <summary>The button name to create when a slot is missing (`Choice1`, `Choice2`, …).</summary>
        public static string SlotName(int index)
        {
            return "Choice" + index;
        }

        /// <summary>The button a scene may author for that index: the numbered spelling first,
        /// then the letter one (`Choice1`, else `ChoiceA`). Empty when neither exists.</summary>
        public static string FindButton(IEventWorld world, int index)
        {
            if (world == null || index < 1)
                return "";
            string numeric = SlotName(index);
            if (world.FindActor(numeric) != null)
                return numeric;
            if (index <= 26)
            {
                string letter = "Choice" + (char)('A' + index - 1);
                if (world.FindActor(letter) != null)
                    return letter;
            }
            return "";
        }

        /// <summary>
        /// Adds the next choice to what <paramref name="owner"/> is showing. Calls made while a
        /// set is still on screen append to it, so two `show choice` lines are two buttons; a
        /// pick removes the set, so the next line starts a fresh numbered list.
        /// </summary>
        public ChoiceOption Add(object owner, EventContext context, IEventWorld world, string text)
        {
            var set = For(owner);
            if (set == null)
            {
                set = new ChoiceSet { Owner = owner, Context = context };
                sets.Add(set);
            }
            if (set.Options.Count >= MaxChoices)
                return null;
            int index = set.Options.Count + 1;
            string button = FindButton(world, index);
            if (button.Length == 0)
                return null;
            var option = new ChoiceOption { Index = index, Text = text, Button = button };
            set.Options.Add(option);
            return option;
        }

        /// <summary>The option a clicked button stands for, with the set it belongs to.</summary>
        public bool TryFind(string button, out ChoiceOption option, out ChoiceSet set)
        {
            option = null;
            set = null;
            if (string.IsNullOrEmpty(button))
                return false;
            foreach (var candidate in sets)
            {
                foreach (var each in candidate.Options)
                {
                    if (string.Equals(each.Button, button, StringComparison.Ordinal))
                    {
                        option = each;
                        set = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Takes a picked set out of play, so a button answers once and the next
        /// `show choice` starts counting from 1 again.</summary>
        public void Remove(ChoiceSet set)
        {
            if (set != null)
                sets.Remove(set);
        }

        /// <summary>Removes and returns what the owner was showing, so `clear dialogue` can hide
        /// the buttons it was using.</summary>
        public ChoiceSet Take(object owner)
        {
            var set = For(owner);
            if (set != null)
                sets.Remove(set);
            return set;
        }

        /// <summary>Drops every set (a new scene, a hot reload).</summary>
        public void Clear()
        {
            sets.Clear();
        }

        /// <summary>Drops the set an actor's sheet was showing (the actor went away).</summary>
        public void ClearActor(object owner)
        {
            for (int i = sets.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(sets[i].Owner, owner))
                    sets.RemoveAt(i);
            }
        }
    }
}
