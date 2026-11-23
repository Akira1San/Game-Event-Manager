using System;

namespace Game
{
    /// <summary>
    /// Typed damage (Task 9 F2). One place that turns a written damage type (`fire`, `cold`,
    /// `poison`, `steel`, …) into the resistance stat it meets (`FireResist`, …) and spends a
    /// hit through the existing <see cref="HealthManager.Damage"/> pipeline:
    /// <c>&lt;Type&gt;Resist</c> takes its percentage off first, then <c>my Armor</c>,
    /// <c>my Shield</c> and <c>my Health</c>.
    ///
    /// Every type has a stat name; only the ones an actor carries do anything, so `steel`
    /// against an actor with <c>my FireResist 50</c> deals its full number. A resistance is a
    /// percentage clamped to 0..100, so <c>my FireResist 999</c> reads as immunity rather than
    /// healing the victim.
    ///
    /// Pure logic with no engine types, so the event runtime and the tests drive it directly;
    /// the .events words that reach it are <c>damage &lt;target&gt; by &lt;n&gt; [&lt;type&gt;]</c>
    /// and <c>damage area around &lt;spot&gt; by &lt;n&gt; [&lt;type&gt;] [for radius &lt;r&gt;]</c>.
    /// </summary>
    public static class DamageTypes
    {
        /// <summary>The resistance stat a type meets: <c>fire</c> → <c>FireResist</c>,
        /// <c>cold</c> → <c>ColdResist</c>. The words are the same names <see cref="ItemStats"/>
        /// and the affix rows already use, so gear and buffs feed this without a new vocabulary.
        /// An empty type has no stat.</summary>
        public static string ResistStat(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return "";
            string trimmed = type.Trim();
            string first = trimmed.Substring(0, 1).ToUpperInvariant();
            string rest = trimmed.Length > 1 ? trimmed.Substring(1).ToLowerInvariant() : "";
            return first + rest + "Resist";
        }

        /// <summary>How much of <paramref name="type"/> the victim resists, as a percentage.
        /// Clamped to 0..100; a stat the victim never set is 0, and so is a type whose
        /// resistance does not exist on it.</summary>
        public static double ResistOf(EventVariables variables, object victim, string type)
        {
            string stat = ResistStat(type);
            if (variables == null || victim == null || stat.Length == 0 || !variables.HasLocal(victim, stat))
                return 0;
            return Math.Max(0, Math.Min(100, variables.GetLocal(victim, stat).AsNumber()));
        }

        /// <summary>The number that reaches armor once the victim's resistance has taken its
        /// share: 30 fire against <c>my FireResist 50</c> is 15.</summary>
        public static double Mitigate(EventVariables variables, object victim, double amount, string type)
        {
            if (amount <= 0)
                return 0;
            return amount * (100 - ResistOf(variables, victim, type)) / 100;
        }

        /// <summary>Resists the hit, then spends it through armor → shield → health, exactly
        /// the path <c>add -&lt;n&gt; to Health</c> uses. A sheet that never declared health is
        /// left alone, as <see cref="HealthManager.Damage"/> already promises.</summary>
        public static DamageResult Apply(EventVariables variables, object victim, double amount, string type)
        {
            return HealthManager.Damage(variables, victim, Mitigate(variables, victim, amount, type));
        }
    }
}
