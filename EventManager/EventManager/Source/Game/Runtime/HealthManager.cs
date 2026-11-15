using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>The outcome of one <see cref="HealthManager.Damage"/> call.</summary>
    public struct DamageResult
    {
        /// <summary>The damage left after armor: what the shield and health then absorb.</summary>
        public double Mitigated;
        /// <summary>How much the shield soaked up.</summary>
        public double AbsorbedByShield;
        /// <summary>How much came off <c>my Health</c>.</summary>
        public double HealthLost;
        /// <summary>True when this hit took health from above 0 to 0.</summary>
        public bool Died;
    }

    /// <summary>
    /// RTS per-unit health (Task 18 Part G). <c>my Health</c>, <c>my MaxHealth</c>,
    /// <c>my Shield</c>, <c>my MaxShield</c> and <c>my Armor</c> are ordinary per-actor
    /// variables, but writes made with <c>set</c> / <c>add</c> / <c>deduct</c> go through
    /// here so they stay inside their bounds:
    /// <list type="bullet">
    /// <item><c>set Health to &lt;n&gt;</c> clamps to 0..MaxHealth.</item>
    /// <item><c>set Shield to &lt;n&gt;</c> clamps to 0..MaxShield.</item>
    /// <item><c>add -&lt;n&gt; to Health</c> (and <c>deduct n from Health</c>) is the damage
    /// path: <c>my Armor</c> is subtracted first, <c>my Shield</c> absorbs what is left, and
    /// only the remainder comes off <c>my Health</c> — which never goes below 0, so
    /// <c>when my Health becomes 0</c> fires exactly once.</item>
    /// <item><c>add &lt;n&gt; to Health</c> heals, clamped at MaxHealth.</item>
    /// </list>
    /// An actor whose sheet never set any of these keeps the historical behaviour (plain
    /// arithmetic, no clamping, no warnings), so existing .events files are unchanged.
    /// </summary>
    public static class HealthManager
    {
        public const string Health = "Health";
        public const string MaxHealth = "MaxHealth";
        public const string Shield = "Shield";
        public const string MaxShield = "MaxShield";
        public const string Armor = "Armor";

        /// <summary>Where a <c>when message "damage &lt;amount&gt;" arrives</c> pattern drops
        /// the number it captured, so a sheet can react to the size of the hit.</summary>
        public const string DamageTakenVariable = "DamageTaken";

        private static readonly Dictionary<string, string> Aliases = BuildAliases();

        private static Dictionary<string, string> BuildAliases()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Add(map, Health, "Health", "HP", "Hit Points", "HitPoints");
            Add(map, MaxHealth, "MaxHealth", "Max Health", "Max HP", "MaxHP", "Max Hit Points");
            Add(map, Shield, "Shield", "Shields");
            Add(map, MaxShield, "MaxShield", "Max Shield", "Max Shields");
            Add(map, Armor, "Armor", "Armour");
            return map;
        }

        private static void Add(Dictionary<string, string> map, string stat, params string[] names)
        {
            foreach (string name in names)
                map[Normalize(name)] = stat;
        }

        /// <summary>Trims, collapses whitespace and lowercases a written name for lookup.</summary>
        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            var parts = name.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts).ToLowerInvariant();
        }

        /// <summary>True when <paramref name="name"/> spells a health stat; returns its
        /// canonical spelling (<c>Health</c>, <c>MaxHealth</c>, <c>Shield</c>,
        /// <c>MaxShield</c> or <c>Armor</c>).</summary>
        public static bool TryStat(string name, out string stat)
        {
            return Aliases.TryGetValue(Normalize(name), out stat);
        }

        public static bool IsStat(string name)
        {
            return TryStat(name, out _);
        }

        /// <summary>The stat's current value, read from whichever spelling the sheet used
        /// (<c>my Health</c> or <c>my health</c>). Never warns; a missing stat is 0.</summary>
        public static double Read(EventVariables variables, object actor, string stat)
        {
            return TryReadLocal(variables, actor, Candidate(stat), out ValueBox value) ? value.AsNumber() : 0;
        }

        /// <summary>
        /// Assigns a stat, keeping it in range. Health and Shield clamp to their maximum
        /// when the sheet declared one, and never go below 0. Setting a maximum also pulls
        /// the current value down into it. An actor with none of these variables yet keeps
        /// the plain assignment behaviour.
        /// </summary>
        public static void Set(EventVariables variables, object actor, string name, double value)
        {
            if (variables == null || actor == null)
                return;
            if (!TryStat(name, out string stat))
            {
                variables.SetLocal(actor, name, ValueBox.OfNumber(value));
                return;
            }

            // A maximum always applies its ceiling, even the first time it is written.
            if (stat == MaxHealth)
            {
                double ceiling = Math.Max(0, value);
                Write(variables, actor, stat, name, ceiling);
                string healthName = ExistingName(variables, actor, Health, name);
                if (TryReadLocal(variables, actor, healthName, out ValueBox health) && health.AsNumber() > ceiling)
                    variables.SetLocal(actor, healthName, ValueBox.OfNumber(ceiling));
                return;
            }
            if (stat == MaxShield)
            {
                double ceiling = Math.Max(0, value);
                Write(variables, actor, stat, name, ceiling);
                string shieldName = ExistingName(variables, actor, Shield, name);
                if (TryReadLocal(variables, actor, shieldName, out ValueBox shield) && shield.AsNumber() > ceiling)
                    variables.SetLocal(actor, shieldName, ValueBox.OfNumber(ceiling));
                return;
            }

            if (!IsTracked(variables, actor, stat))
            {
                // None of the actor's health variables exist yet: plain assignment, so a
                // sheet that only ever used `my health` behaves exactly as before.
                variables.SetLocal(actor, name, ValueBox.OfNumber(value));
                return;
            }

            double upper = stat == Health ? MaxOf(variables, actor, MaxHealth)
                         : stat == Shield ? MaxOf(variables, actor, MaxShield)
                         : double.MaxValue;
            double clamped = Math.Max(0, upper == double.MaxValue ? value : Math.Min(value, upper));
            Write(variables, actor, stat, name, clamped);
        }

        /// <summary>
        /// Adds to a stat. A negative amount on <c>Health</c> is damage and goes through
        /// <see cref="Damage"/> (armor, then shield, then health); anything else is a plain
        /// add that clamps like <see cref="Set"/>.
        /// </summary>
        public static void Add(EventVariables variables, object actor, string name, double delta)
        {
            if (variables == null || actor == null)
                return;
            if (!TryStat(name, out string stat))
            {
                double legacy = TryReadLocal(variables, actor, name, out ValueBox box) ? box.AsNumber() : 0;
                variables.SetLocal(actor, name, ValueBox.OfNumber(legacy + delta));
                return;
            }
            if (stat == MaxHealth || stat == MaxShield)
            {
                // `add 20 to MaxHealth` raises the ceiling and keeps the current value below it.
                Set(variables, actor, name, Read(variables, actor, stat) + delta);
                return;
            }
            if (!IsTracked(variables, actor, stat))
            {
                double legacy = TryReadLocal(variables, actor, name, out ValueBox box) ? box.AsNumber() : 0;
                variables.SetLocal(actor, name, ValueBox.OfNumber(legacy + delta));
                return;
            }
            if (stat == Health && delta < 0)
            {
                Damage(variables, actor, -delta);
                return;
            }
            Set(variables, actor, name, Read(variables, actor, stat) + delta);
        }

        /// <summary>
        /// Takes a hit: <c>my Armor</c> reduces the amount, <c>my Shield</c> absorbs what is
        /// left, and the remainder comes off <c>my Health</c>, which clamps at 0. A stat that
        /// was never set counts as 0, so an actor with no health variables is left alone by
        /// this path (it is only reached through the tracked branch of <see cref="Add"/>).
        /// </summary>
        public static DamageResult Damage(EventVariables variables, object actor, double amount)
        {
            var result = new DamageResult();
            if (variables == null || actor == null || amount <= 0)
                return result;

            // A unit already at 0 (or below, a legacy negative left by the old arithmetic)
            // is dead: further hits change nothing, so `when my Health becomes 0` cannot fire
            // again from walking further down.
            double before = Read(variables, actor, Health);
            if (IsTracked(variables, actor, Health) && before <= 0)
                return result;

            double armor = Math.Max(0, Read(variables, actor, Armor));
            result.Mitigated = Math.Max(0, amount - armor);

            double shield = Read(variables, actor, Shield);
            double absorbed = Math.Min(shield, result.Mitigated);
            if (absorbed > 0)
            {
                string shieldName = ExistingName(variables, actor, Shield, Shield);
                variables.SetLocal(actor, shieldName, ValueBox.OfNumber(shield - absorbed));
                result.AbsorbedByShield = absorbed;
            }

            double remaining = result.Mitigated - absorbed;
            double after = Math.Max(0, before - remaining);
            if (after != before)
            {
                string healthName = ExistingName(variables, actor, Health, Health);
                variables.SetLocal(actor, healthName, ValueBox.OfNumber(after));
                result.HealthLost = before - after;
                result.Died = before > 0 && after <= 0;
            }
            return result;
        }

        /// <summary>True when the sheet has declared this stat (or its maximum), which is
        /// what opts an actor into clamping. Uses whichever spelling it wrote.</summary>
        private static bool IsTracked(EventVariables variables, object actor, string stat)
        {
            if (variables == null || actor == null)
                return false;
            if (HasAny(variables, actor, Candidate(stat)))
                return true;
            if (stat == Health && HasAny(variables, actor, Candidate(MaxHealth)))
                return true;
            if (stat == Shield && HasAny(variables, actor, Candidate(MaxShield)))
                return true;
            return false;
        }

        private static double MaxOf(EventVariables variables, object actor, string stat)
        {
            return TryReadLocal(variables, actor, Candidate(stat), out ValueBox value)
                ? Math.Max(0, value.AsNumber())
                : double.MaxValue;
        }

        /// <summary>The spellings a canonical stat can have been stored under.</summary>
        private static string[] Candidate(string stat)
        {
            return new[] { stat, stat.ToLowerInvariant() };
        }

        private static bool HasAny(EventVariables variables, object actor, string[] names)
        {
            foreach (string name in names)
                if (variables.HasLocal(actor, name))
                    return true;
            return false;
        }

        private static bool TryReadLocal(EventVariables variables, object actor, string name, out ValueBox value)
        {
            return TryReadLocal(variables, actor, new[] { name }, out value);
        }

        private static bool TryReadLocal(EventVariables variables, object actor, string[] names, out ValueBox value)
        {
            value = new ValueBox();
            if (variables == null || actor == null)
                return false;
            foreach (string name in names)
            {
                if (!variables.HasLocal(actor, name))
                    continue;
                value = variables.GetLocal(actor, name);
                return true;
            }
            return false;
        }

        /// <summary>Writes under the canonical stat name, unless the sheet already used the
        /// written spelling (so <c>my health</c> and <c>my Health</c> never fork).</summary>
        private static void Write(EventVariables variables, object actor, string stat, string written, double value)
        {
            variables.SetLocal(actor, ExistingName(variables, actor, stat, written), ValueBox.OfNumber(value));
        }

        /// <summary>The spelling the sheet already uses for this stat, else
        /// <paramref name="fallback"/>.</summary>
        private static string ExistingName(EventVariables variables, object actor, string stat, string fallback)
        {
            if (variables == null || actor == null)
                return fallback;
            foreach (string name in Candidate(stat))
                if (variables.HasLocal(actor, name))
                    return name;
            return fallback;
        }
    }
}
