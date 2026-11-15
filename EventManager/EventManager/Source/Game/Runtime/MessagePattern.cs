using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game
{
    /// <summary>
    /// <c>when message "damage &lt;amount&gt;" arrives</c> — a message trigger whose quoted
    /// text carries one or more <c>&lt;slot&gt;</c> placeholders (Task 18 Part G). Each slot
    /// matches a number in the arriving message and hands it to the receiving sheet, so one
    /// event answers hits of any size instead of one event per damage tier.
    /// <c>"damage"</c> without a slot stays an exact name match, so existing sheets that
    /// send and listen for a bare damage message are unchanged.
    /// </summary>
    public static class MessagePattern
    {
        /// <summary>True when the quoted text is a template (it has a <c>&lt;slot&gt;</c>).</summary>
        public static bool IsPattern(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            int open = text.IndexOf('<');
            return open >= 0 && text.IndexOf('>', open + 1) > open + 1;
        }

        /// <summary>
        /// Matches <paramref name="message"/> against <paramref name="pattern"/>. Literal words
        /// compare case-insensitively; every slot takes one number, captured under the slot's
        /// own name. A plain (slot-less) pattern is an exact name compare.
        /// </summary>
        public static bool TryMatch(string pattern, string message, out List<KeyValuePair<string, double>> captures)
        {
            captures = new List<KeyValuePair<string, double>>();
            string[] patternParts = Split(pattern);
            string[] messageParts = Split(message);
            if (patternParts.Length == 0 || patternParts.Length != messageParts.Length)
                return false;

            for (int i = 0; i < patternParts.Length; i++)
            {
                if (TryReadSlot(patternParts[i], out string slot))
                {
                    if (!double.TryParse(messageParts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                        return false;
                    captures.Add(new KeyValuePair<string, double>(slot, number));
                }
                else if (!string.Equals(patternParts[i], messageParts[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The name inside a <c>&lt;slot&gt;</c> word, if that is what the word is.</summary>
        private static bool TryReadSlot(string word, out string slot)
        {
            slot = "";
            if (word.Length < 3 || word[0] != '<' || word[word.Length - 1] != '>')
                return false;
            slot = word.Substring(1, word.Length - 2).Trim();
            return slot.Length > 0;
        }

        private static string[] Split(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();
            return text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
