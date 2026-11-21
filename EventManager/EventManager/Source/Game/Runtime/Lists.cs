using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One filled list (Task 34 Part A): its rows, oldest first.</summary>
    public class ListModel
    {
        /// <summary>The list as written in the events — the scene's row actors are named after it.</summary>
        public string Name = "";

        /// <summary>The values filling the rows. A blank entry is a row that exists but shows nothing.</summary>
        public readonly List<string> Rows = new List<string>();
    }

    /// <summary>
    /// The data behind <c>clear list</c>, <c>add row to list</c>, <c>set row … of list</c> and
    /// <c>fill list … from my bag</c> (Task 34 Part A). It is runtime state, not a control: the
    /// bridge paints each list into the scene's authored row labels, so a bag or quest log is
    /// data, and a hot reload keeps whatever was already filled.
    /// </summary>
    public class ListManager
    {
        private readonly Dictionary<string, ListModel> lists =
            new Dictionary<string, ListModel>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The rows of a list, oldest first. An unknown list reads empty rather than
        /// creating one, so a scene with no rows is not left with phantom state.</summary>
        public IReadOnlyList<string> RowsFor(string name)
        {
            if (!string.IsNullOrEmpty(name) && lists.TryGetValue(name, out var model))
                return model.Rows;
            return Array.Empty<string>();
        }

        /// <summary>How many rows the list holds, including blank ones.</summary>
        public int CountOf(string name)
        {
            return name != null && lists.TryGetValue(name, out var model) ? model.Rows.Count : 0;
        }

        /// <summary>The lists that were filled at least once, in the order they first appeared.</summary>
        public List<string> Names()
        {
            var names = new List<string>();
            foreach (var pair in lists)
                names.Add(pair.Key);
            return names;
        }

        private ListModel GetOrCreate(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (!lists.TryGetValue(name, out var model))
            {
                model = new ListModel { Name = name };
                lists[name] = model;
            }
            return model;
        }

        /// <summary><c>clear list &lt;thing&gt;</c>: forgets every row. The list stays known (as an
        /// empty list), so the bridge still repaints its authored rows and hides them.</summary>
        public void Clear(string name)
        {
            GetOrCreate(name)?.Rows.Clear();
        }

        /// <summary><c>add row to list &lt;thing&gt; with &lt;value&gt;</c>: appends and returns the
        /// 1-based index the new row answers to. A blank value adds a blank row: it keeps the
        /// numbering stable, and the bridge shows nothing for it.</summary>
        public int Add(string name, string value)
        {
            var model = GetOrCreate(name);
            if (model == null)
                return 0;
            model.Rows.Add(value ?? "");
            return model.Rows.Count;
        }

        /// <summary>
        /// <c>set row &lt;n&gt; of list &lt;thing&gt; to &lt;value&gt;</c>: writes row n without
        /// disturbing the rows after it. Setting past the end pads the gap with blank rows, so a
        /// scoreboard can fill row 2 before row 1 and row 3 stays row 3. Returns false for an index
        /// below 1 (the parser refuses a literal 0), so a bad index never shifts the list.
        /// </summary>
        public bool Set(string name, int index, string value)
        {
            if (index < 1)
                return false;
            var model = GetOrCreate(name);
            if (model == null)
                return false;
            while (model.Rows.Count < index)
                model.Rows.Add("");
            model.Rows[index - 1] = value ?? "";
            return true;
        }

        /// <summary><c>fill list &lt;thing&gt; from my bag</c>: replaces the whole list with the
        /// given values, so a bag window mirrors the bag rather than growing a duplicate row on
        /// every refill. An empty bag leaves an empty list.</summary>
        public void Fill(string name, IEnumerable<string> values)
        {
            var model = GetOrCreate(name);
            if (model == null)
                return;
            model.Rows.Clear();
            if (values == null)
                return;
            foreach (string value in values)
                model.Rows.Add(value ?? "");
        }

        /// <summary>Forgets every list (a new game, or a test).</summary>
        public void ClearAll()
        {
            lists.Clear();
        }
    }
}
