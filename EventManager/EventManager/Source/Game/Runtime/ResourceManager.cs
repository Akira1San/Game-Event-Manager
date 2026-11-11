using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game
{
    /// <summary>
    /// Canonical names and helpers for the RTS resource economy (Task 18 Part B).
    /// Resources are ordinary global variables with stable canonical spellings, so
    /// <c>Supply</c>, <c>Supply Used</c> and <c>SupplyUsed</c> all name the same
    /// value. A resource that was never set reads as 0 without the usual
    /// "read before set" warning, which keeps HUD/afford checks quiet on a fresh scene.
    /// </summary>
    public static class ResourceManager
    {
        /// <summary>Every resource, in the order a change snapshot reports them.</summary>
        public static readonly string[] CanonicalNames =
        {
            "Minerals", "Gas", "SupplyUsed", "SupplyMax", "FoodCap",
            "Ore", "Gems", "Power", "RUs",
        };

        private static readonly Dictionary<string, string> Aliases = BuildAliases();

        private static Dictionary<string, string> BuildAliases()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in CanonicalNames)
                map[Normalize(name)] = name;
            Add(map, "Minerals", "mineral");
            Add(map, "SupplyUsed", "supply", "supply used", "supplied", "used supply");
            Add(map, "SupplyMax", "supply max", "max supply");
            Add(map, "FoodCap", "food cap", "food");
            Add(map, "Gems", "gem");
            Add(map, "RUs", "ru", "rus", "resource units", "resource unit");
            return map;
        }

        private static void Add(Dictionary<string, string> map, string canonical, params string[] aliases)
        {
            foreach (var alias in aliases)
                map[Normalize(alias)] = canonical;
        }

        /// <summary>Trims, collapses whitespace and lowercases a written name for lookup.</summary>
        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            var builder = new StringBuilder(name.Length);
            bool pendingSpace = false;
            foreach (char c in name.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        /// <summary>Maps any spelling of a resource to its canonical global name.</summary>
        public static bool TryCanonical(string name, out string canonical)
        {
            if (!string.IsNullOrEmpty(name) && Aliases.TryGetValue(Normalize(name), out canonical))
                return true;
            canonical = name;
            return false;
        }

        /// <summary>The canonical resource name, or <paramref name="name"/> unchanged.</summary>
        public static string CanonicalOrName(string name)
        {
            return TryCanonical(name, out string canonical) ? canonical : name;
        }

        public static bool IsResource(string name)
        {
            return TryCanonical(name, out _);
        }

        /// <summary>
        /// Reads a global variable. Resources default to 0 (no warning); everything else
        /// keeps the normal missing-variable behaviour. Returns true for resources.
        /// </summary>
        public static bool TryReadAt(EventVariables variables, string name, out ValueBox value)
        {
            if (TryCanonical(name, out string canonical))
            {
                value = variables != null && variables.HasGlobal(canonical)
                    ? variables.GetGlobal(canonical)
                    : ValueBox.OfNumber(0);
                return true;
            }
            value = variables != null ? variables.GetGlobal(name) : new ValueBox();
            return false;
        }

        public static ValueBox Read(EventVariables variables, string name)
        {
            TryReadAt(variables, name, out ValueBox value);
            return value;
        }

        public static double ReadNumber(EventVariables variables, string name)
        {
            return Read(variables, name).AsNumber();
        }

        /// <summary>A compact, stable signature of every resource's numeric value.</summary>
        public static string Snapshot(EventVariables variables)
        {
            var builder = new StringBuilder();
            foreach (var name in CanonicalNames)
            {
                if (builder.Length > 0)
                    builder.Append(';');
                builder.Append(name).Append('=')
                    .Append(ReadNumber(variables, name).ToString("R", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        /// <summary>
        /// True when the current resources cover every cost variable the building declares.
        /// Costs are data-driven: <c>BarracksCost</c> (minerals), <c>BarracksGasCost</c>,
        /// <c>BarracksOreCost</c>, <c>BarracksGemsCost</c>, <c>BarracksPowerCost</c>,
        /// <c>BarracksRUsCost</c> and <c>BarracksSupplyCost</c> (supply slots).
        /// A building with no cost variables is always affordable.
        /// </summary>
        public static bool CanAfford(EventVariables variables, string building)
        {
            if (string.IsNullOrEmpty(building))
                return true;

            foreach (string resource in new[] { "Minerals", "Gas", "Ore", "Gems", "Power", "RUs" })
            {
                if (!TryCost(variables, building, resource, out double cost))
                    continue;
                if (ReadNumber(variables, resource) < cost)
                    return false;
            }

            if (TryCost(variables, building, "SupplyUsed", out double supplyCost) ||
                TryCost(variables, building, "Supply", out supplyCost))
            {
                if (ReadNumber(variables, "SupplyUsed") + supplyCost > SupplyCap(variables))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The supply ceiling: <c>SupplyMax</c> when set, otherwise <c>FoodCap</c>
        /// (Red Alert style), otherwise 0 for a fresh scene.
        /// </summary>
        public static double SupplyCap(EventVariables variables)
        {
            if (variables != null && variables.HasGlobal("SupplyMax"))
                return ReadNumber(variables, "SupplyMax");
            if (variables != null && variables.HasGlobal("FoodCap"))
                return ReadNumber(variables, "FoodCap");
            return 0;
        }

        /// <summary>The declared cost of one resource for a building, if a cost variable
        /// exists. Used by <c>enough Minerals for Barracks</c> as well as affordability.</summary>
        public static bool TryCost(EventVariables variables, string building, string resource, out double cost)
        {
            cost = 0;
            // `<Building>Cost` is the friendly universal minerals form; every resource
            // can also be named explicitly, e.g. `<Building>GasCost`.
            if (resource == "Minerals" && TryNumber(variables, building + "Cost", out cost))
                return true;
            return TryNumber(variables, building + resource + "Cost", out cost);
        }

        private static bool TryNumber(EventVariables variables, string name, out double value)
        {
            value = 0;
            if (variables == null || !variables.HasGlobal(name))
                return false;
            value = variables.GetGlobal(name).AsNumber();
            return true;
        }
    }
}
