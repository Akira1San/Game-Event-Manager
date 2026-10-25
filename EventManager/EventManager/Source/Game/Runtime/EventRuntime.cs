using System;
using System.Collections.Generic;

namespace Game
{
    public class EventContext
    {
        public EventVariables Variables;
        public IEventWorld World;
        public object Me;
        public object Other;
        public EventRuntime Runtime;
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
            => variable => variable.Owner == "my" ? ReadMy(variable.Name) : ReadGlobal(variable.Name);

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
        public ProgramFile Program = new ProgramFile();
        public List<TriggerDescriptor> Triggers = new List<TriggerDescriptor>();
        public Queue<PushSignal> PushQueue = new Queue<PushSignal>();
        public List<PendingRun> Waiting = new List<PendingRun>();
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
        public Action<string> OnRuntimeWarning = message => { };
        public Action<EventContext, List<Token>> OnRawAction = (context, words) => { };
        public Action<EventContext, string, ValueBox> OnSetText = (context, thing, value) => { };

        private readonly List<SheetInstance> sheets = new List<SheetInstance>();
        private readonly HashSet<string> warnedLines = new HashSet<string>();
        private readonly Dictionary<object, Dictionary<string, int>> bags = new Dictionary<object, Dictionary<string, int>>();

        public EventRuntime()
        {
            Variables.OnWarning = message => OnRuntimeWarning(message);
        }

        public IReadOnlyList<SheetInstance> Sheets => sheets;

        public SheetInstance AddSheet(object actor, ProgramFile program)
        {
            var sheet = new SheetInstance
            {
                Actor = actor,
                ActorName = actor == null ? "" : actor.ToString(),
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
            sheet.Triggers.Clear();
            for (int i = 0; i < count; i++)
            {
                sheet.LastFire[i] = Time;
                sheet.Watches[i] = new WatchState();
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

        public void Tick(double time)
        {
            Time = time;
            foreach (var sheet in sheets)
            {
                for (int i = 0; i < sheet.FiredThisTick.Length; i++)
                    sheet.FiredThisTick[i] = false;
            }

            foreach (var sheet in sheets)
            {
                if (sheet.Waiting.Count == 0)
                    continue;
                var snapshot = new List<PendingRun>(sheet.Waiting);
                sheet.Waiting.Clear();
                foreach (var pending in snapshot)
                {
                    if (pending.ResumeAt <= Time)
                        RunBody(pending.Sheet, pending.Event, pending.Context, pending.LineIndex, pending.Gate);
                    else
                        sheet.Waiting.Add(pending);
                }
            }

            foreach (var sheet in sheets)
            {
                while (sheet.PushQueue.Count > 0)
                {
                    var signal = sheet.PushQueue.Dequeue();
                    for (int eventIndex = 0; eventIndex < sheet.Program.Events.Count; eventIndex++)
                    {
                        if (sheet.FiredThisTick[eventIndex])
                            continue;
                        if (!Matches(sheet.Triggers[eventIndex], signal))
                            continue;
                        RunEvent(sheet, eventIndex, signal.Other);
                    }
                }
            }

            foreach (var sheet in sheets)
            {
                for (int eventIndex = 0; eventIndex < sheet.Program.Events.Count; eventIndex++)
                {
                    if (sheet.FiredThisTick[eventIndex])
                        continue;
                    TryPolled(sheet, eventIndex);
                }
            }
        }

        private static string NameOf(object actor)
        {
            return actor == null ? "" : actor.ToString();
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

        private bool Matches(TriggerDescriptor descriptor, PushSignal signal)
        {
            if (descriptor.Kind != signal.Kind)
                return false;
            if (descriptor.RequireName.Length == 0)
                return true;
            return signal.Name == descriptor.RequireName;
        }

        private void RunEvent(SheetInstance sheet, int eventIndex, object other)
        {
            var ev = sheet.Program.Events[eventIndex];
            var context = new EventContext
            {
                Variables = Variables,
                World = World,
                Me = sheet.Actor,
                Other = other,
                Runtime = this,
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
                else if (statement is SetTextNode setText)
                {
                    OnSetText(context, setText.Target, context.Evaluate(setText.Value));
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

        private void ExecuteAdd(EventContext context, AddNode add)
        {
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

        private void ExecuteSend(EventContext context, List<Token> words)
        {
            if (words.Count < 3 || words[2].Type != TokenType.Str)
                return;
            string message = words[2].Text;
            string targetName = "";
            if (words.Count >= 5 && words[3].Type == TokenType.Word && words[3].Text == "to" && words[4].Type == TokenType.Word)
                targetName = words[4].Text;
            SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = message, Other = context.Me }, targetName);
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
                case TriggerKind.WatchBecomes:
                case TriggerKind.WatchBecomesNot:
                case TriggerKind.WatchChanges:
                    TickWatch(sheet, eventIndex, descriptor);
                    break;
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
                    // from above (health counting down from unset crosses 0 on the first hit)
                    fires = matchesTarget && !lastMatchesTarget ||
                            (target.Kind == ValueKind.Number && state.Last.Kind == ValueKind.Number &&
                             current.Kind == ValueKind.Number &&
                             state.Last.Number >= target.Number && current.Number < target.Number);
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
