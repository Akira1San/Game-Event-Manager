using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One unit in a building's training queue.</summary>
    public class ProductionOrder
    {
        public string Unit = "";
        /// <summary>Seconds the unit takes to train; <c>set build time</c> can change it.</summary>
        public double Duration;
        public double Elapsed;
    }

    /// <summary>The training queue of one building, oldest order first.</summary>
    public class ProductionQueue
    {
        public string Building = "";
        public readonly List<ProductionOrder> Items = new List<ProductionOrder>();
        /// <summary>An explicit <c>set build time</c> for future orders, or null to use data.</summary>
        public double? DefaultDuration;

        public bool IsEmpty => Items.Count == 0;
        public ProductionOrder Current => Items.Count > 0 ? Items[0] : null;
    }

    /// <summary>
    /// RTS production queues (Task 18 Part D): a per-building training queue, the start/finish
    /// edges, and the empty-queue edge. Pure logic with no Flax types, so the event runtime and
    /// the tests drive it directly.
    /// </summary>
    public class ProductionManager
    {
        /// <summary>Seconds a unit takes when no build-time variable says otherwise.</summary>
        public const double DefaultBuildTime = 5.0;

        /// <summary>Global that overrides <see cref="DefaultBuildTime"/> for every unit.</summary>
        public const string DefaultBuildTimeVariable = "ProductionBuildTime";

        /// <summary>
        /// How many orders one building's queue holds when no cap variable says otherwise
        /// (Task 29 Part E). The cap is what a command card's Train button greys out at.
        /// </summary>
        public const double DefaultQueueCap = 5;

        /// <summary>Global that overrides <see cref="DefaultQueueCap"/> for every building.</summary>
        public const string DefaultQueueCapVariable = "ProductionQueueCap";

        private readonly Dictionary<object, ProductionQueue> _queues = new Dictionary<object, ProductionQueue>();
        /// <summary>Buildings whose work is on hold (a lifted-off building, Task 28 Part C).
        /// A paused queue keeps its orders and its elapsed time; it just stops advancing.</summary>
        private readonly HashSet<object> _paused = new HashSet<object>();
        private double _lastTime;
        private bool _hasTime;

        /// <summary>True when this actor has ever been used as a production building, even
        /// after its queue drained. That is what <c>me has build queue</c> reports.</summary>
        public bool HasQueue(object building)
        {
            return building != null && _queues.ContainsKey(building);
        }

        public int QueueCount(object building)
        {
            return building != null && _queues.TryGetValue(building, out ProductionQueue queue) ? queue.Items.Count : 0;
        }

        /// <summary>True when nothing is being trained. A building that never trained anything
        /// also counts as idle; <see cref="HasQueue"/> distinguishes the two.</summary>
        public bool IsIdle(object building)
        {
            return building == null || !_queues.TryGetValue(building, out ProductionQueue queue) || queue.IsEmpty;
        }

        public string CurrentUnit(object building)
        {
            return building != null && _queues.TryGetValue(building, out ProductionQueue queue) && queue.Current != null
                ? queue.Current.Unit
                : "";
        }

        /// <summary>Seconds the current order still needs, or 0 when the queue is empty.</summary>
        public double RemainingOf(object building)
        {
            if (building == null || !_queues.TryGetValue(building, out ProductionQueue queue) || queue.Current == null)
                return 0;
            return Math.Max(0, queue.Current.Duration - queue.Current.Elapsed);
        }

        /// <summary>
        /// How long the current order takes in total (what <c>my train time</c> reads), or 0 when
        /// the queue is empty — the number a card shows next to the unit's icon.
        /// </summary>
        public double CurrentDurationOf(object building)
        {
            if (building == null || !_queues.TryGetValue(building, out ProductionQueue queue) || queue.Current == null)
                return 0;
            return Math.Max(0, queue.Current.Duration);
        }

        /// <summary>
        /// The queue cap for one building: <c>&lt;Building&gt;QueueCap</c>, then
        /// <see cref="DefaultQueueCapVariable"/>, then <see cref="DefaultQueueCap"/> — the same
        /// data-first order the build time uses, so a game tunes both the same way.
        /// </summary>
        public static double ResolveQueueCap(EventVariables variables, string buildingName)
        {
            if (!string.IsNullOrEmpty(buildingName) &&
                TryNumber(variables, buildingName + "QueueCap", out double cap))
                return Math.Max(0, cap);
            if (TryNumber(variables, DefaultQueueCapVariable, out cap))
                return Math.Max(0, cap);
            return DefaultQueueCap;
        }

        /// <summary>The queue cap of the building <paramref name="building"/> trains at.</summary>
        public double QueueCapOf(EventVariables variables, object building)
        {
            string name = building != null && _queues.TryGetValue(building, out ProductionQueue queue)
                ? queue.Building
                : "";
            return ResolveQueueCap(variables, name);
        }

        /// <summary>
        /// True while the building's queue has room for one more order — what <c>queue has room</c>
        /// asks, so a card's Train button and its hotkey share one gate.
        /// </summary>
        public bool CanEnqueue(EventVariables variables, object building)
        {
            if (building == null)
                return false;
            return QueueCount(building) < QueueCapOf(variables, building);
        }

        /// <summary>
        /// Adds <paramref name="unit"/> to the building's queue. Starting a unit that was idle
        /// raises <c>TrainingStarted</c> immediately; the completion edge comes from
        /// <see cref="Tick"/>. Returns false when the order was refused, which today only happens
        /// at the queue cap (Task 29 Part E) — the command card's train button greys out at the
        /// same number.
        /// </summary>
        public bool Enqueue(EventVariables variables, EventRuntime runtime, object building,
                            string buildingName, string unit)
        {
            if (building == null || string.IsNullOrEmpty(unit))
                return false;

            ProductionQueue queue = EnsureQueue(building, buildingName);
            if (queue.Items.Count >= QueueCapOf(variables, building))
                return false;
            double duration = queue.DefaultDuration ?? ResolveBuildTime(variables, unit, queue.Building);
            queue.Items.Add(new ProductionOrder { Unit = unit, Duration = Math.Max(0, duration) });
            if (queue.Items.Count == 1)
                RaiseStarted(runtime, building, queue, unit);
            return true;
        }

        /// <summary>
        /// <c>set build time to &lt;n&gt; seconds</c>: changes the current unit's remaining time
        /// and the default for later orders. With no queue yet the value still sticks, so a
        /// building can be tuned before its first <c>train</c>.
        /// </summary>
        public void SetBuildTime(EventVariables variables, object building, string buildingName, double seconds)
        {
            if (building == null)
                return;
            ProductionQueue queue = EnsureQueue(building, buildingName);
            seconds = Math.Max(0, seconds);
            queue.DefaultDuration = seconds;
            if (queue.Current != null)
                queue.Current.Duration = seconds;
        }

        /// <summary>
        /// Cancels the matching order (case-insensitive unit name; an empty name means the
        /// current order). Cancelling the active unit starts the next one or raises
        /// <c>ProductionQueueEmpty</c> when that was the last order.
        /// </summary>
        public bool Cancel(EventRuntime runtime, object building, string unit)
        {
            if (building == null || !_queues.TryGetValue(building, out ProductionQueue queue) || queue.IsEmpty)
                return false;

            int index = 0;
            if (!string.IsNullOrEmpty(unit))
            {
                index = -1;
                for (int i = 0; i < queue.Items.Count; i++)
                {
                    if (string.Equals(queue.Items[i].Unit, unit, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
                if (index < 0)
                    return false;
            }

            queue.Items.RemoveAt(index);
            if (index != 0)
                return true;

            if (queue.Items.Count > 0)
                RaiseStarted(runtime, building, queue, queue.Items[0].Unit);
            else
                RaiseEmpty(runtime, building, queue);
            return true;
        }

        /// <summary>Holds or releases a building's training without dropping anything it queued
        /// (<c>lift off</c> / <c>land</c>).</summary>
        public void SetPaused(object building, bool paused)
        {
            if (building == null)
                return;
            if (paused)
                _paused.Add(building);
            else
                _paused.Remove(building);
        }

        public bool IsPaused(object building)
        {
            return building != null && _paused.Contains(building);
        }

        /// <summary>Drops a destroyed building's queue without raising any edge signals.</summary>
        public void Destroyed(object building)
        {
            if (building != null)
            {
                _queues.Remove(building);
                _paused.Remove(building);
            }
        }

        /// <summary>Clears every queue for a scene restart / new session.</summary>
        public void Reset()
        {
            _queues.Clear();
            _paused.Clear();
            _lastTime = 0;
            _hasTime = false;
        }

        /// <summary>
        /// Advances the active order by the time since the previous tick. Every finished unit
        /// raises <c>TrainingFinished</c>; the next queued unit raises <c>TrainingStarted</c>;
        /// draining the last unit raises <c>ProductionQueueEmpty</c>. Called at the top of
        /// <see cref="EventRuntime.Tick"/>, before push signals are drained.
        /// </summary>
        public void Tick(EventRuntime runtime, double time)
        {
            double delta = _hasTime ? Math.Max(0, time - _lastTime) : 0;
            _lastTime = time;
            _hasTime = true;
            if (delta <= 0)
                return;

            List<(object Actor, ProductionQueue Queue, string Unit)> finished = null;
            List<(object Actor, ProductionQueue Queue, string Unit)> started = null;
            List<(object Actor, ProductionQueue Queue)> emptied = null;

            foreach (var pair in _queues)
            {
                ProductionQueue queue = pair.Value;
                ProductionOrder order = queue.Current;
                if (order == null)
                    continue;
                // A lifted-off building's work waits: nothing is lost, no time passes.
                if (_paused.Contains(pair.Key))
                    continue;
                order.Elapsed += delta;
                if (order.Elapsed < order.Duration)
                    continue;

                queue.Items.RemoveAt(0);
                (finished ??= new List<(object, ProductionQueue, string)>()).Add((pair.Key, queue, order.Unit));
                if (queue.Items.Count > 0)
                    (started ??= new List<(object, ProductionQueue, string)>()).Add((pair.Key, queue, queue.Items[0].Unit));
                else
                    (emptied ??= new List<(object, ProductionQueue)>()).Add((pair.Key, queue));
            }

            if (finished != null)
                foreach (var (actor, queue, unit) in finished)
                    RaiseFinished(runtime, actor, queue, unit);
            if (started != null)
                foreach (var (actor, queue, unit) in started)
                    RaiseStarted(runtime, actor, queue, unit);
            if (emptied != null)
                foreach (var (actor, queue) in emptied)
                    RaiseEmpty(runtime, actor, queue);
        }

        /// <summary>
        /// The data-driven training time: <c>&lt;Unit&gt;BuildTime</c>, then
        /// <c>&lt;Building&gt;BuildTime</c>, then <see cref="DefaultBuildTimeVariable"/>, then
        /// <see cref="DefaultBuildTime"/>.
        /// </summary>
        public static double ResolveBuildTime(EventVariables variables, string unit, string building)
        {
            if (TryNumber(variables, unit + "BuildTime", out double seconds))
                return seconds;
            if (TryNumber(variables, building + "BuildTime", out seconds))
                return seconds;
            if (TryNumber(variables, DefaultBuildTimeVariable, out seconds))
                return seconds;
            return DefaultBuildTime;
        }

        private ProductionQueue EnsureQueue(object building, string buildingName)
        {
            if (!_queues.TryGetValue(building, out ProductionQueue queue))
            {
                queue = new ProductionQueue { Building = buildingName ?? "" };
                _queues[building] = queue;
            }
            else if (string.IsNullOrEmpty(queue.Building) && !string.IsNullOrEmpty(buildingName))
            {
                queue.Building = buildingName;
            }
            return queue;
        }

        private static bool TryNumber(EventVariables variables, string name, out double value)
        {
            value = 0;
            if (variables == null || !variables.HasGlobal(name))
                return false;
            value = variables.GetGlobal(name).AsNumber();
            return true;
        }

        private static void RaiseStarted(EventRuntime runtime, object building, ProductionQueue queue, string unit)
        {
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.TrainingStarted,
                Name = unit,
                Other = building,
                Material = queue.Building,
            });
        }

        private static void RaiseFinished(EventRuntime runtime, object building, ProductionQueue queue, string unit)
        {
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.TrainingFinished,
                Name = unit,
                Other = building,
                Material = queue.Building,
            });
        }

        private static void RaiseEmpty(EventRuntime runtime, object building, ProductionQueue queue)
        {
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.ProductionQueueEmpty,
                Name = queue.Building,
                Other = building,
            });
        }
    }
}
