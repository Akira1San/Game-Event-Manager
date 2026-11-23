using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// Typed enemy definitions (Task 16 Part D).
    ///
    /// A type is a bundle of base stats. <c>enemy type "Zombie"</c> copies them onto the acting
    /// enemy's own variables, so the rest of its sheet reads <c>my Speed</c>,
    /// <c>my SightDistance</c>, <c>my Health</c>, <c>my Weapon</c>, <c>my Hostile</c> and
    /// <c>my ExplosionDeath</c> instead of hard-coded numbers. It also records <c>my Type</c>,
    /// which <c>my type is Zombie</c> reads.
    ///
    /// The stats stay data: a global variable named <c>&lt;Type&gt;&lt;Stat&gt;</c> — for example
    /// <c>ZombieHealth</c>, set in <c>EnemyTypes.events</c> — overrides the built-in TK4 values,
    /// and a type that exists only as such variables is a valid type.
    /// </summary>
    public static class EnemyTypes
    {
        /// <summary>The variable naming the enemy's type (`my Type`).</summary>
        public const string TypeStat = "Type";

        /// <summary>Every stat <c>enemy type</c> writes, in a stable order.</summary>
        public static readonly string[] StatNames =
        {
            "Speed", "SightDistance", "Health", "Weapon", "Hostile", "ExplosionDeath",
        };

        /// <summary>One built-in type: TK4's base stats, before any data file overrides them.</summary>
        private sealed class Definition
        {
            public double Speed;
            public double SightDistance;
            public double Health;
            public string Weapon = "";
            public bool Hostile;
            public bool ExplosionDeath;

            public bool TryGet(string stat, out ValueBox value)
            {
                switch (stat)
                {
                    case "Speed": value = ValueBox.OfNumber(Speed); return true;
                    case "SightDistance": value = ValueBox.OfNumber(SightDistance); return true;
                    case "Health": value = ValueBox.OfNumber(Health); return true;
                    case "Weapon": value = ValueBox.OfText(Weapon); return true;
                    case "Hostile": value = ValueBox.OfBoolean(Hostile); return true;
                    case "ExplosionDeath": value = ValueBox.OfBoolean(ExplosionDeath); return true;
                    default: value = new ValueBox(); return false;
                }
            }
        }

        private static readonly Dictionary<string, Definition> Builtins =
            new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Zombie", new Definition
                    {
                        Speed = 2, SightDistance = 10, Health = 50,
                        Weapon = "Claws", Hostile = true, ExplosionDeath = false,
                    }
                },
                {
                    "Turret", new Definition
                    {
                        Speed = 0, SightDistance = 15, Health = 100,
                        Weapon = "Cannon", Hostile = true, ExplosionDeath = true,
                    }
                },
            };

        /// <summary>
        /// Stats authored in a data file (`EnemyTypes.events`), keyed by type then stat. Read at
        /// load time so a type's numbers exist before any actor's `me appears` runs — a data
        /// sheet attached to the Game actor would otherwise race the enemies' own sheets.
        /// </summary>
        private static readonly DataTable Table = new DataTable("EnemyTypes", StatNames);

        /// <summary>
        /// Reads <c>&lt;Type&gt;&lt;Stat&gt;</c> assignments out of a data file, e.g.
        /// <c>when game starts</c> → <c>set ZombieSightDistance to 200</c>. Called once per
        /// load/reload by the bridge; pass null to forget the previous table.
        /// </summary>
        public static void LoadData(ProgramFile file)
        {
            Table.LoadData(file);
        }

        /// <summary>
        /// Every type name that exists right now — the built-in fallbacks plus everything the
        /// data file defines — sorted, for warning messages. Adding a type to
        /// <c>EnemyTypes.events</c> therefore names it in the "I don't know that type" help too.
        /// </summary>
        public static string KnownTypeNames()
        {
            var names = AllTypeNames();
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", names);
        }

        /// <summary>Every type name that exists right now, unsorted.</summary>
        public static List<string> AllTypeNames()
        {
            return Table.MergeWith(Builtins.Keys);
        }

        /// <summary>
        /// The type an actor name refers to — the exact name, or the longest known type name the
        /// name starts with, ending on a word boundary. That is what makes `enemy type my name`
        /// work for clones: a spawned "Pistolboy 2" or "Pistolboy(Clone)" is a Pistolboy, while
        /// "Pistolboys" and "Zombies" are not. Null when nothing matches.
        /// </summary>
        public static string TypeForActorName(EventVariables variables, string actorName)
        {
            if (string.IsNullOrWhiteSpace(actorName))
                return null;
            string exact = CanonicalFor(variables, actorName.Trim());
            if (exact != null)
                return exact;
            string trimmed = actorName.Trim();
            string best = null;
            foreach (string name in AllTypeNames())
            {
                if (trimmed.Length <= name.Length ||
                    string.Compare(trimmed, 0, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
                    continue;
                char next = trimmed[name.Length];
                if (char.IsLetterOrDigit(next))
                    continue; // "Pistolboys" is not a Pistolboy
                if (best == null || name.Length > best.Length)
                    best = name;
            }
            return best;
        }

        /// <summary>
        /// The spelling to store in <c>my Type</c>, or null when nothing defines that type.
        /// A built-in name wins (any casing); otherwise the type must have at least one
        /// <c>&lt;Type&gt;&lt;Stat&gt;</c> global.
        /// </summary>
        public static string CanonicalFor(EventVariables variables, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;
            foreach (var pair in Builtins)
                if (string.Equals(pair.Key, typeName, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            return Table.CanonicalFor(variables, typeName);
        }

        /// <summary>
        /// Applies the named type to the acting enemy, writing each stat as one of its own
        /// variables. Returns false (and writes nothing) when the type is unknown.
        /// </summary>
        public static bool Apply(EventContext context, string typeName)
        {
            if (context == null)
                return false;
            string canonical = CanonicalFor(context.Variables, typeName);
            if (canonical == null)
                return false;
            foreach (string stat in StatNames)
            {
                if (TryStat(context.Variables, canonical, stat, out ValueBox value))
                    context.WriteMy(stat, value);
            }
            context.WriteMy(TypeStat, ValueBox.OfText(canonical));
            return true;
        }

        /// <summary>One stat: the data file's global when it exists, otherwise the built-in value.</summary>
        private static bool TryStat(EventVariables variables, string typeName, string stat, out ValueBox value)
        {
            if (Table.TryRead(variables, typeName, stat, out value))
                return true;
            if (Builtins.TryGetValue(typeName, out Definition definition))
                return definition.TryGet(stat, out value);
            value = new ValueBox();
            return false;
        }
    }
}
