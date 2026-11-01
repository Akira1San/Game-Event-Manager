using System;
using System.Collections.Generic;

namespace Game
{
    public class WordEntry
    {
        public string Kind = "";
        public string Word = "";
        public string Example = "";
        public string AltShape = "";
    }

    public static class WordRegistry
    {
        public static readonly WordEntry[] All =
        {
            new WordEntry { Kind = "trigger", Word = "when <key> is pressed", Example = "when W is pressed" },
            new WordEntry { Kind = "trigger", Word = "when <key> is released", Example = "when Space is released" },
            new WordEntry { Kind = "trigger", Word = "when <key> is held", Example = "when Left Shift is held" },
            new WordEntry { Kind = "trigger", Word = "when wheel scrolls up", Example = "when wheel scrolls up" },
            new WordEntry { Kind = "trigger", Word = "when wheel scrolls down", Example = "when wheel scrolls down" },
            new WordEntry { Kind = "trigger", Word = "when other touches me", Example = "when other touches me" },
            new WordEntry { Kind = "trigger", Word = "when me touches <thing>", Example = "when me touches Water" },
            new WordEntry { Kind = "trigger", Word = "when me overlaps <thing>", Example = "when me overlaps Enemy" },
            new WordEntry { Kind = "trigger", Word = "when me is near <thing>", Example = "when me is near Player", AltShape = "when me is near <thing> by <dist>" },
            new WordEntry { Kind = "trigger", Word = "when game starts", Example = "when game starts" },
            new WordEntry { Kind = "trigger", Word = "when scene starts", Example = "when scene starts" },
            new WordEntry { Kind = "trigger", Word = "when <thing> is clicked", Example = "when PlayButton is clicked" },
            new WordEntry { Kind = "trigger", Word = "when me arrives at <thing>", Example = "when me arrives at Waypoint" },
            new WordEntry { Kind = "trigger", Word = "when me sees <thing> by <dist>", Example = "when me sees Player by 8" },
            new WordEntry { Kind = "trigger", Word = "when me stops seeing <thing>", Example = "when me stops seeing Player", AltShape = "when me stops seeing <thing> by <dist>" },
            new WordEntry { Kind = "trigger", Word = "when me gets equipped", Example = "when me gets equipped" },
            new WordEntry { Kind = "trigger", Word = "when me gets unequipped", Example = "when me gets unequipped" },
            new WordEntry { Kind = "trigger", Word = "when me gets dropped", Example = "when me gets dropped" },
            new WordEntry { Kind = "trigger", Word = "when me is hovered", Example = "when me is hovered" },
            new WordEntry { Kind = "trigger", Word = "when me appears", Example = "when me appears" },
            new WordEntry { Kind = "trigger", Word = "when every <n> seconds", Example = "when every 2 seconds" },
            new WordEntry { Kind = "trigger", Word = "when <var> becomes <value>", Example = "when my health becomes 0", AltShape = "when <var> becomes not <value>" },
            new WordEntry { Kind = "trigger", Word = "when <var> changes", Example = "when Score changes" },
            new WordEntry { Kind = "trigger", Word = "when message \"<name>\" arrives", Example = "when message \"die\" arrives" },
            new WordEntry { Kind = "trigger", Word = "when anim \"<name>\" ends", Example = "when anim \"Shoot\" ends" },

            new WordEntry { Kind = "condition", Word = "<var> is <value>", Example = "Score is 3" },
            new WordEntry { Kind = "condition", Word = "<var> is not <value>", Example = "my health is lower than 1" },
            new WordEntry { Kind = "condition", Word = "<thing> exists", Example = "Key exists", AltShape = "<thing> does not exist" },
            new WordEntry { Kind = "condition", Word = "me is near <thing> by <dist>", Example = "me is near Player by 2" },
            new WordEntry { Kind = "condition", Word = "<key> is held", Example = "Shift is held" },
            new WordEntry { Kind = "condition", Word = "chance <n>%", Example = "chance 20%" },
            new WordEntry { Kind = "condition", Word = "<thing> is/has tag <tag>", Example = "other has tag Enemy" },
            new WordEntry { Kind = "condition", Word = "my bag has <thing>", Example = "my bag has Sword" },

            new WordEntry { Kind = "action", Word = "move", Example = "move forward speed 5" },
            new WordEntry { Kind = "action", Word = "turn", Example = "turn left degrees 90" },
            new WordEntry { Kind = "action", Word = "goto", Example = "goto Player" },
            new WordEntry { Kind = "action", Word = "goto", Example = "goto there" },
            new WordEntry { Kind = "action", Word = "camera", Example = "camera looks at Chest" },
            new WordEntry { Kind = "action", Word = "camera", Example = "camera follows Player" },
            new WordEntry { Kind = "action", Word = "look", Example = "look at Player" },
            new WordEntry { Kind = "action", Word = "face", Example = "face Player" },
            new WordEntry { Kind = "action", Word = "teleport", Example = "teleport me to SpawnPoint" },
            new WordEntry { Kind = "action", Word = "push", Example = "push other up strength 10" },
            new WordEntry { Kind = "action", Word = "make", Example = "make me solid" },
            new WordEntry { Kind = "action", Word = "destroy", Example = "destroy me" },
            new WordEntry { Kind = "action", Word = "spawn", Example = "spawn Coin at SpawnPoint" },
            new WordEntry { Kind = "action", Word = "play", Example = "play sound \"hit.wav\"" },
            new WordEntry { Kind = "action", Word = "play", Example = "play anim \"Walk\"" },
            new WordEntry { Kind = "action", Word = "loop", Example = "loop anim \"Walk\"" },
            new WordEntry { Kind = "action", Word = "stop", Example = "stop anim" },
            new WordEntry { Kind = "action", Word = "stop", Example = "stop this event" },
            new WordEntry { Kind = "action", Word = "send", Example = "send message \"die\"" },
            new WordEntry { Kind = "action", Word = "set", Example = "set Score to 0" },
            new WordEntry { Kind = "action", Word = "set", Example = "set text of ScoreLabel to Score" },
            new WordEntry { Kind = "action", Word = "add", Example = "add 1 to Score" },
            new WordEntry { Kind = "action", Word = "add", Example = "add Sword to my bag" },
            new WordEntry { Kind = "action", Word = "wait", Example = "wait 0.5 seconds" },
            new WordEntry { Kind = "action", Word = "show", Example = "show PausePanel" },
            new WordEntry { Kind = "action", Word = "show", Example = "show text \"Game Over\"" },
            new WordEntry { Kind = "action", Word = "hide", Example = "hide me" },
            new WordEntry { Kind = "action", Word = "hide", Example = "hide text" },
            new WordEntry { Kind = "action", Word = "remove", Example = "remove Potion from my bag" },
            new WordEntry { Kind = "action", Word = "equip", Example = "equip Sword" },
            new WordEntry { Kind = "action", Word = "drop", Example = "drop Sword" },
            new WordEntry { Kind = "action", Word = "restart", Example = "restart scene" },
            new WordEntry { Kind = "action", Word = "load", Example = "load scene \"Level1\"" },
            new WordEntry { Kind = "action", Word = "quit", Example = "quit game" },
            new WordEntry { Kind = "action", Word = "print", Example = "print \"W is held\"" },
            new WordEntry { Kind = "action", Word = "debug", Example = "debug \"health = \" + my health" },
        };

        public static readonly string[] ActionWords = BuildActionWords();

        public static readonly string[] ConditionMarks =
        {
            "is", "has", "becomes", "changes", "exists", "does", "near", "chance",
        };

        public static readonly string[] Suggestables = BuildSuggestables();

        private static string[] BuildActionWords()
        {
            var words = new List<string>();
            foreach (var entry in All)
                if (entry.Kind == "action" && words.IndexOf(entry.Word) < 0)
                    words.Add(entry.Word);
            return words.ToArray();
        }

        private static string[] BuildSuggestables()
        {
            var words = new List<string>
            {
                "when", "unless", "and", "not", "my", "me", "other", "to",
            };
            foreach (var word in ActionWords)
                words.Add(word);
            foreach (var mark in ConditionMarks)
                words.Add(mark);
            return words.ToArray();
        }
    }
}
