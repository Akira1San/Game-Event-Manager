using System;

namespace Game
{
    /// <summary>
    /// RTS unit types (Task 22 Part A): what a unit moves over — <c>land</c>, <c>air</c> or
    /// <c>sea</c>.
    ///
    /// The type is an ordinary per-unit variable, so a sheet writes it once
    /// (<c>set my unit type to "air"</c>) and reads it back with <c>me is air</c>; nothing is
    /// remembered anywhere else. A unit that never wrote one — every sheet written before this
    /// word existed — is a land unit, which keeps the old behaviour.
    ///
    /// Task 22 Part B is what makes the type matter to movement: the terrain map hands each
    /// cell's kind to <see cref="CanCross"/>, so ground units stop at water and air units ignore
    /// it.
    /// </summary>
    public static class UnitTypes
    {
        /// <summary>The per-unit variable holding the type (`my unit type`).</summary>
        public const string Variable = "unit type";

        public const string Land = "land";
        public const string Air = "air";
        public const string Sea = "sea";

        /// <summary>Every type, in the order the help text lists them.</summary>
        public static readonly string[] All = { Land, Air, Sea };

        /// <summary>The type a unit has before its sheet writes one.</summary>
        public const string Default = Land;

        /// <summary>Both spellings the language accepts for the variable.</summary>
        private static readonly string[] VariableNames = { "unit type", "UnitType" };

        /// <summary>What the ground under a unit is (Task 22 Part B's <see cref="TerrainMap"/>).</summary>
        public enum Ground
        {
            /// <summary>Solid ground — what every cell is until something marks it.</summary>
            Land,
            /// <summary>Water: sea units only.</summary>
            Water,
            /// <summary>Open space with no ground at all — air units only.</summary>
            Air,
        }

        /// <summary>The word for a ground kind (`land`, `water`, `air`), for messages.</summary>
        public static string GroundName(Ground ground)
        {
            switch (ground)
            {
                case Ground.Water: return "water";
                case Ground.Air: return "air";
                default: return Land;
            }
        }

        /// <summary>The ground a written word names, or null when it is not one.</summary>
        public static Ground? GroundFrom(string written)
        {
            if (string.IsNullOrWhiteSpace(written))
                return null;
            switch (written.Trim().ToLowerInvariant())
            {
                case "land":
                case "ground":
                    return Ground.Land;
                case "water":
                case "sea":
                    return Ground.Water;
                case "air":
                case "void":
                    return Ground.Air;
                default:
                    return null;
            }
        }

        /// <summary>The canonical spelling of a written type, or null when it is not one.</summary>
        public static string Canonical(string written)
        {
            if (string.IsNullOrWhiteSpace(written))
                return null;
            string trimmed = written.Trim();
            foreach (string type in All)
                if (string.Equals(type, trimmed, StringComparison.OrdinalIgnoreCase))
                    return type;
            return null;
        }

        public static bool IsKnown(string written)
        {
            return Canonical(written) != null;
        }

        /// <summary>Every known type, comma-separated, for warning messages.</summary>
        public static string KnownNames()
        {
            return string.Join(", ", All);
        }

        /// <summary>True for the two spellings of the per-unit variable (<c>unit type</c>).</summary>
        public static bool IsVariableName(string name)
        {
            foreach (string candidate in VariableNames)
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// The unit's own <c>my unit type</c>, canonically spelled. A sheet that never wrote one
        /// (or wrote something that is not a type) reads as a land unit.
        /// </summary>
        public static string TypeOf(EventVariables variables, object unit)
        {
            if (variables == null || unit == null)
                return Default;
            foreach (string name in VariableNames)
            {
                if (!variables.HasLocal(unit, name))
                    continue;
                string canonical = Canonical(variables.GetLocal(unit, name).AsText());
                if (canonical != null)
                    return canonical;
            }
            return Default;
        }

        /// <summary>
        /// Whether a unit of <paramref name="type"/> may cross ground of <paramref name="ground"/>.
        /// Ground units walk on land, sea units are restricted to water, and air units ignore the
        /// ground entirely (so they also cross water and open air).
        /// </summary>
        public static bool CanCross(string type, Ground ground)
        {
            switch (Canonical(type) ?? Default)
            {
                case Air:
                    return true;
                case Sea:
                    return ground == Ground.Water;
                default:
                    return ground == Ground.Land;
            }
        }

        /// <summary>`me is land` / `me is air` / `me is sea`.</summary>
        public static bool Is(EventContext context, string type)
        {
            string wanted = Canonical(type);
            if (context == null || wanted == null)
                return false;
            return string.Equals(TypeOf(context.Variables, context.Me), wanted,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
