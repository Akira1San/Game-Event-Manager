using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game
{
    /// <summary>
    /// Canonical names and helpers for the RTS resource economy (Task 18 Part B).
    ///
    /// Resources are ordinary global variables, and **which names exist is data**, not code: a
    /// game declares them in <c>Resources.events</c> (read at load like <c>Matchups.events</c>), so
    /// a StarCraft flavour can ship Minerals/Gas and a Red Alert one Ore/Gems without touching C#.
    ///
    /// <code>
    /// when game starts
    ///   set Resources to "Minerals, Gas, SupplyUsed, SupplyMax, FoodCap, Ore, Gems, Power, RUs"
    ///   set ResourceAliases to "Minerals=mineral; SupplyUsed=supply,supply used,supplied"
    ///   set SupplyUsedResource to "SupplyUsed"
    ///   set SupplyCapResource to "SupplyMax"
    ///   set SupplyCapFallbackResource to "FoodCap"
    ///   set DefaultCostResource to "Minerals"
    /// </code>
    ///
    /// A declared name is a resource everywhere: <c>Minerals is higher than 50</c>,
    /// <c>enough Minerals for Barracks</c>, <c>&lt;Building&gt;&lt;Resource&gt;Cost</c>,
    /// <c>when resources change</c> and <c>add 5 to Credits</c> all work on it. A resource that was
    /// never set reads as 0 without the usual "read before set" warning.
    ///
    /// No file (or a file that declares nothing) leaves the built-in nine in place, so every
    /// existing scene behaves exactly as before.
    /// </summary>
    public static class ResourceManager
    {
        /// <summary>The data file's name, like <c>Matchups.events</c> and <c>EnemyTypes.events</c>.</summary>
        public const string FileName = "Resources";

        /// <summary>The built-in list, used until a data file declares its own.</summary>
        public static readonly string[] DefaultNames =
        {
            "Minerals", "Gas", "SupplyUsed", "SupplyMax", "FoodCap",
            "Ore", "Gems", "Power", "RUs",
        };

        // ---- the keys a Resources.events file writes -------------------------------------
        /// <summary>`set Resources to "Minerals, Gas, ..."` — the list, in snapshot order.</summary>
        public const string NamesKey = "Resources";
        /// <summary>`set ResourceAliases to "Minerals=mineral; ..."` — extra spellings.</summary>
        public const string AliasesKey = "ResourceAliases";
        /// <summary>Which declared resource counts as "supply used".</summary>
        public const string SupplyUsedKey = "SupplyUsedResource";
        /// <summary>Which declared resource is the supply ceiling.</summary>
        public const string SupplyCapKey = "SupplyCapResource";
        /// <summary>The ceiling to read when the primary one was never set (Red Alert's food cap).</summary>
        public const string SupplyFallbackKey = "SupplyCapFallbackResource";
        /// <summary>Which resource `<Building>Cost` means (Minerals by default).</summary>
        public const string DefaultCostKey = "DefaultCostResource";

        private static readonly List<string> Current = new List<string>(DefaultNames);
        private static Dictionary<string, string> _aliases = BuildAliases(Current, null);
        private static string _supplyUsed = "SupplyUsed";
        private static string _supplyCap = "SupplyMax";
        private static string _supplyFallback = "FoodCap";
        private static string _defaultCost = "Minerals";

        /// <summary>Every declared resource, in the order a change snapshot reports them.</summary>
        public static IReadOnlyList<string> CanonicalNames => Current;

        /// <summary>The declared resource that counts as supply used (<c>SupplyUsed</c> by default).</summary>
        public static string SupplyUsedName => _supplyUsed;

        /// <summary>The declared supply ceiling, or the fallback when the primary was never set.</summary>
        public static string SupplyCapName => _supplyCap;

        /// <summary>The resource `<Building>Cost` names (Minerals by default).</summary>
        public static string DefaultCostName => _defaultCost;

        /// <summary>
        /// Reads the resource declarations out of <c>Resources.events</c>. Call it before the other
        /// sheets are parsed, so their spellings resolve through the same table. Pass <c>null</c>
        /// (or a file that declares nothing) to restore the built-in nine.
        /// </summary>
        public static void LoadData(ProgramFile file)
        {
            Reset();
            if (file == null)
                return;

            string aliases = null;
            foreach (EventNode ev in file.Events)
            {
                foreach (BodyLineNode line in ev.Body)
                {
                    if (!(line.Statement is SetNode set) || set.Op != '\0')
                        continue;
                    if (!string.IsNullOrEmpty(set.Target.Owner))
                        continue; // a `my`/`other` variable is not a declaration
                    if (!TryLiteral(set.Value, out ValueBox value))
                        continue;

                    switch (set.Target.Name)
                    {
                        case NamesKey:
                        {
                            List<string> declared = SplitList(value.AsText(), ',', ';');
                            if (declared.Count > 0)
                            {
                                Current.Clear();
                                Current.AddRange(declared);
                            }
                            break;
                        }
                        case AliasesKey:
                            aliases = value.AsText();
                            break;
                        case SupplyUsedKey:
                            _supplyUsed = NonEmpty(value.AsText(), _supplyUsed);
                            break;
                        case SupplyCapKey:
                            _supplyCap = NonEmpty(value.AsText(), _supplyCap);
                            break;
                        case SupplyFallbackKey:
                            _supplyFallback = NonEmpty(value.AsText(), _supplyFallback);
                            break;
                        case DefaultCostKey:
                            _defaultCost = NonEmpty(value.AsText(), _defaultCost);
                            break;
                    }
                }
            }
            _aliases = BuildAliases(Current, aliases);
        }

        /// <summary>Back to the built-in list; the tests and a reload with no file both use this.</summary>
        public static void Reset()
        {
            Current.Clear();
            Current.AddRange(DefaultNames);
            _supplyUsed = "SupplyUsed";
            _supplyCap = "SupplyMax";
            _supplyFallback = "FoodCap";
            _defaultCost = "Minerals";
            _aliases = BuildAliases(Current, null);
        }

        private static Dictionary<string, string> BuildAliases(IReadOnlyList<string> names, string extra)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
                map[Normalize(name)] = name;
            // The built-in spellings keep working for the built-in names, so a scene written
            // before the file existed still parses (`supply used`, `food`, `ru`, …).
            if (Contains(names, "Minerals"))
                Add(map, "Minerals", "mineral");
            if (Contains(names, "SupplyUsed"))
                Add(map, "SupplyUsed", "supply", "supply used", "supplied", "used supply");
            if (Contains(names, "SupplyMax"))
                Add(map, "SupplyMax", "supply max", "max supply");
            if (Contains(names, "FoodCap"))
                Add(map, "FoodCap", "food cap", "food");
            if (Contains(names, "Gems"))
                Add(map, "Gems", "gem");
            if (Contains(names, "RUs"))
                Add(map, "RUs", "ru", "rus", "resource units", "resource unit");

            // `Minerals=mineral,ore; Gas=gas` — a game's own spellings, last so they win.
            if (!string.IsNullOrEmpty(extra))
            {
                foreach (string group in extra.Split(';'))
                {
                    int equals = group.IndexOf('=');
                    if (equals <= 0)
                        continue;
                    string canonical = group.Substring(0, equals).Trim();
                    if (canonical.Length == 0)
                        continue;
                    foreach (string alias in SplitList(group.Substring(equals + 1), ',', '|'))
                        if (alias.Length > 0)
                            map[Normalize(alias)] = canonical;
                }
            }
            return map;
        }

        private static bool Contains(IReadOnlyList<string> names, string name)
        {
            foreach (string candidate in names)
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static void Add(Dictionary<string, string> map, string canonical, params string[] aliases)
        {
            foreach (var alias in aliases)
                map[Normalize(alias)] = canonical;
        }

        private static string NonEmpty(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        /// <summary>Splits a declaration list, trimming and dropping empty entries.</summary>
        private static List<string> SplitList(string text, params char[] separators)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(text))
                return parts;
            foreach (string piece in text.Split(separators))
            {
                string name = piece.Trim();
                if (name.Length > 0)
                    parts.Add(name);
            }
            return parts;
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
            if (!string.IsNullOrEmpty(name) && _aliases.TryGetValue(Normalize(name), out canonical))
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
            foreach (var name in Current)
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
        /// Costs are data-driven: <c>BarracksCost</c> (the default cost resource, Minerals unless
        /// <c>DefaultCostResource</c> says otherwise), <c>BarracksGasCost</c>,
        /// <c>BarracksOreCost</c>, <c>BarracksCreditsCost</c> — one per declared resource — plus
        /// <c>BarracksSupplyCost</c> for supply slots. A building with no cost variables is always
        /// affordable.
        /// </summary>
        public static bool CanAfford(EventVariables variables, string building)
        {
            if (string.IsNullOrEmpty(building))
                return true;

            foreach (string resource in CostResources())
            {
                if (!TryCost(variables, building, resource, out double cost))
                    continue;
                if (ReadNumber(variables, resource) < cost)
                    return false;
            }

            if (TryCost(variables, building, "SupplyUsed", out double supplyCost) ||
                TryCost(variables, building, "Supply", out supplyCost))
            {
                if (ReadNumber(variables, _supplyUsed) + supplyCost > SupplyCap(variables))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Every <c>&lt;Name&gt;&lt;Resource&gt;Cost</c> global the name carries, one entry per declared
        /// resource. Research prices an upgrade by this, so an upgrade needs no building actor.
        /// </summary>
        public static List<(string Resource, double Amount)> CostsOf(EventVariables variables, string name)
        {
            var costs = new List<(string, double)>();
            if (string.IsNullOrEmpty(name))
                return costs;
            foreach (string resource in CostResources())
                if (TryCost(variables, name, resource, out double amount) && amount != 0)
                    costs.Add((resource, amount));
            return costs;
        }

        /// <summary>The bank global for one player: <c>Player2 Minerals</c> (Task 27 Part D).</summary>
        public static string BankName(int playerIndex, string resource)
        {
            return Tech.PlayerPrefix(playerIndex) + " " + CanonicalOrName(resource);
        }

        /// <summary>
        /// A player's stockpile: <c>Player2 Minerals</c> when the game keeps per-player banks,
        /// otherwise the bare global — <c>Minerals</c> stays player 1's alias, so every
        /// single-player economy file keeps working untouched.
        /// </summary>
        public static double BankValue(EventVariables variables, int playerIndex, string resource)
        {
            string bank = BankName(playerIndex, resource);
            if (variables != null && variables.HasGlobal(bank))
                return variables.GetGlobal(bank).AsNumber();
            return playerIndex <= 0 ? ReadNumber(variables, resource) : 0;
        }

        /// <summary>Writes a player's stockpile, clamped at 0 like <c>deduct</c>.</summary>
        public static void WriteBank(EventVariables variables, int playerIndex, string resource, double value)
        {
            if (variables == null)
                return;
            string bank = BankName(playerIndex, resource);
            string target = playerIndex <= 0 && !variables.HasGlobal(bank)
                ? CanonicalOrName(resource)
                : bank;
            variables.SetGlobal(target, ValueBox.OfNumber(value < 0 ? 0 : value));
        }

        /// <summary>The declared resources that carry a cost — everything but the supply ones.</summary>
        public static List<string> CostResources()
        {
            var names = new List<string>(Current.Count);
            foreach (string name in Current)
            {
                if (IsSupplyName(name))
                    continue;
                names.Add(name);
            }
            return names;
        }

        private static bool IsSupplyName(string name)
        {
            return string.Equals(name, _supplyUsed, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, _supplyCap, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, _supplyFallback, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The supply ceiling: the declared cap when it was set, otherwise the declared fallback
        /// (Red Alert style), otherwise 0 for a fresh scene.
        /// </summary>
        public static double SupplyCap(EventVariables variables)
        {
            if (variables != null && variables.HasGlobal(_supplyCap))
                return ReadNumber(variables, _supplyCap);
            if (variables != null && variables.HasGlobal(_supplyFallback))
                return ReadNumber(variables, _supplyFallback);
            return 0;
        }

        /// <summary>The declared cost of one resource for a building, if a cost variable
        /// exists. Used by <c>enough Minerals for Barracks</c> as well as affordability.</summary>
        public static bool TryCost(EventVariables variables, string building, string resource, out double cost)
        {
            cost = 0;
            // `<Building>Cost` is the friendly universal form for the default cost resource;
            // every resource can also be named explicitly, e.g. `<Building>GasCost`.
            if (string.Equals(resource, _defaultCost, StringComparison.OrdinalIgnoreCase) &&
                TryNumber(variables, building + "Cost", out cost))
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
