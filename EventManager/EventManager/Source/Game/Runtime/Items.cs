using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One stat an item grants, e.g. FireResist 12.</summary>
    public class ItemStat
    {
        public string Stat = "";
        public double Value;

        public ItemStat() { }

        public ItemStat(string stat, double value)
        {
            Stat = stat;
            Value = value;
        }
    }

    /// <summary>
    /// The stats one generated item instance rolled (Task 9 E7): its base stats plus one entry per
    /// affix. <see cref="DisplayName"/> is the D2-style name (“Fiery Ring of the Fox”).
    /// </summary>
    public class ItemRoll
    {
        public string Base = "";
        public string Rarity = "Common";
        public int ItemLevel = 1;
        public string DisplayName = "";
        /// <summary>Everything the item grants, base stats included — what equip applies.</summary>
        public List<ItemStat> Stats = new List<ItemStat>();
        /// <summary>The affixes that rolled (their data names), for tests and tooltips.</summary>
        public List<string> AffixNames = new List<string>();
    }

    /// <summary>One item base (`Ring`, `Amulet`, …) from `ItemData.events`.</summary>
    public class ItemBase
    {
        public string Name = "";
        /// <summary>Which equip slot it wants (`my RingSlot`, Task 9 E5 — data only here).</summary>
        public string Slot = "";
        public double Damage;
        public double Armor;
        /// <summary>The item level it is meant for; informational for a base.</summary>
        public double ItemLevel = 1;
    }

    /// <summary>
    /// The stats an affix may write. Generated stats are ordinary `my` variables, so this list is
    /// a spelling guard: an affix whose stat is not here is skipped with a warning instead of
    /// quietly creating a variable nothing ever reads. Add a name here before using it in data.
    /// </summary>
    public static class ItemStats
    {
        public static readonly string[] Names =
        {
            "damage", "armor",
            "FireResist", "ColdResist", "PoisonResist",
            "Strength", "Dexterity", "Vitality", "Energy",
            "MaxHealth", "MaxMana",
        };

        /// <summary>The canonical spelling of a stat, or null when it is not a known stat.</summary>
        public static string Canonical(string stat)
        {
            if (string.IsNullOrEmpty(stat))
                return null;
            foreach (string name in Names)
                if (string.Equals(name, stat, StringComparison.OrdinalIgnoreCase))
                    return name;
            return null;
        }
    }

    /// <summary>
    /// Item bases (Task 9 E7), shaped like <see cref="WeaponData"/>: a global named
    /// <c>&lt;Base&gt;&lt;Field&gt;</c> — <c>RingSlot</c>, <c>RingArmor</c> — in
    /// <c>ItemData.events</c> defines a base. No built-ins: an item base is authored, so nothing
    /// generates stats for a name nobody wrote.
    /// </summary>
    public static class ItemData
    {
        public const string FileName = "ItemData";

        public static readonly string[] FieldNames = { "Slot", "Damage", "Armor", "ItemLevel" };

        private static readonly Dictionary<string, Dictionary<string, ValueBox>> Authored =
            new Dictionary<string, Dictionary<string, ValueBox>>(StringComparer.OrdinalIgnoreCase);

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
                    if (!TrySplitField(set.Target.Name, out string item, out string field))
                        continue;
                    if (!TryLiteral(set.Value, out ValueBox value))
                        continue;
                    if (!Authored.TryGetValue(item, out var stats))
                    {
                        stats = new Dictionary<string, ValueBox>(StringComparer.OrdinalIgnoreCase);
                        Authored[item] = stats;
                    }
                    stats[field] = value;
                }
            }
        }

        private static bool TrySplitField(string name, out string item, out string field)
        {
            item = "";
            field = "";
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (string candidate in FieldNames)
            {
                if (name.Length <= candidate.Length ||
                    !name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
                    continue;
                item = name.Substring(0, name.Length - candidate.Length);
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

        public static List<string> AllNames()
        {
            var names = new List<string>(Authored.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static string KnownNames()
        {
            return string.Join(", ", AllNames());
        }

        public static string CanonicalFor(EventVariables variables, string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;
            foreach (var pair in Authored)
                if (string.Equals(pair.Key, itemName, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            foreach (string field in FieldNames)
                if (variables != null && variables.HasGlobal(itemName + field))
                    return itemName;
            return null;
        }

        public static bool TryGet(EventVariables variables, string itemName, out ItemBase item)
        {
            item = null;
            string canonical = CanonicalFor(variables, itemName);
            if (canonical == null)
                return false;
            var result = new ItemBase { Name = canonical };
            foreach (string field in FieldNames)
                if (TryField(variables, canonical, field, out ValueBox value))
                    Apply(result, field, value);
            item = result;
            return true;
        }

        private static bool TryField(EventVariables variables, string itemName, string field, out ValueBox value)
        {
            string authored = itemName + field;
            if (variables != null && variables.HasGlobal(authored))
            {
                value = ValueBox.Copy(variables.GetGlobal(authored));
                return true;
            }
            if (Authored.TryGetValue(itemName, out var authoredStats) &&
                authoredStats.TryGetValue(field, out ValueBox fromData))
            {
                value = ValueBox.Copy(fromData);
                return true;
            }
            value = new ValueBox();
            return false;
        }

        private static void Apply(ItemBase item, string field, ValueBox value)
        {
            switch (field)
            {
                case "Slot": item.Slot = value.AsText(); break;
                case "Damage": item.Damage = value.AsNumber(); break;
                case "Armor": item.Armor = value.AsNumber(); break;
                case "ItemLevel": item.ItemLevel = value.AsNumber(); break;
            }
        }
    }

    /// <summary>One affix definition (`Fiery` → +FireResist) from `AffixData.events`.</summary>
    public class AffixDef
    {
        public string Name = "";
        public string Stat = "";
        public double Min = 1;
        public double Max = 1;
        /// <summary>The lowest item level this affix can roll on.</summary>
        public double ItemLevel = 1;
        /// <summary>The adjective used in the item's name — “Fiery Ring”.</summary>
        public string Word = "";
        /// <summary>Render the word as a suffix (“Ring of the Fox”) instead of a prefix.</summary>
        public bool Suffix;
    }

    /// <summary>How many affixes one rarity rolls (`Rare` → 1..3, per Task 9 E7).</summary>
    public class RarityDef
    {
        public string Name = "";
        public int MinAffixes;
        public int MaxAffixes;
    }

    /// <summary>
    /// The affix pool and the rarity table (Task 9 E7), one data file: `AffixData.events`.
    ///
    /// Rows are ordinary globals:
    /// <c>set FieryStat to "FireResist"</c>, <c>set FieryMin to 5</c>, <c>set FieryMax to 15</c>,
    /// <c>set FieryItemLevel to 1</c>, <c>set FieryWord to "Fiery"</c> — and
    /// <c>set RareMinAffixes to 1</c> / <c>set RareMaxAffixes to 3</c> for a rarity. Built-in
    /// rarity counts: Common 0, Magic 1–2, Rare 1–3, Unique 3–4.
    /// </summary>
    public static class AffixData
    {
        public const string FileName = "AffixData";

        // Longest first: `RareMinAffixes` must not be read as a field called `Min`.
        public static readonly string[] FieldNames =
        {
            "MinAffixes", "MaxAffixes", "ItemLevel", "Suffix", "Stat", "Word", "Min", "Max",
        };

        private static readonly Dictionary<string, Dictionary<string, ValueBox>> Authored =
            new Dictionary<string, Dictionary<string, ValueBox>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, RarityDef> BuiltinRarities =
            new Dictionary<string, RarityDef>(StringComparer.OrdinalIgnoreCase)
            {
                { "Common", new RarityDef { Name = "Common", MinAffixes = 0, MaxAffixes = 0 } },
                { "Magic", new RarityDef { Name = "Magic", MinAffixes = 1, MaxAffixes = 2 } },
                { "Rare", new RarityDef { Name = "Rare", MinAffixes = 1, MaxAffixes = 3 } },
                { "Unique", new RarityDef { Name = "Unique", MinAffixes = 3, MaxAffixes = 4 } },
            };

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
                    if (!TrySplitField(set.Target.Name, out string name, out string field))
                        continue;
                    if (!TryLiteral(set.Value, out ValueBox value))
                        continue;
                    if (!Authored.TryGetValue(name, out var stats))
                    {
                        stats = new Dictionary<string, ValueBox>(StringComparer.OrdinalIgnoreCase);
                        Authored[name] = stats;
                    }
                    stats[field] = value;
                }
            }
        }

        private static bool TrySplitField(string name, out string owner, out string field)
        {
            owner = "";
            field = "";
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (string candidate in FieldNames)
            {
                if (name.Length <= candidate.Length ||
                    !name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
                    continue;
                owner = name.Substring(0, name.Length - candidate.Length);
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

        /// <summary>Every affix the data file defines (rows with a <c>Stat</c>), in name order.</summary>
        public static List<AffixDef> All(EventVariables variables)
        {
            var found = new List<AffixDef>();
            foreach (var pair in Authored)
            {
                AffixDef def = DefOf(variables, pair.Key);
                if (def != null)
                    found.Add(def);
            }
            found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return found;
        }

        /// <summary>One affix by name, or null when nothing defines it / it has no stat.</summary>
        public static AffixDef DefOf(EventVariables variables, string affixName)
        {
            if (!Authored.TryGetValue(affixName, out var stats))
                return null;
            if (!TryRead(variables, affixName, "Stat", out ValueBox stat))
                return null;
            var def = new AffixDef { Name = CanonicalName(affixName), Stat = stat.AsText() };
            if (TryRead(variables, affixName, "Min", out ValueBox min))
                def.Min = min.AsNumber();
            if (TryRead(variables, affixName, "Max", out ValueBox max))
                def.Max = max.AsNumber();
            if (def.Max < def.Min)
                def.Max = def.Min;
            if (TryRead(variables, affixName, "ItemLevel", out ValueBox level))
                def.ItemLevel = level.AsNumber();
            if (TryRead(variables, affixName, "Word", out ValueBox word))
                def.Word = word.AsText();
            if (string.IsNullOrEmpty(def.Word))
                def.Word = def.Name;
            if (TryRead(variables, affixName, "Suffix", out ValueBox suffix))
                def.Suffix = suffix.AsBoolean();
            return def;
        }

        /// <summary>The rarity row for a name (data file first, then the built-in counts).</summary>
        public static RarityDef RarityFor(EventVariables variables, string rarityName)
        {
            if (string.IsNullOrEmpty(rarityName))
                return null;
            RarityDef result = null;
            if (Authored.TryGetValue(rarityName, out var stats) &&
                (stats.ContainsKey("MinAffixes") || stats.ContainsKey("MaxAffixes")))
            {
                result = new RarityDef { Name = CanonicalName(rarityName) };
                if (TryRead(variables, rarityName, "MinAffixes", out ValueBox min))
                    result.MinAffixes = (int)Math.Round(min.AsNumber());
                if (TryRead(variables, rarityName, "MaxAffixes", out ValueBox max))
                    result.MaxAffixes = (int)Math.Round(max.AsNumber());
            }
            else
            {
                foreach (var pair in BuiltinRarities)
                    if (string.Equals(pair.Key, rarityName, StringComparison.OrdinalIgnoreCase))
                        result = new RarityDef
                        {
                            Name = pair.Value.Name,
                            MinAffixes = pair.Value.MinAffixes,
                            MaxAffixes = pair.Value.MaxAffixes,
                        };
            }
            if (result == null)
                return null;
            if (result.MaxAffixes < result.MinAffixes)
                result.MaxAffixes = result.MinAffixes;
            if (result.MinAffixes < 0)
                result.MinAffixes = 0;
            return result;
        }

        /// <summary>Every rarity name that exists right now, sorted.</summary>
        public static List<string> RarityNames(EventVariables variables)
        {
            var names = new List<string>(BuiltinRarities.Keys);
            foreach (var pair in Authored)
                if (pair.Value.ContainsKey("MinAffixes") || pair.Value.ContainsKey("MaxAffixes"))
                    if (!names.Exists(n => string.Equals(n, pair.Key, StringComparison.OrdinalIgnoreCase)))
                        names.Add(pair.Key);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Every affix name, sorted, for help messages.</summary>
        public static string KnownNames(EventVariables variables)
        {
            var names = new List<string>();
            foreach (var def in All(variables))
                names.Add(def.Name);
            return string.Join(", ", names);
        }

        private static bool TryRead(EventVariables variables, string name, string field, out ValueBox value)
        {
            string authored = name + field;
            if (variables != null && variables.HasGlobal(authored))
            {
                value = ValueBox.Copy(variables.GetGlobal(authored));
                return true;
            }
            if (Authored.TryGetValue(name, out var stats) && stats.TryGetValue(field, out ValueBox fromData))
            {
                value = ValueBox.Copy(fromData);
                return true;
            }
            value = new ValueBox();
            return false;
        }

        private static string CanonicalName(string name)
        {
            foreach (var pair in Authored)
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            return name;
        }
    }

    /// <summary>
    /// The affix generator (Task 9 E7). Compose an item's stats from a base plus N random affixes,
    /// driven by the item level and the rarity: <c>Rare</c> rolls 1–3 affixes, <c>Magic</c> 1–2,
    /// <c>Common</c> none. Affix values scale a little with the item level above the affix's own
    /// minimum level, so a level-20 ring is worth more than a level-1 one.
    ///
    /// Rolls use <see cref="Rng"/>, so a seed makes a whole loot table replayable in tests.
    /// Pure: no engine types, no globals of its own.
    /// </summary>
    public static class ItemGenerator
    {
        /// <summary>Extra value per item level above an affix's minimum level (5% each).</summary>
        public const double LevelGrowth = 0.05;

        /// <summary>
        /// Roll one item. Returns null when the base or the rarity is unknown, so the caller can
        /// warn with the list it knows. Base damage/armor and the affix values all land in
        /// <see cref="ItemRoll.Stats"/>, which is what equip applies.
        /// </summary>
        public static ItemRoll Roll(EventVariables variables, string baseName, string rarityName, double itemLevel)
        {
            if (!ItemData.TryGet(variables, baseName, out ItemBase item))
                return null;
            RarityDef rarity = AffixData.RarityFor(variables, rarityName);
            if (rarity == null)
                return null;
            int level = (int)Math.Round(itemLevel);
            if (level < 1)
                level = 1;

            var roll = new ItemRoll
            {
                Base = item.Name,
                Rarity = rarity.Name,
                ItemLevel = level,
            };
            if (item.Damage > 0)
                roll.Stats.Add(new ItemStat("damage", item.Damage));
            if (item.Armor > 0)
                roll.Stats.Add(new ItemStat("armor", item.Armor));

            var pool = new List<AffixDef>();
            foreach (AffixDef def in AffixData.All(variables))
            {
                if (def.ItemLevel > level)
                    continue;
                if (ItemStats.Canonical(def.Stat) == null)
                    continue;
                pool.Add(def);
            }

            int count = AffixCount(rarity, pool.Count, Rng.NextDouble());
            var picked = new List<AffixDef>();
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int index = (int)(Rng.NextDouble() * pool.Count);
                if (index >= pool.Count)
                    index = pool.Count - 1;
                AffixDef def = pool[index];
                pool.RemoveAt(index);
                picked.Add(def);
                double value = RollValue(def, level, Rng.NextDouble());
                roll.Stats.Add(new ItemStat(ItemStats.Canonical(def.Stat) ?? def.Stat, value));
                roll.AffixNames.Add(def.Name);
            }
            roll.DisplayName = ComposeName(item, picked);
            return roll;
        }

        /// <summary>
        /// How many affixes a rarity rolls, given how many are eligible. The count is clamped to
        /// the pool so a small level-gated pool never asks for more affixes than it has.
        /// <paramref name="fraction"/> is the roll in 0..1 (Rng in play, a fixed number in tests).
        /// </summary>
        public static int AffixCount(RarityDef rarity, int poolSize, double fraction)
        {
            if (rarity == null || poolSize <= 0)
                return 0;
            int min = rarity.MinAffixes;
            int max = rarity.MaxAffixes;
            if (max < min)
                max = min;
            if (min > poolSize)
                min = poolSize;
            if (max > poolSize)
                max = poolSize;
            if (max <= min)
                return min;
            int count = min + (int)Math.Floor(fraction * (max - min + 1));
            if (count > max)
                count = max;
            return count;
        }

        /// <summary>
        /// One affix's value: a roll between its min and max, grown by
        /// <see cref="LevelGrowth"/> for each item level above the affix's minimum level.
        /// Whole-number affixes stay whole so a HUD never shows 12.000000001.
        /// </summary>
        public static double RollValue(AffixDef def, double itemLevel, double fraction)
        {
            if (def == null)
                return 0;
            if (fraction < 0)
                fraction = 0;
            if (fraction > 1)
                fraction = 1;
            double baseValue = def.Min + (def.Max - def.Min) * fraction;
            double levelsAbove = itemLevel - def.ItemLevel;
            if (levelsAbove < 0)
                levelsAbove = 0;
            double value = baseValue * (1 + LevelGrowth * levelsAbove);
            bool whole = Math.Abs(def.Min - Math.Round(def.Min)) < 1e-9 &&
                         Math.Abs(def.Max - Math.Round(def.Max)) < 1e-9;
            return whole ? Math.Round(value) : Math.Round(value, 1);
        }

        /// <summary>“Fiery Ring of the Fox” — prefixes in order, suffixes last.</summary>
        public static string ComposeName(ItemBase item, IList<AffixDef> affixes)
        {
            string name = item == null ? "" : item.Name;
            var prefixes = new List<string>();
            var suffixes = new List<string>();
            if (affixes != null)
            {
                foreach (AffixDef def in affixes)
                {
                    string word = string.IsNullOrEmpty(def.Word) ? def.Name : def.Word;
                    if (def.Suffix)
                        suffixes.Add("of the " + word);
                    else
                        prefixes.Add(word);
                }
            }
            string composed = string.Join(" ", prefixes).Trim();
            if (composed.Length > 0)
                composed += " ";
            composed += name;
            if (suffixes.Count > 0)
                composed += " " + string.Join(" ", suffixes);
            return composed.Trim();
        }
    }

    /// <summary>
    /// Per-instance generated items (Task 9 E7): which actor carries which roll, and what an equip
    /// actually added to a wearer so unequipping can take back exactly that — recorded, never
    /// recomputed, so no drift.
    ///
    /// Hand-authored items keep their own <c>when me gets equipped</c> sheets; a generated item
    /// additionally has a roll here, and both apply.
    /// </summary>
    public class ItemAffixes
    {
        private readonly Dictionary<object, ItemRoll> rolls = new Dictionary<object, ItemRoll>();
        private readonly Dictionary<(object Wearer, string Name), List<ItemStat>> applied =
            new Dictionary<(object, string), List<ItemStat>>();

        /// <summary>Remember that this item actor carries a rolled item.</summary>
        public void Stamp(object item, ItemRoll roll)
        {
            if (item == null || roll == null)
                return;
            rolls[item] = roll;
        }

        public bool HasRoll(object item)
        {
            return item != null && rolls.ContainsKey(item);
        }

        public ItemRoll RollFor(object item)
        {
            return item != null && rolls.TryGetValue(item, out ItemRoll roll) ? roll : null;
        }

        /// <summary>
        /// Apply the item's rolled stats to the wearer. Returns false (and changes nothing) when
        /// that item carries no roll — a hand-authored item's sheet does its own work.
        /// </summary>
        public bool Apply(EventVariables variables, object wearer, string name, object item)
        {
            if (variables == null || wearer == null || item == null)
                return false;
            if (!rolls.TryGetValue(item, out ItemRoll roll))
                return false;
            var appliedStats = new List<ItemStat>();
            foreach (ItemStat stat in roll.Stats)
            {
                if (string.IsNullOrEmpty(stat.Stat) || stat.Value == 0)
                    continue;
                double current = variables.GetLocal(wearer, stat.Stat).AsNumber();
                variables.SetLocal(wearer, stat.Stat, ValueBox.OfNumber(current + stat.Value));
                appliedStats.Add(new ItemStat(stat.Stat, stat.Value));
            }
            applied[(wearer, name ?? "")] = appliedStats;
            return true;
        }

        /// <summary>Take back exactly what <see cref="Apply"/> added for this worn item.</summary>
        public bool Remove(EventVariables variables, object wearer, string name)
        {
            if (variables == null || wearer == null)
                return false;
            if (!applied.TryGetValue((wearer, name ?? ""), out List<ItemStat> appliedStats))
                return false;
            foreach (ItemStat stat in appliedStats)
            {
                double current = variables.GetLocal(wearer, stat.Stat).AsNumber();
                variables.SetLocal(wearer, stat.Stat, ValueBox.OfNumber(current - stat.Value));
            }
            applied.Remove((wearer, name ?? ""));
            return true;
        }

        /// <summary>Forget a destroyed item actor (its roll and any wearer record keyed by it).</summary>
        public void Forget(object item)
        {
            if (item == null)
                return;
            rolls.Remove(item);
        }

        /// <summary>Forget everything a destroyed wearer was wearing.</summary>
        public void ForgetWearer(object wearer)
        {
            if (wearer == null)
                return;
            var stale = new List<(object, string)>();
            foreach (var key in applied.Keys)
                if (ReferenceEquals(key.Item1, wearer))
                    stale.Add(key);
            foreach (var key in stale)
                applied.Remove(key);
        }
    }
}
