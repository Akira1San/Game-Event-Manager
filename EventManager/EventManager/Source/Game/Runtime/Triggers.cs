using System;
using System.Collections.Generic;

namespace Game
{
    public enum TriggerKind
    {
        Unknown,
        GameStart,
        SceneStart,
        Appears,
        KeyHeld,
        KeyPressed,
        KeyReleased,
        WheelScrollUp,
        WheelScrollDown,
        Touches,
        Overlaps,
        Near,
        Clicked,
        RightClicked,
        MiddleClicked,
        Hovered,
        Arrives,
        Sees,
        StopsSeeing,
        Equipped,
        Unequipped,
        Dropped,
        Every,
        WatchBecomes,
        WatchBecomesNot,
        WatchChanges,
        Message,
        AnimEnd,
        StateIs,
    }

    public class TriggerDescriptor
    {
        public TriggerKind Kind = TriggerKind.Unknown;
        public string Key = "";
        public string RequireName = "";
        /// <summary>For <c>when me touches &lt;material&gt;</c>: the physics-material name the
        /// touched actor's collider must carry (matched in addition to, or instead of, a name).</summary>
        public string RequireMaterial = "";
        public double Distance;
        public double Seconds;
        public string WatchOwner = "";
        public string WatchName = "";
        public ValueNode WatchTarget;
    }

    public static class TriggerClassifier
    {
        public static TriggerDescriptor Classify(EventNode ev)
        {
            var trigger = ev.Trigger;
            var words = new string[trigger.Count];
            for (int i = 0; i < trigger.Count; i++)
                words[i] = trigger[i].Type == TokenType.Word ? trigger[i].Text : trigger[i].Text;

            if (words.Length >= 2 && words[0] == "every")
                return EveryDescriptor(trigger, ev);
            if (words.Length >= 2 && words[0] == "wheel")
                return new TriggerDescriptor
                {
                    Kind = words[words.Length - 1] == "up" ? TriggerKind.WheelScrollUp : TriggerKind.WheelScrollDown,
                };
            if (words.Length >= 2 && words[0] == "game" && words[words.Length - 1] == "starts")
                return new TriggerDescriptor { Kind = TriggerKind.GameStart };
            if (words.Length >= 2 && words[0] == "scene" && words[words.Length - 1] == "starts")
                return new TriggerDescriptor { Kind = TriggerKind.SceneStart };
            if (words.Length >= 3 && words[0] == "state" && words[1] == "is")
                return new TriggerDescriptor { Kind = TriggerKind.StateIs, RequireName = Join(trigger, 2, words.Length) };
            if (words.Length >= 3 && words[words.Length - 2] == "is" && words[words.Length - 1] != "clicked" && words[0] != "me" && words[0] != "other")
                return KeyDescriptor(words, trigger);
            if (words.Length >= 4 && words[words.Length - 3] == "is" && words[words.Length - 2] == "right" && words[words.Length - 1] == "clicked")
                return new TriggerDescriptor { Kind = TriggerKind.RightClicked, RequireName = Join(trigger, 0, words.Length - 3) };
            if (words.Length >= 4 && words[words.Length - 3] == "is" && words[words.Length - 2] == "middle" && words[words.Length - 1] == "clicked")
                return new TriggerDescriptor { Kind = TriggerKind.MiddleClicked, RequireName = Join(trigger, 0, words.Length - 3) };
            if (words.Length >= 3 && words[words.Length - 2] == "is" && words[words.Length - 1] == "clicked")
                return new TriggerDescriptor { Kind = TriggerKind.Clicked, RequireName = Join(trigger, 0, words.Length - 2) };
            if (words.Length >= 3 && words[0] == "message")
                return new TriggerDescriptor { Kind = TriggerKind.Message, RequireName = trigger[1].Text };
            if (words.Length >= 3 && words[0] == "anim")
                return new TriggerDescriptor { Kind = TriggerKind.AnimEnd, RequireName = trigger[1].Text };
            if (words.Length >= 2 && (words[0] == "me" || words[0] == "other"))
                return PronounDescriptor(trigger, ev);
            int becomesIndex = IndexOf(words, "becomes");
            if (becomesIndex >= 0)
                return WatchDescriptor(trigger, ev, becomesIndex, becomesNot: false);
            int notIndex = IndexOf(words, "not");
            if (notIndex > 0 && IndexOf(words, "becomes") > 0 && IndexOf(words, "becomes") < notIndex)
                return WatchDescriptor(trigger, ev, IndexOf(words, "becomes"), becomesNot: true);
            if (words.Length >= 2 && words[words.Length - 1] == "changes")
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.WatchChanges,
                    WatchOwner = Owner(words, IndexOf(words, "changes")),
                    WatchName = NameOf(words, IndexOf(words, "changes")),
                };
            return new TriggerDescriptor { Kind = TriggerKind.Unknown };
        }

        private static TriggerDescriptor EveryDescriptor(List<Token> trigger, EventNode ev)
        {
            double seconds = 1;
            if (trigger.Count >= 2 && trigger[1].Type == TokenType.Number)
                seconds = trigger[1].Number;
            return new TriggerDescriptor { Kind = TriggerKind.Every, Seconds = seconds };
        }

        private static TriggerDescriptor KeyDescriptor(string[] words, List<Token> trigger)
        {
            int isIndex = words.Length - 2;
            var keyName = Join(trigger, 0, isIndex);
            var key = new TriggerDescriptor { Key = keyName, RequireName = keyName };
            switch (words[words.Length - 1])
            {
                case "pressed":
                    key.Kind = TriggerKind.KeyPressed;
                    break;
                case "released":
                    key.Kind = TriggerKind.KeyReleased;
                    break;
                default:
                    key.Kind = TriggerKind.KeyHeld;
                    break;
            }
            return key;
        }

        private static TriggerDescriptor PronounDescriptor(List<Token> trigger, EventNode ev)
        {
            var words = new string[trigger.Count];
            for (int i = 0; i < trigger.Count; i++)
                words[i] = trigger[i].Text;
            var descriptor = new TriggerDescriptor();
            if (words.Length >= 3 && words[1] == "touches")
            {
                descriptor.Kind = TriggerKind.Touches;
                descriptor.RequireName = Join(trigger, 2, words.Length);
                descriptor.RequireMaterial = descriptor.RequireName;
                return descriptor;
            }
            if (words.Length >= 3 && words[1] == "overlaps")
            {
                descriptor.Kind = TriggerKind.Overlaps;
                descriptor.RequireName = Join(trigger, 2, words.Length);
                return descriptor;
            }
            if (words.Length >= 4 && words[1] == "arrives" && words[2] == "at")
            {
                descriptor.Kind = TriggerKind.Arrives;
                descriptor.RequireName = Join(trigger, 3, words.Length);
                return descriptor;
            }
            if (words.Length >= 3 && words[1] == "sees")
            {
                descriptor.Kind = TriggerKind.Sees;
                descriptor.RequireName = words[2];
                descriptor.Distance = DistanceBy(trigger);
                return descriptor;
            }
            if (words.Length >= 4 && words[1] == "stops" && words[2] == "seeing")
            {
                descriptor.Kind = TriggerKind.StopsSeeing;
                descriptor.RequireName = words[3];
                descriptor.Distance = DistanceBy(trigger);
                return descriptor;
            }
            if (words.Length >= 3 && words[1] == "gets")
            {
                descriptor.Kind = words[2] switch
                {
                    "equipped" => TriggerKind.Equipped,
                    "unequipped" => TriggerKind.Unequipped,
                    _ => TriggerKind.Dropped,
                };
                return descriptor;
            }
            if (words.Length >= 3 && words[1] == "is" && words[2] == "hovered")
            {
                descriptor.Kind = TriggerKind.Hovered;
                return descriptor;
            }
            if (words.Length >= 4 && words[1] == "is" && words[2] == "near")
            {
                descriptor.Kind = TriggerKind.Near;
                descriptor.RequireName = trigger[3].Text;
                descriptor.Distance = DistanceBy(trigger);
                return descriptor;
            }
            if (words.Length >= 2 && words[1] == "appears")
            {
                descriptor.Kind = TriggerKind.Appears;
                return descriptor;
            }
            return new TriggerDescriptor { Kind = TriggerKind.Unknown };
        }

        private static double DistanceBy(List<Token> trigger)
        {
            for (int i = 0; i < trigger.Count - 1; i++)
                if (trigger[i].Text == "by" && trigger[i + 1].Type == TokenType.Number)
                    return trigger[i + 1].Number;
            return -1;
        }

        private static TriggerDescriptor WatchDescriptor(List<Token> trigger, EventNode ev, int becomesIndex, bool becomesNot)
        {
            var descriptor = new TriggerDescriptor();
            if (trigger.Count > becomesIndex && trigger[becomesIndex + 1].Text == "not")
                descriptor.Kind = TriggerKind.WatchBecomesNot;
            else if (becomesNot)
                descriptor.Kind = TriggerKind.WatchBecomesNot;
            else
                descriptor.Kind = TriggerKind.WatchBecomes;
            int nameIndex = becomesIndex - 1;
            if (nameIndex - 1 >= 0 && trigger[nameIndex - 1].Text == "my")
            {
                descriptor.WatchOwner = "my";
                descriptor.WatchName = trigger[nameIndex].Text;
            }
            else if (nameIndex >= 0)
            {
                descriptor.WatchOwner = "";
                descriptor.WatchName = trigger[nameIndex].Text;
            }
            descriptor.WatchTarget = ValueFromToken(trigger[trigger.Count - 1]);
            return descriptor;
        }

        private static ValueNode ValueFromToken(Token token)
        {
            switch (token.Type)
            {
                case TokenType.Number:
                    return new NumberValue { Line = token.Line, Value = token.Number };
                case TokenType.Str:
                    return new TextValue { Line = token.Line, Text = token.Text };
                case TokenType.Word:
                    if (token.Text == "true" || token.Text == "false")
                        return new BooleanValue { Line = token.Line, Value = token.Text == "true" };
                    return new VariableValue { Line = token.Line, Name = token.Text };
                default:
                    return new NumberValue { Line = token.Line };
            }
        }

        private static int IndexOf(string[] words, string word)
        {
            for (int i = 0; i < words.Length; i++)
                if (words[i] == word)
                    return i;
            return -1;
        }

        private static string Join(List<Token> trigger, int from, int until)
        {
            var parts = new System.Text.StringBuilder();
            for (int i = from; i < until; i++)
            {
                if (parts.Length > 0)
                    parts.Append(' ');
                parts.Append(trigger[i].Text);
            }
            return parts.ToString();
        }

        private static string Owner(string[] words, int anchorIndex)
        {
            if (anchorIndex - 2 >= 0 && words[anchorIndex - 2] == "my")
                return "my";
            return "";
        }

        private static string NameOf(string[] words, int anchorIndex)
        {
            if (anchorIndex >= 1)
                return words[anchorIndex - 1];
            return "";
        }
    }

    public class PushSignal
    {
        public TriggerKind Kind;
        public object Other;
        /// <summary>If set, the pushed event runs with this actor as `me` (used by `equip`:
        /// an item's `gets equipped`/`gets unequipped` event runs with the wearer as `me`).</summary>
        public object MeOverride;
        public string Name = "";
        /// <summary>Physics-material name of the touched/colliding actor (contact triggers only).</summary>
        public string Material = "";
    }
}
