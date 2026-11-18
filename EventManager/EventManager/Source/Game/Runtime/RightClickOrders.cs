using System;

namespace Game
{
    /// <summary>What the cursor was over when a right click asked for an order (Task 29 Part C).</summary>
    public enum RightClickTarget
    {
        /// <summary>Nothing at all — the click missed the world.</summary>
        Nothing,
        /// <summary>Bare ground, or scenery that is nobody's unit.</summary>
        Ground,
        /// <summary>A unit or building of another player.</summary>
        Enemy,
        /// <summary>A unit of the local player.</summary>
        Friendly,
        /// <summary>A building of the local player.</summary>
        OwnBuilding,
        /// <summary>A resource node (`my ResourceNode` or `my NodeAmount`).</summary>
        ResourceNode,
    }

    /// <summary>The default order a right click issues for the selection.</summary>
    public enum DefaultOrderKind
    {
        /// <summary>Leave the selection alone — the click means nothing for these actors.</summary>
        None,
        Move,
        Attack,
        /// <summary>Walk a worker to a resource node and let its sheet do the mining.</summary>
        Gather,
        /// <summary>Walk to a damaged friendly unit or building and let its sheet repair it.</summary>
        Repair,
        /// <summary>A production building's rally point (the click picks where new units walk).</summary>
        Rally,
    }

    /// <summary>One row of the right-click table.</summary>
    public sealed class RightClickRule
    {
        public RightClickTarget Target;
        public DefaultOrderKind Order;
        /// <summary>Why the row says what it says, so a change to the table reads as a decision.</summary>
        public string Reason;
    }

    /// <summary>
    /// The StarCraft right click (Task 29 Part C): what the thing under the cursor is, and the
    /// order that means for whatever is selected. Pure decisions with no Flax types, so the
    /// bridge only classifies the hit and starts the movement.
    ///
    /// The table is deliberately a value here rather than rules written into the runtime: a game
    /// that wants right-clicking its own buildings to mean something else edits
    /// <see cref="Table"/> (or a later task adds the authored form) instead of fighting C#.
    /// </summary>
    public static class RightClickOrders
    {
        /// <summary>A local variable that tags an actor as a resource node (`my ResourceNode`).</summary>
        public const string ResourceNodeVariable = "ResourceNode";

        /// <summary>The amount a worker would take from it (`my NodeAmount`), which also tags it.</summary>
        public const string ResourceAmountVariable = "NodeAmount";

        /// <summary>
        /// A per-actor flag meaning "this actor is an enemy" (`my Hostile`), which `enemy type`
        /// writes and `when all enemies dead` already reads. It is the second half of the enemy
        /// test, so a scene that never authored a player still has enemies.
        /// </summary>
        public const string HostileVariable = "Hostile";

        /// <summary>
        /// What every target means, in one place: enemy → attack, ground → move, a resource
        /// node → gather, a damaged friendly → repair, and your own buildings → no order. The
        /// last part is StarCraft's "don't walk off and do something surprising" rule — an ally
        /// nobody needs to fix orders nothing, and a building only takes a rally point because a
        /// selection of units has no business walking into its own base.
        /// </summary>
        public static readonly RightClickRule[] Table =
        {
            new RightClickRule
            {
                Target = RightClickTarget.Enemy,
                Order = DefaultOrderKind.Attack,
                Reason = "an enemy under the cursor is attacked by everything selected that can reach it",
                // Enemy comes from another fog player or from `my Hostile`, so an ordinary scene
                // with no player data still attacks its enemy sheets.
            },
            new RightClickRule
            {
                Target = RightClickTarget.Ground,
                Order = DefaultOrderKind.Move,
                Reason = "empty ground is a move order; the group spreads over formation slots",
            },
            new RightClickRule
            {
                Target = RightClickTarget.ResourceNode,
                Order = DefaultOrderKind.Gather,
                Reason = "a worker walks to the node on a gather order and the sheet's own tick does the mining",
            },
            new RightClickRule
            {
                Target = RightClickTarget.Friendly,
                Order = DefaultOrderKind.Repair,
                Reason = "a damaged ally is repaired; Resolve sends a healthy one back to no order",
            },
            new RightClickRule
            {
                Target = RightClickTarget.OwnBuilding,
                Order = DefaultOrderKind.None,
                Reason = "clicking your own building orders nothing",
            },
            new RightClickRule
            {
                Target = RightClickTarget.Nothing,
                Order = DefaultOrderKind.None,
                Reason = "a click that missed the world orders nothing",
            },
        };

        /// <summary>The table row for one target, or <see cref="DefaultOrderKind.None"/> when it is absent.</summary>
        public static DefaultOrderKind RuleFor(RightClickTarget target)
        {
            foreach (RightClickRule rule in Table)
                if (rule.Target == target)
                    return rule.Order;
            return DefaultOrderKind.None;
        }

        /// <summary>
        /// What the cursor is over. <paramref name="samePlayer"/> is the fog grid's answer for
        /// the clicked actor against the unit taking the order; <paramref name="hostile"/> is
        /// the declared-enemy fallback (`my Hostile`, see <see cref="IsDeclaredEnemy"/>), so a
        /// scene that never wrote a player still has enemies it can attack; and
        /// <paramref name="hidden"/> is fog saying the player cannot see this actor at all.
        /// </summary>
        public static RightClickTarget Classify(bool hasActor, bool hasPoint, bool isBuilding,
            bool samePlayer, bool isResourceNode, bool hostile = false, bool hidden = false)
        {
            if (!hasActor)
                return hasPoint ? RightClickTarget.Ground : RightClickTarget.Nothing;
            // Fog hides a model, not its collider, so a click on an invisible actor still hits
            // something. It reads as bare ground: ordering an attack on what the player cannot
            // see would leak the enemy's position, and a move there is what the click meant.
            if (hidden)
                return hasPoint ? RightClickTarget.Ground : RightClickTarget.Nothing;
            if (isResourceNode)
                return RightClickTarget.ResourceNode;
            if (hostile)
                return RightClickTarget.Enemy;
            if (isBuilding)
                return samePlayer ? RightClickTarget.OwnBuilding : RightClickTarget.Enemy;
            return samePlayer ? RightClickTarget.Friendly : RightClickTarget.Enemy;
        }

        /// <summary>Whether an actor declared itself hostile (`my Hostile`).</summary>
        public static bool IsHostile(EventVariables variables, object actor)
        {
            if (variables == null || actor == null)
                return false;
            return variables.HasLocal(actor, HostileVariable) &&
                   variables.GetLocal(actor, HostileVariable).AsBoolean();
        }

        /// <summary>
        /// Whether <paramref name="other"/> is <paramref name="me"/>'s enemy by declaration
        /// alone: it is hostile and the actor taking the order is not. This is what makes the
        /// default enemy test work in a normal scene, where nobody wrote `my Player` and the
        /// fog grid therefore puts every actor on player 0: the enemy sheets are already
        /// hostile through `enemy type`, so `Worker` + `Zombie` is an attack with no setup.
        /// Two hostile actors stay on one side, so a mob does not order itself to attack itself.
        /// </summary>
        public static bool IsDeclaredEnemy(EventVariables variables, object me, object other)
        {
            if (other == null || ReferenceEquals(me, other))
                return false;
            return IsHostile(variables, other) && !IsHostile(variables, me);
        }

        /// <summary>
        /// Whether an actor is missing health it could get back (`my Health` below
        /// `my MaxHealth`). Read through <see cref="HealthManager"/> so whichever spelling the
        /// sheet wrote (`my Health`, `my HP`) answers; an actor that declared no health at all
        /// is not damaged, which keeps repair off scenery and off older sheets.
        /// </summary>
        public static bool IsDamaged(EventVariables variables, object actor)
        {
            if (variables == null || actor == null)
                return false;
            double max = HealthManager.Read(variables, actor, HealthManager.MaxHealth);
            if (max <= 0)
                return false;
            return HealthManager.Read(variables, actor, HealthManager.Health) < max;
        }

        /// <summary>
        /// The order a right click issues. A standing building sets a rally point instead of
        /// taking an order itself — what comes out of it walks where you clicked — while a
        /// lifted building (Task 28) is airborne and takes the unit rules. Nothing selected is
        /// the no-op the task asks for, and so is a target with no answer in the table.
        /// <paramref name="targetDamaged"/> is whether the clicked ally needs fixing: the table
        /// says repair, so a healthy one is the case this rule overrides — nobody walks across
        /// the map to polish an undamaged unit.
        /// </summary>
        public static DefaultOrderKind Resolve(bool hasSelection, bool buildingSelection,
            bool liftedBuilding, RightClickTarget target, bool targetDamaged = true)
        {
            if (!hasSelection || target == RightClickTarget.Nothing)
                return DefaultOrderKind.None;
            if (buildingSelection && !liftedBuilding)
                return DefaultOrderKind.Rally;
            if (target == RightClickTarget.Friendly && !targetDamaged)
                return DefaultOrderKind.None;
            return RuleFor(target);
        }

        /// <summary>Whether an actor is tagged as a resource node, from the data the economy reads.</summary>
        public static bool IsResourceNode(EventVariables variables, object actor)
        {
            if (variables == null || actor == null)
                return false;
            return variables.HasLocal(actor, ResourceNodeVariable) ||
                   variables.HasLocal(actor, ResourceAmountVariable);
        }

        /// <summary>The canonical name an order of this kind writes into the order queue.</summary>
        public static string OrderName(DefaultOrderKind order)
        {
            switch (order)
            {
                case DefaultOrderKind.Move:
                    return "move";
                case DefaultOrderKind.Attack:
                    return "attack";
                case DefaultOrderKind.Gather:
                    return "gather";
                case DefaultOrderKind.Repair:
                    return "repair";
                case DefaultOrderKind.Rally:
                    return "rally";
                default:
                    return "";
            }
        }

        /// <summary>
        /// Whether a click should start the walk right away. A replacing order always does, even
        /// when the unit is already walking to a previous click: the bridge drops the old path, so
        /// the click retargets the unit instead of being eaten when the first walk arrives. An
        /// appended (Shift) order waits behind a running one, and starts at once when nothing was
        /// running to queue behind.
        /// </summary>
        public static bool StartsWalkNow(bool append, bool hasOrders)
        {
            return !append || !hasOrders;
        }
    }
}
