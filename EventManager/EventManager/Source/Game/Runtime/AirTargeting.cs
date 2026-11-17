using System;

namespace Game
{
    /// <summary>
    /// Air targeting (Task 28 Part D): what <c>target</c> means while a fight is running, and
    /// whether this unit may hit a written unit type at all.
    ///
    /// <c>target</c> is the thing a unit is engaging: the actor in its active order, and when it
    /// has no such order — an idle sheet, or an event that is only starting the order — the actor
    /// whose event is running (<c>other</c>). Both resolve to a scene actor, so
    /// <c>target is air</c> and <c>me can attack target</c> work in an event body and in a
    /// per-tick check alike.
    ///
    /// <c>me can attack air</c> / <c>me can attack ground</c> ask the Task 23 matchup table about
    /// a type instead of a named actor, and <see cref="Refusal"/> is the same answer phrased for
    /// an <c>attack</c> order that cannot possibly land: the order is refused with one warning
    /// rather than walking a ground unit over to swing at the sky.
    /// </summary>
    public static class AirTargeting
    {
        /// <summary>The pronoun a sheet writes for "the thing this unit is engaging".</summary>
        public const string Pronoun = "target";

        /// <summary>The word that means the land unit type on an attack gate.</summary>
        public const string Ground = "ground";

        /// <summary>
        /// The canonical unit type a written type names, with <c>ground</c> read as
        /// <c>land</c> so the RTS wording <c>can attack ground</c> works.
        /// </summary>
        public static string Canonical(string written)
        {
            if (!string.IsNullOrWhiteSpace(written) &&
                string.Equals(written.Trim(), Ground, StringComparison.OrdinalIgnoreCase))
                return UnitTypes.Land;
            return UnitTypes.Canonical(written);
        }

        /// <summary>
        /// The actor <c>target</c> points at: the active order's target while there is one,
        /// otherwise the actor whose event is running. Null when neither exists.
        /// </summary>
        public static object TargetOf(EventContext context)
        {
            if (context == null)
                return null;
            return OrderTargetOf(context) ?? context.Other;
        }

        /// <summary>
        /// The active order's target actor, or null for an idle/stopped unit and for an order
        /// that points at a clicked position instead of an actor.
        /// </summary>
        public static object OrderTargetOf(EventContext context)
        {
            if (context == null || context.Runtime == null || context.Me == null)
                return null;
            UnitOrder order = context.Runtime.Orders.CurrentOrderOf(context.Me);
            return order == null ? null : order.Target;
        }

        /// <summary>
        /// The type a unit counts as for the air gate: a unit holding an altitude above 0 is
        /// air whatever <c>my unit type</c> says, because a building lifted with <c>lift off</c>
        /// (<c>Runtime/LiftOff.cs</c>) is up in the sky rather than standing on the ground where
        /// a land unit could reach it.
        /// </summary>
        public static string EffectiveTypeOf(EventVariables variables, object unit)
        {
            if (Altitude.IsAirborne(variables, unit))
                return UnitTypes.Air;
            return UnitTypes.TypeOf(variables, unit);
        }

        /// <summary>`target is air` / `target is land` — the pointed-at unit's effective type.</summary>
        public static bool IsType(EventContext context, object target, string written)
        {
            string wanted = Canonical(written);
            if (wanted == null || target == null)
                return false;
            return string.Equals(EffectiveTypeOf(context.Variables, target), wanted,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// `me can attack air` / `me can attack ground`: this unit's effective type against the written
        /// one, through the Task 23 table (so a scene's authored rows apply).
        /// </summary>
        public static bool CanAttackType(EventContext context, string written)
        {
            string defender = Canonical(written);
            if (context == null || defender == null)
                return false;
            return Matchups.CanAttack(context.Variables,
                EffectiveTypeOf(context.Variables, context.Me), defender);
        }

        /// <summary>Whether an <c>attack</c> on this actor can land at all, per the table.</summary>
        public static bool CanEngage(EventContext context, object target)
        {
            return Matchups.CanAttackTarget(context, target);
        }

        /// <summary>
        /// The single warning an <c>attack</c> order writes for a pair the table forbids, or
        /// null when the attack may start. Names the global that would allow the pair, so the
        /// line points at the fix instead of silently doing nothing.
        /// </summary>
        public static string Refusal(EventContext context, object target)
        {
            if (context == null || target == null || CanEngage(context, target))
                return null;
            string mine = EffectiveTypeOf(context.Variables, context.Me);
            string theirs = EffectiveTypeOf(context.Variables, target);
            string name = context.Runtime == null ? "" : context.Runtime.NameOfActor(target);
            if (string.IsNullOrEmpty(name))
                name = "that target";
            return $"\u201Cattack\u201D can't hit \u201C{name}\u201D: a {mine} unit cannot attack a {theirs} " +
                   $"unit. Set {Matchups.VariableFor(mine, theirs)} to true if it should be able to.";
        }
    }
}
