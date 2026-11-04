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
ExpectParseError("when game starts\n  set Score to damage\n", "capital", "a lowercase value word explains the variable rule");
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

var animEnd = parser.Parse("when anim \"Shoot\" ends\n  play anim \"Idle\"\n");
Check(animEnd.Events.Count == 1, "`when anim \"Shoot\" ends` parses as an event");
var animDesc = TriggerClassifier.Classify(animEnd.Events[0]);
Check(animDesc.Kind == TriggerKind.AnimEnd && animDesc.RequireName == "Shoot",
    "`when anim \"Shoot\" ends` classifies to AnimEnd with RequireName \"Shoot\"");

var animEndEngine = FreshEngine(new FakeWorld());
animEndEngine.AddSheet(new FakeActor("Dragon"),
    parser.Parse("when anim \"shoot\" ends\n  set AnimEnded to 1\n"));
animEndEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.AnimEnd, Name = "Shoot" });
animEndEngine.Tick(0);
Check(animEndEngine.Variables.GetGlobal("AnimEnded").AsNumber() == 1,
    "`when anim \"shoot\" ends` matches the clip name case-insensitively");

var sceneStart = parser.Parse("when scene starts\n  set Score to 0\n");
Check(sceneStart.Events.Count == 1, "`when scene starts` parses as an event");
var sceneDesc = TriggerClassifier.Classify(sceneStart.Events[0]);
Check(sceneDesc.Kind == TriggerKind.SceneStart, "`when scene starts` classifies to SceneStart");


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

var crossingEngine = FreshEngine(new FakeWorld());
var crossingProgram = parser.Parse(@"when message ""hit"" arrives
  add -1 to my health

when my health becomes 0
  set Died to Died + 1");
var crossingGuy = new FakeActor("Enemy");
crossingEngine.AddSheet(crossingGuy, crossingProgram);
crossingEngine.Tick(0);
Check(crossingEngine.Variables.GetGlobal("Died").IsNone(), "a fresh enemy with unset health has not died yet");
crossingEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "hit" }, "Enemy");
crossingEngine.Tick(1);
Check(crossingEngine.Variables.GetGlobal("Died").AsNumber() == 1,
    "the shooter's `my health becomes 0` fires on the first hit when health counts down from unset | Died=" +
    crossingEngine.Variables.GetGlobal("Died").AsNumber() + " HealthKind=" + crossingEngine.Variables.GetLocal(crossingGuy, "health").Kind + " Health=" + crossingEngine.Variables.GetLocal(crossingGuy, "health").AsNumber());
crossingEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "hit" }, "Enemy");
crossingEngine.Tick(2);
Check(crossingEngine.Variables.GetGlobal("Died").AsNumber() == 1, "walking further below zero does not refire `becomes 0`");

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

// ------------------------------------------------------------------
// Task 8 Part A: the bag (add / remove / has, `my bag` as text, possessive reads)
// ------------------------------------------------------------------

var removeSpec = parser.Parse("when me appears\n  remove Potion from my bag\n");
var removeNode = (BagRemoveNode)removeSpec.Events[0].Body[0].Statement!;
Check(removeNode.Thing == "Potion", "`remove Potion from my bag` parses to a BagRemoveNode");
ExpectParseError("when me appears\n  remove Potion\n", "looks like", "`remove` without `from my bag` is a friendly error");
ExpectParseError("when me appears\n  remove from my bag\n", "looks like", "`remove` needs a thing to remove");

var bagTextSpec = parser.Parse("when me appears\n  set text of BagLabel to my bag\n");
var setTextNode = (SetTextNode)bagTextSpec.Events[0].Body[0].Statement!;
Check(setTextNode.Value is VariableValue bv && bv.Owner == "my" && bv.Name == "bag",
    "`set text of BagLabel to my bag` reads `my bag` as a value");

var possessiveSpec = parser.Parse("when me appears\n  set Dmg to other's damage\n");
var possessiveSet = (SetNode)possessiveSpec.Events[0].Body[0].Statement!;
Check(possessiveSet.Value is VariableValue pv && pv.Owner == "other" && pv.Name == "damage",
    "`other's damage` parses as a possessive read");

// runtime: the bag holds things by name and count via the `add ... to my bag` word
var bagRun = FreshEngine(new FakeWorld());
var bagPlayer = new FakeActor("Player");
bagRun.AddSheet(bagPlayer, parser.Parse("when game starts\n  add Sword to my bag\n  add Sword to my bag\n  add Potion to my bag"));
bagRun.SessionStarted();
bagRun.Tick(0);
Check(bagRun.BagText(bagPlayer) == "Sword x2, Potion",
    "two Swords and a Potion read back as \"Sword x2, Potion\"");
Check(bagRun.BagHas(bagPlayer, "Sword") && bagRun.BagHas(bagPlayer, "Potion") &&
      !bagRun.BagHas(bagPlayer, "Shield"),
    "`my bag has <thing>` reflects the bag contents after `add`");

// `my bag` evaluates to the bag text summary (what `set text of BagLabel to my bag` writes)
var bagCtx = new EventContext { Variables = bagRun.Variables, Me = bagPlayer, Runtime = bagRun };
Check(bagCtx.Evaluate(new VariableValue { Owner = "my", Name = "bag" }).AsText() == "Sword x2, Potion",
    "`my bag` evaluates to the bag text summary");

// `remove <thing> from my bag` decrements the count
var removeEngine = FreshEngine(new FakeWorld());
var remPlayer = new FakeActor("Player");
removeEngine.AddSheet(remPlayer, parser.Parse("when game starts\n  add Sword to my bag\n  add Sword to my bag\n  remove Sword from my bag"));
removeEngine.SessionStarted();
removeEngine.Tick(0);
Check(removeEngine.BagText(remPlayer) == "Sword",
    "`remove Sword from my bag` decrements the count (Sword x2 -> Sword)");

// possessive reads: `other's damage` via a message — `other` is the message sender
var dmgEngine = FreshEngine(new FakeWorld());
var victim = new FakeActor("Victim");
var attacker = new FakeActor("Attacker");
dmgEngine.Variables.SetLocal(attacker, "damage", ValueBox.OfNumber(7));
dmgEngine.AddSheet(victim, parser.Parse("when message \"damage\" arrives\n  set Dmg to other's damage"));
dmgEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "damage", Other = attacker }, "Victim");
dmgEngine.Tick(1);
Check(dmgEngine.Variables.GetGlobal("Dmg").AsNumber() == 7,
    "`other's damage` reads the sender's `my` variable through a message");

// `Player's gold` reads a named actor's `my` variable
var goldEngine = FreshEngine(new FakeWorld());
var goldPlayer = new FakeActor("Player");
goldEngine.AddSheet(goldPlayer, parser.Parse("when game starts\n  set my gold to 50"));
goldEngine.SessionStarted();
var goldReader = new FakeActor("Reader");
goldEngine.AddSheet(goldReader, parser.Parse("when me appears\n  set Loot to Player's gold"));
goldEngine.Tick(0);
Check(goldEngine.Variables.GetLocal(goldPlayer, "gold").AsNumber() == 50,
    "the Player carries `my Gold`");
Check(goldEngine.Variables.GetGlobal("Loot").AsNumber() == 50,
    "`Player's gold` reads another actor's `my` variable");

// ------------------------------------------------------------------
// Task 8 Part B: equip & item events
// ------------------------------------------------------------------

var equipSpec = parser.Parse("when me appears\n  equip Sword\n");
var equipNode = (EquipNode)equipSpec.Events[0].Body[0].Statement!;
Check(equipNode.Thing == "Sword", "`equip Sword` parses to an EquipNode");
var dropSpec = parser.Parse("when me appears\n  drop Potion\n");
var dropNode = (DropNode)dropSpec.Events[0].Body[0].Statement!;
Check(dropNode.Thing == "Potion", "`drop Potion` parses to a DropNode");
ExpectParseError("when me appears\n  equip\n", "looks like", "`equip` without a thing is a friendly error");
ExpectParseError("when me appears\n  drop\n", "looks like", "`drop` without a thing is a friendly error");

// runtime: equipping takes the item from the bag, and the item's `gets equipped` event
// runs with `me` = the wearer, so `add 5 to my damage` raises the wearer's stat
var equipWorld = new FakeWorld();
var equipEngine = FreshEngine(equipWorld);
var eqPlayer = new FakeActor("Player");
var eqSword = new FakeActor("Sword");
equipWorld.AddActor(eqPlayer);
equipWorld.AddActor(eqSword);
equipEngine.AddSheet(eqPlayer, parser.Parse("when game starts\n  add Sword to my bag\n  equip Sword"));
equipEngine.AddSheet(eqSword, parser.Parse("when me gets equipped\n  add 5 to my damage"));
equipEngine.SessionStarted();
equipEngine.Tick(0);
Check(!equipEngine.BagHas(eqPlayer, "Sword"), "equipping takes the item out of the bag");
Check(equipEngine.Variables.GetLocal(eqPlayer, "damage").AsNumber() == 5,
    "`when me gets equipped` runs with `me` = the wearer (Player's damage = 5)");
Check(equipEngine.Variables.GetLocal(eqSword, "damage").Kind == ValueKind.None,
    "the wearer's stat changed, not the item's own stat");
Check(equipEngine.IsEquipped(eqPlayer, "Sword"), "Sword is equipped on Player after `equip Sword`");

// `drop Sword` unequips: `gets unequipped` runs with `me` = the wearer, taking the stat back
var dropWorld = new FakeWorld();
var dropEngine = FreshEngine(dropWorld);
var dPlayer = new FakeActor("Player");
var dSword = new FakeActor("Sword");
dropWorld.AddActor(dPlayer);
dropWorld.AddActor(dSword);
dropEngine.AddSheet(dPlayer, parser.Parse("when game starts\n  add Sword to my bag\n  equip Sword\nwhen Q is pressed\n  drop Sword"));
dropEngine.AddSheet(dSword, parser.Parse("when me gets equipped\n  add 5 to my damage\nwhen me gets unequipped\n  add -5 to my damage"));
dropEngine.SessionStarted();
dropEngine.Tick(0);
Check(dropEngine.Variables.GetLocal(dPlayer, "damage").AsNumber() == 5, "equipped: damage = 5");
dropEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Q" });
dropEngine.Tick(1);
Check(dropEngine.Variables.GetLocal(dPlayer, "damage").AsNumber() == 0,
    "`drop Sword` runs `gets unequipped` with `me` = the wearer (damage back to 0)");
Check(!dropEngine.IsEquipped(dPlayer, "Sword"), "after `drop`, the item is no longer equipped");

// `gets dropped` fires on the item's sheet with `me` = the item itself
var dropWorld2 = new FakeWorld();
var dropEngine2 = FreshEngine(dropWorld2);
var drPlayer = new FakeActor("Player");
var drSword = new FakeActor("Sword");
dropWorld2.AddActor(drPlayer);
dropWorld2.AddActor(drSword);
dropEngine2.AddSheet(drPlayer, parser.Parse("when game starts\n  add Sword to my bag\n  equip Sword\nwhen Q is pressed\n  drop Sword"));
dropEngine2.AddSheet(drSword, parser.Parse("when me gets dropped\n  set my WasDropped to true"));
dropEngine2.SessionStarted();
dropEngine2.Tick(0);
Check(dropEngine2.IsEquipped(drPlayer, "Sword"), "Sword equipped before drop");
dropEngine2.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Q" });
dropEngine2.Tick(1);
Check(dropEngine2.Variables.GetLocal(drSword, "WasDropped").AsBoolean() == true,
    "`when me gets dropped` fires on the item's sheet with `me` = the item itself");
Check(!dropEngine2.IsEquipped(drPlayer, "Sword"), "after `drop`, nothing is equipped");

// equipping an item not in the bag does nothing
var noBagEngine = FreshEngine(new FakeWorld());
var nbPlayer = new FakeActor("Player");
var nbSword = new FakeActor("Sword");
noBagEngine.AddSheet(nbPlayer, parser.Parse("when me appears\n  equip Sword"));
noBagEngine.AddSheet(nbSword, parser.Parse("when me gets equipped\n  set my Equipped to true"));
noBagEngine.SessionStarted();
noBagEngine.Tick(0);
Check(noBagEngine.Variables.GetLocal(nbSword, "Equipped").Kind == ValueKind.None,
    "`equip Sword` with Sword not in the bag does nothing");

// ------------------------------------------------------------------
// Task 8 Part C — Acceptance: the D2 loop (pickup -> equip -> stats change -> damage flows)
// ------------------------------------------------------------------

// All three DESIGN.md §5 listings must parse as real .events files.
var swordEvents = parser.Parse(File.ReadAllText("Source/Game/GameFiles/test/Sword.events"));
Check(swordEvents.Events.Count == 2, "Sword.events parses (gets-equipped + gets-unequipped)");
var playerEvents = parser.Parse(File.ReadAllText("Source/Game/GameFiles/test/Player.events"));
Check(playerEvents.Events.Count > 0, "Player.events parses (movement + hurt + loot/equip/attack)");
var enemyEvents = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Enemy.events"));
Check(enemyEvents.Events.Count > 0, "Enemy.events parses (patrol/chase/attack + damage + death loot)");

// Pure end-to-end loop (spawn/touches/send-to-nearest need the Flax bridge; the
// stat-and-message parts of the loop are pure runtime and fully testable here).
var loopWorld = new FakeWorld();
var loopEngine = FreshEngine(loopWorld);
var lpPlayer = new FakeActor("Player");
var lpEnemy = new FakeActor("Enemy");
var lpSword = new FakeActor("Sword");
loopWorld.AddActor(lpPlayer);
loopWorld.AddActor(lpEnemy);
loopWorld.AddActor(lpSword);

// Player: pickup (bag), equip on `1`, attack on mouse (sends "damage" to Enemy),
// drop on `Q`. ("to nearest Enemy" needs the bridge's nearest-actor resolution;
// pure runtime matches exact ActorName, so the test uses "to Enemy".)
loopEngine.AddSheet(lpPlayer, parser.Parse(@"when game starts
  add Sword to my bag
when 1 is pressed
  equip Sword
when Q is pressed
  drop Sword
when Left Mouse is pressed
  send message " + "\"damage\"" + @" to Enemy"));
// Sword: equip gives wearer +5 damage
loopEngine.AddSheet(lpSword, parser.Parse(@"when me gets equipped
  add 5 to my damage
when me gets unequipped
  add -5 to my damage"));
// Enemy: takes other's damage; dies below 1 health
loopEngine.AddSheet(lpEnemy, parser.Parse(@"when game starts
  set my health to 10
when message " + "\"damage\"" + @" arrives
  set my LastDmg to other's damage
  set my health to my health - my LastDmg"));

loopEngine.SessionStarted();
loopEngine.Tick(0);

// Before equip: Player has Sword in bag, damage is 0
Check(loopEngine.BagHas(lpPlayer, "Sword"), "after pickup, Sword is in the bag");
Check(loopEngine.Variables.GetLocal(lpPlayer, "damage").Kind == ValueKind.None,
    "before equip, Player has no damage stat");
Check(loopEngine.Variables.GetLocal(lpEnemy, "health").AsNumber() == 10,
    "Enemy starts at full health (10)");

// Press 1 → equip Sword → wearer's damage becomes +5
loopEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "1" });
loopEngine.Tick(1);
Check(loopEngine.Variables.GetLocal(lpPlayer, "damage").AsNumber() == 5,
    "after `equip Sword`, wearer's damage = +5");
Check(!loopEngine.BagHas(lpPlayer, "Sword"), "equipping removes Sword from the bag");
Check(loopEngine.IsEquipped(lpPlayer, "Sword"), "Sword is equipped on Player");

// Mouse swing → send "damage" to Enemy → Enemy takes other's damage (+5)
loopEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Left Mouse" });
loopEngine.Tick(1);
Check(loopEngine.Variables.GetLocal(lpEnemy, "health").AsNumber() == 5,
    "after equipped swing, Enemy health = 10 - 5 = 5 (other's damage flows)");

// Swing again without equip change: still +5 damage
loopEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Left Mouse" });
loopEngine.Tick(2);
Check(loopEngine.Variables.GetLocal(lpEnemy, "health").AsNumber() == 0,
    "second equipped swing brings Enemy health to 0");

// Drop Sword → unequip → wearer's damage back to 0
loopEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Q" });
loopEngine.Tick(3);
Check(loopEngine.Variables.GetLocal(lpPlayer, "damage").AsNumber() == 0,
    "after `drop Sword`, wearer's damage returns to baseline (0)");
Check(!loopEngine.IsEquipped(lpPlayer, "Sword"), "after `drop`, Sword is no longer equipped");

// `my bag has Sword` condition reflects the bag (used as a gate, not a trigger)
var bagCondEngine = FreshEngine(new FakeWorld());
var bcPlayer = new FakeActor("Player");
var bcSword = new FakeActor("Sword");
bagCondEngine.AddSheet(bcPlayer, parser.Parse(@"when game starts
  add Sword to my bag
  my bag has Sword
  set my BagFlag to 1"));
bagCondEngine.SessionStarted();
bagCondEngine.Tick(0);
Check(bagCondEngine.Variables.GetLocal(bcPlayer, "BagFlag").AsNumber() == 1,
    "`my bag has Sword` condition fires when Sword is in the bag");

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

    // ------------------------------------------------------------------
    // Task 7 Part A: `goto` / `when me arrives at <thing>` (nav bridge)
    // ------------------------------------------------------------------

    var arrives = parser.Parse("when me appears\n  set Ready to 1\n\nwhen me arrives at Waypoint\n  set Arrived to 1\n");
    Check(arrives.Events.Count == 2, "`when me arrives at Waypoint` parses as an event");
    var arriveDesc = TriggerClassifier.Classify(arrives.Events[1]);
    Check(arriveDesc.Kind == TriggerKind.Arrives && arriveDesc.RequireName == "Waypoint",
        "`when me arrives at Waypoint` classifies to Arrives with RequireName \"Waypoint\"");

    var arriveWorld = new FakeWorld();
    var arriveSeeker = new FakeActor("Seeker");
    var arriveWaypoint = new FakeActor("Waypoint");
    arriveWorld.AddActor(arriveSeeker);
    arriveWorld.AddActor(arriveWaypoint);
    var arriveEngine = FreshEngine(arriveWorld);
    var arriveSheet = arriveEngine.AddSheet(arriveSeeker, parser.Parse("when me arrives at Waypoint\n  set Arrived to Arrived + 1"));
    arriveWorld.SetDistance(arriveSeeker, arriveWaypoint, 0.1);
    arriveEngine.Tick(0);
    Check(arriveEngine.Variables.GetGlobal("Arrived").AsNumber() == 1, "`when me arrives at <thing>` fires once on arrival");
    arriveEngine.Tick(1);
    Check(arriveEngine.Variables.GetGlobal("Arrived").AsNumber() == 1, "still in range does not refire (enter edge)");
    arriveWorld.SetDistance(arriveSeeker, arriveWaypoint, 99);
    arriveEngine.Tick(2);
    Check(arriveEngine.Variables.GetGlobal("Arrived").AsNumber() == 1, "leaving range arms arrival again");
    arriveWorld.SetDistance(arriveSeeker, arriveWaypoint, 0.1);
    arriveEngine.Tick(3);
    Check(arriveEngine.Variables.GetGlobal("Arrived").AsNumber() == 2, "coming back in range refires");

    var noArriveWorld = new FakeWorld();
    var noArriveSeeker = new FakeActor("Seeker");
    noArriveWorld.AddActor(noArriveSeeker);
    var noArriveEngine = FreshEngine(noArriveWorld);
    noArriveEngine.AddSheet(noArriveSeeker, parser.Parse("when me arrives at Nowhere\n  set Arrived to Arrived + 1"));
    noArriveEngine.Tick(0);
    Check(noArriveEngine.Variables.GetGlobal("Arrived").IsNone(), "an arrival target that does not exist never fires");

    // Test: arrival re-arms when goto same waypoint twice (fix for guard stopping after laps)
    var rearmWorld = new FakeWorld();
    var rearmSeeker = new FakeActor("Guard");
    var rearmWaypoint = new FakeActor("Waypoint");
    rearmWorld.AddActor(rearmSeeker);
    rearmWorld.AddActor(rearmWaypoint);
    rearmWorld.SetDistance(rearmSeeker, rearmWaypoint, 0.1); // already at waypoint (within ArrivalSlack)
    var rearmEngine = FreshEngine(rearmWorld);
    var rearmSheet = rearmEngine.AddSheet(rearmSeeker, parser.Parse("when me arrives at Waypoint\n  set Arrived to Arrived + 1"));
    rearmEngine.Tick(0);
    Check(rearmEngine.Variables.GetGlobal("Arrived").AsNumber() == 1, "initial arrival fires once");
    // Simulate what FlaxActions.Remember does: call ClearArrivalContact when new goto targets same position
    EventRuntime.ClearArrivalContact(rearmSeeker, rearmWaypoint);
    rearmEngine.Tick(1);
    Check(rearmEngine.Variables.GetGlobal("Arrived").AsNumber() == 2, "after ClearArrivalContact, arrival re-arms and fires again");


    // ------------------------------------------------------------------
    // Task 9 Part A: `when me is hovered` / `goto there` (point & click)
    // ------------------------------------------------------------------

    var hoveredProg = parser.Parse("when me is hovered\n  set Hint to 1\n");
    Check(hoveredProg.Events.Count == 1, "`when me is hovered` parses as an event");
    var hoverDesc = TriggerClassifier.Classify(hoveredProg.Events[0]);
    Check(hoverDesc.Kind == TriggerKind.Hovered && hoverDesc.RequireName.Length == 0,
        "`when me is hovered` classifies to Hovered with no require name");

    var hoverWorld = new FakeWorld();
    var hoverActor = new FakeActor("Hotspot");
    hoverWorld.AddActor(hoverActor);
    var hoverEngine = FreshEngine(hoverWorld);
    hoverEngine.AddSheet(hoverActor, parser.Parse("when me is hovered\n  add 1 to Hint\n"));
    hoverWorld.SetHovered(hoverActor, true);
    hoverEngine.Tick(0);
    Check(hoverEngine.Variables.GetGlobal("Hint").AsNumber() == 1, "`when me is hovered` fires while hovered");
    hoverEngine.Tick(1);
    Check(hoverEngine.Variables.GetGlobal("Hint").AsNumber() == 2, "`when me is hovered` is steady (fires each tick)");
    hoverWorld.SetHovered(hoverActor, false);
    hoverEngine.Tick(2);
    Check(hoverEngine.Variables.GetGlobal("Hint").AsNumber() == 2, "`when me is hovered` stops when unhovered");

    var thereProg = parser.Parse("when Ground is clicked\n  goto there\n\nwhen me arrives at there\n  set Arrived to 1\n");
    Check(thereProg.Events.Count == 2, "`goto there` / `when me arrives at there` parses as two events");
    var gotoStmt = thereProg.Events[0].Body[0].Statement as RawActionNode;
    Check(gotoStmt != null && gotoStmt.Words.Count >= 2 && gotoStmt.Words[1].Text == "there",
        "`goto there` parses with thing \"there\"");
    var arriveThereDesc = TriggerClassifier.Classify(thereProg.Events[1]);
    Check(arriveThereDesc.Kind == TriggerKind.Arrives && arriveThereDesc.RequireName == "there",
        "`when me arrives at there` classifies to Arrives with RequireName \"there\"");

    // Click routing: `when Ground is clicked` can live on the Player's sheet, while
    // `when me is clicked` fires only on the sheet attached to the clicked actor.
    var clickWorld = new FakeWorld();
    var clickPlayer = new FakeActor("Player");
    var clickGround = new FakeActor("Ground");
    var clickChest = new FakeActor("Chest");
    clickWorld.AddActor(clickPlayer);
    clickWorld.AddActor(clickGround);
    clickWorld.AddActor(clickChest);
    var clickEngine = FreshEngine(clickWorld);
    clickEngine.AddSheet(clickPlayer, parser.Parse("when Ground is clicked\n  add 1 to Walks\n"));
    clickEngine.AddSheet(clickGround, parser.Parse("when me is clicked\n  add 1 to GroundClicks\n"));
    clickEngine.AddSheet(clickChest, parser.Parse("when me is clicked\n  add 1 to ChestClicks\n"));
    clickEngine.RaiseClick(clickGround, TriggerKind.Clicked);
    clickEngine.Tick(0);
    Check(clickEngine.Variables.GetGlobal("Walks").AsNumber() == 1,
        "clicking Ground reaches `when Ground is clicked` on another sheet");
    Check(clickEngine.Variables.GetGlobal("GroundClicks").AsNumber() == 1,
        "`when me is clicked` fires on the clicked actor's own sheet");
    Check(clickEngine.Variables.GetGlobal("ChestClicks").IsNone(),
        "a click on Ground does not fire `when me is clicked` on an unrelated sheet");
    clickEngine.Tick(1);
    Check(clickEngine.Variables.GetGlobal("Walks").AsNumber() == 1,
        "a click is a one-shot push, not a steady trigger");

    clickEngine.RaiseClick(clickChest, TriggerKind.Clicked);
    clickEngine.Tick(2);
    Check(clickEngine.Variables.GetGlobal("ChestClicks").AsNumber() == 1,
        "clicking Chest fires its own `when me is clicked`");
    Check(clickEngine.Variables.GetGlobal("Walks").AsNumber() == 1,
        "clicking Chest does not fire `when Ground is clicked`");

    // ------------------------------------------------------------------
    // Task 7 Part B: `when me sees <thing> by <dist>` / `when me stops seeing <thing>`
    // ------------------------------------------------------------------

    var sees = parser.Parse("when me sees Player by 8\n  set Spotted to Spotted + 1\n");
    Check(sees.Events.Count == 1, "`when me sees Player by 8` parses as an event");
    var seesDesc = TriggerClassifier.Classify(sees.Events[0]);
    Check(seesDesc.Kind == TriggerKind.Sees && seesDesc.RequireName == "Player" && seesDesc.Distance == 8,
        "`when me sees Player by 8` classifies to Sees with RequireName \"Player\" and Distance 8");

    var stops = parser.Parse("when me stops seeing Player\n  set Lost to Lost + 1\n");
    Check(stops.Events.Count == 1, "`when me stops seeing Player` parses as an event");
    var stopsDesc = TriggerClassifier.Classify(stops.Events[0]);
    Check(stopsDesc.Kind == TriggerKind.StopsSeeing && stopsDesc.RequireName == "Player" && stopsDesc.Distance == -1,
        "`when me stops seeing Player` classifies to StopsSeeing with no distance (-1)");

    var stopsBy = parser.Parse("when me stops seeing Player by 8\n  set Lost to Lost + 1\n");
    var stopsByDesc = TriggerClassifier.Classify(stopsBy.Events[0]);
    Check(stopsByDesc.Kind == TriggerKind.StopsSeeing && stopsByDesc.Distance == 8,
        "`when me stops seeing Player by 8` keeps its own distance");

    var sightWorld = new FakeWorld();
    var sightSeeker = new FakeActor("Guard");
    var sightTarget = new FakeActor("Player");
    sightWorld.AddActor(sightSeeker);
    sightWorld.AddActor(sightTarget);
    sightWorld.SetDistance(sightSeeker, sightTarget, 2);
    var sightEngine = FreshEngine(sightWorld);
    sightEngine.AddSheet(sightSeeker, parser.Parse("when me sees Player by 8\n  set Spotted to Spotted + 1"));
    sightWorld.SetBlocking(sightSeeker, sightTarget, false);
    sightEngine.Tick(0);
    Check(sightEngine.Variables.GetGlobal("Spotted").AsNumber() == 1, "sight fires once when a target enters range with line of sight");
    sightEngine.Tick(1);
    Check(sightEngine.Variables.GetGlobal("Spotted").AsNumber() == 1, "still in sight does not refire (enter edge)");
    sightWorld.SetDistance(sightSeeker, sightTarget, 99);
    sightEngine.Tick(2);
    sightWorld.SetDistance(sightSeeker, sightTarget, 2);
    sightEngine.Tick(3);
    Check(sightEngine.Variables.GetGlobal("Spotted").AsNumber() == 2, "leaving sight and coming back refires");

    // Walls block sight.
    sightWorld.SetDistance(sightSeeker, sightTarget, 2);
    sightWorld.SetBlocking(sightSeeker, sightTarget, true);
    sightEngine.Tick(4);
    Check(sightEngine.Variables.GetGlobal("Spotted").AsNumber() == 2, "sight does not fire through a wall");
    sightWorld.SetBlocking(sightSeeker, sightTarget, false);
    sightEngine.Tick(5);
    Check(sightEngine.Variables.GetGlobal("Spotted").AsNumber() == 3, "sight refires once the wall is gone");

    // Falling edge: `stops seeing` fires once when sight ends.
    var lostWorld = new FakeWorld();
    var lostSeeker = new FakeActor("Guard");
    var lostTarget = new FakeActor("Player");
    lostWorld.AddActor(lostSeeker);
    lostWorld.AddActor(lostTarget);
    lostWorld.SetDistance(lostSeeker, lostTarget, 2);
    var lostEngine = FreshEngine(lostWorld);
    lostEngine.AddSheet(lostSeeker, parser.Parse("when me sees Player by 8\n  set Spotted to Spotted + 1\n\nwhen me stops seeing Player\n  set Lost to Lost + 1"));
    lostWorld.SetBlocking(lostSeeker, lostTarget, false);
    lostEngine.Tick(0);
    Check(lostEngine.Variables.GetGlobal("Spotted").AsNumber() == 1 && lostEngine.Variables.GetGlobal("Lost").IsNone(), "sight starts; nothing lost yet");
    lostWorld.SetBlocking(lostSeeker, lostTarget, true);
    lostEngine.Tick(1);
    Check(lostEngine.Variables.GetGlobal("Spotted").AsNumber() == 1 && lostEngine.Variables.GetGlobal("Lost").AsNumber() == 1, "`stops seeing` fires once when sight ends");
    lostEngine.Tick(2);
    Check(lostEngine.Variables.GetGlobal("Spotted").AsNumber() == 1 && lostEngine.Variables.GetGlobal("Lost").AsNumber() == 1, "still out of sight does not refire");
    lostWorld.SetBlocking(lostSeeker, lostTarget, false);
    lostEngine.Tick(3);
    Check(lostEngine.Variables.GetGlobal("Lost").AsNumber() == 1, "back in sight does not refire `stops seeing`");
    lostWorld.SetBlocking(lostSeeker, lostTarget, true);
    lostEngine.Tick(4);
    Check(lostEngine.Variables.GetGlobal("Lost").AsNumber() == 2, "losing sight again refires `stops seeing`");

    // `stops seeing` without `by <dist>` reuses the last `sees` range.
    var reuseWorld = new FakeWorld();
    var reuseSeeker = new FakeActor("Guard");
    var reuseTarget = new FakeActor("Player");
    reuseWorld.AddActor(reuseSeeker);
    reuseWorld.AddActor(reuseTarget);
    reuseWorld.SetDistance(reuseSeeker, reuseTarget, 6);
    var reuseEngine = FreshEngine(reuseWorld);
    reuseEngine.AddSheet(reuseSeeker, parser.Parse("when me sees Player by 8\n  set Spotted to Spotted + 1\n\nwhen me stops seeing Player\n  set Lost to Lost + 1"));
    reuseWorld.SetBlocking(reuseSeeker, reuseTarget, false);
    reuseEngine.Tick(0);
    Check(reuseEngine.Variables.GetGlobal("Spotted").AsNumber() == 1 && reuseEngine.Variables.GetGlobal("Lost").IsNone(), "sight established at range 6 within the configured 8");
    reuseWorld.SetDistance(reuseSeeker, reuseTarget, 10);
    reuseEngine.Tick(1);
    Check(reuseEngine.Variables.GetGlobal("Spotted").AsNumber() == 1 && reuseEngine.Variables.GetGlobal("Lost").AsNumber() == 1, "moving past 8 makes distance-less `stops seeing` fire (reuses range 8)");

    // ------------------------------------------------------------------
    // Task 7 Part B: `when me is near <thing> by <dist>` (near trigger)
    // ------------------------------------------------------------------

    var near = parser.Parse("when me is near Player by 1.5\n  set Nearing to Nearing + 1\n");
    Check(near.Events.Count == 1, "`when me is near Player by 1.5` parses as an event");
    var nearDesc = TriggerClassifier.Classify(near.Events[0]);
    Check(nearDesc.Kind == TriggerKind.Near && nearDesc.RequireName == "Player" && nearDesc.Distance == 1.5,
        "`when me is near Player by 1.5` classifies to Near with RequireName \"Player\" and Distance 1.5");

    var nearNoDist = parser.Parse("when me is near Player\n  set Nearing to Nearing + 1\n");
    var nearNoDistDesc = TriggerClassifier.Classify(nearNoDist.Events[0]);
    Check(nearNoDistDesc.Kind == TriggerKind.Near && nearNoDistDesc.Distance == -1,
        "`when me is near Player` classifies to Near with no distance (-1)");

    var nearTriggerWorld = new FakeWorld();
    var nearTriggerSeeker = new FakeActor("Guard");
    var nearTriggerTarget = new FakeActor("Player");
    nearTriggerWorld.AddActor(nearTriggerSeeker);
    nearTriggerWorld.AddActor(nearTriggerTarget);
    nearTriggerWorld.SetDistance(nearTriggerSeeker, nearTriggerTarget, 99); // out of range (> 1.5)
    var nearTriggerEngine = FreshEngine(nearTriggerWorld);
    nearTriggerEngine.AddSheet(nearTriggerSeeker, parser.Parse("when me is near Player by 1.5\n  set Nearing to Nearing + 1"));
    nearTriggerWorld.SetBlocking(nearTriggerSeeker, nearTriggerTarget, false);
    nearTriggerEngine.Tick(0);
    Check(nearTriggerEngine.Variables.GetGlobal("Nearing").IsNone(), "near does not fire when out of range");
    nearTriggerWorld.SetDistance(nearTriggerSeeker, nearTriggerTarget, 1); // enter range
    nearTriggerEngine.Tick(1);
    Check(nearTriggerEngine.Variables.GetGlobal("Nearing").AsNumber() == 1, "near fires once when a target enters range");
    nearTriggerEngine.Tick(2);
    Check(nearTriggerEngine.Variables.GetGlobal("Nearing").AsNumber() == 1, "still in range does not refire (enter edge)");
    nearTriggerWorld.SetDistance(nearTriggerSeeker, nearTriggerTarget, 99); // leave range
    nearTriggerEngine.Tick(3);
    Check(nearTriggerEngine.Variables.GetGlobal("Nearing").AsNumber() == 1, "leaving range does not refire");
    nearTriggerWorld.SetDistance(nearTriggerSeeker, nearTriggerTarget, 1); // re-enter range
    nearTriggerEngine.Tick(4);
    Check(nearTriggerEngine.Variables.GetGlobal("Nearing").AsNumber() == 2, "coming back into range refires near");

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

var backMove = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  move back speed 5"), null);
Check(backMove.Verb == ActionVerb.Move && backMove.Dir == "backward" && backMove.HasNumber && backMove.Number == 5,
    "`move back speed 5` (the DESIGN �5 dir list) runs like `backward`");

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
    "`look at Enemy` parses | V=" + lookSpec.Verb + " T=" + lookSpec.Thing + " LO=" + lookSpec.Leftover.Count);

var nearestLook = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  look at nearest Enemy"), null);
Check(nearestLook.Verb == ActionVerb.LookAt && nearestLook.Thing == "nearest Enemy",
    "`look at nearest Enemy` carries the qualifier through");

var randomGoto = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  goto random Waypoint"), null);
Check(randomGoto.Verb == ActionVerb.Goto && randomGoto.Thing == "random Waypoint",
    "`goto random Waypoint` carries the qualifier through");

var showTextSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  show text ""Game Over"""), null);
Check(showTextSpec.Verb == ActionVerb.ShowText && showTextSpec.Thing == "Game Over",
    "`show text \"Game Over\"` becomes the HUD text word");

var hideTextSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  hide text"), null);
Check(hideTextSpec.Verb == ActionVerb.HideText, "`hide text` becomes its own word");

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

var gotoSpeedSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  goto random Waypoint speed 40"), null);
Check(gotoSpeedSpec.Verb == ActionVerb.Goto && gotoSpeedSpec.Thing == "random Waypoint" &&
      gotoSpeedSpec.HasNumber && gotoSpeedSpec.Number == 40,
    "`goto random Waypoint speed 40` carries qualifier and speed");

var gotoDefaultSpeedSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  goto SpawnPoint"), null);
Check(gotoDefaultSpeedSpec.Verb == ActionVerb.Goto && gotoDefaultSpeedSpec.HasNumber &&
      gotoDefaultSpeedSpec.Number == 5,
    "`goto SpawnPoint` defaults to speed 5");

var gotoFacingSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  goto Player facing forward"), null);
Check(gotoFacingSpec.Verb == ActionVerb.Goto && gotoFacingSpec.Thing == "Player" &&
      gotoFacingSpec.FaceForward && gotoFacingSpec.Number == 5,
    "`goto Player facing forward` sets FaceForward and defaults speed");

var gotoFacingSpeedSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  goto random Waypoint speed 40 facing forward"), null);
Check(gotoFacingSpeedSpec.Verb == ActionVerb.Goto && gotoFacingSpeedSpec.Thing == "random Waypoint" &&
      gotoFacingSpeedSpec.HasNumber && gotoFacingSpeedSpec.Number == 40 && gotoFacingSpeedSpec.FaceForward,
    "`goto random Waypoint speed 40 facing forward` carries qualifier, speed, and FaceForward");

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

// Sound lookup keys ignore folder, .flax and the audio extension, case-insensitively.
Check(AssetNames.Key("sounds/hurt.wav.flax") == "hurt", "sound key strips folder and .flax and .wav");
Check(AssetNames.Key(@"Content\sounds\boom.flax") == "boom", "sound key accepts backslash paths");
Check(AssetNames.Key("Hurt.WAV") == "hurt", "sound key is case-insensitive");
Check(AssetNames.Key("shot") == "shot", "sound key leaves a plain name alone");
Check(AssetNames.Key("boom9") == "boom9", "sound key keeps digits");
Check(AssetNames.Key("") == "" && AssetNames.Key(null) == "", "sound key handles empty input");

// Verbose imported clip names answer to the action name at their end.
Check(AssetNames.Suffixes("bge dragon 2.07 animation walk").Contains("walk"),
    "a verbose clip path answers to its last word");
Check(AssetNames.Suffixes("bge dragon 2.07 animation idle pose").Contains("idle pose"),
    "multi-word clip names keep their tail");
Check(AssetNames.Suffixes("walk").Count() == 1, "a plain name yields only itself");
Check(AssetNames.Suffixes("").Count() == 0, "empty key yields nothing");

var animSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  play anim ""Attack"""), null);
Check(animSpec.Verb == ActionVerb.PlayAnim && animSpec.Thing == "Attack", "`play anim \"Attack\"` parses");

var stopSound = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  stop sound"), null);
Check(stopSound.Verb == ActionVerb.StopSound, "`stop sound` parses");

var stopAnim = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  stop anim"), null);
Check(stopAnim.Verb == ActionVerb.StopAnim, "`stop anim` parses");

var cameraSpec = ActionWords.Parse(RawWords(actionParser, @"when game starts
  camera follows me"), null);
Check(cameraSpec.Verb == ActionVerb.CameraFollow && cameraSpec.Thing == "me",
    "`camera follows me` parses to CameraFollow");

var cameraLooks = ActionWords.Parse(RawWords(actionParser, @"when game starts
  camera looks at Chest"), null);
Check(cameraLooks.Verb == ActionVerb.CameraLooksAt && cameraLooks.Thing == "Chest",
    "`camera looks at Chest` parses to CameraLooksAt");

var loopAnim = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  loop anim ""Walk"""), null);
Check(loopAnim.Verb == ActionVerb.LoopAnim && loopAnim.Thing == "Walk", "`loop anim \"Walk\"` parses");

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

var printSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  print ""W is held"""), null);
Check(printSpec.Verb == ActionVerb.Print && printSpec.Thing == "W is held",
    "`print \"...\"` parses with quoted string");

var printPlain = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  print W is held"), null);
Check(printPlain.Verb == ActionVerb.Print && printPlain.Thing == "W is held",
    "`print W is held` parses plain text (joins remaining words)");

var debugSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  debug Score changed"), null);
Check(debugSpec.Verb == ActionVerb.Print && debugSpec.Thing == "Score changed",
    "`debug` is an alias for `print`");

var bananaAction = ActionWords.Parse(LooseWords("banana", "time"), GrabHint);
Check(bananaAction.Verb == ActionVerb.Unknown && actionHint.Contains("banana"),
    "an odd action line gets the unknown-word explanation");

actionHint = "";
var oddMove = ActionWords.Parse(LooseWords("move", "sideways"), GrabHint);
Check(oddMove.Verb == ActionVerb.Unknown && actionHint.Contains("\u201Cmove\u201D"),
    "a bent `move` line explains the shape (move looks like this)");

// ---- Task 4 Part C: physics-native movement (opt-in, pure decisions) ----

Check(PhysicsMovement.ModeFor(false, true, false) == PhysicsMoveMode.Transform,
    "physics movement stays off by default even when the actor has a RigidBody");
Check(PhysicsMovement.ModeFor(true, false, false) == PhysicsMoveMode.Transform,
    "opting in without a RigidBody keeps the transform path");
Check(PhysicsMovement.ModeFor(true, true, false) == PhysicsMoveMode.Dynamic,
    "an opted-in dynamic body is driven by LinearVelocity");
Check(PhysicsMovement.ModeFor(true, true, true) == PhysicsMoveMode.Kinematic,
    "an opted-in kinematic body is driven by a swept AddMovement");

PhysicsMovement.DynamicVelocity(0, 0, -1, 80, 0, out double pvx, out double pvy, out double pvz);
Check(Math.Abs(pvx) < 1e-9 && Math.Abs(pvy) < 1e-9 && Math.Abs(pvz + 80) < 1e-9,
    "a forward move at speed 80 becomes velocity (0, 0, -80)");

PhysicsMovement.DynamicVelocity(1, 0, 0, 5, -9.8, out pvx, out pvy, out pvz);
Check(Math.Abs(pvx - 5) < 1e-9 && Math.Abs(pvy + 9.8) < 1e-9 && Math.Abs(pvz) < 1e-9,
    "a horizontal move keeps the body's vertical speed (gravity still falls)");

PhysicsMovement.DynamicVelocity(0, 1, 0, 5, -9.8, out pvx, out pvy, out pvz);
Check(Math.Abs(pvx) < 1e-9 && Math.Abs(pvy - 5) < 1e-9 && Math.Abs(pvz) < 1e-9,
    "a vertical move overrides the current vertical speed");

PhysicsMovement.BrakeVelocity(4, out pvx, out pvy, out pvz);
Check(Math.Abs(pvx) < 1e-9 && Math.Abs(pvy - 4) < 1e-9 && Math.Abs(pvz) < 1e-9,
    "`stop moving` brakes horizontal velocity but keeps a fall");

Check(PhysicsMovement.PushModeFor(false, false) == PhysicsPushMode.Transform &&
      PhysicsMovement.PushModeFor(true, true) == PhysicsPushMode.Transform,
    "`push` stays a transform nudge with no body or on a kinematic one");
Check(PhysicsMovement.PushModeFor(true, false) == PhysicsPushMode.Impulse,
    "`push` becomes a real impulse on a dynamic body");

var physicsEngine = FreshEngine(new FakeWorld());
var physicsSheet = physicsEngine.AddSheet(new FakeActor("Mover"),
    parser.Parse("when me appears\n  move forward speed 5"));
Check(!physicsSheet.UsePhysicsMovement, "a sheet starts with physics movement off (transform default)");
physicsSheet.UsePhysicsMovement = true;
physicsEngine.SwapSheetProgram(physicsSheet, parser.Parse("when me appears\n  move forward speed 9"));
Check(physicsSheet.UsePhysicsMovement, "the physics opt-in survives a hot-reload swap");

var flagWorld = new FakeWorld();
var flagEngine = FreshEngine(flagWorld);
var flagSheet = flagEngine.AddSheet(new FakeActor("Flagged"),
    parser.Parse("when me appears\n  move forward speed 5"));
flagSheet.UsePhysicsMovement = true;
SheetInstance seenSheet = null;
flagEngine.OnRawAction = (context, words) => seenSheet = context.Sheet;
flagEngine.Tick(0);
Check(ReferenceEquals(seenSheet, flagSheet),
    "actions can read their sheet's physics flag (context.Sheet is set while ticking)");

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

var nameCandidates = new List<string> { "Player", "Game", "Bullet", "Enemy" };
Check(EventFiles.SuggestFileName("Playr", nameCandidates) == "Player",
    "a typo file name suggests the closest real file (Playr → Player)");
Check(EventFiles.SuggestFileName("bullet", nameCandidates) == "Bullet",
    "case differences still suggest the file (bullet → Bullet)");
Check(EventFiles.SuggestFileName("Wombat", nameCandidates) == null,
    "a far-off file name gets no suggestion instead of a wrong one");

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

var pairEngine = FreshEngine(new FakeWorld());
var sharedProgram = reloadParser.Parse(@"when every 1 seconds
  add 1 to Ticks");
var enemyA = new FakeActor("Enemy_A");
var enemyB = new FakeActor("Enemy_B");
var sheetA = pairEngine.AddSheet(enemyA, sharedProgram, "Enemy");
var sheetB = pairEngine.AddSheet(enemyB, sharedProgram, "Enemy");
Check(sheetA.SourceName == "Enemy" && sheetA.ActorName == "Enemy_A" && sheetB.ActorName == "Enemy_B",
    "SourceName (which file) stays separate from ActorName (which object)");
pairEngine.Tick(1);
Check(pairEngine.Variables.GetGlobal("Ticks").AsNumber() == 2, "two actors sharing one .events file both tick");
Check(pairEngine.Variables.GetLocal(enemyA, "Ticks").Kind == ValueKind.None,
    "a shared sheet still keeps `my` variables per-object");

var usingEnemy = pairEngine.SheetsUsingSource("Enemy");
Check(usingEnemy.Count == 2 && usingEnemy.Contains(sheetA) && usingEnemy.Contains(sheetB),
    "SheetsUsingSource fans out to every actor on one file");

var gameSheet = pairEngine.AddSheet(new GlobalHost(), sharedProgram);
Check(gameSheet.SourceName == "Game" && pairEngine.SheetsUsingSource("Game").Count == 1,
    "the global host rides in as a sheet sourced from Game.events");
Check(pairEngine.FindSheetFor(new GlobalHost()) == null && pairEngine.FindSheetFor(gameSheet.Actor) == null,
    "FindSheetFor never returns the Game host (it is not a real actor)");

pairEngine.RemoveSheet(sheetB);
Check(pairEngine.FindSheetFor(enemyB) == null && pairEngine.FindSheetFor(enemyA) == sheetA,
    "RemoveSheet drops exactly that one sheet");
pairEngine.RemoveSheet(sheetB);
Check(pairEngine.SheetsUsingSource("Enemy").Count == 1, "removing an already-removed sheet changes nothing");

// ------------------------------------------------------------------
// Task 4 follow-up: real actor names, distance contacts, targeted messages
// ------------------------------------------------------------------

var shippedGameFiles = Path.Combine(Path.GetDirectoryName(FindUpward("DESIGN.md"))!,
    "EventManager", "EventManager", "Source", "Game", "GameFiles");
var shippedPrograms = EventFiles.LoadAll(shippedGameFiles);
bool shippedFilesParse = shippedPrograms.Count > 0;
foreach (EventFileResult shipped in shippedPrograms)
{
    if (!string.IsNullOrEmpty(shipped.Error))
    {
        shippedFilesParse = false;
        Console.WriteLine($"  SHIPPED FILE ERROR [{shipped.Name}]: {shipped.Error}");
    }
}
Check(shippedFilesParse, "every shipped GameFiles .events file parses (including `hit to other`)");

var decoratedWorld = new FakeWorld();
var decoratedBullet = new DecoratedActor("Bullet");
var decoratedEnemy = new DecoratedActor("Enemy");
var decoratedProbe = new FakeActor("Probe");
decoratedWorld.AddActor(decoratedBullet);
decoratedWorld.AddActor(decoratedEnemy);
decoratedWorld.AddActor(decoratedProbe);
decoratedWorld.SetDistance(decoratedProbe, decoratedEnemy, 1);
var decoratedEngine = FreshEngine(decoratedWorld);
var decoratedBulletSheet = decoratedEngine.AddSheet(decoratedBullet, parser.Parse(@"when me touches Enemy
  set Touched to Touched + ""T""
  send message ""hit"" to Enemy"));
var decoratedEnemySheet = decoratedEngine.AddSheet(decoratedEnemy, parser.Parse(@"when message ""hit"" arrives
  add -1 to my health"));
decoratedEngine.Variables.SetLocal(decoratedEnemy, "health", ValueBox.OfNumber(3));
var decoratedProbeSheet = decoratedEngine.AddSheet(decoratedProbe, parser.Parse(@"when every 1 seconds
  unless Enemy exists
  set Missing to Missing + 1
  me is near Enemy by 5
  set NearDecorated to NearDecorated + 1"));
decoratedEngine.PollTouches = true;
decoratedWorld.SetTouching(decoratedBullet, decoratedEnemy, true);
decoratedEngine.Tick(0);
decoratedEngine.Tick(1);
Check(decoratedBulletSheet.ActorName == "Bullet" && decoratedEnemySheet.ActorName == "Enemy",
    "sheet names come from World.NameOf, not a decorated ToString()");
Check(decoratedEngine.Variables.GetGlobal("Touched").AsText() == "T", "`me touches Enemy` matches the clean actor name");
Check(decoratedEngine.Variables.GetLocal(decoratedEnemy, "health").AsNumber() == 2,
    "`send message \"hit\" to Enemy` reaches the clean name after naming fix");
Check(decoratedEngine.Variables.GetGlobal("Missing").IsNone(), "`Enemy exists` finds a decorated actor by its real name");
Check(decoratedEngine.Variables.GetGlobal("NearDecorated").AsNumber() == 1, "`me is near Enemy` finds a decorated actor by its real name");

var enterWorld = new FakeWorld();
var enterA = new FakeActor("Toucher");
var enterB = new FakeActor("Target");
enterWorld.AddActor(enterA);
enterWorld.AddActor(enterB);
var enterEngine = FreshEngine(enterWorld);
enterEngine.AddSheet(enterA, parser.Parse("when me touches Target\n  set TouchCount to TouchCount + 1"));
enterEngine.PollTouches = true;
    enterWorld.SetTouching(enterA, enterB, true);
    enterEngine.Tick(0);
    enterEngine.Tick(1);
    Check(enterEngine.Variables.GetGlobal("TouchCount").AsNumber() == 1,
        "a distance touch fires once on enter, not every tick");
    enterWorld.SetTouching(enterA, enterB, false);
    enterEngine.Tick(2);
    enterWorld.SetTouching(enterA, enterB, true);
    enterEngine.Tick(3);
    Check(enterEngine.Variables.GetGlobal("TouchCount").AsNumber() == 2,
        "a distance touch re-fires after separation and re-entry");

var pollOverlapWorld = new FakeWorld();
var pollOverlapA = new FakeActor("Prober");
var pollOverlapB = new FakeActor("Enemy");
pollOverlapWorld.AddActor(pollOverlapA);
pollOverlapWorld.AddActor(pollOverlapB);
pollOverlapWorld.SetTouching(pollOverlapA, pollOverlapB, true);
var pollOverlapEngine = FreshEngine(pollOverlapWorld);
pollOverlapEngine.PollTouches = false;
pollOverlapEngine.PollOverlaps = true;
pollOverlapEngine.AddSheet(pollOverlapA, parser.Parse("when me overlaps Enemy\n  add 1 to OverlapPolls"));
pollOverlapEngine.Tick(0);
pollOverlapEngine.Tick(1);
Check(pollOverlapEngine.Variables.GetGlobal("OverlapPolls").AsNumber() == 1,
    "`overlaps` polls even when touches polling is disabled (colliders mode)");

var noOverlapWorld = new FakeWorld();
var noOverlapA = new FakeActor("Prober");
var noOverlapB = new FakeActor("Enemy");
noOverlapWorld.AddActor(noOverlapA);
noOverlapWorld.AddActor(noOverlapB);
noOverlapWorld.SetTouching(noOverlapA, noOverlapB, true);
var noOverlapEngine = FreshEngine(noOverlapWorld);
noOverlapEngine.PollOverlaps = false;
noOverlapEngine.AddSheet(noOverlapA, parser.Parse("when me overlaps Enemy\n  add 1 to OverlapOff"));
noOverlapEngine.Tick(0);
Check(noOverlapEngine.Variables.GetGlobal("OverlapOff").IsNone(),
    "`overlaps` polling can be switched off explicitly");

var dedupeWorld = new FakeWorld();
var dedupeA = new FakeActor("Bullet");
var dedupeB = new FakeActor("Enemy");
dedupeWorld.AddActor(dedupeA);
dedupeWorld.AddActor(dedupeB);
dedupeWorld.SetTouching(dedupeA, dedupeB, true);
var dedupeEngine = FreshEngine(dedupeWorld);
dedupeEngine.PollTouches = true;
dedupeEngine.AddSheet(dedupeA, parser.Parse("when me touches Enemy\n  add 1 to Dedupe"));
dedupeEngine.PushContact(TriggerKind.Touches, dedupeA, dedupeB);
dedupeEngine.Tick(0);
dedupeEngine.Tick(1);
Check(dedupeEngine.Variables.GetGlobal("Dedupe").AsNumber() == 1,
    "Both mode: a collider push followed by distance polling fires one event, not two");

var dedupeReverseWorld = new FakeWorld();
var dedupeReverseA = new FakeActor("Bullet");
var dedupeReverseB = new FakeActor("Enemy");
dedupeReverseWorld.AddActor(dedupeReverseA);
dedupeReverseWorld.AddActor(dedupeReverseB);
dedupeReverseWorld.SetTouching(dedupeReverseA, dedupeReverseB, true);
var dedupeReverseEngine = FreshEngine(dedupeReverseWorld);
dedupeReverseEngine.PollTouches = true;
dedupeReverseEngine.AddSheet(dedupeReverseA, parser.Parse("when me touches Enemy\n  add 1 to DedupeReverse"));
dedupeReverseEngine.Tick(0); // distance poll fires first
dedupeReverseEngine.PushContact(TriggerKind.Touches, dedupeReverseA, dedupeReverseB); // collider echo next tick
dedupeReverseEngine.Tick(1);
Check(dedupeReverseEngine.Variables.GetGlobal("DedupeReverse").AsNumber() == 1,
    "Both mode: a distance poll followed by a collider push fires one event, not two");

var targetedWorld = new FakeWorld();
var targetedBullet = new FakeActor("Bullet");
var targetedEnemyA = new FakeActor("Enemy");
var targetedEnemyB = new FakeActor("Enemy");
targetedWorld.AddActor(targetedBullet);
targetedWorld.AddActor(targetedEnemyA);
targetedWorld.AddActor(targetedEnemyB);
targetedWorld.SetTouching(targetedBullet, targetedEnemyA, true);
var targetedEngine = FreshEngine(targetedWorld);
var hurtProgram = parser.Parse(@"when message ""hit"" arrives
  add -1 to my health");
var targetedSheetA = targetedEngine.AddSheet(targetedEnemyA, hurtProgram);
var targetedSheetB = targetedEngine.AddSheet(targetedEnemyB, hurtProgram);
targetedEngine.Variables.SetLocal(targetedEnemyA, "health", ValueBox.OfNumber(3));
targetedEngine.Variables.SetLocal(targetedEnemyB, "health", ValueBox.OfNumber(3));
targetedEngine.AddSheet(targetedBullet, parser.Parse(@"when me touches Enemy
  send message ""hit"" to other
  destroy me"));
targetedEngine.PollTouches = true;
targetedEngine.Tick(0);
targetedEngine.Tick(1);
Check(targetedEngine.Variables.GetLocal(targetedEnemyA, "health").AsNumber() == 2 &&
      targetedEngine.Variables.GetLocal(targetedEnemyB, "health").AsNumber() == 3,
    "`send message \"hit\" to other` damages only the touched enemy");

var broadcastEngine = FreshEngine(new FakeWorld());
var broadcastA = new FakeActor("Enemy");
var broadcastB = new FakeActor("Enemy");
var broadcastSheetA = broadcastEngine.AddSheet(broadcastA, hurtProgram);
var broadcastSheetB = broadcastEngine.AddSheet(broadcastB, hurtProgram);
broadcastEngine.Variables.SetLocal(broadcastA, "health", ValueBox.OfNumber(3));
broadcastEngine.Variables.SetLocal(broadcastB, "health", ValueBox.OfNumber(3));
broadcastEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "hit" }, "");
broadcastEngine.Tick(0);
Check(broadcastEngine.Variables.GetLocal(broadcastA, "health").AsNumber() == 2 &&
      broadcastEngine.Variables.GetLocal(broadcastB, "health").AsNumber() == 2,
    "a message with no target still broadcasts to every matching sheet");

var wildcardWorld = new FakeWorld();
var wildcardMe = new FakeActor("Me");
var wildcardOther = new FakeActor("Anything");
wildcardWorld.AddActor(wildcardMe);
wildcardWorld.AddActor(wildcardOther);
wildcardWorld.SetTouching(wildcardMe, wildcardOther, true);
var wildcardEngine = FreshEngine(wildcardWorld);
wildcardEngine.PollTouches = true;
wildcardEngine.AddSheet(wildcardMe, parser.Parse("when other touches me\n  add 1 to Wildcard"));
wildcardEngine.Tick(0);
Check(wildcardEngine.Variables.GetGlobal("Wildcard").AsNumber() == 1,
    "`when other touches me` matches any toucher by distance");

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

/// <summary>Mimics Flax's Actor.ToString() shape: the real name plus type/id decoration.</summary>
class DecoratedActor
{
    public DecoratedActor(string name)
    {
        Name = name;
    }

    public string Name;

    public override string ToString()
    {
        return $"{Name} (FlaxEngine.StaticModel; {Guid.NewGuid():N})";
    }
}

class FakeWorld : IEventWorld
{
    private readonly HashSet<string> held = new HashSet<string>();
    private readonly Dictionary<(object, object), double> distances = new Dictionary<(object, object), double>();
    private readonly HashSet<(object, string)> tags = new HashSet<(object, string)>();
    private readonly List<object> actors = new List<object>();
    private readonly HashSet<(object, object)> touching = new HashSet<(object, object)>();
    private readonly HashSet<object> hovered = new HashSet<object>();

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

    public void AddActor(object actor)
    {
        if (!actors.Contains(actor))
            actors.Add(actor);
    }

    public void SetTouching(object a, object b, bool on)
    {
        if (on)
            touching.Add((a, b));
        else
            touching.Remove((a, b));
    }

    public void SetHovered(object actor, bool on)
    {
        if (on)
            hovered.Add(actor);
        else
            hovered.Remove(actor);
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

    public string NameOf(object actor)
    {
        if (actor is FakeActor fake)
            return fake.Name;
        if (actor is DecoratedActor decorated)
            return decorated.Name;
        return actor == null ? "" : actor.ToString();
    }

    public bool IsTouching(object a, object b)
    {
        return touching.Contains((a, b)) || touching.Contains((b, a));
    }

    public bool IsHovered(object actor)
    {
        return hovered.Contains(actor);
    }

    public object FindActor(string name)
    {
        foreach (var actor in actors)
            if (NameOf(actor) == name)
                return actor;
        return null;
    }

    public IReadOnlyList<object> Actors()
    {
        return actors;
    }

    /// <summary>Blocks (walls) or unblocks the straight line between two actors, both directions.</summary>
    public void SetBlocking(object a, object b, bool on)
    {
        if (on)
            blocking.Add((a, b));
        else
            blocking.Remove((a, b));
    }

    public bool HasLineOfSight(object from, object to)
    {
        return !blocking.Contains((from, to)) && !blocking.Contains((to, from));
    }

    private readonly HashSet<(object, object)> blocking = new HashSet<(object, object)>();
}

