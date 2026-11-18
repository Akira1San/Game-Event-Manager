using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>The kind of work a unit has been told to do (Task 18 Part E).</summary>
    public enum UnitOrderKind
    {
        None,
        Move,
        AttackMove,
        Attack,
        Build,
        Hold,
        Stop,
    }

    /// <summary>One command in a unit's order queue.</summary>
    public class UnitOrder
    {
        public UnitOrderKind Kind = UnitOrderKind.None;
        /// <summary>Canonical name: <c>move</c>, <c>attack-move</c>, <c>attack</c>, <c>build</c>.</summary>
        public string Name = "";
        /// <summary>The target actor, when the order points at one.</summary>
        public object Target;
        /// <summary>The target's name, or <c>there</c> for a clicked ground position.</summary>
        public string TargetName = "";
        /// <summary>True when <see cref="TargetName"/> is a clicked world position.</summary>
        public bool HasPositionTarget;
        /// <summary>
        /// The clicked point the order was written for, captured when it was given (Task 29
        /// Part C). An order without it falls back to the last click, which is what every
        /// order written before this existed does; a queued or selection-wide order keeps its
        /// own destination instead of following a later click.
        /// </summary>
        public bool HasPoint;
        public double PointX;
        public double PointY;
        public double PointZ;

        /// <summary>Copies the clicked point onto this order (a no-op when it has none).</summary>
        public void SetPoint(double x, double y, double z)
        {
            HasPoint = true;
            PointX = x;
            PointY = y;
            PointZ = z;
        }
    }

    /// <summary>One unit's active order plus the commands waiting behind it.</summary>
    public class UnitOrderQueue
    {
        public readonly List<UnitOrder> Items = new List<UnitOrder>();
        /// <summary><c>stop</c> or <c>hold</c> while the unit is in that state, otherwise empty.</summary>
        public string IdleOrder = "";
        /// <summary>Where newly trained units should walk (a rally point on a building).</summary>
        public object RallyTarget;
        public string RallyTargetName = "";
        /// <summary>The clicked point the rally was set at, captured at set time (Task 29 Part C).</summary>
        public bool RallyHasPoint;
        public double RallyX;
        public double RallyY;
        public double RallyZ;

        public UnitOrder Current => Items.Count > 0 ? Items[0] : null;
    }

    /// <summary>
    /// RTS orders and attack-move (Task 18 Part E): a per-unit FIFO order queue, the
    /// start/complete/path-blocked edges, and building rally points. Pure logic with no
    /// Flax types, so the runtime and the tests drive it directly; the bridge turns orders
    /// into NavMesh movement.
    /// </summary>
    public class OrderManager
    {
        /// <summary>
        /// The reach a unit has before it writes <c>my attack range</c> (Task 21 Part A): close
        /// enough to be a melee hit, so `me is in range of …` means something on day one.
        /// </summary>
        public const double DefaultAttackRange = 50;

        /// <summary>Both spellings the language accepts for the range the sheet declares.</summary>
        private static readonly string[] AttackRangeNames = { "attack range", "AttackRange" };

        private readonly Dictionary<object, UnitOrderQueue> _queues = new Dictionary<object, UnitOrderQueue>();

        /// <summary>
        /// The unit's own <c>my attack range</c>, or 0 when the sheet never declared one. Read
        /// without the unset-variable warning, so probing a sheet that has no range is silent.
        /// </summary>
        public static double DeclaredAttackRange(EventVariables variables, object unit)
        {
            if (variables == null || unit == null)
                return 0;
            foreach (string name in AttackRangeNames)
            {
                if (!variables.HasLocal(unit, name))
                    continue;
                double declared = variables.GetLocal(unit, name).AsNumber();
                if (declared > 0)
                    return declared;
            }
            return 0;
        }

        /// <summary>
        /// The reach a <c>ranged</c> unit has before it declares <c>my attack range</c> (Task 23
        /// Part A) — far enough to be worth calling ranged, near enough to feel like shooting.
        /// </summary>
        public const double DefaultRangedAttackRange = 300;

        /// <summary>The reach of an attack type that declared no range: melee touch, ranged shot.</summary>
        public static double DefaultRangeFor(string attackType)
        {
            return AttackTypes.IsRanged(attackType) ? DefaultRangedAttackRange : DefaultAttackRange;
        }

        /// <summary>
        /// The range an attacking condition asks about: the declared <c>my attack range</c>, else
        /// this unit's own attack type's default (melee 50, ranged 300).
        /// </summary>
        public static double AttackRangeOf(EventContext context)
        {
            if (context == null)
                return DefaultAttackRange;
            double declared = DeclaredAttackRange(context.Variables, context.Me);
            return declared > 0
                ? declared
                : DefaultRangeFor(AttackTypes.TypeOf(context.Variables, context.Me));
        }

        /// <summary>
        /// How far an <c>attack</c> order closes before it stops (Task 23 Part A): the declared
        /// <c>my attack range</c>, else the ranged default for a <c>ranged</c> unit, else 0 — walk
        /// into contact, which is what a melee unit and every older sheet expects.
        /// </summary>
        public static double StopDistanceFor(EventContext context)
        {
            if (context == null)
                return 0;
            double declared = DeclaredAttackRange(context.Variables, context.Me);
            if (declared > 0)
                return declared;
            return AttackTypes.IsRanged(AttackTypes.TypeOf(context.Variables, context.Me))
                ? DefaultRangedAttackRange
                : 0;
        }

        /// <summary>
        /// Where a unit stands to attack a target it cannot reach yet: on the straight line, one
        /// range short of it. A target already inside the range answers the unit's own position,
        /// so an approach never walks past it.
        /// </summary>
        public static void ApproachPoint(double fromX, double fromZ, double targetX, double targetZ,
            double range, out double x, out double z)
        {
            double dx = targetX - fromX;
            double dz = targetZ - fromZ;
            double distance = System.Math.Sqrt(dx * dx + dz * dz);
            if (range <= 0 || distance <= range || distance <= 0.0001)
            {
                x = fromX;
                z = fromZ;
                return;
            }
            double scale = (distance - range) / distance;
            x = fromX + dx * scale;
            z = fromZ + dz * scale;
        }

        /// <summary>True when the unit has an active order or one waiting behind it.</summary>
        public bool HasOrders(object unit)
        {
            return unit != null && _queues.TryGetValue(unit, out UnitOrderQueue queue) && queue.Items.Count > 0;
        }

        /// <summary>The current order's name, <c>stop</c>/<c>hold</c>, or <c>idle</c>.</summary>
        public string CurrentOrderName(object unit)
        {
            if (unit == null || !_queues.TryGetValue(unit, out UnitOrderQueue queue))
                return "idle";
            if (queue.Current != null)
                return queue.Current.Name;
            return string.IsNullOrEmpty(queue.IdleOrder) ? "idle" : queue.IdleOrder;
        }

        /// <summary>The current order's target name, or "" when it has none.</summary>
        public string OrderTargetName(object unit)
        {
            return unit != null && _queues.TryGetValue(unit, out UnitOrderQueue queue) && queue.Current != null
                ? queue.Current.TargetName
                : "";
        }

        /// <summary>The active order object, or null when the unit is idle/stopped/holding.</summary>
        public UnitOrder CurrentOrderOf(object unit)
        {
            return unit != null && _queues.TryGetValue(unit, out UnitOrderQueue queue) ? queue.Current : null;
        }

        /// <summary>Number of orders in the queue, including the active one.</summary>
        public int OrderCount(object unit)
        {
            return unit != null && _queues.TryGetValue(unit, out UnitOrderQueue queue) ? queue.Items.Count : 0;
        }

        /// <summary>The unit's order names, active order first (Task 18 Part H:
        /// <c>GetOrderQueue</c>). Empty when it has no orders.</summary>
        public IReadOnlyList<string> OrderNames(object unit)
        {
            var names = new List<string>();
            if (unit == null || !_queues.TryGetValue(unit, out UnitOrderQueue queue))
                return names;
            foreach (UnitOrder order in queue.Items)
                names.Add(order.Name);
            return names;
        }

        public bool IsHolding(object unit)
        {
            return unit != null && _queues.TryGetValue(unit, out UnitOrderQueue queue) && queue.IdleOrder == "hold";
        }

        /// <summary>
        /// Replaces the unit's orders with <paramref name="order"/> and raises
        /// <c>OrderStarts</c>. A new command cancels anything still running, matching the
        /// usual RTS click behavior.
        /// </summary>
        public void StartOrder(EventVariables variables, EventRuntime runtime, object unit, UnitOrder order)
        {
            if (unit == null || order == null || string.IsNullOrEmpty(order.Name))
                return;
            UnitOrderQueue queue = EnsureQueue(unit);
            queue.Items.Clear();
            queue.IdleOrder = "";
            queue.Items.Add(order);
            Publish(variables, unit, queue);
            Raise(runtime, unit, TriggerKind.OrderStarts, order);
        }

        /// <summary>
        /// Appends <paramref name="order"/> behind the current one (the shift-click queue).
        /// An empty queue starts the unit immediately.
        /// </summary>
        public void AddOrder(EventVariables variables, EventRuntime runtime, object unit, UnitOrder order)
        {
            if (unit == null || order == null || string.IsNullOrEmpty(order.Name))
                return;
            UnitOrderQueue queue = EnsureQueue(unit);
            bool starts = queue.Items.Count == 0;
            queue.IdleOrder = "";
            queue.Items.Add(order);
            Publish(variables, unit, queue);
            if (starts)
                Raise(runtime, unit, TriggerKind.OrderStarts, order);
        }

        /// <summary>
        /// Marks the active order finished, removes it, promotes the next queued order and
        /// raises <c>OrderCompletes</c> (then <c>OrderStarts</c> for the promoted order).
        /// </summary>
        public bool Complete(EventVariables variables, EventRuntime runtime, object unit)
        {
            if (unit == null || !_queues.TryGetValue(unit, out UnitOrderQueue queue) || queue.Items.Count == 0)
                return false;

            UnitOrder finished = queue.Items[0];
            queue.Items.RemoveAt(0);
            Publish(variables, unit, queue);
            Raise(runtime, unit, TriggerKind.OrderCompletes, finished);
            if (queue.Current != null)
                Raise(runtime, unit, TriggerKind.OrderStarts, queue.Current);
            return true;
        }

        /// <summary>
        /// <c>stop</c>: drops current and queued work and leaves the unit idle. The current
        /// order reads <c>stop</c> until a new command arrives.
        /// </summary>
        public void Stop(EventVariables variables, object unit)
        {
            if (unit == null)
                return;
            UnitOrderQueue queue = EnsureQueue(unit);
            queue.Items.Clear();
            queue.IdleOrder = "stop";
            Publish(variables, unit, queue);
        }

        /// <summary><c>hold position</c>: drops queued work and freezes the unit in place.</summary>
        public void Hold(EventVariables variables, object unit)
        {
            if (unit == null)
                return;
            UnitOrderQueue queue = EnsureQueue(unit);
            queue.Items.Clear();
            queue.IdleOrder = "hold";
            Publish(variables, unit, queue);
        }

        /// <summary><c>clear orders</c>: silently drops current and queued work.</summary>
        public void Clear(EventVariables variables, object unit)
        {
            if (unit == null || !_queues.TryGetValue(unit, out UnitOrderQueue queue))
                return;
            queue.Items.Clear();
            queue.IdleOrder = "";
            Publish(variables, unit, queue);
        }

        /// <summary>
        /// <c>when path blocked</c>: the unit could not find a route for its current order.
        /// The order is kept so the author can retry or <c>stop</c>.
        /// </summary>
        public void ReportPathBlocked(EventRuntime runtime, object unit)
        {
            if (unit == null)
                return;
            UnitOrder order = _queues.TryGetValue(unit, out UnitOrderQueue queue) ? queue.Current : null;
            runtime?.RaisePush(unit, new PushSignal
            {
                Kind = TriggerKind.PathBlocked,
                Name = order?.Name ?? "move",
                Other = order?.Target,
            });
        }

        /// <summary><c>set rally point to &lt;target&gt;</c> on a production building.</summary>
        public void SetRallyPoint(object building, object target, string targetName)
        {
            SetRallyPoint(building, target, targetName, false, 0, 0, 0);
        }

        /// <summary>
        /// The right click's rally point (Task 29 Part C): the same state, plus the world point
        /// the click landed on so a unit trained later still walks to the spot the player chose
        /// rather than to wherever the mouse was last clicked.
        /// </summary>
        public void SetRallyPoint(object building, object target, string targetName,
            bool hasPoint, double x, double y, double z)
        {
            if (building == null)
                return;
            UnitOrderQueue queue = EnsureQueue(building);
            queue.RallyTarget = target;
            queue.RallyTargetName = targetName ?? "";
            queue.RallyHasPoint = hasPoint;
            queue.RallyX = x;
            queue.RallyY = y;
            queue.RallyZ = z;
        }

        /// <summary>Whether the rally point carries a clicked world position.</summary>
        public bool RallyHasPointOf(object building)
        {
            return building != null && _queues.TryGetValue(building, out UnitOrderQueue queue) &&
                   queue.RallyHasPoint;
        }

        /// <summary>The clicked point a rally was set at; zeroes when it was set to an actor.</summary>
        public void RallyPointPositionOf(object building, out double x, out double y, out double z)
        {
            x = 0;
            y = 0;
            z = 0;
            if (building == null || !_queues.TryGetValue(building, out UnitOrderQueue queue))
                return;
            x = queue.RallyX;
            y = queue.RallyY;
            z = queue.RallyZ;
        }

        public object RallyPointOf(object building)
        {
            return building != null && _queues.TryGetValue(building, out UnitOrderQueue queue) ? queue.RallyTarget : null;
        }

        public string RallyTargetNameOf(object building)
        {
            return building != null && _queues.TryGetValue(building, out UnitOrderQueue queue) ? queue.RallyTargetName : "";
        }

        public bool HasRallyPoint(object building)
        {
            return building != null && _queues.TryGetValue(building, out UnitOrderQueue queue) &&
                   (queue.RallyTarget != null || !string.IsNullOrEmpty(queue.RallyTargetName));
        }

        /// <summary>Drops a destroyed unit/building's orders and rally point.</summary>
        public void Destroyed(object actor)
        {
            if (actor != null)
                _queues.Remove(actor);
        }

        /// <summary>Clears every order for a scene restart / new session.</summary>
        public void Reset()
        {
            _queues.Clear();
        }

        private UnitOrderQueue EnsureQueue(object unit)
        {
            if (!_queues.TryGetValue(unit, out UnitOrderQueue queue))
            {
                queue = new UnitOrderQueue();
                _queues[unit] = queue;
            }
            return queue;
        }

        /// <summary>Mirrors the state the language exposes as <c>my current order</c>,
        /// <c>my order target</c> and <c>my order queue</c>.</summary>
        private void Publish(EventVariables variables, object unit, UnitOrderQueue queue)
        {
            if (variables == null)
                return;
            variables.SetLocal(unit, "current order", ValueBox.OfText(CurrentOrderName(unit)));
            variables.SetLocal(unit, "order target", ValueBox.OfText(OrderTargetName(unit)));
            variables.SetLocal(unit, "order queue", ValueBox.OfNumber(queue.Items.Count));
        }

        private static void Raise(EventRuntime runtime, object unit, TriggerKind kind, UnitOrder order)
        {
            runtime?.RaisePush(unit, new PushSignal
            {
                Kind = kind,
                Name = order.Name,
                Other = order.Target,
            });
        }
    }
}
