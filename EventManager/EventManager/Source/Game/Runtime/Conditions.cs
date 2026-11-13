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
        // Hearing (Task 16 Part B)
        Hears,
        // Typed enemies (Task 16 Part D)
        MyTypeIs,
        // Weapons & ammo (Task 16 Part C)
        WeaponIs,
        AmmoIs,
        Reloading,
        // Wander / still modes (Task 16 Part A)
        Wandering,
        Still,
        HasTag,
        BagHas,
        ParticlePlaying,
        ButtonPressed,
        ButtonReleased,
        ButtonDisabled,
        Focused,
        TextIs,
        ProgressIs,
        SliderIs,
        // RTS selection (Task 18 Part A)
        Selected,
        SelectionCountIs,
        MyControlGroupAssigned,
        // RTS resource economy (Task 18 Part B)
        CanAfford,
        // RTS construction & base building (Task 18 Part C)
        UnderConstruction,
        EnoughFor,
        // RTS production queues & unit training (Task 18 Part D)
        ProductionIdle,
        QueueCount,
        HasBuildQueue,
        // RTS orders & attack-move (Task 18 Part E)
        CurrentOrderIs,
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
            if (words[0].Type == TokenType.Word && (words[0].Text == "unless" || words[0].Text == "if"))
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

            // `me hears "gunshot" by 12` — Task 16 Part B. `by` is optional: without it the
            // sound's own radius decides how far it carries.
            if (texts.Length >= 3 && texts[0] == "me" && texts[1] == "hears")
            {
                node.Kind = ConditionKind.Hears;
                node.Thing = tokens[2].Text;
                for (int i = 3; i < tokens.Count - 1; i++)
                    if (tokens[i].Text == "by" && tokens[i + 1].Type == TokenType.Number)
                        node.Distance = tokens[i + 1].Number;
                return node;
            }

            // `my type is Zombie` / `my type is "Zombie"` — the name `enemy type` recorded.
            // A bare word names the type rather than a variable, so this sits before the
            // generic `<var> is <value>` handling.
            if (texts.Length == 4 && texts[0] == "my" && texts[1] == "type" && texts[2] == "is")
            {
                node.Kind = ConditionKind.MyTypeIs;
                node.Name = tokens[3].Text;
                return node;
            }

            // `me is wandering` / `me is still` — Task 16 Part A modes.
            if (texts.Length == 3 && texts[0] == "me" && texts[1] == "is" &&
                (texts[2] == "wandering" || texts[2] == "still"))
            {
                node.Kind = texts[2] == "wandering" ? ConditionKind.Wandering : ConditionKind.Still;
                return node;
            }

            // ---- weapons & ammo (Task 16 Part C) ----
            // `reloading` / `me is reloading`: true until the current reload timer runs out.
            if ((texts.Length == 1 && texts[0] == "reloading") ||
                (texts.Length == 3 && texts[0] == "me" && texts[1] == "is" && texts[2] == "reloading"))
            {
                node.Kind = ConditionKind.Reloading;
                return node;
            }
            // `weapon is Pistol` — reads `my Weapon`, which `set weapon to` wrote. Sits before
            // the generic `<var> is <value>` shape so `weapon` is never a global variable.
            if (texts.Length == 3 && texts[0] == "weapon" && texts[1] == "is")
            {
                node.Kind = ConditionKind.WeaponIs;
                node.Name = tokens[2].Text;
                return node;
            }
            // `ammo Pistol is 0` / `ammo my Weapon is 0` — that weapon's ammo count on the acting
            // actor (`my <variable>` reads the name from the variable, e.g. the current weapon).
            if (texts.Length >= 5 && texts[0] == "ammo" && (texts[1] == "my" || texts[1] == "me") &&
                texts[3] == "is")
            {
                node.Kind = ConditionKind.AmmoIs;
                node.Key = texts[2];
                node.Right = ValueFrom(tokens, texts, 4);
                return node;
            }
            if (texts.Length >= 4 && texts[0] == "ammo" && texts[2] == "is")
            {
                node.Kind = ConditionKind.AmmoIs;
                node.Thing = tokens[1].Text;
                node.Right = ValueFrom(tokens, texts, 3);
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

            // `<thing> is focused` / `text of <thing> is <value>` — UI text input (Task 15 Part C).
            // These sit before the generic `<var> is <value>` handling so the control name is
            // never mistaken for a variable.
            if (texts.Length == 3 && texts[1] == "is" && texts[2] == "focused")
            {
                node.Kind = ConditionKind.Focused;
                node.Thing = texts[0];
                return node;
            }
            if (texts.Length >= 5 && texts[0] == "text" && texts[1] == "of" && texts[3] == "is")
            {
                node.Kind = ConditionKind.TextIs;
                node.Thing = texts[2];
                node.Right = ValueFrom(tokens, texts, 4);
                return node;
            }

            // `progress of <thing> is <value>` / `slider <thing> is <value>` — UI control values.
            if (texts.Length >= 5 && texts[0] == "progress" && texts[1] == "of" && texts[3] == "is")
            {
                node.Kind = ConditionKind.ProgressIs;
                node.Thing = texts[2];
                node.Right = ValueFrom(tokens, texts, 4);
                return node;
            }
            if (texts.Length == 4 && texts[0] == "slider" && texts[2] == "is")
            {
                node.Kind = ConditionKind.SliderIs;
                node.Thing = texts[1];
                node.Right = ValueFrom(tokens, texts, 3);
                return node;
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

            // ---- RTS resource economy (Task 18 Part B) ----
            // `can afford Barracks` — data-driven cost check (see ResourceManager).
            if (texts.Length >= 3 && texts[0] == "can" && texts[1] == "afford")
            {
                node.Kind = ConditionKind.CanAfford;
                node.Thing = JoinTexts(texts, 2, texts.Length);
                return node;
            }

            // ---- RTS construction & base building (Task 18 Part C) ----
            // `enough Minerals for Barracks` — one resource against a data-driven cost.
            if (texts.Length >= 4 && texts[0] == "enough")
            {
                int forIndex = IndexOf(texts, "for");
                if (forIndex > 1 && forIndex + 1 < texts.Length)
                {
                    node.Kind = ConditionKind.EnoughFor;
                    node.Name = ResourceManager.CanonicalOrName(JoinTexts(texts, 1, forIndex));
                    node.Thing = JoinTexts(texts, forIndex + 1, texts.Length);
                    return node;
                }
            }
            // `Barracks is under construction` — must precede the generic `is` handling.
            if (texts.Length >= 4 && texts[texts.Length - 3] == "is" &&
                texts[texts.Length - 2] == "under" && texts[texts.Length - 1] == "construction")
            {
                node.Kind = ConditionKind.UnderConstruction;
                node.Thing = JoinTexts(texts, 0, texts.Length - 3);
                return node;
            }

            // ---- RTS production queues & unit training (Task 18 Part D) ----
            // `Barracks is idle` / `me is idle` — nothing is currently training.
            if (texts.Length >= 3 && texts[texts.Length - 2] == "is" && texts[texts.Length - 1] == "idle")
            {
                node.Kind = ConditionKind.ProductionIdle;
                node.Thing = JoinTexts(texts, 0, texts.Length - 2);
                return node;
            }
            // `queue count is <n>` — the acting building's pending orders.
            if (texts.Length >= 4 && texts[0] == "queue" && texts[1] == "count" && texts[2] == "is")
            {
                node.Kind = ConditionKind.QueueCount;
                node.Right = ValueFrom(tokens, texts, 3);
                return node;
            }
            // `me has build queue` — this actor is a production building (it has trained
            // before, even when its queue is currently empty).
            if (texts.Length == 4 && texts[0] == "me" && texts[1] == "has" &&
                texts[2] == "build" && texts[3] == "queue")
            {
                node.Kind = ConditionKind.HasBuildQueue;
                return node;
            }

            // ---- RTS orders & attack-move (Task 18 Part E) ----
            // `my current order is attack-move` / `my order target is Enemy` /
            // `my order queue is 2` — multiword state that the OrderManager publishes.
            if (texts.Length >= 5 && texts[0] == "my" && texts[3] == "is")
            {
                string subject = texts[1] + " " + texts[2];
                if (subject == "current order" || subject == "order target" || subject == "order queue")
                {
                    bool notFlag = texts.Length >= 6 && texts[4] == "not";
                    node.Kind = subject == "current order" && !notFlag
                        ? ConditionKind.CurrentOrderIs
                        : notFlag ? ConditionKind.NotEqual : ConditionKind.Equal;
                    node.Owner = "my";
                    node.Name = subject;
                    int valueIndex = notFlag ? 5 : 4;
                    if (subject == "order queue")
                    {
                        node.Right = ValueFrom(tokens, texts, valueIndex);
                    }
                    else if (valueIndex >= 0 && valueIndex < tokens.Count && tokens[valueIndex].Type == TokenType.Word)
                    {
                        // Order names and targets are words, not variables: `my current
                        // order is attack-move` compares against the literal text.
                        node.Right = new TextValue
                        {
                            Line = tokens[valueIndex].Line,
                            Text = JoinTexts(texts, valueIndex, texts.Length),
                        };
                    }
                    else
                    {
                        node.Right = ValueFrom(tokens, texts, valueIndex);
                    }
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
                    if (!TrySetResourceSubject(node, texts, isIndex))
                    {
                        // `build progress is higher than 50` reads the mirrored local.
                        if (JoinTexts(texts, 0, isIndex) == "build progress")
                        {
                            node.Owner = "my";
                            node.Name = "BuildProgress";
                        }
                        else
                        {
                            SetSubject(node, tokens, texts, isIndex);
                        }
                    }
                    int opIndex = higher ? Array.IndexOf(texts, "higher", isIndex) : Array.IndexOf(texts, "lower", isIndex);
                    node.Right = ValueFromSpan(tokens, texts, opIndex + 2);
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
                case ConditionKind.Hears:
                    return context.Runtime != null && context.Me != null &&
                           context.Runtime.Sounds.IsAudible(node.Thing, context.Me, context.World, node.Distance);
                case ConditionKind.MyTypeIs:
                    return string.Equals(context.ReadMy("Type").AsText(), node.Name,
                        StringComparison.OrdinalIgnoreCase);                case ConditionKind.Wandering:
                    return context.Runtime != null && context.Runtime.Wander.IsWandering(context.Me);
                case ConditionKind.WeaponIs:
                    return string.Equals(context.ReadMy(WeaponData.CurrentWeaponVariable).AsText(), node.Name,
                        StringComparison.OrdinalIgnoreCase);
                case ConditionKind.AmmoIs:
                {
                    if (node.Right == null)
                        return false;
                    string weapon = node.Key.Length > 0 ? context.ReadMy(node.Key).AsText() : node.Thing;
                    double held = WeaponData.ReadAmmo(context, weapon);
                    double wanted = ValueEvaluator.Evaluate(node.Right, ContextResolver(context)).AsNumber();
                    return Math.Abs(held - wanted) < 0.0001;
                }
                case ConditionKind.Reloading:
                    return context.Runtime != null && context.Runtime.Weapons.IsReloading(context.Me, context.Runtime.Time);
                case ConditionKind.Still:
                    return context.Runtime != null && context.Runtime.Wander.IsStill(context.Me);
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
                case ConditionKind.Focused:
                {
                    object target = FindTarget(node.Thing, context);
                    return target != null && context.World.IsFocused(target);
                }
                case ConditionKind.TextIs:
                {
                    if (node.Right == null)
                        return false;
                    string wanted = ValueEvaluator.Evaluate(node.Right, ContextResolver(context)).AsText();
                    object target = FindTarget(node.Thing, context);
                    return target != null && context.World.TextOf(target) == wanted;
                }
                case ConditionKind.ProgressIs:
                {
                    if (node.Right == null)
                        return false;
                    double wanted = ValueEvaluator.Evaluate(node.Right, ContextResolver(context)).AsNumber();
                    if (wanted > 1)
                        wanted /= 100; // 0-100 form: 50 means half full
                    object target = FindTarget(node.Thing, context);
                    return target != null && Math.Abs(context.World.ProgressOf(target) - wanted) < 0.001;
                }
                case ConditionKind.SliderIs:
                {
                    if (node.Right == null)
                        return false;
                    double wanted = ValueEvaluator.Evaluate(node.Right, ContextResolver(context)).AsNumber();
                    if (wanted <= 1)
                        wanted *= 100; // 0-1 form: 0.5 means 50 on a 0-100 slider
                    object target = FindTarget(node.Thing, context);
                    return target != null && Math.Abs(context.World.SliderOf(target) - wanted) < 1.0;
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
                case ConditionKind.CanAfford:
                    return ResourceManager.CanAfford(context.Variables, node.Thing);
                case ConditionKind.UnderConstruction:
                {
                    object actor = node.Thing == "me"
                        ? context.Me
                        : context.Runtime.FindNamedActor(node.Thing);
                    return context.Runtime.Construction.IsUnderConstruction(actor);
                }
                case ConditionKind.EnoughFor:
                {
                    // A building with no cost variable is always covered.
                    if (!ResourceManager.TryCost(context.Variables, node.Thing, node.Name, out double cost))
                        return true;
                    return ResourceManager.ReadNumber(context.Variables, node.Name) >= cost;
                }
                case ConditionKind.ProductionIdle:
                {
                    object actor = FindTarget(node.Thing, context);
                    return actor != null && context.Runtime.Production.IsIdle(actor);
                }
                case ConditionKind.QueueCount:
                {
                    if (node.Right == null)
                        return false;
                    double wanted = ValueEvaluator.Evaluate(node.Right, ContextResolver(context)).AsNumber();
                    return context.Runtime.Production.QueueCount(context.Me) == wanted;
                }
                case ConditionKind.HasBuildQueue:
                    return context.Runtime.Production.HasQueue(context.Me);
                case ConditionKind.CurrentOrderIs:
                {
                    if (node.Right == null)
                        return false;
                    ValueBox wanted = ValueEvaluator.Evaluate(node.Right, ContextResolver(context));
                    return CompareEqual(ValueBox.OfText(context.Runtime.Orders.CurrentOrderName(context.Me)), wanted);
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

        /// <summary>Recognises a (possibly multiword) global resource on the left of
        /// <c>is</c>, e.g. <c>Supply Used is lower than Supply Max</c>.</summary>
        private static bool TrySetResourceSubject(ConditionNode node, string[] texts, int isIndex)
        {
            if (texts.Length == 0 || texts[0] == "my")
                return false;
            string name = JoinTexts(texts, 0, isIndex);
            if (!ResourceManager.TryCanonical(name, out string canonical))
                return false;
            node.Owner = "";
            node.Name = canonical;
            return true;
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

        /// <summary>
        /// Reads the right-hand side of a comparison. A single token behaves as before;
        /// a run of words is first tried as a resource name (`Supply Max`), then folded
        /// back to its first token.
        /// </summary>
        private static ValueNode ValueFromSpan(List<Token> tokens, string[] texts, int index)
        {
            if (index < 0 || index >= tokens.Count)
                return null;
            if (index + 1 < tokens.Count)
            {
                string name = JoinTexts(texts, index, tokens.Count);
                if (ResourceManager.TryCanonical(name, out string canonical))
                    return new VariableValue { Line = tokens[index].Line, Name = canonical };
            }
            return ValueFrom(tokens, texts, index);
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
