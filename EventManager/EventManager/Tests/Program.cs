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

// ------------------------------------------------------------------
// Task 14 Part A: Particle System words
// ------------------------------------------------------------------

var playParticle = parser.Parse("when me appears\n  play particle \"smoke\"\n");
var playParticleSpec = ActionWords.Parse(((RawActionNode)playParticle.Events[0].Body[0].Statement!).Words, _ => { });
Check(playParticleSpec.Verb == ActionVerb.PlayParticle && playParticleSpec.Thing == "smoke",
    "`play particle \"smoke\"` parses to PlayParticle with thing \"smoke\"");

var stopParticle = parser.Parse("when me appears\n  stop particle \"smoke\"\n");
var stopParticleSpec = ActionWords.Parse(((RawActionNode)stopParticle.Events[0].Body[0].Statement!).Words, _ => { });
Check(stopParticleSpec.Verb == ActionVerb.StopParticle && stopParticleSpec.Thing == "smoke",
    "`stop particle \"smoke\"` parses to StopParticle with thing \"smoke\"");

var playBare = parser.Parse("when me appears\n  play particle\n");
var playBareSpec = ActionWords.Parse(((RawActionNode)playBare.Events[0].Body[0].Statement!).Words, _ => { });
Check(playBareSpec.Verb == ActionVerb.Unknown, "`play particle` without a quoted name is unknown");

var stopBare = parser.Parse("when me appears\n  stop particle\n");
var stopBareSpec = ActionWords.Parse(((RawActionNode)stopBare.Events[0].Body[0].Statement!).Words, _ => { });
Check(stopBareSpec.Verb == ActionVerb.Unknown, "`stop particle` without a quoted name is unknown");

var particleEnd = parser.Parse("when particle \"smoke\" ends\n  set Done to 1\n");
var particleEndDesc = TriggerClassifier.Classify(particleEnd.Events[0]);
Check(particleEndDesc.Kind == TriggerKind.ParticleEnd && particleEndDesc.RequireName == "smoke",
    "`when particle \"smoke\" ends` classifies to ParticleEnd with RequireName \"smoke\"");

// runtime: the end push fires the event (name match is case-insensitive)
var peEngine = FreshEngine(new FakeWorld());
var peActor = new FakeActor("Cannon");
peEngine.AddSheet(peActor, particleEnd);
peEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.ParticleEnd, Name = "Smoke" });
peEngine.Tick(0);
Check(peEngine.Variables.GetGlobal("Done").AsNumber() == 1, "`when particle \"smoke\" ends` fires on a case-insensitive name match");

// condition: parses and evaluates through the world
var cond = parser.Parse("when me appears\n  unless particle \"smoke\" is playing\n  set Playing to 0\n");
Check(cond.Events[0].Body[0].IsCondition, "`particle \"smoke\" is playing` classifies as a condition");
var particleCondTokens = new List<Token>
{
    new Token { Type = TokenType.Word, Text = "particle" },
    new Token { Type = TokenType.Str, Text = "smoke" },
    new Token { Type = TokenType.Word, Text = "is" },
    new Token { Type = TokenType.Word, Text = "playing" },
};
var particleCondNode = Conditions.Parse(particleCondTokens);
Check(particleCondNode.Kind == ConditionKind.ParticlePlaying && particleCondNode.Thing == "smoke",
    "`particle \"smoke\" is playing` parses to ParticlePlaying with thing \"smoke\"");

var playingWorld = new FakeWorld();
var cannon = new FakeActor("Cannon");
playingWorld.SetParticlePlaying(cannon, "smoke", true);
var particlePlayingCtx = new EventContext { Variables = new EventVariables(), World = playingWorld, Me = cannon, Runtime = peEngine };
Check(Conditions.Evaluate(particleCondNode, particlePlayingCtx) == true, "`particle \"smoke\" is playing` is true while the world plays it");
playingWorld.SetParticlePlaying(cannon, "smoke", false);
Check(Conditions.Evaluate(particleCondNode, particlePlayingCtx) == false, "`particle \"smoke\" is playing` is false once it stops");

// AssetNames reuse: particles resolve like sounds — folder, .flax and case ignored
Check(AssetNames.Key("effects/explosion.flax") == "explosion", "AssetNames.Key strips folder and .flax for a particle");
Check(AssetNames.Key("EFFECTS/Explosion.FLAX") == "explosion", "AssetNames.Key is case-insensitive for a particle");
Check(AssetNames.Key("smoke") == "smoke", "AssetNames.Key passes a plain particle name through");
Check(AssetNames.Key("") == "", "AssetNames.Key of an empty name is empty");

// ------------------------------------------------------------------
// Task 14 Part B: Light words
// ------------------------------------------------------------------

var li = parser.Parse("when me appears\n  set light intensity to 2\n");
var liSpec = ActionWords.Parse(((RawActionNode)li.Events[0].Body[0].Statement!).Words, _ => { });
Check(liSpec.Verb == ActionVerb.SetLightIntensity && liSpec.HasNumber && liSpec.Number == 2,
    "`set light intensity to 2` parses to SetLightIntensity with Number 2");

var liVar = parser.Parse("when me appears\n  set light intensity to Score\n");
var liVarSpec = ActionWords.Parse(((RawActionNode)liVar.Events[0].Body[0].Statement!).Words, _ => { });
Check(liVarSpec.Verb == ActionVerb.SetLightIntensity && liVarSpec.AmountNode is VariableValue,
    "`set light intensity to Score` parses to SetLightIntensity with a variable amount");

var lc = parser.Parse("when me appears\n  set light color to \"1 0 0\"\n");
var lcSpec = ActionWords.Parse(((RawActionNode)lc.Events[0].Body[0].Statement!).Words, _ => { });
Check(lcSpec.Verb == ActionVerb.SetLightColor && lcSpec.Thing == "1 0 0",
    "`set light color to \"1 0 0\"` parses to SetLightColor with Thing \"1 0 0\"");

var lc4 = parser.Parse("when me appears\n  set light color to \"0.5 0.25 1 0.5\"\n");
var lc4Spec = ActionWords.Parse(((RawActionNode)lc4.Events[0].Body[0].Statement!).Words, _ => { });
Check(lc4Spec.Verb == ActionVerb.SetLightColor && lc4Spec.Thing == "0.5 0.25 1 0.5",
    "`set light color to \"0.5 0.25 1 0.5\"` parses to SetLightColor with alpha");

var lr = parser.Parse("when me appears\n  set light range to 10\n");
var lrSpec = ActionWords.Parse(((RawActionNode)lr.Events[0].Body[0].Statement!).Words, _ => { });
Check(lrSpec.Verb == ActionVerb.SetLightRange && lrSpec.HasNumber && lrSpec.Number == 10,
    "`set light range to 10` parses to SetLightRange with Number 10");

var en = parser.Parse("when me appears\n  enable light\n");
var enSpec = ActionWords.Parse(((RawActionNode)en.Events[0].Body[0].Statement!).Words, _ => { });
Check(enSpec.Verb == ActionVerb.EnableLight, "`enable light` parses to EnableLight");

var dis = parser.Parse("when me appears\n  disable light\n");
var disSpec = ActionWords.Parse(((RawActionNode)dis.Events[0].Body[0].Statement!).Words, _ => { });
Check(disSpec.Verb == ActionVerb.DisableLight, "`disable light` parses to DisableLight");

var tg = parser.Parse("when me appears\n  toggle light\n");
var tgSpec = ActionWords.Parse(((RawActionNode)tg.Events[0].Body[0].Statement!).Words, _ => { });
Check(tgSpec.Verb == ActionVerb.ToggleLight, "`toggle light` parses to ToggleLight");

var liBare = parser.Parse("when me appears\n  set light intensity\n");
var liBareSpec = ActionWords.Parse(((RawActionNode)liBare.Events[0].Body[0].Statement!).Words, _ => { });
Check(liBareSpec.Verb == ActionVerb.Unknown, "`set light intensity` without a value is unknown");

var enBare = parser.Parse("when me appears\n  enable\n");
var enBareSpec = ActionWords.Parse(((RawActionNode)enBare.Events[0].Body[0].Statement!).Words, _ => { });
Check(enBareSpec.Verb == ActionVerb.Unknown, "`enable` without `light` is unknown");

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

    // ------------------------------------------------------------------
    // Task 15 Part A: UI button & image support
    // ------------------------------------------------------------------

    // Trigger classification.
    var dblProg = parser.Parse("when PlayButton is double-clicked\n  set Clicked2 to 1\n");
    Check(dblProg.Events.Count == 1, "`when PlayButton is double-clicked` parses as an event");
    var dblDesc = TriggerClassifier.Classify(dblProg.Events[0]);
    Check(dblDesc.Kind == TriggerKind.DoubleClicked && dblDesc.RequireName == "PlayButton",
        "`when PlayButton is double-clicked` classifies to DoubleClicked");

    var rightProg = parser.Parse("when RestartButton is right-clicked\n  set ClickedR to 1\n");
    var rightDesc = TriggerClassifier.Classify(rightProg.Events[0]);
    Check(rightDesc.Kind == TriggerKind.RightClicked && rightDesc.RequireName == "RestartButton",
        "`when RestartButton is right-clicked` classifies to RightClicked");

    var pressProg = parser.Parse("when RestartButton is pressed\n  set Pressed to 1\n");
    var pressDesc = TriggerClassifier.Classify(pressProg.Events[0]);
    Check(pressDesc.Kind == TriggerKind.ButtonPressed && pressDesc.RequireName == "RestartButton",
        "`when RestartButton is pressed` classifies to ButtonPressed");

    var releaseProg = parser.Parse("when RestartButton is released\n  set Released to 1\n");
    var releaseDesc = TriggerClassifier.Classify(releaseProg.Events[0]);
    Check(releaseDesc.Kind == TriggerKind.ButtonReleased && releaseDesc.RequireName == "RestartButton",
        "`when RestartButton is released` classifies to ButtonReleased");

    // `when W is pressed` is still a key press, not a button press.
    var keyPressProg = parser.Parse("when W is pressed\n  set K to 1\n");
    var keyPressDesc = TriggerClassifier.Classify(keyPressProg.Events[0]);
    Check(keyPressDesc.Kind == TriggerKind.KeyPressed, "`when W is pressed` stays a key press");

    // Condition parsing.
    var pressCondProg = parser.Parse(@"when every 1 seconds
  RestartButton is pressed
  set PressGate to 1
  RestartButton is released
  set ReleasedGate to 1
  RestartButton is disabled
  set DisabledGate to 1");
    Check(pressCondProg.Events.Count == 1, "`is pressed`/`released`/`disabled` conditions parse in one event");
    var pressCond = Conditions.Parse(pressCondProg.Events[0].Body[0].Words);
    Check(pressCond.Kind == ConditionKind.ButtonPressed, "`RestartButton is pressed` parses to ButtonPressed");
    var releasedCond = Conditions.Parse(pressCondProg.Events[0].Body[2].Words);
    Check(releasedCond.Kind == ConditionKind.ButtonReleased, "`RestartButton is released` parses to ButtonReleased");
    var disabledCond = Conditions.Parse(pressCondProg.Events[0].Body[4].Words);
    Check(disabledCond.Kind == ConditionKind.ButtonDisabled, "`RestartButton is disabled` parses to ButtonDisabled");

    // Action parsing.
    var setEnabledProg = parser.Parse("when W is pressed\n  set button RestartButton enabled\n");
    var setEnabledStmt = setEnabledProg.Events[0].Body[0].Statement as SetButtonEnabledNode;
    Check(setEnabledStmt != null && setEnabledStmt.Thing == "RestartButton" && setEnabledStmt.Enabled,
        "`set button RestartButton enabled` builds a SetButtonEnabledNode (enabled)");

    var setDisabledProg = parser.Parse("when W is pressed\n  set button RestartButton disabled\n");
    var setDisabledStmt = setDisabledProg.Events[0].Body[0].Statement as SetButtonEnabledNode;
    Check(setDisabledStmt != null && setDisabledStmt.Thing == "RestartButton" && !setDisabledStmt.Enabled,
        "`set button RestartButton disabled` builds a SetButtonEnabledNode (disabled)");

    var setTextProg = parser.Parse("when W is pressed\n  set button RestartButton text to \"Go\"\n");
    var setTextStmt = setTextProg.Events[0].Body[0].Statement as SetTextNode;
    Check(setTextStmt != null && setTextStmt.Target == "RestartButton" &&
          setTextStmt.Value is TextValue tv && tv.Text == "Go",
        "`set button RestartButton text to \"Go\"` builds a SetTextNode");

    var setImageProg = parser.Parse("when W is pressed\n  set image of HeartIcon to \"heart_full\"\n");
    var setImageStmt = setImageProg.Events[0].Body[0].Statement as SetImageBrushNode;
    Check(setImageStmt != null && setImageStmt.Thing == "HeartIcon" && setImageStmt.Sprite == "heart_full",
        "`set image of HeartIcon to \"heart_full\"` builds a SetImageBrushNode");

    var setColorProg = parser.Parse("when W is pressed\n  set color of HeartIcon to \"1 0 0\"\n");
    var setColorStmt = setColorProg.Events[0].Body[0].Statement as SetControlColorNode;
    Check(setColorStmt != null && setColorStmt.Thing == "HeartIcon" && setColorStmt.Color == "1 0 0",
        "`set color of HeartIcon to \"1 0 0\"` builds a SetControlColorNode");

    // Runtime: press/release edge triggers and disabled condition.
    var buttonProg = parser.Parse(@"when me is pressed
  set PressCount to PressCount + 1
when me is released
  set ReleaseCount to ReleaseCount + 1");
    var buttonWorld = new FakeWorld();
    var buttonActor = new FakeActor("RestartButton");
    buttonWorld.AddActor(buttonActor);
    var buttonEngine = FreshEngine(buttonWorld);
    buttonEngine.AddSheet(buttonActor, buttonProg);
    buttonEngine.Tick(0);
    Check(buttonEngine.Variables.GetGlobal("PressCount").IsNone() &&
          buttonEngine.Variables.GetGlobal("ReleaseCount").IsNone(),
        "button triggers do not fire before any press");
    buttonEngine.RaisePushAll(new PushSignal
    {
        Kind = TriggerKind.ButtonPressed,
        Name = buttonActor.Name,
        Other = buttonActor,
    });
    buttonEngine.Tick(1);
    Check(buttonEngine.Variables.GetGlobal("PressCount").AsNumber() == 1, "`when me is pressed` fires on the press edge");
    buttonEngine.Tick(2);
    Check(buttonEngine.Variables.GetGlobal("PressCount").AsNumber() == 1, "`when me is pressed` does not refire while held");
    buttonEngine.RaisePushAll(new PushSignal
    {
        Kind = TriggerKind.ButtonReleased,
        Name = buttonActor.Name,
        Other = buttonActor,
    });
    buttonEngine.Tick(3);
    Check(buttonEngine.Variables.GetGlobal("ReleaseCount").AsNumber() == 1, "`when me is released` fires on the release edge");

    // Condition: `me is disabled`.
    var disabledProg = parser.Parse(@"when every 1 seconds
  me is disabled
  set DisabledGate to 1");
    var disabledWorld = new FakeWorld();
    var disabledActor = new FakeActor("RestartButton");
    disabledWorld.AddActor(disabledActor);
    var disabledEngine = FreshEngine(disabledWorld);
    disabledEngine.AddSheet(disabledActor, disabledProg);
    disabledEngine.Tick(0);
    Check(disabledEngine.Variables.GetGlobal("DisabledGate").IsNone(),
        "`me is disabled` is false while enabled");
    disabledWorld.SetButtonDisabled(disabledActor, true);
    disabledEngine.Tick(1);
    Check(disabledEngine.Variables.GetGlobal("DisabledGate").AsNumber() == 1, "`me is disabled` fires while disabled");


    // ------------------------------------------------------------------
    // Task 15 Part B: ProgressBar & Slider
    // ------------------------------------------------------------------

    // Parser nodes: `set progress of <thing> to <value>` / `set slider <thing> to <value>`.
    var progressSpec = parser.Parse("when W is pressed\n  set progress of HealthBar to 0.5\n");
    var progressStmt = progressSpec.Events[0].Body[0].Statement as SetProgressNode;
    Check(progressStmt != null && progressStmt.Thing == "HealthBar" &&
          progressStmt.Value is NumberValue progNum && progNum.Value == 0.5,
        "`set progress of HealthBar to 0.5` builds a SetProgressNode");

    var sliderSpec = parser.Parse("when W is pressed\n  set slider HealthBar to 50\n");
    var sliderStmt = sliderSpec.Events[0].Body[0].Statement as SetSliderNode;
    Check(sliderStmt != null && sliderStmt.Thing == "HealthBar" &&
          sliderStmt.Value is NumberValue sliderNum && sliderNum.Value == 50,
        "`set slider HealthBar to 50` builds a SetSliderNode");

    var progressVarSpec = parser.Parse("when W is pressed\n  set progress of HealthBar to Score\n");
    var progressVarStmt = progressVarSpec.Events[0].Body[0].Statement as SetProgressNode;
    Check(progressVarStmt != null && progressVarStmt.Value is VariableValue progVal && progVal.Name == "Score",
        "`set progress of HealthBar to Score` reads a variable");

    ExpectParseError("when W is pressed\n  set progress of HealthBar\n", "variable",
        "`set progress of` without a value is a friendly error");
    ExpectParseError("when W is pressed\n  set slider HealthBar\n", "variable",
        "`set slider` without a value is a friendly error");

    // Condition parsing.
    var progressCondTokens = new List<Token>
    {
        new Token { Type = TokenType.Word, Text = "progress" },
        new Token { Type = TokenType.Word, Text = "of" },
        new Token { Type = TokenType.Word, Text = "HealthBar" },
        new Token { Type = TokenType.Word, Text = "is" },
        new Token { Type = TokenType.Number, Text = "1", Number = 1 },
    };
    var progressCond = Conditions.Parse(progressCondTokens);
    Check(progressCond.Kind == ConditionKind.ProgressIs && progressCond.Thing == "HealthBar" &&
          progressCond.Right is NumberValue pcn && pcn.Value == 1,
        "`progress of HealthBar is 1` parses to ProgressIs with thing \"HealthBar\"");

    var sliderCondTokens = new List<Token>
    {
        new Token { Type = TokenType.Word, Text = "slider" },
        new Token { Type = TokenType.Word, Text = "HealthBar" },
        new Token { Type = TokenType.Word, Text = "is" },
        new Token { Type = TokenType.Number, Text = "50", Number = 50 },
    };
    var sliderCond = Conditions.Parse(sliderCondTokens);
    Check(sliderCond.Kind == ConditionKind.SliderIs && sliderCond.Thing == "HealthBar" &&
          sliderCond.Right is NumberValue scn && scn.Value == 50,
        "`slider HealthBar is 50` parses to SliderIs with thing \"HealthBar\"");

    // Condition evaluation: progress reads the world value (0-1); 0-100 input normalizes.
    var pbA = new FakeActor("HealthBar");
    var worldA = new FakeWorld();
    worldA.AddActor(pbA);
    worldA.SetProgress(pbA, 0.5);
    var engineA = FreshEngine(worldA);
    engineA.AddSheet(pbA, parser.Parse(@"when me appears
  progress of HealthBar is 0.5
  set Half to 1
  progress of HealthBar is 1
  set Full to 1"));
    engineA.Tick(0);
    Check(engineA.Variables.GetGlobal("Half").AsNumber() == 1 &&
          engineA.Variables.GetGlobal("Full").Kind == ValueKind.None,
        "`progress of HealthBar is 0.5` matches a half bar; `is 1` does not");

    var pbB = new FakeActor("HealthBar");
    var worldB = new FakeWorld();
    worldB.AddActor(pbB);
    worldB.SetProgress(pbB, 1);
    var engineB = FreshEngine(worldB);
    engineB.AddSheet(pbB, parser.Parse("when me appears\n  progress of HealthBar is 1\n  set Full to 1\n"));
    engineB.Tick(0);
    Check(engineB.Variables.GetGlobal("Full").AsNumber() == 1, "`progress of HealthBar is 1` matches a full bar");

    var pbC = new FakeActor("HealthBar");
    var worldC = new FakeWorld();
    worldC.AddActor(pbC);
    worldC.SetProgress(pbC, 50);
    var engineC = FreshEngine(worldC);
    engineC.AddSheet(pbC, parser.Parse("when me appears\n  progress of HealthBar is 50\n  set Half50 to 1\n"));
    engineC.Tick(0);
    Check(engineC.Variables.GetGlobal("Half50").AsNumber() == 1,
        "`progress of HealthBar is 50` matches a bar written as 50 (0-100 form)");

    // Slider: `set slider ... to 0.5` writes 50 to a 0-100 slider; `slider ... is 50` reads it back.
    var sb = new FakeActor("HealthBar");
    var worldS = new FakeWorld();
    worldS.AddActor(sb);
    var engineS = FreshEngine(worldS);
    engineS.OnSetSlider = (context, thing, value) =>
    {
        var target = worldS.FindActor(thing);
        if (target != null) worldS.SetSlider(target, value.AsNumber());
    };
    engineS.AddSheet(sb, parser.Parse(@"when me appears
  set slider HealthBar to 0.5
  slider HealthBar is 50
  set Slid to 1"));
    engineS.Tick(0);
    Check(engineS.Variables.GetGlobal("Slid").AsNumber() == 1,
        "`set slider ... to 0.5` then `slider ... is 50` reads back the 0-100 value");

    // Runtime: the set actions reach the bridge hooks with the values as written.
    var hookActor = new FakeActor("HealthBar");
    var hookWorld = new FakeWorld();
    hookWorld.AddActor(hookActor);
    var hookEngine = FreshEngine(hookWorld);
    var hookLog = new List<string>();
    hookEngine.OnSetProgress = (context, thing, value) => hookLog.Add($"{thing}={value.AsNumber()}");
    hookEngine.OnSetSlider = (context, thing, value) => hookLog.Add($"{thing}={value.AsNumber()}");
    hookEngine.AddSheet(hookActor, parser.Parse("when me appears\n  set progress of HealthBar to 0.75\n  set slider HealthBar to 80\n"));
    hookEngine.Tick(0);
    Check(hookLog.Contains("HealthBar=0.75") && hookLog.Contains("HealthBar=80"),
        "`set progress of` / `set slider of` reach the runtime hooks");

    // ------------------------------------------------------------------
    // Task 13 Part A: `me is moving` (movement gate condition)
    // ------------------------------------------------------------------

    var movingProg = parser.Parse(@"when every 0 seconds
  me is moving
  set Moved to Moved + 1");
    Check(movingProg.Events.Count == 1, "`when every 0 seconds and me is moving` parses as one event");
    var movingDesc = TriggerClassifier.Classify(movingProg.Events[0]);
    Check(movingDesc.Kind == TriggerKind.Every, "`every 0 seconds` classifies to Every");
    var condNode = Conditions.Parse(movingProg.Events[0].Body[0].Words);
    Check(condNode.Kind == ConditionKind.Moving, "`me is moving` parses to ConditionKind.Moving");

    var moveWorld = new FakeWorld();
    var mover = new FakeActor("Mover");
    moveWorld.AddActor(mover);
    var moveEngine = FreshEngine(moveWorld);
    moveEngine.AddSheet(mover, movingProg);

    moveEngine.Tick(0);
    Check(moveEngine.Variables.GetGlobal("Moved").AsNumber() == 0, "`me is moving` is false while standing still");

    moveWorld.SetMoving(mover, true);
    moveEngine.Tick(1);
    Check(moveEngine.Variables.GetGlobal("Moved").AsNumber() == 1, "`me is moving` fires while moving");
    moveEngine.Tick(2);
    Check(moveEngine.Variables.GetGlobal("Moved").AsNumber() == 2, "`me is moving` keeps firing while moving");

    moveWorld.SetMoving(mover, false);
    moveEngine.Tick(3);
    Check(moveEngine.Variables.GetGlobal("Moved").AsNumber() == 2, "`me is moving` is false when stopped again");

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

var cameraAt = ActionWords.Parse(RawWords(actionParser, @"when game starts
  camera at CameraDoor"), null);
Check(cameraAt.Verb == ActionVerb.CameraAt && cameraAt.Thing == "CameraDoor",
    "`camera at CameraDoor` parses to CameraAt");

var cameraSwitch = ActionWords.Parse(RawWords(actionParser, @"when game starts
  camera switch CameraDoor"), null);
Check(cameraSwitch.Verb == ActionVerb.CameraSwitch && cameraSwitch.Thing == "CameraDoor",
    "`camera switch CameraDoor` parses to CameraSwitch");

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
      PhysicsMovement.PushModeFor(true, true) == PhysicsPushMode.Sweep,
    "`push` nudges with no body and sweeps a kinematic body");
Check(PhysicsMovement.PushModeFor(true, false) == PhysicsPushMode.Impulse,
    "`push` becomes a real impulse on a dynamic body");

// ---- Task 12 Part A: CharacterController as a third, opt-in movement type ----

Check(PhysicsMovement.ModeFor(true, true, false, false) == PhysicsMoveMode.Character,
    "an opted-in CharacterController walks with collide-and-slide Move");
Check(PhysicsMovement.ModeFor(true, true, true, true) == PhysicsMoveMode.Character,
    "a CharacterController wins over a RigidBody (it is the purpose-built walking body)");
Check(PhysicsMovement.ModeFor(false, true, true, false) == PhysicsMoveMode.Transform,
    "the CharacterController path is still opt-in (flag off = transform)");
Check(PhysicsMovement.ModeFor(true, false, true, false) == PhysicsMoveMode.Dynamic &&
      PhysicsMovement.ModeFor(true, false, true, true) == PhysicsMoveMode.Kinematic,
    "without a CharacterController the RigidBody modes are unchanged");

var characterDefaults = new CharacterBodySettings().Sanitized();
Check(characterDefaults.Radius == 50f && characterDefaults.Height == 150f &&
      characterDefaults.StepOffset == 30f && characterDefaults.SlopeLimit == 45f &&
      characterDefaults.GroundCheckDistance == 10f,
    "character body defaults match the Flax CharacterController defaults (radius 50, height 150, step 30, slope 45)");

var cleanedCharacter = new CharacterBodySettings
{
    Radius = -5f, Height = 20f, StepOffset = 80f, SlopeLimit = 140f, GroundCheckDistance = -1f,
}.Sanitized();
Check(cleanedCharacter.Radius >= 1f && cleanedCharacter.Height == 20f &&
      cleanedCharacter.StepOffset <= cleanedCharacter.Height &&
      cleanedCharacter.SlopeLimit == 100f && cleanedCharacter.GroundCheckDistance == 0f,
    "character settings are clamped (step offset never taller than the capsule, real angles/distances)");

PhysicsMovement.CharacterDisplacement(0, 0, -1, 120, 0.5, out double chx, out double chy, out double chz);
Check(Math.Abs(chx) < 1e-9 && Math.Abs(chy) < 1e-9 && Math.Abs(chz + 60) < 1e-9,
    "character displacement is direction × speed × tick (120/s for half a second = 60)");

// ---- Task 12 Part B: body-aware goto path following ----

Check(!PhysicsMovement.ReachesWaypoint(10, 120, 0.05) &&
      PhysicsMovement.ReachesWaypoint(6, 120, 0.05) &&
      PhysicsMovement.ReachesWaypoint(3, 120, 0.05),
    "a goto tick reaches the waypoint only when the remaining distance fits in one step");

Check(Math.Abs(PhysicsMovement.PathSpeed(10, 120, 0.05) - 120) < 1e-9,
    "goto keeps full speed while the waypoint is still ahead");
Check(Math.Abs(PhysicsMovement.PathSpeed(3, 120, 0.05) - 60) < 1e-9,
    "on the reaching tick goto slows just enough to land exactly on the waypoint");
Check(Math.Abs(PhysicsMovement.PathSpeed(4.9, 120, 0.05) - 98) < 1e-9,
    "the landing speed never exceeds the authored speed");
Check(Math.Abs(PhysicsMovement.PathSpeed(3, 120, 0) - 120) < 1e-9,
    "a zero-length tick cannot divide by zero (speed unchanged)");

// ---- Task 12 Part C: ground / slope / collision handling ----

Check(PhysicsMovement.IsWalkableSlope(1.0, 45) &&
      PhysicsMovement.IsWalkableSlope(Math.Cos(30 * Math.PI / 180), 45),
    "flat ground and a 30° slope are walkable under a 45° limit");
Check(!PhysicsMovement.IsWalkableSlope(Math.Cos(60 * Math.PI / 180), 45),
    "a 60° slope is rejected by a 45° limit (the character won't climb it)");
Check(PhysicsMovement.IsWalkableSlope(1.0, 0) &&
      !PhysicsMovement.IsWalkableSlope(Math.Cos(1 * Math.PI / 180), 0),
    "a 0° limit still allows flat ground but rejects any real slope");

Check(Math.Abs(PhysicsMovement.FeetY(500, false, 150, 50) - 375) < 1e-9 &&
      Math.Abs(PhysicsMovement.FeetY(500, true, 150, 50) - 500) < 1e-9,
    "feet sit half the capsule below a CapsuleCenter origin, and at the origin itself in Base mode");

Check(Math.Abs(PhysicsMovement.FallSpeed(-100, true, 981, 0.5)) < 1e-9,
    "a grounded character stops falling instead of accumulating speed");
Check(Math.Abs(PhysicsMovement.FallSpeed(0, false, PhysicsMovement.DefaultGravity, 0.5) + 490.5) < 1e-9,
    "an airborne character accelerates by gravity each tick (981 cm/s² for half a second)");

PhysicsMovement.CharacterStep(0, 0, -1, 100, 0, true, PhysicsMovement.DefaultGravity, 2, 0.1,
    out double gx, out double gy, out double gz, out double gFall);
Check(Math.Abs(gz + 10) < 1e-9 && Math.Abs(gx) < 1e-9 &&
      Math.Abs(gy + 0.2) < 1e-9 && Math.Abs(gFall + 2) < 1e-9,
    "a grounded character walks at speed and sticks to the floor with a small downward snap");

PhysicsMovement.CharacterStep(0, 0, -1, 100, -50, false, PhysicsMovement.DefaultGravity, 2, 0.1,
    out gx, out gy, out gz, out gFall);
Check(Math.Abs(gx) < 1e-9 && Math.Abs(gz) < 1e-9 &&
      Math.Abs(gy + 14.81) < 1e-9 && Math.Abs(gFall + 148.1) < 1e-9,
    "an airborne character falls straight down with no air steering");

PhysicsMovement.CharacterStep(0, 1, 0, 100, 0, false, PhysicsMovement.DefaultGravity, 2, 0.1,
    out gx, out gy, out gz, out gFall);
Check(Math.Abs(gy - 10) < 1e-9 && Math.Abs(gFall) < 1e-9,
    "an explicit up move still wins over gravity (elevators and flying characters)");

PhysicsMovement.CharacterStep(0, 0, -1, 100, 0, true, PhysicsMovement.DefaultGravity, 2, 0,
    out gx, out gy, out gz, out gFall);
Check(Math.Abs(gx) < 1e-9 && Math.Abs(gy) < 1e-9 && Math.Abs(gz) < 1e-9,
    "a zero-length character tick moves nothing");

// ---- Task 12 Part D: integration with the existing movement words ----

Check(PhysicsMovement.IsTurnDirection("left") && PhysicsMovement.IsTurnDirection("right") &&
      !PhysicsMovement.IsTurnDirection("up") && !PhysicsMovement.IsTurnDirection("forward"),
    "`turn` only accepts left/right (not every direction word)");
Check(PhysicsMovement.SignedTurnDegrees("left", 90) == -90 &&
      PhysicsMovement.SignedTurnDegrees("right", 90) == 90,
    "`turn left/right degrees 90` becomes the correct signed local yaw (Flax +Y turns right)");
Check(PhysicsMovement.SignedTurnDegrees("up", 90) == 0,
    "an unsupported turn direction cannot spin the actor");

Check(PhysicsMovement.PushModeFor(false, false, true, false) == PhysicsPushMode.Transform &&
      PhysicsMovement.PushModeFor(true, false, false, false) == PhysicsPushMode.Transform,
    "`push` stays a transform nudge when physics movement is off or the target has no body");
Check(PhysicsMovement.PushModeFor(true, true, false, false) == PhysicsPushMode.Character,
    "`push` moves a CharacterController through its collide-and-slide Move");
Check(PhysicsMovement.PushModeFor(true, false, true, true) == PhysicsPushMode.Sweep,
    "`push` sweeps a kinematic body so it cannot tunnel");
Check(PhysicsMovement.PushModeFor(true, false, true, false) == PhysicsPushMode.Impulse,
    "`push` is a real impulse on a dynamic body");
Check(PhysicsMovement.PushModeFor(true, true, true, false) == PhysicsPushMode.Character,
    "a CharacterController wins over a RigidBody for push, just like movement");

var physicsEngine = FreshEngine(new FakeWorld());
var physicsSheet = physicsEngine.AddSheet(new FakeActor("Mover"),
    parser.Parse("when me appears\n  move forward speed 5"));
Check(!physicsSheet.UsePhysicsMovement, "a sheet starts with physics movement off (transform default)");
Check(physicsSheet.GroundCheckDistance > 0f, "a sheet starts with a sane character ground-check distance");
physicsSheet.UsePhysicsMovement = true;
physicsSheet.GroundCheckDistance = 25f;
physicsEngine.SwapSheetProgram(physicsSheet, parser.Parse("when me appears\n  move forward speed 9"));
Check(physicsSheet.UsePhysicsMovement, "the physics opt-in survives a hot-reload swap");
Check(physicsSheet.GroundCheckDistance == 25f,
    "the character ground-check distance survives a hot-reload swap");

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

    // ------------------------------------------------------------------
    // Task 13 Part B: `when me touches <material>` (material detection)
    // ------------------------------------------------------------------

    var matProg = parser.Parse("when me touches Grass\n  set TouchedGrass to TouchedGrass + 1");
    Check(matProg.Events.Count == 1, "`when me touches Grass` parses as an event");
    var matDesc = TriggerClassifier.Classify(matProg.Events[0]);
    Check(matDesc.Kind == TriggerKind.Touches && matDesc.RequireMaterial == "Grass",
        "`when me touches Grass` classifies to Touches with RequireMaterial \"Grass\"");

    var matWorld = new FakeWorld();
    var matActor = new FakeActor("Feet");
    var grass = new FakeActor("GrassPatch");
    var stone = new FakeActor("StonePatch");
    matWorld.AddActor(matActor);
    matWorld.AddActor(grass);
    matWorld.AddActor(stone);
    matWorld.SetMaterial(grass, "Grass");
    matWorld.SetMaterial(stone, "Stone");
    matWorld.SetTouching(matActor, grass, true);
    matWorld.SetTouching(matActor, stone, true);
    var matEngine = FreshEngine(matWorld);
    matEngine.PollTouches = true;
    matEngine.AddSheet(matActor, matProg);
    matEngine.Tick(0);
    Check(matEngine.Variables.GetGlobal("TouchedGrass").AsNumber() == 1,
        "`when me touches Grass` fires on the grass material contact");
    matEngine.Tick(1);
    Check(matEngine.Variables.GetGlobal("TouchedGrass").AsNumber() == 2,
        "still touching grass refires every tick (material is a continuous state)");
    matWorld.SetTouching(matActor, grass, false);
    matWorld.SetTouching(matActor, stone, true);
    matEngine.Tick(2);
    Check(matEngine.Variables.GetGlobal("TouchedGrass").AsNumber() == 2,
        "leaving grass stops the continuous fire");
    matWorld.SetTouching(matActor, grass, true);
    matEngine.Tick(3);
    Check(matEngine.Variables.GetGlobal("TouchedGrass").AsNumber() == 3,
        "re-entering grass refires the material touch");

    // Unknown material reads as empty and never matches.
    var unkWorld = new FakeWorld();
    var unkActor = new FakeActor("Feet");
    var unk = new FakeActor("Thing");
    unkWorld.AddActor(unkActor);
    unkWorld.AddActor(unk);
    unkWorld.SetTouching(unkActor, unk, true);
    var unkEngine = FreshEngine(unkWorld);
    unkEngine.PollTouches = true;
    unkEngine.AddSheet(unkActor, parser.Parse("when me touches Grass\n  set TouchedGrass to TouchedGrass + 1"));
    unkEngine.Tick(0);
    Check(unkEngine.Variables.GetGlobal("TouchedGrass").IsNone(),
        "an actor with no material never matches `touches Grass`");

    // ------------------------------------------------------------------
    // Task 13 Part D: `when me touches <material> and <condition>`
    // ------------------------------------------------------------------

    var comboProg = parser.Parse("when me touches Grass and me is moving\n  set Combo to Combo + 1");
    Check(comboProg.Events.Count == 1, "`when me touches Grass and me is moving` parses as one event");
    var comboDesc = TriggerClassifier.Classify(comboProg.Events[0]);
    Check(comboDesc.Kind == TriggerKind.Touches && comboDesc.RequireMaterial == "Grass",
        "`when me touches Grass and me is moving` classifies to Touches with RequireMaterial \"Grass\"");
    Check(comboDesc.ExtraConditions.Count == 1 && comboDesc.ExtraConditions[0].Kind == ConditionKind.Moving,
        "`and me is moving` becomes one ExtraCondition of kind Moving");

    var noComboProg = parser.Parse("when me touches Grass\n  set Combo to Combo + 1");
    var noComboDesc = TriggerClassifier.Classify(noComboProg.Events[0]);
    Check(noComboDesc.ExtraConditions.Count == 0, "`when me touches Grass` without `and` has no extra conditions");

    var comboWorld = new FakeWorld();
    var comboFeet = new FakeActor("Feet");
    var comboGrass = new FakeActor("GrassPatch");
    comboWorld.AddActor(comboFeet);
    comboWorld.AddActor(comboGrass);
    comboWorld.SetMaterial(comboGrass, "Grass");
    comboWorld.SetTouching(comboFeet, comboGrass, true);
    var comboEngine = FreshEngine(comboWorld);
    comboEngine.PollTouches = true;
    comboEngine.AddSheet(comboFeet, comboProg);
    comboEngine.Tick(0);
    Check(comboEngine.Variables.GetGlobal("Combo").IsNone(),
        "`touches Grass and me is moving` does not fire when standing still");
    comboWorld.SetMoving(comboFeet, true);
    comboEngine.Tick(1);
    Check(comboEngine.Variables.GetGlobal("Combo").AsNumber() == 1,
        "`touches Grass and me is moving` fires while moving");
    comboWorld.SetMoving(comboFeet, false);
    comboEngine.Tick(2);
    Check(comboEngine.Variables.GetGlobal("Combo").AsNumber() == 1,
        "`touches Grass and me is moving` stops firing when standing still again");

    // ------------------------------------------------------------------
    // Task 13 Part C: `play step sound "<material>"` + per-step cooldown
    // ------------------------------------------------------------------

    var stepProg = parser.Parse(@"when me appears
  play step sound ""Grass""");
    Check(stepProg.Events.Count == 1, "`play step sound \"Grass\"` parses as one event");
    var stepStmt = stepProg.Events[0].Body[0].Statement as PlayStepSoundNode;
    Check(stepStmt != null && stepStmt.Material == "Grass",
        "`play step sound \"Grass\"` parses to PlayStepSoundNode with Material \"Grass\"");

    var stepWorld = new FakeWorld();
    var stepper = new FakeActor("Feet");
    stepWorld.AddActor(stepper);
    var stepEngine = FreshEngine(stepWorld);
    var stepSheet = stepEngine.AddSheet(stepper, parser.Parse(@"when every 0 seconds
  play step sound ""Grass"""));
    stepSheet.StepSoundMap["Grass"] = "grass_step.wav";
    int stepPlays = 0;
    stepEngine.OnPlayStepSound = (ctx, material, clip) => stepPlays++;
    Check(stepSheet.StepCooldown == 0.3, "default step cooldown is 0.3 seconds");

    stepEngine.Tick(0);
    Check(stepPlays == 1, "first tick plays the mapped step sound");
    stepEngine.Tick(0.1);
    Check(stepPlays == 1, "within cooldown the step sound is not replayed");
    stepEngine.Tick(0.4);
    Check(stepPlays == 2, "after cooldown the step sound plays again");

    // Material miss: unknown material warns once and never plays.
    var missWorld = new FakeWorld();
    var misser = new FakeActor("Feet");
    missWorld.AddActor(misser);
    var missEngine = FreshEngine(missWorld);
    var missSheet = missEngine.AddSheet(misser, parser.Parse(@"when every 0 seconds
  play step sound ""Ice"""));
    int missPlays = 0;
    int missWarnings = 0;
    missEngine.OnPlayStepSound = (ctx, material, clip) => missPlays++;
    missEngine.OnRuntimeWarning = msg => { if (msg.Contains("step sound")) missWarnings++; };

    missEngine.Tick(0);
    missEngine.Tick(0.5);
    missEngine.Tick(1.0);
    Check(missPlays == 0, "an unmapped material never plays a step sound");
    Check(missWarnings == 1, "a missing material mapping warns exactly once");

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

    // ------------------------------------------------------------------
    // Task 17 Part A: State Machines (BGE logic bricks) - core state words
    // ------------------------------------------------------------------

    // Registry examples for the new state words parse clean.
    var stateReg = WordRegistry.All.FirstOrDefault(e => e.Word == "when state is <thing>");
    Check(stateReg != null && stateReg.Example == "when state is chase", "registry has `when state is <thing>` with example");
    var setStateReg = WordRegistry.All.FirstOrDefault(e => e.Word == "set state");
    Check(setStateReg != null && setStateReg.Example == "set state patrol", "registry has `set state` with example");

    // `when state is chase` parses as an event.
    var stateIs = parser.Parse("when state is chase\n  set MyState to 1\n");
    Check(stateIs.Events.Count == 1, "`when state is chase` parses as an event");
    var stateIsDesc = TriggerClassifier.Classify(stateIs.Events[0]);
    Check(stateIsDesc.Kind == TriggerKind.StateIs && stateIsDesc.RequireName == "chase",
        "`when state is chase` classifies to StateIs with RequireName \"chase\"");

    // `set state chase` parses to a SetStateNode.
    var setState = parser.Parse("when me appears\n  set state chase\n");
    Check(setState.Events.Count == 1, "`set state chase` parses as an event");
    Check(setState.Events[0].Body[0].Statement is SetStateNode ss && ss.State == "chase",
        "`set state chase` builds a SetStateNode with State \"chase\"");

    // `my state` reads as a value.
    var myState = parser.Parse("when me appears\n  set X to my state\n");
    Check(myState.Events.Count == 1, "`my state` parses as a value");
    var myStateSet = (SetNode)myState.Events[0].Body[0].Statement!;
    Check(myStateSet.Value is VariableValue mv && mv.Owner == "my" && mv.Name == "state",
        "`my state` parses as a `my` variable read of `state`");

    // `state <name>` trigger modifier: `when W is pressed state chase` gates on state.
    var gated = parser.Parse("when W is pressed state chase\n  set Ordered to 1\n");
    Check(gated.Events.Count == 1, "`when W is pressed state chase` parses as an event");
    Check(gated.Events[0].RequiredState == "chase",
        "`when W is pressed state chase` sets RequiredState to \"chase\"");
    var gatedDesc = TriggerClassifier.Classify(gated.Events[0]);
    Check(gatedDesc.Kind == TriggerKind.KeyPressed,
        "the gated trigger still classifies as KeyPressed underneath");

    // `set state` without a name is a friendly error.
    ExpectParseError("when me appears\n  set state\n", "needs a state name", "`set state` without a name is a friendly error");

    // Runtime: `when state is chase` fires once on entry edge when `set state chase` runs.
    var stWorld = new FakeWorld();
    var stActor = new FakeActor("Guard");
    stWorld.AddActor(stActor);
    var stEngine = FreshEngine(stWorld);
    stEngine.AddSheet(stActor, parser.Parse(@"when state is chase
  set EnteredChase to EnteredChase + 1
when state is patrol
  set EnteredPatrol to EnteredPatrol + 1
when me appears
  set state patrol
when 1 is pressed
  set state chase
when 2 is pressed
  set state patrol"));
    stEngine.Tick(0);
    Check(stEngine.Variables.GetGlobal("EnteredPatrol").AsNumber() == 1 && stEngine.Variables.GetGlobal("EnteredChase").IsNone(),
        "appears sets state to patrol, `when state is patrol` fires once on entry");
    stEngine.Tick(1);
    Check(stEngine.Variables.GetGlobal("EnteredPatrol").AsNumber() == 1 && stEngine.Variables.GetGlobal("EnteredChase").IsNone(),
        "still in patrol does not refire `when state is patrol`");
    stEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "1" });
    stEngine.Tick(2);
    Check(stEngine.Variables.GetGlobal("EnteredChase").AsNumber() == 1, "`set state chase` fires `when state is chase` once");
    stEngine.Tick(3);
    Check(stEngine.Variables.GetGlobal("EnteredChase").AsNumber() == 1, "still in chase does not refire `when state is chase`");
    stEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "2" });
    stEngine.Tick(4);
    Check(stEngine.Variables.GetGlobal("EnteredPatrol").AsNumber() == 2, "`set state patrol` fires `when state is patrol` again");
    Check(stEngine.Variables.GetGlobal("EnteredChase").AsNumber() == 1, "leaving chase does not refire `when state is chase`");

    // Runtime: `state <name>` modifier gates an event — only fires when in that state.
    var gtWorld = new FakeWorld();
    var gtActor = new FakeActor("Guard");
    gtWorld.AddActor(gtActor);
    var gtEngine = FreshEngine(gtWorld);
    gtEngine.AddSheet(gtActor, parser.Parse(@"when W is pressed state chase
  set PressedInChase to 1
when W is pressed state patrol
  set PressedInPatrol to 1
when me appears
  set state patrol
when 1 is pressed
  set state chase"));
    gtEngine.Tick(0);
    Check(gtEngine.Variables.GetGlobal("PressedInPatrol").IsNone(), "appears sets state to patrol, no W pressed yet");
    gtEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "W" });
    gtEngine.Tick(1);
    Check(gtEngine.Variables.GetGlobal("PressedInPatrol").AsNumber() == 1, "in patrol state, `when W is pressed state patrol` fires");
    Check(gtEngine.Variables.GetGlobal("PressedInChase").IsNone(), "not in chase state, `when W is pressed state chase` does not fire");
    gtEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "1" });
    gtEngine.Tick(2);
    gtEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "W" });
    gtEngine.Tick(3);
    Check(gtEngine.Variables.GetGlobal("PressedInChase").AsNumber() == 1, "now in chase state, `when W is pressed state chase` fires");
    Check(gtEngine.Variables.GetGlobal("PressedInPatrol").AsNumber() == 1, "left patrol state, patrol event does not refire");

    // Runtime: `my state` reads the current state.
    var rsWorld = new FakeWorld();
    var rsActor = new FakeActor("Guard");
    rsWorld.AddActor(rsActor);
    var rsEngine = FreshEngine(rsWorld);
    rsEngine.AddSheet(rsActor, parser.Parse(@"when me appears
  set state chase
  set MyState to my state"));
    rsEngine.Tick(0);
    Check(rsEngine.Variables.GetGlobal("MyState").AsText() == "chase", "`my state` reads \"chase\" after `set state chase`");

    // ------------------------------------------------------------------
    // Task 18 Part A: RTS selection & control groups
    // ------------------------------------------------------------------

    // Trigger classification.
    var selectedProg = parser.Parse("when me is selected\n  set Picked to 1\n");
    Check(selectedProg.Events.Count == 1, "`when me is selected` parses as an event");
    var selectedDesc = TriggerClassifier.Classify(selectedProg.Events[0]);
    Check(selectedDesc.Kind == TriggerKind.Selected && selectedDesc.RequireName == "me",
        "`when me is selected` classifies to Selected with RequireName \"me\"");

    var namedSelected = parser.Parse("when Marine is selected\n  set Picked to 1\n");
    var namedSelectedDesc = TriggerClassifier.Classify(namedSelected.Events[0]);
    Check(namedSelectedDesc.Kind == TriggerKind.Selected && namedSelectedDesc.RequireName == "Marine",
        "`when Marine is selected` classifies to Selected with RequireName \"Marine\"");

    var deselectedProg = parser.Parse("when me is deselected\n  set Dropped to 1\n");
    var deselectedDesc = TriggerClassifier.Classify(deselectedProg.Events[0]);
    Check(deselectedDesc.Kind == TriggerKind.Deselected && deselectedDesc.RequireName == "me",
        "`when me is deselected` classifies to Deselected");

    var selectionChangedProg = parser.Parse("when selection changes\n  set Changed to 1\n");
    var selectionChangedDesc = TriggerClassifier.Classify(selectionChangedProg.Events[0]);
    Check(selectionChangedDesc.Kind == TriggerKind.SelectionChanged,
        "`when selection changes` classifies to SelectionChanged");

    var groupPressedProg = parser.Parse("when control group 1 is pressed\n  set GroupKey to 1\n");
    var groupPressedDesc = TriggerClassifier.Classify(groupPressedProg.Events[0]);
    Check(groupPressedDesc.Kind == TriggerKind.ControlGroupPressed &&
          groupPressedDesc.RequireName == "1" && groupPressedDesc.Key == "D1",
        "`when control group 1 is pressed` classifies to ControlGroupPressed (key D1)");

    var groupHeldProg = parser.Parse("when control group 0 is held\n  set GroupKey to 1\n");
    var groupHeldDesc = TriggerClassifier.Classify(groupHeldProg.Events[0]);
    Check(groupHeldDesc.Kind == TriggerKind.ControlGroupHeld && groupHeldDesc.Key == "D0",
        "`when control group 0 is held` classifies to ControlGroupHeld (key D0)");

    // Condition parsing.
    var selectionCondProg = parser.Parse(@"when every 1 seconds
  me is selected
  set Gate to 1
  selection count is 2
  set CountGate to 1
  my control group 1 is assigned
  set GroupGate to 1");
    var selectedCond = Conditions.Parse(selectionCondProg.Events[0].Body[0].Words);
    Check(selectedCond.Kind == ConditionKind.Selected, "`me is selected` parses to ConditionKind.Selected");
    var countCond = Conditions.Parse(selectionCondProg.Events[0].Body[2].Words);
    Check(countCond.Kind == ConditionKind.SelectionCountIs && countCond.Right is NumberValue cn && cn.Value == 2,
        "`selection count is 2` parses to SelectionCountIs with the right value");
    var groupCond = Conditions.Parse(selectionCondProg.Events[0].Body[4].Words);
    Check(groupCond.Kind == ConditionKind.MyControlGroupAssigned && groupCond.Group == 1,
        "`my control group 1 is assigned` parses to MyControlGroupAssigned with Group 1");

    // Action parsing.
    var selectMeSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  select me"), null);
    Check(selectMeSpec.Verb == ActionVerb.Select && selectMeSpec.Thing == "me" && !selectMeSpec.All && !selectMeSpec.Near,
        "`select me` parses as a single-target Select");

    var deselectMeSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  deselect me"), null);
    Check(deselectMeSpec.Verb == ActionVerb.Deselect && deselectMeSpec.Thing == "me",
        "`deselect me` parses as Deselect");

    var selectAllSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  select all Marine"), null);
    Check(selectAllSpec.Verb == ActionVerb.Select && selectAllSpec.All && selectAllSpec.Thing == "Marine",
        "`select all Marine` parses as an all-of-type Select");

    var selectNearSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  select units near me by 5"), null);
    Check(selectNearSpec.Verb == ActionVerb.Select && selectNearSpec.Near &&
          selectNearSpec.Thing == "me" && selectNearSpec.Distance == 5,
        "`select units near me by 5` parses as a near Select with radius 5");

    var deselectAllSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  deselect all"), null);
    Check(deselectAllSpec.Verb == ActionVerb.Deselect && deselectAllSpec.All,
        "`deselect all` parses as Deselect with the all flag");

    var controlGroupSet = parser.Parse("when me appears\n  set my control group to 2\n");
    Check(controlGroupSet.Events[0].Body[0].Statement is SetControlGroupNode cgNode && cgNode.Group == 2,
        "`set my control group to 2` builds a SetControlGroupNode with Group 2");
    ExpectParseError("when me appears\n  set my control group to\n", "looks like",
        "`set my control group to` without a number is a friendly error");

    // Runtime: Selected / Deselected / SelectionChanged fire on real selection changes.
    var selWorld = new FakeWorld();
    var marine = new FakeActor("Marine");
    selWorld.AddActor(marine);
    var selEngine = FreshEngine(selWorld);
    selEngine.AddSheet(marine, parser.Parse(@"when me is selected
  set SelectedCount to SelectedCount + 1
when me is deselected
  set DeselectedCount to DeselectedCount + 1"));
    selEngine.AddSheet(new GlobalHost(), parser.Parse(@"when selection changes
  set Changes to Changes + 1
when Marine is selected
  set NamedSelections to NamedSelections + 1"));

    selEngine.SelectActor(marine);
    selEngine.Tick(0);
    Check(selEngine.Variables.GetGlobal("SelectedCount").AsNumber() == 1,
        "`when me is selected` fires on the selected actor's own sheet");
    Check(selEngine.Variables.GetGlobal("NamedSelections").AsNumber() == 1,
        "`when Marine is selected` reaches a global sheet by name");
    Check(selEngine.Variables.GetGlobal("Changes").AsNumber() == 1,
        "`when selection changes` fires once for the selection change");
    selEngine.Tick(1);
    Check(selEngine.Variables.GetGlobal("SelectedCount").AsNumber() == 1,
        "still selected does not refire `when me is selected`");
    selEngine.DeselectActor(marine);
    selEngine.Tick(2);
    Check(selEngine.Variables.GetGlobal("DeselectedCount").AsNumber() == 1,
        "`when me is deselected` fires on deselection");
    Check(selEngine.Variables.GetGlobal("Changes").AsNumber() == 2,
        "`when selection changes` fires again for the deselection");

    // Runtime: `me is selected` and `selection count is <n>` gate actions.
    var selCondWorld = new FakeWorld();
    var scout = new FakeActor("Scout");
    var tank = new FakeActor("Tank");
    selCondWorld.AddActor(scout);
    selCondWorld.AddActor(tank);
    var selCondEngine = FreshEngine(selCondWorld);
    selCondEngine.AddSheet(scout, parser.Parse(@"when every 1 seconds
  me is selected
  set SelectedTicks to SelectedTicks + 1"));
    selCondEngine.AddSheet(new GlobalHost(), parser.Parse(@"when every 1 seconds
  selection count is 2
  set TwoSelectedTicks to TwoSelectedTicks + 1"));
    selCondEngine.Tick(1);
    Check(selCondEngine.Variables.GetGlobal("SelectedTicks").IsNone(),
        "`me is selected` blocks while the actor is not selected");
    selCondEngine.SelectActor(scout);
    selCondEngine.Tick(2);
    Check(selCondEngine.Variables.GetGlobal("SelectedTicks").AsNumber() == 1,
        "`me is selected` lets the event run once the actor is selected");
    Check(selCondEngine.Variables.GetGlobal("TwoSelectedTicks").IsNone(),
        "`selection count is 2` blocks with only one actor selected");
    selCondEngine.SelectActor(tank);
    selCondEngine.Tick(3);
    Check(selCondEngine.Variables.GetGlobal("TwoSelectedTicks").AsNumber() == 1,
        "`selection count is 2` runs with two actors selected");
    Check(selCondEngine.Variables.GetGlobal("SelectedTicks").AsNumber() == 2,
        "the selected actor keeps passing the condition each tick");

    // Runtime: `set my control group to <n>` on a unit's sheet, plus membership condition.
    var cgWorld = new FakeWorld();
    var grunt = new FakeActor("Grunt");
    cgWorld.AddActor(grunt);
    var cgEngine = FreshEngine(cgWorld);
    cgEngine.AddSheet(grunt, parser.Parse(@"when me appears
  set my control group to 1
when every 1 seconds
  my control group 1 is assigned
  set InGroupTicks to InGroupTicks + 1"));
    cgEngine.Tick(0);
    Check(cgEngine.Selection.IsInControlGroup(grunt, 1),
        "`set my control group to 1` assigns the unit to control group 1");
    cgEngine.Tick(1);
    Check(cgEngine.Variables.GetGlobal("InGroupTicks").AsNumber() == 1,
        "`my control group 1 is assigned` passes for a unit in that group");

    // Runtime: assigning the current selection from the global sheet (group stores the selection).
    cgEngine.SelectActor(grunt);
    cgEngine.AddSheet(new GlobalHost(), parser.Parse(@"when 2 is pressed
  set my control group to 2"));
    cgEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "2" });
    cgEngine.Tick(2);
    Check(cgEngine.Selection.IsInControlGroup(grunt, 2),
        "`set my control group to 2` on the global sheet stores the current selection");

    // Runtime: pressing and holding a control-group key.
    var keyWorld = new FakeWorld();
    var keyUnit = new FakeActor("Unit");
    keyWorld.AddActor(keyUnit);
    var keyEngine = FreshEngine(keyWorld);
    keyEngine.AddSheet(keyUnit, parser.Parse(@"when control group 1 is pressed
  set GroupPressed to GroupPressed + 1
when control group 1 is held
  set GroupHeld to GroupHeld + 1"));
    keyWorld.SetHeld("D1", true);
    keyEngine.Tick(0);
    Check(keyEngine.Variables.GetGlobal("GroupPressed").AsNumber() == 1,
        "`when control group 1 is pressed` fires on the key's enter edge");
    Check(keyEngine.Variables.GetGlobal("GroupHeld").AsNumber() == 1,
        "`when control group 1 is held` fires while the key is down");
    keyEngine.Tick(1);
    Check(keyEngine.Variables.GetGlobal("GroupPressed").AsNumber() == 1,
        "holding the group key does not refire `pressed`");
    Check(keyEngine.Variables.GetGlobal("GroupHeld").AsNumber() == 2,
        "`held` keeps firing every tick");
    keyWorld.SetHeld("D1", false);
    keyEngine.Tick(2);
    Check(keyEngine.Variables.GetGlobal("GroupHeld").AsNumber() == 2, "`held` stops when the key is released");
    keyWorld.SetHeld("D1", true);
    keyEngine.Tick(3);
    Check(keyEngine.Variables.GetGlobal("GroupPressed").AsNumber() == 2,
        "pressing the group key again refires `pressed`");

    // Selection state helpers: recall, replace, forget.
    var stateEngine = FreshEngine(new FakeWorld());
    var a1 = new FakeActor("A");
    var a2 = new FakeActor("B");
    stateEngine.Selection.ReplaceSelection(new object[] { a1, a2 });
    Check(stateEngine.Selection.Count == 2 && stateEngine.Selection.IsSelected(a1) && stateEngine.Selection.IsSelected(a2),
        "ReplaceSelection selects exactly the requested actors");
    stateEngine.Selection.SetControlGroup(3, new object[] { a1, a2 });
    Check(stateEngine.Selection.IsControlGroupAssigned(3) && stateEngine.Selection.IsInControlGroup(a2, 3),
        "SetControlGroup stores the group members");
    stateEngine.Selection.Clear();
    Check(stateEngine.Selection.Count == 0, "Clear empties the selection");
    Check(stateEngine.SelectControlGroup(3) && stateEngine.Selection.Count == 2,
        "SelectControlGroup recalls the stored group");
    stateEngine.Selection.Forget(a1);
    Check(!stateEngine.Selection.IsSelected(a1) && !stateEngine.Selection.IsInControlGroup(a1, 3),
        "Forget removes a destroyed actor from the selection and every control group");
    Check(!stateEngine.SelectControlGroup(9), "recalling an unassigned group changes nothing");

// ------------------------------------------------------------------
// Task 18 Part B: RTS resource economy
// ------------------------------------------------------------------

Check(ResourceManager.TryCanonical("supply", out var supplyCanonical) && supplyCanonical == "SupplyUsed",
      "resource alias `supply` canonicalises to SupplyUsed");
Check(ResourceManager.TryCanonical("Supply Used", out var supplyUsedCanonical) && supplyUsedCanonical == "SupplyUsed",
      "resource alias `Supply Used` canonicalises to SupplyUsed");
Check(ResourceManager.TryCanonical("supply max", out var supplyMaxCanonical) && supplyMaxCanonical == "SupplyMax",
      "resource alias `supply max` canonicalises to SupplyMax");
Check(ResourceManager.TryCanonical("Food Cap", out var foodCanonical) && foodCanonical == "FoodCap",
      "resource alias `Food Cap` canonicalises to FoodCap");
Check(ResourceManager.TryCanonical("rus", out var rusCanonical) && rusCanonical == "RUs",
      "resource alias `rus` canonicalises to RUs");
Check(!ResourceManager.IsResource("Score"), "a normal variable is not mistaken for a resource");

var resourceWarnings = 0;
var resourceVars = new EventVariables();
resourceVars.OnWarning = _ => resourceWarnings++;
Check(ResourceManager.ReadNumber(resourceVars, "Minerals") == 0 && resourceWarnings == 0,
      "an unset resource reads as 0 without a read-before-set warning");

// Trigger parsing + canonicalisation.
var resourceTriggers = parser.Parse(@"when Minerals becomes 500
  set Hit to 1
when Supply becomes 10
  set Hit to 1
when Supply Used becomes 10
  set Hit to 1
when resources change
  set Hit to 1");
var mineralsBecomes = TriggerClassifier.Classify(resourceTriggers.Events[0]);
Check(mineralsBecomes.Kind == TriggerKind.WatchBecomes && mineralsBecomes.WatchName == "Minerals",
      "`when Minerals becomes 500` watches Minerals");
var supplyBecomes = TriggerClassifier.Classify(resourceTriggers.Events[1]);
Check(supplyBecomes.Kind == TriggerKind.WatchBecomes && supplyBecomes.WatchName == "SupplyUsed",
      "`when Supply becomes 10` watches the canonical SupplyUsed");
var supplyUsedBecomes = TriggerClassifier.Classify(resourceTriggers.Events[2]);
Check(supplyUsedBecomes.Kind == TriggerKind.WatchBecomes && supplyUsedBecomes.WatchName == "SupplyUsed",
      "`when Supply Used becomes 10` watches the canonical SupplyUsed");
var resourcesChange = TriggerClassifier.Classify(resourceTriggers.Events[3]);
Check(resourcesChange.Kind == TriggerKind.ResourceChanges,
      "`when resources change` classifies to ResourceChanges");

// Condition parsing.
var mineralCondition = Conditions.Parse(parser.Parse(@"when every 1 seconds
  Minerals is higher than 50").Events[0].Body[0].Words);
Check(mineralCondition.Kind == ConditionKind.Higher && mineralCondition.Name == "Minerals" &&
      mineralCondition.Right is NumberValue mineralValue && mineralValue.Value == 50,
      "`Minerals is higher than 50` parses to Higher with value 50");
var supplyCondition = Conditions.Parse(parser.Parse(@"when every 1 seconds
  Supply Used is lower than Supply Max").Events[0].Body[0].Words);
Check(supplyCondition.Kind == ConditionKind.Lower && supplyCondition.Name == "SupplyUsed" &&
      supplyCondition.Right is VariableValue supplyRight && supplyRight.Name == "SupplyMax",
      "`Supply Used is lower than Supply Max` compares the two canonical resources");
var affordCondition = Conditions.Parse(parser.Parse(@"when every 1 seconds
  can afford Barracks").Events[0].Body[0].Words);
Check(affordCondition.Kind == ConditionKind.CanAfford && affordCondition.Thing == "Barracks",
      "`can afford Barracks` parses to CanAfford");

// Action parsing.
var resourceActions = parser.Parse(@"when game starts
  set Minerals to 500
  add 50 to Minerals
  deduct 100 from Minerals
  increase Supply Max by 8
  set Supply Used to 0");
Check(resourceActions.Events[0].Body[0].Statement is SetNode setMinerals &&
      setMinerals.Target.Name == "Minerals",
      "`set Minerals to 500` targets Minerals");
Check(resourceActions.Events[0].Body[1].Statement is AddNode addMinerals &&
      addMinerals.Target.Name == "Minerals",
      "`add 50 to Minerals` targets Minerals");
Check(resourceActions.Events[0].Body[2].Statement is DeductNode deductMinerals &&
      deductMinerals.Target.Name == "Minerals",
      "`deduct 100 from Minerals` builds a DeductNode");
Check(resourceActions.Events[0].Body[3].Statement is AddNode increaseSupply &&
      increaseSupply.Target.Name == "SupplyMax" &&
      increaseSupply.Amount is NumberValue increaseValue && increaseValue.Value == 8,
      "`increase Supply Max by 8` reuses AddNode on the canonical SupplyMax");
Check(resourceActions.Events[0].Body[4].Statement is SetNode setSupply &&
      setSupply.Target.Name == "SupplyUsed",
      "`set Supply Used to 0` targets the canonical SupplyUsed");

// Runtime set/add/deduct/increase and clamping.
var resourceEngine = FreshEngine(new FakeWorld());
resourceEngine.AddSheet(new GlobalHost(), parser.Parse(@"when game starts
  set Minerals to 500
  add 50 to Minerals
  deduct 100 from Minerals
  increase Supply Max by 8
  set Supply Used to 3"));
resourceEngine.SessionStarted();
resourceEngine.Tick(0);
Check(resourceEngine.Variables.GetGlobal("Minerals").AsNumber() == 450,
      "set/add/deduct run in order (500 + 50 - 100 = 450)");
Check(resourceEngine.Variables.GetGlobal("SupplyMax").AsNumber() == 8,
      "`increase Supply Max by 8` raises SupplyMax");
Check(resourceEngine.Variables.GetGlobal("SupplyUsed").AsNumber() == 3,
      "`set Supply Used to 3` writes canonical SupplyUsed");

var clampEngine = FreshEngine(new FakeWorld());
clampEngine.AddSheet(new GlobalHost(), parser.Parse(@"when game starts
  set Minerals to 10
  deduct 999 from Minerals"));
clampEngine.SessionStarted();
clampEngine.Tick(0);
Check(clampEngine.Variables.GetGlobal("Minerals").AsNumber() == 0,
      "`deduct` clamps resources at 0");

// Aliases share one slot.
var aliasEngine = FreshEngine(new FakeWorld());
aliasEngine.AddSheet(new GlobalHost(), parser.Parse(@"when game starts
  set Supply to 5
  add 2 to Supply Used"));
aliasEngine.SessionStarted();
aliasEngine.Tick(0);
Check(aliasEngine.Variables.HasGlobal("SupplyUsed") &&
      aliasEngine.Variables.GetGlobal("SupplyUsed").AsNumber() == 7 &&
      !aliasEngine.Variables.HasGlobal("Supply"),
      "`Supply` and `Supply Used` write the same canonical global");

// `when Minerals becomes` fires on the change edge, not on the baseline tick.
var becomesWorld = new FakeWorld();
var becomesEngine = FreshEngine(becomesWorld);
becomesEngine.AddSheet(new FakeActor("Economy"), parser.Parse(@"when Minerals becomes 500
  set Hit to Hit + 1
when Supply becomes 10
  set SupplyHit to SupplyHit + 1"));
becomesEngine.Tick(0);
Check(becomesEngine.Variables.GetGlobal("Hit").IsNone(),
      "the first tick only establishes the resource baseline");
becomesEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(500));
becomesEngine.Tick(1);
Check(becomesEngine.Variables.GetGlobal("Hit").AsNumber() == 1,
      "`when Minerals becomes 500` fires when the resource reaches 500");
becomesEngine.Variables.SetGlobal("SupplyUsed", ValueBox.OfNumber(10));
becomesEngine.Tick(2);
Check(becomesEngine.Variables.GetGlobal("SupplyHit").AsNumber() == 1,
      "`when Supply becomes 10` reads the canonical SupplyUsed slot");
becomesEngine.Tick(3);
Check(becomesEngine.Variables.GetGlobal("Hit").AsNumber() == 1,
      "staying at 500 does not refire `when Minerals becomes 500`");

// `when resources change` fires for any resource, once per change.
var resourcesWorld = new FakeWorld();
var resourcesEngine = FreshEngine(resourcesWorld);
resourcesEngine.AddSheet(new GlobalHost(), parser.Parse(@"when resources change
  set ResourceChanges to ResourceChanges + 1"));
resourcesEngine.Tick(0);
Check(resourcesEngine.Variables.GetGlobal("ResourceChanges").IsNone(),
      "`when resources change` does not fire on its baseline tick");
resourcesEngine.Variables.SetGlobal("Gas", ValueBox.OfNumber(25));
resourcesEngine.Tick(1);
Check(resourcesEngine.Variables.GetGlobal("ResourceChanges").AsNumber() == 1,
      "`when resources change` fires when Gas changes");
resourcesEngine.Tick(2);
Check(resourcesEngine.Variables.GetGlobal("ResourceChanges").AsNumber() == 1,
      "unchanged resources do not refire `when resources change`");
resourcesEngine.Variables.SetGlobal("Ore", ValueBox.OfNumber(4));
resourcesEngine.Tick(3);
Check(resourcesEngine.Variables.GetGlobal("ResourceChanges").AsNumber() == 2,
      "a second resource change fires again");

// `can afford` uses data-driven cost variables.
var affordVars = new EventVariables();
Check(ResourceManager.CanAfford(affordVars, "Barracks"),
      "a building with no cost variables is affordable");
affordVars.SetGlobal("BarracksCost", ValueBox.OfNumber(150));
Check(!ResourceManager.CanAfford(affordVars, "Barracks"),
      "`BarracksCost` blocks when Minerals are short");
affordVars.SetGlobal("Minerals", ValueBox.OfNumber(200));
Check(ResourceManager.CanAfford(affordVars, "Barracks"),
      "`BarracksCost` passes when Minerals are enough");
affordVars.SetGlobal("BarracksGasCost", ValueBox.OfNumber(100));
Check(!ResourceManager.CanAfford(affordVars, "Barracks"),
      "a gas cost also blocks when Gas is short");
affordVars.SetGlobal("Gas", ValueBox.OfNumber(100));
Check(ResourceManager.CanAfford(affordVars, "Barracks"),
      "the building is affordable once Minerals and Gas are covered");
affordVars.SetGlobal("BarracksSupplyCost", ValueBox.OfNumber(2));
affordVars.SetGlobal("SupplyMax", ValueBox.OfNumber(10));
affordVars.SetGlobal("SupplyUsed", ValueBox.OfNumber(9));
Check(!ResourceManager.CanAfford(affordVars, "Barracks"),
      "a supply cost blocks when the supply cap would be exceeded");
affordVars.SetGlobal("SupplyUsed", ValueBox.OfNumber(8));
Check(ResourceManager.CanAfford(affordVars, "Barracks"),
      "a supply cost passes exactly at the cap");

var foodVars = new EventVariables();
foodVars.SetGlobal("FactorySupplyCost", ValueBox.OfNumber(1));
foodVars.SetGlobal("FoodCap", ValueBox.OfNumber(5));
foodVars.SetGlobal("SupplyUsed", ValueBox.OfNumber(4));
Check(ResourceManager.CanAfford(foodVars, "Factory"),
      "FoodCap is the supply ceiling when SupplyMax is unset");
foodVars.SetGlobal("SupplyUsed", ValueBox.OfNumber(5));
Check(!ResourceManager.CanAfford(foodVars, "Factory"),
      "FoodCap blocks a building that would exceed it");

// `can afford` gates actions at runtime, and the supply condition compares resources.
var resourceGateWorld = new FakeWorld();
var resourceGateEngine = FreshEngine(resourceGateWorld);
resourceGateEngine.AddSheet(new GlobalHost(), parser.Parse(@"when every 1 seconds
  can afford Barracks
  set CanBuild to CanBuild + 1
  Supply Used is lower than Supply Max
  set SupplyRoom to SupplyRoom + 1"));
resourceGateEngine.Variables.SetGlobal("BarracksCost", ValueBox.OfNumber(100));
resourceGateEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(50));
resourceGateEngine.Variables.SetGlobal("SupplyUsed", ValueBox.OfNumber(3));
resourceGateEngine.Variables.SetGlobal("SupplyMax", ValueBox.OfNumber(10));
resourceGateEngine.Tick(1);
Check(resourceGateEngine.Variables.GetGlobal("CanBuild").IsNone(),
      "`can afford Barracks` blocks while Minerals are short");
Check(resourceGateEngine.Variables.GetGlobal("SupplyRoom").AsNumber() == 1,
      "`Supply Used is lower than Supply Max` passes with spare supply");
resourceGateEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(150));
resourceGateEngine.Tick(2);
Check(resourceGateEngine.Variables.GetGlobal("CanBuild").AsNumber() == 1,
      "`can afford Barracks` lets the event run once the cost is covered");
resourceGateEngine.Variables.SetGlobal("SupplyUsed", ValueBox.OfNumber(10));
resourceGateEngine.Tick(3);
Check(resourceGateEngine.Variables.GetGlobal("SupplyRoom").AsNumber() == 2,
      "`Supply Used is lower than Supply Max` blocks at the cap");

// Reading a resource through an event does not warn.
var quietWorld = new FakeWorld();
var quietEngine = FreshEngine(quietWorld);
int quietWarnings = 0;
quietEngine.Variables.OnWarning = _ => quietWarnings++;
quietEngine.AddSheet(new GlobalHost(), parser.Parse(@"when every 1 seconds
  set Seen to Minerals"));
quietEngine.Tick(1);
Check(quietEngine.Variables.GetGlobal("Seen").AsNumber() == 0 && quietWarnings == 0,
      "reading an unset resource from an event is quiet and reads 0");

    // ------------------------------------------------------------------
    // Task 15 Part C: TextBox / InputField
    // ------------------------------------------------------------------

    var textUiProg = parser.Parse(@"when NameBox text changes
  set TextChangedCount to TextChangedCount + 1

when NameBox is focused
  set FocusCount to FocusCount + 1

when NameBox loses focus
  set BlurCount to BlurCount + 1

when NameBox submits
  set SubmitCount to SubmitCount + 1");
    Check(textUiProg.Events.Count == 4, "the four TextBox triggers parse as four events");
    Check(TriggerClassifier.Classify(textUiProg.Events[0]).Kind == TriggerKind.TextChanged &&
          TriggerClassifier.Classify(textUiProg.Events[0]).RequireName == "NameBox",
        "`when NameBox text changes` classifies to TextChanged");
    Check(TriggerClassifier.Classify(textUiProg.Events[1]).Kind == TriggerKind.Focused &&
          TriggerClassifier.Classify(textUiProg.Events[1]).RequireName == "NameBox",
        "`when NameBox is focused` classifies to Focused");
    Check(TriggerClassifier.Classify(textUiProg.Events[2]).Kind == TriggerKind.LostFocus &&
          TriggerClassifier.Classify(textUiProg.Events[2]).RequireName == "NameBox",
        "`when NameBox loses focus` classifies to LostFocus");
    Check(TriggerClassifier.Classify(textUiProg.Events[3]).Kind == TriggerKind.Submitted &&
          TriggerClassifier.Classify(textUiProg.Events[3]).RequireName == "NameBox",
        "`when NameBox submits` classifies to Submitted");

    // `when me ...` forms use the sheet's own actor.
    var meTextProg = parser.Parse("when me text changes\n  set MeChanged to MeChanged + 1\n");
    Check(TriggerClassifier.Classify(meTextProg.Events[0]).Kind == TriggerKind.TextChanged &&
          TriggerClassifier.Classify(meTextProg.Events[0]).RequireName == "me",
        "`when me text changes` classifies to TextChanged with `me`");

    // Actions: focus / unfocus / select all.
    var focusProg = parser.Parse("when W is pressed\n  focus NameBox\n  unfocus NameBox\n  select all NameBox\n");
    Check(focusProg.Events[0].Body.Count == 3, "`focus`, `unfocus` and `select all` parse as body lines");
    var focusAction = ActionWords.Parse(focusProg.Events[0].Body[0].Words, null);
    Check(focusAction.Verb == ActionVerb.Focus && focusAction.Thing == "NameBox",
        "`focus NameBox` parses to ActionVerb.Focus");
    var unfocusAction = ActionWords.Parse(focusProg.Events[0].Body[1].Words, null);
    Check(unfocusAction.Verb == ActionVerb.Unfocus && unfocusAction.Thing == "NameBox",
        "`unfocus NameBox` parses to ActionVerb.Unfocus");
    var selectAllAction = ActionWords.Parse(focusProg.Events[0].Body[2].Words, null);
    Check(selectAllAction.Verb == ActionVerb.Select && selectAllAction.All && selectAllAction.Thing == "NameBox",
        "`select all NameBox` keeps the select-all spec (the bridge picks text over units)");

    // Conditions: `<thing> is focused` and `text of <thing> is <value>`.
    var focusedCond = Conditions.Parse(new List<Token>
    {
        new Token { Type = TokenType.Word, Text = "NameBox" },
        new Token { Type = TokenType.Word, Text = "is" },
        new Token { Type = TokenType.Word, Text = "focused" },
    });
    Check(focusedCond.Kind == ConditionKind.Focused && focusedCond.Thing == "NameBox",
        "`NameBox is focused` parses to ConditionKind.Focused");
    var textCond = Conditions.Parse(new List<Token>
    {
        new Token { Type = TokenType.Word, Text = "text" },
        new Token { Type = TokenType.Word, Text = "of" },
        new Token { Type = TokenType.Word, Text = "NameBox" },
        new Token { Type = TokenType.Word, Text = "is" },
        new Token { Type = TokenType.Str, Text = "Player" },
    });
    Check(textCond.Kind == ConditionKind.TextIs && textCond.Thing == "NameBox" &&
          textCond.Right is TextValue tcv && tcv.Text == "Player",
        "`text of NameBox is \"Player\"` parses to ConditionKind.TextIs");

    // Runtime: text-change / focus / blur / submit pushes reach the matching sheet.
    var nameBox = new FakeActor("NameBox");
    var textWorld = new FakeWorld();
    textWorld.AddActor(nameBox);
    var textEngine = FreshEngine(textWorld);
    textEngine.AddSheet(nameBox, textUiProg);
    textEngine.Tick(0);
    textEngine.RaisePush(nameBox, new PushSignal { Kind = TriggerKind.TextChanged, Name = "NameBox", Other = nameBox });
    textEngine.Tick(1);
    Check(textEngine.Variables.GetGlobal("TextChangedCount").AsNumber() == 1,
        "a TextChanged push fires `when NameBox text changes`");
    textEngine.RaisePush(nameBox, new PushSignal { Kind = TriggerKind.Focused, Name = "NameBox", Other = nameBox });
    textEngine.Tick(2);
    Check(textEngine.Variables.GetGlobal("FocusCount").AsNumber() == 1,
        "a Focused push fires `when NameBox is focused`");
    textEngine.RaisePush(nameBox, new PushSignal { Kind = TriggerKind.LostFocus, Name = "NameBox", Other = nameBox });
    textEngine.Tick(3);
    Check(textEngine.Variables.GetGlobal("BlurCount").AsNumber() == 1,
        "a LostFocus push fires `when NameBox loses focus`");
    textEngine.RaisePush(nameBox, new PushSignal { Kind = TriggerKind.Submitted, Name = "NameBox", Other = nameBox });
    textEngine.Tick(4);
    Check(textEngine.Variables.GetGlobal("SubmitCount").AsNumber() == 1,
        "a Submitted push fires `when NameBox submits`");

    // `when me text changes` ignores another text box's broadcast.
    var otherBox = new FakeActor("OtherBox");
    textWorld.AddActor(otherBox);
    var meBoxEngine = FreshEngine(textWorld);
    meBoxEngine.AddSheet(nameBox, meTextProg);
    meBoxEngine.Tick(0);
    meBoxEngine.RaisePush(otherBox, new PushSignal { Kind = TriggerKind.TextChanged, Name = "OtherBox", Other = otherBox });
    meBoxEngine.Tick(1);
    Check(meBoxEngine.Variables.GetGlobal("MeChanged").Kind == ValueKind.None,
        "`when me text changes` ignores another text box");
    meBoxEngine.RaisePush(nameBox, new PushSignal { Kind = TriggerKind.TextChanged, Name = "NameBox", Other = nameBox });
    meBoxEngine.Tick(2);
    Check(meBoxEngine.Variables.GetGlobal("MeChanged").AsNumber() == 1,
        "`when me text changes` fires for the sheet's own text box");

    // Condition evaluation against the world's focus/text state.
    var condBox = new FakeActor("NameBox");
    var condWorld = new FakeWorld();
    condWorld.AddActor(condBox);
    condWorld.SetFocused(condBox, true);
    condWorld.SetText(condBox, "Player");
    var condEngine = FreshEngine(condWorld);
    condEngine.AddSheet(condBox, parser.Parse(@"when me appears
  NameBox is focused
  text of NameBox is ""Player""
  set UiMatch to 1"));
    condEngine.Tick(0);
    Check(condEngine.Variables.GetGlobal("UiMatch").AsNumber() == 1,
        "`<thing> is focused` and `text of <thing> is <value>` read the UI state");

    var condWorldB = new FakeWorld();
    condWorldB.AddActor(condBox);
    condWorldB.SetText(condBox, "Enemy");
    var condEngineB = FreshEngine(condWorldB);
    condEngineB.AddSheet(condBox, parser.Parse(@"when me appears
  NameBox is focused
  text of NameBox is ""Player""
  set UiMatch to 1"));
    condEngineB.Tick(0);
    Check(condEngineB.Variables.GetGlobal("UiMatch").Kind == ValueKind.None,
        "an unfocused text box with different text fails both conditions");

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
    private readonly HashSet<object> moving = new HashSet<object>();
    private readonly List<object> actors = new List<object>();
    private readonly HashSet<(object, object)> touching = new HashSet<(object, object)>();
    private readonly HashSet<object> hovered = new HashSet<object>();
    private readonly HashSet<object> buttonPressed = new HashSet<object>();
    private readonly HashSet<object> buttonDisabled = new HashSet<object>();
    private readonly Dictionary<object, double> progress = new Dictionary<object, double>();
    private readonly Dictionary<object, double> sliders = new Dictionary<object, double>();
    private readonly Dictionary<object, string> materials = new Dictionary<object, string>();
    private readonly HashSet<object> focused = new HashSet<object>();
    private readonly Dictionary<object, string> texts = new Dictionary<object, string>();

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

    public void SetMaterial(object actor, string material)
    {
        materials[actor] = material;
    }

    public void SetHovered(object actor, bool on)
    {
        if (on)
            hovered.Add(actor);
        else
            hovered.Remove(actor);
    }

    public void SetButtonPressed(object actor, bool on)
    {
        if (on)
            buttonPressed.Add(actor);
        else
            buttonPressed.Remove(actor);
    }

    public void SetButtonDisabled(object actor, bool on)
    {
        if (on)
            buttonDisabled.Add(actor);
        else
            buttonDisabled.Remove(actor);
    }

    public void SetProgress(object actor, double value)
    {
        progress[actor] = value;
    }

    public void SetSlider(object actor, double value)
    {
        sliders[actor] = value;
    }

    public void SetFocused(object actor, bool on)
    {
        if (on)
            focused.Add(actor);
        else
            focused.Remove(actor);
    }

    public void SetText(object actor, string text)
    {
        texts[actor] = text;
    }

    public void SetMoving(object actor, bool on)
    {
        if (on)
            moving.Add(actor);
        else
            moving.Remove(actor);
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

    // The fake stores one scalar per pair, so 2D and 3D distances are the same here.
    // Ground-plane triggers (near/arrives) call Distance2D; tests still drive it via SetDistance.
    public double Distance2D(object from, object to)
    {
        return Distance(from, to);
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

    public string MaterialOf(object actor)
    {
        return materials.TryGetValue(actor, out string m) ? m : "";
    }

    public bool IsHovered(object actor)
    {
        return hovered.Contains(actor);
    }

    public bool IsButtonPressed(object actor)
    {
        return buttonPressed.Contains(actor);
    }

    public bool IsButtonDisabled(object actor)
    {
        return buttonDisabled.Contains(actor);
    }

    public bool IsFocused(object actor)
    {
        return focused.Contains(actor);
    }

    public string TextOf(object actor)
    {
        return texts.TryGetValue(actor, out string text) ? text : "";
    }

    public double ProgressOf(object actor)
    {
        double v = progress.TryGetValue(actor, out var raw) ? raw : 0;
        return v > 1 ? v / 100.0 : v; // 0-100 input normalizes to 0-1, matching the Flax bar
    }

    public double SliderOf(object actor)
    {
        double v = sliders.TryGetValue(actor, out var raw) ? raw : 0;
        return v <= 1 ? v * 100.0 : v; // 0-1 input normalizes to 0-100, matching the Flax slider
    }

    public bool IsMoving(object actor)
    {
        return moving.Contains(actor);
    }

    private readonly HashSet<(object, string)> playingParticles = new HashSet<(object, string)>();

    public void SetParticlePlaying(object actor, string name, bool on)
    {
        if (on)
            playingParticles.Add((actor, name));
        else
            playingParticles.Remove((actor, name));
    }

    public bool IsParticlePlaying(object actor, string name)
    {
        return playingParticles.Contains((actor, name));
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

    public bool IsDescendantOf(object child, object root)
    {
        return ReferenceEquals(child, root);
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

