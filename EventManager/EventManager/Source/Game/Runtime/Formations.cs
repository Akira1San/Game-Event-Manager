using System;

namespace Game
{
    /// <summary>The shapes a group can walk in (Task 23 Part C).</summary>
    public enum FormationKind
    {
        /// <summary>A row across the line of travel.</summary>
        Line,
        /// <summary>A V pointing the way the group is heading.</summary>
        Wedge,
        /// <summary>A single file, one behind the other.</summary>
        Column,
    }

    /// <summary>
    /// RTS formation movement (Task 23 Part C): how a group spreads over a ground destination so
    /// the units stop standing on one point.
    ///
    /// When several units are selected and each is given the same ground order (<c>goto there</c>,
    /// <c>attack-move to there</c>), every unit offsets its destination by its own slot in the
    /// group: a row across the direction of travel, a V, or a single file. The shape comes from
    /// the <c>Formation</c> global (<c>line</c> by default, <c>wedge</c> / <c>column</c>) and the
    /// gap between slots from <c>FormationSpacing</c>. A unit that is alone, or not selected,
    /// gets no offset, so single-unit commands behave exactly as before.
    ///
    /// Actor targets (an enemy, a rally actor) are untouched: units converge on the actor, and an
    /// attack order's stop distance is measured from it. Pure maths, so the tests drive it.
    /// </summary>
    public static class Formations
    {
        /// <summary>Global holding the shape: <c>line</c>, <c>wedge</c> or <c>column</c>.</summary>
        public const string Variable = "Formation";

        /// <summary>Global holding the gap between slots, in world units.</summary>
        public const string SpacingVariable = "FormationSpacing";

        /// <summary>Gap used when <c>FormationSpacing</c> was never set — one grid cell.</summary>
        public const double DefaultSpacing = 80;

        public static string Name(FormationKind kind)
        {
            switch (kind)
            {
                case FormationKind.Wedge: return "wedge";
                case FormationKind.Column: return "column";
                default: return "line";
            }
        }

        /// <summary>The shape a written word names (`line`/`row`, `wedge`/`v`, `column`/`file`).</summary>
        public static bool TryParse(string written, out FormationKind kind)
        {
            kind = FormationKind.Line;
            if (string.IsNullOrWhiteSpace(written))
                return false;
            switch (written.Trim().ToLowerInvariant())
            {
                case "line":
                case "row":
                    kind = FormationKind.Line;
                    return true;
                case "wedge":
                case "v":
                    kind = FormationKind.Wedge;
                    return true;
                case "column":
                case "file":
                    kind = FormationKind.Column;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The shape this game uses right now (unknown or unset reads as <c>line</c>).</summary>
        public static FormationKind KindFrom(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(Variable))
                return FormationKind.Line;
            return TryParse(variables.GetGlobal(Variable).AsText(), out FormationKind kind)
                ? kind
                : FormationKind.Line;
        }

        /// <summary>The gap between slots right now, clamped to something sane.</summary>
        public static double SpacingFrom(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(SpacingVariable))
                return DefaultSpacing;
            double spacing = variables.GetGlobal(SpacingVariable).AsNumber();
            return spacing > 0 ? spacing : DefaultSpacing;
        }

        /// <summary>
        /// Where unit <paramref name="index"/> of <paramref name="count"/> stands relative to the
        /// destination, with the group heading along (<paramref name="headingX"/>,
        /// <paramref name="headingZ"/>). A lone unit, a bad index or no spacing gets no offset.
        /// </summary>
        public static void Slot(FormationKind kind, int index, int count, double spacing,
            double headingX, double headingZ, out double offsetX, out double offsetZ)
        {
            offsetX = 0;
            offsetZ = 0;
            if (count <= 1 || index < 0 || index >= count || spacing <= 0)
                return;

            double length = Math.Sqrt(headingX * headingX + headingZ * headingZ);
            double forwardX = 0;
            double forwardZ = 1;
            if (length > 0.0001)
            {
                forwardX = headingX / length;
                forwardZ = headingZ / length;
            }
            // Right of the direction of travel in the XZ plane: heading +Z → +X.
            double rightX = forwardZ;
            double rightZ = -forwardX;

            double forward = 0;
            double right = 0;
            switch (kind)
            {
                case FormationKind.Column:
                    forward = -index * spacing;
                    break;
                case FormationKind.Wedge:
                {
                    int rank = (index + 1) / 2;
                    int side = index == 0 ? 0 : (index % 2 == 1 ? 1 : -1);
                    forward = -rank * spacing;
                    right = side * rank * spacing;
                    break;
                }
                default:
                    right = (index - (count - 1) / 2.0) * spacing;
                    break;
            }

            offsetX = forwardX * forward + rightX * right;
            offsetZ = forwardZ * forward + rightZ * right;
        }
    }
}
