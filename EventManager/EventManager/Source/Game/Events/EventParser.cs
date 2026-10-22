using System;
using System.Collections.Generic;

namespace Game
{
    public class EventParser
    {
        private static readonly string[] MathOps = { "+", "-", "*", "/" };

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

        private static bool TryMatchShape(string[] shape, int si, List<Token> trigger, int ti)
        {
            if (si >= shape.Length)
                return ti == trigger.Count;
            string part = shape[si];
            if (part.Contains("<"))
            {
                string name = part.Replace("\"", "").Replace("<", "").Replace(">", "");
                if (name == "value")
                {
                    return ti < trigger.Count && si == shape.Length - 1;
                }
                if (name == "n" || name == "dist")
                {
                    return ti < trigger.Count && trigger[ti].Type == TokenType.Number &&
                           TryMatchShape(shape, si + 1, trigger, ti + 1);
                }
                if (name == "name")
                {
                    return ti < trigger.Count && trigger[ti].Type == TokenType.Str &&
                           TryMatchShape(shape, si + 1, trigger, ti + 1);
                }
                for (int take = 1; ti + take - 1 < trigger.Count; take++)
                {
                    if (trigger[ti + take - 1].Type != TokenType.Word)
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
                ev.Body.Add(ParseBodyLine());
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
            if (first.Text == "unless")
            {
                line.IsCondition = true;
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
            if (first.Text == "set")
            {
                line.Statement = ParseSet(line);
                return;
            }
            if (first.Text == "add")
            {
                line.Statement = ParseAdd(line);
                return;
            }
            if (Array.IndexOf(WordRegistry.ActionWords, first.Text) >= 0)
            {
                var action = new RawActionNode { Line = line.Line };
                action.Words.AddRange(line.Words);
                line.Statement = action;
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
            if (t.Type == TokenType.Word)
                return ReadVariable(words, ref index, flagLine, "`set`");
            throw new ParseException("a value was expected here — a number, quoted text, or a variable", t.Line);
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
                if (index + 1 < words.Count && words[index + 1].Type == TokenType.Word)
                {
                    var name = words[index + 1];
                    index += 2;
                    return new VariableValue { Line = t.Line, Owner = "my", Name = name.Text };
                }
                throw new ParseException("`my` needs a variable name right after it — like `my health`", t.Line);
            }
            if (!char.IsLetter(t.Text[0]) || !char.IsUpper(t.Text[0]))
                throw new ParseException($@"I can't use {DescribeWord(t)} as a variable — variables start with a capital letter, or use `my` for a per-object one (like `Score` or `my health`)", t.Line);
            index++;
            return new VariableValue { Line = t.Line, Name = t.Text };
        }

        private StatementNode ParseSet(BodyLineNode line)
        {
            var words = line.Words;
            if (words.Count > 2 && words[1].Type == TokenType.Word && words[1].Text == "text" &&
                words[2].Type == TokenType.Word && words[2].Text == "of")
                return ParseSetText(line);
            var node = new SetNode { Line = line.Line };
            int index = 1;
            node.Target = ReadVariable(words, ref index, line.Line, "`set`");
            if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != "to")
                throw new ParseException("a `set` line joins a variable and a value with `to` — `set Score to 0`", line.Line);
            index++;
            node.Value = ReadValue(words, ref index, line.Line);
            if (index < words.Count && words[index].Type == TokenType.Word && words[index].Text.Length == 1 &&
                Array.IndexOf(MathOps, words[index].Text) >= 0)
            {
                node.Op = words[index].Text[0];
                index++;
                node.Right = ReadValue(words, ref index, line.Line);
            }
            CheckEndOfLine(words, index, line.Line, node.Target.Owner.Length == 0
                ? $"set {node.Target.Name} to <value>"
                : $"set my {node.Target.Name} to <value>");
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

        private AddNode ParseAdd(BodyLineNode line)
        {
            var words = line.Words;
            var node = new AddNode { Line = line.Line };
            int index = 1;
            node.Amount = ReadValue(words, ref index, line.Line);
            if (node.Amount is MathValue)
                throw new ParseException("`add` takes one plain value — do the math in a `set` line instead, like `set Score to Score + 1`", line.Line);
            if (index >= words.Count || words[index].Type != TokenType.Word || words[index].Text != "to")
                throw new ParseException("an `add` line joins a value and a variable with `to` — `add 1 to Score`", line.Line);
            index++;
            node.Target = ReadVariable(words, ref index, line.Line, "`add`");
            CheckEndOfLine(words, index, line.Line, node.Target.Owner.Length == 0
                ? $"add 1 to {node.Target.Name}"
                : $"add 1 to my {node.Target.Name}");
            return node;
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
