using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// The one <c>&lt;Row&gt;&lt;Field&gt;</c> table loader (Task 9 F10).
    ///
    /// Every data table in the project has the same shape: rows of fields, authored as one global
    /// per row and field — <c>PistolDamage</c> in <c>WeaponData.events</c>, <c>ZombieHealth</c> in
    /// <c>EnemyTypes.events</c>, <c>SwordArmor</c> in <c>ItemData.events</c>,
    /// <c>RareMinAffixes</c> in <c>AffixData.events</c>. Loading one is therefore the same walk
    /// every time: read a data file's literal <c>set</c> lines, split each name into the row and the
    /// field it ends with, and keep the value. Two rules sit on top of that, and both belong here
    /// rather than in six copies:
    ///
    /// <list type="bullet">
    /// <item>The live global of the same name always wins over the file, so a sheet can change a
    /// number at runtime; a row that exists only as live globals is a valid row, which is what
    /// makes a data-only weapon or enemy type work.</item>
    /// <item>A field list is matched longest-first by the order the author passes it, so
    /// <c>RareMinAffixes</c> is not read as a field called <c>Min</c> by a table that also has
    /// <c>MinAffixes</c>.</item>
    /// </list>
    ///
    /// A table class is now a field list, its built-in defaults and the field-to-stat switch; the
    /// next table (loot, buffs, abilities) is those three and nothing else.
    /// </summary>
    public class DataTable
    {
        private readonly Dictionary<string, Dictionary<string, ValueBox>> rows =
            new Dictionary<string, Dictionary<string, ValueBox>>(StringComparer.OrdinalIgnoreCase);

        /// <param name="fileName">The data file's name, e.g. <c>WeaponData</c> for
        /// <c>WeaponData.events</c>; kept for error messages and for a table that loads itself.</param>
        /// <param name="fields">Every field a row has, in matching order (longest first where one
        /// field name is the suffix of another).</param>
        public DataTable(string fileName, params string[] fields)
        {
            FileName = fileName ?? "";
            Fields = fields ?? new string[0];
        }

        /// <summary>The data file's name, like <c>WeaponData</c>.</summary>
        public string FileName { get; }

        /// <summary>Every field, in the order <see cref="TrySplit"/> matches them.</summary>
        public string[] Fields { get; }

        /// <summary>How many rows the loaded file defines.</summary>
        public int RowCount => rows.Count;

        /// <summary>Every row the loaded file defines, in file order.</summary>
        public IEnumerable<string> RowNames => rows.Keys;

        /// <summary>
        /// Reads <c>&lt;Row&gt;&lt;Field&gt;</c> assignments out of a data file, e.g.
        /// <c>when game starts</c> → <c>set PistolReload to 0.6</c>. Called once per load/reload by
        /// the bridge; pass <c>null</c> to forget the previous table.
        /// </summary>
        public void LoadData(ProgramFile file)
        {
            rows.Clear();
            DataFile.ForEachLiteral(file, (name, value) =>
            {
                if (!TrySplit(name, out string row, out string field))
                    return;
                if (!rows.TryGetValue(row, out var fields))
                {
                    fields = new Dictionary<string, ValueBox>(StringComparer.OrdinalIgnoreCase);
                    rows[row] = fields;
                }
                fields[field] = value;
            });
        }

        /// <summary>Forget the loaded table (the tests and a reload with no file both use this).</summary>
        public void Clear()
        {
            rows.Clear();
        }

        /// <summary>Splits <c>PistolReload</c> into the row <c>Pistol</c> and the field <c>Reload</c>.</summary>
        public bool TrySplit(string name, out string row, out string field)
        {
            row = "";
            field = "";
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (string candidate in Fields)
            {
                if (name.Length <= candidate.Length ||
                    !name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
                    continue;
                row = name.Substring(0, name.Length - candidate.Length);
                field = candidate;
                return true;
            }
            return false;
        }

        /// <summary>True when the loaded file defines this row (any casing).</summary>
        public bool HasRow(string row)
        {
            return !string.IsNullOrEmpty(row) && rows.ContainsKey(row);
        }

        /// <summary>True when the loaded file sets this one field of this row.</summary>
        public bool HasField(string row, string field)
        {
            return !string.IsNullOrEmpty(row) && rows.TryGetValue(row, out var fields) &&
                   fields.ContainsKey(field ?? "");
        }

        /// <summary>
        /// The spelling the data file uses for a row, or null when the file never defines it. A
        /// built-in table asks this first so a built-in name wins any casing.
        /// </summary>
        public string CanonicalRow(string row)
        {
            if (string.IsNullOrEmpty(row))
                return null;
            foreach (var pair in rows)
                if (string.Equals(pair.Key, row, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            return null;
        }

        /// <summary>
        /// The spelling to store as a row's name, or null when nothing defines the row: the data
        /// file's own spelling, otherwise the name as written when at least one live
        /// <c>&lt;Row&gt;&lt;Field&gt;</c> global exists.
        /// </summary>
        public string CanonicalFor(EventVariables variables, string row)
        {
            string canonical = CanonicalRow(row);
            if (canonical != null)
                return canonical;
            if (string.IsNullOrEmpty(row))
                return null;
            foreach (string field in Fields)
                if (variables != null && variables.HasGlobal(row + field))
                    return row;
            return null;
        }

        /// <summary>
        /// One field: the live global <c>&lt;Row&gt;&lt;Field&gt;</c> when it exists, otherwise the
        /// data file's value. False when neither does, so the caller keeps its built-in default.
        /// </summary>
        public bool TryRead(EventVariables variables, string row, string field, out ValueBox value)
        {
            string name = (row ?? "") + (field ?? "");
            if (variables != null && variables.HasGlobal(name))
            {
                value = ValueBox.Copy(variables.GetGlobal(name));
                return true;
            }
            if (!string.IsNullOrEmpty(row) && rows.TryGetValue(row, out var fields) &&
                fields.TryGetValue(field ?? "", out ValueBox stored))
            {
                value = ValueBox.Copy(stored);
                return true;
            }
            value = new ValueBox();
            return false;
        }

        /// <summary>
        /// The names a help message should list: <paramref name="first"/> (a built-in table's
        /// names) followed by every loaded row, with a name that appears in both kept once.
        /// </summary>
        public List<string> MergeWith(IEnumerable<string> first)
        {
            var names = new List<string>();
            if (first != null)
                foreach (string name in first)
                    AddName(names, name);
            foreach (string name in rows.Keys)
                AddName(names, name);
            return names;
        }

        /// <summary>Every loaded row, sorted, for warning messages.</summary>
        public List<string> SortedRowNames()
        {
            var names = new List<string>(rows.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Every loaded row as one sorted, comma-separated line, for warning messages.</summary>
        public string KnownNames()
        {
            return string.Join(", ", SortedRowNames());
        }

        private static void AddName(List<string> names, string name)
        {
            if (string.IsNullOrEmpty(name))
                return;
            foreach (string existing in names)
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                    return;
            names.Add(name);
        }
    }

    /// <summary>
    /// The literal half of a data file (Task 9 F10): a <c>.events</c> file used as data holds
    /// <c>set &lt;name&gt; to &lt;literal&gt;</c> lines and nothing else that matters, and every
    /// table reads them through this walk instead of repeating it. Nothing in the file runs — the
    /// literals are read at load, so a scene that never attaches the sheet still gets the numbers.
    /// </summary>
    public static class DataFile
    {
        /// <summary>
        /// Visits every <c>set &lt;name&gt; to &lt;literal&gt;</c> line in the file, in file order.
        /// A compound assignment (<c>add 5 to …</c>), a variable target or a computed value is not
        /// a data row and is skipped, exactly as every table did before.
        /// </summary>
        public static void ForEachLiteral(ProgramFile file, Action<string, ValueBox> visit)
        {
            if (file == null || visit == null)
                return;
            foreach (EventNode ev in file.Events)
            {
                foreach (BodyLineNode line in ev.Body)
                {
                    if (!(line.Statement is SetNode set) || set.Op != '\0')
                        continue;
                    if (!TryLiteral(set.Value, out ValueBox value))
                        continue;
                    visit(set.Target.Name, value);
                }
            }
        }

        /// <summary>The literal a <c>set</c> line writes, or false for a computed value.</summary>
        public static bool TryLiteral(ValueNode node, out ValueBox value)
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
    }
}
