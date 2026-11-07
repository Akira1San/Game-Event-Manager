using System;
using System.Collections.Generic;

namespace Game
{
    public enum ConditionKind
    {
        Unknown,
        Equal,
        NotEqual,
        Higher,
        Lower,
        ThingExists,
        ThingMissing,
        KeyHeld,
        Chance,
        Near,
        Moving,
        HasTag,
        BagHas,
        ParticlePlaying,
        ButtonPressed,
        ButtonReleased,
        ButtonDisabled,
        // RTS selection (Task 18 Part A)
        Selected,
        SelectionCountIs,
        MyControlGroupAssigned,
    }

    public class ConditionNode
    {
        public ConditionKind Kind = ConditionKind.Unknown;
        public string Owner = "";
        public string Name = "";
        public ValueNode Right;
        public string Thing = "";
        public string Key = "";
        public string Tag = "";
        public double Percent;
        public double Distance;
        /// <summary>Control group number for <c>my control group &lt;n&gt; is assigned</c>.</summary>
        public int Group = -1;
    }

    public static class Conditions
    {
        public static ConditionNode Parse(List<Token> words)
        {
            var node = new ConditionNode();
            int start = 0;
            if (words[0].Type == TokenType.Word && words[0].Text == "unless")
                start = 1;
            var tokens = new List<Token>();
            for (int i = start; i < words.Count; i++)
                tokens.Add(words[i]);
            if (tokens.Count == 0)
                return node;
            var texts = new string[tokens.Count];
            for (int i = 0; i < tokens.Count; i++)
                texts[i] = tokens[i].Text;

            if (texts[0] == "chance" && tokens.Count >= 2)
            {
                string percentText = tokens[1].Text;
                if (percentText.EndsWith("%", StringComparison.Ordinal))
                    percentText = percentText.Substring(0, percentText.Length - 1);
                double.TryParse(percentText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out node.Percent);
                node.Kind = ConditionKind.Chance;
                return node;
            }

            if (texts.Length == 3 && texts[0] == "me" && texts[1] == "is" && texts[2] == "moving")
            {
                node.Kind = ConditionKind.Moving;
                return node;
            }

            // `<thing> is pressed / released / disabled` — steady-state button conditions.
            if (texts.Length == 3 && texts[1] == "is")
            {
                if (texts[2] == "pressed")
                {
                    node.Kind = ConditionKind.ButtonPressed;
                    node.Thing = texts[0];
                    return node;
                }
                if (texts[2] == "released")
                {
                    node.Kind = ConditionKind.ButtonReleased;
                    node.Thing = texts[0];
                    return node;
                }
                if (texts[2] == "disabled")
                {
                    node.Kind = ConditionKind.ButtonDisabled;
                    node.Thing = texts[0];
                    return node;
                }
            }

            // `particle "smoke" is playing` — true while that particle system runs on me.
            if (texts.Length >= 4 && texts[0] == "particle" && texts[2] == "is" && texts[3] == "playing")
            {
                node.Kind = ConditionKind.ParticlePlaying;
                node.Thing = tokens[1].Text;
                return node;
            }

            // ---- RTS selection (Task 18 Part A) ----
            if (texts.Length == 3 && texts[0] == "me" && texts[1] == "is" && texts[2] == "selected")
            {
                node.Kind = ConditionKind.Selected;
                return node;
            }
            if (texts.Length >= 4 && texts[0] == "selection" && texts[1] == "count" && texts[2] == "is")
            {
                node.Kind = ConditionKind.SelectionCountIs;
                node.Right = ValueFrom(tokens, texts, 3);
                return node;
            }
            {
                // `my control group 3 is assigned` (a unit's membership) and the global
                // `control group 3 is assigned` (the group holds at least one unit).
                int groupStart = texts[0] == "my" ? 1 : 0;
                if (texts.Length >= groupStart + 5 &&
                    texts[groupStart] == "control" && texts[groupStart + 1] == "group" &&
                    tokens[groupStart + 2].Type == TokenType.Number &&
                    texts[groupStart + 3] == "is" && texts[groupStart + 4] == "assigned")
                {
                    node.Kind = ConditionKind.MyControlGroupAssigned;
                    node.Group = (int)tokens[groupStart + 2].Number;
                    return node;
                }
            }

            int isIndex = IndexOf(texts, "is");
            if (isIndex > 0)
            {
                bool higher = Array.IndexOf(texts, "higher", isIndex) >= 0;
                bool lower = Array.IndexOf(texts, "lower", isIndex) >= 0;
                if (higher || lower)
                {
                    node.Kind = higher ? ConditionKind.Higher : ConditionKind.Lower;
                    SetSubject(node, tokens, texts, isIndex);
                    int thanIndex = higher ? Array.IndexOf(texts, "higher", isIndex) + 1 : Array.IndexOf(texts, "lower", isIndex) + 1;
                    node.Right = ValueFrom(tokens, texts, thanIndex + 1);
                    return node;
                }
                int nearIndex = IndexOf(texts, "near");
                if (nearIndex > 0)
                {
                    node.Kind = ConditionKind.Near;
                    SetSubject(node, tokens, texts, isIndex);
                    if (nearIndex + 1 < tokens.Count)
                        node.Thing = tokens[nearIndex + 1].Text;
                    int byIndex = Array.IndexOf(texts, "by");
                    if (byIndex >= 0 && byIndex + 1 < tokens.Count && tokens[byIndex + 1].Type == TokenType.Number)
                        node.Distance = tokens[byIndex + 1].Number;
                    return node;
                }
                bool held = Array.IndexOf(texts, "held", isIndex + 1) >= 0;
                if (held)
                {
                    node.Kind = ConditionKind.KeyHeld;
                    node.Key = JoinTexts(texts, 0, isIndex);
                    return node;
                }
                bool notFlag = Array.IndexOf(texts, "not", isIndex + 1) >= 0;
                node.Kind = notFlag ? ConditionKind.NotEqual : ConditionKind.Equal;
                SetSubject(node, tokens, texts, isIndex);
                int valueIndex = notFlag ? IndexOf(texts, "not", isIndex + 1) + 1 : isIndex + 1;
                node.Right = ValueFrom(tokens, texts, valueIndex);
                return node;
            }

            int bagIndex = IndexOf(texts, "bag");
            int hasIndex = IndexOf(texts, "has");
            if (bagIndex > 0 && hasIndex > bagIndex)
            {
                node.Kind = ConditionKind.BagHas;
                node.Owner = texts[bagIndex - 1] == "my" ? "my" : "";
                if (hasIndex + 1 < tokens.Count)
                    node.Thing = tokens[hasIndex + 1].Text;
                return node;
            }
            if (hasIndex > 0)
            {
                int tagIndex = IndexOf(texts, "tag");
                if (tagIndex > hasIndex)
                {
                    node.Kind = ConditionKind.HasTag;
                    node.Thing = texts[0];
                    if (tagIndex + 1 < texts.Length)
                        node.Tag = texts[tagIndex + 1];
                    return node;
                }
            }
            int existsIndex = Array.IndexOf(texts, "exists");
            if (existsIndex > 0)
            {
                node.Kind = ConditionKind.ThingExists;
                node.Thing = JoinTexts(texts, 0, existsIndex);
                return node;
            }
            int doesIndex = Array.IndexOf(texts, "exist");
            if (doesIndex > 0)
            {
                node.Kind = ConditionKind.ThingMissing;
                node.Thing = JoinTexts(texts, 0, Array.IndexOf(texts, "does"));
                return node;
            }
            return node;
        }

        public static bool Evaluate(ConditionNode node, EventContext context)
        {
            switch (node.Kind)
            {
                case ConditionKind.Chance:
                    return Rng.NextDouble() * 100 < node.Percent;
                case ConditionKind.KeyHeld:
                    return context.World.IsKeyHeld(node.Key);
                case ConditionKind.Equal:
                case ConditionKind.NotEqual:
                {
                    if (node.Right == null || IsPronoun(node))
                        return false;
                    ValueBox right = ValueEvaluator.Evaluate(node.Right, ContextResolver(context));
                    ValueBox left = ReadSubject(node, context);
                    bool equal = CompareEqual(left, right);
                    return node.Kind == ConditionKind.Equal ? equal : !equal;
                }
                case ConditionKind.Higher:
                case ConditionKind.Lower:
                {
                    if (node.Right == null || IsPronoun(node))
                        return false;
                    ValueBox right = ValueEvaluator.Evaluate(node.Right, ContextResolver(context));
                    ValueBox left = ReadSubject(node, context);
                    bool higher = left.AsNumber() > right.AsNumber();
                    return node.Kind == ConditionKind.Higher ? higher : !higher && left.AsNumber() < right.AsNumber();
                }
                case ConditionKind.ThingExists:
                    return context.Runtime.FindNamedActor(node.Thing) != null;
                case ConditionKind.ThingMissing:
                    return context.Runtime.FindNamedActor(node.Thing) == null;
                case ConditionKind.Near:
                {
                    object thing = context.Runtime.FindNamedActor(node.Thing);
                    object anchor = node.Name == "other" && context.Other != null ? context.Other : context.Me;
                    if (thing == null)
                        return false;
                    return context.World.Distance2D(anchor, thing) <= node.Distance;
                }
                case ConditionKind.Moving:
                    return context.World.IsMoving(context.Me);
                case ConditionKind.ParticlePlaying:
                    return context.World.IsParticlePlaying(context.Me, node.Thing);
                case ConditionKind.ButtonPressed:
                {
                    object target = FindTarget(node.Thing, context);
                    return target != null && context.World.IsButtonPressed(target);
                }
                case ConditionKind.ButtonReleased:
                {
                    object target = FindTarget(node.Thing, context);
                    return target != null && !context.World.IsButtonPressed(target);
                }
                case ConditionKind.ButtonDisabled:
                {
                    object target = FindTarget(node.Thing, context);
                    return target != null && context.World.IsButtonDisabled(target);
                }
                case ConditionKind.HasTag:
                {
                    object target = FindTarget(node.Thing, context);
                    return target != null && context.World.HasTag(target, node.Tag);
                }
                case ConditionKind.BagHas:
                {
                    object owner = context.Me;
                    return context.Runtime.BagHas(owner, node.Thing);
                }
                case ConditionKind.Selected:
                    return context.Me != null && context.Runtime.Selection.IsSelected(context.Me);
                case ConditionKind.SelectionCountIs:
                {
                    if (node.Right == null)
                        return false;
                    double wanted = ValueEvaluator.Evaluate(node.Right, ContextResolver(context)).AsNumber();
                    return context.Runtime.Selection.Count == wanted;
                }
                case ConditionKind.MyControlGroupAssigned:
                {
                    Selection selection = context.Runtime.Selection;
                    // A global sheet asks whether the group exists at all; a unit's sheet
                    // asks whether this unit belongs to the group.
                    if (context.Me == null || context.Me is GlobalHost)
                        return selection.IsControlGroupAssigned(node.Group);
                    return selection.IsInControlGroup(context.Me, node.Group);
                }
                default:
                    return false;
            }
        }

        public static bool CompareEqual(ValueBox left, ValueBox right)
        {
            switch (right.Kind)
            {
                case ValueKind.Number:
                case ValueKind.None:
                    return left.AsNumber() == right.AsNumber();
                case ValueKind.Text:
                    return left.AsText() == right.AsText();
                case ValueKind.Boolean:
                    return left.AsBoolean() == right.AsBoolean();
                default:
                    return false;
            }
        }

        private static void SetSubject(ConditionNode node, List<Token> tokens, string[] texts, int isIndex)
        {
            if (isIndex >= 2 && texts[isIndex - 2] == "my")
            {
                node.Owner = "my";
                node.Name = texts[isIndex - 1];
            }
            else if (isIndex >= 1)
            {
                node.Owner = "";
                node.Name = texts[isIndex - 1];
            }
        }

        private static ValueBox ReadSubject(ConditionNode node, EventContext context)
        {
            if (node.Owner == "my")
                return context.ReadMy(node.Name);
            return context.ReadGlobal(node.Name);
        }

        private static bool IsPronoun(ConditionNode node)
        {
            return node.Owner.Length == 0 && (node.Name == "me" || node.Name == "other"); 
        }

        private static object FindTarget(string thing, EventContext context)
        {
            if (thing == "me")
                return context.Me;
            if (thing == "other")
                return context.Other;
            return context.Runtime.FindNamedActor(thing);
        }

        private static ValueNode ValueFrom(List<Token> tokens, string[] texts, int index)
        {
            if (index < 0 || index >= tokens.Count)
                return null;
            var token = tokens[index];
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
                    return null;
            }
        }

        private static int IndexOf(string[] texts, string word)
        {
            return Array.IndexOf(texts, word);
        }

        private static int IndexOf(string[] texts, string word, int from)
        {
            return Array.IndexOf(texts, word, from);
        }

        private static string JoinTexts(string[] texts, int from, int until)
        {
            var parts = new System.Text.StringBuilder();
            for (int i = from; i < until; i++)
            {
                if (parts.Length > 0)
                    parts.Append(' ');
                parts.Append(texts[i]);
            }
            return parts.ToString();
        }

        private static VariableResolver ContextResolver(EventContext context)
        {
            return variable => variable.Owner == "my"
                ? context.ReadMy(variable.Name)
                : context.ReadGlobal(variable.Name);
        }
    }
}
