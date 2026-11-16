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
    /// Mana is the caster's own <c>my Mana</c>; the cooldown lives in <see cref="SpellClock"/>,
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
        private static readonly Dictionary<string, Dictionary<string, ValueBox>> Authored =
            new Dictionary<string, Dictionary<string, ValueBox>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reads <c>&lt;Spell&gt;&lt;Field&gt;</c> assignments out of a data file, e.g.
        /// <c>when game starts</c> → <c>set FireballMana to 10</c>. Called once per load/reload
        /// by the bridge; pass null to forget the previous table.
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
                    if (!TrySplitField(set.Target.Name, out string spell, out string field))
                        continue;
                    if (!TryLiteral(set.Value, out ValueBox value))
                        continue;
                    if (!Authored.TryGetValue(spell, out var stats))
                    {
                        stats = new Dictionary<string, ValueBox>(StringComparer.OrdinalIgnoreCase);
                        Authored[spell] = stats;
                    }
                    stats[field] = value;
                }
            }
        }

        /// <summary>Splits `FireballDamage` into the spell `Fireball` and the field `Damage`.</summary>
        private static bool TrySplitField(string name, out string spell, out string field)
        {
            spell = "";
            field = "";
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (string candidate in FieldNames)
            {
                if (name.Length <= candidate.Length ||
                    !name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
                    continue;
                spell = name.Substring(0, name.Length - candidate.Length);
                field = candidate;
                return true;
            }
            return false;
        }

        private static bool TryLiteral(ValueNode node, out ValueBox value)
        {
            switch (node)
            {
                case NumberValue number:
                    value = ValueBox.OfNumber(number.Value);
                    return true;
                case TextValue text:
                    value = ValueBox.OfText(text.Text);
                    return true;
                case BooleanValue boolean:
                    value = ValueBox.OfBoolean(boolean.Value);
                    return true;
                default:
                    value = new ValueBox();
                    return false;
            }
        }

        /// <summary>The spell names the project defines right now, sorted, for help messages.</summary>
        public static string KnownNames(EventVariables variables)
        {
            var names = new List<string>(Authored.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", names);
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
            foreach (var pair in Authored)
                if (string.Equals(pair.Key, spellName, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            foreach (string field in FieldNames)
                if (variables != null && variables.HasGlobal(spellName + field))
                    return spellName;
            return null;
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
                if (TryField(variables, canonical, field, out ValueBox value))
                    Apply(result, field, value);
            stats = result;
            return true;
        }

        /// <summary>One field: the live global when it exists, otherwise the data file's value.</summary>
        private static bool TryField(EventVariables variables, string spellName, string field, out ValueBox value)
        {
            string authored = spellName + field;
            if (variables != null && variables.HasGlobal(authored))
            {
                value = ValueBox.Copy(variables.GetGlobal(authored));
                return true;
            }
            if (Authored.TryGetValue(spellName, out var authoredStats) &&
                authoredStats.TryGetValue(field, out ValueBox fromData))
            {
                value = ValueBox.Copy(fromData);
                return true;
            }
            value = new ValueBox();
            return false;
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
    /// Per-actor, per-spell cooldown timers (Task 9 E3). <c>cast</c> starts one when a spell goes
    /// off and refuses the next cast while it is live, exactly like <see cref="WeaponClock"/> does
    /// for <c>reload</c> — but keyed by (actor, spell), so a sheet can hold several spells.
    ///
    /// Kept free of engine types so the timing is unit-testable: the caller supplies "now".
    /// </summary>
    public class SpellClock
    {
        private readonly Dictionary<(object Actor, string Spell), double> ends =
            new Dictionary<(object, string), double>();

        /// <summary>Begin (or restart) a spell's cooldown, ending <paramref name="seconds"/> from now.</summary>
        public void Start(object actor, string spell, double seconds, double now)
        {
            if (actor == null || string.IsNullOrEmpty(spell))
                return;
            if (seconds <= 0)
            {
                ends.Remove((actor, spell));
                return;
            }
            ends[(actor, spell)] = now + seconds;
        }

        /// <summary>True while the spell's cooldown still has time on it.</summary>
        public bool IsCooling(object actor, string spell, double now)
        {
            if (actor == null || string.IsNullOrEmpty(spell))
                return false;
            return ends.TryGetValue((actor, spell), out double end) && now < end;
        }

        /// <summary>Seconds left on a spell's cooldown, or 0 when it is ready.</summary>
        public double Remaining(object actor, string spell, double now)
        {
            if (actor == null || string.IsNullOrEmpty(spell) ||
                !ends.TryGetValue((actor, spell), out double end) || now >= end)
                return 0;
            return end - now;
        }

        /// <summary>Forget one spell for one actor.</summary>
        public void Forget(object actor, string spell)
        {
            if (actor == null || string.IsNullOrEmpty(spell))
                return;
            ends.Remove((actor, spell));
        }

        /// <summary>Forget an actor entirely (it was destroyed) — every spell it had running.</summary>
        public void Clear(object actor)
        {
            if (actor == null)
                return;
            var stale = new List<(object, string)>();
            foreach (var key in ends.Keys)
                if (ReferenceEquals(key.Item1, actor))
                    stale.Add(key);
            foreach (var key in stale)
                ends.Remove(key);
        }
    }
}
