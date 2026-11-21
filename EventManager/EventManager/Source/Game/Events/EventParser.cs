using System;
using System.Collections.Generic;

namespace Game
{
    public class EventParser
    {
        private static readonly string[] MathOps = { "+", "-", "*", "/" };

        /// <summary>How many operators one `set` value may carry (Task 29 Part A).</summary>
        private const int MaxSetOperators = 3;

        private List<Token> tokens = new List<Token>();
        private int pos;

        public ProgramFile Parse(string source)
            => Parse(new EventLexer().Tokenize(source));

        public ProgramFile Parse(List<Token> tokenList)
        {
            tokens = tokenList;
            pos = 0;
            var file = new ProgramFile();
            while (pos < tokens.Count)
            {
                var t = tokens[pos];
                if (t.Type == TokenType.NewLine)
                {
                    pos++;
                    continue;
                }
                if (t.Type == TokenType.EndOfFile)
                    break;
                if (t.Type == TokenType.Word && t.Text == "when")
                {
                    file.Events.Add(ParseEvent());
                    continue;
                }
                if (t.Type == TokenType.Word && t.Text == "bind")
                {
                    // `bind` is sugar: it expands into ordinary `when <var> changes` events here.
                    file.Events.AddRange(ParseBind());
                    continue;
                }
                if (t.Type == TokenType.Indent || t.Type == TokenType.Dedent)
                    throw new ParseException("this line is indented, but nothing above it was a `when` to belong to", t.Line);
                if (t.Type == TokenType.Number)
                    throw new ParseException($"a line can't start with the number {t.Text} — lines start with a word, and events start with `when`", t.Line);
                if (t.Type == TokenType.Str)
                    throw new ParseException("a line can't start with quoted text — lines start with a word, and events start with `when`", t.Line);
                throw UnknownWord(t);
            }
            return file;
        }

        private static readonly string[][] TriggerShapes = BuildTriggerShapes();

        private static string[][] BuildTriggerShapes()
        {
            var shapes = new List<string[]>();
            foreach (var entry in WordRegistry.All)
            {
                if (entry.Kind != "trigger")
                    continue;
                shapes.Add(SplitShape(entry.Word));
                if (entry.AltShape.Length > 0)
                    shapes.Add(SplitShape(entry.AltShape));
            }
            return shapes.ToArray();
        }

        private static string[] SplitShape(string template)
        {
            var parts = new List<string>();
            int index = 0;
            while (index < template.Length)
            {
                while (index < template.Length && template[index] == ' ')
                    index++;
                int start = index;
                while (index < template.Length && template[index] != ' ')
                    index++;
                if (index > start)
                    parts.Add(template.Substring(start, index - start));
            }
            return parts.ToArray();
        }

        /// <summary>True for a percentage operand like <c>60%</c> — the lexer keeps it one word token,
        /// the same shape the numeric helpers read.</summary>
        private static bool IsPercentToken(string text)
        {
            return text.Length > 1 && text.EndsWith("%", StringComparison.Ordinal) &&
                   double.TryParse(text.Substring(0, text.Length - 1),
                                   System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out _);
        }

        private static bool TryMatchShape(string[] shape, int si, List<Token> trigger, int ti)
        {
            if (si >= shape.Length)
                return ti == trigger.Count;
            string part = shape[si];
            if (part.Contains("<"))
            {
                // `<n>%` — a percentage written as one token, like `60%` (Task 33 Part B).
                bool percent = part.EndsWith("%", StringComparison.Ordinal);
                string name = part.Replace("\"", "").Replace("<", "").Replace(">", "");
                if (percent)
                    name = name.Substring(0, name.Length - 1);
                if (name == "value")
                {
                    return ti < trigger.Count && si == shape.Length - 1;
                }
                if (percent)
                    return ti < trigger.Count && trigger[ti].Type == TokenType.Word &&
                           IsPercentToken(trigger[ti].Text) &&
                           TryMatchShape(shape, si + 1, trigger, ti + 1);
                if (name == "n" || name == "dist")
                {
                    return ti < trigger.Count && trigger[ti].Type == TokenType.Number &&
                           TryMatchShape(shape, si + 1, trigger, ti + 1);
                }
                // `<name>` is a quoted string; `<marker>` is one too (a clip's authored marker,
                // Task 33 Part B), so both match a Str token.
                if (name == "name" || name == "marker")
                {
                    return ti < trigger.Count && trigger[ti].Type == TokenType.Str &&
                           TryMatchShape(shape, si + 1, trigger, ti + 1);
                }
            for (int take = 1; ti + take - 1 < trigger.Count; take++)
            {
                var tt = trigger[ti + take - 1];
                if (tt.Type != TokenType.Word && tt.Type != TokenType.Number)
                    break;
                if (TryMatchShape(shape, si + 1, trigger, ti + take))
                    return true;
            }
            return false;
            }
            return ti < trigger.Count && trigger[ti].Type == TokenType.Word && trigger[ti].Text == part &&
                   TryMatchShape(shape, si + 1, trigger, ti + 1);
        }

        private void ValidateTrigger(Token whenToken, List<Token> trigger)
        {
            var full = new List<Token> { whenToken };
            full.AddRange(trigger);
            var basePart = full;
            for (int i = 1; i < full.Count; i++)
            {
                if (full[i].Type == TokenType.Word && full[i].Text == "and")
                {
                    if (i == full.Count - 1)
                        throw new ParseException("`and` joins conditions — it needs something after it, like `when me overlaps Enemy and other is a Player`", full[i].Line);
                    basePart = full.GetRange(0, i);
                    break;
                }
            }
            foreach (var shape in TriggerShapes)
            {
                if (TryMatchShape(shape, 0, basePart, 0))
                    return;
            }
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < full.Count; i++)
            {
                if (i > 0)
                    text.Append(' ');
                text.Append(full[i].Type == TokenType.Str ? "\"" + full[i].Text + "\"" : full[i].Text);
            }
            throw new ParseException($"I don't know this trigger — \u201C{text}\u201D isn't a `when` line I recognize. Triggers look like \u201Cwhen game starts\u201D or \u201Cwhen W is pressed\u201D.", whenToken.Line);
        }

        private EventNode ParseEvent()
        {
            var whenToken = tokens[pos];
            var ev = new EventNode { Line = whenToken.Line };
            pos++;
            while (pos < tokens.Count &&
                   (tokens[pos].Type == TokenType.Word || tokens[pos].Type == TokenType.Number || tokens[pos].Type == TokenType.Str))
                ev.Trigger.Add(tokens[pos++]);
            if (ev.Trigger.Count == 0)
                throw new ParseException("a `when` needs something after it — like `when game starts`", whenToken.Line);
            // `state <name>` is a trigger modifier: gates this event on the sheet's current state.
            // `when state is <name>` is the StateIs trigger itself — don't extract a modifier from it.
            bool isStateIsTrigger = ev.Trigger.Count >= 2 &&
                ev.Trigger[0].Type == TokenType.Word && ev.Trigger[0].Text == "state" &&
                ev.Trigger[1].Type == TokenType.Word && ev.Trigger[1].Text == "is";
            if (!isStateIsTrigger)
            {
                for (int i = 0; i < ev.Trigger.Count - 1; i++)
                {
                    if (ev.Trigger[i].Type == TokenType.Word && ev.Trigger[i].Text == "state" &&
                        ev.Trigger[i + 1].Type == TokenType.Word)
                    {
                        ev.RequiredState = ev.Trigger[i + 1].Text;
                        ev.Trigger.RemoveAt(i);
                        ev.Trigger.RemoveAt(i);
                        break;
                    }
                }
            }
            ValidateTrigger(whenToken, ev.Trigger);
            if (pos < tokens.Count && tokens[pos].Type == TokenType.NewLine)
                pos++;
            if (pos >= tokens.Count || tokens[pos].Type != TokenType.Indent)
            {
                int anchorLine = pos < tokens.Count ? tokens[pos].Line : whenToken.Line;
                throw new ParseException("this `when` line has nothing indented under it — an event needs at least one line (indented 2 spaces) saying what happens", anchorLine);
            }
            pos++;
            while (true)
            {
                var t = tokens[pos];
                if (t.Type == TokenType.Dedent)
                {
                    pos++;
                    break;
                }
                if (t.Type == TokenType.EndOfFile)
                    break;
                if (t.Type == TokenType.NewLine)
                {
                    pos++;
                    continue;
                }
                if (t.Type == TokenType.Indent)
                    throw TooDeep(t);
                ev.Body.Add(ParseBodyLine(ev.Body));
            }
            return ev;
        }

        private ParseException TooDeep(Token indentToken)
        {
            int look = pos + 1;
            while (look < tokens.Count && tokens[look].Type == TokenType.NewLine)
                look++;
            string message = look < tokens.Count && tokens[look].Type == TokenType.Word && tokens[look].Text == "when"
                ? "events stack one after another — a `when` never sits inside another event"
                : "this line is indented too deep — every line under a `when` starts at the same 2 spaces";
            return new ParseException(message, indentToken.Line);
        }

        /// <summary>
        /// <c>bind &lt;label&gt; to &lt;variable&gt;</c>, <c>bind progress of &lt;thing&gt; to &lt;variable&gt;</c>,
        /// plus <c>and &lt;variable&gt;</c> and <c>format "&lt;template&gt;"</c>. Returns the ordinary
        /// change events the line stands for (see <see cref="Bindings.Expand"/>).
        /// </summary>
        private List<EventNode> ParseBind()
        {
            var bindToken = tokens[pos];
            pos++;
            var words = new List<Token>();
            while (pos < tokens.Count &&
                   (tokens[pos].Type == TokenType.Word || tokens[pos].Type == TokenType.Number || tokens[pos].Type == TokenType.Str))
                words.Add(tokens[pos++]);
            if (pos < tokens.Count && tokens[pos].Type == TokenType.NewLine)
                pos++;
            if (pos < tokens.Count && tokens[pos].Type == TokenType.Indent)
                throw new ParseException("`bind` makes its own event, so nothing is indented under it — write `bind ScoreLabel to Score` on its own line", bindToken.Line);

            var spec = new BindSpec { Line = bindToken.Line };
            int index = 0;
            if (index < words.Count && words[index].Type == TokenType.Word &&
                (words[index].Text == "text" || words[index].Text == "progress"))
            {
                spec.Kind = words[index].Text == "progress" ? BindKind.Progress : BindKind.Text;
                index++;
            }
            if (index < words.Count && words[index].Type == TokenType.Word && words[index].Text == "of")
                index++;
            if (index >= words.Count || words[index].Type != TokenType.Word)
                throw new ParseException("`bind` needs a control to keep in sync, like `bind ScoreLabel to Score`", bindToken.Line);
            spec.Thing = words[index].Text;
            index++;
            if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != "to")
                throw new ParseException("a `bind` line joins the control and a variable with `to` — `bind ScoreLabel to Score`", bindToken.Line);
            index++;
            spec.Variables.Add(ReadVariable(words, ref index, bindToken.Line, "`bind`"));
            while (index < words.Count && words[index].Type == TokenType.Word && words[index].Text == "and")
            {
                index++;
                spec.Variables.Add(ReadVariable(words, ref index, bindToken.Line, "`bind`"));
            }
            if (index < words.Count)
            {
                if (words[index].Type != TokenType.Word || words[index].Text != "format")
                    throw new ParseException("after the variable, `bind` only takes `and <variable>` or `format \"<template>\"`", words[index].Line);
                index++;
                if (index >= words.Count || words[index].Type != TokenType.Str)
                    throw new ParseException("`format` needs the template in quotes, like `bind HealthLabel to Health format \"Health: {0}\"`", bindToken.Line);
                if (spec.Kind == BindKind.Progress)
                    throw new ParseException("a progress bar can't use `format` — it takes a number from one variable", words[index].Line);
                spec.HasTemplate = true;
                spec.Template = words[index].Text;
                index++;
                CheckEndOfLine(words, index, bindToken.Line, "bind <label> to <variable> format \"<template>\"");
                CheckTemplateArguments(spec, bindToken.Line);
            }
            else if (spec.Variables.Count > 1)
            {
                throw new ParseException("binding several variables needs a `format` template — like `bind HealthLabel to Health and MaxHealth format \"Health: {0}/{1}\"`", bindToken.Line);
            }
            return Bindings.Expand(spec);
        }

        /// <summary>Every <c>{n}</c> placeholder needs a variable to fill it.</summary>
        private static void CheckTemplateArguments(BindSpec spec, int line)
        {
            for (int i = 0; i < spec.Template.Length; i++)
            {
                if (spec.Template[i] != '{')
                    continue;
                int end = spec.Template.IndexOf('}', i + 1);
                if (end < 0)
                    break;
                string inner = spec.Template.Substring(i + 1, end - i - 1);
                if (!int.TryParse(inner, out int argIndex))
                {
                    i = end;
                    continue;
                }
                if (argIndex < 0 || argIndex >= spec.Variables.Count)
                    throw new ParseException(
                        $"the template placeholder {{{inner}}} has no variable — this `bind` has {spec.Variables.Count} variable{(spec.Variables.Count == 1 ? "" : "s")} (they fill {{0}}, {{1}}, ... in order)",
                        line);
                i = end;
            }
        }

        private BodyLineNode ParseBodyLine()
        {
            var line = new BodyLineNode { Line = tokens[pos].Line };
            while (pos < tokens.Count &&
                   (tokens[pos].Type == TokenType.Word || tokens[pos].Type == TokenType.Number || tokens[pos].Type == TokenType.Str))
                line.Words.Add(tokens[pos++]);
            Classify(line);
            if (pos < tokens.Count && tokens[pos].Type == TokenType.NewLine)
                pos++;
            return line;
        }

        private BodyLineNode ParseBodyLine(List<BodyLineNode> body)
        {
            var line = ParseBodyLine();
            if (line.IsOtherwise)
                ValidateOtherwise(body, line);
            return line;
        }

        /// <summary><c>otherwise</c> pairs with the nearest condition line above it, and each
        /// condition gets at most one <c>otherwise</c>.</summary>
        private static void ValidateOtherwise(List<BodyLineNode> body, BodyLineNode line)
        {
            int lastCondition = -1;
            bool alreadyPaired = false;
            for (int i = body.Count - 1; i >= 0; i--)
            {
                if (body[i].IsCondition)
                {
                    lastCondition = i;
                    break;
                }
                if (body[i].IsOtherwise)
                    alreadyPaired = true;
            }
            if (lastCondition < 0 || alreadyPaired)
                throw new ParseException("put an `if`/`unless` line right above `otherwise`", line.Line);
        }

        private void Classify(BodyLineNode line)
        {
            var first = line.Words[0];
            if (first.Type != TokenType.Word)
            {
                string kind = first.Type == TokenType.Number ? "a number" : "quoted text";
                throw new ParseException($"a line can't start with {kind} — lines start with a word", first.Line);
            }
            if (first.Text == "when")
                throw new ParseException("events stack one after another — a `when` never sits inside another event", first.Line);
            if (first.Text == "bind")
                throw new ParseException("`bind` makes its own event, so it sits on its own line — like `bind ScoreLabel to Score` — not indented under a `when`", first.Line);
            if (first.Text == "if")
            {
                line.IsCondition = true;
                return;
            }
            if (first.Text == "otherwise" || first.Text == "else")
            {
                if (line.Words.Count > 1)
                    throw new ParseException("`otherwise` stands alone on its line — it opens the block that runs when the condition above it is false", first.Line);
                line.IsOtherwise = true;
                return;
            }
            if (first.Text == "unless")
            {
                line.IsCondition = true;
                return;
            }
            // `all players dead` (Task 16 Part F) is a condition with no condition mark in it,
            // so name its shape here rather than adding `all` to the mark list.
            if (first.Text == "all" && line.Words.Count >= 2 && line.Words[1].Type == TokenType.Word &&
                line.Words[1].Text == "players")
            {
                line.IsCondition = true;
                return;
            }
            // Weapon words (Task 16 Part C). `reload` is an action, and `weapon <name>` sets the
            // current weapon — while `weapon is Pistol` is the condition of the same shape, so
            // the check happens here, before the action-verb branch below.
            if (first.Text == "reload")
            {
                var reloadNode = new ReloadNode { Line = line.Line };
                int reloadIndex = 1;
                if (reloadIndex < line.Words.Count)
                {
                    if (!TryReadWeaponName(line.Words, ref reloadIndex, out string reloadWeapon, out string reloadVariable))
                        throw new ParseException("`reload` stands alone, or names a weapon like `reload Shotgun`", first.Line);
                    reloadNode.Weapon = reloadWeapon;
                    reloadNode.WeaponVariable = reloadVariable;
                }
                CheckEndOfLine(line.Words, reloadIndex, line.Line, "reload");
                line.Statement = reloadNode;
                return;
            }
            if (first.Text == "weapon")
            {
                if (line.Words.Count >= 2 && line.Words[1].Type == TokenType.Word && line.Words[1].Text == "is")
                {
                    line.IsCondition = true;
                    return;
                }
                if (line.Words.Count < 2)
                    throw new ParseException("`weapon` needs a name, like `weapon \"Shotgun\"` — or use `weapon is Shotgun` as a condition", first.Line);
                var weaponNode = new SetWeaponNode { Line = line.Line };
                int weaponIndex = 1;
                weaponNode.Value = ReadValue(line.Words, ref weaponIndex, line.Line);
                CheckEndOfLine(line.Words, weaponIndex, line.Line, "weapon \"Shotgun\"");
                line.Statement = weaponNode;
                return;
            }
            // Action verbs take priority over condition marks, so `print W is held`
            // isn't misclassified as a condition just because `is` is a condition mark.
            // Cursor words (Task 48 Part E): `set cursor "Attack"`, `set cursor to default`,
            // `push cursor "Attack"` / `pop cursor`, `hide cursor` / `show cursor`. They are
            // checked before the word table because `pop` is not an action word anywhere else -
            // and before the generic `set` / `push` / `hide` forms, so "cursor" is never read as
            // a variable name or as an actor to hide.
            if (line.Words.Count >= 2 && line.Words[1].Type == TokenType.Word &&
                line.Words[1].Text == "cursor")
            {
                CursorNode cursor = ParseCursor(line);
                if (cursor != null)
                {
                    line.Statement = cursor;
                    return;
                }
            }
            // `clear dialogue` — dismiss the whole open dialogue in one word (Task 35 Part D).
            // Checked before the action-word table so `dialogue` is never read as a thing.
            if (first.Text == "clear" && line.Words.Count >= 2 &&
                line.Words[1].Type == TokenType.Word && line.Words[1].Text == "dialogue")
            {
                CheckEndOfLine(line.Words, 2, line.Line, "clear dialogue");
                line.Statement = new ClearDialogueNode { Line = line.Line };
                return;
            }
            // `show choice "…"` — numbered dialogue choices (Task 35 Part C). Checked before the
            // action-word table so `choice` is never read as an actor to show.
            if (first.Text == "show" && line.Words.Count >= 2 &&
                line.Words[1].Type == TokenType.Word && line.Words[1].Text == "choice")
            {
                line.Statement = ParseShowChoice(line);
                return;
            }
            // `type text "…" into <thing> speed <n>` — the typewriter (Task 35 Part A). Checked
            // before the action-word table so its own statement carries the timing.
            if (first.Text == "type" && line.Words.Count >= 2 &&
                line.Words[1].Type == TokenType.Word && line.Words[1].Text == "text")
            {
                line.Statement = ParseTypeText(line);
                return;
            }
            // `expect <condition>` — Task 51 Part A. The tail is re-parsed through
            // Conditions.Parse, so it reuses the existing condition grammar and a shape that
            // does not parse gets a friendly load-time error.
            if (first.Text == "expect")
            {
                var tail = line.Words.GetRange(1, line.Words.Count - 1);
                if (tail.Count == 0)
                    throw new ParseException("`expect` needs a condition — like `expect my Health is 0`", line.Line);
                var condition = Conditions.Parse(tail);
                if (condition.Kind == ConditionKind.Unknown)
                    throw new ParseException("`expect` needs a condition — like `expect my Health is 0`", line.Line);
                var conditionText = new System.Text.StringBuilder();
                foreach (var t in tail)
                {
                    if (conditionText.Length > 0)
                        conditionText.Append(' ');
                    conditionText.Append(t.Text);
                }
                line.Statement = new ExpectNode { Line = line.Line, Condition = condition, ConditionText = conditionText.ToString() };
                return;
            }
            // `pass` — Task 51 Part A. Closes the currently running self-test case as passed;
            // `pass` outside a case is a friendly runtime error, not a parse error.
            if (first.Text == "pass")
            {
                CheckEndOfLine(line.Words, 1, line.Line, "pass");
                line.Statement = new PassNode { Line = line.Line };
                return;
            }
            // Task 51 Part B: `test "<label>"` names the current self-test case; a no-op at
            // runtime, the label is captured at load by EventRuntime.ReadyTree.
            if (first.Text == "test")
            {
                if (line.Words.Count != 2 || line.Words[1].Type != TokenType.Str)
                    throw new ParseException("`test` needs a quoted label — like `test \"player can jump\"`", line.Line);
                CheckEndOfLine(line.Words, 2, line.Line, "test \"<label>\"");
                line.Statement = new TestNode { Line = line.Line, Label = line.Words[1].Text };
                return;
            }
            // Task 51 Part B: `self test timeout <n> seconds` sets the self-test deadline.
            if (first.Text == "self")
            {
                if (line.Words.Count < 2 || line.Words[1].Type != TokenType.Word || line.Words[1].Text != "test")
                    throw UnknownWord(first);
                if (line.Words.Count != 5 || line.Words[2].Text != "timeout" || line.Words[3].Type != TokenType.Number ||
                    line.Words[4].Text != "seconds")
                    throw new ParseException("`self test timeout` reads `self test timeout <n> seconds` — like `self test timeout 2 seconds`", line.Line);
                CheckEndOfLine(line.Words, 5, line.Line, "self test timeout <n> seconds");
                line.Statement = new TimeoutNode { Line = line.Line, Seconds = line.Words[3].Number };
                return;
            }
            if (Array.IndexOf(WordRegistry.ActionWords, first.Text) >= 0)
            {
            if (first.Text == "set")
            {
                if (line.Words.Count >= 2 && line.Words[1].Type == TokenType.Word && line.Words[1].Text == "state")
                {
                    line.Statement = ParseSetState(line);
                    return;
                }
                line.Statement = ParseSet(line);
                return;
            }
            if (first.Text == "add")
            {
                line.Statement = ParseAdd(line);
                return;
            }
            if (first.Text == "deduct")
            {
                line.Statement = ParseDeduct(line);
                return;
            }
            if (first.Text == "increase")
            {
                line.Statement = ParseIncrease(line);
                return;
            }
            if (first.Text == "remove")
            {
                line.Statement = ParseRemove(line);
                return;
            }
            if (first.Text == "equip")
            {
                line.Statement = ParseEquip(line);
                return;
            }
            if (first.Text == "drop")
            {
                line.Statement = ParseDrop(line);
                return;
            }
            if (first.Text == "play")
            {
                if (line.Words.Count >= 4 && line.Words[1].Text == "step" && line.Words[2].Text == "sound" && line.Words[3].Type == TokenType.Str)
                {
                    line.Statement = new PlayStepSoundNode { Line = line.Line, Material = line.Words[3].Text };
                    return;
                }
            }
                var action = new RawActionNode { Line = line.Line };
                action.Words.AddRange(line.Words);
                line.Statement = action;
                return;
            }
            bool hasConditionMark = false;
            foreach (var w in line.Words)
            {
                if (w.Type == TokenType.Word && Array.IndexOf(WordRegistry.ConditionMarks, w.Text) >= 0)
                {
                    hasConditionMark = true;
                    break;
                }
            }
            if (hasConditionMark)
            {
                line.IsCondition = true;
                return;
            }
            throw UnknownWord(first);
        }

        private void CheckEndOfLine(List<Token> words, int index, int flagLine, string shape)
        {
            if (index >= words.Count)
                return;
            var left = words[index];
            throw new ParseException($"I can't read the rest of this line — it should look like `{shape}`, but {DescribeWord(left)} comes after the value", left.Line);
        }

        private ValueNode ReadValue(List<Token> words, ref int index, int flagLine)
        {
            if (index >= words.Count)
                throw new ParseException("the value is missing — this line ends where a number, quoted text, or a variable was expected", flagLine);
            var t = words[index];
            if (t.Type == TokenType.Number)
            {
                index++;
                return new NumberValue { Line = t.Line, Value = t.Number };
            }
            if (t.Type == TokenType.Str)
            {
                index++;
                return new TextValue { Line = t.Line, Text = t.Text };
            }
            if (t.Type == TokenType.Word && (t.Text == "true" || t.Text == "false"))
            {
                index++;
                return new BooleanValue { Line = t.Line, Value = t.Text == "true" };
            }
            if (t.Type == TokenType.Word && t.Text == "random")
            {
                index++;
                var min = ReadRangeBound(words, ref index, flagLine);
                if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != "to")
                    throw new ParseException("`random` needs a range in the shape `random <low> to <high>` — like `random 1 to 4`", flagLine);
                index++;
                var max = ReadRangeBound(words, ref index, flagLine);
                return new RandomRangeValue { Line = t.Line, Min = min, Max = max };
            }
            if (IsHelperWord(t))
                return ReadHelper(words, ref index, flagLine);
            if (t.Type == TokenType.Word && t.Text == "field")
                return ReadFieldValue(words, ref index, flagLine);
            if (t.Type == TokenType.Word)
            {
                // `-my DamageTaken`, `-Score` — a negated value (Task 18 Part G): the lexer keeps
                // the sign attached to the first word, so a captured damage number can be spent in
                // one action, `add -my DamageTaken to Health`.
                if (t.Text.Length > 1 && t.Text[0] == '-' && !char.IsDigit(t.Text[1]))
                {
                    var negated = ReadNegatedValue(words, ref index, flagLine);
                    if (negated != null)
                        return negated;
                }
                // `other's damage`, `Player's gold` — read another actor's `my` variable
                if (t.Text.EndsWith("'s", StringComparison.Ordinal) && t.Text.Length > 2 &&
                    index + 1 < words.Count && words[index + 1].Type == TokenType.Word)
                {
                    var owner = t.Text.Substring(0, t.Text.Length - 2);
                    var nameToken = words[index + 1];
                    index += 2;
                    return new VariableValue { Line = t.Line, Owner = owner, Name = nameToken.Text };
                }
                return ReadVariable(words, ref index, flagLine, "`set`");
            }
            throw new ParseException("a value was expected here — a number, quoted text, or a variable", t.Line);
        }

        /// <summary>
        /// Reads <c>field "&lt;field&gt;" of "&lt;row&gt;" in "&lt;table&gt;"</c> (Task 29 Part C). All three
        /// parts are quoted on purpose: they are data names, not variables, so a row called
        /// <c>Supply Max</c> is written in full instead of being mistaken for a variable read.
        /// </summary>
        private ValueNode ReadFieldValue(List<Token> words, ref int index, int flagLine)
        {
            var token = words[index];
            const string shape =
                "`field` reads `field \"<field>\" of \"<row>\" in \"<table>\"`, like `field \"Damage\" of \"Sword\" in \"item\"`";
            var node = new FieldValue { Line = token.Line };
            index++;
            node.Field = ReadFieldName(words, ref index, "field", token.Line, shape);
            ExpectWord(words, ref index, "of", token.Line, shape);
            node.Row = ReadFieldName(words, ref index, "row", token.Line, shape);
            ExpectWord(words, ref index, "in", token.Line, shape);
            node.Table = ReadFieldName(words, ref index, "table", token.Line, shape);
            return node;
        }

        /// <summary>One quoted name inside a `field` lookup; an empty quote is a mistake, not a name.</summary>
        private string ReadFieldName(List<Token> words, ref int index, string what, int line, string shape)
        {
            if (index >= words.Count || words[index].Type != TokenType.Str)
                throw new ParseException($"after `{what}` comes a quoted name — {shape}", line);
            string text = words[index].Text.Trim();
            if (text.Length == 0)
                throw new ParseException($"the {what} name inside `field` is empty — {shape}", line);
            index++;
            return text;
        }

        /// <summary>Whether this token starts a numeric helper (Task 29 Part B): one of
        /// <c>clamp</c>, <c>min</c>, <c>max</c>, <c>round</c>, <c>floor</c>, <c>ceil</c>, <c>abs</c>,
        /// or a percentage like <c>10%</c>.</summary>
        private static bool IsHelperWord(Token token)
        {
            if (token.Type != TokenType.Word)
                return false;
            switch (token.Text)
            {
                case "clamp":
                case "min":
                case "max":
                case "round":
                case "floor":
                case "ceil":
                case "abs":
                    return true;
            }
            return token.Text.Length > 1 && token.Text.EndsWith("%", StringComparison.Ordinal);
        }

        /// <summary>
        /// Reads a numeric helper as one value (Task 29 Part B). Every operand goes through
        /// <see cref="ReadValue"/>, so helpers nest and take any value form: <c>clamp my Mana to 0</c>,
        /// <c>max my BaseSpeed and 10</c>, <c>round random 1 to 6</c>, <c>clamp random 1 to 6 to 1 and 4</c>.
        /// </summary>
        private ValueNode ReadHelper(List<Token> words, ref int index, int flagLine)
        {
            var token = words[index];
            string word = token.Text;
            index++;

            var helper = new HelperValue { Line = token.Line };
            if (word.EndsWith("%", StringComparison.Ordinal))
            {
                string number = word.Substring(0, word.Length - 1);
                if (!double.TryParse(number, System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture, out double percent))
                    throw new ParseException(
                        $"I can't read \u201C{word}\u201D as a percentage — write it like `10% of my Gold`",
                        token.Line);
                helper.Kind = ValueHelper.PercentOf;
                helper.First = new NumberValue { Line = token.Line, Value = percent };
                ExpectWord(words, ref index, "of", token.Line, $"`{word}` reads like `10% of my Gold`");
                helper.Second = ReadValue(words, ref index, flagLine);
                return helper;
            }

            if (word == "clamp")
            {
                helper.Kind = ValueHelper.Clamp;
                helper.First = ReadValue(words, ref index, flagLine);
                ExpectWord(words, ref index, "to", token.Line,
                           "`clamp` reads `clamp <value> to <min> and <max>`, like `clamp my Mana to 0 and my MaxMana`");
                helper.Second = ReadValue(words, ref index, flagLine);
                ExpectWord(words, ref index, "and", token.Line,
                           "`clamp` reads `clamp <value> to <min> and <max>`, like `clamp my Mana to 0 and my MaxMana`");
                helper.Third = ReadValue(words, ref index, flagLine);
                return helper;
            }

            if (word == "min" || word == "max")
            {
                helper.Kind = word == "min" ? ValueHelper.Min : ValueHelper.Max;
                helper.First = ReadValue(words, ref index, flagLine);
                ExpectWord(words, ref index, "and", token.Line,
                           $"`{word}` reads `{word} <a> and <b>`, like `{word} my BaseSpeed and 10`");
                helper.Second = ReadValue(words, ref index, flagLine);
                return helper;
            }

            switch (word)
            {
                case "round": helper.Kind = ValueHelper.Round; break;
                case "floor": helper.Kind = ValueHelper.Floor; break;
                case "ceil": helper.Kind = ValueHelper.Ceil; break;
                default: helper.Kind = ValueHelper.Abs; break;
            }
            if (index >= words.Count)
                throw new ParseException($"`{word}` needs a value after it — like `{word} my Gold`", token.Line);
            helper.First = ReadValue(words, ref index, flagLine);
            return helper;
        }

        /// <summary>Consumes one expected word, or explains the shape the line should have.</summary>
        private void ExpectWord(List<Token> words, ref int index, string word, int line, string shape)
        {
            if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != word)
                throw new ParseException($"{shape} — I expected \u201C{word}\u201D here", line);
            index++;
        }

        /// <summary>
        /// Reads a value written with a leading minus: <c>-my DamageTaken</c>, <c>-other's damage</c>
        /// or a plain global like <c>-Score</c>. Returns null when the word after the sign is not a
        /// variable the language knows, so the caller can report its usual error.
        /// </summary>
        private ValueNode ReadNegatedValue(List<Token> words, ref int index, int flagLine)
        {
            var token = words[index];
            string stripped = token.Text.Substring(1);
            var negated = new VariableValue { Line = token.Line, Negated = true };
            if (stripped == "my" || stripped == "me")
            {
                if (index + 1 >= words.Count || words[index + 1].Type != TokenType.Word)
                    throw new ParseException("`-my` needs a variable name right after it — like `-my DamageTaken`", token.Line);
                negated.Owner = "my";
                negated.Name = words[index + 1].Text;
                index += 2;
                return negated;
            }
            if (stripped == "other" || stripped == "other's")
            {
                if (stripped == "other's")
                {
                    if (index + 1 >= words.Count || words[index + 1].Type != TokenType.Word)
                        throw new ParseException("`-other's` needs a variable name right after it — like `-other's damage`", token.Line);
                    negated.Owner = "other";
                    negated.Name = words[index + 1].Text;
                    index += 2;
                    return negated;
                }
                negated.Owner = "other";
                negated.Name = "other";
                index++;
                return negated;
            }
            if (!char.IsLetter(stripped[0]) || !char.IsUpper(stripped[0]))
                throw new ParseException($"I can't use \u201C-{stripped}\u201D as a value — after a minus comes `my`, `other's`, or a variable that starts with a capital letter", token.Line);
            negated.Name = ResourceManager.CanonicalOrName(stripped);
            index++;
            return negated;
        }

        private ValueNode ReadRangeBound(List<Token> words, ref int index, int flagLine)
        {
            if (index >= words.Count)
                throw new ParseException("`random` needs two numbers — `random 1 to 4`", flagLine);
            var t = words[index];
            if (t.Type == TokenType.Number)
            {
                index++;
                return new NumberValue { Line = t.Line, Value = t.Number };
            }
            if (t.Type == TokenType.Str)
                throw new ParseException("`random` ranges take numbers, not quoted text — like `random 1 to 4`", t.Line);
            return ReadVariable(words, ref index, flagLine, "`random`");
        }

        /// <summary>
        /// Parses the five cursor words (Task 48 Part E) into one node: the name is the quoted or
        /// bare word after <c>cursor</c>, and <c>to default</c> is spelled the long way so a sheet
        /// reads as English. A shape this method does not recognise answers null, which lets the
        /// ordinary `set` parser warn about it instead of silently doing nothing.
        /// </summary>
        private static CursorNode ParseCursor(BodyLineNode line)
        {
            List<Token> words = line.Words;
            string first = words[0].Text;
            if (first == "set")
            {
                string name = null;
                if (words.Count == 3)
                    name = words[2].Text;
                else if (words.Count == 4 && words[2].Type == TokenType.Word && words[2].Text == "to")
                    name = words[3].Text;
                if (string.IsNullOrWhiteSpace(name))
                    return null;
                return new CursorNode { Line = line.Line, Op = CursorOp.Set, Name = name };
            }
            if (first == "push" && words.Count == 3)
                return new CursorNode { Line = line.Line, Op = CursorOp.Push, Name = words[2].Text };
            if (first == "pop" && words.Count == 2)
                return new CursorNode { Line = line.Line, Op = CursorOp.Pop };
            if ((first == "hide" || first == "show") && words.Count == 2)
            {
                return new CursorNode
                {
                    Line = line.Line,
                    Op = first == "hide" ? CursorOp.Hide : CursorOp.Show,
                };
            }
            return null;
        }

        private VariableValue ReadVariable(List<Token> words, ref int index, int flagLine, string afterWhat)
        {
            if (index >= words.Count)
                throw new ParseException($"after {afterWhat} there must come a variable name — like `Score` or `my health`", flagLine);
            var t = words[index];
            if (t.Type != TokenType.Word)
            {
                string kind = t.Type == TokenType.Number ? "a number" : t.Type == TokenType.Str ? "quoted text" : DescribeWord(t);
                throw new ParseException($"{kind} can't be a variable — variables are named things, like `Score` or `my health`", t.Line);
            }
            if (t.Text == "my")
            {
                // `my "stim" time left` — the seconds a buff still has (Task 27 Part C).
                if (index + 3 < words.Count && words[index + 1].Type == TokenType.Str &&
                    words[index + 2].Text == "time" && words[index + 3].Text == "left")
                {
                    string buffName = words[index + 1].Text;
                    index += 4;
                    return new VariableValue { Line = t.Line, Owner = "my", Name = Buffs.TimeLeftName(buffName) };
                }
                // `my "stim" cooldown` — the seconds left on a shared timer (Task 27 Part E).
                if (index + 2 < words.Count && words[index + 1].Type == TokenType.Str &&
                    words[index + 2].Text == "cooldown")
                {
                    string timerName = words[index + 1].Text;
                    index += 3;
                    return new VariableValue { Line = t.Line, Owner = "my", Name = Cooldowns.VariableName(timerName) };
                }
                // `my cursor` — the cursor in effect right now (Task 48 Part E). It reads the
                // global the bridge keeps in sync each frame, so a condition can compare it and
                // `when my cursor changes` can watch it with the ordinary changes trigger.
                if (index + 1 < words.Count && words[index].Type == TokenType.Word &&
                    words[index].Text == "my" && words[index + 1].Type == TokenType.Word &&
                    words[index + 1].Text == "cursor")
                {
                    index += 2;
                    return new VariableValue { Line = t.Line, Owner = "", Name = EventRuntime.CursorGlobal };
                }
                // `my Cursor fps` / `my Cursor hotspot` / `my Cursor fallback` / `my Cursor image`
                // — the settings a cursor actor carries for its own art (Task 48 Part C). They
                // are one variable each, so `set my Cursor fps to 12` lands on that actor.
                if (index + 2 < words.Count && words[index + 1].Type == TokenType.Word &&
                    words[index + 2].Type == TokenType.Word && words[index + 1].Text == "Cursor")
                {
                    string cursorVariable = null;
                    switch (words[index + 2].Text)
                    {
                        case "fps": cursorVariable = CursorVariables.FpsVariable; break;
                        case "hotspot": cursorVariable = CursorVariables.HotspotVariable; break;
                        case "fallback": cursorVariable = CursorVariables.FallbackVariable; break;
                        case "image": cursorVariable = CursorVariables.ImageVariable; break;
                    }
                    if (cursorVariable != null)
                    {
                        index += 3;
                        return new VariableValue { Line = t.Line, Owner = "my", Name = cursorVariable };
                    }
                }
                if (index + 2 < words.Count && words[index + 1].Type == TokenType.Word &&
                    words[index + 2].Type == TokenType.Word)
                {
                    string pair = words[index + 1].Text + " " + words[index + 2].Text;
                    // `my aim x` / `my aim z` / `my aim target` — the target-pick result
                    // (Task 27 Part A), read as one variable like the order state.
                    // `my selected unit` / `my selected building` / `my selected kind` /
                    // `my selected count` — the selection as a value (Task 29 Part D), read like
                    // the order state so an expression and a condition see the same thing.
                    // `my train time` / `my queue cap` — the production read-outs a command card
                    // shows (Task 29 Part E).
                    if (pair == "train time" || pair == "queue cap" ||
                        pair == "queue count" || pair == "train left")
                    {
                        index += 3;
                        return new VariableValue { Line = t.Line, Owner = "my", Name = pair };
                    }
                    if (pair == "current order" || pair == "order target" || pair == "order queue" ||
                        pair == "aim x" || pair == "aim z" || pair == "aim target" ||
                        pair == "selected unit" || pair == "selected building" ||
                        pair == "selected kind" || pair == "selected count" ||
                        pair == "selection count")
                    {
                        index += 3;
                        return new VariableValue { Line = t.Line, Owner = "my", Name = pair };
                    }
                }
                // `my camera zoom` / `my camera position` — the RTS camera state (Task 26
                // Part D), read as one variable like `my attack range`.
                if (index + 2 < words.Count && words[index + 1].Type == TokenType.Word &&
                    words[index + 2].Type == TokenType.Word &&
                    words[index + 1].Text == "camera" &&
                    (words[index + 2].Text == "zoom" || words[index + 2].Text == "position"))
                {
                    string cameraName = "camera " + words[index + 2].Text;
                    index += 3;
                    return new VariableValue { Line = t.Line, Owner = "my", Name = cameraName };
                }
                // `my attack type` — RTS melee/ranged (Task 23 Part A).
                if (index + 2 < words.Count && words[index + 1].Type == TokenType.Word &&
                    words[index + 2].Type == TokenType.Word &&
                    words[index + 1].Text == "attack" && words[index + 2].Text == "type")
                {
                    index += 3;
                    return new VariableValue { Line = t.Line, Owner = "my", Name = AttackTypes.Variable };
                }
                // `my unit type` — RTS land/air/sea (Task 22 Part A).
                if (index + 2 < words.Count && words[index + 1].Type == TokenType.Word &&
                    words[index + 2].Type == TokenType.Word &&
                    words[index + 1].Text == "unit" && words[index + 2].Text == "type")
                {
                    index += 3;
                    return new VariableValue { Line = t.Line, Owner = "my", Name = UnitTypes.Variable };
                }
                // `my picked choice` — the dialogue branch index (Task 35 Part C). One variable
                // with a space, so a sheet may write it by hand as well as read it.
                if (index + 2 < words.Count && words[index + 1].Type == TokenType.Word &&
                    words[index + 2].Type == TokenType.Word &&
                    words[index + 1].Text == "picked" && words[index + 2].Text == "choice")
                {
                    index += 3;
                    return new VariableValue { Line = t.Line, Owner = "my", Name = ChoiceManager.PickedVariable };
                }
                // `my attack range` — the distance an attack stops at (Task 21 Part A).
                if (index + 2 < words.Count && words[index + 1].Type == TokenType.Word &&
                    words[index + 2].Type == TokenType.Word &&
                    words[index + 1].Text == "attack" && words[index + 2].Text == "range")
                {
                    index += 3;
                    return new VariableValue { Line = t.Line, Owner = "my", Name = "attack range" };
                }
                // `my Max Health` / `my Max Shield` — a multiword health stat is one variable
                // (Task 18 Part G), so it is joined before the single-word rule takes `Max`.
                if (index + 2 < words.Count && words[index + 1].Type == TokenType.Word &&
                    words[index + 2].Type == TokenType.Word &&
                    HealthManager.TryStat(words[index + 1].Text + " " + words[index + 2].Text, out _))
                {
                    string pairName = words[index + 1].Text + " " + words[index + 2].Text;
                    index += 3;
                    return new VariableValue { Line = t.Line, Owner = "my", Name = pairName };
                }
                if (index + 1 < words.Count && words[index + 1].Type == TokenType.Word)
                {
                    var name = words[index + 1];
                    index += 2;
                    return new VariableValue { Line = t.Line, Owner = "my", Name = name.Text };
                }
                throw new ParseException("`my` needs a variable name right after it — like `my health`", t.Line);
            }
            // `Player1 Score` / `Player2 Kills` — a per-player counter is written as two words
            // (Task 16 Part F). Only a player name plus a known counter (Score/Lives/Kills)
            // joins, so an ordinary `set Foo Bar` is untouched.
            if (index + 1 < words.Count && words[index + 1].Type == TokenType.Word &&
                Players.TryReadVariableName(t.Text, words[index + 1].Text, out string playerVariable))
            {
                index += 2;
                return new VariableValue { Line = t.Line, Name = playerVariable };
            }
            // `Player2 ShootKey` — a saved input binding, so a label can show the key a player
            // presses (Task 16 input follow-ups).
            if (index + 1 < words.Count && words[index + 1].Type == TokenType.Word &&
                PlayerInput.TryReadSettingName(t.Text, words[index + 1].Text, out int bindPlayer, out string bindAction))
            {
                string setting = PlayerInput.SettingName(bindPlayer, bindAction);
                index += 2;
                return new VariableValue { Line = t.Line, Name = setting };
            }
            // `Player2 UpgradeStim` / `Player2 PistolDamage` — per-player tech and weapon values
            // (Task 27 Part B). A player name plus a capitalised name is one global, so research
            // can raise one side's levels and an upgrade can buff one side's guns.
            if (index + 1 < words.Count && words[index + 1].Type == TokenType.Word &&
                Tech.TryReadVariableName(t.Text, words[index + 1].Text, out string techVariable))
            {
                index += 2;
                return new VariableValue { Line = t.Line, Name = techVariable };
            }
            // Level statistics (Task 16 Part G): `kills total`, `kills Zombie`, `accuracy`,
            // `time elapsed`, `level number` and `level name`. These are lowercase words, so
            // they must be recognised before the capital-letter variable rule below.
            if (Levels.TryReadStatName(words, index, out string statName, out int statUsed))
            {
                index += statUsed;
                return new VariableValue { Line = t.Line, Name = statName };
            }
            // `Max Health` / `Max Shield` — a multiword health stat used without `my` on a
            // unit's own sheet (Task 18 Part G).
            if (index + 1 < words.Count && words[index + 1].Type == TokenType.Word &&
                HealthManager.TryStat(t.Text + " " + words[index + 1].Text, out _))
            {
                string statPair = t.Text + " " + words[index + 1].Text;
                index += 2;
                return new VariableValue { Line = t.Line, Name = statPair };
            }
            // The run-together spelling `Player1Score` reads as the same variable.
            if (Players.TryReadVariableName(t.Text, out string runTogetherVariable))
            {
                index++;
                return new VariableValue { Line = t.Line, Name = runTogetherVariable };
            }
            // ... and so does `Player2UpgradeStim` / `Player2PistolDamage` (Task 27 Part B).
            if (Tech.TryReadVariableName(t.Text, out string runTogetherTech))
            {
                index++;
                return new VariableValue { Line = t.Line, Name = runTogetherTech };
            }
            if (!char.IsLetter(t.Text[0]) || !char.IsUpper(t.Text[0]))
                throw new ParseException($@"I can't use {DescribeWord(t)} as a variable — variables start with a capital letter, or use `my` for a per-object one (like `Score` or `my health`)", t.Line);
            index++;
            return new VariableValue { Line = t.Line, Name = t.Text };
        }

        private StatementNode ParseSet(BodyLineNode line)
        {
            var words = line.Words;
            // `set root motion on` / `off` — the actor's root-motion switch (Task 33 Part D). Another
            // action word, not a global called "root motion", so it goes to the action parser (which
            // also owns the friendly warning for a bare `set root motion`).
            if (words.Count >= 3 && words[1].Type == TokenType.Word && words[1].Text == "root" &&
                words[2].Type == TokenType.Word && words[2].Text == "motion")
            {
                var rootMotionRaw = new RawActionNode { Line = line.Line };
                rootMotionRaw.Words.AddRange(words);
                return rootMotionRaw;
            }
            // `set anim speed to <n>` — animation playback speed (Task 33 Part A). An action word,
            // not a global variable called "anim speed", so it goes to the action parser.
            if (words.Count >= 3 && words[1].Type == TokenType.Word && words[1].Text == "anim" &&
                words[2].Type == TokenType.Word && words[2].Text == "speed")
            {
                var animSpeedRaw = new RawActionNode { Line = line.Line };
                animSpeedRaw.Words.AddRange(words);
                return animSpeedRaw;
            }
            // `set music volume to 0.4`, `set sound volume to 0.7`, `set master volume to 0.8`
            // — the audio volume groups (Task 31). They are action words, not a global called
            // "music volume", so they go to the raw action parser.
            if (words.Count >= 4 && words[1].Type == TokenType.Word && words[2].Type == TokenType.Word &&
                words[2].Text == "volume" &&
                (words[1].Text == "music" || words[1].Text == "sound" || words[1].Text == "master"))
            {
                var volumeRaw = new RawActionNode { Line = line.Line };
                volumeRaw.Words.AddRange(words);
                return volumeRaw;
            }
            if (words.Count > 2 && words[1].Type == TokenType.Word && words[1].Text == "text" &&
                words[2].Type == TokenType.Word && words[2].Text == "of")
                return ParseSetText(line);
            // `set build progress to <n>` / `set build progress of <building> to <n>`
            // — RTS construction progress (Task 18 Part C).
            if (words.Count >= 3 && words[1].Type == TokenType.Word && words[1].Text == "build" &&
                words[2].Type == TokenType.Word && words[2].Text == "time")
                return ParseSetBuildTime(line);
            if (words.Count >= 3 && words[1].Type == TokenType.Word && words[1].Text == "build" &&
                words[2].Type == TokenType.Word && words[2].Text == "progress")
            {
                if (words.Count >= 5 && words[3].Type == TokenType.Word && words[3].Text == "to")
                {
                    var buildNode = new SetBuildProgressNode { Line = line.Line };
                    int idx = 4;
                    buildNode.Value = ReadValue(words, ref idx, line.Line);
                    CheckEndOfLine(words, idx, line.Line, "set build progress to <n>");
                    return buildNode;
                }
                if (words.Count >= 7 && words[3].Type == TokenType.Word && words[3].Text == "of")
                {
                    int progressToIndex = -1;
                    for (int i = 4; i < words.Count; i++)
                    {
                        if (words[i].Type == TokenType.Word && words[i].Text == "to")
                        {
                            progressToIndex = i;
                            break;
                        }
                    }
                    if (progressToIndex > 4)
                    {
                        var buildNode = new SetBuildProgressNode
                        {
                            Line = line.Line,
                            Thing = JoinWords(words, 4, progressToIndex),
                        };
                        int idx = progressToIndex + 1;
                        buildNode.Value = ReadValue(words, ref idx, line.Line);
                        CheckEndOfLine(words, idx, line.Line, "set build progress of <building> to <n>");
                        return buildNode;
                    }
                }
                throw new ParseException(
                    "`set build progress` looks like this: `set build progress to 0` or `set build progress of Barracks to 0`",
                    line.Line);
            }
            // `set my control group to <n>` — RTS control group assignment (Task 18 Part A).
            if (words.Count >= 3 && words[1].Type == TokenType.Word && words[1].Text == "my" &&
                words[2].Type == TokenType.Word && words[2].Text == "control")
            {
                if (words.Count >= 6 && words[3].Text == "group" && words[4].Text == "to" &&
                    words[5].Type == TokenType.Number)
                {
                    var groupNode = new SetControlGroupNode { Line = line.Line, Group = (int)words[5].Number };
                    if (groupNode.Group < 0 || groupNode.Group > 9)
                        throw new ParseException("control groups are 1 to 0 (ten groups) — `set my control group to 1`", line.Line);
                    CheckEndOfLine(words, 6, line.Line, "set my control group to 1");
                    return groupNode;
                }
                throw new ParseException("`set my control group` looks like this: `set my control group to 1`", line.Line);
            }
            // `set weapon to "Pistol"` — Task 16 Part C. This is the current weapon, not a
            // global variable called "weapon", so it gets its own statement.
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "weapon")
            {
                if (words.Count < 4 || words[2].Type != TokenType.Word || words[2].Text != "to")
                    throw new ParseException("`set weapon` looks like this: `set weapon to \"Pistol\"`", line.Line);
                var weaponNode = new SetWeaponNode { Line = line.Line };
                int weaponIndex = 3;
                weaponNode.Value = ReadValue(words, ref weaponIndex, line.Line);
                CheckEndOfLine(words, weaponIndex, line.Line, "set weapon to \"Pistol\"");
                return weaponNode;
            }
            // `set ammo Pistol to 12` — that weapon's ammo count on the acting actor.
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "ammo")
            {
                var ammoNode = new SetAmmoNode { Line = line.Line };
                int ammoIndex = 2;
                if (!TryReadWeaponName(words, ref ammoIndex, out string ammoWeapon, out string ammoVariable) ||
                    ammoIndex >= words.Count || words[ammoIndex].Type != TokenType.Word ||
                    words[ammoIndex].Text != "to")
                    throw new ParseException("`set ammo` looks like this: `set ammo Pistol to 12`", line.Line);
                ammoNode.Weapon = ammoWeapon;
                ammoNode.WeaponVariable = ammoVariable;
                ammoIndex++;
                ammoNode.Value = ReadValue(words, ref ammoIndex, line.Line);
                CheckEndOfLine(words, ammoIndex, line.Line, "set ammo Pistol to 12");
                return ammoNode;
            }
            // `set level to <n>` — level progression (Task 16 Part G). The current level is
            // runtime state, not a global variable called "level".
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "level")
            {
                var levelRaw = new RawActionNode { Line = line.Line };
                levelRaw.Words.AddRange(words);
                return levelRaw;
            }
            // `set light intensity/color/range to <value>` — handled as a raw action word.
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "light")
            {
                var raw = new RawActionNode { Line = line.Line };
                raw.Words.AddRange(words);
                return raw;
            }
            // `set button <thing> enabled/disabled`.
            if (words.Count >= 4 && words[1].Type == TokenType.Word && words[1].Text == "button" &&
                words[2].Type == TokenType.Word)
            {
                string buttonThing = words[2].Text;
                if (words[3].Text == "enabled")
                {
                    CheckEndOfLine(words, 4, line.Line, "set button <thing> enabled");
                    return new SetButtonEnabledNode { Line = line.Line, Thing = buttonThing, Enabled = true };
                }
                if (words[3].Text == "disabled")
                {
                    CheckEndOfLine(words, 4, line.Line, "set button <thing> disabled");
                    return new SetButtonEnabledNode { Line = line.Line, Thing = buttonThing, Enabled = false };
                }
            }
            // `set button <thing> text to <value>` — a Button is a Label, so this reuses SetTextNode.
            if (words.Count >= 6 && words[1].Type == TokenType.Word && words[1].Text == "button" &&
                words[2].Type == TokenType.Word &&
                words[3].Type == TokenType.Word && words[3].Text == "text" &&
                words[4].Type == TokenType.Word && words[4].Text == "to")
            {
                var textNode = new SetTextNode { Line = line.Line, Target = words[2].Text };
                int idx = 5;
                textNode.Value = ReadValue(words, ref idx, line.Line);
                CheckEndOfLine(words, idx, line.Line, $"set button {textNode.Target} text to <value>");
                return textNode;
            }
            // `set image of <thing> to <sprite>`.
            if (words.Count >= 6 && words[1].Type == TokenType.Word && words[1].Text == "image" &&
                words[2].Type == TokenType.Word && words[2].Text == "of" &&
                words[3].Type == TokenType.Word &&
                words[4].Type == TokenType.Word && words[4].Text == "to")
            {
                var imgNode = new SetImageBrushNode { Line = line.Line, Thing = words[3].Text };
                int idx = 5;
                var value = ReadValue(words, ref idx, line.Line);
                imgNode.Sprite = value is TextValue tv ? tv.Text : Values.Describe(value);
                CheckEndOfLine(words, idx, line.Line, $"set image of {imgNode.Thing} to <sprite>");
                return imgNode;
            }
            // `set portrait of <thing> to "<sprite>"` — dialogue portrait (Task 35 Part B). It goes
            // to its own node so the sprite keeps the `set image` resolver and the speaker label
            // stays a separate word.
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "portrait")
                return ParseSetPortrait(line);
            // `set speaker to "<name>"` — the name goes into its own label (Task 35 Part B).
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "speaker")
            {
                var speakerNode = new SetSpeakerNode { Line = line.Line };
                if (words.Count < 4 || words[2].Type != TokenType.Word || words[2].Text != "to")
                    throw new ParseException("a `set speaker` line joins the name with `to` — `set speaker to \"Innkeeper\"`", line.Line);
                int speakerIndex = 3;
                speakerNode.Value = ReadValue(words, ref speakerIndex, line.Line);
                CheckEndOfLine(words, speakerIndex, line.Line, "set speaker to \"<name>\"");
                return speakerNode;
            }
            // `set color of <thing> to <color>`.
            if (words.Count >= 6 && words[1].Type == TokenType.Word && words[1].Text == "color" &&
                words[2].Type == TokenType.Word && words[2].Text == "of" &&
                words[3].Type == TokenType.Word &&
                words[4].Type == TokenType.Word && words[4].Text == "to")
            {
                var colorNode = new SetControlColorNode { Line = line.Line, Thing = words[3].Text };
                int idx = 5;
                var value = ReadValue(words, ref idx, line.Line);
                colorNode.Color = value is TextValue cv ? cv.Text : Values.Describe(value);
                CheckEndOfLine(words, idx, line.Line, $"set color of {colorNode.Thing} to <color>");
                return colorNode;
            }
            // `set progress of <thing> to <value>` (0-1 or 0-100).
            if (words.Count >= 6 && words[1].Type == TokenType.Word && words[1].Text == "progress" &&
                words[2].Type == TokenType.Word && words[2].Text == "of" &&
                words[3].Type == TokenType.Word &&
                words[4].Type == TokenType.Word && words[4].Text == "to")
            {
                var progressNode = new SetProgressNode { Line = line.Line, Thing = words[3].Text };
                int idx = 5;
                progressNode.Value = ReadValue(words, ref idx, line.Line);
                CheckEndOfLine(words, idx, line.Line, $"set progress of {progressNode.Thing} to <value>");
                return progressNode;
            }
            // `set slider <thing> to <value>` (0-1 or 0-100).
            if (words.Count >= 5 && words[1].Type == TokenType.Word && words[1].Text == "slider" &&
                words[2].Type == TokenType.Word &&
                words[3].Type == TokenType.Word && words[3].Text == "to")
            {
                var sliderNode = new SetSliderNode { Line = line.Line, Thing = words[2].Text };
                int idx = 4;
                sliderNode.Value = ReadValue(words, ref idx, line.Line);
                CheckEndOfLine(words, idx, line.Line, $"set slider {sliderNode.Thing} to <value>");
                return sliderNode;
            }
            // `set time scale to <value>` — handled as a raw action word.
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "time" &&
                words[2].Type == TokenType.Word && words[2].Text == "scale")
            {
                var raw = new RawActionNode { Line = line.Line };
                raw.Words.AddRange(words);
                return raw;
            }
            // `set rally point to <target>` — an RTS order action (Task 18 Part E), parsed by
            // ActionWords rather than as a variable called "rally".
            if (words.Count >= 5 && words[1].Type == TokenType.Word && words[1].Text == "rally" &&
                words[2].Type == TokenType.Word && words[2].Text == "point" &&
                words[3].Type == TokenType.Word && words[3].Text == "to")
            {
                var raw = new RawActionNode { Line = line.Line };
                raw.Words.AddRange(words);
                return raw;
            }
            // `set visibility to <radius>` — RTS fog of war (Task 18 Part F), also parsed by
            // ActionWords rather than as a variable called "visibility".
            if (words.Count >= 4 && words[1].Type == TokenType.Word && words[1].Text == "visibility" &&
                words[2].Type == TokenType.Word && words[2].Text == "to")
            {
                var raw = new RawActionNode { Line = line.Line };
                raw.Words.AddRange(words);
                return raw;
            }
            var node = new SetNode { Line = line.Line };
            int index = 1;
            // `set Supply Used to 5` / `set Minerals to 500`: resources may be multiword.
            if (TryReadResourcePrefix(words, index, "to", out VariableValue resourceTarget, out int toIndex))
            {
                node.Target = resourceTarget;
                index = toIndex;
            }
            else
            {
                node.Target = ReadVariable(words, ref index, line.Line, "`set`");
            }
            if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != "to")
                throw new ParseException("a `set` line joins a variable and a value with `to` — `set Score to 0`", line.Line);
            index++;
            node.Value = ReadValue(words, ref index, line.Line);
            // Task 29 Part A: up to three operators on one `set` line. One operator keeps the
            // older SetNode.Op/Right shape, so every sheet written before this already reads
            // exactly as it did; two or three collect into an ExpressionValue, which the
            // runtime folds with `* /` before `+ -` (see Expressions.Fold).
            var extraOps = new List<char>();
            var extraValues = new List<ValueNode>();
            while (index < words.Count && words[index].Type == TokenType.Word &&
                   words[index].Text.Length == 1 && Array.IndexOf(MathOps, words[index].Text) >= 0)
            {
                if (extraOps.Count >= MaxSetOperators)
                {
                    string firstPart = Values.Describe(node.Value) + " " + extraOps[0] + " " +
                                       Values.Describe(extraValues[0]);
                    throw new ParseException(
                        $"this line does {extraOps.Count + 1} calculations and I can read " +
                        $"{MaxSetOperators} — split it in two: work out the first part on the " +
                        $"line above, like `set running to {firstPart}`, then use `running` here",
                        words[index].Line);
                }
                extraOps.Add(words[index].Text[0]);
                index++;
                extraValues.Add(ReadValue(words, ref index, line.Line));
            }
            if (extraOps.Count == 1)
            {
                node.Op = extraOps[0];
                node.Right = extraValues[0];
            }
            else if (extraOps.Count > 1)
            {
                var expression = new ExpressionValue { Line = line.Line };
                expression.Parts.Add(node.Value);
                expression.Parts.AddRange(extraValues);
                expression.Ops.AddRange(extraOps);
                node.Value = expression;
            }
            CheckEndOfLine(words, index, line.Line, node.Target.Owner.Length == 0
                ? $"set {node.Target.Name} to <value>"
                : $"set my {node.Target.Name} to <value>");
            return node;
        }

        /// <summary><c>set build time to &lt;n&gt; [seconds]</c> and the optional
        /// <c>set build time of &lt;building&gt; to &lt;n&gt; [seconds]</c> — RTS
        /// production timing (Task 18 Part D).</summary>
        private StatementNode ParseSetBuildTime(BodyLineNode line)
        {
            var words = line.Words;
            int toIndex = -1;
            for (int i = 3; i < words.Count; i++)
            {
                if (words[i].Type == TokenType.Word && words[i].Text == "to")
                {
                    toIndex = i;
                    break;
                }
            }
            if (toIndex < 3 || toIndex == 4 || toIndex + 1 >= words.Count)
                throw new ParseException(
                    "`set build time` looks like this: `set build time to 10 seconds` or `set build time of Barracks to 10 seconds`",
                    line.Line);

            var node = new SetBuildTimeNode { Line = line.Line };
            if (toIndex > 4)
            {
                if (words[3].Type != TokenType.Word || words[3].Text != "of")
                    throw new ParseException(
                        "`set build time` looks like this: `set build time to 10 seconds` or `set build time of Barracks to 10 seconds`",
                        line.Line);
                node.Thing = JoinWords(words, 4, toIndex);
            }

            int index = toIndex + 1;
            node.Value = ReadValue(words, ref index, line.Line);
            if (index < words.Count && words[index].Type == TokenType.Word && words[index].Text == "seconds")
                index++;
            CheckEndOfLine(words, index, line.Line, node.Thing.Length == 0
                ? "set build time to 10 seconds"
                : $"set build time of {node.Thing} to 10 seconds");
            return node;
        }

        private SetTextNode ParseSetText(BodyLineNode line)
        {
            var words = line.Words;
            var node = new SetTextNode { Line = line.Line };
            int index = 3;
            if (index >= words.Count || words[index].Type != TokenType.Word)
                throw new ParseException("after `set text of` comes the name of the thing — `set text of ScoreLabel to Score`", line.Line);
            node.Target = words[index].Text;
            index++;
            if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != "to")
                throw new ParseException("a `set text of` line joins the thing and the value with `to` — `set text of ScoreLabel to Score`", line.Line);
            index++;
            node.Value = ReadValue(words, ref index, line.Line);
            CheckEndOfLine(words, index, line.Line, $"set text of {node.Target} to <value>");
            return node;
        }

        /// <summary>
        /// <c>add to backlog "&lt;text&gt;"</c> — Task 35 Part D. The line is kept as written; the
        /// bridge fills the scene's newest row labels with the history.
        /// </summary>
        private StatementNode ParseAddBacklog(BodyLineNode line)
        {
            var words = line.Words;
            const string shape = "add to backlog \"Innkeeper: Hello.\"";
            if (words.Count < 4 || words[3].Type != TokenType.Str)
                throw new ParseException($"`add to backlog` needs the line in quotes — `{shape}`", line.Line);
            var node = new AddBacklogNode { Line = line.Line };
            int index = 3;
            node.Value = ReadValue(words, ref index, line.Line);
            CheckEndOfLine(words, index, line.Line, "add to backlog \"<text>\"");
            return node;
        }

        /// <summary>
        /// <c>show choice "&lt;text&gt;"</c> — Task 35 Part C. The button is scene content, so the
        /// line only carries the words; the runtime finds the next slot and numbers it.
        /// </summary>
        private StatementNode ParseShowChoice(BodyLineNode line)
        {
            var words = line.Words;
            const string shape = "show choice \"I need a room.\"";
            if (words.Count < 3 || words[2].Type != TokenType.Str)
                throw new ParseException($"`show choice` needs the words on the button in quotes — `{shape}`", line.Line);
            var node = new ShowChoiceNode { Line = line.Line };
            int index = 2;
            node.Value = ReadValue(words, ref index, line.Line);
            CheckEndOfLine(words, index, line.Line, "show choice \"<text>\"");
            return node;
        }

        /// <summary>
        /// <c>set portrait of &lt;thing&gt; to "&lt;sprite&gt;"</c> — Task 35 Part B. The sprite is the
        /// same loose name <c>set image of</c> and <c>play sound</c> take (no folder, no
        /// extension, case ignored), resolved by the bridge's shared texture index.
        /// </summary>
        private StatementNode ParseSetPortrait(BodyLineNode line)
        {
            var words = line.Words;
            const string shape = "set portrait of PortraitBox to \"innkeeper\"";
            if (words.Count < 5 || words[2].Type != TokenType.Word || words[2].Text != "of" ||
                words[3].Type != TokenType.Word)
                throw new ParseException($"`set portrait` names the actor and then the sprite — `{shape}`", line.Line);
            var node = new SetPortraitNode { Line = line.Line, Thing = words[3].Text };
            int index = 4;
            if (words[index].Type != TokenType.Word || words[index].Text != "to")
                throw new ParseException($"a `set portrait` line joins the actor and the sprite with `to` — `{shape}`", line.Line);
            index++;
            if (index >= words.Count)
                throw new ParseException($"`set portrait of {node.Thing}` needs a sprite name — `{shape}`", line.Line);
            var value = ReadValue(words, ref index, line.Line);
            node.Sprite = value is TextValue text ? text.Text : Values.Describe(value);
            CheckEndOfLine(words, index, line.Line, $"set portrait of {node.Thing} to \"<sprite>\"");
            return node;
        }

        /// <summary>
        /// <c>type text "&lt;text&gt;" into &lt;thing&gt; speed &lt;n&gt;</c> — Task 35 Part A. The text and
        /// the speed both take a value, so a character sheet can type a variable line at
        /// <c>my TypeSpeed</c> as well as a quoted one.
        /// </summary>
        private StatementNode ParseTypeText(BodyLineNode line)
        {
            var words = line.Words;
            const string shape = "type text \"Hello, traveler.\" into DialogLabel speed 30";
            var node = new TypeTextNode { Line = line.Line };
            int index = 2;
            if (index >= words.Count || (words[index].Type != TokenType.Str && words[index].Type != TokenType.Word))
                throw new ParseException($"`type text` needs the words to type — `{shape}`", line.Line);
            node.Value = ReadValue(words, ref index, line.Line);
            if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != "into")
                throw new ParseException($"a `type text` line joins the words and the label with `into` — `{shape}`", line.Line);
            index++;
            if (index >= words.Count || words[index].Type != TokenType.Word)
                throw new ParseException($"after `into` comes the name of the thing to type into — `{shape}`", line.Line);
            node.Target = words[index].Text;
            index++;
            if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != "speed")
                throw new ParseException($"`type text` needs a speed after the label, in characters per second — `{shape}`", line.Line);
            index++;
            if (index >= words.Count)
                throw new ParseException($"after `speed` comes a number, like `{shape}`", line.Line);
            node.Speed = ReadValue(words, ref index, line.Line);
            CheckEndOfLine(words, index, line.Line, $"type text \"<text>\" into {node.Target} speed <n>");
            return node;
        }

        private StatementNode ParseSetState(BodyLineNode line)
        {
            var words = line.Words;
            // `set state chase` — changes the sheet's current state
            if (words.Count < 3 || words[2].Type != TokenType.Word)
                throw new ParseException("`set state` needs a state name, like `set state patrol`", line.Line);
            var node = new SetStateNode { Line = line.Line, State = words[2].Text };
            CheckEndOfLine(words, 3, line.Line, "set state <name>");
            return node;
        }

        private StatementNode ParseAdd(BodyLineNode line)
        {
            var words = line.Words;
            // `add to backlog "…"` — dialogue history (Task 35 Part D), not the
            // `add <value> to <variable>` statement (`backlog` is not a variable).
            if (words.Count >= 3 && words[1].Type == TokenType.Word && words[1].Text == "to" &&
                words[2].Type == TokenType.Word && words[2].Text == "backlog")
                return ParseAddBacklog(line);
            // `add buff "stim" to me for 8 seconds` is a buff word (Task 27 Part C), not the
            // `add <value> to <variable>` statement.
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "buff")
            {
                var buffRaw = new RawActionNode { Line = line.Line };
                buffRaw.Words.AddRange(words);
                return buffRaw;
            }
            // `add order to queue` is an RTS order action (Task 18 Part E), not the
            // `add <value> to <variable>` statement.
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "order")
            {
                var orderRaw = new RawActionNode { Line = line.Line };
                orderRaw.Words.AddRange(words);
                return orderRaw;
            }
            // `add scene "HUD"` is an action word (additive scene load), not the
            // `add <value> to <variable>` statement — hand it to the action parser.
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "scene")
            {
                var raw = new RawActionNode { Line = line.Line };
                raw.Words.AddRange(words);
                return raw;
            }
            // `add -1 to ammo Pistol` — the shot cost (Task 16 Part C). Detected before the
            // generic `add` shape so `ammo <weapon>` is not read as a variable called "ammo".
            {
                int ammoTo = -1;
                for (int i = 2; i < words.Count; i++)
                {
                    if (words[i].Type == TokenType.Word && words[i].Text == "to")
                    {
                        ammoTo = i;
                        break;
                    }
                }
                if (ammoTo > 1 && ammoTo + 1 < words.Count && words[ammoTo + 1].Type == TokenType.Word &&
                    words[ammoTo + 1].Text == "ammo")
                {
                    var ammoNode = new AddAmmoNode { Line = line.Line };
                    int ammoIndex = 1;
                    ammoNode.Amount = ReadValue(words, ref ammoIndex, line.Line);
                    if (ammoNode.Amount is MathValue)
                        throw new ParseException("`add` takes one plain value — do the math in a `set` line instead", line.Line);
                    if (ammoIndex != ammoTo)
                        throw new ParseException("`add` to ammo looks like this: `add -1 to ammo Pistol`", line.Line);
                    ammoIndex = ammoTo + 2;
                    if (!TryReadWeaponName(words, ref ammoIndex, out string ammoWeapon, out string ammoVariable))
                        throw new ParseException("`add` to ammo names a weapon, like `add -1 to ammo Pistol`", line.Line);
                    ammoNode.Weapon = ammoWeapon;
                    ammoNode.WeaponVariable = ammoVariable;
                    CheckEndOfLine(words, ammoIndex, line.Line, "add -1 to ammo Pistol");
                    return ammoNode;
                }
            }
            // `add 1 to kills <enemyType>` / `add 1 to kills my Type` — level statistics
            // (Task 16 Part G). Detected before the generic `add` shape so `kills` is never
            // read as a variable.
            {
                int killsTo = -1;
                for (int i = 2; i < words.Count; i++)
                {
                    if (words[i].Type == TokenType.Word && words[i].Text == "to")
                    {
                        killsTo = i;
                        break;
                    }
                }
                if (killsTo > 1 && killsTo + 1 < words.Count && words[killsTo + 1].Type == TokenType.Word &&
                    words[killsTo + 1].Text == "kills")
                {
                    var killNode = new AddKillNode { Line = line.Line };
                    int killIndex = 1;
                    killNode.Amount = ReadValue(words, ref killIndex, line.Line);
                    if (killNode.Amount is MathValue)
                        throw new ParseException("`add` takes one plain value — do the math in a `set` line instead", line.Line);
                    if (killIndex != killsTo)
                        throw new ParseException("`add` to kills looks like this: `add 1 to kills Zombie`", line.Line);

                    int typeIndex = killsTo + 2;
                    if (typeIndex >= words.Count)
                        throw new ParseException("`add` to kills names an enemy type — `add 1 to kills Zombie` or `add 1 to kills my Type`", line.Line);
                    if (words[typeIndex].Type == TokenType.Word && words[typeIndex].Text == "my")
                    {
                        if (typeIndex + 1 >= words.Count || words[typeIndex + 1].Type != TokenType.Word)
                            throw new ParseException("`add` to kills my Type` needs the variable after `my` — `add 1 to kills my Type`", line.Line);
                        killNode.TypeVariable = words[typeIndex + 1].Text;
                        typeIndex += 2;
                    }
                    else
                    {
                        if (words[typeIndex].Type != TokenType.Word)
                            throw new ParseException("`add` to kills names an enemy type — `add 1 to kills Zombie`", line.Line);
                        killNode.EnemyType = words[typeIndex].Text;
                        typeIndex += 1;
                    }
                    CheckEndOfLine(words, typeIndex, line.Line, "add 1 to kills Zombie");
                    return killNode;
                }
            }
            // `add <n> to build progress of <building>` — named RTS construction
            // progress (Task 18 Part C follow-up). Detected before the trailing-word
            // form so a building name after `of` is not read as a variable.
            if (words.Count >= 2)
            {
                int namedTo = -1;
                for (int i = 2; i < words.Count; i++)
                {
                    if (words[i].Type == TokenType.Word && words[i].Text == "to")
                    {
                        namedTo = i;
                        break;
                    }
                }
                if (namedTo > 1 && namedTo + 4 < words.Count &&
                    words[namedTo + 1].Type == TokenType.Word && words[namedTo + 1].Text == "build" &&
                    words[namedTo + 2].Type == TokenType.Word && words[namedTo + 2].Text == "progress" &&
                    words[namedTo + 3].Type == TokenType.Word && words[namedTo + 3].Text == "of")
                {
                    var namedNode = new AddBuildProgressNode
                    {
                        Line = line.Line,
                        Thing = JoinWords(words, namedTo + 4, words.Count),
                    };
                    int namedIndex = 1;
                    namedNode.Amount = ReadValue(words, ref namedIndex, line.Line);
                    if (namedNode.Amount is MathValue)
                        throw new ParseException("`add` takes one plain value — do the math in a `set` line instead", line.Line);
                    if (namedIndex != namedTo)
                        throw new ParseException(
                            "`add` to build progress looks like this: `add 10 to build progress` or `add 10 to build progress of Barracks`",
                            line.Line);
                    return namedNode;
                }
            }
            // `add <n> to build progress` — RTS construction progress (Task 18 Part C).
            // Only the exact trailing words `build progress` switch modes, so normal
            // `add 1 to Score` / `add 50 to Minerals` stay untouched.
            if (words.Count >= 2 && words[words.Count - 2].Type == TokenType.Word &&
                words[words.Count - 2].Text == "build" &&
                words[words.Count - 1].Type == TokenType.Word && words[words.Count - 1].Text == "progress")
            {
                if (words.Count >= 5 && words[words.Count - 3].Type == TokenType.Word &&
                    words[words.Count - 3].Text == "to")
                {
                    var buildNode = new AddBuildProgressNode { Line = line.Line };
                    int buildIndex = 1;
                    buildNode.Amount = ReadValue(words, ref buildIndex, line.Line);
                    if (buildNode.Amount is MathValue)
                        throw new ParseException("`add` takes one plain value — do the math in a `set` line instead", line.Line);
                    if (buildIndex != words.Count - 3)
                        throw new ParseException("`add` to build progress looks like this: `add 10 to build progress`", line.Line);
                    return buildNode;
                }
                throw new ParseException("`add` to build progress looks like this: `add 10 to build progress`", line.Line);
            }
            var node = new AddNode { Line = line.Line };
            int index = 1;
            node.Amount = ReadValue(words, ref index, line.Line);
            if (node.Amount is MathValue)
                throw new ParseException("`add` takes one plain value — do the math in a `set` line instead, like `set Score to Score + 1`", line.Line);
            if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != "to")
                throw new ParseException("an `add` line joins a value and a variable with `to` — `add 1 to Score`", line.Line);
            index++;
            if (TryReadResourceSuffix(words, index, out VariableValue resourceTarget))
            {
                node.Target = resourceTarget;
                index = words.Count;
            }
            else
            {
                node.Target = ReadVariable(words, ref index, line.Line, "`add`");
            }
            CheckEndOfLine(words, index, line.Line, node.Target.Owner.Length == 0
                ? $"add 1 to {node.Target.Name}"
                : $"add 1 to my {node.Target.Name}");
            return node;
        }

        private StatementNode ParseDeduct(BodyLineNode line)
        {
            var words = line.Words;
            var node = new DeductNode { Line = line.Line };
            int index = 1;
            node.Amount = ReadValue(words, ref index, line.Line);
            if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != "from")
                throw new ParseException("a `deduct` line joins a value and a resource with `from` — `deduct 100 from Minerals`", line.Line);
            index++;
            if (TryReadResourceSuffix(words, index, out VariableValue resourceTarget))
            {
                node.Target = resourceTarget;
                index = words.Count;
            }
            else
            {
                node.Target = ReadVariable(words, ref index, line.Line, "`deduct`");
            }
            CheckEndOfLine(words, index, line.Line, $"deduct <amount> from {node.Target.Name}");
            return node;
        }

        private StatementNode ParseIncrease(BodyLineNode line)
        {
            var words = line.Words;
            int byIndex = -1;
            for (int i = 1; i < words.Count; i++)
                if (words[i].Type == TokenType.Word && words[i].Text == "by")
                {
                    byIndex = i;
                    break;
                }
            if (byIndex < 2)
                throw new ParseException("`increase` looks like this: `increase Supply Max by 8`", line.Line);
            string name = JoinWords(words, 1, byIndex);
            if (!ResourceManager.TryCanonical(name, out string canonical))
                throw new ParseException($"`increase` needs a resource name like `Minerals` or `Supply Max` — I don't know one called \u201C{name}\u201D", line.Line);
            var node = new AddNode
            {
                Line = line.Line,
                Target = new VariableValue { Line = words[1].Line, Name = canonical },
            };
            int index = byIndex + 1;
            node.Amount = ReadValue(words, ref index, line.Line);
            CheckEndOfLine(words, index, line.Line, $"increase Supply Max by <amount>");
            return node;
        }

        /// <summary>
        /// Reads the weapon an ammo line targets: a plain name (`Pistol`), or `my Weapon` to mean
        /// whatever the actor holds right now.
        /// </summary>
        private static bool TryReadWeaponName(List<Token> words, ref int index, out string weapon, out string variable)
        {
            weapon = "";
            variable = "";
            if (index >= words.Count)
                return false;
            if (words[index].Type == TokenType.Word &&
                (words[index].Text == "my" || words[index].Text == "me"))
            {
                if (index + 1 >= words.Count || words[index + 1].Type != TokenType.Word)
                    return false;
                variable = words[index + 1].Text;
                index += 2;
                return true;
            }
            if (words[index].Type == TokenType.Word && words[index].Text == "to")
                return false;
            if (words[index].Type == TokenType.Word || words[index].Type == TokenType.Str)
            {
                weapon = words[index].Text;
                index++;
                return true;
            }
            return false;
        }

        /// <summary>Joins a run of words back into one name (used for multiword resources).</summary>
        private static string JoinWords(List<Token> words, int from, int until)
        {
            var parts = new System.Text.StringBuilder();
            for (int i = from; i < until; i++)
            {
                if (parts.Length > 0)
                    parts.Append(' ');
                parts.Append(words[i].Text);
            }
            return parts.ToString();
        }

        /// <summary>Reads a resource name that runs up to <paramref name="separator"/>
        /// (e.g. `set Supply Used to 5`). Returns false when the words are not a resource.</summary>
        private static bool TryReadResourcePrefix(List<Token> words, int start, string separator,
            out VariableValue target, out int separatorIndex)
        {
            target = null;
            separatorIndex = -1;
            int separatorAt = -1;
            for (int i = start; i < words.Count; i++)
            {
                if (words[i].Type == TokenType.Word && words[i].Text == separator)
                {
                    separatorAt = i;
                    break;
                }
            }
            if (separatorAt <= start)
                return false;
            string name = JoinWords(words, start, separatorAt);
            if (!ResourceManager.TryCanonical(name, out string canonical))
                return false;
            target = new VariableValue { Line = words[start].Line, Name = canonical };
            separatorIndex = separatorAt;
            return true;
        }

        /// <summary>Reads a trailing resource name (e.g. `deduct 5 from Supply Used`).</summary>
        private static bool TryReadResourceSuffix(List<Token> words, int start, out VariableValue target)
        {
            target = null;
            if (start >= words.Count)
                return false;
            string name = JoinWords(words, start, words.Count);
            if (!ResourceManager.TryCanonical(name, out string canonical))
                return false;
            target = new VariableValue { Line = words[start].Line, Name = canonical };
            return true;
        }

        private StatementNode ParseRemove(BodyLineNode line)
        {
            var words = line.Words;
            int index = 1;
            // `remove buff "stim" from me` ends a buff (Task 27 Part C).
            if (words.Count >= 2 && words[1].Type == TokenType.Word && words[1].Text == "buff")
            {
                var buffRaw = new RawActionNode { Line = line.Line };
                buffRaw.Words.AddRange(words);
                return buffRaw;
            }
            // `remove Potion from my bag` — takes one named item out of the bag
            if (words.Count >= 5 && words[index].Type == TokenType.Word &&
                words[index + 1].Text == "from" && words[index + 2].Text == "my" && words[index + 3].Text == "bag")
            {
                var node = new BagRemoveNode { Line = line.Line, Thing = words[index].Text };
                CheckEndOfLine(words, index + 4, line.Line, "remove Potion from my bag");
                return node;
            }
            throw new ParseException("`remove` looks like this: `remove Potion from my bag`", line.Line);
        }

        private StatementNode ParseEquip(BodyLineNode line)
        {
            var words = line.Words;
            // `equip Sword` — takes Sword from the bag and equips it on me
            if (words.Count >= 2 && words[1].Type == TokenType.Word)
            {
                var node = new EquipNode { Line = line.Line, Thing = words[1].Text };
                CheckEndOfLine(words, 2, line.Line, "equip Sword");
                return node;
            }
            throw new ParseException("`equip` looks like this: `equip Sword`", line.Line);
        }

        private StatementNode ParseDrop(BodyLineNode line)
        {
            var words = line.Words;
            // `drop Sword` — unequips if worn, takes from the bag, drops the item at my feet
            if (words.Count >= 2 && words[1].Type == TokenType.Word)
            {
                var node = new DropNode { Line = line.Line, Thing = words[1].Text };
                CheckEndOfLine(words, 2, line.Line, "drop Sword");
                return node;
            }
            throw new ParseException("`drop` looks like this: `drop Sword`", line.Line);
        }

        private ParseException UnknownWord(Token word)
        {
            string suggestion = Suggest(word.Text);
            if (suggestion.Length > 0)
                return new ParseException($"I don't know the word \u201C{word.Text}\u201D. Did you mean \u201C{suggestion}\u201D?", word.Line);
            return new ParseException($"I don't know what \u201C{word.Text}\u201D means yet.", word.Line);
        }

        private static string Suggest(string word)
        {
            string best = "";
            int bestDistance = int.MaxValue;
            foreach (var candidate in WordRegistry.Suggestables)
            {
                int distance = WordDistance.Between(word, candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return bestDistance <= 2 ? best : "";
        }

        private static string DescribeWord(Token token)
        {
            switch (token.Type)
            {
                case TokenType.Word: return $@"the word ""{token.Text}""";
                case TokenType.Number: return $"the number {token.Text}";
                case TokenType.Str: return $@"quoted text ""{token.Text}""";
                default: return token.Text;
            }
        }
    }
}
