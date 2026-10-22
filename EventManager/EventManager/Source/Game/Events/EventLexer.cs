using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game
{
    public class EventLexer
    {
        public List<Token> Tokenize(string source)
        {
            var tokens = new List<Token>();
            var depths = new Stack<int>();
            depths.Push(0);

            var lines = (source ?? "").Replace("\r", "").Split('\n');
            if (lines.Length == 1 && lines[0].Length == 0)
                lines = Array.Empty<string>();

            int lastPhysical = lines.Length;
            while (lastPhysical > 0 && lines[lastPhysical - 1].Trim().Length == 0)
                lastPhysical--;

            for (int index = 0; index < lines.Length; index++)
                ProcessLine(lines[index], index + 1, depths, tokens);

            while (depths.Peek() > 0)
            {
                depths.Pop();
                tokens.Add(new Token { Type = TokenType.Dedent, Line = Math.Max(1, lastPhysical) });
            }

            tokens.Add(new Token { Type = TokenType.EndOfFile, Line = Math.Max(1, lastPhysical) });
            return tokens;
        }

        private static void ProcessLine(string text, int line, Stack<int> depths, List<Token> tokens)
        {
            int i = 0;
            while (i < text.Length && text[i] == ' ')
                i++;
            if (i < text.Length && text[i] == '\t')
                throw new LexException("indentation must use spaces (2 per level), but there's a tab here", line);

            string body = text.Substring(i);
            if (string.IsNullOrWhiteSpace(body))
                return;
            if (body[0] == '#')
                return;

            if (i % 2 != 0)
                throw new LexException($"{i} spaces of indent — indent must be a multiple of 2 (one level = 2 spaces)", line);
            int depth = i / 2;

            int current = depths.Peek();
            if (depth > current + 1)
                throw new LexException($"indentation jumped from level {current} to level {depth} at once — step in with exactly 2 spaces per level", line);

            while (depth < current)
            {
                depths.Pop();
                tokens.Add(new Token { Type = TokenType.Dedent, Line = line });
                current = depths.Peek();
            }
            while (depth > current)
            {
                current++;
                depths.Push(current);
                tokens.Add(new Token { Type = TokenType.Indent, Line = line, IndentLevel = current });
            }

            bool anyTokens = false;
            int pos = 0;
            while (pos < body.Length)
            {
                char c = body[pos];
                if (c == ' ' || c == '\t')
                {
                    pos++;
                    continue;
                }
                if (c == '#')
                    break;
                if (c == '"')
                {
                    int end = body.IndexOf('"', pos + 1);
                    if (end < 0)
                        throw new LexException("this string starts and never ends — a closing \" quote is missing", line);
                    tokens.Add(new Token { Type = TokenType.Str, Text = body.Substring(pos + 1, end - pos - 1), Line = line });
                    pos = end + 1;
                    anyTokens = true;
                    continue;
                }
                int start = pos;
                while (pos < body.Length && body[pos] != ' ' && body[pos] != '\t' && body[pos] != '#' && body[pos] != '"')
                    pos++;
                string raw = body.Substring(start, pos - start);
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    tokens.Add(new Token { Type = TokenType.Number, Number = number, Text = raw, Line = line });
                else
                    tokens.Add(new Token { Type = TokenType.Word, Text = raw, Line = line });
                anyTokens = true;
            }

            if (anyTokens)
                tokens.Add(new Token { Type = TokenType.NewLine, Line = line });
        }
    }
}
