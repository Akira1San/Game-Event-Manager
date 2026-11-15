using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>Body for the global Game.events sheet — no actor, but `me` reads as "Game".</summary>
    public class GlobalHost
    {
        public override string ToString()
        {
            return "Game";
        }
    }

    public class EventContext
    {
        public EventVariables Variables;
        public IEventWorld World;
        public object Me;
        public object Other;
        public EventRuntime Runtime;
        public SheetInstance Sheet;
        public bool Stopped;

        public ValueBox ReadGlobal(string name)
        {
            // A statistic nobody wrote yet reads as 0 instead of warning: a fresh run asks
            // `kills Zombie` before the first zombie dies (Task 16 Part G).
            if (Levels.IsStatName(name) && !Variables.HasGlobal(name))
                return Levels.DefaultFor(name);
            return ResourceManager.Read(Variables, name);
        }

        /// <summary>
        /// Reads one of the sheet actor's own variables. A few names are computed rather than
        /// stored — <c>my bag</c>, <c>my state</c> and <c>my spawnedFrom</c> — and they live here
        /// so a condition (<c>my spawnedFrom is "Bullet"</c>) sees exactly what an expression
        /// (<c>set X to my spawnedFrom</c>) sees. They used to be special-cased only in
        /// <see cref="Resolver"/>, which made every condition on them read an unset variable.
        /// </summary>
        public ValueBox ReadMy(string name)
        {
            if (Runtime == null)
                return Variables.GetLocal(Me, name);
            if (name == "bag")
                return ValueBox.OfText(Runtime.BagText(Me));
            if (name == "state")
            {
                var sheet = Runtime.FindSheetFor(Me);
                return ValueBox.OfText(sheet == null ? "default" : sheet.CurrentState);
            }
            if (name == "spawnedFrom")
                return ValueBox.OfText(Runtime.SpawnedFrom(Me));
            // `my name` is the actor's own name, so one sheet can serve every actor in a scene —
            // `enemy type my name` reads the type from a scene actor called "Pistolboy".
            if (string.Equals(name, "name", StringComparison.OrdinalIgnoreCase))
                return ValueBox.OfText(Runtime.NameOfActor(Me));
            // `my TargetPlayer` (Task 16 Part F) is computed rather than stored: the nearest
            // living player, recomputed on every read, so vision/hearing can never leave a
            // stale name behind. Compare it with `my TargetPlayer is Player1`.
            if (name == "TargetPlayer" || name == "Target Player")
            {
                object target = Runtime.NearestPlayer(Me);
                return ValueBox.OfText(target == null ? "" : Runtime.NameOfActor(target));
            }
            // Order state is owned by the OrderManager, so `my current order` and friends
            // always agree with the queue even before the first command.
            if (name == "current order" || name == "CurrentOrder")
                return ValueBox.OfText(Runtime.Orders.CurrentOrderName(Me));
            if (name == "order target" || name == "OrderTarget")
                return ValueBox.OfText(Runtime.Orders.OrderTargetName(Me));
            if (name == "order queue" || name == "OrderQueue")
                return ValueBox.OfNumber(Runtime.Orders.OrderCount(Me));
            // `my Ammo` is the held weapon's count, so one HUD line can show the selected
            // weapon and its rounds without the sheet mirroring a variable per weapon.
            if (string.Equals(name, "Ammo", StringComparison.OrdinalIgnoreCase))
                return ValueBox.OfNumber(WeaponData.ReadAmmo(this, ReadMy(WeaponData.CurrentWeaponVariable).AsText()));
            return Variables.GetLocal(Me, name);
        }

        public void WriteGlobal(string name, ValueBox value)
            => Variables.SetGlobal(ResourceManager.CanonicalOrName(name), value);

        public void WriteMy(string name, ValueBox value)
            => Variables.SetLocal(Me, name, value);

        public ValueBox ReadOther(string name)
            => Other == null ? new ValueBox() : Variables.GetLocal(Other, name);

        public VariableResolver Resolver()
            => variable =>
            {
                // `my bag` / `my state` / `my spawnedFrom` are computed inside ReadMy, so
                // values and conditions share one implementation.
                ValueBox value;
                if (variable.Owner == "my" || variable.Owner == "me")
                    value = ReadMy(variable.Name);
                else if (variable.Owner == "other")
                    value = ReadOther(variable.Name);
                // `Player's gold` — a named actor's `my` variable (read-only)
                else if (variable.Owner.Length > 0)
                {
                    var actor = Runtime.FindNamedActor(variable.Owner);
                    value = actor == null ? new ValueBox() : Variables.GetLocal(actor, variable.Name);
                }
                else
                    value = ReadGlobal(variable.Name);
                // `-my DamageTaken` — read, then negate (Task 18 Part G).
                return variable.Negated ? ValueBox.OfNumber(-value.AsNumber()) : value;
            };

        public ValueBox Evaluate(ValueNode node)
            => ValueEvaluator.Evaluate(node, Resolver());
    }

    public class WatchState
    {
        public ValueBox Last = new ValueBox();
        public bool Initialized;
    }

    public class PendingRun
    {
        public SheetInstance Sheet;
        public EventNode Event;
        public EventContext Context;
        public int LineIndex;
        public bool Gate;
        /// <summary>The gate of the condition block the wait sits in, so a later
        /// <c>otherwise</c> still knows whether that condition was true.</summary>
        public bool ConditionGate;
        public double ResumeAt;
    }

    public class SheetInstance
    {
        public object Actor;
        public string ActorName = "";
        /// <summary>Which .events file drives this sheet (e.g. "Enemy"); several actors can share one.</summary>
        public string SourceName = "";
        /// <summary>Opt-in (EventSheet "Use Physics Movement"): move through the actor's
        /// body instead of assigning Position. Off = the normal transform path.</summary>
        public bool UsePhysicsMovement;
        /// <summary>Which local player this sheet belongs to (1 or 2, from the actor name), or 0
        /// for a sheet that is not a player and reads physical keys globally.</summary>
        public int Player;
        /// <summary>How far below the feet still counts as ground for a CharacterController
        /// (EventSheet "Character Ground Check Distance").</summary>
        public float GroundCheckDistance = 10f;
        /// <summary>The sheet's current state (default "default"). `set state` writes it;
        /// `when state is` fires on entry edge; `state &lt;name&gt;` gates triggers by it.</summary>
        public string CurrentState = "default";
        /// <summary>The state at the end of the previous tick, used for StateIs edge detection.</summary>
        public string LastState = "default";
        public ProgramFile Program = new ProgramFile();
        public List<TriggerDescriptor> Triggers = new List<TriggerDescriptor>();
        public Queue<PushSignal> PushQueue = new Queue<PushSignal>();
        public List<PendingRun> Waiting = new List<PendingRun>();
        /// <summary>One enter-edge set per trigger: pairs already touching, so a poll fires once.</summary>
        public HashSet<object>[] Contacts = new HashSet<object>[0];
        /// <summary>Pairs the distance poll fired for; lets Both mode ignore a later collider push for the same pair.</summary>
        public HashSet<object>[] PolledContacts = new HashSet<object>[0];
        /// <summary>Actors currently in sight for each polled sight trigger (enter/falling edge state).</summary>
        public HashSet<object>[] InSight = new HashSet<object>[0];
        /// <summary>Ids of the sounds each hearing trigger already answered, so one gunshot
        /// fires `when me hears` once instead of every tick while it is still on the bus.</summary>
        public HashSet<int>[] Heard = new HashSet<int>[0];
        /// <summary>Reloads finished so far, per `when reload completes` event, so a sheet that
        /// polls sees each completion exactly once.</summary>
        public int[] ReloadSeen = new int[0];
        /// <summary>Last distance at which <c>sees name</c> established sight; reused by a
        /// distance-less <c>stops seeing name</c>.</summary>
        public Dictionary<string, double> LastSeenRange = new Dictionary<string, double>();
        /// <summary>Material → clip name map for <c>play step sound "&lt;material&gt;"</c>.
        /// Populated per-project (e.g. Grass → "grass_step.wav").</summary>
        public Dictionary<string, string> StepSoundMap = new Dictionary<string, string>();
        /// <summary>Minimum seconds between two step sounds on the same material, per actor.</summary>
        public double StepCooldown = 0.3;
        /// <summary>Last play time per material, used to enforce StepCooldown.</summary>
        public Dictionary<string, double> StepLastPlay = new Dictionary<string, double>();
        /// <summary>Materials already warned about a missing clip mapping (warn once each).</summary>
        public HashSet<string> StepWarned = new HashSet<string>();
        public bool[] FiredThisTick = new bool[0];
        public double[] LastFire = new double[0];
        public WatchState[] Watches = new WatchState[0];
        public double AnchorTime;
    }

    public class EventRuntime
    {
        public EventVariables Variables = new EventVariables();
        public IEventWorld World = new DefaultEventWorld();
        /// <summary>RTS selection + control groups (Task 18 Part A).</summary>
        public Selection Selection;
        /// <summary>RTS construction state, progress and grid occupancy (Task 18 Part C).</summary>
        public ConstructionManager Construction = new ConstructionManager();
        /// <summary>RTS production queues and training timers (Task 18 Part D).</summary>
        public ProductionManager Production = new ProductionManager();
        /// <summary>RTS per-unit order queues, attack-move and rally points (Task 18 Part E).</summary>
        public OrderManager Orders = new OrderManager();
        /// <summary>RTS per-player fog of war and visibility (Task 18 Part F).</summary>
        public FogOfWarManager Fog = new FogOfWarManager();
        /// <summary>RTS terrain passability — land/water/air cells (Task 22 Part B).</summary>
        public TerrainMap Terrain = new TerrainMap();
        /// <summary>Random-angle wander legs and still mode per actor (Task 16 Part A).</summary>
        public Wanderer Wander = new Wanderer();
        /// <summary>Per-player key / gamepad bindings (Task 16 input follow-ups).</summary>
        public PlayerInput Input = new PlayerInput();
        /// <summary>Recent sounds that `when me hears` / `me hears` poll (Task 16 Part B).</summary>
        public SoundBus Sounds = new SoundBus();
        /// <summary>Per-actor reload timers behind `reload` / `reloading` (Task 16 Part C).</summary>
        public WeaponClock Weapons = new WeaponClock();
        /// <summary>Level order, current level and run statistics (Task 16 Part G).</summary>
        public Levels Levels = new Levels();
        /// <summary>The bridge loads the scene `next level` picks; a test can just record it.</summary>
        public Action<string> OnLevelAdvance = name => { };
        /// <summary>The level `next level` asked for, loaded at the end of the tick so the
        /// `when level completes` events queued with it still run first.</summary>
        private string pendingLevel = "";
        /// <summary>True once an enemy (`my Type` set) has stood in this level, so a scene with
        /// no enemies never claims "all enemies dead".</summary>
        private bool enemiesSeen;
        private bool allEnemiesDeadRaised;
        public double Time;
        /// <summary>Bridge flag: poll `touches` from world contacts (distance mode / both mode).</summary>
        public bool PollTouches;
        /// <summary>Bridge flag: poll `overlaps` from world contacts (always on under EventsManager).</summary>
        public bool PollOverlaps = true;
        /// <summary>Close enough to count as having arrived at a <c>goto</c> target.</summary>
        public const double ArrivalSlack = 0.5;

        public static Action<object, object> ClearArrivalContact = (subject, target) => { };

        public Action<string> OnRuntimeWarning = message => { };
        public Action<EventContext, List<Token>> OnRawAction = (context, words) => { };
        public Action<EventContext, string, ValueBox> OnSetText = (context, thing, value) => { };
        /// <summary>Play a step sound: <paramref name="material"/> is the surface name (e.g.
        /// "Grass"), <paramref name="clipName"/> is the resolved clip to play. The bridge
        /// wires this to actual audio playback.</summary>
        public Action<EventContext, string, string> OnPlayStepSound = (context, material, clipName) => { };
        /// <summary>Set a UI Button enabled/disabled.</summary>
        public Action<EventContext, string, bool> OnSetButtonEnabled = (context, thing, enabled) => { };
        /// <summary>Swap a UI Image's brush to the named sprite.</summary>
        public Action<EventContext, string, string> OnSetImageBrush = (context, thing, sprite) => { };
        /// <summary>Tint a UI control to the named color ("r g b" or "r g b a").</summary>
        public Action<EventContext, string, string> OnSetControlColor = (context, thing, color) => { };
        /// <summary>Fill a UI ProgressBar: the value is 0-1 or 0-100.</summary>
        public Action<EventContext, string, ValueBox> OnSetProgress = (context, thing, value) => { };
        /// <summary>Move a UI Slider: the value is 0-1 or 0-100.</summary>
        public Action<EventContext, string, ValueBox> OnSetSlider = (context, thing, value) => { };

        private readonly List<SheetInstance> sheets = new List<SheetInstance>();
        private readonly HashSet<string> warnedLines = new HashSet<string>();
        /// <summary>actor -> the prefab / scene / template that spawned it (`my spawnedFrom`).</summary>
        private readonly Dictionary<object, string> spawnSources = new Dictionary<object, string>();
        private readonly Dictionary<object, Dictionary<string, int>> bags = new Dictionary<object, Dictionary<string, int>>();
        /// <summary>wearer -> set of item names currently equipped on that wearer.</summary>
        private readonly Dictionary<object, HashSet<string>> equipped = new Dictionary<object, HashSet<string>>();
        /// <summary>Per-player join/die/score edges behind the Part F triggers, indexed 1..2.</summary>
        private readonly PlayerWatch[] playerWatches = new PlayerWatch[Players.MaxPlayers + 1];
        /// <summary>victim -> the attacker whose `"damage"` last landed on it, for deathmatch credit.</summary>
        private readonly Dictionary<object, object> lastAttacker = new Dictionary<object, object>();
        private bool allPlayersDeadRaised;
        /// <summary>The sheet waiting for a key press (`rebind player &lt;n&gt; &lt;action&gt;`).</summary>
        private SheetInstance rebindSheet;
        private int rebindPlayer;
        private string rebindAction = "";

        public EventRuntime()
        {
            Variables.OnWarning = message => OnRuntimeWarning(message);
            Selection = new Selection(this);
            ClearArrivalContact = ClearArrivalContactForSubjectTarget;
        }

        private void ClearArrivalContactForSubjectTarget(object subject, object target)
        {
            foreach (var sheet in sheets)
            {
                if (ReferenceEquals(sheet.Actor, subject))
                {
                    for (int i = 0; i < sheet.Contacts.Length; i++)
                        sheet.Contacts[i].Remove(target);
                }
            }
        }

        public IReadOnlyList<SheetInstance> Sheets => sheets;

        public SheetInstance AddSheet(object actor, ProgramFile program)
        {
            return AddSheet(actor, program, actor == null ? "" : actor.ToString());
        }

        public SheetInstance AddSheet(object actor, ProgramFile program, string sourceName)
        {
            string actorName = World.NameOf(actor);
            var sheet = new SheetInstance
            {
                Actor = actor,
                ActorName = actorName,
                Player = Players.TryReadNumber(actorName, out int playerNumber) ? playerNumber : 0,
                SourceName = sourceName ?? "",
                Program = program,
                AnchorTime = Time,
            };
            ReadyTree(sheet, program);
            sheets.Add(sheet);
            sheet.PushQueue.Enqueue(new PushSignal { Kind = TriggerKind.Appears });
            return sheet;
        }

        public void SwapSheetProgram(SheetInstance sheet, ProgramFile replacement)
        {
            if (sheet == null || replacement == null)
                return;
            ReadyTree(sheet, replacement);
            sheet.PushQueue.Clear();
            sheet.Waiting.Clear();
        }

        private void ReadyTree(SheetInstance sheet, ProgramFile program)
        {
            sheet.Program = program;
            int count = program.Events.Count;
            sheet.FiredThisTick = new bool[count];
            sheet.LastFire = new double[count];
            sheet.Watches = new WatchState[count];
            sheet.Contacts = new HashSet<object>[count];
            sheet.PolledContacts = new HashSet<object>[count];
            sheet.InSight = new HashSet<object>[count];
            sheet.Heard = new HashSet<int>[count];
            sheet.ReloadSeen = new int[count];
            sheet.Triggers.Clear();
            sheet.CurrentState = "default";
            sheet.LastState = "default";
            for (int i = 0; i < count; i++)
            {
                sheet.LastFire[i] = Time;
                sheet.Watches[i] = new WatchState();
                sheet.Contacts[i] = new HashSet<object>();
                sheet.PolledContacts[i] = new HashSet<object>();
                sheet.InSight[i] = new HashSet<object>();
                sheet.Heard[i] = new HashSet<int>();
                sheet.Triggers.Add(TriggerClassifier.Classify(program.Events[i]));
            }
        }

        public void RemoveAllSheets()
        {
            sheets.Clear();
            spawnSources.Clear();
        }

        public void RemoveActorSheet(object actor)
        {
            for (int i = sheets.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(sheets[i].Actor, actor))
                    sheets.RemoveAt(i);
            }
            if (equipped.TryGetValue(actor, out var _))
                equipped.Remove(actor);
            spawnSources.Remove(actor);
            Fog.RemoveActor(actor);
        }

        public void SessionStarted()
        {
            // Bindings saved by the options menu come back before any gameplay key is polled.
            Input.LoadFrom(Variables);
            foreach (var sheet in sheets)
                sheet.PushQueue.Enqueue(new PushSignal { Kind = TriggerKind.GameStart });
        }

        /// <summary>
        /// A scene finished loading. A level is a scene (Task 16 Part G), so this is also
        /// where the level name/number and this level's clock are recorded; `when scene
        /// starts` and `when level starts` both answer it. Statistics survive (a run spans
        /// levels) — only <c>Time Elapsed</c> restarts.
        /// </summary>
        public void SceneStarted(string levelName = "")
        {
            Levels.Start(levelName, Variables, Time);
            enemiesSeen = false;
            allEnemiesDeadRaised = false;
            pendingLevel = "";
            Fog.Reset();
            foreach (var sheet in sheets)
                sheet.PushQueue.Enqueue(new PushSignal { Kind = TriggerKind.SceneStart });
        }

        /// <summary>Pushes <c>when scene starts</c> to the sheets of the actors that just arrived
        /// with a newly loaded scene (`add scene`), without re-running it for the level that was
        /// already playing.</summary>
        public void SceneStartedFor(IEnumerable<object> actors)
        {
            var wanted = new HashSet<object>();
            foreach (var actor in actors)
                if (actor != null)
                    wanted.Add(actor);
            if (wanted.Count == 0)
                return;
            foreach (var sheet in sheets)
                if (wanted.Contains(sheet.Actor))
                    sheet.PushQueue.Enqueue(new PushSignal { Kind = TriggerKind.SceneStart });
        }

        public void RaisePush(object actor, PushSignal signal)
        {
            foreach (var sheet in sheets)
                if (ReferenceEquals(sheet.Actor, actor))
                    sheet.PushQueue.Enqueue(signal);
        }

        public void RaisePushAll(PushSignal signal)
        {
            foreach (var sheet in sheets)
                sheet.PushQueue.Enqueue(signal);
        }

        /// <summary>A click on an actor is a world event: the clicked actor's own sheet gets
        /// `when me is clicked`, and any other sheet can listen by name with
        /// `when Ground is clicked`. Broadcast it and let <see cref="Matches"/> filter by name.</summary>
        public void RaiseClick(object actor, TriggerKind kind)
        {
            if (actor == null)
                return;
            var signal = new PushSignal { Kind = kind, Name = NameOf(actor), Other = actor };
            foreach (var sheet in sheets)
                sheet.PushQueue.Enqueue(signal);
        }

        /// <summary>Select one actor (used by the bridge's click/box select and by
        /// <c>select me</c>). Returns true when the selection changed.</summary>
        public bool SelectActor(object actor)
        {
            return Selection.Select(actor);
        }

        /// <summary>Deselect one actor. Returns true when the selection changed.</summary>
        public bool DeselectActor(object actor)
        {
            return Selection.Deselect(actor);
        }

        /// <summary>Recall a control group (pressing 1–0 in the engine bridge).</summary>
        public bool SelectControlGroup(int group)
        {
            return Selection.SelectControlGroup(group);
        }

        // ---------------- RTS state queries (Task 18 Part H) ----------------
        // The event language reads this state directly; the world exposes it to bridge code,
        // tools and AI through IEventWorld (see FlaxWorld), which forwards here.

        /// <summary>The actors currently selected, in click order.</summary>
        public IReadOnlyList<object> SelectedActors => Selection.Selected;

        /// <summary>How many actors are selected.</summary>
        public int SelectedCount => Selection.Selected.Count;

        /// <summary>One RTS resource's value; an unset resource reads as 0.</summary>
        public double ResourceValue(string name) => ResourceManager.ReadNumber(Variables, name);

        /// <summary>True when <paramref name="playerId"/> can see <paramref name="actor"/>
        /// in the fog grid. Player 0 is the local player's grid.</summary>
        public bool IsRevealedFor(object actor, int playerId) => Fog.IsVisibleTo(playerId, actor);

        /// <summary>Explicitly reveals a world circle for one player.</summary>
        public bool RevealArea(int playerId, double x, double z, double radius)
            => Fog.RevealArea(playerId, x, z, radius);

        /// <summary>Explicitly hides a world circle for one player.</summary>
        public bool HideArea(int playerId, double x, double z, double radius)
            => Fog.HideArea(playerId, x, z, radius);

        /// <summary>Construction progress 0–100, or 0 when the actor is not building.</summary>
        public double BuildProgressOf(object building) => Construction.ProgressOf(building);

        /// <summary>The unit's order names, active order first.</summary>
        public IReadOnlyList<string> OrderQueueOf(object unit) => Orders.OrderNames(unit);

        public void PushContact(TriggerKind kind, object a, object b)
        {
            foreach (var sheet in sheets)
            {
                if (ReferenceEquals(sheet.Actor, a))
                    sheet.PushQueue.Enqueue(new PushSignal { Kind = kind, Other = b, Name = NameOf(b), Material = World.MaterialOf(b) });
                if (ReferenceEquals(sheet.Actor, b))
                    sheet.PushQueue.Enqueue(new PushSignal { Kind = kind, Other = a, Name = NameOf(a), Material = World.MaterialOf(a) });
            }
        }

        public void SendRawMessage(PushSignal signal, string toName)
        {
            if (string.IsNullOrEmpty(toName))
            {
                RaisePushAll(signal);
                return;
            }
            // Local multiplayer targets (Task 16 Part F): `to player 2` reaches one player,
            // `to all players` both, `to nearest player` whichever is closest to the sender.
            if (Players.IsAllReference(toName))
            {
                bool delivered = false;
                for (int number = 1; number <= Players.MaxPlayers; number++)
                {
                    SheetInstance sheet = FindSheetFor(FindPlayer(number));
                    if (sheet == null)
                        continue;
                    DeliverToSheet(sheet, signal);
                    delivered = true;
                }
                if (!delivered)
                    DeliverToSheet(FindSheetFor(FindNamedActor("Player")), signal);
                return;
            }
            if (Players.TryReadNumber(toName, out int playerNumber))
            {
                DeliverToSheet(FindSheetFor(FindPlayer(playerNumber)), signal);
                return;
            }
            if (Players.IsNearestReference(toName))
            {
                DeliverToSheet(FindSheetFor(NearestPlayer(signal.Other)), signal);
                return;
            }
            foreach (var sheet in sheets)
                if (sheet.ActorName == toName)
                    DeliverToSheet(sheet, signal);
        }

        /// <summary>Queues a message for one sheet, and remembers where a damage landed.</summary>
        private void DeliverToSheet(SheetInstance sheet, PushSignal signal)
        {
            if (sheet == null)
                return;
            sheet.PushQueue.Enqueue(signal);
            if (signal.Kind == TriggerKind.Message && IsDamageMessage(signal.Name))
            {
                NoteDamage(sheet.Actor, signal.Other);
                // A landed shot on an enemy feeds `accuracy` (Task 16 Part G).
                if (IsEnemy(sheet.Actor))
                    Levels.CountHit(Variables);
            }
        }

        /// <summary>Remembers what produced a spawned actor, so `my spawnedFrom` can name it
        /// and the bridge can find the right .events file when the instance was renamed.</summary>
        public void MarkSpawned(object actor, string source)
        {
            if (actor == null || string.IsNullOrEmpty(source))
                return;
            spawnSources[actor] = source;
        }

        /// <summary>The prefab / scene / template that spawned this actor, or "" for an
        /// actor that was authored into the scene.</summary>
        public string SpawnedFrom(object actor)
        {
            if (actor == null)
                return "";
            return spawnSources.TryGetValue(actor, out string source) ? source : "";
        }

        public object FindNamedActor(string name)
        {
            foreach (var sheet in sheets)
                if (sheet.ActorName == name)
                    return sheet.Actor;
            return null;
        }

        public SheetInstance FindActorSheet(string name)
        {
            foreach (var sheet in sheets)
                if (name != "Game" && sheet.ActorName == name)
                    return sheet;
            return null;
        }

        // ---------------- local multiplayer (Task 16 Part F) ----------------

        /// <summary>The actor named <c>Player&lt;n&gt;</c> that carries a sheet, or null.</summary>
        public object FindPlayer(int number)
        {
            if (!Players.IsValidNumber(number))
                return null;
            string name = Players.ActorName(number);
            foreach (var sheet in sheets)
                if (string.Equals(sheet.ActorName, name, StringComparison.OrdinalIgnoreCase))
                    return sheet.Actor;
            return null;
        }

        /// <summary>
        /// The closest player to <paramref name="from"/> — what a bare <c>player</c> /
        /// <c>nearest player</c> resolves to in a target position. A living player wins over a
        /// dead one, so an enemy chases the player still in the fight; a single-player scene
        /// whose hero is named <c>Player</c> still answers.
        /// </summary>
        public object NearestPlayer(object from)
        {
            object best = null;
            double bestDistance = double.MaxValue;
            object anyPlayer = null;
            double anyDistance = double.MaxValue;
            for (int number = 1; number <= Players.MaxPlayers; number++)
            {
                object player = FindPlayer(number);
                if (player == null || ReferenceEquals(player, from))
                    continue;
                double distance = from == null ? 0 : World.Distance(from, player);
                if (distance < anyDistance)
                {
                    anyDistance = distance;
                    anyPlayer = player;
                }
                if (HealthOf(player) > 0 && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = player;
                }
            }
            if (best != null)
                return best;
            if (anyPlayer != null)
                return anyPlayer;
            object single = FindNamedActor("Player");
            return ReferenceEquals(single, from) ? null : single;
        }

        /// <summary>
        /// A player is dead when its <c>my Health</c> has reached 0 (TK_Player leaves the body
        /// for the gibs) or when its sheet is gone. A player that never set Health is alive.
        /// </summary>
        public bool IsPlayerAlive(int number)
        {
            object player = FindPlayer(number);
            return player != null && HealthOf(player) > 0;
        }

        /// <summary>True once every player in the scene is dead. No player at all is not "all dead".</summary>
        public bool AllPlayersDead
        {
            get
            {
                bool sawPlayer = false;
                for (int number = 1; number <= Players.MaxPlayers; number++)
                {
                    if (FindPlayer(number) == null)
                        continue;
                    sawPlayer = true;
                    if (IsPlayerAlive(number))
                        return false;
                }
                object single = FindNamedActor("Player");
                if (single != null)
                {
                    sawPlayer = true;
                    if (HealthOf(single) > 0)
                        return false;
                }
                return sawPlayer;
            }
        }

        /// <summary>The number of the player <paramref name="actor"/> is, or 0 for anybody else.</summary>
        public int PlayerNumberOf(object actor)
        {
            if (actor == null)
                return 0;
            for (int number = 1; number <= Players.MaxPlayers; number++)
                if (ReferenceEquals(FindPlayer(number), actor))
                    return number;
            return 0;
        }

        /// <summary>The actor's real name — the bridge uses it to stamp `my shooter` on bullets.</summary>
        public string NameOfActor(object actor)
        {
            return World.NameOf(actor);
        }

        // ---------------- per-player input (Task 16 input follow-ups) ----------------

        /// <summary>Which player owns a physical key, or 0 when the key is global (`P`, `Escape`).</summary>
        public int PlayerForKey(string physicalKey)
        {
            return Input.OwnerOf(physicalKey);
        }

        /// <summary>
        /// The physical key this sheet reads for a written key. A player's sheet turns
        /// <c>Left Arrow</c> / <c>turn left</c> into that player's binding; any other sheet keeps
        /// the written key, so menus and single-player scenes behave exactly as before.
        /// </summary>
        public string ResolveKeyForSheet(SheetInstance sheet, string writtenKey)
        {
            return Input.Resolve(sheet == null ? 0 : sheet.Player, writtenKey);
        }

        /// <summary>
        /// True while the sheet's player holds the written key or the gamepad button bound to it.
        /// <c>when Left Arrow is held</c> on Player2 asks about A, and the left stick works too.
        /// </summary>
        public bool IsKeyHeldFor(SheetInstance sheet, string writtenKey)
        {
            if (string.IsNullOrEmpty(writtenKey))
                return false;
            if (World.IsKeyHeld(ResolveKeyForSheet(sheet, writtenKey)))
                return true;
            int player = sheet == null ? 0 : sheet.Player;
            if (player <= 0 || !PlayerInput.TryActionFor(writtenKey, out string action))
                return false;
            string button = Input.ButtonFor(player, action);
            return button.Length > 0 && World.IsGamepadButtonHeld(PlayerInput.GamepadFor(player), button);
        }

        /// <summary>True while the options menu is waiting for the next key or button.</summary>
        public bool IsCapturingKey
        {
            get { return rebindSheet != null && rebindAction.Length > 0; }
        }

        /// <summary>
        /// <c>rebind player 2 Shoot</c> — arms the capture: the next key press (or gamepad
        /// button) on that sheet's watch is stored as player 2's Shoot binding and persisted.
        /// </summary>
        public void BeginRebind(EventContext context, int player, string action)
        {
            if (!Players.IsValidNumber(player) || !PlayerInput.TryReadAction(action, out string canonical))
            {
                OnRuntimeWarning($"I can't rebind \u201C{action}\u201D for player {player} \u2014 the actions are {PlayerInput.ActionNames()}.");
                return;
            }
            rebindSheet = context == null ? null : context.Sheet;
            rebindPlayer = player;
            rebindAction = canonical;
            Variables.SetLocal(context == null ? null : context.Me, "LastBoundKey", ValueBox.OfText(""));
            Variables.SetLocal(context == null ? null : context.Me, "LastBoundAction", ValueBox.OfText(canonical));
            Variables.SetLocal(context == null ? null : context.Me, "LastBoundPlayer", ValueBox.OfNumber(player));
        }

        /// <summary>
        /// Finishes an armed capture with the key (<paramref name="key"/>) or gamepad button
        /// (<paramref name="button"/>) that was just pressed. Returns false when nothing is armed.
        /// </summary>
        public bool TryCompleteCapture(string key, string button)
        {
            if (!IsCapturingKey)
                return false;
            if (!string.IsNullOrEmpty(key))
                Input.SetBinding(rebindPlayer, rebindAction, key);
            else if (!string.IsNullOrEmpty(button))
                Input.SetButton(rebindPlayer, rebindAction, button);
            else
                return false;

            // Persist immediately, so a `bind` label on the options menu refreshes itself.
            Input.SaveTo(Variables);
            string shown = !string.IsNullOrEmpty(key) ? key : button;
            Variables.SetLocal(rebindSheet.Actor, "LastBoundKey", ValueBox.OfText(shown));
            Variables.SetLocal(rebindSheet.Actor, "LastBoundAction", ValueBox.OfText(rebindAction));
            Variables.SetLocal(rebindSheet.Actor, "LastBoundPlayer", ValueBox.OfNumber(rebindPlayer));
            string action = rebindAction;
            rebindSheet = null;
            rebindPlayer = 0;
            rebindAction = "";
            RaisePushAll(new PushSignal { Kind = TriggerKind.BindingChanges, Name = action });
            return true;
        }

        private double HealthOf(object actor)
        {
            return Variables.HasLocal(actor, "Health") ? Variables.GetLocal(actor, "Health").AsNumber() : 1;
        }

        /// <summary>
        /// Polls the two player slots once a tick and pushes the edges: a player already in the
        /// scene has "joined" on the first poll, a health drop to 0 (or a lost sheet) is a death,
        /// and a changed <c>Player&lt;n&gt; Score</c> global is a score change. A death credits a
        /// kill when the last <c>"damage"</c> on that player came from the other player's bullet.
        /// </summary>
        private void TickPlayers()
        {
            for (int number = 1; number <= Players.MaxPlayers; number++)
            {
                PlayerWatch watch = playerWatches[number];
                if (watch == null)
                    watch = playerWatches[number] = new PlayerWatch();

                bool alive = IsPlayerAlive(number);
                string scoreName = Players.VariableName(number, "Score");
                double score = Variables.HasGlobal(scoreName) ? Variables.GetGlobal(scoreName).AsNumber() : 0;

                if (!watch.Initialized)
                {
                    if (alive)
                        RaisePlayer(TriggerKind.PlayerJoins, number);
                }
                else
                {
                    if (alive && !watch.Alive)
                        RaisePlayer(TriggerKind.PlayerJoins, number);
                    if (!alive && watch.Alive)
                        PlayerDied(number);
                }
                if (watch.ScoreKnown && score != watch.Score)
                    RaisePlayer(TriggerKind.PlayerScoreChanges, number);

                watch.Alive = alive;
                watch.Score = score;
                watch.ScoreKnown = true;
                watch.Initialized = true;
            }

            bool allDead = AllPlayersDead;
            if (allDead && !allPlayersDeadRaised)
            {
                allPlayersDeadRaised = true;
                RaisePushAll(new PushSignal { Kind = TriggerKind.AllPlayersDead });
            }
            else if (!allDead)
            {
                allPlayersDeadRaised = false;
            }
        }

        private void RaisePlayer(TriggerKind kind, int number)
        {
            RaisePushAll(new PushSignal
            {
                Kind = kind,
                PlayerA = number,
                Other = FindPlayer(number),
            });
        }

        private void PlayerDied(int number)
        {
            RaisePlayer(TriggerKind.PlayerDies, number);
            object victim = FindPlayer(number);
            if (victim == null || !lastAttacker.TryGetValue(victim, out object attacker))
                return;
            lastAttacker.Remove(victim);
            int killer = PlayerNumberOf(attacker);
            if (killer == 0 || killer == number)
                return;
            RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.PlayerKills,
                PlayerA = killer,
                PlayerB = number,
                Other = victim,
            });
        }

        /// <summary>Remembers who hit what, so a player death can be credited to the other player.</summary>
        private void NoteDamage(object target, object sender)
        {
            if (target == null || sender == null)
                return;
            lastAttacker[target] = ResolveAttacker(sender);
        }

        private static bool IsDamageMessage(string message)
        {
            return string.Equals(message, "damage", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(message, "hurt", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Turns the sender of a damage message into the player who should be credited: a
        /// projectile carries <c>my shooter</c> (stamped by `spawn`), so a bullet from Player1
        /// credits Player1 rather than the bullet clone. Anything else is its own attacker.
        /// </summary>
        private object ResolveAttacker(object sender)
        {
            if (sender == null)
                return null;
            if (Variables.HasLocal(sender, "shooter"))
            {
                string name = Variables.GetLocal(sender, "shooter").AsText();
                if (name.Length > 0)
                {
                    object shooter = FindNamedActor(name);
                    if (shooter != null)
                        return shooter;
                }
            }
            return sender;
        }

        /// <summary>The sheet bound to this exact object, or null. The global Game host never matches.</summary>
        public SheetInstance FindSheetFor(object actor)
        {
            if (actor == null)
                return null;
            foreach (var sheet in sheets)
                if (ReferenceEquals(sheet.Actor, actor) && !(sheet.Actor is GlobalHost))
                    return sheet;
            return null;
        }

        /// <summary>Every sheet driven by one .events file (several actor instances can share it).</summary>
        public List<SheetInstance> SheetsUsingSource(string sourceName)
        {
            var found = new List<SheetInstance>();
            foreach (var sheet in sheets)
                if (sheet.SourceName == sourceName)
                    found.Add(sheet);
            return found;
        }

        public void RemoveSheet(SheetInstance sheet)
        {
            for (int i = sheets.Count - 1; i >= 0; i--)
                if (ReferenceEquals(sheets[i], sheet))
                    sheets.RemoveAt(i);
        }

        public void BagAdd(object owner, string thing)
        {
            var bag = BagOf(owner);
            bag.TryGetValue(thing, out int count);
            bag[thing] = count + 1;
        }

        public void BagRemove(object owner, string thing)
        {
            if (!bags.TryGetValue(owner, out var bag))
                return;
            if (bag.TryGetValue(thing, out int count))
            {
                count--;
                if (count <= 0)
                    bag.Remove(thing);
                else
                    bag[thing] = count;
            }
        }

        public bool BagHas(object owner, string thing)
        {
            return bags.TryGetValue(owner, out var bag) && bag.TryGetValue(thing, out int count) && count > 0;
        }

        public int BagCount(object owner, string thing)
        {
            return bags.TryGetValue(owner, out var bag) && bag.TryGetValue(thing, out int count) ? count : 0;
        }

        /// <summary>Record that <paramref name="wearer"/> is now wearing <paramref name="thing"/>.</summary>
        public void EquipItem(object wearer, string thing)
        {
            if (!equipped.TryGetValue(wearer, out var set))
            {
                set = new HashSet<string>();
                equipped[wearer] = set;
            }
            set.Add(thing);
        }

        /// <summary>Record that <paramref name="wearer"/> is no longer wearing <paramref name="thing"/>.</summary>
        public void UnequipItem(object wearer, string thing)
        {
            if (equipped.TryGetValue(wearer, out var set))
            {
                set.Remove(thing);
                if (set.Count == 0)
                    equipped.Remove(wearer);
            }
        }

        public bool IsEquipped(object wearer, string thing)
        {
            return equipped.TryGetValue(wearer, out var set) && set.Contains(thing);
        }

        public void Tick(double time)
        {
            Time = time;
            // Sounds are momentary: drop the ones older than the bus's memory before this tick's
            // actions can add new ones, so a shot fired now is heard now and not a minute later.
            Sounds.Expire(Time);
            // Control group keys are global input: raise ControlGroupPressed on the enter
            // edge before the push queues are drained so the event runs this same tick.
            Selection.PollControlGroupKeys(World);
            // Production completions are pushed before the drain below, so a
            // `when <building> finishes training <unit>` event runs this same tick.
            Production.Tick(this, Time);
            // Construction completions are pushed before the drain below, so a
            // `when construction completes` event runs this same tick.
            Construction.Tick(this);
            // Fog edges (`when area becomes revealed`, `when <unit> enters fog`) are pushed
            // before the drain too, so they run in the tick the vision changed (Task 18 Part F).
            Fog.Tick(this);
            // Player joins/deaths/score edges (Task 16 Part F) are pushed before the drain too,
            // so `when player 1 dies` runs in the same tick its health reached 0.
            TickPlayers();
            // Level clock, accuracy and `when all enemies dead` (Task 16 Part G).
            TickLevel(time);
            var snapshot = sheets.ToArray();
            foreach (var sheet in snapshot)
            {
                for (int i = 0; i < sheet.FiredThisTick.Length; i++)
                    sheet.FiredThisTick[i] = false;
            }

            foreach (var sheet in snapshot)
            {
                if (sheet.Waiting.Count == 0)
                    continue;
                var pendingSnapshot = new List<PendingRun>(sheet.Waiting);
                sheet.Waiting.Clear();
                foreach (var pending in pendingSnapshot)
                {
                    if (pending.ResumeAt <= Time)
                        RunBody(pending.Sheet, pending.Event, pending.Context, pending.LineIndex, pending.Gate, pending.ConditionGate);
                    else
                        sheet.Waiting.Add(pending);
                }
            }

            foreach (var sheet in snapshot)
            {
                while (sheet.PushQueue.Count > 0)
                {
                    var signal = sheet.PushQueue.Dequeue();
                    for (int eventIndex = 0; eventIndex < sheet.Program.Events.Count; eventIndex++)
                    {
                        if (sheet.FiredThisTick[eventIndex])
                            continue;
                        if (!Matches(sheet, sheet.Triggers[eventIndex], signal))
                            continue;
                        var pushEv = sheet.Program.Events[eventIndex];
                        if (pushEv.RequiredState.Length > 0 && sheet.CurrentState != pushEv.RequiredState)
                            continue;
                        // A pattern message hands its captured numbers to the sheet before the
                        // body runs: `my amount` (the slot's name) and `my DamageTaken`.
                        if (sheet.Triggers[eventIndex].MessagePattern)
                            BindMessageCaptures(sheet, sheet.Triggers[eventIndex], signal);
                        if (IsContactKind(signal.Kind) && signal.Other != null)
                        {
                            // A distance poll already fired this pair: ignore the collider's echo.
                            if (sheet.PolledContacts[eventIndex].Contains(signal.Other))
                                continue;
                            // A name match locks to enter-edge; a material-only match fires
                            // continuously, so don't let Contacts suppress a later poll.
                            var desc = sheet.Triggers[eventIndex];
                            bool pushMatchedByName = desc.RequireName.Length == 0 || desc.RequireName == "me" || signal.Name == desc.RequireName;
                            if (pushMatchedByName)
                                sheet.Contacts[eventIndex].Add(signal.Other); // and don't let a later poll refire it
                        }
                        RunEvent(sheet, eventIndex, signal.Other, signal.MeOverride);
                    }
                }
            }

            foreach (var sheet in snapshot)
            {
                for (int eventIndex = 0; eventIndex < sheet.Program.Events.Count; eventIndex++)
                {
                    if (sheet.FiredThisTick[eventIndex])
                        continue;
                    TryPolled(sheet, eventIndex);
                }
            }

            foreach (var sheet in snapshot)
                sheet.LastState = sheet.CurrentState;

            // `next level` asked for a scene: load it only now, so the events queued with the
            // signal (`when level completes`) have had their run this tick.
            if (pendingLevel.Length > 0)
            {
                string next = pendingLevel;
                pendingLevel = "";
                OnLevelAdvance(next);
            }
        }

        /// <summary>
        /// Once a tick: refreshes the level clock and accuracy, and raises
        /// <c>when all enemies dead</c> on the edge from "some enemy alive" to "none". An
        /// enemy is any actor whose sheet carries <c>my Type</c> (what `enemy type` writes).
        /// </summary>
        private void TickLevel(double time)
        {
            Levels.Tick(time, Variables);
            bool living = false;
            foreach (var sheet in sheets)
            {
                if (!IsEnemy(sheet.Actor))
                    continue;
                enemiesSeen = true;
                if (EnemyHealthOf(sheet.Actor) > 0)
                    living = true;
            }
            bool allDead = enemiesSeen && !living;
            if (allDead && !allEnemiesDeadRaised)
            {
                allEnemiesDeadRaised = true;
                RaisePushAll(new PushSignal { Kind = TriggerKind.AllEnemiesDead });
            }
            else if (!allDead)
            {
                allEnemiesDeadRaised = false;
            }
        }

        /// <summary>An enemy is an actor whose sheet was given a type by `enemy type`.</summary>
        /// <summary>
        /// Whether an actor counts as an enemy for level completion and run statistics: it has a
        /// type and has not declared itself a bystander. A type can opt out with `my Hostile
        /// false` (the Civilian, Task 16 Part I), so a cleared level does not wait on civilians
        /// and shooting one spoils nobody's accuracy.
        /// </summary>
        private bool IsEnemy(object actor)
        {
            if (actor == null || actor is GlobalHost)
                return false;
            if (TypeOf(actor).Length == 0)
                return false;
            if (Variables.HasLocal(actor, "Hostile"))
                return Variables.GetLocal(actor, "Hostile").AsBoolean();
            return true;
        }

        private string TypeOf(object actor)
        {
            return Variables.HasLocal(actor, "Type") ? Variables.GetLocal(actor, "Type").AsText() : "";
        }

        /// <summary>
        /// The enemy's remaining hit points. The enemy sheets in this project spell the stat
        /// lowercase (`my health`), while `enemy type` seeds the capitalised `my Health`; either
        /// spelling answers here, and an enemy with neither counts as alive.
        /// </summary>
        private double EnemyHealthOf(object actor)
        {
            if (Variables.HasLocal(actor, "health"))
                return Variables.GetLocal(actor, "health").AsNumber();
            if (Variables.HasLocal(actor, "Health"))
                return Variables.GetLocal(actor, "Health").AsNumber();
            return 1;
        }

        private string NameOf(object actor)
        {
            return World.NameOf(actor);
        }

        private static bool IsContactKind(TriggerKind kind)
        {
            return kind == TriggerKind.Touches || kind == TriggerKind.Overlaps;
        }

        private static bool IsBuildingKind(TriggerKind kind)
        {
            return kind == TriggerKind.BuildingPlaced ||
                   kind == TriggerKind.ConstructionCompleted ||
                   kind == TriggerKind.BuildingDestroyed;
        }

        private static bool IsProductionKind(TriggerKind kind)
        {
            return kind == TriggerKind.TrainingStarted ||
                   kind == TriggerKind.TrainingFinished ||
                   kind == TriggerKind.ProductionQueueEmpty;
        }

        private static bool IsOrderKind(TriggerKind kind)
        {
            return kind == TriggerKind.OrderCompletes ||
                   kind == TriggerKind.OrderStarts ||
                   kind == TriggerKind.PathBlocked;
        }

        private Dictionary<string, int> BagOf(object owner)
        {
            if (!bags.TryGetValue(owner, out var bag))
            {
                bag = new Dictionary<string, int>();
                bags[owner] = bag;
            }
            return bag;
        }

        public bool Matches(SheetInstance sheet, TriggerDescriptor descriptor, PushSignal signal)
        {
            if (descriptor.Kind != signal.Kind)
                return false;
            // ---- local multiplayer (Task 16 Part F) ----
            // Player signals are broadcast with the number in the signal, so the filter is a
            // number, not a name. `when all players dead` has no number to match.
            if (descriptor.Kind == TriggerKind.PlayerJoins ||
                descriptor.Kind == TriggerKind.PlayerDies ||
                descriptor.Kind == TriggerKind.PlayerScoreChanges)
                return descriptor.Player == signal.PlayerA;
            if (descriptor.Kind == TriggerKind.PlayerKills)
                return descriptor.Player == signal.PlayerA && descriptor.Player2 == signal.PlayerB;
            // ---- fog of war (Task 18 Part F) ----
            // Area edges are broadcast per player; the bare trigger listens to the local
            // player (0), which is the view the bridge renders.
            if (descriptor.Kind == TriggerKind.AreaRevealed || descriptor.Kind == TriggerKind.AreaHidden)
                return descriptor.Player == signal.PlayerA;
            // `when <unit> enters fog` carries the unit in `Other` and its name in `Name`.
            if (descriptor.Kind == TriggerKind.EntersFog)
                return descriptor.RequireName == "me" || descriptor.RequireName == "my"
                    ? ReferenceEquals(signal.Other, sheet.Actor)
                    : string.Equals(signal.Name, descriptor.RequireName, StringComparison.OrdinalIgnoreCase);
            // `when message "damage <amount>" arrives` — the quoted text is a template whose
            // `<slot>` parts capture a number from the message (Task 18 Part G). The capture is
            // written onto the receiving sheet's actor by BindMessageCaptures before the event runs.
            if (descriptor.Kind == TriggerKind.Message && descriptor.MessagePattern)
                return MessagePattern.TryMatch(descriptor.RequireName, signal.Name, out _);
            if (descriptor.RequireName.Length == 0)
                return true;
            // `when me is selected` / `when me is deselected`: the signal is broadcast, so
            // filter it to the sheet's own actor. A named form (`when Marine is selected`)
            // matches on the signal name below.
            if (descriptor.Kind == TriggerKind.Selected || descriptor.Kind == TriggerKind.Deselected)
            {
                return descriptor.RequireName == "me"
                    ? ReferenceEquals(signal.Other, sheet.Actor)
                    : signal.Name == descriptor.RequireName;
            }
            // Building signals are broadcast with the template name in `Name` and the
            // placed actor in `Other`; `when me is placed` belongs to that actor's sheet.
            if (IsBuildingKind(descriptor.Kind))
            {
                return descriptor.RequireName == "me" || descriptor.RequireName == "my"
                    ? ReferenceEquals(signal.Other, sheet.Actor)
                    : signal.Name == descriptor.RequireName;
            }
            // Production signals carry the unit in `Name`, the building actor in `Other`
            // and the building template in `Material`. The trigger names both parts.
            if (IsProductionKind(descriptor.Kind))
            {
                if (descriptor.Kind == TriggerKind.ProductionQueueEmpty)
                {
                    if (descriptor.RequireName.Length == 0)
                        return true;
                    return descriptor.RequireName == "me" || descriptor.RequireName == "my"
                        ? ReferenceEquals(signal.Other, sheet.Actor)
                        : signal.Name == descriptor.RequireName;
                }
                if (descriptor.RequireUnit.Length > 0 &&
                    !string.Equals(signal.Name, descriptor.RequireUnit, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (descriptor.RequireName.Length == 0)
                    return true;
                if (descriptor.RequireName == "me" || descriptor.RequireName == "my")
                    return ReferenceEquals(signal.Other, sheet.Actor);
                return signal.Material == descriptor.RequireName || NameOf(signal.Other) == descriptor.RequireName;
            }
            // Order signals carry the order name in `Name` and its target in `Other`; the
            // bare trigger listens to the acting unit, the named form filters by order.
            if (IsOrderKind(descriptor.Kind))
            {
                if (descriptor.RequireName.Length == 0)
                    return true;
                if (descriptor.RequireName == "me" || descriptor.RequireName == "my")
                    return true;
                return string.Equals(signal.Name, descriptor.RequireName, StringComparison.OrdinalIgnoreCase);
            }
            // `when other touches me` / `when other overlaps me` means "any toucher".
            if (descriptor.RequireName == "me" && IsContactKind(descriptor.Kind))
                return true;
            // `when me is clicked` means "the actor this sheet is attached to was clicked".
            // Clicks are broadcast (so `when Ground is clicked` can live on the Player sheet),
            // which is why the name alone is not enough for the `me` form. TextBox edits/focus
            // and submits are broadcast the same way (Task 15 Part C).
            if (descriptor.RequireName == "me" && IsUiKind(descriptor.Kind))
                return ReferenceEquals(signal.Other, sheet.Actor);
            // Clip names are matched loosely: `play anim "Walk"` answers `when anim "walk" ends`.
            // Particle names match the same way: `play particle "Smoke"` answers `when particle "smoke" ends`.
            if (descriptor.Kind == TriggerKind.AnimEnd || descriptor.Kind == TriggerKind.ParticleEnd)
                return string.Equals(signal.Name, descriptor.RequireName, StringComparison.OrdinalIgnoreCase);
            // `when tween me ends` belongs to the tweened actor's own sheet; a named form
            // (`when tween PausePanel ends`) may live on any sheet because the signal is broadcast.
            if (descriptor.Kind == TriggerKind.TweenEnd)
                return descriptor.RequireName == "me"
                    ? ReferenceEquals(signal.Other, sheet.Actor)
                    : signal.Name == descriptor.RequireName;
            // Number keys: `when 1 is pressed` must answer the input layer's `D1` signal (and
            // tests/manual pushes of plain `1`). A player's sheet listens only to its own keys:
            // the signal carries the owning player (0 = a key nobody bound, e.g. P or Escape),
            // and the written key resolves through that player's binding before comparing.
            if (IsKeyKind(descriptor.Kind))
            {
                // Only a *player* sheet filters: global sheets (menus, the Game host) still hear
                // every key, so a menu can answer `when 1 is pressed`.
                if (sheet.Player != 0 && signal.PlayerA != 0 && signal.PlayerA != sheet.Player)
                    return false;
                return KeyNameMatches(ResolveKeyForSheet(sheet, descriptor.RequireName), signal.Name) &&
                       ExtraConditionsMatch(descriptor, sheet, signal.Other);
            }
            // Name match, or material match for `when me touches <material>`.
            if (signal.Name == descriptor.RequireName && ExtraConditionsMatch(descriptor, sheet, signal.Other))
                return true;
            if (descriptor.RequireMaterial.Length > 0 && signal.Material == descriptor.RequireMaterial && ExtraConditionsMatch(descriptor, sheet, signal.Other))
                return true;
            return false;
        }

        /// <summary>
        /// True when a <c>set</c> / <c>add</c> / <c>deduct</c> statement addresses a
        /// per-unit health stat (Task 18 Part G): either <c>my Health</c>, or the bare
        /// <c>Health</c> / <c>Shield</c> / <c>Armor</c> form on an actor's own sheet. A global
        /// sheet keeps writing the global variable of that name.
        /// </summary>
        private static bool IsOwnHealthTarget(EventContext context, VariableValue target)
        {
            if (target == null || !HealthManager.IsStat(target.Name))
                return false;
            if (context.Me == null || context.Me is GlobalHost)
                return false;
            return target.Owner == "my" || target.Owner == "me" || target.Owner.Length == 0;
        }

        /// <summary>
        /// Writes a pattern message's captured numbers onto the receiving sheet's actor, under
        /// each slot's own name, plus <c>my DamageTaken</c> for the last one (Task 18 Part G).
        /// </summary>
        private void BindMessageCaptures(SheetInstance sheet, TriggerDescriptor descriptor, PushSignal signal)
        {
            object actor = signal.MeOverride ?? sheet.Actor;
            if (actor == null || actor is GlobalHost)
                return;
            if (!MessagePattern.TryMatch(descriptor.RequireName, signal.Name, out var captures) || captures.Count == 0)
                return;
            double last = 0;
            foreach (var capture in captures)
            {
                Variables.SetLocal(actor, capture.Key, ValueBox.OfNumber(capture.Value));
                last = capture.Value;
            }
            Variables.SetLocal(actor, HealthManager.DamageTakenVariable, ValueBox.OfNumber(last));
        }

        /// <summary>Resolves the optional <c>of &lt;building&gt;</c> target for a build-progress
        /// statement. No target means the acting sheet's actor.</summary>
        private object ResolveBuildProgressTarget(EventContext context, string thing)
        {
            if (string.IsNullOrEmpty(thing) || thing == "me")
                return context.Me is GlobalHost ? null : context.Me;
            if (thing == "other")
                return context.Other is GlobalHost ? null : context.Other;
            object found = FindNamedActor(thing);
            return found is GlobalHost ? null : found;
        }

        /// <summary>Warns once per sheet when a build-progress line has no resolvable building.</summary>
        private void WarnBuildProgressTarget(EventContext context, string thing)
        {
            string key = "build-progress:" + (string.IsNullOrEmpty(thing) ? "me" : thing);
            if (context.Sheet != null && !context.Sheet.StepWarned.Add(key))
                return;
            if (string.IsNullOrEmpty(thing))
                OnRuntimeWarning("`set build progress` / `add to build progress` need an acting building — on a global sheet use `of <building>`.");
            else
                OnRuntimeWarning($"I couldn't find the building \"{thing}\" to change build progress.");
        }

        /// <summary>Warns once per sheet when `set build time` has no resolvable building.</summary>
        private void WarnBuildTimeTarget(EventContext context, string thing)
        {
            string key = "build-time:" + (string.IsNullOrEmpty(thing) ? "me" : thing);
            if (context.Sheet != null && !context.Sheet.StepWarned.Add(key))
                return;
            if (string.IsNullOrEmpty(thing))
                OnRuntimeWarning("`set build time` needs an acting building — on a global sheet use `of <building>`.");
            else
                OnRuntimeWarning($"I couldn't find the building \"{thing}\" to change build time.");
        }

        /// <summary>The template name stored for a placed building, falling back to the actor name.</summary>
        private string BuildingNameOf(object actor)
        {
            string name = Construction.BuildingOf(actor);
            return string.IsNullOrEmpty(name) ? NameOf(actor) : name;
        }

        private bool ExtraConditionsMatch(TriggerDescriptor descriptor, SheetInstance sheet, object other)
        {
            if (descriptor.ExtraConditions.Count == 0)
                return true;
            var ctx = new EventContext { Variables = Variables, World = World, Me = sheet.Actor, Other = other, Runtime = this, Sheet = sheet };
            foreach (var cond in descriptor.ExtraConditions)
                if (!Conditions.Evaluate(cond, ctx))
                    return false;
            return true;
        }

        private static bool IsClickKind(TriggerKind kind)
        {
            return kind == TriggerKind.Clicked ||
                   kind == TriggerKind.RightClicked ||
                   kind == TriggerKind.MiddleClicked ||
                   kind == TriggerKind.DoubleClicked ||
                   kind == TriggerKind.ButtonPressed ||
                   kind == TriggerKind.ButtonReleased;
        }

        /// <summary>UI signals broadcast by name (clicks, text edits, focus, submit). The
        /// <c>me</c> form matches the sheet's own actor; a named form matches the signal name.</summary>
        private static bool IsUiKind(TriggerKind kind)
        {
            return IsClickKind(kind) ||
                   kind == TriggerKind.TextChanged ||
                   kind == TriggerKind.Focused ||
                   kind == TriggerKind.LostFocus ||
                   kind == TriggerKind.Submitted;
        }

        private static bool IsKeyKind(TriggerKind kind)
        {
            return kind == TriggerKind.KeyPressed ||
                   kind == TriggerKind.KeyReleased ||
                   kind == TriggerKind.KeyHeld;
        }

        /// <summary>Compares key names after mapping `1` → `D1`, case-insensitively.</summary>
        private static bool KeyNameMatches(string required, string signal)
        {
            if (signal == null)
                return false;
            return string.Equals(
                ActionWords.NormalizeKeyName(required),
                ActionWords.NormalizeKeyName(signal),
                StringComparison.OrdinalIgnoreCase);
        }

        private void RunEvent(SheetInstance sheet, int eventIndex, object other, object meOverride = null)
        {
            var ev = sheet.Program.Events[eventIndex];
            var context = new EventContext
            {
                Variables = Variables,
                World = World,
                Me = meOverride ?? sheet.Actor,
                Other = other,
                Runtime = this,
                Sheet = sheet,
            };
            sheet.FiredThisTick[eventIndex] = true;
            RunBody(sheet, ev, context, 0, true);
        }

        private void RunBody(SheetInstance sheet, EventNode ev, EventContext context, int startLine, bool startGate, bool startConditionGate = false)
        {
            bool gate = startGate;
            // Gate of the nearest condition line, so `otherwise` can invert exactly that block.
            bool conditionGate = startConditionGate;
            // Conditions written one under another are one block and must all pass, so they
            // stack with AND. A statement or an `otherwise` closes the run, and the next
            // condition opens a fresh block (the pairs in a body stay independent).
            bool stacking = false;
            for (int i = startLine; i < ev.Body.Count; i++)
            {
                var line = ev.Body[i];
                if (line.IsCondition)
                {
                    var node = Conditions.Parse(line.Words);
                    if (node.Kind == ConditionKind.Unknown)
                        WarnUnknownCondition(sheet, line);
                    bool result = Conditions.Evaluate(node, context);
                    bool inverted = line.Words[0].Type == TokenType.Word && line.Words[0].Text == "unless";
                    conditionGate = inverted ? !result : result;
                    gate = stacking ? gate && conditionGate : conditionGate;
                    stacking = true;
                    continue;
                }
                if (line.IsOtherwise)
                {
                    gate = !conditionGate;
                    stacking = false;
                    continue;
                }
                if (!gate)
                {
                    stacking = false;
                    continue;
                }
                stacking = false;
                var statement = line.Statement;
                if (statement is SetNode set)
                {
                    ExecuteSet(context, set);
                }
                else if (statement is AddNode add)
                {
                    ExecuteAdd(context, add);
                }
                else if (statement is AddKillNode addKill)
                {
                    ExecuteAddKill(context, addKill);
                }
                else if (statement is DeductNode deduct)
                {
                    ExecuteDeduct(context, deduct);
                }
                else if (statement is BagRemoveNode bagRemove)
                {
                    ExecuteBagRemove(context, bagRemove);
                }
                else if (statement is EquipNode equip)
                {
                    ExecuteEquip(context, equip);
                }
                else if (statement is DropNode drop)
                {
                    ExecuteDrop(context, drop);
                }
                else if (statement is SetTextNode setText)
                {
                    OnSetText(context, setText.Target, context.Evaluate(setText.Value));
                }
                else if (statement is SetButtonEnabledNode setButton)
                {
                    OnSetButtonEnabled(context, setButton.Thing, setButton.Enabled);
                }
                else if (statement is SetImageBrushNode setImage)
                {
                    OnSetImageBrush(context, setImage.Thing, setImage.Sprite);
                }
                else if (statement is SetControlColorNode setColor)
                {
                    OnSetControlColor(context, setColor.Thing, setColor.Color);
                }
                else if (statement is SetProgressNode setProgress)
                {
                    OnSetProgress(context, setProgress.Thing, context.Evaluate(setProgress.Value));
                }
                else if (statement is SetSliderNode setSlider)
                {
                    OnSetSlider(context, setSlider.Thing, context.Evaluate(setSlider.Value));
                }
                else if (statement is SetStateNode setState)
                {
                    ExecuteSetState(context, setState);
                }
                else if (statement is SetControlGroupNode setControlGroup)
                {
                    ExecuteSetControlGroup(context, setControlGroup);
                }
                else if (statement is SetBuildProgressNode setBuildProgress)
                {
                    object target = ResolveBuildProgressTarget(context, setBuildProgress.Thing);
                    if (target == null)
                        WarnBuildProgressTarget(context, setBuildProgress.Thing);
                    else
                        Construction.SetProgress(context.Variables, target,
                            context.Evaluate(setBuildProgress.Value).AsNumber());
                }
                else if (statement is AddBuildProgressNode addBuildProgress)
                {
                    object target = ResolveBuildProgressTarget(context, addBuildProgress.Thing);
                    if (target == null)
                        WarnBuildProgressTarget(context, addBuildProgress.Thing);
                    else
                        Construction.AddProgress(context.Variables, target,
                            context.Evaluate(addBuildProgress.Amount).AsNumber());
                }
                else if (statement is SetBuildTimeNode setBuildTime)
                {
                    object target = ResolveBuildProgressTarget(context, setBuildTime.Thing);
                    if (target == null)
                        WarnBuildTimeTarget(context, setBuildTime.Thing);
                    else
                        Production.SetBuildTime(context.Variables, target, BuildingNameOf(target),
                            context.Evaluate(setBuildTime.Value).AsNumber());
                }
                else if (statement is PlayStepSoundNode step)
                {
                    ExecutePlayStepSound(context, step);
                }
                else if (statement is SetWeaponNode setWeapon)
                {
                    ExecuteSetWeapon(context, setWeapon);
                }
                else if (statement is SetAmmoNode setAmmo)
                {
                    string weapon = ResolveWeaponName(context, setAmmo.Weapon, setAmmo.WeaponVariable, "set ammo");
                    if (weapon.Length > 0)
                        WeaponData.WriteAmmo(context, weapon, context.Evaluate(setAmmo.Value).AsNumber());
                }
                else if (statement is AddAmmoNode addAmmo)
                {
                    string weapon = ResolveWeaponName(context, addAmmo.Weapon, addAmmo.WeaponVariable, "add to ammo");
                    if (weapon.Length > 0)
                    {
                        double held = WeaponData.ReadAmmo(context, weapon);
                        WeaponData.WriteAmmo(context, weapon, held + context.Evaluate(addAmmo.Amount).AsNumber());
                    }
                }
                else if (statement is ReloadNode reload)
                {
                    ExecuteReload(context, reload);
                }
                else if (statement is RawActionNode raw)
                {
                    // `enemy type "Zombie"` — Task 16 Part D. The typed stats are plain variables,
                    // so this stays in the engine-independent runtime and needs no engine action.
                    if (raw.Words.Count >= 3 && raw.Words[0].Text == "enemy" && raw.Words[1].Text == "type")
                    {
                        // `enemy type "Zombie"` names the type; `enemy type my name` takes it from
                        // a variable, so one shared sheet can serve every armed type (TK_Gunner).
                        string typeName = raw.Words[2].Text == "my" && raw.Words.Count >= 4
                            ? context.ReadMy(raw.Words[3].Text).AsText()
                            : raw.Words[2].Text;
                        // A name can carry a suffix a spawned copy picks up ("Pistolboy 2"), so
                        // fall back to matching the actor name against the known types.
                        string resolved = EnemyTypes.CanonicalFor(context.Variables, typeName) ??
                                          EnemyTypes.TypeForActorName(context.Variables, typeName) ?? typeName;
                        if (!EnemyTypes.Apply(context, resolved))
                            OnRuntimeWarning($"I don't know the enemy type \u201C{typeName}\u201D \u2014 the built-in types are {EnemyTypes.KnownTypeNames()}, and a new type needs its own `<Type><Stat>` variables (see EnemyTypes.events).");
                        continue;
                    }
                    // ---- level progression & statistics (Task 16 Part G) ----
                    // All engine-free, so the stats and the level hand-off are the same code the
                    // tests drive; only the actual scene load is the bridge's job.
                    if (raw.Words.Count >= 2 && raw.Words[0].Text == "next" && raw.Words[1].Text == "level")
                    {
                        ExecuteNextLevel();
                        continue;
                    }
                    if (raw.Words.Count >= 3 && raw.Words[0].Text == "set" && raw.Words[1].Text == "level" &&
                        raw.Words[2].Text == "to")
                    {
                        ExecuteSetLevel(context, raw.Words);
                        continue;
                    }
                    if (raw.Words.Count >= 2 && raw.Words[0].Text == "save" && raw.Words[1].Text == "stats")
                    {
                        Variables.SaveStats();
                        continue;
                    }
                    if (raw.Words.Count >= 2 && raw.Words[0].Text == "load" && raw.Words[1].Text == "stats")
                    {
                        if (!Variables.LoadStats())
                            OnRuntimeWarning("`load stats` found nothing saved yet — `save stats` first.");
                        continue;
                    }
                    if (raw.Words.Count >= 2 && raw.Words[0].Text == "wipe" && raw.Words[1].Text == "game")
                    {
                        Variables.WipeGlobals();
                        Levels.Reseed(Variables);
                        continue;
                    }
                    // A `spawn Bullet` on a player's sheet is a shot fired — the accuracy
                    // denominator. Enemy sheets shooting (`Turret.events`) don't count.
                    if (raw.Words.Count >= 2 && raw.Words[0].Text == "spawn" && raw.Words[1].Text == "Bullet" &&
                        context.Sheet != null && Players.IsPlayerActorName(context.Sheet.ActorName))
                        Levels.CountShot(Variables);
                    if (IsStopThisEvent(raw))
                    {
                        context.Stopped = true;
                        return;
                    }
                    if (raw.Words.Count >= 2 && raw.Words[0].Text == "wait")
                    {
                        if (TryReadWaitSeconds(raw.Words, out double seconds))
                        {
                            sheet.Waiting.Add(new PendingRun
                            {
                                Sheet = sheet,
                                Event = ev,
                                Context = context,
                                LineIndex = i + 1,
                                Gate = gate,
                                ConditionGate = conditionGate,
                                ResumeAt = Time + seconds,
                            });
                            return;
                        }
                    }
                    if (IsSend(raw))
                    {
                        ExecuteSend(context, raw.Words);
                        continue;
                    }
                    // Per-player input (Task 16 input follow-ups): all engine-free, so the
                    // capture and persistence paths are the same ones the tests drive.
                    if (raw.Words.Count >= 2 && raw.Words[0].Text == "rebind")
                    {
                        ExecuteRebind(context, raw.Words);
                        continue;
                    }
                    if (raw.Words.Count >= 2 && raw.Words[0].Text == "save" && raw.Words[1].Text == "bindings")
                    {
                        Input.SaveTo(Variables);
                        continue;
                    }
                    if (raw.Words.Count >= 2 && raw.Words[0].Text == "load" && raw.Words[1].Text == "bindings")
                    {
                        Input.LoadFrom(Variables);
                        continue;
                    }
                    OnRawAction(context, raw.Words);
                }
            }
        }

        private void ExecuteSet(EventContext context, SetNode set)
        {
            ValueBox value = context.Evaluate(set.Value);
            bool healthTarget = IsOwnHealthTarget(context, set.Target);
            if (set.Op != '\0' && set.Right != null)
            {
                ValueBox right = context.Evaluate(set.Right);
                ValueBox result;
                if (set.Op == '+' && (value.Kind == ValueKind.Text || right.Kind == ValueKind.Text))
                    result = ValueBox.OfText(value.AsText() + right.AsText());
                else
                {
                    double leftNumber = value.AsNumber();
                    double rightNumber = right.AsNumber();
                    switch (set.Op)
                    {
                        case '+': result = ValueBox.OfNumber(leftNumber + rightNumber); break;
                        case '-': result = ValueBox.OfNumber(leftNumber - rightNumber); break;
                        case '*': result = ValueBox.OfNumber(leftNumber * rightNumber); break;
                        case '/': result = ValueBox.OfNumber(rightNumber == 0 ? 0 : leftNumber / rightNumber); break;
                        default: result = ValueBox.OfNumber(0); break;
                    }
                }
                value = result;
            }
            // Per-unit health (Task 18 Part G): `set my Health to 60` clamps to MaxHealth,
            // and the bare `set Health to 60` form addresses the acting unit's own health.
            if (healthTarget && HealthManager.IsStat(set.Target.Name))
                HealthManager.Set(context.Variables, context.Me, set.Target.Name, value.AsNumber());
            else if (set.Target.Owner == "my")
            {
                context.WriteMy(set.Target.Name, value);
                // A typo must not quietly turn an air unit into a ground one (Task 22 Part A).
                // The write still happens: the sheet may correct itself later.
                if (UnitTypes.IsVariableName(set.Target.Name) && !UnitTypes.IsKnown(value.AsText()))
                    OnRuntimeWarning($"I don't know the unit type \u201C{value.AsText()}\u201D \u2014 units are {UnitTypes.KnownNames()}, and a sheet that writes none counts as {UnitTypes.Default}.");
            }
            else
                context.WriteGlobal(set.Target.Name, value);
        }

        /// <summary>
        /// <c>set weapon to &lt;name&gt;</c> / <c>weapon &lt;name&gt;</c> — Task 16 Part C. The name is
        /// stored in <c>my Weapon</c>; switching drops a reload in progress so the new weapon
        /// starts ready. An unknown name is still recorded (the sheet may define it later) but
        /// warns once, like `enemy type`.
        /// </summary>
        private void ExecuteSetWeapon(EventContext context, SetWeaponNode node)
        {
            string name = context.Evaluate(node.Value).AsText();
            if (string.IsNullOrEmpty(name))
            {
                OnRuntimeWarning("`set weapon` needs a weapon name, like `set weapon to \"Pistol\"`.");
                return;
            }
            string canonical = WeaponData.CanonicalFor(context.Variables, name);
            if (canonical == null)
                OnRuntimeWarning($"I don't know the weapon \u201C{name}\u201D \u2014 the built-in weapons are {WeaponData.KnownNames()}, and a new one needs its own `<Weapon><Field>` variables (see WeaponData.events).");
            Weapons.Cancel(context.Me);
            context.WriteMy(WeaponData.CurrentWeaponVariable, ValueBox.OfText(canonical ?? name));
        }

        /// <summary>
        /// <c>reload</c> — starts the current weapon's reload timer. The timer is what makes
        /// `reloading` true and later raises `when reload completes`.
        /// </summary>
        private void ExecuteReload(EventContext context, ReloadNode node)
        {
            // A bare `reload` reloads whatever is held; `reload Shotgun` names one.
            string weapon = string.IsNullOrEmpty(node.Weapon) && string.IsNullOrEmpty(node.WeaponVariable)
                ? context.ReadMy(WeaponData.CurrentWeaponVariable).AsText()
                : ResolveWeaponName(context, node.Weapon, node.WeaponVariable, "reload");
            double seconds = WeaponClock.DefaultReload;
            if (weapon.Length > 0 && WeaponData.TryGet(context.Variables, weapon, out WeaponStats stats))
                seconds = stats.Reload;
            else if (weapon.Length > 0)
                OnRuntimeWarning($"I don't know the weapon \u201C{weapon}\u201D, so `reload` uses {WeaponClock.DefaultReload} seconds.");
            Weapons.Start(context.Me, seconds, Time);
        }

        /// <summary>
        /// The weapon an ammo line names. A blank name means `my Weapon` (whatever is held), and
        /// a sheet that has not set one gets a warning instead of silently writing `Ammo`.
        /// </summary>
        private string ResolveWeaponName(EventContext context, string weapon, string variable, string line)
        {
            if (!string.IsNullOrEmpty(variable))
                weapon = context.ReadMy(variable).AsText();
            if (string.IsNullOrEmpty(weapon))
            {
                OnRuntimeWarning($"`{line}` needs a weapon \u2014 set one first with `set weapon to \"Pistol\"`.");
                return "";
            }
            return weapon;
        }

        private void ExecuteSetState(EventContext context, SetStateNode setState)
        {
            var sheet = context.Sheet;
            sheet.LastState = sheet.CurrentState;
            sheet.CurrentState = setState.State;
            // Entry edge: fire every StateIs trigger on this sheet that matches the new state.
            for (int eventIndex = 0; eventIndex < sheet.Program.Events.Count; eventIndex++)
            {
                if (sheet.FiredThisTick[eventIndex])
                    continue;
                var desc = sheet.Triggers[eventIndex];
                if (desc.Kind == TriggerKind.StateIs && desc.RequireName == setState.State)
                    RunEvent(sheet, eventIndex, context.Me);
            }
        }

        /// <summary>
        /// <c>set my control group to &lt;n&gt;</c>. On a unit's own sheet it adds that unit
        /// to the group; on the global sheet it stores the current selection as the group.
        /// </summary>
        private void ExecuteSetControlGroup(EventContext context, SetControlGroupNode node)
        {
            if (!Selection.IsValidGroup(node.Group))
            {
                OnRuntimeWarning($"control groups are 1 to 0 (ten groups) — {node.Group} was skipped.");
                return;
            }
            if (context.Me == null || context.Me is GlobalHost)
                Selection.SetControlGroup(node.Group, Selection.Selected);
            else
                Selection.AddToControlGroup(node.Group, context.Me);
        }

        /// <summary>
        /// <c>add 1 to kills &lt;type&gt;</c> — one kill for that enemy type and for
        /// <c>Kills Total</c> (Task 16 Part G). <c>add 1 to kills my Type</c> reads the type
        /// from the actor, so one sheet can serve every enemy type.
        /// </summary>
        private void ExecuteAddKill(EventContext context, AddKillNode node)
        {
            string type = node.TypeVariable.Length > 0
                ? context.ReadMy(node.TypeVariable).AsText()
                : node.EnemyType;
            if (string.IsNullOrEmpty(type))
            {
                OnRuntimeWarning("`add ... to kills my Type` needs `enemy type <name>` to have run first — nothing was counted.");
                return;
            }
            string canonical = EnemyTypes.CanonicalFor(context.Variables, type) ?? type;
            Levels.CountKill(canonical, context.Evaluate(node.Amount).AsNumber(), Variables);
        }

        /// <summary>
        /// <c>next level</c>: raises <c>when level completes</c> and asks the bridge for the
        /// next scene in the level order. The load itself happens at the end of the tick, so
        /// the events queued with the signal still run (see <see cref="Tick"/>).
        /// </summary>
        private void ExecuteNextLevel()
        {
            RaisePushAll(new PushSignal { Kind = TriggerKind.LevelComplete });
            string next = Levels.NextName();
            if (string.IsNullOrEmpty(next))
            {
                OnRuntimeWarning(Levels.Order.Count == 0
                    ? "`next level` needs a level order — the bridge fills it from the project's scenes; on a level that is not in it, use `load scene \"<name>\"`."
                    : $"\u201C{Levels.Current}\u201D is the last level, so `next level` has nowhere to go.");
                return;
            }
            pendingLevel = next;
        }

        /// <summary><c>set level to &lt;n&gt;</c>: records the run's level without loading it.</summary>
        private void ExecuteSetLevel(EventContext context, List<Token> words)
        {
            if (words.Count < 4)
            {
                OnRuntimeWarning("`set level` looks like this: `set level to 2`");
                return;
            }
            double value;
            if (words[3].Type == TokenType.Number)
                value = words[3].Number;
            else if (words[3].Type == TokenType.Word)
                value = context.ReadGlobal(words[3].Text).AsNumber();
            else
            {
                OnRuntimeWarning("`set level` takes a level number, like `set level to 2`");
                return;
            }
            Levels.SetNumber(value, Variables);
        }

        private void ExecuteAdd(EventContext context, AddNode add)
        {
            // `add Sword to my bag` puts a named item in the bag (counted). The amount names
            // the item — it is not read as a numeric value. Anything else is a normal variable add.
            if (add.Target.Owner == "my" && add.Target.Name == "bag")
            {
                string thing = add.Amount switch
                {
                    VariableValue vv => vv.Name,
                    TextValue tv => tv.Text,
                    _ => null,
                };
                if (thing != null && thing.Length > 0)
                    context.Runtime.BagAdd(context.Me, thing);
                return;
            }
            double amount = context.Evaluate(add.Amount).AsNumber();
            // Per-unit health (Task 18 Part G): `add -10 to Health` / `add 20 to Shield` go
            // through the health bounds (and a negative Health add is the damage path).
            if (IsOwnHealthTarget(context, add.Target))
            {
                HealthManager.Add(context.Variables, context.Me, add.Target.Name, amount);
                return;
            }
            ValueBox current = add.Target.Owner == "my"
                ? context.ReadMy(add.Target.Name)
                : context.ReadGlobal(add.Target.Name);
            ValueBox updated = ValueBox.OfNumber(current.AsNumber() + amount);
            if (add.Target.Owner == "my")
                context.WriteMy(add.Target.Name, updated);
            else
                context.WriteGlobal(add.Target.Name, updated);
        }

        /// <summary><c>deduct &lt;n&gt; from &lt;resource&gt;</c>: a normal subtraction that
        /// never goes below 0 (spending can't leave a negative stockpile).</summary>
        private void ExecuteDeduct(EventContext context, DeductNode deduct)
        {
            double amount = context.Evaluate(deduct.Amount).AsNumber();
            // `deduct 10 from Health` is damage like `add -10 to Health` (Task 18 Part G).
            if (IsOwnHealthTarget(context, deduct.Target))
            {
                HealthManager.Add(context.Variables, context.Me, deduct.Target.Name, -amount);
                return;
            }
            ValueBox current = deduct.Target.Owner == "my"
                ? context.ReadMy(deduct.Target.Name)
                : context.ReadGlobal(deduct.Target.Name);
            double updated = Math.Max(0, current.AsNumber() - amount);
            if (deduct.Target.Owner == "my")
                context.WriteMy(deduct.Target.Name, ValueBox.OfNumber(updated));
            else
                context.WriteGlobal(deduct.Target.Name, ValueBox.OfNumber(updated));
        }

        private void ExecuteBagRemove(EventContext context, BagRemoveNode node)
        {
            context.Runtime.BagRemove(context.Me, node.Thing);
        }

        private void ExecuteEquip(EventContext context, EquipNode node)
        {
            string thing = node.Thing;
            // `equip <thing>` takes the item from the bag and links the item's sheet to me.
            if (!context.Runtime.BagHas(context.Me, thing))
                return;
            context.Runtime.BagRemove(context.Me, thing);
            context.Runtime.EquipItem(context.Me, thing);
            // the item's `gets equipped` event runs with the wearer as `me`
            foreach (var sheet in context.Runtime.SheetsUsingSource(thing))
                sheet.PushQueue.Enqueue(new PushSignal { Kind = TriggerKind.Equipped, MeOverride = context.Me });
        }

        private void ExecuteDrop(EventContext context, DropNode node)
        {
            string thing = node.Thing;
            bool hadIt = context.Runtime.IsEquipped(context.Me, thing) || context.Runtime.BagHas(context.Me, thing);
            if (!hadIt)
                return;
            // unequip if worn: `gets unequipped` runs with the wearer as `me`
            if (context.Runtime.IsEquipped(context.Me, thing))
            {
                context.Runtime.UnequipItem(context.Me, thing);
                foreach (var sheet in context.Runtime.SheetsUsingSource(thing))
                    sheet.PushQueue.Enqueue(new PushSignal { Kind = TriggerKind.Unequipped, MeOverride = context.Me });
            }
            // take it from the bag if it's still there
            if (context.Runtime.BagHas(context.Me, thing))
                context.Runtime.BagRemove(context.Me, thing);
            // spawn the item at my feet happens on the engine side; here we raise the item's
            // `gets dropped` event so its sheet runs with `me` = the item itself again
            foreach (var sheet in context.Runtime.SheetsUsingSource(thing))
                sheet.PushQueue.Enqueue(new PushSignal { Kind = TriggerKind.Dropped });
        }

        private void ExecutePlayStepSound(EventContext context, PlayStepSoundNode step)
        {
            var sheet = context.Sheet;
            if (sheet == null)
                return;
            string material = step.Material;
            if (!sheet.StepSoundMap.TryGetValue(material, out string clipName) || string.IsNullOrEmpty(clipName))
            {
                // Warn once per missing material, then skip.
                string key = sheet.ActorName + ": step sound " + material;
                if (!sheet.StepWarned.Contains(key))
                {
                    sheet.StepWarned.Add(key);
                    OnRuntimeWarning($"\"{sheet.ActorName}\": no clip mapped for material \"{material}\" — `play step sound \"{material}\"` skipped.");
                }
                return;
            }
            // Cooldown: at most one step sound per material per StepCooldown seconds.
            double now = Time;
            double last = sheet.StepLastPlay.TryGetValue(material, out double v) ? v : double.MinValue;
            if (now - last < sheet.StepCooldown)
                return;
            sheet.StepLastPlay[material] = now;
            OnPlayStepSound(context, material, clipName);
        }

        /// <summary>Renders a bag's contents as text: "Sword x2, Potion" (count 1 shows bare).</summary>
        public string BagText(object owner)
        {
            if (!bags.TryGetValue(owner, out var bag) || bag.Count == 0)
                return "";
            var parts = new List<string>();
            foreach (var pair in bag)
                parts.Add(pair.Value == 1 ? pair.Key : pair.Key + " x" + pair.Value);
            return string.Join(", ", parts);
        }

        private void ExecuteSend(EventContext context, List<Token> words)
        {
            if (words.Count < 3 || words[2].Type != TokenType.Str)
                return;
            string message = words[2].Text;
            string targetName = "";
            if (words.Count >= 5 && words[3].Type == TokenType.Word && words[3].Text == "to")
            {
                // A target can be several words now: `player 2`, `all players`, `nearest player`.
                var parts = new System.Text.StringBuilder();
                for (int i = 4; i < words.Count; i++)
                {
                    if (parts.Length > 0)
                        parts.Append(' ');
                    parts.Append(words[i].Text);
                }
                targetName = parts.ToString();
            }
            var signal = new PushSignal { Kind = TriggerKind.Message, Name = message, Other = context.Me };
            if (targetName == "me")
            {
                FindSheetFor(context.Me)?.PushQueue.Enqueue(signal);
                return;
            }
            if (targetName == "other")
            {
                DeliverToSheet(FindSheetFor(context.Other), signal);
                return;
            }
            SendRawMessage(signal, targetName);
        }

        private static bool IsStopThisEvent(RawActionNode raw)
        {
            return raw.Words.Count >= 3 && raw.Words[0].Text == "stop" &&
                   raw.Words[1].Text == "this" && raw.Words[2].Text == "event";
        }

        /// <summary><c>rebind player &lt;n&gt; &lt;action&gt;</c> — arms a capture (see <see cref="BeginRebind"/>).</summary>
        private void ExecuteRebind(EventContext context, List<Token> words)
        {
            if (words.Count < 4 || words[1].Text != "player" || words[2].Type != TokenType.Number)
            {
                OnRuntimeWarning("`rebind` looks like this: `rebind player 2 Shoot`");
                return;
            }
            var parts = new System.Text.StringBuilder();
            for (int i = 3; i < words.Count; i++)
            {
                if (parts.Length > 0)
                    parts.Append(' ');
                parts.Append(words[i].Text);
            }
            BeginRebind(context, (int)words[2].Number, parts.ToString());
        }

        private static bool IsSend(RawActionNode raw)
        {
            return raw.Words.Count >= 2 && raw.Words[0].Text == "send" && raw.Words[1].Text == "message";
        }

        private static bool TryReadWaitSeconds(List<Token> words, out double seconds)
        {
            seconds = 0;
            if (words.Count >= 2 && words[1].Type == TokenType.Number)
            {
                seconds = words[1].Number;
                return true;
            }
            if (words.Count >= 5 && words[1].Text == "random" && words[2].Type == TokenType.Number &&
                words[3].Text == "to" && words[4].Type == TokenType.Number)
            {
                var range = new RandomRangeValue
                {
                    Min = new NumberValue { Value = words[2].Number },
                    Max = new NumberValue { Value = words[4].Number },
                };
                seconds = ValueEvaluator.Evaluate(range, null).AsNumber();
                return true;
            }
            return false;
        }

        private void TryPolled(SheetInstance sheet, int eventIndex)
        {
            var descriptor = sheet.Triggers[eventIndex];
            var ev = sheet.Program.Events[eventIndex];
            if (ev.RequiredState.Length > 0 && sheet.CurrentState != ev.RequiredState)
                return;
            switch (descriptor.Kind)
            {
                case TriggerKind.KeyHeld:
                    if (IsKeyHeldFor(sheet, descriptor.RequireName))
                        RunEvent(sheet, eventIndex, null);
                    break;
                case TriggerKind.ControlGroupHeld:
                    if (World.IsKeyHeld(descriptor.Key))
                        RunEvent(sheet, eventIndex, null);
                    break;
                case TriggerKind.Every:
                {
                    if (Time - sheet.LastFire[eventIndex] >= descriptor.Seconds)
                    {
                        sheet.LastFire[eventIndex] = Time;
                        RunEvent(sheet, eventIndex, null);
                    }
                    break;
                }
                case TriggerKind.Touches:
                    if (PollTouches)
                        PollContact(sheet, eventIndex, descriptor);
                    break;
                case TriggerKind.Overlaps:
                    if (PollOverlaps)
                        PollContact(sheet, eventIndex, descriptor);
                    break;
                case TriggerKind.WatchBecomes:
                case TriggerKind.WatchBecomesNot:
                case TriggerKind.WatchChanges:
                    TickWatch(sheet, eventIndex, descriptor);
                    break;
                case TriggerKind.ResourceChanges:
                    TickResourceChanges(sheet, eventIndex);
                    break;
                case TriggerKind.Arrives:
                    PollArrival(sheet, eventIndex, descriptor);
                    break;
                case TriggerKind.Sees:
                case TriggerKind.StopsSeeing:
                    PollSight(sheet, eventIndex, descriptor);
                    break;
                case TriggerKind.Hears:
                    PollHearing(sheet, eventIndex, descriptor);
                    break;
                case TriggerKind.HealthLow:
                    TickHealthLow(sheet, eventIndex, descriptor);
                    break;
                case TriggerKind.ReloadComplete:
                {
                    int completed = Weapons.Completion(sheet.Actor, Time);
                    if (completed <= sheet.ReloadSeen[eventIndex])
                        break;
                    sheet.ReloadSeen[eventIndex] = completed;
                    RunEvent(sheet, eventIndex, null);
                    break;
                }
                case TriggerKind.Near:
                    PollNear(sheet, eventIndex, descriptor);
                    break;
                case TriggerKind.Hovered:
                    PollHover(sheet, eventIndex, descriptor);
                    break;
                case TriggerKind.StateIs:
                    if (sheet.CurrentState == descriptor.RequireName && sheet.LastState != descriptor.RequireName)
                        RunEvent(sheet, eventIndex, null);
                    break;
            }
        }

        /// <summary>Sight triggers: distance check + line of sight, both edge triggers.
        /// <c>Sees</c> fires once when a matching target comes into sight; <c>StopsSeeing</c>
        /// fires once when a seen target leaves sight or range. A distance-less
        /// <c>StopsSeeing</c> reuses the last range a <c>Sees</c> sighted that target at.</summary>
        private void PollSight(SheetInstance sheet, int eventIndex, TriggerDescriptor descriptor)
        {
            var inSight = sheet.InSight[eventIndex];
            var seeing = new List<object>();
            double range = descriptor.Distance;
            // `when me sees Player by my SightDistance`: the range is the sheet actor's own stat.
            if (!string.IsNullOrEmpty(descriptor.DistanceVariable))
                range = Variables.HasLocal(sheet.Actor, descriptor.DistanceVariable)
                    ? Variables.GetLocal(sheet.Actor, descriptor.DistanceVariable).AsNumber()
                    : double.MaxValue;
            if (descriptor.Kind == TriggerKind.StopsSeeing && range < 0)
                range = sheet.LastSeenRange.GetValueOrDefault(descriptor.RequireName, double.MaxValue);
            foreach (object other in World.Actors())
            {
                if (other == null || ReferenceEquals(other, sheet.Actor))
                    continue;
                if (!NameMatches(descriptor, other))
                    continue;
                if (range >= 0 && World.Distance(sheet.Actor, other) > range)
                    continue;
                if (!World.HasLineOfSight(sheet.Actor, other))
                    continue;
                seeing.Add(other);
            }
            if (descriptor.Kind == TriggerKind.Sees)
            {
                if (range >= 0 && descriptor.RequireName.Length > 0)
                    sheet.LastSeenRange[descriptor.RequireName] = range;
                foreach (object other in seeing)
                {
                    if (inSight.Contains(other))
                        continue; // still in sight — the enter edge fired already
                    if (sheet.FiredThisTick[eventIndex])
                        continue; // one sighting per tick, try the rest next tick
                    inSight.Add(other);
                    RunEvent(sheet, eventIndex, other);
                }
                inSight.RemoveWhere(other => !seeing.Contains(other));
                return;
            }
            // StopsSeeing — falling edge: fire once per target that was seen and isn't anymore.
            var stillSeeing = new HashSet<object>(seeing);
            var lostSight = new List<object>();
            foreach (object other in inSight)
                if (!stillSeeing.Contains(other))
                    lostSight.Add(other);
            foreach (object other in lostSight)
            {
                if (sheet.FiredThisTick[eventIndex])
                    continue;
                inSight.Remove(other);
                RunEvent(sheet, eventIndex, other);
            }
            foreach (object other in seeing)
                if (!inSight.Contains(other))
                    inSight.Add(other); // (re)sighted — arms a future exit edge
        }

        /// <summary>
        /// <c>when me hears &lt;sound&gt; by &lt;dist&gt;</c> — Task 16 Part B. Sounds come off the
        /// <see cref="SoundBus"/>, so the same tick can serve several sheets. Each sound fires a
        /// trigger once: its id is remembered in <see cref="SheetInstance.Heard"/>, and forgotten
        /// when the sound falls off the bus. The sound's source actor arrives as `other`, which is
        /// what makes `goto other` walk toward the shot.
        /// </summary>
        private void PollHearing(SheetInstance sheet, int eventIndex, TriggerDescriptor descriptor)
        {
            var heard = sheet.Heard[eventIndex];
            var live = new HashSet<int>();
            foreach (HeardSound sound in Sounds.Recent)
                live.Add(sound.Id);
            heard.RemoveWhere(id => !live.Contains(id));

            // `by <dist>` narrows the sound's own radius; `by my Hearing` reads the actor's stat.
            double range = descriptor.Distance;
            if (!string.IsNullOrEmpty(descriptor.DistanceVariable))
                range = Variables.HasLocal(sheet.Actor, descriptor.DistanceVariable)
                    ? Variables.GetLocal(sheet.Actor, descriptor.DistanceVariable).AsNumber()
                    : 0;

            foreach (HeardSound sound in Sounds.Audible(descriptor.RequireName, sheet.Actor, World, range))
            {
                if (heard.Contains(sound.Id))
                    continue;
                if (sheet.FiredThisTick[eventIndex])
                    return; // one hearing per tick — the next sound is still on the bus tomorrow
                heard.Add(sound.Id);
                RunEvent(sheet, eventIndex, sound.Source);
            }
        }

        /// <summary>Enter-edge distance contact: fires once per pair, again only after separation.</summary>
        private void PollContact(SheetInstance sheet, int eventIndex, TriggerDescriptor descriptor)
        {
            HashSet<object> contacts = sheet.Contacts[eventIndex];
            HashSet<object> polled = sheet.PolledContacts[eventIndex];
            var touching = new List<object>();
            foreach (object other in World.Actors())
            {
                if (other == null || World.IsDescendantOf(other, sheet.Actor))
                    continue;
                if (!NameMatches(descriptor, other) && !MaterialMatches(descriptor, other))
                    continue;
                if (!World.IsTouching(sheet.Actor, other))
                    continue;
                touching.Add(other);
            }
            contacts.RemoveWhere(other => !touching.Contains(other));
            polled.RemoveWhere(other => !touching.Contains(other));
            foreach (object other in touching)
            {
                if (sheet.FiredThisTick[eventIndex])
                    continue; // this event already ran this tick; try the pair next tick
                // A name match (bumping an actor, `when other touches me`) is an event:
                // enter-edge — fire once per pair, re-arm after separation. A material-only
                // match (standing on a surface, `when me touches Grass`) is a continuous
                // state: fire every tick so `play step sound` can repeat with its cooldown
                // and the `me is moving` condition (re-checked each tick) gates silence.
                bool matchedByName = NameMatches(descriptor, other);
                if (descriptor.ExtraConditions.Count > 0)
                {
                    var ctx = new EventContext { Variables = Variables, World = World, Me = sheet.Actor, Other = other, Runtime = this, Sheet = sheet };
                    bool allMatch = true;
                    foreach (var cond in descriptor.ExtraConditions)
                        if (!Conditions.Evaluate(cond, ctx)) { allMatch = false; break; }
                    if (!allMatch) continue;
                }
                if (matchedByName)
                {
                    if (contacts.Contains(other))
                        continue; // still touching the same pair — enter edge only
                    contacts.Add(other);
                    polled.Add(other);
                }
                RunEvent(sheet, eventIndex, other);
            }
        }

        /// <summary>Enter-edge distance near: fires once per target that comes within
        /// <c>descriptor.Distance</c>, then re-arms after the target leaves that range.</summary>
        private void PollNear(SheetInstance sheet, int eventIndex, TriggerDescriptor descriptor)
        {
            if (descriptor.RequireName.Length == 0 || descriptor.Distance < 0)
                return;
            var contacts = sheet.Contacts[eventIndex];
            var inRange = new List<object>();
            foreach (object other in World.Actors())
            {
                if (other == null || ReferenceEquals(other, sheet.Actor))
                    continue;
                if (!NameMatches(descriptor, other))
                    continue;
                if (World.Distance2D(sheet.Actor, other) <= descriptor.Distance)
                {
                    inRange.Add(other);
                }
            }
            contacts.RemoveWhere(other => !inRange.Contains(other));
            foreach (object other in inRange)
            {
                if (contacts.Contains(other))
                    continue; // still in range — enter edge fired already
                if (sheet.FiredThisTick[eventIndex])
                    continue; // one near firing per tick
                contacts.Add(other);
                RunEvent(sheet, eventIndex, other);
            }
        }

        /// <summary>Steady hover: fires once per tick while the cursor is over <c>me</c>.
        /// A hotspot shows its hint text for as long as the cursor pauses on it.</summary>
        private void PollHover(SheetInstance sheet, int eventIndex, TriggerDescriptor descriptor)
        {
            if (sheet.Actor == null)
                return;
            if (!World.IsHovered(sheet.Actor))
                return;
            RunEvent(sheet, eventIndex, null);
        }

        private bool NameMatches(TriggerDescriptor descriptor, object other)
        {
            if (descriptor.RequireName.Length == 0)
                return true;
            if (descriptor.RequireName == "me")
                return true; // `other touches me` = any toucher
            // `when me sees player` means any of the player actors (Player, Player1, Player2), so
            // one enemy sheet serves a two-player scene (Task 16 Part F).
            if (Players.IsNearestReference(descriptor.RequireName) &&
                Players.IsPlayerActorName(World.NameOf(other)))
                return true;
            return World.NameOf(other) == descriptor.RequireName;
        }

        private bool MaterialMatches(TriggerDescriptor descriptor, object other)
        {
            if (descriptor.RequireMaterial.Length == 0)
                return false;
            return World.MaterialOf(other) == descriptor.RequireMaterial;
        }

        /// <summary>Enter-edge arrival: fires once when <c>me</c> reaches any actor matching
        /// <paramref name="descriptor"/>'s name (path done or close enough), then re-arms
        /// after leaving that actor's range again.</summary>
        private void PollArrival(SheetInstance sheet, int eventIndex, TriggerDescriptor descriptor)
        {
            if (descriptor.RequireName.Length == 0)
                return;
            HashSet<object> contacts = sheet.Contacts[eventIndex];
            var arriving = new List<object>();
            foreach (object other in World.Actors())
            {
                if (other == null || ReferenceEquals(other, sheet.Actor))
                    continue;
                if (!NameMatches(descriptor, other))
                    continue;
                double dist = World.Distance2D(sheet.Actor, other);
                if (dist <= ArrivalSlack)
                    arriving.Add(other);
            }
            contacts.RemoveWhere(other => !arriving.Contains(other));
            foreach (object other in arriving)
            {
                if (contacts.Contains(other))
                    continue; // already arrived — still here, no refire
                if (sheet.FiredThisTick[eventIndex])
                    continue;
                contacts.Add(other);
                OnRuntimeWarning($"[NavBridge] {World.NameOf(sheet.Actor)} ARRIVED at {World.NameOf(other)} — firing event");
                RunEvent(sheet, eventIndex, other);
            }
        }

        private void TickWatch(SheetInstance sheet, int eventIndex, TriggerDescriptor descriptor)
        {
            var context = new EventContext
            {
                Variables = Variables,
                World = World,
                Me = sheet.Actor,
                Runtime = this,
            };
            ValueBox target = descriptor.WatchTarget == null ? new ValueBox() : context.Evaluate(descriptor.WatchTarget);
            ValueBox current = descriptor.WatchOwner == "my"
                ? context.ReadMy(descriptor.WatchName)
                : context.ReadGlobal(descriptor.WatchName);
            WatchState state = sheet.Watches[eventIndex];
            if (!state.Initialized)
            {
                state.Last = ValueBox.Copy(current);
                state.Initialized = true;
                return;
            }
            bool changed = ValuesChanged(state.Last, current);
            bool matchesTarget = Conditions.CompareEqual(current, target);
            bool lastMatchesTarget = Conditions.CompareEqual(state.Last, target);
            bool fires;
            switch (descriptor.Kind)
            {
                case TriggerKind.WatchChanges:
                    fires = changed;
                    break;
                case TriggerKind.WatchBecomes:
                    // an equality edge, or a numeric transition that passes through the target
                    // from above (health counting down from unset crosses 0 on the first hit —
                    // an unset value reads as its numeric default, 0)
                    double lastNumber = state.Last.Kind == ValueKind.Number ? state.Last.Number
                        : state.Last.Kind == ValueKind.None ? 0 : double.NaN;
                    fires = matchesTarget && !lastMatchesTarget ||
                            (target.Kind == ValueKind.Number && current.Kind == ValueKind.Number &&
                             !double.IsNaN(lastNumber) &&
                             lastNumber >= target.Number && current.Number < target.Number);
                    break;
                default:
                    fires = !matchesTarget && lastMatchesTarget;
                    break;
            }
            state.Last = ValueBox.Copy(current);
            if (fires)
                RunEvent(sheet, eventIndex, null);
        }

        /// <summary>
        /// <c>when my Health is lower than &lt;n&gt;</c> (Task 18 Part G): a downward edge on a
        /// per-unit health stat. The first tick establishes the baseline like the other
        /// watchers, so loading a scene with a wounded unit does not fire it; it re-arms when
        /// the stat comes back above the threshold.
        /// </summary>
        private void TickHealthLow(SheetInstance sheet, int eventIndex, TriggerDescriptor descriptor)
        {
            double current = HealthManager.Read(Variables, sheet.Actor, descriptor.WatchName);
            WatchState state = sheet.Watches[eventIndex];
            if (!state.Initialized)
            {
                state.Last = ValueBox.OfNumber(current);
                state.Initialized = true;
                return;
            }
            double last = state.Last.AsNumber();
            state.Last = ValueBox.OfNumber(current);
            if (last >= descriptor.Threshold && current < descriptor.Threshold)
                RunEvent(sheet, eventIndex, null);
        }

        /// <summary>Fires when any RTS resource changes. The first tick establishes the
        /// baseline (like <c>when &lt;var&gt; changes</c>), so loading a scene does not fire it.</summary>
        private void TickResourceChanges(SheetInstance sheet, int eventIndex)
        {
            string signature = ResourceManager.Snapshot(Variables);
            WatchState state = sheet.Watches[eventIndex];
            if (!state.Initialized)
            {
                state.Last = ValueBox.OfText(signature);
                state.Initialized = true;
                return;
            }
            bool changed = state.Last.Kind != ValueKind.Text || state.Last.Text != signature;
            state.Last = ValueBox.OfText(signature);
            if (changed)
                RunEvent(sheet, eventIndex, null);
        }

        private static bool ValuesChanged(ValueBox previous, ValueBox current)
        {
            if (previous.Kind != current.Kind)
                return !(previous.Kind == ValueKind.None && current.Kind == ValueKind.None);
            switch (current.Kind)
            {
                case ValueKind.Number:
                    return previous.Number != current.Number;
                case ValueKind.Text:
                    return previous.Text != current.Text;
                case ValueKind.Boolean:
                    return previous.Boolean != current.Boolean;
                default:
                    return false;
            }
        }

        private void WarnUnknownCondition(SheetInstance sheet, BodyLineNode line)
        {
            var parts = new System.Text.StringBuilder();
            foreach (var word in line.Words)
            {
                if (parts.Length > 0)
                    parts.Append(' ');
                parts.Append(word.Text);
            }
            string key = sheet.ActorName + " L" + line.Line + ": " + parts;
            if (warnedLines.Contains(key))
                return;
            warnedLines.Add(key);
            OnRuntimeWarning($"I don't understand the condition \u201C{parts}\u201D yet — it never blocks. The line was skipped as always false.");
        }
    }
}
