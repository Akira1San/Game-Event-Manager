using System;
using System.Collections.Generic;
using System.Text;

namespace Game
{
    /// <summary>One upgrade in a building's research queue.</summary>
    public class ResearchOrder
    {
        /// <summary>The upgrade as the sheet wrote it (<c>"Stim Pack"</c>), matched against
        /// <c>when … finishes researching …</c> signals.</summary>
        public string Upgrade = "";
        /// <summary>The whitespace-free spelling used for per-player level globals
        /// (<c>Player2 StimPack</c>) and the data table.</summary>
        public string LevelName = "";
        public double Duration;
        public double Elapsed;
        /// <summary>The player the upgrade belongs to (0 is <c>Player1</c>).</summary>
        public int Player;
        /// <summary>What was actually paid, per resource, so a cancel refunds exactly that.</summary>
        public Dictionary<string, double> Paid = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The research queue of one building, oldest order first.</summary>
    public class ResearchQueue
    {
        public string Building = "";
        public readonly List<ResearchOrder> Items = new List<ResearchOrder>();

        public bool IsEmpty => Items.Count == 0;
        public ResearchOrder Current => Items.Count > 0 ? Items[0] : null;
    }

    /// <summary>Why <see cref="ResearchManager.Enqueue"/> accepted or refused an order.</summary>
    public enum ResearchStartResult
    {
        Started,
        /// <summary>No building, or no upgrade name.</summary>
        Invalid,
        /// <summary>The upgrade's <c>Requires</c> names a different building.</summary>
        WrongBuilding,
        AlreadyQueued,
        AtMaxLevel,
        CannotAfford,
    }

    /// <summary>One row of <c>Upgrades.events</c>.</summary>
    public class UpgradeData
    {
        /// <summary>The name as authored (<c>StimPack</c>).</summary>
        public string Name = "";
        /// <summary>Declared <c>&lt;Upgrade&gt;Time</c>, in seconds, when the file sets it.</summary>
        public double? Time;
        /// <summary>Declared <c>&lt;Upgrade&gt;Cost</c>, charged to the default cost resource.</summary>
        public double? Cost;
        /// <summary>Declared <c>&lt;Upgrade&gt;Requires</c>: the building that may research it.</summary>
        public string Requires = "";
        /// <summary>Declared <c>&lt;Upgrade&gt;Levels</c> / <c>MaxLevel</c>; 0 means unlimited.</summary>
        public double MaxLevel = 1;
    }

    /// <summary>
    /// RTS upgrades are data (Task 27 Part D).
    ///
    /// <c>GameFiles/Upgrades.events</c> declares one upgrade per block of globals, read at boot
    /// the same way <c>Matchups.events</c> is:
    ///
    /// <code>
    /// set StimPackTime to 30
    /// set StimPackCost to 100
    /// set StimPackRequires to "Barracks"
    /// set StimPackLevels to 1
    /// </code>
    ///
    /// Nothing in the file runs — the loader reads the literals — so a scene that never attaches
    /// the sheet still gets the numbers. Every value is overridable by the live global of the same
    /// name, and a missing file leaves no upgrades: a scene with no research behaves exactly as
    /// before this file existed.
    /// </summary>
    public static class Upgrades
    {
        /// <summary>The data file's name, like <c>Matchups.events</c>.</summary>
        public const string FileName = "Upgrades";

        /// <summary>Seconds an upgrade takes when no time variable or file row says otherwise.</summary>
        public const double DefaultResearchTime = 20.0;

        /// <summary>Global that overrides every research time.</summary>
        public const string DefaultTimeVariable = "ResearchTime";

        /// <summary>Suffixes the data file uses, longest first so <c>MaxLevel</c> beats <c>Level</c>.</summary>
        private static readonly string[] Suffixes = { "MaxLevel", "Levels", "Requires", "Time", "Cost", "Level" };

        private static readonly Dictionary<string, UpgradeData> Rows =
            new Dictionary<string, UpgradeData>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every upgrade the file declared, in file order.</summary>
        public static IReadOnlyList<UpgradeData> All
        {
            get
            {
                var rows = new List<UpgradeData>(Rows.Count);
                foreach (var pair in Rows)
                    rows.Add(pair.Value);
                return rows;
            }
        }

        /// <summary>The whitespace-free spelling used for globals and level names.</summary>
        public static string KeyOf(string upgrade)
        {
            if (string.IsNullOrWhiteSpace(upgrade))
                return "";
            if (Rows.TryGetValue(key(upgrade), out UpgradeData row))
                return row.Name;
            var builder = new StringBuilder(upgrade.Length);
            foreach (char c in upgrade.Trim())
                if (!char.IsWhiteSpace(c))
                    builder.Append(c);
            return builder.ToString();
        }

        /// <summary>The upgrade's own name as authored/typed, used in signals and warnings.</summary>
        public static string DisplayName(string upgrade)
        {
            return string.IsNullOrWhiteSpace(upgrade) ? "" : upgrade.Trim();
        }

        /// <summary>The declared row, when the file has one.</summary>
        public static bool TryGet(string upgrade, out UpgradeData row)
        {
            row = null;
            return !string.IsNullOrWhiteSpace(upgrade) && Rows.TryGetValue(key(upgrade), out row);
        }

        /// <summary>How long the upgrade takes: live <c>&lt;Upgrade&gt;Time</c>, then the file, then
        /// <c>&lt;Building&gt;ResearchTime</c>, then <c>ResearchTime</c>, then the default.</summary>
        public static double DurationOf(EventVariables variables, string upgrade, string building)
        {
            string name = KeyOf(upgrade);
            if (TryNumber(variables, name + "Time", out double seconds))
                return Math.Max(0, seconds);
            if (TryGet(upgrade, out UpgradeData row) && row.Time.HasValue)
                return Math.Max(0, row.Time.Value);
            if (!string.IsNullOrEmpty(building) &&
                TryNumber(variables, building + "ResearchTime", out seconds))
                return Math.Max(0, seconds);
            if (TryNumber(variables, DefaultTimeVariable, out seconds))
                return Math.Max(0, seconds);
            return DefaultResearchTime;
        }

        /// <summary>The cost the file declares for the default cost resource (0 when none).</summary>
        public static double DeclaredCostOf(string upgrade)
        {
            return TryGet(upgrade, out UpgradeData row) && row.Cost.HasValue
                ? Math.Max(0, row.Cost.Value)
                : 0;
        }

        /// <summary>The building that may research this upgrade (empty for any building).</summary>
        public static string RequiresOf(string upgrade)
        {
            return TryGet(upgrade, out UpgradeData row) ? row.Requires : "";
        }

        /// <summary>Highest level research may reach; 0 means unlimited, and a row nobody wrote
        /// defaults to a single level.</summary>
        public static double MaxLevelOf(string upgrade)
        {
            if (!TryGet(upgrade, out UpgradeData row))
                return 1;
            return row.MaxLevel <= 0 ? 0 : row.MaxLevel;
        }

        /// <summary>
        /// Reads the rows out of <c>Upgrades.events</c>. Called once per load/reload by the bridge;
        /// pass <c>null</c> to forget the file and leave every scene untouched.
        /// </summary>
        public static void LoadData(ProgramFile file)
        {
            Rows.Clear();
            if (file == null)
                return;

            foreach (EventNode ev in file.Events)
            {
                foreach (BodyLineNode line in ev.Body)
                {
                    if (!(line.Statement is SetNode set) || set.Op != '\0')
                        continue;
                    if (!string.IsNullOrEmpty(set.Target.Owner))
                        continue;
                    if (!TrySplit(set.Target.Name, out string name, out string field))
                        continue;
                    if (!TryLiteral(set.Value, out ValueBox value))
                        continue;

                    if (!Rows.TryGetValue(key(name), out UpgradeData row))
                    {
                        row = new UpgradeData { Name = name };
                        Rows[key(name)] = row;
                    }

                    switch (field)
                    {
                        case "Time":
                            row.Time = value.AsNumber();
                            break;
                        case "Cost":
                            row.Cost = value.AsNumber();
                            break;
                        case "Requires":
                            row.Requires = value.AsText();
                            break;
                        case "MaxLevel":
                        case "Levels":
                        case "Level":
                            row.MaxLevel = value.AsNumber();
                            break;
                    }
                }
            }
        }

        /// <summary>Forgets the file; the tests and a reload with no file both use this.</summary>
        public static void Reset()
        {
            Rows.Clear();
        }

        /// <summary>Splits <c>StimPackTime</c> into <c>StimPack</c> and <c>Time</c>.</summary>
        private static bool TrySplit(string variable, out string name, out string field)
        {
            name = "";
            field = "";
            if (string.IsNullOrWhiteSpace(variable))
                return false;
            foreach (string suffix in Suffixes)
            {
                if (variable.Length <= suffix.Length ||
                    !variable.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                name = variable.Substring(0, variable.Length - suffix.Length);
                field = suffix == "Level" ? "Levels" : suffix;
                return name.Trim().Length > 0;
            }
            return false;
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

        private static bool TryNumber(EventVariables variables, string name, out double value)
        {
            value = 0;
            if (variables == null || string.IsNullOrEmpty(name) || !variables.HasGlobal(name))
                return false;
            value = variables.GetGlobal(name).AsNumber();
            return true;
        }

        private static string key(string name)
        {
            var builder = new StringBuilder(name == null ? 0 : name.Length);
            if (!string.IsNullOrEmpty(name))
                foreach (char c in name)
                    if (!char.IsWhiteSpace(c))
                        builder.Append(c);
            return builder.ToString();
        }
    }

    /// <summary>
    /// RTS research queues (Task 27 Part D): a per-building upgrade queue that mirrors
    /// <see cref="ProductionManager"/> — start/finish/empty edges, a cancel that refunds, progress
    /// a HUD can bind, and costs charged through <see cref="ResourceManager"/> against the
    /// building's owner bank. Pure logic with no Flax types, so the runtime and the tests drive it
    /// directly.
    ///
    /// Finishing applies the upgrade through <see cref="Tech"/>: <c>Player2 StimPack</c> goes up by
    /// one, so an upgrade researched by one side never raises the other's tech. A scene that never
    /// writes <c>research</c> keeps every existing sheet's hand-rolled <c>wait</c> upgrade working.
    /// </summary>
    public class ResearchManager
    {
        /// <summary>How much of what was paid comes back on a cancel; 1 is a full refund.</summary>
        public const string RefundVariable = "ResearchRefund";

        /// <summary>0–100 progress written on the building while it researches.</summary>
        public const string ProgressVariable = "ResearchProgress";

        /// <summary>The upgrade being researched, written on the building.</summary>
        public const string NameVariable = "ResearchName";

        private readonly Dictionary<object, ResearchQueue> _queues = new Dictionary<object, ResearchQueue>();
        private double _lastTime;
        private bool _hasTime;

        /// <summary>True when this actor has ever been used to research, even after its queue
        /// drained.</summary>
        public bool HasQueue(object building)
        {
            return building != null && _queues.ContainsKey(building);
        }

        public int QueueCount(object building)
        {
            return building != null && _queues.TryGetValue(building, out ResearchQueue queue) ? queue.Items.Count : 0;
        }

        /// <summary>True when nothing is being researched. A building that never researched also
        /// counts as idle; <see cref="HasQueue"/> tells the two apart.</summary>
        public bool IsIdle(object building)
        {
            return building == null || !_queues.TryGetValue(building, out ResearchQueue queue) || queue.IsEmpty;
        }

        public string CurrentUpgrade(object building)
        {
            return building != null && _queues.TryGetValue(building, out ResearchQueue queue) && queue.Current != null
                ? queue.Current.Upgrade
                : "";
        }

        /// <summary>True when that upgrade is already waiting in this building's queue.</summary>
        public bool IsQueued(object building, string upgrade)
        {
            if (building == null || !_queues.TryGetValue(building, out ResearchQueue queue))
                return false;
            string wanted = Upgrades.KeyOf(upgrade);
            foreach (ResearchOrder order in queue.Items)
                if (string.Equals(Upgrades.KeyOf(order.Upgrade), wanted, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>Seconds the active upgrade still needs, or 0 when the queue is empty.</summary>
        public double Remaining(object building)
        {
            if (building == null || !_queues.TryGetValue(building, out ResearchQueue queue) || queue.Current == null)
                return 0;
            return Math.Max(0, queue.Current.Duration - queue.Current.Elapsed);
        }

        /// <summary>Alias of <see cref="Remaining"/>, named like the production manager's reader.</summary>
        public double RemainingOf(object building)
        {
            return Remaining(building);
        }

        /// <summary>Active research progress 0–100 (0 when idle), the same scale
        /// <c>ConstructionManager.ProgressOf</c> uses.</summary>
        public double ProgressOf(object building)
        {
            if (building == null || !_queues.TryGetValue(building, out ResearchQueue queue) || queue.Current == null)
                return 0;
            ResearchOrder order = queue.Current;
            if (order.Duration <= 0)
                return 100;
            double percent = order.Elapsed / order.Duration * 100;
            return percent < 0 ? 0 : percent > 100 ? 100 : percent;
        }

        /// <summary>
        /// Adds <paramref name="upgrade"/> to the building's queue, charging its cost up front so a
        /// cancel can refund and the bank cannot be spent twice. Starting an idle building raises
        /// <c>ResearchStarted</c> immediately; <see cref="Tick"/> raises the finish.
        /// </summary>
        public ResearchStartResult Enqueue(EventVariables variables, EventRuntime runtime, object building,
                                           string buildingName, string upgrade, int player)
        {
            if (building == null || string.IsNullOrWhiteSpace(upgrade))
                return ResearchStartResult.Invalid;

            ResearchQueue queue = EnsureQueue(building, buildingName);
            string display = Upgrades.DisplayName(upgrade);
            string levelName = Upgrades.KeyOf(upgrade);

            string requires = Upgrades.RequiresOf(upgrade);
            if (requires.Length > 0 &&
                !string.Equals(requires.Trim(), queue.Building, StringComparison.OrdinalIgnoreCase))
                return ResearchStartResult.WrongBuilding;

            if (IsQueued(building, upgrade))
                return ResearchStartResult.AlreadyQueued;

            double maxLevel = Upgrades.MaxLevelOf(upgrade);
            if (maxLevel > 0 && Tech.LevelOf(variables, player, levelName) >= maxLevel)
                return ResearchStartResult.AtMaxLevel;

            List<(string Resource, double Amount)> costs = ResolveCosts(variables, upgrade);
            if (!CanPay(variables, player, costs))
                return ResearchStartResult.CannotAfford;

            var order = new ResearchOrder
            {
                Upgrade = display,
                LevelName = levelName,
                Duration = Upgrades.DurationOf(variables, upgrade, queue.Building),
                Player = player < 0 ? 0 : player,
            };
            order.Paid = Pay(variables, player, costs);
            queue.Items.Add(order);
            if (queue.Items.Count == 1)
                RaiseStarted(runtime, building, queue, order);
            WriteProgress(variables, building, queue);
            return ResearchStartResult.Started;
        }

        /// <summary>
        /// Cancels the matching upgrade (case-insensitive; an empty name means the active one) and
        /// refunds what was paid for it, scaled by <c>ResearchRefund</c>. Cancelling the active
        /// order starts the next one or raises <c>ResearchQueueEmpty</c>.
        /// </summary>
        public bool Cancel(EventVariables variables, EventRuntime runtime, object building, string upgrade)
        {
            if (building == null || !_queues.TryGetValue(building, out ResearchQueue queue) || queue.IsEmpty)
                return false;

            int index = 0;
            if (!string.IsNullOrWhiteSpace(upgrade))
            {
                string wanted = Upgrades.KeyOf(upgrade);
                index = -1;
                for (int i = 0; i < queue.Items.Count; i++)
                    if (string.Equals(Upgrades.KeyOf(queue.Items[i].Upgrade), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                if (index < 0)
                    return false;
            }

            ResearchOrder order = queue.Items[index];
            queue.Items.RemoveAt(index);
            Refund(variables, order);
            if (index == 0)
            {
                if (queue.Items.Count > 0)
                {
                    RaiseStarted(runtime, building, queue, queue.Items[0]);
                    WriteProgress(variables, building, queue);
                }
                else
                {
                    ClearProgress(variables, building);
                    RaiseEmpty(runtime, building, queue, order.Player);
                }
            }
            return true;
        }

        /// <summary>Drops a destroyed building's queue without refunding or raising edges — the
        /// investment goes with the building, exactly like a lost training queue.</summary>
        public void Destroyed(object building)
        {
            if (building != null)
                _queues.Remove(building);
        }

        /// <summary>Clears every queue for a scene restart / new session.</summary>
        public void Reset()
        {
            _queues.Clear();
            _lastTime = 0;
            _hasTime = false;
        }

        /// <summary>
        /// Advances the active order by the time since the previous tick. A finished upgrade raises
        /// <c>ResearchFinished</c> after applying its level, the next order raises
        /// <c>ResearchStarted</c>, and draining the last one raises <c>ResearchQueueEmpty</c>.
        /// Called at the top of <see cref="EventRuntime.Tick"/>, before push signals are drained.
        /// </summary>
        public void Tick(EventRuntime runtime, double time)
        {
            double delta = _hasTime ? Math.Max(0, time - _lastTime) : 0;
            _lastTime = time;
            _hasTime = true;
            if (delta <= 0)
                return;

            EventVariables variables = runtime.Variables;
            List<(object Actor, ResearchQueue Queue, ResearchOrder Order)> finished = null;
            List<(object Actor, ResearchQueue Queue, ResearchOrder Order)> started = null;
            List<(object Actor, ResearchQueue Queue, int Player)> emptied = null;

            foreach (var pair in _queues)
            {
                ResearchQueue queue = pair.Value;
                ResearchOrder order = queue.Current;
                if (order == null)
                    continue;
                order.Elapsed += delta;
                if (order.Elapsed < order.Duration)
                {
                    WriteProgress(variables, pair.Key, queue);
                    continue;
                }

                queue.Items.RemoveAt(0);
                (finished ??= new List<(object, ResearchQueue, ResearchOrder)>()).Add((pair.Key, queue, order));
                if (queue.Items.Count > 0)
                    (started ??= new List<(object, ResearchQueue, ResearchOrder)>()).Add((pair.Key, queue, queue.Items[0]));
                else
                    (emptied ??= new List<(object, ResearchQueue, int)>()).Add((pair.Key, queue, order.Player));
            }

            if (finished != null)
                foreach (var (actor, queue, order) in finished)
                {
                    ApplyLevel(variables, order);
                    RaiseFinished(runtime, actor, queue, order);
                    if (queue.IsEmpty)
                        ClearProgress(variables, actor);
                    else
                        WriteProgress(variables, actor, queue);
                }
            if (started != null)
                foreach (var (actor, queue, order) in started)
                    RaiseStarted(runtime, actor, queue, order);
            if (emptied != null)
                foreach (var (actor, queue, player) in emptied)
                    RaiseEmpty(runtime, actor, queue, player);
        }

        private ResearchQueue EnsureQueue(object building, string buildingName)
        {
            if (!_queues.TryGetValue(building, out ResearchQueue queue))
            {
                queue = new ResearchQueue { Building = buildingName ?? "" };
                _queues[building] = queue;
            }
            else if (string.IsNullOrEmpty(queue.Building) && !string.IsNullOrEmpty(buildingName))
            {
                queue.Building = buildingName;
            }
            return queue;
        }

        /// <summary>Raises one player's level by one, never past the upgrade's cap.</summary>
        private static void ApplyLevel(EventVariables variables, ResearchOrder order)
        {
            double current = Tech.LevelOf(variables, order.Player, order.LevelName);
            double next = current + 1;
            double maxLevel = Upgrades.MaxLevelOf(order.Upgrade);
            if (maxLevel > 0 && next > maxLevel)
                next = maxLevel;
            Tech.SetLevel(variables, order.Player, order.LevelName, next);
        }

        /// <summary>
        /// The upgrade's price: a live <c>&lt;Upgrade&gt;&lt;Resource&gt;Cost</c> global per resource,
        /// plus the file's declared <c>Cost</c> when the default resource has no live price.
        /// </summary>
        private static List<(string Resource, double Amount)> ResolveCosts(EventVariables variables, string upgrade)
        {
            string name = Upgrades.KeyOf(upgrade);
            var costs = ResourceManager.CostsOf(variables, name);
            double declared = Upgrades.DeclaredCostOf(upgrade);
            if (declared <= 0)
                return costs;

            string defaultResource = ResourceManager.DefaultCostName;
            foreach (var cost in costs)
                if (string.Equals(cost.Resource, defaultResource, StringComparison.OrdinalIgnoreCase))
                    return costs;

            costs.Add((defaultResource, declared));
            return costs;
        }

        private static bool CanPay(EventVariables variables, int player, List<(string Resource, double Amount)> costs)
        {
            foreach (var (resource, amount) in costs)
            {
                if (amount <= 0)
                    continue;
                if (ResourceManager.BankValue(variables, player, resource) < amount)
                    return false;
            }
            return true;
        }

        /// <summary>Spends the price and returns exactly what each bank lost.</summary>
        private static Dictionary<string, double> Pay(EventVariables variables, int player,
                                                     List<(string Resource, double Amount)> costs)
        {
            var paid = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (resource, amount) in costs)
            {
                if (amount <= 0)
                    continue;
                ResourceManager.WriteBank(variables, player, resource,
                    ResourceManager.BankValue(variables, player, resource) - amount);
                paid[resource] = amount;
            }
            return paid;
        }

        private static void Refund(EventVariables variables, ResearchOrder order)
        {
            if (variables == null || order == null || order.Paid.Count == 0)
                return;
            double factor = 1;
            if (variables.HasGlobal(RefundVariable))
                factor = variables.GetGlobal(RefundVariable).AsNumber();
            factor = factor < 0 ? 0 : factor > 1 ? 1 : factor;
            if (factor <= 0)
                return;

            foreach (var pair in order.Paid)
                ResourceManager.WriteBank(variables, order.Player, pair.Key,
                    ResourceManager.BankValue(variables, order.Player, pair.Key) + pair.Value * factor);
        }

        private static void WriteProgress(EventVariables variables, object building, ResearchQueue queue)
        {
            if (variables == null || building == null || queue.Current == null)
                return;
            double percent = queue.Current.Duration <= 0
                ? 100
                : Math.Max(0, Math.Min(100, queue.Current.Elapsed / queue.Current.Duration * 100));
            variables.SetLocal(building, ProgressVariable, ValueBox.OfNumber(percent));
            variables.SetLocal(building, NameVariable, ValueBox.OfText(queue.Current.Upgrade));
        }

        private static void ClearProgress(EventVariables variables, object building)
        {
            if (variables == null || building == null)
                return;
            variables.SetLocal(building, ProgressVariable, ValueBox.OfNumber(0));
            variables.SetLocal(building, NameVariable, ValueBox.OfText(""));
        }

        private static void RaiseStarted(EventRuntime runtime, object building, ResearchQueue queue, ResearchOrder order)
        {
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.ResearchStarted,
                Name = order.Upgrade,
                Other = building,
                Material = queue.Building,
                PlayerA = order.Player,
            });
        }

        private static void RaiseFinished(EventRuntime runtime, object building, ResearchQueue queue, ResearchOrder order)
        {
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.ResearchFinished,
                Name = order.Upgrade,
                Other = building,
                Material = queue.Building,
                PlayerA = order.Player,
            });
        }

        private static void RaiseEmpty(EventRuntime runtime, object building, ResearchQueue queue, int player)
        {
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.ResearchQueueEmpty,
                Name = queue.Building,
                Other = building,
                PlayerA = player,
            });
        }
    }
}
