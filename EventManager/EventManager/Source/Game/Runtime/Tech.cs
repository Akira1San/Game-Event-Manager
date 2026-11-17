using System;

namespace Game
{
    /// <summary>
    /// Per-player tech and weapon values (Task 27 Part B).
    ///
    /// Upgrades are globals written per player — <c>Player2 UpgradeStim</c>, or the run-together
    /// <c>Player2UpgradeStim</c> — so research on one side never raises the other's tech level.
    /// A weapon field takes the same shape: <c>Player2 PistolDamage</c> is a bonus and
    /// <c>Player2 PistolDamageBonusMultiplier</c> a multiplier, and
    /// <see cref="WeaponData.Effective"/> combines them as
    /// <c>base × multiplier + bonus</c> over whatever <c>WeaponData.events</c> defines.
    ///
    /// Player numbers follow the minimap and the fog: index 0 is the local player and is named
    /// <c>Player1</c>, index 1 is named <c>Player2</c> (the same numbering
    /// <see cref="Players.ActorName"/> and <c>Player1 Score</c> use).
    ///
    /// No Flax types, so the runtime and the tests drive it directly.
    /// </summary>
    public static class Tech
    {
        /// <summary>The actor variable the acting unit's player falls back to (<c>my Player</c>).</summary>
        public const string PlayerVariable = FogOfWarManager.PlayerVariable;

        /// <summary>Suffix that turns <c>PistolDamage</c> into <c>PistolDamageBonusMultiplier</c>.</summary>
        public const string MultiplierSuffix = "BonusMultiplier";

        /// <summary>The word form of a player index: 0 → <c>Player1</c>, 1 → <c>Player2</c>.</summary>
        public static string PlayerPrefix(int playerIndex)
        {
            return Players.ActorName(playerIndex < 0 ? 1 : playerIndex + 1);
        }

        /// <summary>One per-player global: <c>VariableName(1, "UpgradeStim")</c> → <c>"Player2 UpgradeStim"</c>.</summary>
        public static string VariableName(int playerIndex, string name)
        {
            return PlayerPrefix(playerIndex) + " " + (name ?? "");
        }

        /// <summary>A weapon field's bonus global (<c>Player2 PistolDamage</c>).</summary>
        public static string WeaponBonusName(int playerIndex, string weapon, string field)
        {
            return VariableName(playerIndex, (weapon ?? "") + (field ?? ""));
        }

        /// <summary>A weapon field's multiplier global (<c>Player2 PistolDamageBonusMultiplier</c>).</summary>
        public static string WeaponMultiplierName(int playerIndex, string weapon, string field)
        {
            return VariableName(playerIndex, (weapon ?? "") + (field ?? "") + MultiplierSuffix);
        }

        /// <summary>
        /// The player the acting unit belongs to: fog first, then <c>my Player</c>, then 0 — the
        /// same rule the minimap uses, so a unit's tech never depends on who is reading it.
        /// </summary>
        public static int PlayerIndexOf(EventContext context)
        {
            if (context == null)
                return 0;
            object me = context.Me;
            if (me == null)
                return 0;
            if (context.Runtime != null && context.Runtime.Fog.TryGetActor(me, out FogActor info))
                return info.Player < 0 ? 0 : info.Player;
            if (context.Variables != null &&
                context.Variables.HasLocal(me, PlayerVariable))
            {
                int fromVariable = (int)context.Variables.GetLocal(me, PlayerVariable).AsNumber();
                return fromVariable < 0 ? 0 : fromVariable;
            }
            return 0;
        }

        /// <summary>
        /// <c>Player2 UpgradeStim</c> read as a number. A level nobody wrote is 0, and a negative
        /// level (a penalty or a typo) reads as 0 — research only ever goes up.
        /// </summary>
        public static double LevelOf(EventVariables variables, int playerIndex, string upgrade)
        {
            if (string.IsNullOrEmpty(upgrade))
                return 0;
            string name = FindGlobal(variables, VariableName(playerIndex, upgrade));
            if (name == null)
                return 0;
            double level = variables.GetGlobal(name).AsNumber();
            return level < 0 ? 0 : level;
        }

        /// <summary><c>set Player2 UpgradeStim to 1</c>: raises (or lowers) one player's level.</summary>
        public static void SetLevel(EventVariables variables, int playerIndex, string upgrade, double level)
        {
            if (variables == null || string.IsNullOrEmpty(upgrade))
                return;
            variables.SetGlobal(VariableName(playerIndex, upgrade),
                ValueBox.OfNumber(level < 0 ? 0 : level));
        }

        /// <summary>
        /// One weapon number with a player's tech on top: <c>base × multiplier + bonus</c>, where a
        /// missing multiplier is 1 and a missing bonus is 0 — so a player with no upgrades reads
        /// exactly the base number.
        /// </summary>
        public static double Scale(EventVariables variables, int playerIndex, string name, double baseValue)
        {
            double multiplier = 1;
            string multiplierName = FindGlobal(variables,
                VariableName(playerIndex, (name ?? "") + MultiplierSuffix));
            if (multiplierName != null)
                multiplier = variables.GetGlobal(multiplierName).AsNumber();

            double bonus = 0;
            string bonusName = FindGlobal(variables, VariableName(playerIndex, name));
            if (bonusName != null)
                bonus = variables.GetGlobal(bonusName).AsNumber();

            return baseValue * multiplier + bonus;
        }

        /// <summary>A per-player value that replaces a field outright (text and flags: `Player2 PistolBullet`).</summary>
        public static bool TryOverride(EventVariables variables, int playerIndex, string name, out ValueBox value)
        {
            value = new ValueBox();
            string actual = FindGlobal(variables, VariableName(playerIndex, name));
            if (actual == null)
                return false;
            value = ValueBox.Copy(variables.GetGlobal(actual));
            return true;
        }

        /// <summary>
        /// Reads <c>Player2 UpgradeStim</c> as one global. The second word has to start with a
        /// capital letter, and the counters Players already owns (<c>Player1 Score</c>) are left
        /// to <see cref="Players.TryReadVariableName(string, string, out string)"/>.
        /// </summary>
        public static bool TryReadVariableName(string first, string second, out string name)
        {
            name = "";
            if (string.IsNullOrWhiteSpace(second))
                return false;
            int number;
            if (!Players.TryReadNumber(first, out number))
                return false;
            string trimmed = second.Trim();
            if (trimmed.Length == 0 || !char.IsUpper(trimmed[0]))
                return false;
            if (Players.TryReadVariableName(first, trimmed, out _))
                return false;
            name = VariableName(number - 1, trimmed);
            return true;
        }

        /// <summary>
        /// Reads the run-together spelling <c>Player2UpgradeStim</c> as <c>Player2 UpgradeStim</c>.
        /// Anything that is not a player name followed by a capitalised word is refused, so
        /// <c>Player2</c> on its own stays an actor name.
        /// </summary>
        public static bool TryReadVariableName(string single, out string name)
        {
            name = "";
            if (string.IsNullOrWhiteSpace(single))
                return false;
            string trimmed = single.Trim();
            const string prefix = "Player";
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            int digitsEnd = prefix.Length;
            while (digitsEnd < trimmed.Length && char.IsDigit(trimmed[digitsEnd]))
                digitsEnd++;
            if (digitsEnd == prefix.Length || digitsEnd >= trimmed.Length)
                return false;
            int number;
            if (!int.TryParse(trimmed.Substring(prefix.Length, digitsEnd - prefix.Length), out number) ||
                !Players.IsValidNumber(number))
                return false;
            string rest = trimmed.Substring(digitsEnd);
            if (rest.Length == 0 || !char.IsUpper(rest[0]))
                return false;
            name = VariableName(number - 1, rest);
            return true;
        }

        /// <summary>The global actually carrying a name, matched case-insensitively, or null.</summary>
        private static string FindGlobal(EventVariables variables, string name)
        {
            if (variables == null || string.IsNullOrEmpty(name))
                return null;
            return variables.FindGlobalName(name);
        }
    }
}
