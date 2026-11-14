using System;

namespace Game
{
    /// <summary>
    /// Local two-player support (Task 16 Part F) — TK4's "two players, one PC", with no
    /// networking at all. A player is simply an actor named <c>Player1</c> or <c>Player2</c>,
    /// and the older single <c>Player</c> still counts as one so single-player sheets keep
    /// working unchanged.
    ///
    /// Only the naming rules live here (actor names, the <c>Player1 Score</c> variable shape,
    /// the "any player" words) so they are unit-testable without the engine. The live state —
    /// who joined, who died, who last hit whom — is polled by <see cref="EventRuntime"/>.
    /// </summary>
    public static class Players
    {
        public const int MaxPlayers = 2;

        /// <summary>A bare <c>player</c> means "any player" (the nearest one, in target position).</summary>
        public const string GroupWord = "player";
        public const string NearestGroup = "nearest player";
        public const string AllGroup = "all players";

        /// <summary>The per-player counters Game.events keeps as globals (e.g. <c>Player1 Score</c>).</summary>
        public static readonly string[] Fields = { "Score", "Lives", "Kills" };

        public static string ActorName(int number)
        {
            return "Player" + number;
        }

        public static string VariableName(int number, string field)
        {
            return ActorName(number) + " " + field;
        }

        public static bool IsValidNumber(int number)
        {
            return number >= 1 && number <= MaxPlayers;
        }

        /// <summary>
        /// Reads a player number from an actor-name-shaped word: <c>Player1</c>,
        /// <c>player 1</c> and <c>Player2</c> all work; a bare <c>Player</c> is not a number.
        /// </summary>
        public static bool TryReadNumber(string text, out int number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string word = text.Trim();
            if (!word.StartsWith("player", StringComparison.OrdinalIgnoreCase))
                return false;
            string digits = word.Substring("player".Length).Replace(" ", "");
            if (digits.Length == 0)
                return false;
            int parsed;
            if (!int.TryParse(digits, out parsed) || !IsValidNumber(parsed))
                return false;
            number = parsed;
            return true;
        }

        /// <summary>
        /// Reads <c>player 1</c> (two words, used by the condition and trigger sheets) or
        /// <c>Player1</c> (one word) at <paramref name="index"/>. <paramref name="used"/> says
        /// how many words the reference spent.
        /// </summary>
        public static bool TryReadReference(string[] words, int index, out int number, out int used)
        {
            number = 0;
            used = 0;
            if (words == null || index >= words.Length)
                return false;
            if (TryReadNumber(words[index], out number))
            {
                used = 1;
                return true;
            }
            if (!string.Equals(words[index], GroupWord, StringComparison.OrdinalIgnoreCase) ||
                index + 1 >= words.Length)
                return false;
            if (!TryReadNumber(GroupWord + words[index + 1], out number))
                return false;
            used = 2;
            return true;
        }

        /// <summary>True for any actor name this system treats as a player: Player, Player1, Player2.</summary>
        public static bool IsPlayerActorName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (TryReadNumber(name, out _))
                return true;
            return string.Equals(name.Trim(), "Player", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True for the words that mean "whichever player is closest": <c>player</c> and <c>nearest player</c>.</summary>
        public static bool IsNearestReference(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            string trimmed = name.Trim();
            return string.Equals(trimmed, GroupWord, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, NearestGroup, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True for <c>all players</c> — the broadcast target of <c>send message</c>.</summary>
        public static bool IsAllReference(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   string.Equals(name.Trim(), AllGroup, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads the two-word per-player variable: <c>Player1</c> + <c>Score</c> →
        /// "Player1 Score". The second word has to be one of <see cref="Fields"/>, so an
        /// ordinary <c>set Foo Bar</c> is never swallowed.
        /// </summary>
        public static bool TryReadVariableName(string first, string second, out string name)
        {
            name = "";
            int number;
            if (!TryReadNumber(first, out number) || string.IsNullOrWhiteSpace(second))
                return false;
            foreach (string field in Fields)
            {
                if (!string.Equals(field, second.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                name = VariableName(number, field);
                return true;
            }
            return false;
        }

        /// <summary>Reads the run-together spelling <c>Player1Score</c> as <c>Player1 Score</c>.</summary>
        public static bool TryReadVariableName(string single, out string name)
        {
            name = "";
            if (string.IsNullOrWhiteSpace(single))
                return false;
            foreach (string field in Fields)
            {
                if (single.Length <= field.Length ||
                    !single.EndsWith(field, StringComparison.OrdinalIgnoreCase))
                    continue;
                return TryReadVariableName(single.Substring(0, single.Length - field.Length), field, out name);
            }
            return false;
        }
    }

    /// <summary>
    /// What the runtime remembers about one player between ticks, so
    /// <c>when player &lt;n&gt; joins/dies/score changes</c> fire on their edge instead of
    /// every frame (Task 16 Part F).
    /// </summary>
    public class PlayerWatch
    {
        public bool Initialized;
        public bool Alive;
        public bool ScoreKnown;
        public double Score;
    }
}
