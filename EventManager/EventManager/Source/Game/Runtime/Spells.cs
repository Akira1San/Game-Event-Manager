using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// One spell's data (Task 9 E3): what `cast` spends and what it sends off.
    ///
    /// A spell is a bundle of numbers and names, exactly like a weapon. The stats stay data: a
    /// global variable named <c>&lt;Spell&gt;&lt;Field&gt;</c> — for example <c>FireballDamage</c>,
    /// set in <c>SpellData.events</c> — defines the spell, and a spell that exists only as such
    /// variables is a valid spell. An empty <see cref="Bullet"/> means the spell has no
    /// projectile: <c>cast</c> then sends the message <c>"cast &lt;Spell&gt;"</c> and the events
    /// decide what happens (a heal, a buff, a teleport).
    /// </summary>
    public class SpellStats
    {
        public string Name = "";
        /// <summary>Mana one cast costs (`my Mana`).</summary>
        public double Mana = 10;
        /// <summary>Seconds before the same actor may cast this spell again.</summary>
        public double Cooldown = 1;
        /// <summary>Damage stamped onto the spawned projectile (`my damage`).</summary>
        public double Damage = 10;
        /// <summary>Blast radius in cm for an explosive projectile (`my blast`).</summary>
        public double Blast;
        /// <summary>Damage type the projectile carries (`my Element`): fire, cold, poison, ...</summary>
        public string Element = "";
        /// <summary>Actor template the cast clones. Empty = no projectile, the message branch.</summary>
        public string Bullet = "";
        /// <summary>Projectile speed in cm/s (`my BulletSpeed`).</summary>
        public double Speed = 400;
        /// <summary>Projectile lifetime in seconds (`my Life` / `my TimeLeft`).</summary>
        public double Life = 3;
        /// <summary>Sound played by the caster when the spell goes off.</summary>
        public string Sound = "";
    }

    /// <summary>
    /// Spell definitions (Task 9 E3), shaped exactly like <see cref="WeaponData"/>.
    ///
    /// The action word is <c>cast "&lt;spell&gt;"</c> / <c>cast "&lt;spell&gt;" at &lt;thing&gt;</c>.
    /// Mana is the caster's own <c>my Mana</c>; the cooldown is the spell's own named timer in
    /// <see cref="Cooldowns"/> (Task 37 F5),
    /// per actor and per spell, so two spells on one sheet cool down independently.
    /// </summary>
    public static class SpellData
    {
        /// <summary>The data file's name, like <c>WeaponData.events</c>.</summary>
        public const string FileName = "SpellData";

        /// <summary>Every field a spell has, in a stable order.</summary>
        public static readonly string[] FieldNames =
        {
            "Mana", "Cooldown", "Damage", "Blast", "Element", "Bullet", "Speed", "Life", "Sound",
        };

        /// <summary>
        /// Stats authored in a data file (`SpellData.events`), keyed by spell then field. Read
        /// at load time so a spell is complete before any actor's `me appears` runs.
        /// </summary>
        private static readonly DataTable Table = new DataTable(FileName, FieldNames);

        /// <summary>
        /// Reads <c>&lt;Spell&gt;&lt;Field&gt;</c> assignments out of a data file, e.g.
        /// <c>when game starts</c> → <c>set FireballMana to 10</c>. Called once per load/reload
        /// by the bridge; pass null to forget the previous table.
        /// </summary>
        public static void LoadData(ProgramFile file)
        {
            Table.LoadData(file);
        }

        /// <summary>The spell names the project defines right now, sorted, for help messages.</summary>
        public static string KnownNames(EventVariables variables)
        {
            return Table.KnownNames();
        }

        /// <summary>
        /// The spelling to store as the spell's name, or null when nothing defines that spell.
        /// A data-file entry wins (any casing); otherwise the spell must have at least one
        /// <c>&lt;Spell&gt;&lt;Field&gt;</c> global.
        /// </summary>
        public static string CanonicalFor(EventVariables variables, string spellName)
        {
            if (string.IsNullOrEmpty(spellName))
                return null;
            return Table.CanonicalFor(variables, spellName);
        }

        /// <summary>True when the named spell exists — used to decide whether <c>my
        /// &lt;Spell&gt;Cooldown</c> is a computed readout rather than an ordinary variable.</summary>
        public static bool IsKnown(EventVariables variables, string spellName)
        {
            return CanonicalFor(variables, spellName) != null;
        }

        /// <summary>The spell's full data, or false when nothing defines it.</summary>
        public static bool TryGet(EventVariables variables, string spellName, out SpellStats stats)
        {
            stats = null;
            string canonical = CanonicalFor(variables, spellName);
            if (canonical == null)
                return false;
            var result = new SpellStats { Name = canonical };
            foreach (string field in FieldNames)
                if (Table.TryRead(variables, canonical, field, out ValueBox value))
                    Apply(result, field, value);
            stats = result;
            return true;
        }

        private static void Apply(SpellStats stats, string field, ValueBox value)
        {
            switch (field)
            {
                case "Mana": stats.Mana = value.AsNumber(); break;
                case "Cooldown": stats.Cooldown = value.AsNumber(); break;
                case "Damage": stats.Damage = value.AsNumber(); break;
                case "Blast": stats.Blast = value.AsNumber(); break;
                case "Element": stats.Element = value.AsText(); break;
                case "Bullet": stats.Bullet = value.AsText(); break;
                case "Speed": stats.Speed = value.AsNumber(); break;
                case "Life": stats.Life = value.AsNumber(); break;
                case "Sound": stats.Sound = value.AsText(); break;
            }
        }
    }

    /// <summary>
    /// The feedback half of <c>cast</c> (Task 37 F7). A refused cast is silent in play — a shortage
    /// is normal — but a HUD may want to flash the slot, so the caster's own sheet hears
    /// <c>when cast fails "&lt;spell&gt;"</c>. The bridge calls <see cref="RaiseFailed"/> on each of
    /// its two refusals; the rule lives here rather than in the bridge so the suite can drive it.
    /// </summary>
    public static class CastFeedback
    {
        /// <summary>Raises the caster's `when cast fails "&lt;spell&gt;"` for one refused cast.</summary>
        public static void RaiseFailed(EventRuntime runtime, object caster, string spell)
        {
            if (runtime == null || caster == null || string.IsNullOrEmpty(spell))
                return;
            runtime.RaisePush(caster, new PushSignal
            {
                Kind = TriggerKind.CastFailed,
                Name = spell,
                Other = caster,
            });
        }
    }
}
