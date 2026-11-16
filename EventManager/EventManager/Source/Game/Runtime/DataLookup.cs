using System;
using System.Collections.Generic;
using System.Text;

namespace Game
{
    /// <summary>
    /// The naming convention behind <c>field "Damage" of "Sword" in "item"</c> (Task 29 Part C).
    /// The data files already store every value as one global named after its row and field —
    /// <c>SwordDamage</c> in <c>ItemData.events</c>, <c>PistolAmmo</c> in <c>WeaponData.events</c>,
    /// <c>ZombieHealth</c> in <c>EnemyTypes.events</c> — so a lookup needs no new data format, only
    /// that convention written down once. Matching is forgiving the way asset names are: case does
    /// not matter and spaces inside the row or field are ignored, so
    /// <c>field "fire rate" of "shotgun maniac"</c> reaches <c>ShotgunManiacFireRate</c>.
    /// </summary>
    public static class DataLookup
    {
        /// <summary>The global that holds one field: row + field, spaces dropped (<c>SwordDamage</c>).</summary>
        public static string Name(string row, string field)
            => WithoutSpaces(row) + WithoutSpaces(field);

        public static string WithoutSpaces(string text)
            => string.IsNullOrEmpty(text) ? "" : text.Replace(" ", "").Replace("\t", "");

        /// <summary>
        /// The fields a row has, discovered from the globals that begin with the row's name. Used by
        /// the missing-field warning; order is alphabetical so the message reads the same every run.
        /// </summary>
        public static List<string> FieldsOf(string row, IEnumerable<string> globals)
        {
            var fields = new List<string>();
            string prefix = WithoutSpaces(row);
            if (prefix.Length == 0 || globals == null)
                return fields;
            foreach (string name in globals)
            {
                if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                string field = name.Substring(prefix.Length);
                if (!Contains(fields, field))
                    fields.Add(field);
            }
            fields.Sort(StringComparer.OrdinalIgnoreCase);
            return fields;
        }

        /// <summary>The rows that define a given field, discovered from the globals that end with it.</summary>
        public static List<string> RowsWith(string field, IEnumerable<string> globals)
        {
            var rows = new List<string>();
            string suffix = WithoutSpaces(field);
            if (suffix.Length == 0 || globals == null)
                return rows;
            foreach (string name in globals)
            {
                if (name.Length <= suffix.Length || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                string row = name.Substring(0, name.Length - suffix.Length);
                if (!Contains(rows, row))
                    rows.Add(row);
            }
            rows.Sort(StringComparer.OrdinalIgnoreCase);
            return rows;
        }

        /// <summary>
        /// The warning for a row or field that does not exist. It names the global it looked for and
        /// then tells the author what the data does have, so a typo is fixed from the message instead
        /// of by grepping the data files.
        /// </summary>
        public static string MissingMessage(FieldValue field, string name, IEnumerable<string> globals)
        {
            string table = string.IsNullOrEmpty(field.Table) ? "data" : field.Table;
            var message = new StringBuilder();
            message.Append("I couldn't find field \u201C").Append(field.Field)
                   .Append("\u201D of \u201C").Append(field.Row)
                   .Append("\u201D in the \u201C").Append(table)
                   .Append("\u201D table — nothing has set `").Append(name).Append("`.");

            var fields = FieldsOf(field.Row, globals);
            if (fields.Count > 0)
            {
                message.Append(" \u201C").Append(field.Row).Append("\u201D has ")
                       .Append(Join(fields)).Append('.');
                string nearestField = Nearest(fields, field.Field);
                if (nearestField != null)
                    message.Append(" Did you mean \u201C").Append(nearestField).Append("\u201D?");
                return message.ToString();
            }

            var rows = RowsWith(field.Field, globals);
            if (rows.Count > 0)
            {
                message.Append(" Rows with a \u201C").Append(field.Field).Append("\u201D field are ")
                       .Append(Join(rows)).Append('.');
                string nearestRow = Nearest(rows, field.Row);
                if (nearestRow != null)
                    message.Append(" Did you mean row \u201C").Append(nearestRow).Append("\u201D?");
                return message.ToString();
            }

            message.Append(" No row has a \u201C").Append(field.Field)
                   .Append("\u201D field yet, so a data file has to set it — `set ").Append(name).Append(" to ...`.");
            return message.ToString();
        }

        /// <summary>
        /// The closest name to what the sheet wrote, or null when nothing is close. Two edits is the
        /// same threshold the parser and the file loader use for their "did you mean" hints, so a
        /// typo is answered the same way wherever the author makes it.
        /// </summary>
        private static string Nearest(List<string> names, string wanted)
        {
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (string name in names)
            {
                int distance = WordDistance.Between(wanted, name);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = name;
                }
            }
            return bestDistance <= 2 ? best : null;
        }

        private static string Join(List<string> names)
        {
            const int limit = 8;
            var text = new StringBuilder();
            for (int i = 0; i < names.Count && i < limit; i++)
            {
                if (i > 0)
                    text.Append(i == names.Count - 1 ? " and " : ", ");
                text.Append(names[i]);
            }
            if (names.Count > limit)
                text.Append(" and ").Append(names.Count - limit).Append(" more");
            return text.ToString();
        }

        private static bool Contains(List<string> names, string name)
        {
            foreach (string existing in names)
            {
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
