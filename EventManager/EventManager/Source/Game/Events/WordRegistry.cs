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
            new WordEntry { Kind = "trigger", Word = "when <thing> text changes", Example = "when NameBox text changes" },
            new WordEntry { Kind = "trigger", Word = "when <thing> is focused", Example = "when NameBox is focused" },
            new WordEntry { Kind = "trigger", Word = "when <thing> loses focus", Example = "when NameBox loses focus" },
            new WordEntry { Kind = "trigger", Word = "when <thing> submits", Example = "when NameBox submits" },
            new WordEntry { Kind = "trigger", Word = "when <thing> is double-clicked", Example = "when PlayButton is double-clicked" },
            new WordEntry { Kind = "trigger", Word = "when <thing> is right-clicked", Example = "when RestartButton is right-clicked" },
            new WordEntry { Kind = "trigger", Word = "when <thing> is pressed", Example = "when RestartButton is pressed" },
            new WordEntry { Kind = "trigger", Word = "when <thing> is released", Example = "when RestartButton is released" },
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
            new WordEntry { Kind = "trigger", Word = "when anim \"<name>\" ends", Example = "when anim \"dragon_fire\" ends" },
            new WordEntry { Kind = "trigger", Word = "when particle \"<name>\" ends", Example = "when particle \"explosion\" ends" },
            new WordEntry { Kind = "trigger", Word = "when tween <thing> ends", Example = "when tween PausePanel ends" },
            new WordEntry { Kind = "trigger", Word = "when state is <thing>", Example = "when state is chase" },
            new WordEntry { Kind = "trigger", Word = "when <thing> is selected", Example = "when me is selected" },
            new WordEntry { Kind = "trigger", Word = "when <thing> is deselected", Example = "when me is deselected" },
            new WordEntry { Kind = "trigger", Word = "when selection changes", Example = "when selection changes" },
            new WordEntry { Kind = "trigger", Word = "when control group <n> is pressed", Example = "when control group 1 is pressed" },
            new WordEntry { Kind = "trigger", Word = "when control group <n> is held", Example = "when control group 1 is held" },
            new WordEntry { Kind = "trigger", Word = "when Minerals becomes <n>", Example = "when Minerals becomes 500" },
            new WordEntry { Kind = "trigger", Word = "when Supply becomes <n>", Example = "when Supply becomes 10" },
            new WordEntry { Kind = "trigger", Word = "when Supply Used becomes <n>", Example = "when Supply Used becomes 10" },
            new WordEntry { Kind = "trigger", Word = "when resources change", Example = "when resources change" },
            new WordEntry { Kind = "trigger", Word = "when ResourcesChange", Example = "when ResourcesChange", AltShape = "when resources change" },

            new WordEntry { Kind = "condition", Word = "<var> is <value>", Example = "Score is 3" },
            new WordEntry { Kind = "condition", Word = "<var> is not <value>", Example = "my health is lower than 1" },
            new WordEntry { Kind = "condition", Word = "<thing> exists", Example = "Key exists", AltShape = "<thing> does not exist" },
            new WordEntry { Kind = "condition", Word = "me is near <thing> by <dist>", Example = "me is near Player by 2" },
            new WordEntry { Kind = "condition", Word = "me is moving", Example = "me is moving" },
            new WordEntry { Kind = "condition", Word = "me touches <material> and <condition>", Example = "me touches Grass and me is moving" },
            new WordEntry { Kind = "condition", Word = "<key> is held", Example = "Shift is held" },
            new WordEntry { Kind = "condition", Word = "<thing> is pressed", Example = "RestartButton is pressed" },
            new WordEntry { Kind = "condition", Word = "<thing> is released", Example = "RestartButton is released" },
            new WordEntry { Kind = "condition", Word = "<thing> is disabled", Example = "RestartButton is disabled" },
            new WordEntry { Kind = "condition", Word = "<thing> is focused", Example = "NameBox is focused" },
            new WordEntry { Kind = "condition", Word = "text of <thing> is <value>", Example = "text of NameBox is \"Player\"" },
            new WordEntry { Kind = "condition", Word = "chance <n>%", Example = "chance 20%" },
            new WordEntry { Kind = "condition", Word = "<thing> is/has tag <tag>", Example = "other has tag Enemy" },
            new WordEntry { Kind = "condition", Word = "my bag has <thing>", Example = "my bag has Sword" },
            new WordEntry { Kind = "condition", Word = "progress of <thing> is <value>", Example = "progress of HealthBar is 1" },
            new WordEntry { Kind = "condition", Word = "slider <thing> is <value>", Example = "slider HealthBar is 50" },
            new WordEntry { Kind = "condition", Word = "particle \"<name>\" is playing", Example = "particle \"smoke\" is playing" },
            new WordEntry { Kind = "condition", Word = "me is selected", Example = "me is selected" },
            new WordEntry { Kind = "condition", Word = "selection count is <n>", Example = "selection count is 2" },
            new WordEntry { Kind = "condition", Word = "my control group <n> is assigned", Example = "my control group 1 is assigned" },
            new WordEntry { Kind = "condition", Word = "Minerals is higher than <n>", Example = "Minerals is higher than 50" },
            new WordEntry { Kind = "condition", Word = "Supply Used is lower than Supply Max", Example = "Supply Used is lower than Supply Max" },
            new WordEntry { Kind = "condition", Word = "can afford <building>", Example = "can afford Barracks" },

            new WordEntry { Kind = "action", Word = "move", Example = "move forward speed 5" },
            new WordEntry { Kind = "action", Word = "turn", Example = "turn left degrees 90" },
            new WordEntry { Kind = "action", Word = "goto", Example = "goto Player" },
            new WordEntry { Kind = "action", Word = "goto", Example = "goto there" },
            new WordEntry { Kind = "action", Word = "goto", Example = "goto Player facing forward" },
            new WordEntry { Kind = "action", Word = "camera", Example = "camera looks at Chest" },
            new WordEntry { Kind = "action", Word = "camera", Example = "camera follows Player" },
            new WordEntry { Kind = "action", Word = "look", Example = "look at Player" },
            new WordEntry { Kind = "action", Word = "face", Example = "face Player" },
            new WordEntry { Kind = "action", Word = "teleport", Example = "teleport me to SpawnPoint" },
            new WordEntry { Kind = "action", Word = "push", Example = "push other up strength 10" },
            new WordEntry { Kind = "action", Word = "parent", Example = "parent Box to me" },
            new WordEntry { Kind = "action", Word = "unparent", Example = "unparent Box" },
            new WordEntry { Kind = "action", Word = "scale", Example = "scale Box 2" },
            new WordEntry { Kind = "action", Word = "make", Example = "make me solid" },
            new WordEntry { Kind = "action", Word = "destroy", Example = "destroy me" },
            new WordEntry { Kind = "action", Word = "spawn", Example = "spawn Coin at SpawnPoint" },
            new WordEntry { Kind = "action", Word = "play", Example = "play sound \"hit.wav\"" },
            new WordEntry { Kind = "action", Word = "play", Example = "play anim \"dragon_fire\"" },
            new WordEntry { Kind = "action", Word = "play", Example = "play particle \"smoke\"" },
            new WordEntry { Kind = "action", Word = "play step sound", Example = "play step sound \"Grass\"" },
            new WordEntry { Kind = "action", Word = "loop", Example = "loop anim \"animation walk\"" },
            new WordEntry { Kind = "action", Word = "stop", Example = "stop anim" },
            new WordEntry { Kind = "action", Word = "stop", Example = "stop particle \"smoke\"" },
            new WordEntry { Kind = "action", Word = "stop", Example = "stop this event" },
            new WordEntry { Kind = "action", Word = "send", Example = "send message \"die\"" },
            new WordEntry { Kind = "action", Word = "set", Example = "set Score to 0" },
            new WordEntry { Kind = "action", Word = "set", Example = "set text of ScoreLabel to Score" },
            new WordEntry { Kind = "action", Word = "set", Example = "set my control group to 1" },
            new WordEntry { Kind = "action", Word = "set", Example = "set light intensity to 2" },
            new WordEntry { Kind = "action", Word = "set", Example = "set light color to \"1 0 0\"" },
            new WordEntry { Kind = "action", Word = "set", Example = "set light range to 10" },
            new WordEntry { Kind = "action", Word = "set progress of <thing> to <value>", Example = "set progress of HealthBar to 1" },
            new WordEntry { Kind = "action", Word = "set slider <thing> to <value>", Example = "set slider HealthBar to 50" },
            new WordEntry { Kind = "action", Word = "enable", Example = "enable light" },
            new WordEntry { Kind = "action", Word = "disable", Example = "disable light" },
            new WordEntry { Kind = "action", Word = "toggle", Example = "toggle light" },
            new WordEntry { Kind = "action", Word = "select", Example = "select me" },
            new WordEntry { Kind = "action", Word = "select", Example = "select all Marine" },
            new WordEntry { Kind = "action", Word = "select", Example = "select units near me by 5" },
            new WordEntry { Kind = "action", Word = "tween", Example = "tween PausePanel position to CenterPoint in 0.5" },
            new WordEntry { Kind = "action", Word = "tween", Example = "tween PausePanel alpha to 0 in 0.3" },
            new WordEntry { Kind = "action", Word = "tween", Example = "tween PausePanel scale to 1.2 in 0.25" },
            new WordEntry { Kind = "action", Word = "tween", Example = "tween PausePanel color to \"1 0 0\" in 0.5" },
            new WordEntry { Kind = "action", Word = "tween", Example = "tween PausePanel position to CenterPoint in 0.5 easing ease out" },
            new WordEntry { Kind = "action", Word = "focus", Example = "focus NameBox" },
            new WordEntry { Kind = "action", Word = "unfocus", Example = "unfocus NameBox" },
            new WordEntry { Kind = "action", Word = "navigate", Example = "navigate up" },
            new WordEntry { Kind = "action", Word = "navigate", Example = "navigate down" },
            new WordEntry { Kind = "action", Word = "navigate", Example = "navigate left" },
            new WordEntry { Kind = "action", Word = "navigate", Example = "navigate right" },
            new WordEntry { Kind = "action", Word = "deselect", Example = "deselect me" },
            new WordEntry { Kind = "action", Word = "set state", Example = "set state patrol" },
            new WordEntry { Kind = "action", Word = "add", Example = "add 1 to Score" },
            new WordEntry { Kind = "action", Word = "add", Example = "add 50 to Minerals" },
            new WordEntry { Kind = "action", Word = "add", Example = "add Sword to my bag" },
            new WordEntry { Kind = "action", Word = "deduct", Example = "deduct 100 from Minerals" },
            new WordEntry { Kind = "action", Word = "increase", Example = "increase Supply Max by 8" },
            new WordEntry { Kind = "action", Word = "set", Example = "set Minerals to 500" },
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
            "is", "has", "becomes", "changes", "exists", "does", "near", "chance", "afford",
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
                "when", "unless", "and", "not", "my", "me", "other", "to", "state",
            };
            foreach (var word in ActionWords)
                words.Add(word);
            foreach (var mark in ConditionMarks)
                words.Add(mark);
            return words.ToArray();
        }
    }
}
