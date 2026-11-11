using System;

namespace Game
{
    /// <summary>
    /// Names for spawned instances (Task 17 Part D). A prefab (or scene) instance starts
    /// with the asset's actor name — the same name as the template/prefab root — so a
    /// second spawn would collide with the first and <c>Level.FindActor</c> could only ever
    /// find one of them. Spawned copies get a Flax-style numbered name instead
    /// ("Hero", "Hero 2", "Hero 3", …).
    /// </summary>
    public static class SpawnNames
    {
        /// <summary>First free name starting from <paramref name="desired"/>: the name
        /// itself, then "&lt;name&gt; 2", "&lt;name&gt; 3", … <paramref name="taken"/> decides
        /// whether a name is already in use (case-insensitive callers pass their own
        /// comparer). An empty desired name becomes "Spawned".</summary>
        public static string Unique(string desired, Func<string, bool> taken)
        {
            string first = string.IsNullOrEmpty(desired) ? "Spawned" : desired.Trim();
            if (first.Length == 0)
                first = "Spawned";
            if (taken == null || !taken(first))
                return first;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = first + " " + i;
                if (!taken(candidate))
                    return candidate;
            }
            return first + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
