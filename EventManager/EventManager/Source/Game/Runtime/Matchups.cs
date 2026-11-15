using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// RTS attack matchups (Task 23 Part B): which unit type can hit which.
    ///
    /// The table is data: one global per pair, named <c>&lt;Attacker&gt;CanAttack&lt;Defender&gt;</c> —
    /// <c>LandCanAttackAir</c>, <c>AirCanAttackLand</c>, <c>SeaCanAttackSea</c>, and so on — read
    /// from <c>Matchups.events</c> at load and overridable at runtime by any sheet that writes the
    /// same global. A pair nobody wrote uses the built-in default below, which is the usual RTS
    /// shape: a unit hits its own ground, land and sea hit each other, neither land nor sea can
    /// hit air, and air hits everything.
    ///
    /// The condition <c>me can attack other</c> asks the table, and the runtime asks it too: a
    /// <c>"damage"</c>/<c>"hurt"</c> message between two types that cannot fight is dropped before
    /// it reaches the target's sheet, so a mismatched attack does nothing at all.
    /// </summary>
    public static class Matchups
    {
        /// <summary>The data file's name, like <c>EnemyTypes.events</c> and <c>WeaponData.events</c>.</summary>
        public const string FileName = "Matchups";

        /// <summary>Every unit type the table has a row for, in help order.</summary>
        public static readonly string[] Types = { UnitTypes.Land, UnitTypes.Air, UnitTypes.Sea };

        private static readonly Dictionary<(string Attacker, string Defender), bool> Authored =
            new Dictionary<(string, string), bool>();

        /// <summary>The global naming one pair, e.g. <c>LandCanAttackAir</c>.</summary>
        public static string VariableFor(string attacker, string defender)
        {
            return Capitalised(attacker) + "CanAttack" + Capitalised(defender);
        }

        /// <summary>
        /// Whether a unit of <paramref name="attackerType"/> can hit a unit of
        /// <paramref name="defenderType"/>: the live global wins, then the loaded data file, then
        /// <see cref="Default"/>.
        /// </summary>
        public static bool CanAttack(EventVariables variables, string attackerType, string defenderType)
        {
            string attacker = UnitTypes.Canonical(attackerType) ?? UnitTypes.Default;
            string defender = UnitTypes.Canonical(defenderType) ?? UnitTypes.Default;
            string variable = VariableFor(attacker, defender);
            if (variables != null && variables.HasGlobal(variable))
                return variables.GetGlobal(variable).AsNumber() != 0;
            if (Authored.TryGetValue((attacker, defender), out bool authored))
                return authored;
            return Default(attacker, defender);
        }

        /// <summary>The built-in table, before any data file or global says otherwise.</summary>
        public static bool Default(string attackerType, string defenderType)
        {
            string attacker = UnitTypes.Canonical(attackerType) ?? UnitTypes.Default;
            string defender = UnitTypes.Canonical(defenderType) ?? UnitTypes.Default;
            if (attacker == UnitTypes.Air)
                return true; // air strikes everything, air included
            return defender != UnitTypes.Air; // ground and sea cannot reach the sky
        }

        /// <summary>The two actors' own types decide the answer.</summary>
        public static bool CanAttackBetween(EventVariables variables, object attacker, object defender)
        {
            return CanAttack(variables,
                UnitTypes.TypeOf(variables, attacker),
                UnitTypes.TypeOf(variables, defender));
        }

        /// <summary>`me can attack other` — the acting unit against a resolved target.</summary>
        public static bool CanAttackTarget(EventContext context, object target)
        {
            if (context == null || target == null)
                return false;
            return CanAttackBetween(context.Variables, context.Me, target);
        }

        /// <summary>The whole table as one line of text, for warnings and HUDs.</summary>
        public static string Describe()
        {
            var parts = new List<string>();
            foreach (string attacker in Types)
                foreach (string defender in Types)
                    parts.Add($"{attacker} vs {defender}: {(CanAttack(null, attacker, defender) ? "yes" : "no")}");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Reads the table out of a data file (<c>Matchups.events</c>), one
        /// <c>set &lt;Attacker&gt;CanAttack&lt;Defender&gt; to true|false</c> per pair. Called once
        /// per load/reload by the bridge; pass null to forget the file.
        /// </summary>
        public static void LoadData(ProgramFile file)
        {
            Authored.Clear();
            if (file == null)
                return;
            foreach (EventNode ev in file.Events)
            {
                foreach (BodyLineNode line in ev.Body)
                {
                    if (!(line.Statement is SetNode set) || set.Op != '\0')
                        continue;
                    if (!TrySplit(set.Target.Name, out string attacker, out string defender))
                        continue;
                    ValueBox value = LiteralOf(set.Value);
                    if (value.IsNone())
                        continue;
                    Authored[(attacker, defender)] = value.AsNumber() != 0;
                }
            }
        }

        /// <summary>
        /// Splits <c>LandCanAttackAir</c> into the two canonical unit types. False for any other
        /// variable name, so a data file may hold unrelated globals.
        /// </summary>
        public static bool TrySplit(string name, out string attacker, out string defender)
        {
            attacker = "";
            defender = "";
            if (string.IsNullOrEmpty(name))
                return false;
            int split = name.IndexOf("CanAttack", StringComparison.OrdinalIgnoreCase);
            if (split <= 0 || split + 9 >= name.Length)
                return false;
            string left = UnitTypes.Canonical(name.Substring(0, split));
            string right = UnitTypes.Canonical(name.Substring(split + 9));
            if (left == null || right == null)
                return false;
            attacker = left;
            defender = right;
            return true;
        }

        private static ValueBox LiteralOf(ValueNode node)
        {
            switch (node)
            {
                case BooleanValue boolean:
                    return ValueBox.OfBoolean(boolean.Value);
                case NumberValue number:
                    return ValueBox.OfNumber(number.Value);
                case TextValue text:
                    return ValueBox.OfText(text.Text);
                default:
                    return new ValueBox();
            }
        }

        private static string Capitalised(string type)
        {
            string canonical = UnitTypes.Canonical(type) ?? UnitTypes.Default;
            return char.ToUpperInvariant(canonical[0]) + canonical.Substring(1);
        }
    }
}
