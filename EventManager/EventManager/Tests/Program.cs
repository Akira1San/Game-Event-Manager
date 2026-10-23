using Game;
using System.Text;

int passed = 0;
int failed = 0;

void Check(bool condition, string name)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"PASS  {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"FAIL  {name}");
    }
}

void CheckStrings(string expected, string actual, string name)
{
    if (expected == actual)
    {
        passed++;
        Console.WriteLine($"PASS  {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine($"  expected: {expected}");
        Console.WriteLine($"  actual:   {actual}");
    }
}

void ExpectError(string source, string mustContain, string name)
{
    try
    {
        new EventLexer().Tokenize(source);
        failed++;
        Console.WriteLine($"FAIL  {name} (no error was thrown)");
    }
    catch (LexException e)
    {
        Check(e.Message.Contains(mustContain, StringComparison.OrdinalIgnoreCase), $"{name} -> {e.Message}");
    }
    catch (Exception e)
    {
        failed++;
        Console.WriteLine($"FAIL  {name} (wrong error type: {e.GetType().Name}: {e.Message})");
    }
}

string Describe(List<Token> tokens)
{
    var sb = new StringBuilder();
    foreach (var token in tokens)
    {
        if (sb.Length > 0)
            sb.Append('|');
        sb.Append(token.ToString());
    }
    return sb.ToString();
}

var lexer = new EventLexer();

var oneEvent = lexer.Tokenize("when W is pressed\n  move forward speed 5\n");
Check(Describe(oneEvent) ==
      "Word:when|Word:W|Word:is|Word:pressed|NewLine|Indent:1|Word:move|Word:forward|Word:speed|Number:5|NewLine|Dedent|EndOfFile",
      "one event lexes to the expected token list");

var comments = lexer.Tokenize("# top comment\n\nwhen game starts   # greet\n  show text \"hi\"\n");
Check(Describe(comments) ==
      "Word:when|Word:game|Word:starts|NewLine|Indent:1|Word:show|Word:text|Str:hi|NewLine|Dedent|EndOfFile",
      "comments and blank lines vanish (leading, lone, trailing)");

var twoEvents = lexer.Tokenize("when game starts\n  show text \"hi\"\n\nwhen scene starts\n  set Score to 0\n");
CheckStrings(
      "Word:when|Word:game|Word:starts|NewLine|Indent:1|Word:show|Word:text|Str:hi|NewLine|Dedent|Word:when|Word:scene|Word:starts|NewLine|Indent:1|Word:set|Word:Score|Word:to|Number:0|NewLine|Dedent|EndOfFile",
      Describe(twoEvents),
      "dedent sits before the second event and trailing dedents land before eof");

var nested = lexer.Tokenize("when me appears\n  set X to 1\n    set Y to 2\n");
Check(Describe(nested) ==
      "Word:when|Word:me|Word:appears|NewLine|Indent:1|Word:set|Word:X|Word:to|Number:1|NewLine|Indent:2|Word:set|Word:Y|Word:to|Number:2|NewLine|Dedent|Dedent|EndOfFile",
      "two nesting levels and stacked dedents at the end");

var empty = lexer.Tokenize("");
Check(empty.Count == 1 && empty[0].Type == TokenType.EndOfFile, "empty input is just EndOfFile");

var values = lexer.Tokenize("set X to 0.5\n  add -1 to Lives\n  20%\n  play sound \"hit.wav\"\n");
Check(values[3].Type == TokenType.Number && Math.Abs(values[3].Number - 0.5) < 0.000001, "decimal number value");
Check(values[7].Type == TokenType.Number && Math.Abs(values[7].Number - (-1)) < 0.000001, "negative number value");
Check(values[11].Type == TokenType.Word && values[11].Text == "20%", "percent stays a word");
Check(values[15].Type == TokenType.Str && values[15].Text == "hit.wav", "string value");

var lineNumbers = lexer.Tokenize("when game starts\n  set Score to 0\n\n  set Ammo to 3\n");
bool allLinesCorrect = true;
for (int i = 0; i < lineNumbers.Count - 1; i++)
{
    int expectedLine = i < 4 ? 1 : (i < 10 ? 2 : 4);
    if (lineNumbers[i].Line != expectedLine)
        allLinesCorrect = false;
}
Check(allLinesCorrect, "token line numbers skip blank lines correctly");
Check(lineNumbers[lineNumbers.Count - 1].Line == 4, "eof carries the last line number");

ExpectError("when game starts\n\tset Score to 0\n", "tab", "tab indent is a friendly error");
ExpectError("when game starts\n   set Score to 0\n", "multiple of 2", "odd indent width is a friendly error");
ExpectError("when game starts\n    set Score to 0\n", "jumped", "2-level indent jump is a friendly error");
ExpectError("play sound \"hit.wav\n", "\" quote is missing", "unterminated string is a friendly error");
ExpectError("when game ends\n\n\nplay sound \"oops\n", "Line 4", "unterminated string reports the right line after blanks");

string FindUpward(string fileName)
{
    var dir = AppContext.BaseDirectory;
    for (int i = 0; i < 12; i++)
    {
        var candidate = Path.Combine(dir, fileName);
        if (File.Exists(candidate))
            return candidate;
        dir = Path.GetFullPath(Path.Combine(dir, ".."));
    }
    throw new FileNotFoundException($"could not find {fileName} above {AppContext.BaseDirectory}");
}

var docLines = File.ReadAllLines(FindUpward("DESIGN.md"));
var fence = new List<string>();
var inFence = false;
var fenceHasEvents = false;
var docFenceFailures = 0;
var docEventsChecked = 0;
foreach (var docLine in docLines)
{
    if (docLine.StartsWith("```", StringComparison.Ordinal))
    {
        if (inFence)
        {
            if (fenceHasEvents)
            {
                docEventsChecked++;
                try
                {
                    new EventLexer().Tokenize(string.Join("\n", fence));
                }
                catch (LexException e)
                {
                    docFenceFailures++;
                    Console.WriteLine($"FAIL  doc listing did not lex -> {e.Message}");
                }
            }
        }
        inFence = !inFence;
        fence.Clear();
        fenceHasEvents = false;
    }
    else if (inFence)
    {
        fence.Add(docLine);
        if (docLine.StartsWith("when ", StringComparison.Ordinal))
            fenceHasEvents = true;
    }
}
Check(docEventsChecked > 0 && docFenceFailures == 0,
      $"every event listing in DESIGN.md lexes clean ({docEventsChecked} listings checked)");

// ------------------------------------------------------------------
// Parser (Task 2 Part B): tokens -> event tree
// ------------------------------------------------------------------

var parser = new EventParser();

void ExpectParseError(string source, string mustContain, string name)
{
    try
    {
        parser.Parse(source);
        failed++;
        Console.WriteLine($"FAIL  {name} (no error was thrown)");
    }
    catch (ParseException e)
    {
        if (e.Message.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
        {
            passed++;
            Console.WriteLine($"PASS  {name} -> {e.Message}");
        }
        else
        {
            failed++;
            Console.WriteLine($"FAIL  {name} (message is missing \"{mustContain}\")");
            Console.WriteLine($"  actual: {e.Message}");
        }
    }
    catch (Exception e)
    {
        failed++;
        Console.WriteLine($"FAIL  {name} (wrong error type: {e.GetType().Name}: {e.Message})");
    }
}

// doc self-test: the Section 7 shooter parses with zero errors and lands in 16 events
string FindUpward2(string fileName)
{
    var dir3 = AppContext.BaseDirectory;
    for (int i = 0; i < 12; i++)
    {
        var candidate = Path.Combine(dir3, fileName);
        if (File.Exists(candidate))
            return candidate;
        dir3 = Path.GetFullPath(Path.Combine(dir3, ".."));
    }
    throw new FileNotFoundException($"could not find {fileName} above {AppContext.BaseDirectory}");
}

string shooterText = "";
{
    var lines3 = File.ReadAllLines(FindUpward2("DESIGN.md"));
    var fence3 = new List<string>();
    var inFence3 = false;
    foreach (var docLine in lines3)
    {
        if (docLine.StartsWith("```", StringComparison.Ordinal))
        {
            if (inFence3 && shooterText.Length == 0 && fence3.Contains("when me overlaps Enemy"))
                shooterText = string.Join("\n", fence3);
            inFence3 = !inFence3;
            fence3.Clear();
        }
        else if (inFence3)
        {
            fence3.Add(docLine);
        }
    }
}
ProgramFile shooter = parser.Parse(shooterText);
Check(shooter.Events.Count == 16, $"section 7 shooter parses into 16 events (got {shooter.Events.Count})");
bool shooterShaped = true;
foreach (var ev in shooter.Events)
    if (ev.Trigger.Count == 0 || ev.Body.Count == 0)
        shooterShaped = false;
Check(shooterShaped, "every shooter event has a trigger and at least one body line");

EventNode healthEvent = null!;
foreach (var ev in shooter.Events)
{
    bool hasHealth = false;
    bool hasBecomes = false;
    foreach (var t in ev.Trigger)
    {
        if (t.Type == TokenType.Word && t.Text == "health") hasHealth = true;
        if (t.Type == TokenType.Word && t.Text == "becomes") hasBecomes = true;
    }
    if (hasHealth && hasBecomes)
        healthEvent = ev;
}
Check(healthEvent != null, "the shooter has a `when my health becomes 0` event");
var scoreAdd = (AddNode)healthEvent!.Body[0].Statement!;
Check(scoreAdd.Amount is NumberValue an && an.Value == 1 && scoreAdd.Target.Owner.Length == 0 &&
      scoreAdd.Target.Name == "Score", "`add 1 to Score` is an AddNode with amount 1 target Score");
Check(healthEvent.Body[2].Statement is RawActionNode, "`destroy me` is a raw action line");

var dice = parser.Parse("when game starts\n  set Dice to random 1 to 4\n");
var diceSet = (SetNode)dice.Events[0].Body[0].Statement!;
var diceRange = (RandomRangeValue)diceSet.Value;
Check(diceRange.Min is NumberValue mn && mn.Value == 1 && diceRange.Max is NumberValue mx && mx.Value == 4,
      "`set Dice to random 1 to 4` becomes a RandomRangeValue(1, 4)");

var scoring = parser.Parse("when game starts\n  set Score to Score + 1\n");
var mathSet = (SetNode)scoring.Events[0].Body[0].Statement!;
Check(mathSet.Value is VariableValue && mathSet.Op == '+' && mathSet.Right is NumberValue rn && rn.Value == 1,
      "`set Score to Score + 1` keeps the one-op math shape");

var hurt = parser.Parse("when me appears\n  add -1 to Lives\n  add Potion to my bag\n");
var negative = (AddNode)hurt.Events[0].Body[0].Statement!;
Check(negative.Amount is NumberValue av && av.Value == -1 && negative.Target.Name == "Lives",
      "`add -1 to Lives` parses with the negative amount");
var bagFill = (AddNode)hurt.Events[0].Body[1].Statement!;
Check(bagFill.Amount is VariableValue && bagFill.Target.Owner == "my" && bagFill.Target.Name == "bag",
      "`add Potion to my bag` reads my bag as the target");

var gate = parser.Parse("when me appears\n  unless my Invulnerable is true\n  hide me\n");
Check(gate.Events[0].Body[0].IsCondition && gate.Events[0].Body[0].Statement == null,
      "an `unless` line classifies as a condition with no statement");
Check(!gate.Events[0].Body[1].IsCondition && gate.Events[0].Body[1].Statement is RawActionNode,
      "the line after an `unless` is still an action");

var brain = parser.Parse("when every 0.2 seconds\n  my mode is \"chase\"\n  goto Player\n");
Check(brain.Events[0].Body[0].IsCondition, "a bare condition line (my mode is ...) has no `unless` but is a condition");
var chase = (RawActionNode)brain.Events[0].Body[1].Statement!;
Check(chase.Words[0].Type == TokenType.Word && chase.Words[0].Text == "goto",
      "the action after a bare condition is kept as a raw action");

ExpectParseError("wen game starts\n  show text \"hi\"\n", "Did you mean", "typo'd `when` gets the near-miss suggestion");
ExpectParseError("when game starts\n  set Score 5\n", "`set` line", "set without `to` is a friendly error");
ExpectParseError("when game starts\n  add 1 Lives\n", "`add`", "add without `to <var>` is a friendly error");
ExpectParseError("when game starts\n  set Score to other's damage\n", "capital", "lowercase value word explains the variable rule");
ExpectParseError("when game starts\n\nwhen scene starts\n  hide text\n", "nothing indented under it", "empty body is a friendly error");
ExpectParseError("when game starts\n  set Score to 0\n    set Lives to 3\n", "too deep", "a body line 4 spaces in is a friendly error");
ExpectParseError("when game starts\n  when scene starts\n  hide text\n", "never", "a nested `when` explains the stack rule");
ExpectParseError("when game starts\n  restar scane\n", "restart", "near-miss verb gets a did-you-mean");
ExpectParseError("when game starts\n  banana 3\n", "means yet", "far-off word gets the plain unknown-word error");
ExpectParseError("  hide me\n", "when", "an indented line with no `when` above is a friendly error");

// ------------------------------------------------------------------
// Word registry (Task 2 Part C): vocabulary as data
// ------------------------------------------------------------------

int examplesChecked = 0;
int exampleFailures = 0;
foreach (var entry in WordRegistry.All)
{
    if (entry.Example.Length == 0)
        continue;
    string source = entry.Kind == "trigger"
        ? entry.Example + "\n  show me\n"
        : "when me appears\n  " + entry.Example + "\n";
    try
    {
        parser.Parse(source);
        examplesChecked++;
    }
    catch (Exception e)
    {
        exampleFailures++;
        Console.WriteLine($"FAIL  registry example [{entry.Kind}] \"{entry.Word}\" -> {e.Message}");
    }
}
Check(examplesChecked > 0 && exampleFailures == 0,
      $"every registered word's example parses ({examplesChecked} examples checked)");

var sight = parser.Parse("when me appears\n  hide me\n\nwhen me stops seeing Player by 8\n  show me\n");
Check(sight.Events.Count == 2 && sight.Events[1].Trigger.Count == 6,
      "`when me stops seeing Player by 8` validates through the alt trigger shape");

var hud = parser.Parse("when scene starts\n  set text of ScoreLabel to Score\n");
var hudSet = (SetTextNode)hud.Events[0].Body[0].Statement!;
Check(hudSet.Target == "ScoreLabel" && hudSet.Value is VariableValue hv && hv.Name == "Score",
      "`set text of ScoreLabel to Score` builds a SetTextNode");

bool registryVerbsLive = Array.IndexOf(WordRegistry.ActionWords, "quit") >= 0 &&
                         Array.IndexOf(WordRegistry.ActionWords, "move") >= 0;
Check(registryVerbsLive, "actions validate against the registry, not hardcoded strings");

ExpectParseError("when door is open\n  show me\n", "trigger", "an unknown trigger shape is a friendly error");
ExpectParseError("when W is pressings\n  show me\n", "trigger", "a bent trigger shape is a friendly error");
ExpectParseError("when me appears and\n  hide me\n", "needs something after it", "a dangling `and` is a friendly trigger error");

// ------------------------------------------------------------------
// Runtime Part A: variables (values, scopes, hot-reload survival)
// ------------------------------------------------------------------

var store = new EventVariables();
int warned = 0;
store.OnWarning = message => { warned++; };

var unset = store.GetGlobal("Score");
Check(unset.Kind == ValueKind.None && unset.AsNumber() == 0 && unset.AsText() == "" && unset.AsBoolean() == false,
      "reading an unset variable gives 0 / \"\" / false without crashing");
store.GetGlobal("Score");
Check(warned == 1, "the unset warning is one-time per name");
store.GetGlobal("Score");
Check(warned == 1, "reading the same unset name again stays silent");

store.SetGlobal("Score", ValueBox.OfNumber(5));
Check(store.GetGlobal("Score").AsNumber() == 5, "global numbers keep their value");
store.SetGlobal("PlayerName", ValueBox.OfText("Bob"));
Check(store.GetGlobal("PlayerName").AsText() == "Bob", "global text keeps its value");
store.SetGlobal("Gate", ValueBox.OfBoolean(true));
Check(store.GetGlobal("Gate").AsBoolean() == true, "global booleans keep their value");
store.SetGlobal("Score", ValueBox.OfText("ten"));
Check(store.GetGlobal("Score").AsText() == "ten", "a new write re-types the variable");

Check(ValueBox.OfBoolean(true).AsNumber() == 1 && ValueBox.OfBoolean(false).AsNumber() == 0, "booleans read as 1 / 0");
Check(ValueBox.OfText("3.5").AsNumber() == 3.5 && ValueBox.OfText("hugo").AsNumber() == 0, "text reads as a number when parseable, 0 otherwise");
Check(ValueBox.OfNumber(2.5).AsText() == "2.5" && ValueBox.OfNumber(5).AsText() == "5" && ValueBox.OfNumber(0.5).AsText() == "0.5", "numbers read as clean text for set-text-of displays");

var player = new FakeActor("Player");
var enemy = new FakeActor("Enemy");
store.SetLocal(player, "health", ValueBox.OfNumber(3));
store.SetLocal(enemy, "health", ValueBox.OfNumber(8));
Check(store.GetLocal(player, "health").AsNumber() == 3 && store.GetLocal(enemy, "health").AsNumber() == 8,
      "each copy of an actor keeps its own `my` variables");
Check(store.GetGlobal("health").Kind == ValueKind.None, "global scope stays untouched by `my` writes");

var previous = new EventVariables();
previous.SetGlobal("Score", ValueBox.OfNumber(11));
previous.SetGlobal("OldScore", ValueBox.OfNumber(9));
previous.SetLocal(player, "ammo", ValueBox.OfText("12"));
previous.SetLocal(player, "OldLocal", ValueBox.OfNumber(1));

var next = new EventVariables();
next.CarryValuesFrom(previous, name => name == "Score" || name == "ammo");
Check(next.GetGlobal("Score").AsNumber() == 11, "globals survive hot reload when the name still exists");
Check(next.GetGlobal("OldScore").Kind == ValueKind.None, "globals whose name vanished come back unset");
Check(next.HasGlobal("Score") && !next.HasGlobal("OldScore"), "carried store contains exactly the surviving names");
Check(next.GetLocal(player, "ammo").AsText() == "12", "per-object values survive hot reload");
Check(next.GetLocal(player, "OldLocal").Kind == ValueKind.None, "per-object values whose name vanished come back unset");
next.SetGlobal("Score", ValueBox.OfNumber(1));
Check(previous.GetGlobal("Score").AsNumber() == 11, "carried values are copies, not aliases of the old store");

// ------------------------------------------------------------------
// Runtime Part A: value evaluation (all forms incl. seeded random)
// ------------------------------------------------------------------

Rng.Seed(7);
bool diceInRange = true;
for (int roll = 0; roll < 80; roll++)
{
    ValueBox diceRoll = ValueEvaluator.Evaluate(new RandomRangeValue { Min = new NumberValue { Value = 1 }, Max = new NumberValue { Value = 4 } }, null);
    if (diceRoll.AsNumber() < 1 || diceRoll.AsNumber() > 4 || diceRoll.AsNumber() != Math.Floor(diceRoll.AsNumber()))
        diceInRange = false;
}
Check(diceInRange, "`random 1 to 4` only yields whole numbers inside the range");

var eventDice = parser.Parse("when me appears\n  set Dice to random 1 to 4\n");
var diceStatement = (SetNode)eventDice.Events[0].Body[0].Statement!;
ValueBox parsedDice = ValueEvaluator.Evaluate(diceStatement.Value, null);
Check(parsedDice.AsNumber() >= 1 && parsedDice.AsNumber() <= 4, "the parser's Dice node rolls in range too");

Rng.Seed(42);
double first = ValueEvaluator.Evaluate(new RandomRangeValue { Min = new NumberValue { Value = 1 }, Max = new NumberValue { Value = 4 } }, null).AsNumber();
double second = ValueEvaluator.Evaluate(new RandomRangeValue { Min = new NumberValue { Value = 1 }, Max = new NumberValue { Value = 4 } }, null).AsNumber();
Rng.Seed(42);
double firstAgain = ValueEvaluator.Evaluate(new RandomRangeValue { Min = new NumberValue { Value = 1 }, Max = new NumberValue { Value = 4 } }, null).AsNumber();
double secondAgain = ValueEvaluator.Evaluate(new RandomRangeValue { Min = new NumberValue { Value = 1 }, Max = new NumberValue { Value = 4 } }, null).AsNumber();
Check(first == firstAgain && second == secondAgain, "seeded rolls replay the exact sequence");

var fractional = ValueEvaluator.Evaluate(new RandomRangeValue { Min = new NumberValue { Value = 0.5 }, Max = new NumberValue { Value = 2 } }, null).AsNumber();
Check(fractional >= 0.5 && fractional <= 2, "`random 0.5 to 2` rolls a fractional value in range");

Rng.Seed(3);
var reversed = ValueEvaluator.Evaluate(new RandomRangeValue { Min = new NumberValue { Value = 4 }, Max = new NumberValue { Value = 1 } }, null).AsNumber();
Check(reversed >= 1 && reversed <= 4, "`random 4 to 1` still rolls a value in range (bounds swap quietly)");

var diceStore = new EventVariables();
var evalDice = ValueEvaluator.Evaluate(new RandomRangeValue { Min = new VariableValue { Name = "Low" }, Max = new VariableValue { Name = "High" } },
    variable => variable.Name == "Low" ? ValueBox.OfNumber(1) : ValueBox.OfNumber(6));
Check(evalDice.AsNumber() >= 1 && evalDice.AsNumber() <= 6, "random bounds may be variables");

var sum = ValueEvaluator.Evaluate(new MathValue { Left = new NumberValue { Value = 1 }, Op = '+', Right = new NumberValue { Value = 2 } }, null);
Check(sum.AsNumber() == 3, "one-op math evaluates (1 + 2 = 3)");
var times = ValueEvaluator.Evaluate(new MathValue { Left = new NumberValue { Value = 2 }, Op = '*', Right = new NumberValue { Value = 2.5 } }, null);
Check(times.AsNumber() == 5, "one-op math evaluates (2 * 2.5 = 5)");
var byZero = ValueEvaluator.Evaluate(new MathValue { Left = new NumberValue { Value = 1 }, Op = '/', Right = new NumberValue { Value = 0 } }, null);
Check(byZero.AsNumber() == 0, "dividing by zero stays friendly (0, no crash)");

var scoreValue = ValueEvaluator.Evaluate(new VariableValue { Name = "Score", Owner = "my" },
    variable => variable.Owner == "my" ? diceStore.GetLocal(player, variable.Name) : diceStore.GetGlobal(variable.Name));
Check(scoreValue.Kind == ValueKind.None, "a variable resolves through the runtime's variable context");

// ------------------------------------------------------------------
// Runtime Part B: execution engine (ticks, pushing, waits, gating)
// ------------------------------------------------------------------

string J(List<Token> words)
{
    var parts = new System.Text.StringBuilder();
    foreach (var w in words)
    {
        if (parts.Length > 0)
            parts.Append(' ');
        parts.Append(w.Text);
    }
    return parts.ToString();
}

EventRuntime FreshEngine(FakeWorld world)
{
    var engine = new EventRuntime();
    engine.World = world;
    return engine;
}

var overlapWorld = new FakeWorld();
var overlapEngine = FreshEngine(overlapWorld);
var overlapLog = new List<string>();
overlapEngine.OnRawAction = (context, words) => overlapLog.Add(J(words));

var overlapProgram = parser.Parse(@"when game starts
  set Order to """"
when me overlaps Bomb
  set Order to Order + ""A""
when me overlaps Bomb
  set Order to Order + ""B""");
var overlapActor = new FakeActor("Surveyor");
var bombee = new FakeActor("Bomb");
overlapEngine.AddSheet(overlapActor, overlapProgram);
overlapEngine.SessionStarted();
overlapEngine.PushContact(TriggerKind.Overlaps, overlapActor, bombee);
overlapEngine.Tick(0);
var overlapOrder = overlapEngine.Variables.GetGlobal("Order").AsText();
Check(overlapOrder == "AB", $"all matching events run in written order (got {overlapOrder})");

overlapEngine.PushContact(TriggerKind.Overlaps, overlapActor, bombee);
overlapEngine.PushContact(TriggerKind.Overlaps, overlapActor, bombee);
overlapEngine.Tick(0.1);
overlapOrder = overlapEngine.Variables.GetGlobal("Order").AsText();
Check(overlapOrder == "ABAB", "an event fires at most once per tick, even with two pushes queued");

overlapEngine.PushContact(TriggerKind.Overlaps, overlapActor, bombee);
overlapEngine.Tick(0.2);
overlapOrder = overlapEngine.Variables.GetGlobal("Order").AsText();
Check(overlapOrder == "ABABAB", "pushed events fire again on the next tick");

var orderWorld = new FakeWorld();
var orderEngine = FreshEngine(orderWorld);
orderEngine.OnRawAction = (context, words) => { };
var orderProgram = parser.Parse(@"when game starts
  set Order to """"
when every 2 seconds
  set Order to Order + ""B""
when message ""go"" arrives
  set Order to Order + ""A""");
var orderActor = new FakeActor("Scheduler");
orderEngine.AddSheet(orderActor, orderProgram);
orderEngine.SessionStarted();
orderEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.Message, Name = "go", Other = orderActor });
orderEngine.Tick(0);
Check(orderEngine.Variables.GetGlobal("Order").AsText() == "A", "pushed triggers run before the polled pass");
orderEngine.Tick(2);
Check(orderEngine.Variables.GetGlobal("Order").AsText() == "AB", "polled timers fire on their own schedule (2 seconds)");

var sessionProgram = parser.Parse(@"when game starts
  set SessionLog to SessionLog + ""G""
when scene starts
  set SessionLog to SessionLog + ""S""");
var sessionEngine = FreshEngine(new FakeWorld());
sessionEngine.AddSheet(new FakeActor("Watcher"), sessionProgram);
sessionEngine.SessionStarted();
sessionEngine.SceneStarted();
sessionEngine.Tick(0);
Check(sessionEngine.Variables.GetGlobal("SessionLog").AsText() == "GS", "slot: session start (pushed) then scene start in order");
var sessionEngine2 = FreshEngine(new FakeWorld());
sessionEngine2.AddSheet(new FakeActor("Watcher"), sessionProgram);
sessionEngine2.SessionStarted();
sessionEngine2.SceneStarted();
sessionEngine2.Tick(0);
sessionEngine2.SceneStarted();
sessionEngine2.Tick(1);
Check(sessionEngine2.Variables.GetGlobal("SessionLog").AsText() == "GSS", "scene start refires on every scene load, game start does not");

var appearProgram = parser.Parse("when me appears\n  set Born to Born + 1");
var appearEngine = FreshEngine(new FakeWorld());
appearEngine.AddSheet(new FakeActor("Spawned"), appearProgram);
appearEngine.Tick(0);
appearEngine.Tick(1);
Check(appearEngine.Variables.GetGlobal("Born").AsNumber() == 1, "`when me appears` pushes once when a sheet is added");

var waitWorld = new FakeWorld();
var waitEngine = FreshEngine(waitWorld);
waitEngine.OnRawAction = (context, words) => { };
var waitProgram = parser.Parse(@"when game starts
  set Order to """"
when every 3 seconds
  wait 1 second
  set Order to Order + ""W""
when every 4 seconds
  set Order to Order + ""T""");
var waitActor = new FakeActor("Clock");
waitEngine.AddSheet(waitActor, waitProgram);
waitEngine.SessionStarted();
waitEngine.Tick(0);
waitEngine.Tick(3);
Check(waitEngine.Variables.GetGlobal("Order").AsText() == "", "wait suspends only the event, the game keeps ticking");
waitEngine.Tick(4);
Check(waitEngine.Variables.GetGlobal("Order").AsText() == "WT", "suspended continuations resume before the fresh polled pass");

Rng.Seed(11);
var expectedSpan = ValueEvaluator.Evaluate(new RandomRangeValue { Min = new NumberValue { Value = 0.5 }, Max = new NumberValue { Value = 2 } }, null).AsNumber();
var randomWaitEngine = FreshEngine(new FakeWorld());
var randomWaitLog = new List<string>();
randomWaitEngine.OnRawAction = (context, words) => randomWaitLog.Add(J(words));
var randomWaitProgram = parser.Parse(@"when me overlaps Bomb
  wait random 0.5 to 2 seconds
  set Order to Order + ""late""");
var randomWaitSheet = randomWaitEngine.AddSheet(new FakeActor("RandomWish"), randomWaitProgram);
Rng.Seed(11);
randomWaitEngine.PushContact(TriggerKind.Overlaps, randomWaitSheet.Actor, new FakeActor("Bomb"));
randomWaitEngine.Tick(3);
bool pendingFound = false;
double resumeAt = 0;
foreach (var waiting in randomWaitSheet.Waiting)
{
    pendingFound = true;
    resumeAt = waiting.ResumeAt;
}
Check(pendingFound && Math.Abs(resumeAt - (3 + expectedSpan)) < 0.000001, $"wait random honors the seeded span (expected {3 + expectedSpan}, got {resumeAt})");
randomWaitEngine.Tick(resumeAt);
Check(randomWaitEngine.Variables.GetGlobal("Order").AsText() == "late", "the suspended event resumes exactly on time");

var gateWorld = new FakeWorld();
var gateEngine = FreshEngine(gateWorld);
var gateLog = new List<string>();
var gateProgram = parser.Parse(@"when me overlaps Bomb
  unless Score is 0
  set Order to Order + ""A""
  unless Score is 5
  set Order to Order + ""B""");
var gateSheet = gateEngine.AddSheet(new FakeActor("Door"), gateProgram);
var bomb = new FakeActor("Bomb");
gateEngine.Variables.SetGlobal("Score", ValueBox.OfNumber(0));
gateEngine.PushContact(TriggerKind.Overlaps, gateSheet.Actor, bomb);
gateEngine.Tick(0);
Check(gateEngine.Variables.GetGlobal("Order").AsText() == "B", "an `unless` line gates the actions below it until the next condition line");
gateEngine.Variables.SetGlobal("Score", ValueBox.OfNumber(5));
gateEngine.PushContact(TriggerKind.Overlaps, gateSheet.Actor, bomb);
gateEngine.Tick(1);
Check(gateEngine.Variables.GetGlobal("Order").AsText() == "BA", "the gate follows each new condition line");
var gateEngine2 = FreshEngine(new FakeWorld());
var gateProgram2 = parser.Parse(@"when every 10 seconds
  my mode is ""chase""
  set Order to Order + ""!""
  my mode is ""patrol""
  set Order to Order + ""?""");
var gateSheet2 = gateEngine2.AddSheet(new FakeActor("Robot"), gateProgram2);
gateEngine2.Variables.SetLocal(gateSheet2.Actor, "mode", ValueBox.OfText("chase"));
gateEngine2.Tick(10);
Check(gateEngine2.Variables.GetGlobal("Order").AsText() == "!", "bare condition lines gate like `unless` lines (matching = run)");

var stopWorld = new FakeWorld();
var stopEngine = FreshEngine(stopWorld);
var stopLog = new List<string>();
var stopProgram = parser.Parse(@"when me overlaps Bomb
  set Log to Log + ""S""
  stop this event
  set Log to Log + ""NEVER""
when me overlaps Bomb
  set Log to Log + ""Z""");
var stopSheet = stopEngine.AddSheet(new FakeActor("Shield"), stopProgram);
stopEngine.OnRawAction = (context, words) => stopLog.Add(J(words));
stopEngine.PushContact(TriggerKind.Overlaps, stopSheet.Actor, bomb);
stopEngine.Tick(0);
Check(stopEngine.Variables.GetGlobal("Log").AsText() == "SZ", "stop this event ends it early; other events still run");

var heldWorld = new FakeWorld();
var heldEngine = FreshEngine(heldWorld);
var heldLog = new List<string>();
var heldProgram = parser.Parse("when W is held\n  move forward speed 5");
var heldSheet = heldEngine.AddSheet(new FakeActor("Hero"), heldProgram);
heldEngine.OnRawAction = (context, words) => heldLog.Add(J(words));
heldWorld.SetHeld("W", true);
heldEngine.Tick(0);
heldEngine.Tick(1);
heldWorld.SetHeld("W", false);
heldEngine.Tick(2);
Check(heldLog.Count == 2, "`when W is held` fires every tick while held, then stops");

var sprintWorld = new FakeWorld();
var sprintEngine = FreshEngine(sprintWorld);
var sprintLog = new List<string>();
var sprintProgram = parser.Parse(@"when W is held
  unless Left Shift is held
  move forward speed 40
when Left Shift is held
  W is held
  move forward speed 70");
var sprintSheet = sprintEngine.AddSheet(new FakeActor("Runner"), sprintProgram);
sprintEngine.OnRawAction = (context, words) => sprintLog.Add(words.Count >= 4 ? words[3].Text + " " + words[1].Text : "?");
sprintWorld.SetHeld("W", true);
sprintEngine.Tick(0);
Check(sprintLog.Count == 1 && sprintLog[0] == "40 forward", "walk alone moves the normal speed (sprint cut event)");
sprintWorld.SetHeld("Left Shift", true);
sprintLog.Clear();
sprintEngine.Tick(1);
Check(sprintLog.Count == 1 && sprintLog[0] == "70 forward", "shift down swaps the step: W walk skipped, sprint runs");
sprintWorld.SetHeld("W", false);
sprintLog.Clear();
sprintEngine.Tick(2);
Check(sprintLog.Count == 0, "shift alone goes nowhere (sprint is gated to W being held too)");

var watchEngine = FreshEngine(new FakeWorld());
var watchProgram = parser.Parse(@"when Score changes
  set Hits to Hits + 1
when Score becomes 5
  set Wins to Wins + 1");
watchEngine.AddSheet(new FakeActor("Herald"), watchProgram);
watchEngine.Tick(0);
watchEngine.Variables.SetGlobal("Score", ValueBox.OfNumber(1));
watchEngine.Tick(1);
watchEngine.Variables.SetGlobal("Score", ValueBox.OfNumber(2));
watchEngine.Tick(2);
watchEngine.Tick(3);
Check(watchEngine.Variables.GetGlobal("Hits").AsNumber() == 2, "`when Score changes` fires once per real change");

var perObjectWatchWorld = new FakeWorld();
var perObjectWatchEngine = FreshEngine(perObjectWatchWorld);
var healthProgram = parser.Parse("when my health becomes 0\n  set Died to Died + 1");
var wounded = new FakeActor("Sorcerer");
var woundedSheet = perObjectWatchEngine.AddSheet(wounded, healthProgram);
perObjectWatchEngine.Variables.SetLocal(wounded, "health", ValueBox.OfNumber(3));
perObjectWatchEngine.Tick(0);
perObjectWatchEngine.Variables.SetLocal(wounded, "health", ValueBox.OfNumber(0));
perObjectWatchEngine.Tick(1);
perObjectWatchEngine.Tick(2);
Check(perObjectWatchEngine.Variables.GetGlobal("Died").AsNumber() == 1, "`when my health becomes 0` fires once per actor when the value turns");
perObjectWatchEngine.Variables.SetLocal(wounded, "health", ValueBox.OfNumber(0));
perObjectWatchEngine.Tick(3);
Check(perObjectWatchEngine.Variables.GetGlobal("Died").AsNumber() == 1, "watching the same value again does not refire `becomes`");

var perObjectWatchEngine2 = FreshEngine(new FakeWorld());
var wounded2 = new FakeActor("Oracle");
var wounded2Sheet = perObjectWatchEngine2.AddSheet(wounded2, healthProgram);
perObjectWatchEngine2.Variables.SetLocal(wounded2, "health", ValueBox.OfNumber(0));
perObjectWatchEngine2.Tick(0);
perObjectWatchEngine2.Variables.SetLocal(wounded2, "health", ValueBox.OfNumber(9));
perObjectWatchEngine2.Tick(1);
perObjectWatchEngine2.Variables.SetLocal(wounded2, "health", ValueBox.OfNumber(0));
perObjectWatchEngine2.Tick(2);
Check(perObjectWatchEngine2.Variables.GetGlobal("Died").AsNumber() == 1, "each actor copy owns its watch state");

var messageWorld = new FakeWorld();
var messageEngine = FreshEngine(messageWorld);
var senderProgram = parser.Parse("when me touches Bomb\n  send message \"hit\"\n  send message \"boom\" to Oracle");
var enemyProgram = parser.Parse(@"when message ""hit"" arrives
  set Hits to Hits + 1");
var listenerProgram = parser.Parse(@"when message ""boom"" arrives
  set Booms to Booms + 1");
var swordGuy = new FakeActor("Sorcerer");
var oracleGuy = new FakeActor("Oracle");
messageEngine.AddSheet(oracleGuy, listenerProgram);
messageEngine.AddSheet(swordGuy, enemyProgram);
messageEngine.AddSheet(swordGuy, senderProgram);
messageEngine.PushContact(TriggerKind.Touches, swordGuy, new FakeActor("Bomb"));
messageEngine.Tick(0);
bool snapshotMessageHits = messageEngine.Variables.GetGlobal("Hits").IsNone();
Check(snapshotMessageHits, "messages fan out to their sheets on the tick after the push");
messageEngine.Tick(1);
Check(messageEngine.Variables.GetGlobal("Hits").AsNumber() == 1, "the targeted sheet received its message");
Check(messageEngine.Variables.GetGlobal("Booms").AsNumber() == 1, "send message \"...\" to <thing> routes to the named actor");
var messageEngine2 = FreshEngine(messageWorld);
messageEngine2.AddSheet(oracleGuy, listenerProgram);
messageEngine2.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "boom" }, "Oracle");
messageEngine2.Tick(0);
Check(messageEngine2.Variables.GetGlobal("Booms").AsNumber() == 1, "broadcast pushes land via RaisePushAll + engine routing helpers too");

Rng.Seed(5);
var chanceWorld = new FakeWorld();
var chanceEngine = FreshEngine(chanceWorld);
var chanceLog = new List<string>();
var chanceProgram = parser.Parse(@"when me overlaps Bomb
  unless chance 100%
  set Log to Log + ""cold""
  unless chance 0%
  set Log to Log + ""hot""");
var chanceSheet = chanceEngine.AddSheet(new FakeActor("Dicehead"), chanceProgram);
chanceEngine.PushContact(TriggerKind.Overlaps, chanceSheet.Actor, bomb);
chanceEngine.Tick(0);
Check(chanceEngine.Variables.GetGlobal("Log").AsText() == "hot", "chance conditions gate by percentage (100% never blocks, 0% always blocks)");

var bagWorld = new FakeWorld();
var bagEngine = FreshEngine(bagWorld);
var bagProgram = parser.Parse(@"when me overlaps Bomb
  my bag has Sword
  set Log to Log + ""W""");
var bagSheet = bagEngine.AddSheet(new FakeActor("BagGuy"), bagProgram);
bagEngine.PushContact(TriggerKind.Overlaps, bagSheet.Actor, bomb);
bagEngine.Tick(0);
Check(bagEngine.Variables.GetGlobal("Log").Kind == ValueKind.None, "an empty bag rejects its condition");
bagEngine.BagAdd(bagSheet.Actor, "Sword");
bagEngine.PushContact(TriggerKind.Overlaps, bagSheet.Actor, bomb);
bagEngine.Tick(1);
Check(bagEngine.Variables.GetGlobal("Log").AsText() == "W", "`my bag has <thing>` reflects the bag contents");
bagEngine.BagRemove(bagSheet.Actor, "Sword");
bagEngine.PushContact(TriggerKind.Overlaps, bagSheet.Actor, bomb);
bagEngine.Tick(2);
Check(bagEngine.Variables.GetGlobal("Log").AsText() == "W", "removing a thing from the bag unblocks the condition again");

var unsetEngine = new EventRuntime();
int runtimeWarnings = 0;
unsetEngine.OnRuntimeWarning = message => runtimeWarnings++;
unsetEngine.AddSheet(new FakeActor("Fresh"), parser.Parse("when me appears\n  set Order to Order + \"A\""));
unsetEngine.SessionStarted();
unsetEngine.Tick(0);
Check(runtimeWarnings == 1, "the engine surfaces one unset-read warning and no more");

var nearWorld = new FakeWorld();
var nearEngine = FreshEngine(nearWorld);
var humanGuy = new FakeActor("MeGuy");
var towerGuy = new FakeActor("Tower");
var nearProgram = parser.Parse(@"when every 1 seconds
  me is near Tower by 4
  set NearCount to NearCount + 1");
nearEngine.AddSheet(humanGuy, nearProgram);
nearEngine.AddSheet(towerGuy, parser.Parse("# placeholder\n"));
nearWorld.SetDistance(humanGuy, towerGuy, 2.5);
nearEngine.Tick(1);
nearWorld.SetDistance(humanGuy, towerGuy, 9.5);
nearEngine.Tick(2);
Check(nearEngine.Variables.GetGlobal("NearCount").AsNumber() == 1, "`me is near <thing> by <dist>` uses world distances");

var existsWorld = new FakeWorld();
var existEngine = FreshEngine(existsWorld);
var existsProgram = parser.Parse(@"when every 1 seconds
  unless Key exists
  set NoKey to NoKey + 1
  unless Key does not exist
  set YesKey to YesKey + 1");
existEngine.AddSheet(new FakeActor("Digger"), existsProgram);
existEngine.Tick(1);
Check(existEngine.Variables.GetGlobal("NoKey").AsNumber() == 1 && existEngine.Variables.GetGlobal("YesKey").IsNone(), "`<thing> exists` / `does not exist` reads the scene");

var tagWorld = new FakeWorld();
var tagEngine = FreshEngine(tagWorld);
var tagProgram = parser.Parse(@"when me overlaps Bomb
  other has tag Enemy
  set TagHits to TagHits + 1
  other has tag Friend
  set FriendHits to FriendHits + 1");
var tagSheet = tagEngine.AddSheet(new FakeActor("Mapper"), tagProgram);
tagWorld.SetTag(bomb, "Enemy");
tagEngine.PushContact(TriggerKind.Overlaps, tagSheet.Actor, bomb);
tagEngine.Tick(0);
Check(tagEngine.Variables.GetGlobal("TagHits").AsNumber() == 1 && tagEngine.Variables.GetGlobal("FriendHits").Kind == ValueKind.None, "tag conditions read world tags");

static List<Token> RawWords(EventParser parser, string source)
{
    var program = parser.Parse(source);
    var raw = (RawActionNode)program.Events[0].Body[0].Statement!;
    return raw.Words;
}

static List<Token> LooseWords(params string[] words)
{
    var tokens = new List<Token>();
    foreach (string word in words)
    {
        var token = new Token { Type = TokenType.Word, Text = word, Line = 2 };
        if (double.TryParse(word, out double number))
        {
            token.Type = TokenType.Number;
            token.Number = number;
        }
        tokens.Add(token);
    }
    return tokens;
}

var actionParser = new EventParser();
string actionHint = "";
Action<string> GrabHint = message =>
{
    actionHint = message;
};

var moveSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  move forward speed 5"), null);
Check(moveSpec.Verb == ActionVerb.Move && moveSpec.Dir == "forward" && moveSpec.HasNumber && moveSpec.Number == 5,
    "`move forward speed 5` parses as Move_LEFT forward 5");

var fastSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  move left speed 9.5"), null);
Check(fastSpec.Verb == ActionVerb.Move && fastSpec.Dir == "left" && fastSpec.Number == 9.5,
    "`move left speed 9.5` keeps the decimal speed");

var bareMove = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  move backward"), null);
Check(bareMove.Verb == ActionVerb.Move && bareMove.Dir == "backward" && bareMove.HasNumber && bareMove.Number == 5,
    "`move backward` still maps the default speed 5");

var awaySpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  move away from Player"), null);
Check(awaySpec.Verb == ActionVerb.Move && awaySpec.Dir == "away" && awaySpec.Thing == "Player",
    "`move away from Player` reads the chaser target");

var stopSpec = ActionWords.Parse(RawWords(actionParser, @"when W is released
  stop moving"), null);
Check(stopSpec.Verb == ActionVerb.StopMoving, "`stop moving` parses");

var turnSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  turn left degrees 90"), null);
Check(turnSpec.Verb == ActionVerb.Turn && turnSpec.Dir == "left" && turnSpec.Number == 90,
    "`turn left degrees 90` parses");

var lookSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  look at Enemy"), null);
Check(lookSpec.Verb == ActionVerb.LookAt && lookSpec.Thing == "Enemy",
    "`look at Enemy` parses");

var faceSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  face Player"), null);
Check(faceSpec.Verb == ActionVerb.Face && faceSpec.Thing == "Player",
    "`face Player` parses as the continuous tracker");

var teleportSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  teleport me to SpawnPoint"), null);
Check(teleportSpec.Verb == ActionVerb.Teleport && teleportSpec.Thing == "me" && teleportSpec.To == "SpawnPoint",
    "`teleport me to SpawnPoint` reads both things");

var pushSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  push other up strength 10"), null);
Check(pushSpec.Verb == ActionVerb.Push && pushSpec.Thing == "other" && pushSpec.Dir == "up" && pushSpec.Number == 10,
    "`push other up strength 10` parses");

var pushQuick = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  push other left"), null);
Check(pushQuick.Verb == ActionVerb.Push && pushQuick.Dir == "left" && pushQuick.HasNumber && pushQuick.Number == 5,
    "`push other left` sets the quiet strength 5");

var gotoSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  goto SpawnPoint"), null);
Check(gotoSpec.Verb == ActionVerb.Goto && gotoSpec.Thing == "SpawnPoint",
    "`goto SpawnPoint` parses");

var destroyMe = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  destroy me"), null);
Check(destroyMe.Verb == ActionVerb.Destroy && destroyMe.Thing == "me", "`destroy me` parses");

var spawnSpec = ActionWords.Parse(RawWords(actionParser, @"when me overlaps SpawnPoint
  spawn Bullet at me"), null);
Check(spawnSpec.Verb == ActionVerb.Spawn && spawnSpec.Thing == "Bullet" && spawnSpec.To == "me",
    "`spawn Bullet at me` parses");

var spawnAtThing = ActionWords.Parse(RawWords(actionParser, @"when me overlaps SpawnPoint
  spawn Coin at SpawnPoint"), null);
Check(spawnAtThing.Verb == ActionVerb.Spawn && spawnAtThing.Thing == "Coin" && spawnAtThing.To == "SpawnPoint",
    "`spawn Coin at SpawnPoint` parses");

var soundSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  play sound ""Thud.wav"""), null);
Check(soundSpec.Verb == ActionVerb.PlaySound && soundSpec.Thing == "Thud.wav",
    "`play sound \"Thud.wav\"` reads the file");

var animSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  play anim ""Attack"""), null);
Check(animSpec.Verb == ActionVerb.PlayAnim && animSpec.Thing == "Attack", "`play anim \"Attack\"` parses");

var stopSound = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  stop sound"), null);
Check(stopSound.Verb == ActionVerb.StopSound, "`stop sound` parses");

var stopAnim = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  stop anim"), null);
Check(stopAnim.Verb == ActionVerb.StopAnim, "`stop anim` parses");

var hideSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  hide me"), null);
Check(hideSpec.Verb == ActionVerb.Hide && hideSpec.Thing == "me", "`hide me` parses (hiding)");

var showSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  show PausePanel"), null);
Check(showSpec.Verb == ActionVerb.Show && showSpec.Thing == "PausePanel", "`show PausePanel` parses");

var restartSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  restart scene"), null);
Check(restartSpec.Verb == ActionVerb.RestartScene, "`restart scene` parses");

var loadSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  load scene ""Menus"""), null);
Check(loadSpec.Verb == ActionVerb.LoadScene && loadSpec.Thing == "Menus",
    "`load scene \"Menus\"` parses");

var quitSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  quit game"), null);
Check(quitSpec.Verb == ActionVerb.QuitGame, "`quit game` parses");

var bananaAction = ActionWords.Parse(LooseWords("banana", "time"), GrabHint);
Check(bananaAction.Verb == ActionVerb.Unknown && actionHint.Contains("banana"),
    "an odd action line gets the unknown-word explanation");

actionHint = "";
var oddMove = ActionWords.Parse(LooseWords("move", "sideways"), GrabHint);
Check(oddMove.Verb == ActionVerb.Unknown && actionHint.Contains("\u201Cmove\u201D"),
    "a bent `move` line explains the shape (move looks like this)");

string tempBase = Path.Combine(Path.GetTempPath(), "evtest-" + Guid.NewGuid().ToString("N"));
string gameDir = Path.Combine(tempBase, "Source", "Game", "GameFiles");
Directory.CreateDirectory(gameDir);
File.WriteAllText(Path.Combine(gameDir, "Player.events"), "when game starts\n  set Score to 0");
File.WriteAllText(Path.Combine(gameDir, "Broken.events"), "when game starts\n  banana");
string located = EventFiles.LocateGameFiles(gameDir);
Check(located == gameDir, "the loader finds the GameFiles folder itself");
string fromDeep = EventFiles.LocateGameFiles(Path.Combine(tempBase, "Source", "Game"));
Check(fromDeep == gameDir, "the loader walks up from a deeper folder");
Check(EventFiles.LocateGameFiles(tempBase) == gameDir, "the loader finds GameFiles from the project root");
File.WriteAllText(Path.Combine(tempBase, "Solo.events"), "when game starts\n  set Score to 0");
string looseRoot = Path.Combine(tempBase, "Loose");
Directory.CreateDirectory(Path.Combine(looseRoot, "GameFiles"));
File.WriteAllText(Path.Combine(looseRoot, "GameFiles", "Solo.events"), "when game starts\n  set Score to 0");
Check(EventFiles.LocateGameFiles(looseRoot) == Path.Combine(looseRoot, "GameFiles"),
    "a loose GameFiles folder works too (GameFiles path found)");
Check(EventFiles.LocateGameFiles(Path.GetTempPath()) == null,
    "no GameFiles anywhere is null (not a crash)");

var loaded = EventFiles.LoadAll(gameDir);
Check(loaded.Count == 2, "the loader reads both .events files");
Check(loaded[0].Name == "Broken" && loaded[1].Name == "Player", "the loader sorts files by name");
Check(loaded[0].Error.Length > 0 && loaded[0].Error.Contains("banana"),
    "the broken file surfaces its friendly error");
Check(loaded[0].File != null && loaded[0].File.Events.Count == 0,
    "a broken parse yields an empty tree (the old tree keeps playing during hot reload)");
Check(loaded[1].Error.Length == 0 && loaded[1].File.Events.Count == 1,
    "the good file parses to one event");

Directory.Delete(tempBase, true);

var reloadParser = new EventParser();
var reloadEngine = FreshEngine(new FakeWorld());
var timerBefore = reloadParser.Parse(@"when every 1 seconds
  add 1 to TimerTicks");
var timerSheet = reloadEngine.AddSheet(new FakeActor("Watcher"), timerBefore);
reloadEngine.Tick(0.5);
Check(reloadEngine.Variables.GetGlobal("TimerTicks").Kind == ValueKind.None, "before the swap the timer keeps its old counting");
reloadEngine.Tick(1);
Check(reloadEngine.Variables.GetGlobal("TimerTicks").AsNumber() == 1, "the old program fires its own timer first");

reloadEngine.Variables.SetGlobal("Score", ValueBox.OfNumber(5));
reloadEngine.Variables.SetGlobal("TimerTicks", ValueBox.OfNumber(1));
var timerAfter = reloadParser.Parse(@"when every 0.5 seconds
  add 1 to HalfTicks");
reloadEngine.SwapSheetProgram(timerSheet, timerAfter);
Check(reloadEngine.Variables.GetGlobal("Score").AsNumber() == 5,
    "variable values survive the swap untouched");
reloadEngine.Tick(1.5);
Check(reloadEngine.Variables.GetGlobal("HalfTicks").AsNumber() == 1 && reloadEngine.Variables.GetGlobal("TimerTicks").AsNumber() == 1,
    "after the swap the new program's timer runs and the old one is gone");

var waitingBefore = reloadParser.Parse(@"when game starts
  wait 2 seconds
  set ResumeLog to Late");
var waitingSheet = reloadEngine.AddSheet(new FakeActor("Waiter"), waitingBefore);
reloadEngine.SessionStarted();
reloadEngine.SwapSheetProgram(waitingSheet, reloadParser.Parse(@"when game starts
  set ResumeLog to Nope"));
reloadEngine.Tick(3);
Check(reloadEngine.Variables.GetGlobal("ResumeLog").Kind == ValueKind.None,
    "a `wait` from the old tree is dropped on the swap (fresh state)");

var staleSheet = reloadEngine.AddSheet(new FakeActor("EdgeListener"), reloadParser.Parse(@"when K is pressed
  set StalePress to 1"));
reloadEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "K" });
reloadEngine.SwapSheetProgram(staleSheet, reloadParser.Parse(@"when K is pressed
  set StalePress to 2"));
reloadEngine.Tick(0);
Check(reloadEngine.Variables.GetGlobal("StalePress").Kind == ValueKind.None,
    "queued pushes from before the swap don't fire into the new tree");

var appearSheet = reloadEngine.AddSheet(new FakeActor("Reborn"), reloadParser.Parse(@"when game starts
  set DummyFlag to 1"));
reloadEngine.SessionStarted();
reloadEngine.Tick(0);
reloadEngine.SwapSheetProgram(appearSheet, reloadParser.Parse(@"when me appears
  set NewBorn to 1"));
reloadEngine.Tick(0.1);
Check(reloadEngine.Variables.GetGlobal("NewBorn").Kind == ValueKind.None,
    "the swapped tree does not auto-fire `appears` (that only happens on real adds)");

Check(reloadEngine.FindActorSheet("Watcher") == timerSheet && reloadEngine.FindActorSheet("Game") == null,
    "FindActorSheet dedupes attach lookups and never returns the global sheet");

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
if (failed > 0)
    Environment.Exit(1);

class FakeActor
{
    public FakeActor(string name)
    {
        Name = name;
    }

    public string Name;

    public override string ToString()
    {
        return Name;
    }
}

class FakeWorld : IEventWorld
{
    private readonly HashSet<string> held = new HashSet<string>();
    private readonly Dictionary<(object, object), double> distances = new Dictionary<(object, object), double>();
    private readonly HashSet<(object, string)> tags = new HashSet<(object, string)>();

    public void SetHeld(string key, bool on)
    {
        if (on)
            held.Add(key);
        else
            held.Remove(key);
    }

    public void SetDistance(object a, object b, double d)
    {
        distances[(a, b)] = d;
    }

    public void SetTag(object target, string tag)
    {
        tags.Add((target, tag));
    }

    public bool IsKeyHeld(string key)
    {
        return held.Contains(key);
    }

    public double Distance(object from, object to)
    {
        if (distances.TryGetValue((from, to), out double direct))
            return direct;
        if (distances.TryGetValue((to, from), out double reverse))
            return reverse;
        return 100000;
    }

    public bool HasTag(object target, string tag)
    {
        return tags.Contains((target, tag));
    }
}
