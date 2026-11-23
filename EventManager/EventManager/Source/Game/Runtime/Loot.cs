using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One weighted row of a loot table (Task 9 F6).</summary>
    public class LootEntry
    {
        /// <summary>The row's own name, for warnings and tests.</summary>
        public string Name = "";
        /// <summary>Which table the row belongs to (`set TrinketTable to "Loot"`).</summary>
        public string Table = "";
        /// <summary>The item base it drops (`Ring`, `Sword` — a name ItemData knows).</summary>
        public string Base = "";
        /// <summary>A rarity this entry always drops; empty means the word's rarity, else the
        /// table default (`Magic`).</summary>
        public string Rarity = "";
        public double Weight;
    }

    /// <summary>What a loot roll did, so the caller can word the warning and tests can assert it.</summary>
    public enum LootOutcome
    {
        Rolled,
        /// <summary>No row names that table.</summary>
        UnknownTable,
        /// <summary>The table exists but no row has a weight above 0 and an item base.</summary>
        EmptyTable,
        /// <summary>The base and table are fine; the rarity is not defined.</summary>
        UnknownRarity,
        /// <summary>The base in the winning row is not an ItemData base.</summary>
        UnknownBase,
    }

    /// <summary>
    /// Weighted loot tables (Task 9 F6), the data behind <c>spawn item from "&lt;table&gt;" at
    /// &lt;spot&gt;</c> and <c>roll "&lt;rarity&gt;" from "&lt;table&gt;"</c>.
    ///
    /// A table is rows of four fields, authored like every other data file — one global per row and
    /// field — so it loads through <see cref="DataTable"/> rather than a seventh copy of the
    /// reader:
    ///
    /// <code>
    /// set TrinketTable to "Loot", set TrinketBase to "Ring", set TrinketWeight to 50
    /// </code>
    ///
    /// <see cref="TryRoll"/> picks a row by weight, then hands base + rarity + item level to
    /// <see cref="ItemGenerator.Roll"/> — the generator is never reimplemented here, so a drop and
    /// a hand-written <c>spawn Ring … rarity "Rare" level 12</c> roll exactly alike. The item level
    /// comes from the dropper's own <c>my Level</c> (an `enemy type` now writes it), so a dying
    /// enemy passes its level with no number in its sheet.
    /// </summary>
    public static class Loot
    {
        /// <summary>The data file's name, like <c>ItemData.events</c>.</summary>
        public const string FileName = "Loot";

        /// <summary>The rarity a drop uses when neither the word nor its row names one.</summary>
        public const string DefaultRarity = "Magic";

        /// <summary>Every field a row has, in matching order.</summary>
        public static readonly string[] FieldNames = { "Weight", "Table", "Base", "Rarity" };

        private static readonly DataTable Table = new DataTable(FileName, FieldNames);

        /// <summary>
        /// Reads the tables out of <c>Loot.events</c>. Called once per load/reload by the bridge,
        /// before any sheet runs; pass null to forget the file.
        /// </summary>
        public static void LoadData(ProgramFile file)
        {
            Table.LoadData(file);
        }

        /// <summary>Forget the file (the tests and a reload with no file both use this).</summary>
        public static void Clear()
        {
            Table.Clear();
        }

        /// <summary>Every table name the file defines, sorted, for warning messages.</summary>
        public static List<string> TableNames()
        {
            var names = new List<string>();
            foreach (string row in Table.RowNames)
            {
                if (!Table.TryRead(null, row, "Table", out ValueBox value))
                    continue;
                string name = value.AsText().Trim();
                if (name.Length == 0)
                    continue;
                if (!names.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                    names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>
        /// One table's rows, in file order, with the live <c>&lt;Row&gt;&lt;Field&gt;</c> globals
        /// layered over the file. A row whose <c>Table</c> field never matches is ignored, so one
        /// file holds many tables and may also hold unrelated globals.
        /// </summary>
        public static List<LootEntry> Entries(EventVariables variables, string table)
        {
            var entries = new List<LootEntry>();
            if (string.IsNullOrWhiteSpace(table))
                return entries;
            string wanted = table.Trim();
            foreach (string row in Table.RowNames)
            {
                if (!Table.TryRead(null, row, "Table", out ValueBox tableValue))
                    continue;
                if (!string.Equals(tableValue.AsText().Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                var entry = new LootEntry { Name = row, Table = wanted };
                if (Table.TryRead(variables, row, "Base", out ValueBox baseValue))
                    entry.Base = baseValue.AsText().Trim();
                if (Table.TryRead(variables, row, "Rarity", out ValueBox rarityValue))
                    entry.Rarity = rarityValue.AsText().Trim();
                if (Table.TryRead(variables, row, "Weight", out ValueBox weightValue))
                    entry.Weight = weightValue.AsNumber();
                entries.Add(entry);
            }
            return entries;
        }

        /// <summary>
        /// The row one weighted pick lands on, or null when nothing in the list has weight.
        /// <paramref name="fraction"/> is the roll in 0..1 (Rng in play, a fixed number in tests):
        /// each weight is a slice of the total, so 20/30/50 is a 20% / 30% / 50% chance in file
        /// order.
        /// </summary>
        public static LootEntry Pick(IList<LootEntry> entries, double fraction)
        {
            if (entries == null || entries.Count == 0)
                return null;
            double total = 0;
            foreach (LootEntry entry in entries)
                if (entry.Weight > 0)
                    total += entry.Weight;
            if (total <= 0)
                return null;
            if (fraction < 0)
                fraction = 0;
            if (fraction >= 1)
                fraction = 0.999999999;
            double target = fraction * total;
            foreach (LootEntry entry in entries)
            {
                if (entry.Weight <= 0)
                    continue;
                if (target < entry.Weight)
                    return entry;
                target -= entry.Weight;
            }
            for (int i = entries.Count - 1; i >= 0; i--)
                if (entries[i].Weight > 0)
                    return entries[i];
            return null;
        }

        /// <summary>
        /// Rolls one drop from a table: pick a row by weight, choose its rarity (the word's, else
        /// the row's, else <see cref="DefaultRarity"/>), then roll the item through
        /// <see cref="ItemGenerator.Roll"/> at <paramref name="itemLevel"/>. <paramref name="entry"/>
        /// is the row that won even when the roll fails, so the caller can name its base.
        /// </summary>
        public static LootOutcome TryRoll(EventVariables variables, string table, string rarity,
            double itemLevel, out LootEntry entry, out ItemRoll roll)
        {
            entry = null;
            roll = null;
            List<LootEntry> entries = Entries(variables, table);
            if (entries.Count == 0)
                return LootOutcome.UnknownTable;
            entry = Pick(entries, Rng.NextDouble());
            if (entry == null || string.IsNullOrEmpty(entry.Base))
                return LootOutcome.EmptyTable;
            string chosen = !string.IsNullOrWhiteSpace(rarity)
                ? rarity.Trim()
                : !string.IsNullOrWhiteSpace(entry.Rarity) ? entry.Rarity : DefaultRarity;
            roll = ItemGenerator.Roll(variables, entry.Base, chosen, itemLevel);
            if (roll != null)
                return LootOutcome.Rolled;
            return AffixData.RarityFor(variables, chosen) == null
                ? LootOutcome.UnknownRarity
                : LootOutcome.UnknownBase;
        }

        /// <summary>The friendly warning for a failed roll, naming what the project does have.</summary>
        public static string Message(LootOutcome outcome, EventVariables variables, string table,
            string rarity, string baseName = "")
        {
            switch (outcome)
            {
                case LootOutcome.UnknownTable:
                {
                    string known = string.Join(", ", TableNames());
                    return string.IsNullOrEmpty(known)
                        ? $"I don't know the loot table \u201C{table}\u201D \u2014 no Loot.events defines any tables yet."
                        : $"I don't know the loot table \u201C{table}\u201D. This project has: {known}.";
                }
                case LootOutcome.EmptyTable:
                    return $"The loot table \u201C{table}\u201D has no row with a weight above 0 and an item base.";
                case LootOutcome.UnknownRarity:
                    return $"I don't know the rarity \u201C{rarity}\u201D. Try one of: {string.Join(", ", AffixData.RarityNames(variables))}.";
                case LootOutcome.UnknownBase:
                    return $"I don't know the item \u201C{baseName}\u201D. Add its base to ItemData.events \u2014 it has: {ItemData.KnownNames()}.";
                default:
                    return "";
            }
        }

        /// <summary>
        /// The item level a drop rolls at: the dropper's own <c>my Level</c> (written by
        /// <c>enemy type</c>, Task 9 F6), else the <c>ItemLevel</c> global, else 1.
        /// </summary>
        public static double LevelFor(EventVariables variables, object actor)
        {
            if (variables != null && actor != null && variables.HasLocal(actor, "Level"))
                return variables.GetLocal(actor, "Level").AsNumber();
            if (variables != null && variables.HasGlobal("ItemLevel"))
                return variables.GetGlobal("ItemLevel").AsNumber();
            return 1;
        }
    }
}
