using System;
using System.Collections.Generic;

namespace Game
{
    public enum ActionVerb
    {
        Unknown,
        Move,
        StopMoving,
        Turn,
        LookAt,
        Face,
        Teleport,
        Push,
        Goto,
        Destroy,
        Spawn,
        PlaySound,
        StopSound,
        PlayAnim,
        StopAnim,
        Hide,
        Show,
        ShowText,
        HideText,
        RestartScene,
        LoadScene,
        QuitGame,
    }

    public class ActionSpec
    {
        public ActionVerb Verb;
        public string Thing = "";
        public string To = "";
        public string Dir = "";
        public double Number;
        public bool HasNumber;
        public ValueNode AmountNode;
        public List<Token> Leftover = new List<Token>();
    }

    public static class ActionWords
    {
        public static string[] DirectionWords = { "forward", "back", "backward", "left", "right", "up", "down" };

        public static string[] KeyNamesForScanning = BuildKeyNames();

        private static string[] BuildKeyNames()
        {
            var names = new List<string>();
            names.AddRange(new[] { "Space", "Enter", "Escape", "Tab", "Backspace", "Up", "Down", "Left", "Right" });
            names.AddRange(new[] { "Left Shift", "Right Shift", "Left Ctrl", "Right Ctrl", "Left Alt", "Right Alt" });
            names.AddRange(new[] { "Left Mouse", "Right Mouse", "Middle Mouse" });
            for (char letter = 'A'; letter <= 'Z'; letter++)
                names.Add(letter.ToString());
            for (int number = 0; number <= 9; number++)
                names.Add("D" + number);
            return names.ToArray();
        }

        public static ActionSpec Parse(List<Token> words, Action<string> warn)
        {
            var spec = new ActionSpec();
            if (words.Count == 0)
                return Unknown(spec, words, "", warn);

            string verb = words[0].Text;
            switch (verb)
            {
                case "stop":
                    if (words.Count >= 2 && words[1].Text == "moving")
                        return Finish(spec, ActionVerb.StopMoving, words, 2);
                    if (words.Count >= 2 && words[1].Text == "sound")
                        return Finish(spec, ActionVerb.StopSound, words, 2);
                    if (words.Count >= 2 && words[1].Text == "anim")
                        return Finish(spec, ActionVerb.StopAnim, words, 2);
                    return Unknown(spec, words, "yes, a \u201Cstop\u201D line — I only know \u201Cstop sound\u201D, \u201Cstop anim\u201D, \u201Cstop moving\u201D, and \u201Cstop this event\u201D", warn);

                case "move":
                    return Move(spec, words, warn);

                case "goto":
                {
                    string gotoThing = ThingAt(words, 1, out int gotoUsed);
                    if (gotoThing != null)
                    {
                        spec.Thing = gotoThing;
                        return Finish(spec, ActionVerb.Goto, words, 1 + gotoUsed);
                    }
                    return Unknown(spec, words, "\u201Cgoto\u201D needs a thing, like \u201Cgoto SpawnPoint\u201D", warn);
                }

                case "turn":
                    if (words.Count >= 2 && IsDirection(words[1].Text) && words.Count >= 4 && words[2].Text == "degrees" &&
                        words[3].Type == TokenType.Number)
                    {
                        spec.Dir = words[1].Text;
                        spec.Number = words[3].Number;
                        spec.HasNumber = true;
                        return Finish(spec, ActionVerb.Turn, words, 4);
                    }
                    return Unknown(spec, words, "\u201Cturn\u201D looks like this: \u201Cturn left degrees 90\u201D", warn);

                case "look":
                {
                    if (words.Count >= 3 && words[1].Text == "at")
                    {
                        string lookThing = ThingAt(words, 2, out int lookUsed);
                        if (lookThing != null)
                        {
                            spec.Thing = lookThing;
                            return Finish(spec, ActionVerb.LookAt, words, 2 + lookUsed);
                        }
                    }
                    return Unknown(spec, words, "\u201Clook\u201D looks like this: \u201Clook at Player\u201D (or \u201Clook at nearest Enemy\u201D)", warn);
                }

                case "face":
                {
                    string faceThing = ThingAt(words, 1, out int faceUsed);
                    if (faceThing != null)
                    {
                        spec.Thing = faceThing;
                        return Finish(spec, ActionVerb.Face, words, 1 + faceUsed);
                    }
                    return Unknown(spec, words, "\u201Cface\u201D needs a thing, like \u201Cface Player\u201D", warn);
                }

                case "teleport":
                    if (words.Count >= 4 && IsThing(words[1]) && words[2].Text == "to" && IsThing(words[3]))
                    {
                        spec.Thing = words[1].Text;
                        spec.To = words[3].Text;
                        return Finish(spec, ActionVerb.Teleport, words, 4);
                    }
                    return Unknown(spec, words, "\u201Cteleport\u201D looks like this: \u201Cteleport me to SpawnPoint\u201D", warn);

                case "push":
                    if (words.Count >= 3 && IsThing(words[1]) && IsDirection(words[2].Text))
                    {
                        spec.Thing = words[1].Text;
                        spec.Dir = words[2].Text;
                        if (words.Count >= 5 && words[words.Count - 2].Text == "strength")
                        {
                            spec.AmountNode = ValueNodeAt(words, words.Count - 1);
                            spec.HasNumber = words[words.Count - 1].Type == TokenType.Number;
                            if (spec.HasNumber)
                                spec.Number = words[words.Count - 1].Number;
                            return Finish(spec, ActionVerb.Push, words, words.Count);
                        }
                        spec.Number = 5;
                        spec.HasNumber = true;
                        return Finish(spec, ActionVerb.Push, words, 3);
                    }
                    return Unknown(spec, words, "\u201Cpush\u201D looks like this: \u201Cpush other up strength 10\u201D", warn);

                case "destroy":
                    if (words.Count >= 2 && IsThing(words[1]))
                    {
                        spec.Thing = words[1].Text;
                        return Finish(spec, ActionVerb.Destroy, words, 2);
                    }
                    return Unknown(spec, words, "\u201Cdestroy\u201D needs a thing, like \u201Cdestroy other\u201D", warn);

                case "spawn":
                    if (words.Count >= 4 && IsThing(words[1]) && words[2].Text == "at" && IsThing(words[3]))
                    {
                        spec.Thing = words[1].Text;
                        spec.To = words[3].Text;
                        return Finish(spec, ActionVerb.Spawn, words, 4);
                    }
                    return Unknown(spec, words, "\u201Cspawn\u201D looks like this: \u201Cspawn Bullet at me\u201D", warn);
            }

            if (words.Count >= 3 && words[0].Text == "play" && words[1].Text == "sound" && words[2].Type == TokenType.Str)
            {
                spec.Thing = words[2].Text;
                return Finish(spec, ActionVerb.PlaySound, words, 3);
            }
            if (words.Count >= 2 && words[0].Text == "play" && words[1].Text == "sound")
                return Unknown(spec, words, "\u201Cplay sound\u201D needs a file in quotes, like \u201Cplay sound \"Boing.wav\"\u201D", warn);
            if (words.Count >= 3 && words[0].Text == "play" && words[1].Text == "anim" && words[2].Type == TokenType.Str)
            {
                spec.Thing = words[2].Text;
                return Finish(spec, ActionVerb.PlayAnim, words, 3);
            }
            if (words.Count >= 2 && words[0].Text == "play" && words[1].Text == "anim")
                return Unknown(spec, words, "\u201Cplay anim\u201D needs a name in quotes, like \u201Cplay anim \"Attack\"\u201D", warn);
            if (words.Count >= 2 && (words[0].Text == "show" || words[0].Text == "hide") && words[1].Text == "text")
            {
                bool showText = words[0].Text == "show";
                if (showText && words.Count >= 3 && words[2].Type == TokenType.Str)
                {
                    spec.Thing = words[2].Text;
                    return Finish(spec, ActionVerb.ShowText, words, 3);
                }
                if (!showText && words.Count == 2)
                    return Finish(spec, ActionVerb.HideText, words, 2);
                return Unknown(spec, words, showText
                    ? "\u201Cshow text\u201D displays quoted text, like \u201Cshow text \"Game Over\"\""
                    : "\u201Chide text\u201D takes nothing after it", warn);
            }
            if (words.Count >= 2 && (words[0].Text == "hide" || words[0].Text == "show") && IsThing(words[1]))
            {
                spec.Thing = words[1].Text;
                return Finish(spec, words[0].Text == "hide" ? ActionVerb.Hide : ActionVerb.Show, words, 2);
            }
            if (words.Count >= 2 && words[0].Text == "restart" && words[1].Text == "scene")
                return Finish(spec, ActionVerb.RestartScene, words, 2);
            if (words.Count >= 3 && words[0].Text == "load" && words[1].Text == "scene" && words[2].Type == TokenType.Str)
            {
                spec.Thing = words[2].Text;
                return Finish(spec, ActionVerb.LoadScene, words, 3);
            }
            if (words.Count >= 2 && words[0].Text == "quit" && words[1].Text == "game")
                return Finish(spec, ActionVerb.QuitGame, words, 2);
            return Unknown(spec, words, "", warn);
        }

        private static ActionSpec Move(ActionSpec spec, List<Token> words, Action<string> warn)
        {
            if (words.Count >= 2 && words[1].Text == "away" && words.Count >= 4 && words[2].Text == "from" && IsThing(words[3]))
            {
                spec.Thing = words[3].Text;
                spec.Dir = "away";
                if (words.Count >= 6 && words[words.Count - 2].Text == "speed")
                {
                    spec.AmountNode = ValueNodeAt(words, words.Count - 1);
                    spec.HasNumber = words[words.Count - 1].Type == TokenType.Number;
                    if (spec.HasNumber)
                        spec.Number = words[words.Count - 1].Number;
                }
                else
                {
                    spec.Number = 5;
                    spec.HasNumber = true;
                }
                return Finish(spec, ActionVerb.Move, words, words.Count);
            }
            if (words.Count >= 2 && IsDirection(words[1].Text))
            {
                spec.Dir = words[1].Text == "back" ? "backward" : words[1].Text;
                if (words.Count >= 4 && words[2].Text == "speed")
                {
                    spec.AmountNode = ValueNodeAt(words, words.Count - 1);
                    spec.HasNumber = words[words.Count - 1].Type == TokenType.Number;
                    if (spec.HasNumber)
                        spec.Number = words[words.Count - 1].Number;
                }
                else
                {
                    spec.Number = 5;
                    spec.HasNumber = true;
                }
                return Finish(spec, ActionVerb.Move, words, words.Count);
            }
            return Unknown(spec, words, "\u201Cmove\u201D looks like this: \u201Cmove forward speed 5\u201D", warn);
        }

        private static ValueNode ValueNodeAt(List<Token> words, int index)
        {
            if (words[index].Type == TokenType.Number)
                return new NumberValue { Value = words[index].Number };
            return new VariableValue { Owner = "global", Name = words[index].Text };
        }

        private static bool IsThing(Token token)
        {
            return token.Type == TokenType.Word;
        }

        private static bool IsQualifier(string word)
        {
            return word == "nearest" || word == "random";
        }

        private static string ThingAt(List<Token> words, int index, out int used)
        {
            used = 1;
            if (index >= words.Count || words[index].Type != TokenType.Word)
                return null;
            string first = words[index].Text;
            if (IsQualifier(first) && index + 1 < words.Count && words[index + 1].Type == TokenType.Word)
            {
                used = 2;
                return first + " " + words[index + 1].Text;
            }
            return first;
        }

        private static bool IsDirection(string word)
        {
            foreach (string direction in DirectionWords)
                if (word == direction)
                    return true;
            return false;
        }

        private static ActionSpec Finish(ActionSpec spec, ActionVerb verb, List<Token> words, int consumed)
        {
            spec.Verb = verb;
            for (int i = consumed; i < words.Count; i++)
                spec.Leftover.Add(words[i]);
            return spec;
        }

        private static ActionSpec Unknown(ActionSpec spec, List<Token> words, string hint, Action<string> warn)
        {
            spec.Verb = ActionVerb.Unknown;
            foreach (var word in words)
                spec.Leftover.Add(word);
            if (warn != null)
            {
                var parts = new System.Text.StringBuilder();
                for (int i = 0; i < words.Count; i++)
                {
                    if (parts.Length > 0)
                        parts.Append(' ');
                    parts.Append(words[i].Text);
                }
                string way = parts.ToString();
                warn(string.IsNullOrEmpty(hint)
                    ? $"I don't know the action \u201C{way}\u201D yet — the line was skipped."
                    : $"I don't know the action \u201C{way}\u201D yet. {hint}");
            }
            return spec;
        }
    }
}
