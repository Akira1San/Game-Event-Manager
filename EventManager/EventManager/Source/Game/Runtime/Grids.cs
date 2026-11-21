using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One filled grid slot (Task 34 Part B): the text it shows and the picture name
    /// its icon resolves through the same loose texture index as <c>set image</c>.</summary>
    public class GridSlot
    {
        public string Text = "";
        /// <summary>The sprite name the slot's icon tries to show. `fill grid … from my bag`
        /// keeps this as the bare item name, so a stack's row can read `Sword x2` and still
        /// find the `Sword` picture.</summary>
        public string Icon = "";
    }

    /// <summary>One filled grid (Task 34 Part B): its slots, first slot first.</summary>
    public class GridModel
    {
        public string Name = "";
        public readonly List<GridSlot> Slots = new List<GridSlot>();
    }

    /// <summary>
    /// The data behind <c>clear grid</c>, <c>set slot … of grid</c> and
    /// <c>fill grid … from my bag</c> (Task 34 Part B). Like <see cref="ListManager"/> it is
    /// runtime state, not a control: the bridge paints the scene's authored slot actors, so a bag
    /// or an ability bar fills from data and a hot reload keeps what was already shown. Selection
    /// highlight stays authored (<c>tween</c> / <c>set color</c>), so no word is needed for it.
    /// </summary>
    public class GridManager
    {
        private readonly Dictionary<string, GridModel> grids =
            new Dictionary<string, GridModel>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The slots of a grid, first slot first. An unknown grid reads empty rather
        /// than creating one.</summary>
        public IReadOnlyList<GridSlot> SlotsFor(string name)
        {
            if (!string.IsNullOrEmpty(name) && grids.TryGetValue(name, out var model))
                return model.Slots;
            return Array.Empty<GridSlot>();
        }

        /// <summary>How many slots the grid holds, including empty ones.</summary>
        public int CountOf(string name)
        {
            return name != null && grids.TryGetValue(name, out var model) ? model.Slots.Count : 0;
        }

        /// <summary>The grids that were filled at least once, in the order they first appeared.</summary>
        public List<string> Names()
        {
            var names = new List<string>();
            foreach (var pair in grids)
                names.Add(pair.Key);
            return names;
        }

        private GridModel GetOrCreate(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (!grids.TryGetValue(name, out var model))
            {
                model = new GridModel { Name = name };
                grids[name] = model;
            }
            return model;
        }

        /// <summary><c>clear grid &lt;thing&gt;</c>: empties every slot. The grid stays known, so
        /// the bridge still paints its authored slots as empty.</summary>
        public void Clear(string name)
        {
            GetOrCreate(name)?.Slots.Clear();
        }

        /// <summary>
        /// <c>set slot &lt;n&gt; of grid &lt;thing&gt; to &lt;value&gt;</c> (or <c>empty</c>): writes
        /// one slot without disturbing the slots after it. Writing past the end pads the gap with
        /// empty slots, so a bag can fill slot 3 before slot 1 and the numbering stays put. Returns
        /// false for an index below 1.
        /// </summary>
        public bool Set(string name, int index, string text, string icon)
        {
            if (index < 1)
                return false;
            var model = GetOrCreate(name);
            if (model == null)
                return false;
            while (model.Slots.Count < index)
                model.Slots.Add(new GridSlot());
            model.Slots[index - 1] = new GridSlot { Text = text ?? "", Icon = icon ?? "" };
            return true;
        }

        /// <summary><c>set slot &lt;n&gt; of grid &lt;thing&gt; empty</c>: clears one slot (its text
        /// and its icon) and leaves the numbering alone.</summary>
        public bool Empty(string name, int index)
        {
            return Set(name, index, "", "");
        }

        /// <summary><c>fill grid &lt;thing&gt; from my bag</c>: replaces the whole grid with one slot
        /// per entry, so a bag window mirrors the bag rather than growing a duplicate slot on every
        /// refill. An empty bag leaves an empty grid.</summary>
        public void Fill(string name, IEnumerable<GridSlot> slots)
        {
            var model = GetOrCreate(name);
            if (model == null)
                return;
            model.Slots.Clear();
            if (slots == null)
                return;
            foreach (var slot in slots)
            {
                model.Slots.Add(slot == null
                    ? new GridSlot()
                    : new GridSlot { Text = slot.Text ?? "", Icon = slot.Icon ?? "" });
            }
        }

        /// <summary>Forgets every grid (a new game, or a test).</summary>
        public void ClearAll()
        {
            grids.Clear();
        }
    }
}
