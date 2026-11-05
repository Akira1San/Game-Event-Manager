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
            => Variables.GetGlobal(name);

        public ValueBox ReadMy(string name)
            => Variables.GetLocal(Me, name);

        public void WriteGlobal(string name, ValueBox value)
            => Variables.SetGlobal(name, value);

        public void WriteMy(string name, ValueBox value)
            => Variables.SetLocal(Me, name, value);

        public ValueBox ReadOther(string name)
            => Other == null ? new ValueBox() : Variables.GetLocal(Other, name);

        public VariableResolver Resolver()
            => variable =>
            {
                // `my bag` reads back the bag contents as text: "Sword x2, Potion"
                if (variable.Owner == "my" && variable.Name == "bag")
                    return ValueBox.OfText(Runtime.BagText(Me));
                // `my state` reads the sheet's current state (default "default").
                if (variable.Owner == "my" && variable.Name == "state")
                {
                    var sheet = Runtime.FindSheetFor(Me);
                    return ValueBox.OfText(sheet == null ? "default" : sheet.CurrentState);
                }
                if (variable.Owner == "my" || variable.Owner == "me")
                    return ReadMy(variable.Name);
                if (variable.Owner == "other")
                    return ReadOther(variable.Name);
                // `Player's gold` — a named actor's `my` variable (read-only)
                if (variable.Owner.Length > 0)
                {
                    var actor = Runtime.FindNamedActor(variable.Owner);
                    return actor == null ? new ValueBox() : Variables.GetLocal(actor, variable.Name);
                }
                return ReadGlobal(variable.Name);
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
        /// <summary>Last distance at which <c>sees name</c> established sight; reused by a
        /// distance-less <c>stops seeing name</c>.</summary>
        public Dictionary<string, double> LastSeenRange = new Dictionary<string, double>();
        public bool[] FiredThisTick = new bool[0];
        public double[] LastFire = new double[0];
        public WatchState[] Watches = new WatchState[0];
        public double AnchorTime;
    }

    public class EventRuntime
    {
        public EventVariables Variables = new EventVariables();
        public IEventWorld World = new DefaultEventWorld();
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

        private readonly List<SheetInstance> sheets = new List<SheetInstance>();
        private readonly HashSet<string> warnedLines = new HashSet<string>();
        private readonly Dictionary<object, Dictionary<string, int>> bags = new Dictionary<object, Dictionary<string, int>>();
        /// <summary>wearer -> set of item names currently equipped on that wearer.</summary>
        private readonly Dictionary<object, HashSet<string>> equipped = new Dictionary<object, HashSet<string>>();

        public EventRuntime()
        {
            Variables.OnWarning = message => OnRuntimeWarning(message);
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
            var sheet = new SheetInstance
            {
                Actor = actor,
                ActorName = World.NameOf(actor),
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
                sheet.Triggers.Add(TriggerClassifier.Classify(program.Events[i]));
            }
        }

        public void RemoveAllSheets()
        {
            sheets.Clear();
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
        }

        public void SessionStarted()
        {
            foreach (var sheet in sheets)
                sheet.PushQueue.Enqueue(new PushSignal { Kind = TriggerKind.GameStart });
        }

        public void SceneStarted()
        {
            foreach (var sheet in sheets)
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

        public void PushContact(TriggerKind kind, object a, object b)
        {
            foreach (var sheet in sheets)
            {
                if (ReferenceEquals(sheet.Actor, a))
                    sheet.PushQueue.Enqueue(new PushSignal { Kind = kind, Other = b, Name = NameOf(b) });
                if (ReferenceEquals(sheet.Actor, b))
                    sheet.PushQueue.Enqueue(new PushSignal { Kind = kind, Other = a, Name = NameOf(a) });
            }
        }

        public void SendRawMessage(PushSignal signal, string toName)
        {
            if (string.IsNullOrEmpty(toName))
            {
                RaisePushAll(signal);
                return;
            }
            foreach (var sheet in sheets)
                if (sheet.ActorName == toName)
                    sheet.PushQueue.Enqueue(signal);
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
                        RunBody(pending.Sheet, pending.Event, pending.Context, pending.LineIndex, pending.Gate);
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
                        if (IsContactKind(signal.Kind) && signal.Other != null)
                        {
                            // A distance poll already fired this pair: ignore the collider's echo.
                            if (sheet.PolledContacts[eventIndex].Contains(signal.Other))
                                continue;
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
        }

        private string NameOf(object actor)
        {
            return World.NameOf(actor);
        }

        private static bool IsContactKind(TriggerKind kind)
        {
            return kind == TriggerKind.Touches || kind == TriggerKind.Overlaps;
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

        private bool Matches(SheetInstance sheet, TriggerDescriptor descriptor, PushSignal signal)
        {
            if (descriptor.Kind != signal.Kind)
                return false;
            if (descriptor.RequireName.Length == 0)
                return true;
            // `when other touches me` / `when other overlaps me` means "any toucher".
            if (descriptor.RequireName == "me" && IsContactKind(descriptor.Kind))
                return true;
            // `when me is clicked` means "the actor this sheet is attached to was clicked".
            // Clicks are broadcast (so `when Ground is clicked` can live on the Player sheet),
            // which is why the name alone is not enough for the `me` form.
            if (descriptor.RequireName == "me" && IsClickKind(descriptor.Kind))
                return ReferenceEquals(signal.Other, sheet.Actor);
            // Clip names are matched loosely: `play anim "Walk"` answers `when anim "walk" ends`.
            if (descriptor.Kind == TriggerKind.AnimEnd)
                return string.Equals(signal.Name, descriptor.RequireName, StringComparison.OrdinalIgnoreCase);
            return signal.Name == descriptor.RequireName;
        }

        private static bool IsClickKind(TriggerKind kind)
        {
            return kind == TriggerKind.Clicked ||
                   kind == TriggerKind.RightClicked ||
                   kind == TriggerKind.MiddleClicked;
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

        private void RunBody(SheetInstance sheet, EventNode ev, EventContext context, int startLine, bool startGate)
        {
            bool gate = startGate;
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
                    gate = inverted ? !result : result;
                    continue;
                }
                if (!gate)
                    continue;
                var statement = line.Statement;
                if (statement is SetNode set)
                {
                    ExecuteSet(context, set);
                }
                else if (statement is AddNode add)
                {
                    ExecuteAdd(context, add);
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
                else if (statement is SetStateNode setState)
                {
                    ExecuteSetState(context, setState);
                }
                else if (statement is RawActionNode raw)
                {
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
                    OnRawAction(context, raw.Words);
                }
            }
        }

        private void ExecuteSet(EventContext context, SetNode set)
        {
            ValueBox value = context.Evaluate(set.Value);
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
            if (set.Target.Owner == "my")
                context.WriteMy(set.Target.Name, value);
            else
                context.WriteGlobal(set.Target.Name, value);
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
            ValueBox current = add.Target.Owner == "my"
                ? context.ReadMy(add.Target.Name)
                : context.ReadGlobal(add.Target.Name);
            ValueBox updated = ValueBox.OfNumber(current.AsNumber() + amount);
            if (add.Target.Owner == "my")
                context.WriteMy(add.Target.Name, updated);
            else
                context.WriteGlobal(add.Target.Name, updated);
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
            if (words.Count >= 5 && words[3].Type == TokenType.Word && words[3].Text == "to" && words[4].Type == TokenType.Word)
                targetName = words[4].Text;
            var signal = new PushSignal { Kind = TriggerKind.Message, Name = message, Other = context.Me };
            if (targetName == "me")
            {
                FindSheetFor(context.Me)?.PushQueue.Enqueue(signal);
                return;
            }
            if (targetName == "other")
            {
                FindSheetFor(context.Other)?.PushQueue.Enqueue(signal);
                return;
            }
            SendRawMessage(signal, targetName);
        }

        private static bool IsStopThisEvent(RawActionNode raw)
        {
            return raw.Words.Count >= 3 && raw.Words[0].Text == "stop" &&
                   raw.Words[1].Text == "this" && raw.Words[2].Text == "event";
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
                case TriggerKind.Arrives:
                    PollArrival(sheet, eventIndex, descriptor);
                    break;
                case TriggerKind.Sees:
                case TriggerKind.StopsSeeing:
                    PollSight(sheet, eventIndex, descriptor);
                    break;
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

        /// <summary>Enter-edge distance contact: fires once per pair, again only after separation.</summary>
        private void PollContact(SheetInstance sheet, int eventIndex, TriggerDescriptor descriptor)
        {
            HashSet<object> contacts = sheet.Contacts[eventIndex];
            HashSet<object> polled = sheet.PolledContacts[eventIndex];
            var touching = new List<object>();
            foreach (object other in World.Actors())
            {
                if (other == null || ReferenceEquals(other, sheet.Actor))
                    continue;
                if (!NameMatches(descriptor, other))
                    continue;
                if (!World.IsTouching(sheet.Actor, other))
                    continue;
                touching.Add(other);
            }
            contacts.RemoveWhere(other => !touching.Contains(other));
            polled.RemoveWhere(other => !touching.Contains(other));
            foreach (object other in touching)
            {
                if (contacts.Contains(other))
                    continue; // still touching the same pair — enter edge only
                if (sheet.FiredThisTick[eventIndex])
                    continue; // this event already ran this tick; try the pair next tick
                contacts.Add(other);
                polled.Add(other);
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
            return World.NameOf(other) == descriptor.RequireName;
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
