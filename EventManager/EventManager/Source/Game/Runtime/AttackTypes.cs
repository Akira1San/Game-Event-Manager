using System;

namespace Game
{
    /// <summary>
    /// RTS attack types (Task 23 Part A): how a unit strikes — <c>melee</c> (on contact) or
    /// <c>ranged</c> (from its attack range).
    ///
    /// The type is an ordinary per-unit variable, written once
    /// (<c>set my attack type to "ranged"</c>) and read back with <c>me is ranged</c>. A unit that
    /// never wrote one is melee, which is what every sheet did before this word existed.
    ///
    /// The type decides the reach a unit has before it declares <c>my attack range</c>: see
    /// <see cref="OrderManager.DefaultRangeFor"/>. The strike itself stays authored — a melee
    /// sheet sends <c>"damage"</c> when it touches its target, a ranged sheet spawns its
    /// projectile — because the engine has no projectile of its own to fire.
    /// </summary>
    public static class AttackTypes
    {
        /// <summary>The per-unit variable holding the type (`my attack type`).</summary>
        public const string Variable = "attack type";

        public const string Melee = "melee";
        public const string Ranged = "ranged";

        /// <summary>Every type, in the order the help text lists them.</summary>
        public static readonly string[] All = { Melee, Ranged };

        /// <summary>The type a unit has before its sheet writes one.</summary>
        public const string Default = Melee;

        /// <summary>Both spellings the language accepts for the variable.</summary>
        private static readonly string[] VariableNames = { "attack type", "AttackType" };

        /// <summary>The canonical spelling of a written type, or null when it is not one.</summary>
        public static string Canonical(string written)
        {
            if (string.IsNullOrWhiteSpace(written))
                return null;
            string trimmed = written.Trim();
            foreach (string type in All)
                if (string.Equals(type, trimmed, StringComparison.OrdinalIgnoreCase))
                    return type;
            return null;
        }

        public static bool IsKnown(string written)
        {
            return Canonical(written) != null;
        }

        /// <summary>Every known type, comma-separated, for warning messages.</summary>
        public static string KnownNames()
        {
            return string.Join(", ", All);
        }

        /// <summary>True for the two spellings of the per-unit variable (<c>attack type</c>).</summary>
        public static bool IsVariableName(string name)
        {
            foreach (string candidate in VariableNames)
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// The unit's own <c>my attack type</c>, canonically spelled. A sheet that never wrote one
        /// (or wrote something that is not a type) strikes on contact like a melee unit.
        /// </summary>
        public static string TypeOf(EventVariables variables, object unit)
        {
            if (variables == null || unit == null)
                return Default;
            foreach (string name in VariableNames)
            {
                if (!variables.HasLocal(unit, name))
                    continue;
                string canonical = Canonical(variables.GetLocal(unit, name).AsText());
                if (canonical != null)
                    return canonical;
            }
            return Default;
        }

        /// <summary>True when that type strikes from a distance instead of on contact.</summary>
        public static bool IsRanged(string attackType)
        {
            return string.Equals(Canonical(attackType), Ranged, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>`me is melee` / `me is ranged`.</summary>
        public static bool Is(EventContext context, string type)
        {
            string wanted = Canonical(type);
            if (context == null || wanted == null)
                return false;
            return string.Equals(TypeOf(context.Variables, context.Me), wanted,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
