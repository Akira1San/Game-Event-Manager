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

// Task 29 Part A — two or three operators on one `set` line, with `* /` before `+ -`.
var expressionParse = parser.Parse("when game starts\n  set Damage to my Strength * 2 + 5\n");
var expressionSet = (SetNode)expressionParse.Events[0].Body[0].Statement!;
var expressionValue = expressionSet.Value as ExpressionValue;
Check(expressionValue != null && expressionValue.Parts.Count == 3 && expressionValue.Ops.Count == 2 &&
      expressionValue.Ops[0] == '*' && expressionValue.Ops[1] == '+' && expressionSet.Op == '\0',
      "`set Damage to my Strength * 2 + 5` becomes a three-part expression and leaves the one-op slot empty");
var threeParse = parser.Parse("when game starts\n  set Damage to 1 + 2 * 3 - 4\n");
var threeSet = (SetNode)threeParse.Events[0].Body[0].Statement!;
Check(threeSet.Value is ExpressionValue threeValue && threeValue.Ops.Count == 3,
      "three operators on one line still parse");
string tooManyOps = null;
try
{
    parser.Parse("when game starts\n  set Damage to 1 + 2 + 3 + 4 + 5\n");
}
catch (ParseException error)
{
    tooManyOps = error.Message;
}
Check(tooManyOps != null && tooManyOps.Contains("4 calculations") && tooManyOps.Contains("split it in two") &&
      tooManyOps.Contains("set running to 1 + 2"),
      "a fourth operator asks for the line to be split, pointing at the first half to keep");

var expressionEngine = FreshEngine(new FakeWorld());
expressionEngine.AddSheet(new GlobalHost(), parser.Parse(@"when game starts
  set Strength to 10
  set WeaponDamage to 3
  set Damage to Strength * 2 + WeaponDamage
  set Precedence to 2 + 3 * 4
  set LeftToRight to 10 - 2 - 3
  set ThreeOps to 1 + 2 * 3 - 4
  set Dividing to 20 / 2 / 5
  set Negated to Strength * -1 + 10
  set Greeting to ""Gold: "" + Strength + ""!"""));
expressionEngine.SessionStarted();
expressionEngine.Tick(0);
Check(expressionEngine.Variables.GetGlobal("Damage").AsNumber() == 23,
      "`set Damage to Strength * 2 + WeaponDamage` reads 23 for strength 10 and weapon damage 3");
Check(expressionEngine.Variables.GetGlobal("Precedence").AsNumber() == 14,
      "`*` runs before `+` (2 + 3 * 4 = 14)");
Check(expressionEngine.Variables.GetGlobal("LeftToRight").AsNumber() == 5,
      "`+` and `-` stay left to right (10 - 2 - 3 = 5)");
Check(expressionEngine.Variables.GetGlobal("ThreeOps").AsNumber() == 3,
      "three operators mix precedence and left-to-right (1 + 2 * 3 - 4 = 3)");
Check(expressionEngine.Variables.GetGlobal("Dividing").AsNumber() == 2,
      "division stays left to right (20 / 2 / 5 = 2)");
Check(expressionEngine.Variables.GetGlobal("Negated").AsNumber() == 0,
      "a negative number inside an expression reads as unary minus (10 * -1 + 10 = 0)");
Check(expressionEngine.Variables.GetGlobal("Greeting").AsText() == "Gold: 10!",
      "a `+` with text on either side still joins the texts inside an expression");

var divideByZeroEngine = FreshEngine(new FakeWorld());
divideByZeroEngine.AddSheet(new GlobalHost(), parser.Parse("when game starts\n  set Ratio to 10 / 0 + 1\n"));
divideByZeroEngine.SessionStarted();
divideByZeroEngine.Tick(0);
Check(divideByZeroEngine.Variables.GetGlobal("Ratio").AsNumber() == 1,
      "dividing by zero reads 0 inside an expression, like the one-operator line");

// Task 29 Part B — numeric helpers as value forms.
var helperParse = parser.Parse(@"when game starts
  set Mana to clamp my Mana to 0 and my MaxMana
  set Speed to min my BaseSpeed and 10
  set Speed to max my BaseSpeed and 10
  set Gold to round 2.5
  set Mana to floor 2.9
  set Mana to ceil 2.1
  set Move to abs -3
  set Tax to 10% of my Gold");
var clampNode = ((SetNode)helperParse.Events[0].Body[0].Statement!).Value as HelperValue;
var minNode = ((SetNode)helperParse.Events[0].Body[1].Statement!).Value as HelperValue;
var maxNode = ((SetNode)helperParse.Events[0].Body[2].Statement!).Value as HelperValue;
var roundNode = ((SetNode)helperParse.Events[0].Body[3].Statement!).Value as HelperValue;
var floorNode = ((SetNode)helperParse.Events[0].Body[4].Statement!).Value as HelperValue;
var ceilNode = ((SetNode)helperParse.Events[0].Body[5].Statement!).Value as HelperValue;
var absNode = ((SetNode)helperParse.Events[0].Body[6].Statement!).Value as HelperValue;
var percentNode = ((SetNode)helperParse.Events[0].Body[7].Statement!).Value as HelperValue;
Check(clampNode != null && clampNode.Kind == ValueHelper.Clamp &&
      clampNode.First is VariableValue clampValue && clampValue.Owner == "my" && clampValue.Name == "Mana" &&
      clampNode.Second is NumberValue clampLow && clampLow.Value == 0 &&
      clampNode.Third is VariableValue clampHigh && clampHigh.Owner == "my" && clampHigh.Name == "MaxMana",
      "`clamp my Mana to 0 and my MaxMana` is a HelperValue with the value and both bounds");
Check(minNode != null && minNode.Kind == ValueHelper.Min && maxNode != null && maxNode.Kind == ValueHelper.Max &&
      roundNode != null && roundNode.Kind == ValueHelper.Round && floorNode != null && floorNode.Kind == ValueHelper.Floor &&
      ceilNode != null && ceilNode.Kind == ValueHelper.Ceil && absNode != null && absNode.Kind == ValueHelper.Abs,
      "`min`, `max`, `round`, `floor`, `ceil` and `abs` are their own helper kinds");
Check(percentNode != null && percentNode.Kind == ValueHelper.PercentOf &&
      percentNode.First is NumberValue percentAmount && percentAmount.Value == 10 &&
      percentNode.Second is VariableValue percentTarget && percentTarget.Owner == "my" && percentTarget.Name == "Gold",
      "`10% of my Gold` is a PercentOf helper reading 10 and the value after `of`");

var helperEngine = FreshEngine(new FakeWorld());
helperEngine.AddSheet(new GlobalHost(), parser.Parse(@"when game starts
  set Gold to 250
  add 10% of Gold to Gold
  set Price to 50
  set Tax to 20% of Price
  set Mana to 80
  set MaxMana to 60
  set Mana to clamp Mana to 0 and MaxMana
  set Low to clamp -5 to 0 and 10
  set High to clamp 15 to 0 and 10
  set Swapped to clamp 15 to 10 and 0
  set Smallest to min 4 and 9
  set Largest to max 4 and 9
  set Rounded to round 2.5
  set RoundedNegative to round -2.5
  set Floored to floor 2.9
  set Ceiled to ceil 2.1
  set Absolute to abs -4
  set Nested to clamp round 9.6 to 0 and 9
  set InExpression to round 2.5 + 1
  set PercentRounded to round 10% of 55"));
helperEngine.SessionStarted();
helperEngine.Tick(0);
Check(helperEngine.Variables.GetGlobal("Gold").AsNumber() == 275,
      "`add 10% of Gold to Gold` raises 250 by 25");
Check(helperEngine.Variables.GetGlobal("Tax").AsNumber() == 10,
      "`20% of Price` reads a tenth of the variable, unrounded (50 -> 10)");
Check(helperEngine.Variables.GetGlobal("Mana").AsNumber() == 60,
      "a potion over the cap clamps down to MaxMana (80 -> 60)");
Check(helperEngine.Variables.GetGlobal("Low").AsNumber() == 0 &&
      helperEngine.Variables.GetGlobal("High").AsNumber() == 10,
      "`clamp` lifts a value under the low bound and pulls one over the high bound");
Check(helperEngine.Variables.GetGlobal("Swapped").AsNumber() == 10,
      "`clamp` bounds written high-to-low still clamp (15 with 10 and 0 reads 10)");
Check(helperEngine.Variables.GetGlobal("Smallest").AsNumber() == 4 &&
      helperEngine.Variables.GetGlobal("Largest").AsNumber() == 9,
      "`min 4 and 9` reads 4 and `max 4 and 9` reads 9");
Check(helperEngine.Variables.GetGlobal("Rounded").AsNumber() == 3 &&
      helperEngine.Variables.GetGlobal("RoundedNegative").AsNumber() == -3,
      "`round` sends halves away from zero (2.5 -> 3, -2.5 -> -3)");
Check(helperEngine.Variables.GetGlobal("Floored").AsNumber() == 2 &&
      helperEngine.Variables.GetGlobal("Ceiled").AsNumber() == 3,
      "`floor 2.9` reads 2 and `ceil 2.1` reads 3");
Check(helperEngine.Variables.GetGlobal("Absolute").AsNumber() == 4,
      "`abs -4` reads 4");
Check(helperEngine.Variables.GetGlobal("Nested").AsNumber() == 9,
      "helpers nest: `clamp round 9.6 to 0 and 9` rounds to 10 then clamps to 9");
Check(helperEngine.Variables.GetGlobal("InExpression").AsNumber() == 4,
      "a helper is an ordinary operand inside a multi-operator line (round 2.5 + 1 = 4)");
Check(helperEngine.Variables.GetGlobal("PercentRounded").AsNumber() == 6,
      "`round 10% of 55` rounds the percentage (5.5 -> 6)");

string clampShape = null;
try
{
    parser.Parse("when game starts\n  set Mana to clamp 5 to 1\n");
}
catch (ParseException error)
{
    clampShape = error.Message;
}
Check(clampShape != null && clampShape.Contains("clamp <value> to <min> and <max>") &&
      clampShape.Contains("\u201Cand\u201D"),
      "a `clamp` missing its `and` bound explains the full shape");
string minShape = null;
try
{
    parser.Parse("when game starts\n  set Fast to min 5\n");
}
catch (ParseException error)
{
    minShape = error.Message;
}
Check(minShape != null && minShape.Contains("min <a> and <b>"),
      "`min` without its second value explains the shape");
string percentShape = null;
try
{
    parser.Parse("when game starts\n  set Tax to 10% Price\n");
}
catch (ParseException error)
{
    percentShape = error.Message;
}
Check(percentShape != null && percentShape.Contains("10% of my Gold"),
      "a percentage missing its `of` explains the shape");

// Task 29 Part C — `field "<field>" of "<row>" in "<table>"` reads the <Row><Field> data global.
var lookupParse = parser.Parse("when game starts\n  set Damage to field \"Damage\" of \"Sword\" in \"item\"\n");
var fieldNode = ((SetNode)lookupParse.Events[0].Body[0].Statement!).Value as FieldValue;
Check(fieldNode != null && fieldNode.Field == "Damage" && fieldNode.Row == "Sword" && fieldNode.Table == "item",
      "`field \"Damage\" of \"Sword\" in \"item\"` parses into a FieldValue with all three names");
string lookupShape = null;
try
{
    parser.Parse("when game starts\n  set Damage to field \"Damage\" of \"Sword\"\n");
}
catch (ParseException error)
{
    lookupShape = error.Message;
}
Check(lookupShape != null && lookupShape.Contains("I expected \u201Cin\u201D") &&
      lookupShape.Contains("field \"<field>\" of \"<row>\" in \"<table>\""),
      "a `field` missing its `in \"<table>\"` explains the shape");

// A real table read: the shipped data files define the values, the sheet only names the row.
var dataEngine = FreshEngine(new FakeWorld());
dataEngine.AddSheet(new GlobalHost(), parser.Parse(File.ReadAllText("Source/Game/GameFiles/ItemData.events")));
dataEngine.AddSheet(new GlobalHost(), parser.Parse(File.ReadAllText("Source/Game/GameFiles/WeaponData.events")));
dataEngine.AddSheet(new GlobalHost(), parser.Parse(File.ReadAllText("Source/Game/GameFiles/EnemyTypes.events")));
dataEngine.AddSheet(new GlobalHost(), parser.Parse(@"when game starts
  set SwordHit to field ""Damage"" of ""Sword"" in ""item""
  set SwordSlotRead to field ""Slot"" of ""Sword"" in ""item""
  set PistolBlastRead to field ""Blast"" of ""Pistol"" in ""weapon""
  set Doubled to field ""Damage"" of ""Sword"" in ""item"" * 2
  set RoundedRate to round field ""FireRate"" of ""Shotgun"" in ""weapon""
  set ZombieHit to field ""Health"" of ""Zombie"" in ""enemy""
  set SpacedRow to field ""Health"" of ""Shotgun Maniac"" in ""enemy""
  set CasedNames to field ""damage"" of ""sword"" in ""item"""));
dataEngine.SessionStarted();
dataEngine.Tick(0);
Check(dataEngine.Variables.GetGlobal("SwordHit").AsNumber() == 12,
      "`field \"Damage\" of \"Sword\"` reads the SwordDamage ItemData.events ships (12)");
Check(dataEngine.Variables.GetGlobal("SwordSlotRead").AsText() == "weapon",
      "a text field reads as text (SwordSlot is \"weapon\")");
Check(dataEngine.Variables.GetGlobal("PistolBlastRead").AsNumber() == 0,
      "a zero field reads 0 instead of warning (PistolBlast)");
Check(dataEngine.Variables.GetGlobal("Doubled").AsNumber() == 24,
      "a data field works as an operand inside a multi-operator line (12 * 2)");
Check(dataEngine.Variables.GetGlobal("RoundedRate").AsNumber() == 1,
      "a data field nests inside a helper (round ShotgunFireRate 0.8 reads 1)");
Check(dataEngine.Variables.GetGlobal("ZombieHit").AsNumber() == 50,
      "`field \"Health\" of \"Zombie\"` reads EnemyTypes.events' ZombieHealth (50)");
Check(dataEngine.Variables.GetGlobal("SpacedRow").AsNumber() == 55,
      "spaces in the row name are ignored (`Shotgun Maniac` reaches ShotgunManiacHealth)");
Check(dataEngine.Variables.GetGlobal("CasedNames").AsNumber() == 12,
      "row and field names are matched case-insensitively (\"damage\" of \"sword\" reads SwordDamage)");

var missingFieldWarnings = new List<string>();
var missingFieldEngine = FreshEngine(new FakeWorld());
missingFieldEngine.OnRuntimeWarning = message => missingFieldWarnings.Add(message);
missingFieldEngine.AddSheet(new GlobalHost(), parser.Parse(@"when game starts
  set SwordDamage to 12
  set SwordSlot to ""weapon""
  set Missing to field ""Armour"" of ""Sword"" in ""item""
  set MissingAgain to field ""Armour"" of ""Sword"" in ""item""
  set NoRow to field ""Damage"" of ""Hammer"" in ""item"""));
missingFieldEngine.SessionStarted();
missingFieldEngine.Tick(0);
Check(missingFieldEngine.Variables.GetGlobal("Missing").AsNumber() == 0 &&
      missingFieldEngine.Variables.GetGlobal("NoRow").AsNumber() == 0,
      "a missing field or row reads 0, never a silent wrong number");
Check(missingFieldWarnings.Count == 2,
      "each missing field warns once, however many times the line reads it");
Check(missingFieldWarnings.Count > 0 && missingFieldWarnings[0].Contains("SwordArmour") &&
      missingFieldWarnings[0].Contains("has Damage and Slot"),
      "the missing-field warning names the global it looked for and lists the fields the row has");
Check(missingFieldWarnings.Count > 1 && missingFieldWarnings[1].Contains("HammerDamage") &&
      missingFieldWarnings[1].Contains("Rows with a") && missingFieldWarnings[1].Contains("Sword"),
      "a missing row suggests the rows that do have the field");

// Task 29 Part D — a typo gets the nearest name, the way `UnknownWord` and the file loader do.
var typoWarnings = new List<string>();
var typoEngine = FreshEngine(new FakeWorld());
typoEngine.OnRuntimeWarning = message => typoWarnings.Add(message);
typoEngine.AddSheet(new GlobalHost(), parser.Parse(@"when game starts
  set SwordDamage to 12
  set SwordSlot to ""weapon""
  set ZombieHealth to 50
  set ZombieSpeed to 60
  set TypoField to field ""Damag"" of ""Sword"" in ""item""
  set TypoRow to field ""Health"" of ""Zombiee"" in ""enemy""
  set FarOff to field ""Health"" of ""CompletelyDifferent"" in ""enemy"""));
typoEngine.SessionStarted();
typoEngine.Tick(0);
Check(typoWarnings.Count > 0 && typoWarnings[0].Contains("has Damage and Slot") &&
      typoWarnings[0].Contains("Did you mean \u201CDamage\u201D?"),
      "a mistyped field lists the row's fields and offers the nearest one (`Damag` -> `Damage`)");
Check(typoWarnings.Count > 1 && typoWarnings[1].Contains("Did you mean row \u201CZombie\u201D?"),
      "a mistyped row suggests the nearest row that has the field (`Zombiee` -> `Zombie`)");
Check(typoWarnings.Count > 2 && !typoWarnings[2].Contains("Did you mean"),
      "a name with no near match is listed without a silly suggestion");
Check(typoEngine.Variables.GetGlobal("TypoField").AsNumber() == 0 &&
      typoEngine.Variables.GetGlobal("TypoRow").AsNumber() == 0 &&
      typoEngine.Variables.GetGlobal("FarOff").AsNumber() == 0,
      "every mistyped lookup reads 0 instead of a wrong number");

// Task 29 Part D's three friendly errors, asserted together as the task words them.
Check(tooManyOps != null && tooManyOps.Contains("4 calculations") && tooManyOps.Contains("split it in two") &&
      tooManyOps.Contains("set running to 1 + 2"),
      "the too-long expression names the operator to split at and shows the half to keep");
Check(missingFieldWarnings.Count > 0 && missingFieldWarnings[0].Contains("has Damage and Slot") &&
      missingFieldWarnings[0].Contains("SwordArmour"),
      "an unknown field lists the fields the table actually has");
Check(typoWarnings.Count > 1 && typoWarnings[1].Contains("Did you mean row") &&
      typoWarnings[1].Contains("Rows with a"),
      "an unknown row lists and suggests the rows that exist");

// Task 29 Part E — the grammar change must leave every shipped `set` line parsing exactly as it did.
// Every `set` line in every GameFiles sheet is re-parsed on its own under a `when game starts`
// trigger, so a grammar regression points at the file and the message instead of failing a scene.
var setLineFailures = new List<string>();
int setLineCount = 0;
foreach (string sheetPath in Directory.GetFiles("Source/Game/GameFiles", "*.events", SearchOption.AllDirectories))
{
    var setBody = new System.Text.StringBuilder("when game starts\n");
    foreach (string rawLine in File.ReadAllLines(sheetPath))
    {
        string trimmedSet = rawLine.Trim();
        if (!trimmedSet.StartsWith("set ", StringComparison.Ordinal))
            continue;
        setLineCount++;
        setBody.Append("  ").Append(trimmedSet).Append('\n');
    }
    if (setBody.Length == "when game starts\n".Length)
        continue;
    try
    {
        parser.Parse(setBody.ToString());
    }
    catch (ParseException error)
    {
        setLineFailures.Add(Path.GetFileName(sheetPath) + ": " + error.Message);
    }
}
if (setLineFailures.Count > 0)
    Console.WriteLine("  first shipped `set` line that stopped parsing: " + setLineFailures[0]);
Check(setLineCount > 400 && setLineFailures.Count == 0,
      $"every `set` line the shipped sheets use still parses ({setLineCount} lines across GameFiles)");

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
        : entry.Kind == "directive"
            ? entry.Example + "\n"
            : entry.Kind == "structural"
                ? "when me appears\n  my score is 0\n  " + entry.Example + "\n"
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

// Task 20: `otherwise` / `else` blocks
var elseProgram = parser.Parse(@"when me overlaps Bomb
  if Flag is true
  set Log to Log + ""T""
  otherwise
  set Log to Log + ""F""");
Check(elseProgram.Events[0].Body[0].IsCondition && !elseProgram.Events[0].Body[0].IsOtherwise,
      "an `if` line is a condition line with no statement");
Check(elseProgram.Events[0].Body[2].IsOtherwise && elseProgram.Events[0].Body[2].Statement == null,
      "`otherwise` opens a block (no statement of its own)");
var elseAlias = parser.Parse("when me overlaps Bomb\n  if Flag is true\n  set Log to Log + \"T\"\n  else\n  set Log to Log + \"F\"\n");
Check(elseAlias.Events[0].Body[2].IsOtherwise, "`else` is accepted as an alias for `otherwise`");

var elseEngine = FreshEngine(new FakeWorld());
var elseSheet = elseEngine.AddSheet(new FakeActor("Gate"), elseProgram);
var elseBomb = new FakeActor("Bomb");
elseEngine.Variables.SetGlobal("Flag", ValueBox.OfBoolean(true));
elseEngine.PushContact(TriggerKind.Overlaps, elseSheet.Actor, elseBomb);
elseEngine.Tick(0);
Check(elseEngine.Variables.GetGlobal("Log").AsText() == "T",
      "the guarded lines run and the `otherwise` lines are skipped when the condition is true");
elseEngine.Variables.SetGlobal("Flag", ValueBox.OfBoolean(false));
elseEngine.PushContact(TriggerKind.Overlaps, elseSheet.Actor, elseBomb);
elseEngine.Tick(1);
Check(elseEngine.Variables.GetGlobal("Log").AsText() == "TF",
      "the `otherwise` lines run when the condition is false");

var pairsEngine = FreshEngine(new FakeWorld());
var pairsSheet = pairsEngine.AddSheet(new FakeActor("Pairs"), parser.Parse(@"when me overlaps Bomb
  if A is true
  set Log to Log + ""A""
  otherwise
  set Log to Log + ""a""
  if B is true
  set Log to Log + ""B""
  otherwise
  set Log to Log + ""b"""));
pairsEngine.Variables.SetGlobal("A", ValueBox.OfBoolean(true));
pairsEngine.Variables.SetGlobal("B", ValueBox.OfBoolean(false));
pairsEngine.PushContact(TriggerKind.Overlaps, pairsSheet.Actor, elseBomb);
pairsEngine.Tick(0);
Check(pairsEngine.Variables.GetGlobal("Log").AsText() == "Ab",
      "two if/otherwise pairs in one body each use their own condition's gate");
pairsEngine.Variables.SetGlobal("A", ValueBox.OfBoolean(false));
pairsEngine.Variables.SetGlobal("B", ValueBox.OfBoolean(true));
pairsEngine.PushContact(TriggerKind.Overlaps, pairsSheet.Actor, elseBomb);
pairsEngine.Tick(1);
Check(pairsEngine.Variables.GetGlobal("Log").AsText() == "AbaB",
      "a second pair does not inherit the first pair's gate");

var elseWaitEngine = FreshEngine(new FakeWorld());
var elseWaitSheet = elseWaitEngine.AddSheet(new FakeActor("Waiter"), parser.Parse(@"when me overlaps Bomb
  if Flag is true
  wait 1 second
  set Log to Log + ""T""
  otherwise
  set Log to Log + ""F"""));
elseWaitEngine.Variables.SetGlobal("Flag", ValueBox.OfBoolean(true));
elseWaitEngine.PushContact(TriggerKind.Overlaps, elseWaitSheet.Actor, elseBomb);
elseWaitEngine.Tick(0);
elseWaitEngine.Tick(1);
Check(elseWaitEngine.Variables.GetGlobal("Log").AsText() == "T",
      "a wait inside an `if` block resumes knowing the condition was true (no `otherwise` leak)");

ExpectParseError("when me appears\n  otherwise\n  hide me\n", "right above `otherwise`",
      "`otherwise` with no condition line above it is a friendly error");
ExpectParseError("when me appears\n  if Score is 0\n  hide me\n  otherwise\n  show me\n  otherwise\n  hide me\n",
      "right above `otherwise`", "a second `otherwise` for the same condition is a friendly error");

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

    // ------------------------------------------------------------------
    // Task 7 Part G1: Enemy_LOS.events (line-of-sight chase, events only)
    // ------------------------------------------------------------------

    var losProgram = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Enemy_LOS.events"));
    Check(losProgram.Events.Count == 7, $"Enemy_LOS.events parses into 7 events (got {losProgram.Events.Count})");

    // Patrol -> chase on the sight edge, hunt on the fall, patrol again after the 6 s timer.
    var losWorld = new FakeWorld();
    var losEnemy = new FakeActor("Enemy_LOS");
    var losPlayer = new FakeActor("Player");
    losWorld.AddActor(losEnemy);
    losWorld.AddActor(losPlayer);
    losWorld.SetDistance(losEnemy, losPlayer, 500); // outside the Zombie's 200 cm sight
    losWorld.SetBlocking(losEnemy, losPlayer, false);
    var losEngine = FreshEngine(losWorld);
    losEngine.AddSheet(losEnemy, losProgram);
    var losHurtProgram = parser.Parse("when message \"hurt\" arrives\n  set Hurts to Hurts + 1");
    losEngine.AddSheet(losPlayer, losHurtProgram);
    losEngine.Tick(0);
    Check(losEngine.Variables.GetLocal(losEnemy, "mode").AsText() == "patrol", "Enemy_LOS starts in patrol");
    Check(losEngine.Variables.GetLocal(losEnemy, "SightDistance").AsNumber() == 10,
        "Enemy_LOS copies the Zombie type's sight range");

    losWorld.SetDistance(losEnemy, losPlayer, 3);
    losEngine.Tick(0.1);
    Check(losEngine.Variables.GetLocal(losEnemy, "mode").AsText() == "chase", "seeing the player flips Enemy_LOS to chase");

    losWorld.SetBlocking(losEnemy, losPlayer, true);
    losEngine.Tick(0.2);
    Check(losEngine.Variables.GetLocal(losEnemy, "mode").AsText() == "hunt", "a broken ray flips Enemy_LOS to hunt");

    losEngine.Tick(6.3);
    Check(losEngine.Variables.GetLocal(losEnemy, "mode").AsText() == "patrol",
        "hunt gives up after 6 s without sight and returns to patrol");

    // Reacquiring inside the 6 s window keeps the chase (the `my mode is "hunt"` gate re-checks).
    var losReacquireWorld = new FakeWorld();
    var losReacquireEnemy = new FakeActor("Enemy_LOS");
    var losReacquirePlayer = new FakeActor("Player");
    losReacquireWorld.AddActor(losReacquireEnemy);
    losReacquireWorld.AddActor(losReacquirePlayer);
    losReacquireWorld.SetDistance(losReacquireEnemy, losReacquirePlayer, 500);
    losReacquireWorld.SetBlocking(losReacquireEnemy, losReacquirePlayer, false);
    var losReacquireEngine = FreshEngine(losReacquireWorld);
    losReacquireEngine.AddSheet(losReacquireEnemy, losProgram);
    losReacquireEngine.AddSheet(losReacquirePlayer, losHurtProgram);
    losReacquireEngine.Tick(0);
    losReacquireWorld.SetDistance(losReacquireEnemy, losReacquirePlayer, 3);
    losReacquireEngine.Tick(0.1);
    losReacquireWorld.SetBlocking(losReacquireEnemy, losReacquirePlayer, true);
    losReacquireEngine.Tick(0.2);
    losReacquireWorld.SetBlocking(losReacquireEnemy, losReacquirePlayer, false);
    losReacquireEngine.Tick(1);
    Check(losReacquireEngine.Variables.GetLocal(losReacquireEnemy, "mode").AsText() == "chase",
        "reacquiring during the hunt flips back to chase");
    losReacquireEngine.Tick(7);
    Check(losReacquireEngine.Variables.GetLocal(losReacquireEnemy, "mode").AsText() == "chase",
        "a reacquire inside the 6 s window is not reverted to patrol");

    // Melee contact still fires on the `near 1.5` enter edge during a chase.
    losReacquireWorld.SetDistance(losReacquireEnemy, losReacquirePlayer, 1);
    losReacquireEngine.Tick(7.1);
    losReacquireEngine.Tick(7.2); // messages are delivered on the next tick
    Check(losReacquireEngine.Variables.GetGlobal("Hurts").AsNumber() == 1,
        "Enemy_LOS sends `hurt` to the nearest player at melee range during a chase");

    // ------------------------------------------------------------------
    // Task 7 Part G3: arcade lane enemy + spawner (events only)
    // ------------------------------------------------------------------

    var arcadeEnemyProgram = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Arcade_Enemy.events"));
    Check(arcadeEnemyProgram.Events.Count == 7, $"Arcade_Enemy.events parses into 7 events (got {arcadeEnemyProgram.Events.Count})");

    var arcadeSpawnerProgram = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Arcade_Spawner.events"));
    Check(arcadeSpawnerProgram.Events.Count == 5, $"Arcade_Spawner.events parses into 3 events (got {arcadeSpawnerProgram.Events.Count})");

    // Contact: the `near 1.5` enter edge hurts once per approach, never once per tick.
    var arcadeWorld = new FakeWorld();
    var arcadeEnemy = new FakeActor("Zombie");
    var arcadePlayer = new FakeActor("Player");
    arcadeWorld.AddActor(arcadeEnemy);
    arcadeWorld.AddActor(arcadePlayer);
    arcadeWorld.SetDistance(arcadeEnemy, arcadePlayer, 400);
    var arcadeEngine = FreshEngine(arcadeWorld);
    arcadeEngine.AddSheet(arcadeEnemy, arcadeEnemyProgram);
    arcadeEngine.AddSheet(arcadePlayer, parser.Parse("when message \"hurt\" arrives\n  set Hurts to Hurts + 1"));
    arcadeEngine.Tick(0);
    Check(arcadeEngine.Variables.GetGlobal("Hurts").IsNone(), "a lane further than 1.5 away does not hurt");

    arcadeWorld.SetDistance(arcadeEnemy, arcadePlayer, 1);
    arcadeEngine.Tick(0.1);
    arcadeEngine.Tick(0.2); // messages are delivered on the next tick
    Check(arcadeEngine.Variables.GetGlobal("Hurts").AsNumber() == 1,
        "the arcade enemy sends `hurt` on contact");
    arcadeEngine.Tick(0.3);
    Check(arcadeEngine.Variables.GetGlobal("Hurts").AsNumber() == 1, "standing in contact does not hurt again (enter edge)");

    arcadeWorld.SetDistance(arcadeEnemy, arcadePlayer, 40);
    arcadeEngine.Tick(0.4);
    arcadeWorld.SetDistance(arcadeEnemy, arcadePlayer, 1);
    arcadeEngine.Tick(0.5);
    arcadeEngine.Tick(0.6);
    Check(arcadeEngine.Variables.GetGlobal("Hurts").AsNumber() == 2, "a second approach hurts again");

    // Bullets: five 10-damage shots drop a zombie's 50 health, and the lane loses one alive.
    var arcadeBullet = new FakeActor("Bullet");
    arcadeWorld.AddActor(arcadeBullet);
    arcadeWorld.SetDistance(arcadeEnemy, arcadePlayer, 400); // out of melee, so only shots land
    arcadeEngine.Tick(0.7);
    Check(arcadeEngine.Variables.GetLocal(arcadeEnemy, "Health").AsNumber() == 50,
        $"the Zombie type gives the lane enemy 50 hit points (got {arcadeEngine.Variables.GetLocal(arcadeEnemy, "Health").AsNumber()})");

    for (int shot = 0; shot < 5; shot++)
    {
        arcadeEngine.PushContact(TriggerKind.Touches, arcadeEnemy, arcadeBullet);
        arcadeEngine.Tick(0.8 + shot * 0.1);
    }
    arcadeEngine.Tick(1.4); // the `health becomes 0` watch settles on the next tick
    Check(arcadeEngine.Variables.GetLocal(arcadeEnemy, "Health").AsNumber() == 0,
        $"five 10-damage shots empty a zombie's 50 health (got {arcadeEngine.Variables.GetLocal(arcadeEnemy, "Health").AsNumber()})");
    Check(arcadeEngine.Variables.GetGlobal("EnemiesAlive").AsNumber() == -1,
        $"dying decrements EnemiesAlive (the lane counter the spawner caps, got {arcadeEngine.Variables.GetGlobal("EnemiesAlive").AsNumber()})");
    Check(arcadeEngine.Variables.GetGlobal("Kills Zombie").AsNumber() == 1,
        $"the kill is counted against the enemy's type (got {arcadeEngine.Variables.GetGlobal("Kills Zombie").AsNumber()})");

    // Spawner: one enemy every 3 s while the player is near, and never past 4 alive.
    // The player needs a sheet too: a condition's `near <thing>` lookup finds actors through
    // the runtime's registered sheets (the same path `when me is near Player` takes in game).
    var spawnerWorld = new FakeWorld();
    var spawner = new FakeActor("Arcade_Spawner");
    var spawnerPlayer = new FakeActor("Player");
    spawnerWorld.AddActor(spawner);
    spawnerWorld.AddActor(spawnerPlayer);
    spawnerWorld.SetDistance(spawner, spawnerPlayer, 40);
    var spawnerEngine = FreshEngine(spawnerWorld);
    spawnerEngine.AddSheet(spawner, arcadeSpawnerProgram);
    spawnerEngine.AddSheet(spawnerPlayer, parser.Parse(@"when game starts
  set LaneReady to true"));
    spawnerEngine.Tick(0);
    Check(spawnerEngine.Variables.GetGlobal("EnemiesAlive").AsNumber() == 0, "the spawner zeroes the lane counter on appear");

    for (int beat = 1; beat <= 6; beat++)
        spawnerEngine.Tick(beat * 3);
    Check(spawnerEngine.Variables.GetGlobal("EnemiesAlive").AsNumber() == 4,
        $"the spawner fills to 4 alive and holds there (got {spawnerEngine.Variables.GetGlobal("EnemiesAlive").AsNumber()})");

    spawnerWorld.SetDistance(spawner, spawnerPlayer, 500);
    spawnerEngine.Tick(30);
    Check(spawnerEngine.Variables.GetGlobal("EnemiesAlive").AsNumber() == 4,
        $"the `me is near Player by 60` gate switches spawning off when the player walks away (got {spawnerEngine.Variables.GetGlobal("EnemiesAlive").AsNumber()})");

    // ------------------------------------------------------------------
    // Task 7 Part G4 phase 1: knockdown, stand-up and the Combo counter
    // ------------------------------------------------------------------

    var knockdownWorld = new FakeWorld();
    var knockdownEnemy = new FakeActor("Zombie");
    var knockdownPlayer = new FakeActor("Player");
    knockdownWorld.AddActor(knockdownEnemy);
    knockdownWorld.AddActor(knockdownPlayer);
    knockdownWorld.SetDistance(knockdownEnemy, knockdownPlayer, 400);
    var knockdownEngine = FreshEngine(knockdownWorld);
    var knockdownSheet = knockdownEngine.AddSheet(knockdownEnemy, arcadeEnemyProgram);
    knockdownEngine.AddSheet(knockdownPlayer, parser.Parse("when message \"hurt\" arrives\n  set Hurts to Hurts + 1"));
    var knockdownBullet = new FakeActor("Bullet");
    knockdownWorld.AddActor(knockdownBullet);
    knockdownEngine.Tick(0);
    Check(knockdownSheet.CurrentState == "walk",
        $"the lane enemy starts in the walk state (got {knockdownSheet.CurrentState})");

    // First hit: damage lands and the state flips to down on the same tick.
    knockdownEngine.PushContact(TriggerKind.Touches, knockdownEnemy, knockdownBullet);
    knockdownEngine.Tick(0.1);
    Check(knockdownSheet.CurrentState == "down",
        $"a shot knocks the lane enemy down (got {knockdownSheet.CurrentState})");
    Check(knockdownEngine.Variables.GetLocal(knockdownEnemy, "Health").AsNumber() == 40,
        $"the knockdown shot still costs 10 health (got {knockdownEngine.Variables.GetLocal(knockdownEnemy, "Health").AsNumber()})");

    // A downed enemy neither walks nor swings: standing in melee range does nothing.
    knockdownWorld.SetDistance(knockdownEnemy, knockdownPlayer, 1);
    knockdownEngine.Tick(0.2);
    knockdownEngine.Tick(0.3);
    Check(knockdownEngine.Variables.GetGlobal("Hurts").IsNone(), "a downed enemy cannot swing at the player");

    // Re-hit mid-stun: more damage, but the wait blocks are not restarted (still down at 0.5).
    knockdownEngine.PushContact(TriggerKind.Touches, knockdownEnemy, knockdownBullet);
    knockdownEngine.Tick(0.4);
    Check(knockdownEngine.Variables.GetLocal(knockdownEnemy, "Health").AsNumber() == 30,
        $"a burst still hurts a downed enemy (got {knockdownEngine.Variables.GetLocal(knockdownEnemy, "Health").AsNumber()})");
    knockdownEngine.Tick(0.5);
    Check(knockdownSheet.CurrentState == "getup",
        $"the enemy starts standing up 0.4 s after the first hit, not after the re-hit (got {knockdownSheet.CurrentState})");
    knockdownEngine.Tick(0.7);
    Check(knockdownSheet.CurrentState == "walk",
        $"the enemy is walking again 0.6 s after the hit (got {knockdownSheet.CurrentState})");
    Check(knockdownEngine.Variables.GetGlobal("Combo").AsNumber() == 1,
        $"standing up counts one combo hit (got {knockdownEngine.Variables.GetGlobal("Combo").AsNumber()})");

    // Two hits in a row: Combo 2, and the attack works again once the enemy is back on its feet.
    knockdownWorld.SetDistance(knockdownEnemy, knockdownPlayer, 40);
    knockdownEngine.Tick(0.8);
    knockdownWorld.SetDistance(knockdownEnemy, knockdownPlayer, 1);
    knockdownEngine.Tick(0.9);
    knockdownEngine.Tick(1.0); // messages are delivered on the next tick
    Check(knockdownEngine.Variables.GetGlobal("Hurts").AsNumber() == 1,
        $"the attack resumes once the enemy is walking again (got {knockdownEngine.Variables.GetGlobal("Hurts").AsNumber()})");

    knockdownEngine.PushContact(TriggerKind.Touches, knockdownEnemy, knockdownBullet);
    knockdownEngine.Tick(1.1);
    knockdownEngine.Tick(1.5); // 0.4 s down
    knockdownEngine.Tick(1.7); // 0.2 s stand-up
    Check(knockdownSheet.CurrentState == "walk" &&
          knockdownEngine.Variables.GetGlobal("Combo").AsNumber() == 2,
        $"two quick hits read Combo 2 (got {knockdownEngine.Variables.GetGlobal("Combo").AsNumber()})");

    // Combo decays one point per two seconds, run by the single spawner actor.
    spawnerEngine.Variables.SetGlobal("Combo", Game.ValueBox.OfNumber(3));
    spawnerEngine.Tick(32);
    Check(spawnerEngine.Variables.GetGlobal("Combo").AsNumber() == 2,
        $"the lane decays Combo by one every 2 s (got {spawnerEngine.Variables.GetGlobal("Combo").AsNumber()})");

    // ------------------------------------------------------------------
    // Task 7 Part G4 phase 2: the lane's three types share one sheet
    // ------------------------------------------------------------------

    // The shipped data file carries Knife and Brute next to the TK4 cast.
    EnemyTypes.LoadData(parser.Parse(File.ReadAllText("Source/Game/GameFiles/EnemyTypes.events")));

    var knifeRuntime = FreshEngine(new FakeWorld());
    var knifeActor = new FakeActor("Knife");
    knifeRuntime.AddSheet(knifeActor, arcadeEnemyProgram);
    knifeRuntime.Tick(0);
    Check(knifeRuntime.Variables.GetLocal(knifeActor, "Type").AsText() == "Knife" &&
          knifeRuntime.Variables.GetLocal(knifeActor, "Speed").AsNumber() == 110 &&
          knifeRuntime.Variables.GetLocal(knifeActor, "Health").AsNumber() == 30,
        $"a `Knife` template reads its type from its name: speed {knifeRuntime.Variables.GetLocal(knifeActor, "Speed").AsNumber()}, health {knifeRuntime.Variables.GetLocal(knifeActor, "Health").AsNumber()}");

    var bruteRuntime = FreshEngine(new FakeWorld());
    var bruteActor = new FakeActor("Brute");
    bruteRuntime.AddSheet(bruteActor, arcadeEnemyProgram);
    bruteRuntime.Tick(0);
    Check(bruteRuntime.Variables.GetLocal(bruteActor, "Type").AsText() == "Brute" &&
          bruteRuntime.Variables.GetLocal(bruteActor, "Speed").AsNumber() == 40 &&
          bruteRuntime.Variables.GetLocal(bruteActor, "Health").AsNumber() == 120,
        $"a `Brute` template reads its type from its name: speed {bruteRuntime.Variables.GetLocal(bruteActor, "Speed").AsNumber()}, health {bruteRuntime.Variables.GetLocal(bruteActor, "Health").AsNumber()}");

    // Three shots drop a Knife, and the kill is counted against Knife alone.
    var killWorld = new FakeWorld();
    var killKnife = new FakeActor("Knife");
    var killBullet = new FakeActor("Bullet");
    killWorld.AddActor(killKnife);
    killWorld.AddActor(killBullet);
    var killEngine = FreshEngine(killWorld);
    killEngine.AddSheet(killKnife, arcadeEnemyProgram);
    killEngine.Tick(0);
    for (int shot = 0; shot < 3; shot++)
    {
        killEngine.PushContact(TriggerKind.Touches, killKnife, killBullet);
        killEngine.Tick(0.1 + shot * 0.1);
    }
    killEngine.Tick(0.6); // the `Health becomes 0` watch settles on a later tick
    Check(killEngine.Variables.GetGlobal("Kills Knife").AsNumber() == 1 &&
          killEngine.Variables.GetGlobal("Kills Brute").IsNone(),
        $"the lane counts Knife kills apart from Brute (Knife {killEngine.Variables.GetGlobal("Kills Knife").AsNumber()}, Brute {killEngine.Variables.GetGlobal("Kills Brute").AsNumber()})");

    EnemyTypes.LoadData(null);

    // Condition lookups resolve player references the way triggers do (Task 7 G3 follow-up), so
    // the spawner's portable `me is near player by 60` works no matter what the hero is called.
    var thingWorld = new FakeWorld();
    var thingSeeker = new FakeActor("Seeker");
    var thingFirst = new FakeActor("Player1");
    var thingSecond = new FakeActor("Player2");
    thingWorld.AddActor(thingSeeker);
    thingWorld.AddActor(thingFirst);
    thingWorld.AddActor(thingSecond);
    thingWorld.SetDistance(thingSeeker, thingFirst, 5);
    thingWorld.SetDistance(thingSeeker, thingSecond, 50);
    var thingEngine = FreshEngine(thingWorld);
    thingEngine.AddSheet(thingSeeker, parser.Parse(@"when every 1 seconds
  me is near player by 10
  add 1 to NearAny

when every 1 seconds
  me is near nearest player by 10
  add 1 to NearNearest

when every 1 seconds
  me is near Player1 by 10
  add 1 to NearNamed

when every 1 seconds
  me is near player 2 by 10
  add 1 to NearSecond

when every 1 seconds
  me is near Player2 by 10
  add 1 to NearSecondNamed

when every 1 seconds
  player exists
  add 1 to PlayerExists

when every 1 seconds
  Ghost does not exist
  add 1 to GhostMissing"));
    thingEngine.AddSheet(thingFirst, parser.Parse(@"when game starts
  set Ready to true"));
    thingEngine.AddSheet(thingSecond, parser.Parse(@"when game starts
  set Ready to true"));
    thingEngine.Tick(0);
    thingEngine.Tick(1);
    Check(thingEngine.Variables.GetGlobal("NearAny").AsNumber() == 1,
        "a condition resolves lowercase `player` to the nearest player (`me is near player by 10`)");
    Check(thingEngine.Variables.GetGlobal("NearNearest").AsNumber() == 1,
        "`me is near nearest player by 10` is a two-word player reference in a condition");
    Check(thingEngine.Variables.GetGlobal("NearNamed").AsNumber() == 1,
        "an ordinary actor name still resolves by name (`me is near Player1 by 10`)");
    Check(thingEngine.Variables.GetGlobal("NearSecond").AsNumber() == 0 &&
          thingEngine.Variables.GetGlobal("NearSecondNamed").AsNumber() == 0,
        "`me is near player 2` / `Player2` measure the second player, not the nearest one");
    Check(thingEngine.Variables.GetGlobal("PlayerExists").AsNumber() == 1,
        "`player exists` answers through the same lookup");
    Check(thingEngine.Variables.GetGlobal("GhostMissing").AsNumber() == 1,
        "`Ghost does not exist` still reads a missing ordinary actor as missing");

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
    // Task 17 Part D: Prefab / scene spawning
    // ------------------------------------------------------------------

    // Registry documents the prefab/scene spawn shapes (autocomplete + docs-on-hover).
    Check(WordRegistry.All.Any(e => e.Word == "spawn" && e.Example == "spawn prefab \"Hero\" at SpawnPoint"),
        "registry documents `spawn prefab \"<name>\" at <spot>`");
    Check(WordRegistry.All.Any(e => e.Word == "spawn" && e.Example == "spawn scene \"HeroScene\" at SpawnPoint"),
        "registry documents `spawn scene \"<name>\" at <spot>`");

    var spawnPrefabSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  spawn prefab ""Hero"" at SpawnPoint"), null);
    Check(spawnPrefabSpec.Verb == ActionVerb.SpawnPrefab && spawnPrefabSpec.Thing == "Hero" &&
          spawnPrefabSpec.To == "SpawnPoint",
        "`spawn prefab \"Hero\" at SpawnPoint` parses to SpawnPrefab at the spot");

    var spawnSceneSpec = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  spawn scene ""HeroScene"" at SpawnPoint"), null);
    Check(spawnSceneSpec.Verb == ActionVerb.SpawnScene && spawnSceneSpec.Thing == "HeroScene" &&
          spawnSceneSpec.To == "SpawnPoint",
        "`spawn scene \"HeroScene\" at SpawnPoint` parses to SpawnScene at the spot");

    var spawnCloneRegression = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  spawn Coin at SpawnPoint"), null);
    Check(spawnCloneRegression.Verb == ActionVerb.Spawn && spawnCloneRegression.Thing == "Coin",
        "`spawn Coin at SpawnPoint` still parses as a plain template clone");

    var numberedPrefab = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  spawn prefab ""Hero"" at SpawnPoint 1"), null);
    Check(numberedPrefab.Verb == ActionVerb.SpawnPrefab && numberedPrefab.To == "SpawnPoint 1",
        "`spawn prefab ... at SpawnPoint 1` keeps Flax's numbered actor name whole");
    var numberedClone = ActionWords.Parse(RawWords(actionParser, @"when W is pressed
  spawn Coin at SpawnPoint 2"), null);
    Check(numberedClone.Verb == ActionVerb.Spawn && numberedClone.To == "SpawnPoint 2",
        "`spawn Coin at SpawnPoint 2` keeps the numbered spot");

    // Scene lifecycle words (TASK_FIX_SCENES Parts B/C).
    var addSceneSpec = ActionWords.Parse(RawWords(actionParser, "when W is pressed\n  add scene \"HUD\""), null);
    Check(addSceneSpec.Verb == ActionVerb.AddScene && addSceneSpec.Thing == "HUD",
        "`add scene \"HUD\"` parses to AddScene");
    var unloadSceneSpec = ActionWords.Parse(RawWords(actionParser, "when W is pressed\n  unload scene \"HUD\""), null);
    Check(unloadSceneSpec.Verb == ActionVerb.UnloadScene && unloadSceneSpec.Thing == "HUD",
        "`unload scene \"HUD\"` parses to UnloadScene");
    Check(WordRegistry.All.Any(e => e.Word == "add" && e.Example == "add scene \"HUD\""),
        "registry documents `add scene \"HUD\"`");
    Check(WordRegistry.All.Any(e => e.Word == "unload" && e.Example == "unload scene \"HUD\""),
        "registry documents `unload scene \"HUD\"`");
    actionHint = "";
    var unquotedAddScene = ActionWords.Parse(LooseWords("add", "scene", "HUD"), GrabHint);
    Check(unquotedAddScene.Verb == ActionVerb.Unknown && actionHint.Contains("add scene"),
        "an unquoted `add scene` explains the shape");
    actionHint = "";
    var unquotedUnloadScene = ActionWords.Parse(LooseWords("unload", "scene", "HUD"), GrabHint);
    Check(unquotedUnloadScene.Verb == ActionVerb.Unknown && actionHint.Contains("unload scene"),
        "an unquoted `unload scene` explains the shape");

    // Written key names -> Flax's KeyboardKeys names. A name we don't know must not fall back
    // to another key: that bug made every arrow/digit report a held Space.
    Check(ActionWords.EngineKeyName("Space") == "Spacebar", "`Space` maps to Flax's `Spacebar` key");
    Check(ActionWords.EngineKeyName("1") == "Alpha1" && ActionWords.EngineKeyName("D3") == "Alpha3" &&
          ActionWords.EngineKeyName("0") == "Alpha0",
        "number keys (`1`, `D3`, `0`) map to Flax's `Alpha1`–`Alpha0`");
    Check(ActionWords.EngineKeyName("Up") == "ArrowUp" && ActionWords.EngineKeyName("Left") == "ArrowLeft",
        "arrow words map to Flax's `ArrowUp`/`ArrowLeft`");
    Check(ActionWords.EngineKeyName("Left Shift") == "Shift" && ActionWords.EngineKeyName("Right Ctrl") == "Control" &&
          ActionWords.EngineKeyName("Left Alt") == "Alt",
        "modifier words map to Flax's single `Shift`/`Control`/`Alt` keys");
    Check(ActionWords.EngineKeyName("Enter") == "Return", "`Enter` maps to Flax's `Return` key");
    Check(ActionWords.EngineKeyName("Banana") == "Banana",
        "an unknown key name is left alone instead of falling back to another key");

    // ---- Task 16 Part H: TK4 player controls & camera ----

    // Spelled-out arrows are real key names: TK4 steers with the arrow keys, and
    // `when Left Arrow is held` used to parse into a trigger that could never fire.
    Check(ActionWords.EngineKeyName("Left Arrow") == "ArrowLeft" &&
          ActionWords.EngineKeyName("Right Arrow") == "ArrowRight" &&
          ActionWords.EngineKeyName("Up Arrow") == "ArrowUp" &&
          ActionWords.EngineKeyName("Down Arrow") == "ArrowDown",
        "`Left/Right/Up/Down Arrow` map to Flax's Arrow keys");
    Check(ActionWords.EngineKeyName("Arrow Left") == "ArrowLeft" &&
          ActionWords.EngineKeyName("LeftArrow") == "ArrowLeft",
        "every spelling of one arrow resolves to that same arrow");
    Check(ActionWords.KeyNamesForScanning.Contains("Left Arrow") &&
          ActionWords.KeyNamesForScanning.Contains("Space") &&
          ActionWords.KeyNamesForScanning.Contains("Arrow Right"),
        "the input scan list includes both arrow spellings");

    var heldArrow = TriggerClassifier.Classify(
        parser.Parse("when Left Arrow is held\n  turn left degrees 4").Events[0]);
    Check(heldArrow.Kind == TriggerKind.KeyHeld && heldArrow.Key == "Left Arrow",
        "`when Left Arrow is held` is a polled key-held trigger");
    var pressedArrow = TriggerClassifier.Classify(
        parser.Parse("when Up Arrow is pressed\n  print \"go\"").Events[0]);
    Check(pressedArrow.Kind == TriggerKind.KeyPressed && pressedArrow.RequireName == "Up Arrow",
        "`when Up Arrow is pressed` is a key press, not a UI button press");

    var arrowWorld = new FakeWorld();
    var arrowEngine = FreshEngine(arrowWorld);
    var arrowLog = new List<string>();
    var arrowSheet = arrowEngine.AddSheet(new FakeActor("Player"),
        parser.Parse("when Left Arrow is held\n  turn left degrees 4"));
    arrowEngine.OnRawAction = (context, words) => arrowLog.Add(J(words));
    arrowWorld.SetHeld("Left Arrow", true);
    arrowEngine.Tick(0);
    arrowWorld.SetHeld("Left Arrow", false);
    arrowEngine.Tick(1);
    Check(arrowLog.Count == 1 && arrowLog[0].Contains("turn"),
        "`when Left Arrow is held` fires while the arrow is down, then stops");

    // Spawn offset: the clone appears along the spot's forward axis, XZ only.
    var offsetSpawn = ActionWords.Parse(
        RawWords(actionParser, "when Space is pressed\n  spawn Bullet at me offset 30"), null);
    Check(offsetSpawn.Verb == ActionVerb.Spawn && offsetSpawn.Thing == "Bullet" &&
          offsetSpawn.To == "me" && offsetSpawn.HasSpawnOffset &&
          offsetSpawn.AmountNode != null && offsetSpawn.SpawnOffset == 30,
        "`spawn Bullet at me offset 30` carries the spawn offset");
    var plainSpawn = ActionWords.Parse(
        RawWords(actionParser, "when Space is pressed\n  spawn Bullet at me"), null);
    Check(plainSpawn.Verb == ActionVerb.Spawn && plainSpawn.To == "me" &&
          !plainSpawn.HasSpawnOffset && plainSpawn.AmountNode == null,
        "a plain `spawn ... at me` leaves the offset at 0 (old placement)");

    PhysicsMovement.SpawnOffset(0, 1, 30, out double spawnX, out double spawnZ);
    Check(Math.Abs(spawnX) < 1e-9 && Math.Abs(spawnZ - 30) < 1e-9,
        "a spawn offset moves the clone forward along the facing");
    PhysicsMovement.SpawnOffset(1, 0, 30, out spawnX, out spawnZ);
    Check(Math.Abs(spawnX - 30) < 1e-9 && Math.Abs(spawnZ) < 1e-9,
        "the spawn offset follows the facing, not a fixed axis");
    PhysicsMovement.SpawnOffset(0, 0, 30, out spawnX, out spawnZ);
    Check(spawnX == 0 && spawnZ == 0, "a degenerate facing leaves the clone on the spot");
    PhysicsMovement.SpawnOffset(0, 1, 0, out spawnX, out spawnZ);
    Check(spawnX == 0 && spawnZ == 0, "an offset of 0 spawns exactly at the spot");

    // Knockback duration (`push ... for <n> seconds`).
    var knockPush = ActionWords.Parse(
        RawWords(actionParser, "when me touches Enemy\n  push other back strength 100 for 0.2 seconds"), null);
    Check(knockPush.Verb == ActionVerb.Push && knockPush.Dir == "back" &&
          knockPush.HasNumber && knockPush.Number == 100 &&
          Math.Abs(knockPush.Duration - 0.2) < 1e-9,
        "`push other back strength 100 for 0.2 seconds` keeps its strength and duration");
    var legacyPush = ActionWords.Parse(
        RawWords(actionParser, "when me touches Enemy\n  push other up strength 10"), null);
    Check(legacyPush.Verb == ActionVerb.Push && legacyPush.Number == 10 && legacyPush.Duration == 0,
        "`push` without `for` stays the one-tick shove");
    var bareKnock = ActionWords.Parse(
        RawWords(actionParser, "when me touches Enemy\n  push other back for 0.2 seconds"), null);
    Check(bareKnock.Verb == ActionVerb.Push && bareKnock.HasNumber && bareKnock.Number == 5 &&
          Math.Abs(bareKnock.Duration - 0.2) < 1e-9,
        "`push ... for 0.2 seconds` keeps the default strength 5");

    Check(PhysicsMovement.PushDuration(0, 0.016) > 0.0159 &&
          Math.Abs(PhysicsMovement.PushDuration(0.2, 0.016) - 0.2) < 1e-9,
        "a push lasts its authored seconds, or exactly one tick when it has none");
    Check(Math.Abs(PhysicsMovement.PushRemaining(0.2, 0.05) - 0.15) < 1e-9 &&
          PhysicsMovement.PushRemaining(0.02, 0.05) == 0,
        "a sustained push runs out of time and never goes negative");

    // Camera framing: centring needs depth = -height / tan(pitch).
    Check(CameraFraming.ClampPitch(45) == 45 && CameraFraming.ClampPitch(0) == CameraFraming.MinPitch &&
          CameraFraming.ClampPitch(180) == CameraFraming.MaxPitch,
        "camera pitch is clamped to a usable top-down range");
    Check(Math.Abs(CameraFraming.BackOffset(600, 45) - 600) < 1e-6 &&
          Math.Abs(CameraFraming.CenteredDepth(600, 45) + 600) < 1e-6,
        "at 45 degrees the camera sits as far back as it is high (target centred)");
    Check(Math.Abs(CameraFraming.BackOffset(600, 90)) < 1e-9 &&
          Math.Abs(CameraFraming.CenteredDepth(600, 90)) < 1e-9,
        "a straight-down view sits directly above the target");
    Check(Math.Abs(CameraFraming.CenteredDepth(600, 26.56505117707799) + 1200) < 1e-3,
        "a shallower pitch needs a bigger backward offset");
    Check(Math.Abs(CameraFraming.CenteredDepth(-5, 45)) < 1e-9,
        "a negative height cannot flip the camera in front of the target");

    // The TK4 sprint sheet leans on the walk/sprint branches being mutually exclusive: whether a
    // body line like `Left Shift is held` gates correctly decides if Shift does anything at all.
    var sprGateWorld = new FakeWorld();
    var sprGateEngine = FreshEngine(sprGateWorld);
    var sprGateLog = new List<string>();
    var sprGateActor = new FakeActor("SprintGate");
    sprGateEngine.AddSheet(sprGateActor, parser.Parse(
        "when Up Arrow is held\n  unless Left Shift is held\n  move forward speed 45\n" +
        "when Up Arrow is held\n  Left Shift is held\n  my Stamina is higher than 0\n  move forward speed 90\n" +
        "when Up Arrow is held\n  Left Shift is held\n  my Stamina is 0\n  move forward speed 45"));
    sprGateEngine.Variables.SetLocal(sprGateActor, "Stamina", ValueBox.OfNumber(100));
    sprGateEngine.OnRawAction = (context, words) => sprGateLog.Add(J(words));
    sprGateWorld.SetHeld("Up Arrow", true);
    sprGateEngine.Tick(0);
    Check(sprGateLog.Count == 1 && sprGateLog[0].Contains("45"),
        "walking: only the walk branch runs while Shift is up");
    sprGateLog.Clear();
    sprGateWorld.SetHeld("Left Shift", true);
    sprGateEngine.Tick(1);
    Check(sprGateLog.Count == 1 && sprGateLog[0].Contains("90"),
        "`Left Shift is held` as a body condition switches the sheet to the sprint branch");
    sprGateLog.Clear();
    sprGateEngine.Variables.SetLocal(sprGateActor, "Stamina", ValueBox.OfNumber(0));
    sprGateEngine.Tick(2);
    Check(sprGateLog.Count == 1 && sprGateLog[0].Contains("45"),
        "an empty stamina bar drops a sprinting player back to walk pace (never a standstill)");

    // Two conditions in one block stack with AND: the lower one used to overwrite the upper
    // one, so a sprint block gated on `Left Shift is held` ran on stamina alone.
    var andWorld = new FakeWorld();
    var andEngine = FreshEngine(andWorld);
    var andLog = new List<string>();
    andEngine.AddSheet(new FakeActor("AndPair"), parser.Parse(
        "when every 1 seconds\n  A is held\n  B is held\n  print \"both\""));
    andEngine.OnRawAction = (context, words) => andLog.Add(J(words));
    andWorld.SetHeld("A", true);
    andEngine.Tick(1);
    Check(andLog.Count == 0, "one true condition does not open a two-condition block");
    andWorld.SetHeld("B", true);
    andEngine.Tick(2);
    Check(andLog.Count == 1, "stacked conditions run the block once both are true");
    Check(parser.Parse("when every 1 seconds\n  A is held\n  B is held\n  print \"both\"\n  otherwise\n  print \"not both\"") != null,
        "`otherwise` still pairs with the nearest condition of a stacked block");

    // `my spawnedFrom` is computed, and a condition must read the same value an expression does:
    // the TK4 bullet gates its flight on it, and a stale (unset) read kept every clone still.
    var fromWorld = new FakeWorld();
    var fromEngine = FreshEngine(fromWorld);
    var fromLog = new List<string>();
    var spawnedCopy = new FakeActor("Bullet");
    var sceneTemplate = new FakeActor("BulletTemplate");
    fromEngine.OnRawAction = (context, words) => fromLog.Add(context.Me == spawnedCopy ? "copy" : "template");
    var fromProgram = parser.Parse("when every 0 seconds\n  my spawnedFrom is \"Bullet\"\n  move forward speed 80");
    fromEngine.AddSheet(spawnedCopy, fromProgram);
    fromEngine.AddSheet(sceneTemplate, fromProgram);
    fromEngine.MarkSpawned(spawnedCopy, "Bullet");
    fromEngine.Tick(0);
    fromEngine.Tick(1);
    Check(fromLog.Count == 2 && fromLog.TrueForAll(entry => entry == "copy"),
        "`my spawnedFrom is \"Bullet\"` opens for the spawned copy only (conditions read it like values)");

    // Digit triggers are top-row keys (`D1`), never button presses: `when 1 is pressed` must be
    // a key trigger so a UI button named "1" cannot swallow it.
    var digitTrigger = TriggerClassifier.Classify(parser.Parse("when 1 is pressed\n  print \"one\"").Events[0]);
    Check(digitTrigger.Kind == TriggerKind.KeyPressed && digitTrigger.RequireName == "1" && digitTrigger.Key == "D1",
        "`when 1 is pressed` is a key press on the top-row 1 key (polled as D1)");
    var buttonTrigger = TriggerClassifier.Classify(parser.Parse("when PlayButton is pressed\n  print \"go\"").Events[0]);
    Check(buttonTrigger.Kind == TriggerKind.ButtonPressed && buttonTrigger.RequireName == "PlayButton",
        "`when PlayButton is pressed` stays a button press");

    actionHint = "";
    var unquotedPrefab = ActionWords.Parse(LooseWords("spawn", "prefab", "Hero", "at", "SpawnPoint"), GrabHint);
    Check(unquotedPrefab.Verb == ActionVerb.Unknown && actionHint.Contains("spawn prefab"),
        "an unquoted `spawn prefab` explains the shape");

    // Unique names: a second identical spawn becomes "Hero 2", never another "Hero".
    var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    string firstName = SpawnNames.Unique("Hero", takenNames.Contains);
    takenNames.Add(firstName);
    string secondName = SpawnNames.Unique("Hero", takenNames.Contains);
    takenNames.Add(secondName);
    string thirdName = SpawnNames.Unique("Hero", takenNames.Contains);
    Check(firstName == "Hero" && secondName == "Hero 2" && thirdName == "Hero 3",
        "spawned instances keep the asset name, then count up (Hero, Hero 2, Hero 3)");
    Check(SpawnNames.Unique("", _ => false) == "Spawned", "an unnamed spawn falls back to \"Spawned\"");
    Check(SpawnNames.Unique("Hero", name => name.Equals("hero", StringComparison.OrdinalIgnoreCase)) == "Hero 2",
        "the taken-name check is whatever the caller provides (case-insensitive in the bridge)");

    // `my spawnedFrom` reads what produced the actor.
    var spawnedFromRead = parser.Parse("when me appears\n  set X to my spawnedFrom\n");
    Check(spawnedFromRead.Events.Count == 1, "`my spawnedFrom` parses as a value");
    var spawnedFromSet = (SetNode)spawnedFromRead.Events[0].Body[0].Statement!;
    Check(spawnedFromSet.Value is VariableValue sfv && sfv.Owner == "my" && sfv.Name == "spawnedFrom",
        "`my spawnedFrom` parses as a `my` variable read of `spawnedFrom`");

    var sfWorld = new FakeWorld();
    var sfActor = new FakeActor("Hero 2");
    sfWorld.AddActor(sfActor);
    var sfEngine = FreshEngine(sfWorld);
    sfEngine.MarkSpawned(sfActor, "Hero");
    sfEngine.AddSheet(sfActor, parser.Parse(@"when me appears
  set Origin to my spawnedFrom
  set SfState to 1"));
    sfEngine.Tick(0);
    Check(sfEngine.Variables.GetGlobal("SfState").AsNumber() == 1,
        "a spawned instance's `when me appears` still fires (its sheet attaches and runs)");
    Check(sfEngine.Variables.GetGlobal("Origin").AsText() == "Hero",
        "`my spawnedFrom` reads the prefab name on a renamed instance (\"Hero 2\" -> \"Hero\")");

    var sfWorld2 = new FakeWorld();
    var sfPlain = new FakeActor("Door");
    sfWorld2.AddActor(sfPlain);
    var sfEngine2 = FreshEngine(sfWorld2);
    sfEngine2.AddSheet(sfPlain, parser.Parse("when me appears\n  set Origin to my spawnedFrom"));
    sfEngine2.Tick(0);
    Check(sfEngine2.Variables.GetGlobal("Origin").AsText() == "",
        "`my spawnedFrom` is empty for an actor that was placed in the scene");
    sfEngine.RemoveActorSheet(sfActor);
    Check(sfEngine.SpawnedFrom(sfActor) == "", "removing an actor's sheet forgets its spawn source");

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

    // ------------------------------------------------------------------
    // Task 15 Part D: Tween / Animation helpers
    // ------------------------------------------------------------------

    // Trigger: `when tween <thing> ends`.
    var tweenEndProg = parser.Parse("when tween PausePanel ends\n  set PanelDone to 1\n");
    var tweenEndDesc = TriggerClassifier.Classify(tweenEndProg.Events[0]);
    Check(tweenEndDesc.Kind == TriggerKind.TweenEnd && tweenEndDesc.RequireName == "PausePanel",
        "`when tween PausePanel ends` classifies to TweenEnd");
    var tweenMeDesc = TriggerClassifier.Classify(parser.Parse("when tween me ends\n  set Mine to 1\n").Events[0]);
    Check(tweenMeDesc.Kind == TriggerKind.TweenEnd && tweenMeDesc.RequireName == "me",
        "`when tween me ends` classifies to TweenEnd with `me`");

    // Actions: position / alpha / scale / color and the easing qualifier.
    var tweenPositionProg = parser.Parse("when W is pressed\n  tween PausePanel position to CenterPoint in 0.5\n");
    var tweenPosition = ActionWords.Parse(tweenPositionProg.Events[0].Body[0].Words, null);
    Check(tweenPosition.Verb == ActionVerb.Tween && tweenPosition.HasTween &&
          tweenPosition.Thing == "PausePanel" && tweenPosition.TweenProperty == TweenKind.Position &&
          tweenPosition.TweenTarget == "CenterPoint" && tweenPosition.Duration == 0.5 &&
          tweenPosition.Easing == TweenEasing.Linear,
        "`tween ... position to ... in 0.5` parses to Tween/Position");

    var tweenAlpha = ActionWords.Parse(parser.Parse("when W is pressed\n  tween PausePanel alpha to 0 in 0.3\n").Events[0].Body[0].Words, null);
    Check(tweenAlpha.Verb == ActionVerb.Tween && tweenAlpha.TweenProperty == TweenKind.Alpha &&
          tweenAlpha.TweenValue is NumberValue alphaNum && alphaNum.Value == 0 &&
          tweenAlpha.Duration == 0.3,
        "`tween ... alpha to 0 in 0.3` parses to Tween/Alpha");

    var tweenScale = ActionWords.Parse(parser.Parse("when W is pressed\n  tween PausePanel scale to 1.2 in 0.25\n").Events[0].Body[0].Words, null);
    Check(tweenScale.Verb == ActionVerb.Tween && tweenScale.TweenProperty == TweenKind.Scale &&
          tweenScale.TweenValue is NumberValue scaleNum && scaleNum.Value == 1.2,
        "`tween ... scale to 1.2 in 0.25` parses to Tween/Scale");

    var tweenColor = ActionWords.Parse(parser.Parse("when W is pressed\n  tween PausePanel color to \"1 0 0\" in 0.5\n").Events[0].Body[0].Words, null);
    Check(tweenColor.Verb == ActionVerb.Tween && tweenColor.TweenProperty == TweenKind.Color &&
          tweenColor.TweenColor == "1 0 0" && tweenColor.Duration == 0.5,
        "`tween ... color to \"1 0 0\" in 0.5` parses to Tween/Color");

    var tweenEase = ActionWords.Parse(parser.Parse("when W is pressed\n  tween PausePanel alpha to 1 in 0.4 easing ease out\n").Events[0].Body[0].Words, null);
    Check(tweenEase.Verb == ActionVerb.Tween && tweenEase.Easing == TweenEasing.EaseOut,
        "`easing ease out` parses to TweenEasing.EaseOut");
    var tweenEaseInOut = ActionWords.Parse(parser.Parse("when W is pressed\n  tween PausePanel alpha to 1 in 0.4 easing ease in out\n").Events[0].Body[0].Words, null);
    Check(tweenEaseInOut.Verb == ActionVerb.Tween && tweenEaseInOut.Easing == TweenEasing.EaseInOut,
        "`easing ease in out` parses to TweenEasing.EaseInOut");

    // Pure tween timing/interpolation.
    var positionTrack = new TweenTrack
    {
        Kind = TweenKind.Position,
        Duration = 2,
        FromX = 0, FromY = 0, FromZ = 0,
        ToX = 10, ToY = 20, ToZ = -10,
    };
    positionTrack.Advance(1);
    Check(Math.Abs(positionTrack.X - 5) < 0.001 && Math.Abs(positionTrack.Y - 10) < 0.001 &&
          Math.Abs(positionTrack.Z + 5) < 0.001 && !positionTrack.Done,
        "a position tween is halfway at half the duration");
    Check(positionTrack.Advance(1) && Math.Abs(positionTrack.X - 10) < 0.001 &&
          Math.Abs(positionTrack.Y - 20) < 0.001 && Math.Abs(positionTrack.Z + 10) < 0.001,
        "a position tween lands exactly on the target and completes");
    Check(Math.Abs(TweenMath.Ease(0.5, TweenEasing.EaseIn) - 0.25) < 0.001 &&
          Math.Abs(TweenMath.Ease(0.5, TweenEasing.EaseOut) - 0.75) < 0.001 &&
          Math.Abs(TweenMath.Ease(0.5, TweenEasing.EaseInOut) - 0.5) < 0.001 &&
          Math.Abs(TweenMath.Ease(0.5, TweenEasing.Linear) - 0.5) < 0.001,
        "the four easing curves map 0.5 correctly");

    var alphaTrack = new TweenTrack { Kind = TweenKind.Alpha, Duration = 1, FromValue = 1, ToValue = 0 };
    alphaTrack.Advance(0.5);
    Check(Math.Abs(alphaTrack.Value - 0.5) < 0.001, "an alpha tween interpolates 1 to 0");
    Check(alphaTrack.Advance(0.5) && Math.Abs(alphaTrack.Value) < 0.001, "an alpha tween reaches 0");

    var colorTrack = new TweenTrack
    {
        Kind = TweenKind.Color,
        Duration = 1,
        FromR = 1, FromG = 1, FromB = 1, FromA = 1,
        ToR = 0, ToG = 0.5, ToB = 0, ToA = 0.5,
    };
    colorTrack.Advance(0.5);
    Check(Math.Abs(colorTrack.R - 0.5) < 0.001 && Math.Abs(colorTrack.G - 0.75) < 0.001 &&
          Math.Abs(colorTrack.B - 0.5) < 0.001 && Math.Abs(colorTrack.A - 0.75) < 0.001,
        "a color tween interpolates all four channels");
    Check(colorTrack.Advance(0.5) && Math.Abs(colorTrack.R) < 0.001 && Math.Abs(colorTrack.G - 0.5) < 0.001,
        "a color tween lands on the target color");

    var instantTrack = new TweenTrack { Kind = TweenKind.Scale, Duration = 0, FromX = 1, FromY = 1, FromZ = 1, ToX = 2, ToY = 2, ToZ = 2 };
    Check(instantTrack.Advance(0) && Math.Abs(instantTrack.X - 2) < 0.001,
        "a zero-duration tween snaps to its target and completes immediately");

    // Runtime: the end signal is broadcast, named listeners work from any sheet, and the
    // `me` form fires only on the tweened actor's own sheet.
    var tweenBox = new FakeActor("Box");
    var tweenBox2 = new FakeActor("Box2");
    var tweenWorld = new FakeWorld();
    tweenWorld.AddActor(tweenBox);
    tweenWorld.AddActor(tweenBox2);
    var tweenEngine = FreshEngine(tweenWorld);
    tweenEngine.AddSheet(tweenBox, parser.Parse("when tween me ends\n  set my Ends to Ends + 1\n"));
    tweenEngine.AddSheet(tweenBox2, parser.Parse("when tween me ends\n  set my Ends to Ends + 1\n"));
    tweenEngine.AddSheet(new GlobalHost(), parser.Parse("when tween Box ends\n  set BoxEnded to 1\n"));
    tweenEngine.Tick(0);
    tweenEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.TweenEnd, Name = "Box", Other = tweenBox });
    tweenEngine.Tick(1);
    Check(tweenEngine.Variables.GetLocal(tweenBox, "Ends").AsNumber() == 1,
        "`when tween me ends` fires on the tweened actor's sheet");
    Check(tweenEngine.Variables.GetLocal(tweenBox2, "Ends").Kind == ValueKind.None,
        "`when tween me ends` ignores another actor's tween");
    Check(tweenEngine.Variables.GetGlobal("BoxEnded").AsNumber() == 1,
        "`when tween Box ends` fires on a named listener (the Game sheet)");

    // ------------------------------------------------------------------
    // Task 15 Part E: Focus & keyboard/gamepad navigation
    // ------------------------------------------------------------------

    // The `navigate` word accepts the four directions and rejects anything else.
    foreach (string directionWord in new[] { "up", "down", "left", "right" })
    {
        var navigateProgram = parser.Parse($"when W is pressed\n  navigate {directionWord}\n");
        var navigateSpec = ActionWords.Parse(navigateProgram.Events[0].Body[0].Words, null);
        Check(navigateSpec.Verb == ActionVerb.Navigate && navigateSpec.Dir == directionWord,
            $"`navigate {directionWord}` parses to ActionVerb.Navigate");
    }
    string navigateHint = null;
    var badNavigate = ActionWords.Parse(
        parser.Parse("when W is pressed\n  navigate sideways\n").Events[0].Body[0].Words,
        message => navigateHint = message);
    Check(badNavigate.Verb == ActionVerb.Unknown &&
          navigateHint != null && navigateHint.Contains("down", StringComparison.OrdinalIgnoreCase),
        "`navigate sideways` is a friendly unknown action that lists the directions");
    Check(FocusNavigation.ParseDirection("Up") == FocusDirection.Up &&
          FocusNavigation.ParseDirection("nowhere") == null,
        "navigate directions parse case-insensitively and reject unknown words");

    // Pure directional navigation over a 2x2 menu (screen coordinates, Y down).
    FocusCandidate MenuButton(string name, double x, double y) => new FocusCandidate
    {
        Subject = name,
        Name = name,
        CenterX = x,
        CenterY = y,
        HalfWidth = 60,
        HalfHeight = 20,
    };
    var buttonA = MenuButton("PlayButton", 100, 100);
    var buttonB = MenuButton("OptionsButton", 300, 100);
    var buttonC = MenuButton("ResumeButton", 100, 200);
    var buttonD = MenuButton("QuitButton", 300, 200);
    var menu = new List<FocusCandidate> { buttonA, buttonB, buttonC, buttonD };
    Check(ReferenceEquals(FocusNavigation.Choose(menu, buttonA, FocusDirection.Right), buttonB),
        "navigate right moves focus to the nearest button on the right");
    Check(ReferenceEquals(FocusNavigation.Choose(menu, buttonB, FocusDirection.Down), buttonD),
        "navigate down follows the button column");
    Check(ReferenceEquals(FocusNavigation.Choose(menu, buttonD, FocusDirection.Left), buttonC),
        "navigate left follows the button row");
    Check(ReferenceEquals(FocusNavigation.Choose(menu, buttonC, FocusDirection.Up), buttonA),
        "navigate up returns to the button above");
    Check(FocusNavigation.Choose(menu, buttonA, FocusDirection.Left) == null &&
          FocusNavigation.Choose(menu, buttonD, FocusDirection.Down) == null,
        "navigation stops at the edge instead of wrapping");
    Check(ReferenceEquals(FocusNavigation.First(menu), buttonA),
        "the first focus picks the topmost-leftmost button");

    // A control further straight below beats a closer one far off to the side.
    var straightDown = MenuButton("StraightDown", 100, 320);
    var offToTheSide = MenuButton("OffToTheSide", 420, 130);
    Check(ReferenceEquals(FocusNavigation.Choose(new List<FocusCandidate> { offToTheSide, straightDown }, buttonA, FocusDirection.Down), straightDown),
        "navigate down prefers the button in the same column over a closer off-axis one");

    // Focus enter/exit is broadcast by name, so both `when me is focused` on the
    // control's own sheet and a named listener on the Game sheet fire.
    var focusPlay = new FakeActor("PlayButton");
    var focusQuit = new FakeActor("QuitButton");
    var focusWorld = new FakeWorld();
    focusWorld.AddActor(focusPlay);
    focusWorld.AddActor(focusQuit);
    var focusEngine = FreshEngine(focusWorld);
    focusEngine.AddSheet(focusPlay, parser.Parse("when me is focused\n  set my Focused to 1\n"));
    focusEngine.AddSheet(focusQuit, parser.Parse("when me is focused\n  set my Focused to 1\n"));
    focusEngine.AddSheet(new GlobalHost(), parser.Parse(
        "when PlayButton is focused\n  set MenuFocus to 1\n" +
        "when PlayButton loses focus\n  set MenuFocus to 0\n"));
    focusEngine.Tick(0);
    focusEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.Focused, Name = "PlayButton", Other = focusPlay });
    focusEngine.Tick(1);
    Check(focusEngine.Variables.GetLocal(focusPlay, "Focused").AsNumber() == 1,
        "`when me is focused` fires on the focused control's own sheet");
    Check(focusEngine.Variables.GetLocal(focusQuit, "Focused").Kind == ValueKind.None,
        "`when me is focused` ignores another control's focus");
    Check(focusEngine.Variables.GetGlobal("MenuFocus").AsNumber() == 1,
        "`when <thing> is focused` fires on a named listener (the Game sheet)");
    focusEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.LostFocus, Name = "PlayButton", Other = focusPlay });
    focusEngine.Tick(2);
    Check(focusEngine.Variables.GetGlobal("MenuFocus").AsNumber() == 0,
        "`when <thing> loses focus` fires when focus leaves the control");

// ------------------------------------------------------------------
// Task 15 Part F: Data-binding syntactic sugar
// ------------------------------------------------------------------

Check(WordRegistry.All.Any(e => e.Kind == "directive" && e.Word == "bind <label> to <variable>") &&
      WordRegistry.All.Any(e => e.Kind == "directive" && e.Word == "bind progress of <thing> to <variable>"),
    "registry documents both `bind` forms");

// `bind <label> to <variable>` expands at parse time into one change event.
var bindProgram = parser.Parse("bind ScoreLabel to Score\n");
Check(bindProgram.Events.Count == 1, "`bind ScoreLabel to Score` expands to one event");
var bindDesc = TriggerClassifier.Classify(bindProgram.Events[0]);
Check(bindDesc.Kind == TriggerKind.WatchChanges && bindDesc.WatchName == "Score" && bindDesc.WatchOwner.Length == 0,
    "the expanded bind trigger is `when Score changes`");
var bindStatement = bindProgram.Events[0].Body[0].Statement as SetTextNode;
Check(bindStatement != null && bindStatement.Target == "ScoreLabel" &&
      bindStatement.Value is VariableValue bindVariable && bindVariable.Name == "Score",
    "the expanded bind body is `set text of ScoreLabel to Score`");

// `bind progress of <thing> to <variable>` fills a ProgressBar instead.
var bindProgressProgram = parser.Parse("bind progress of HealthBar to Health\n");
var bindProgressStatement = bindProgressProgram.Events[0].Body[0].Statement as SetProgressNode;
Check(bindProgressProgram.Events.Count == 1 && bindProgressStatement != null &&
      bindProgressStatement.Thing == "HealthBar" &&
      bindProgressStatement.Value is VariableValue bindProgressVariable && bindProgressVariable.Name == "Health",
    "`bind progress of HealthBar to Health` expands to `when Health changes -> set progress of HealthBar`");

// A per-object variable binds through `my`.
var bindMineProgram = parser.Parse("bind InfoLabel to my health\n");
var bindMineDesc = TriggerClassifier.Classify(bindMineProgram.Events[0]);
Check(bindMineDesc.Kind == TriggerKind.WatchChanges && bindMineDesc.WatchOwner == "my" && bindMineDesc.WatchName == "health",
    "`bind InfoLabel to my health` watches the sheet's own variable");

// `format` watches every listed variable and fills the numbered placeholders.
var bindFormatProgram = parser.Parse("bind HealthLabel to Health and MaxHealth format \"Health: {0}/{1}\"\n");
Check(bindFormatProgram.Events.Count == 2,
    "a formatted bind watching two variables expands to one change event per variable");
var bindFormatStatement = bindFormatProgram.Events[0].Body[0].Statement as SetTextNode;
Check(bindFormatStatement != null && bindFormatStatement.Target == "HealthLabel" &&
      bindFormatStatement.Value is FormatValue bindFormat &&
      bindFormat.Template == "Health: {0}/{1}" && bindFormat.Args.Count == 2,
    "the formatted bind body keeps the template and both variables");
var bindSecondDesc = TriggerClassifier.Classify(bindFormatProgram.Events[1]);
Check(bindSecondDesc.Kind == TriggerKind.WatchChanges && bindSecondDesc.WatchName == "MaxHealth",
    "the second formatted bind event watches the second variable");

ExpectParseError("bind ScoreLabel\n", "control", "`bind` without `to` is a friendly error");
ExpectParseError("bind ScoreLabel to 5\n", "variable", "`bind` needs a variable, not a number");
ExpectParseError("bind ScoreLabel to Score and MaxHealth\n", "format",
    "binding several variables without a format template is a friendly error");
ExpectParseError("bind HealthLabel to Health format \"Health: {1}\"\n", "placeholder",
    "a template placeholder with no variable is a friendly error");
ExpectParseError("bind progress of HealthBar to Health format \"{0}\"\n", "progress bar",
    "a progress bar bind rejects `format`");
ExpectParseError("when game starts\n  bind ScoreLabel to Score\n", "own line",
    "an indented `bind` explains that it makes its own event");

// Runtime: the expanded events paint the control from the live variable.
var bindActor = new FakeActor("ScoreLabel");
var bindWorld = new FakeWorld();
bindWorld.AddActor(bindActor);
var bindEngine = FreshEngine(bindWorld);
var bindTexts = new Dictionary<string, string>();
var bindProgressValues = new Dictionary<string, double>();
bindEngine.OnSetText = (context, thing, value) => bindTexts[thing] = value.AsText();
bindEngine.OnSetProgress = (context, thing, value) => bindProgressValues[thing] = value.AsNumber();
bindEngine.AddSheet(bindActor, parser.Parse(
    "bind ScoreLabel to Score\n" +
    "bind progress of HealthBar to Health\n" +
    "bind HealthLabel to Health and MaxHealth format \"Health: {0}/{1}\"\n"));
bindEngine.Tick(0);
Check(bindTexts.Count == 0 && bindProgressValues.Count == 0,
    "a bind does not paint on the first tick (it starts from the change baseline)");
bindEngine.Variables.SetGlobal("Score", ValueBox.OfNumber(7));
bindEngine.Variables.SetGlobal("Health", ValueBox.OfNumber(50));
bindEngine.Variables.SetGlobal("MaxHealth", ValueBox.OfNumber(100));
bindEngine.Tick(1);
Check(bindTexts.TryGetValue("ScoreLabel", out string bindScoreText) && bindScoreText == "7",
    "a text bind writes the changed variable into its label");
Check(bindProgressValues.TryGetValue("HealthBar", out double bindHealthFill) && bindHealthFill == 50,
    "a progress bind fills its bar from the changed variable");
Check(bindTexts.TryGetValue("HealthLabel", out string bindHealthText) && bindHealthText == "Health: 50/100",
    "a formatted bind fills every placeholder");

// ------------------------------------------------------------------
// Task 18 Part C: construction & base building
// ------------------------------------------------------------------

// Trigger classification.
var buildingPlacedProg = parser.Parse("when Barracks is placed\n  set Placed to 1\n");
var buildingPlacedDesc = TriggerClassifier.Classify(buildingPlacedProg.Events[0]);
Check(buildingPlacedDesc.Kind == TriggerKind.BuildingPlaced && buildingPlacedDesc.RequireName == "Barracks",
      "`when Barracks is placed` classifies to BuildingPlaced with RequireName \"Barracks\"");

var constructionDesc = TriggerClassifier.Classify(parser.Parse("when construction completes\n  set Done to 1\n").Events[0]);
Check(constructionDesc.Kind == TriggerKind.ConstructionCompleted && constructionDesc.RequireName.Length == 0,
      "`when construction completes` classifies to ConstructionCompleted with no require name");

var myConstructionDesc = TriggerClassifier.Classify(parser.Parse("when my construction completes\n  set Done to 1\n").Events[0]);
Check(myConstructionDesc.Kind == TriggerKind.ConstructionCompleted && myConstructionDesc.RequireName == "my",
      "`when my construction completes` classifies with RequireName \"my\"");

var namedConstructionDesc = TriggerClassifier.Classify(parser.Parse("when Barracks construction completes\n  set Done to 1\n").Events[0]);
Check(namedConstructionDesc.Kind == TriggerKind.ConstructionCompleted && namedConstructionDesc.RequireName == "Barracks",
      "`when Barracks construction completes` classifies with the building name");

var buildingDestroyedDesc = TriggerClassifier.Classify(parser.Parse("when Barracks is destroyed\n  set Gone to 1\n").Events[0]);
Check(buildingDestroyedDesc.Kind == TriggerKind.BuildingDestroyed && buildingDestroyedDesc.RequireName == "Barracks",
      "`when Barracks is destroyed` classifies to BuildingDestroyed with RequireName \"Barracks\"");

// Action parsing.
var placeProg = parser.Parse(@"when game starts
  place Barracks at Mouse
  place Barracks at Mouse on grid
  place Barracks at Mouse on grid by 32
  cancel build");
var freePlaceSpec = ActionWords.Parse(((RawActionNode)placeProg.Events[0].Body[0].Statement).Words, null);
Check(freePlaceSpec.Verb == ActionVerb.PlaceBuilding && freePlaceSpec.Thing == "Barracks" &&
      freePlaceSpec.To == "Mouse" && !freePlaceSpec.OnGrid,
      "`place Barracks at Mouse` is free-form placement");
var gridPlaceSpec = ActionWords.Parse(((RawActionNode)placeProg.Events[0].Body[1].Statement).Words, null);
Check(gridPlaceSpec.Verb == ActionVerb.PlaceBuilding && gridPlaceSpec.OnGrid && !gridPlaceSpec.HasGridSize,
      "`place Barracks at Mouse on grid` opts into grid placement with no explicit size");
var sizedGridPlaceSpec = ActionWords.Parse(((RawActionNode)placeProg.Events[0].Body[2].Statement).Words, null);
Check(sizedGridPlaceSpec.Verb == ActionVerb.PlaceBuilding && sizedGridPlaceSpec.OnGrid &&
      sizedGridPlaceSpec.HasGridSize && sizedGridPlaceSpec.GridSize == 32,
      "`place Barracks at Mouse on grid by 32` keeps the explicit grid size");
var cancelSpec = ActionWords.Parse(((RawActionNode)placeProg.Events[0].Body[3].Statement).Words, null);
Check(cancelSpec.Verb == ActionVerb.CancelBuild, "`cancel build` parses to ActionVerb.CancelBuild");

var buildProgressProg = parser.Parse(@"when game starts
  set build progress to 0
  add 10 to build progress");
Check(buildProgressProg.Events[0].Body[0].Statement is SetBuildProgressNode setBuildProgressNode &&
      setBuildProgressNode.Value is NumberValue setBuildProgressValue && setBuildProgressValue.Value == 0,
      "`set build progress to 0` builds a SetBuildProgressNode");
Check(buildProgressProg.Events[0].Body[1].Statement is AddBuildProgressNode addBuildProgressNode &&
      addBuildProgressNode.Amount is NumberValue addBuildProgressValue && addBuildProgressValue.Value == 10,
      "`add 10 to build progress` builds an AddBuildProgressNode");

var namedBuildProgressProg = parser.Parse(@"when game starts
  set build progress of Barracks to 25
  add 5 to build progress of Barracks");
Check(namedBuildProgressProg.Events[0].Body[0].Statement is SetBuildProgressNode namedSetBuildProgress &&
      namedSetBuildProgress.Thing == "Barracks" && namedSetBuildProgress.Value is NumberValue namedSetValue &&
      namedSetValue.Value == 25,
      "`set build progress of Barracks to 25` keeps the named target");
Check(namedBuildProgressProg.Events[0].Body[1].Statement is AddBuildProgressNode namedAddBuildProgress &&
      namedAddBuildProgress.Thing == "Barracks" && namedAddBuildProgress.Amount is NumberValue namedAddValue &&
      namedAddValue.Value == 5,
      "`add 5 to build progress of Barracks` keeps the named target");
Check(((SetBuildProgressNode)buildProgressProg.Events[0].Body[0].Statement).Thing.Length == 0 &&
      ((AddBuildProgressNode)buildProgressProg.Events[0].Body[1].Statement).Thing.Length == 0,
      "the legacy progress forms still target the acting building");

// Condition parsing.
var underConstructionCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  Barracks is under construction").Events[0].Body[0].Words);
Check(underConstructionCondition.Kind == ConditionKind.UnderConstruction && underConstructionCondition.Thing == "Barracks",
      "`Barracks is under construction` parses to UnderConstruction");

var enoughCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  enough Minerals for Barracks").Events[0].Body[0].Words);
Check(enoughCondition.Kind == ConditionKind.EnoughFor && enoughCondition.Name == "Minerals" &&
      enoughCondition.Thing == "Barracks",
      "`enough Minerals for Barracks` parses to EnoughFor with canonical Minerals");

var buildingExistsCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  Barracks exists").Events[0].Body[0].Words);
Check(buildingExistsCondition.Kind == ConditionKind.ThingExists && buildingExistsCondition.Thing == "Barracks",
      "`Barracks exists` keeps using ThingExists");

var progressCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  build progress is higher than 100").Events[0].Body[0].Words);
Check(progressCondition.Kind == ConditionKind.Higher && progressCondition.Owner == "my" &&
      progressCondition.Name == "BuildProgress" &&
      progressCondition.Right is NumberValue progressRight && progressRight.Value == 100,
      "`build progress is higher than 100` reads the mirrored BuildProgress local");

// Zero-regression spot checks: the new branches must not steal existing lines.
var legacyConstructionProg = parser.Parse(@"when game starts
  set Score to 0
  add 1 to Score
  add 50 to Minerals");
Check(legacyConstructionProg.Events[0].Body[0].Statement is SetNode legacySet && legacySet.Target.Name == "Score",
      "`set Score to 0` still builds a SetNode");
Check(legacyConstructionProg.Events[0].Body[1].Statement is AddNode legacyAdd && legacyAdd.Target.Name == "Score",
      "`add 1 to Score` still targets Score");
Check(legacyConstructionProg.Events[0].Body[2].Statement is AddNode legacyMinerals && legacyMinerals.Target.Name == "Minerals",
      "`add 50 to Minerals` still targets Minerals");
Check(TriggerClassifier.Classify(parser.Parse("when Barracks is selected\n  set X to 1\n").Events[0]).Kind == TriggerKind.Selected,
      "`when Barracks is selected` still classifies to Selected");

// Construction state + mirrored local.
var buildVars = new EventVariables();
var builds = new ConstructionManager();
var barracksActor = new FakeActor("Barracks");
builds.RegisterPlaced(barracksActor, "Barracks", buildVars);
builds.SetProgress(buildVars, barracksActor, 50);
Check(builds.ProgressOf(barracksActor) == 50 && builds.IsUnderConstruction(barracksActor),
      "RegisterPlaced + SetProgress tracks 50% construction");
Check(buildVars.GetLocal(barracksActor, "BuildProgress").AsNumber() == 50,
      "construction progress mirrors into the my BuildProgress local");
builds.AddProgress(buildVars, barracksActor, -100);
Check(builds.ProgressOf(barracksActor) == 0, "AddProgress clamps construction progress at 0");

// Completion fires once, then stays quiet.
var completionWorld = new FakeWorld();
completionWorld.AddActor(barracksActor);
var completionEngine = FreshEngine(completionWorld);
completionEngine.AddSheet(new GlobalHost(), parser.Parse(
    "when construction completes\n  set Completed to Completed + 1\n"));
completionEngine.Construction.RegisterPlaced(barracksActor, "Barracks", completionEngine.Variables);
completionEngine.Construction.SetProgress(completionEngine.Variables, barracksActor, 100);
completionEngine.Tick(1);
Check(completionEngine.Variables.GetGlobal("Completed").AsNumber() == 1,
      "`when construction completes` fires when progress reaches 100");
completionEngine.Tick(2);
Check(completionEngine.Variables.GetGlobal("Completed").AsNumber() == 1,
      "completion does not refire while progress stays at 100");

// `when my construction completes` filters the broadcast to the sheet's own building.
var ownCompletionWorld = new FakeWorld();
var ownCompletionA = new FakeActor("Barracks");
var ownCompletionB = new FakeActor("Factory");
ownCompletionWorld.AddActor(ownCompletionA);
ownCompletionWorld.AddActor(ownCompletionB);
var ownCompletionEngine = FreshEngine(ownCompletionWorld);
ownCompletionEngine.AddSheet(ownCompletionA, parser.Parse("when my construction completes\n  set my Done to my Done + 1"));
ownCompletionEngine.AddSheet(ownCompletionB, parser.Parse("when my construction completes\n  set my Done to my Done + 1"));
ownCompletionEngine.Construction.RegisterPlaced(ownCompletionA, "Barracks", ownCompletionEngine.Variables);
ownCompletionEngine.Construction.RegisterPlaced(ownCompletionB, "Factory", ownCompletionEngine.Variables);
ownCompletionEngine.Construction.SetProgress(ownCompletionEngine.Variables, ownCompletionA, 100);
ownCompletionEngine.Construction.SetProgress(ownCompletionEngine.Variables, ownCompletionB, 100);
ownCompletionEngine.Tick(1);
Check(ownCompletionEngine.Variables.GetLocal(ownCompletionA, "Done").AsNumber() == 1 &&
      ownCompletionEngine.Variables.GetLocal(ownCompletionB, "Done").AsNumber() == 1,
      "`when my construction completes` fires once on each building's own sheet");

// `when <building> construction completes` filters by template name.
var namedCompletionWorld = new FakeWorld();
var namedCompletionBarracks = new FakeActor("Barracks");
var namedCompletionFactory = new FakeActor("Factory");
namedCompletionWorld.AddActor(namedCompletionBarracks);
namedCompletionWorld.AddActor(namedCompletionFactory);
var namedCompletionEngine = FreshEngine(namedCompletionWorld);
namedCompletionEngine.AddSheet(new GlobalHost(), parser.Parse(
    "when Barracks construction completes\n  set BarracksDone to BarracksDone + 1\n"));
namedCompletionEngine.Construction.RegisterPlaced(namedCompletionBarracks, "Barracks", namedCompletionEngine.Variables);
namedCompletionEngine.Construction.RegisterPlaced(namedCompletionFactory, "Factory", namedCompletionEngine.Variables);
namedCompletionEngine.Construction.SetProgress(namedCompletionEngine.Variables, namedCompletionBarracks, 100);
namedCompletionEngine.Construction.SetProgress(namedCompletionEngine.Variables, namedCompletionFactory, 100);
namedCompletionEngine.Tick(1);
Check(namedCompletionEngine.Variables.GetGlobal("BarracksDone").AsNumber() == 1,
      "`when Barracks construction completes` ignores a different building's completion");

// Cancel removes state + mirrored local; destruction removes state.
var cancelVars = new EventVariables();
var cancelManager = new ConstructionManager();
var cancelActor = new FakeActor("Barracks");
cancelManager.RegisterPlaced(cancelActor, "Barracks", cancelVars);
cancelManager.SetProgress(cancelVars, cancelActor, 30);
Check(cancelManager.Cancel(cancelVars, cancelActor), "Cancel reports that the building was under construction");
Check(!cancelManager.IsUnderConstruction(cancelActor) && !cancelVars.HasLocal(cancelActor, "BuildProgress"),
      "Cancel clears construction state and the mirrored BuildProgress local");

var destroyedManager = new ConstructionManager();
var destroyedActor = new FakeActor("Barracks");
destroyedManager.RegisterPlaced(destroyedActor, "Barracks", buildVars);
destroyedManager.Destroyed(buildVars, destroyedActor);
Check(!destroyedManager.IsUnderConstruction(destroyedActor) &&
      !buildVars.HasLocal(destroyedActor, "BuildProgress"),
      "Destroyed clears construction state and the mirrored local");

// Engine-level placed signals and the `me` form.
var placedEngine = FreshEngine(new FakeWorld());
placedEngine.AddSheet(new GlobalHost(), parser.Parse(
    "when Barracks is placed\n  set Placed to Placed + 1\n"));
placedEngine.Tick(0);
var placedActor = new FakeActor("Barracks");
placedEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.BuildingPlaced, Name = "Barracks", Other = placedActor });
placedEngine.Tick(1);
Check(placedEngine.Variables.GetGlobal("Placed").AsNumber() == 1,
      "`when Barracks is placed` fires on a BuildingPlaced broadcast");

var mePlacedEngine = FreshEngine(new FakeWorld());
var mePlacedA = new FakeActor("Barracks");
var mePlacedB = new FakeActor("Barracks");
mePlacedEngine.AddSheet(mePlacedA, parser.Parse("when me is placed\n  set my Placed to 1\n"));
mePlacedEngine.AddSheet(mePlacedB, parser.Parse("when me is placed\n  set my Placed to 1\n"));
mePlacedEngine.Tick(0);
mePlacedEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.BuildingPlaced, Name = "Barracks", Other = mePlacedA });
mePlacedEngine.Tick(1);
Check(mePlacedEngine.Variables.GetLocal(mePlacedA, "Placed").AsNumber() == 1 &&
      mePlacedEngine.Variables.GetLocal(mePlacedB, "Placed").IsNone(),
      "`when me is placed` fires only for the placed actor's own sheet");

// `enough Minerals for <building>` is data-driven.
var enoughEngine = FreshEngine(new FakeWorld());
enoughEngine.AddSheet(new GlobalHost(), parser.Parse(@"when every 1 seconds
  enough Minerals for Barracks
  set Enough to Enough + 1"));
enoughEngine.Tick(1);
Check(enoughEngine.Variables.GetGlobal("Enough").AsNumber() == 1,
      "`enough Minerals for Barracks` passes when the building declares no cost");
enoughEngine.Variables.SetGlobal("BarracksCost", ValueBox.OfNumber(150));
enoughEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(100));
enoughEngine.Tick(2);
Check(enoughEngine.Variables.GetGlobal("Enough").AsNumber() == 1,
      "`enough Minerals for Barracks` blocks while Minerals are short");
enoughEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(150));
enoughEngine.Tick(3);
Check(enoughEngine.Variables.GetGlobal("Enough").AsNumber() == 2,
      "`enough Minerals for Barracks` passes once the cost is covered");

// `<building> is under construction` flips after RegisterPlaced.
var underWorld = new FakeWorld();
var underActor = new FakeActor("Barracks");
underWorld.AddActor(underActor);
var underEngine = FreshEngine(underWorld);
underEngine.AddSheet(underActor, parser.Parse("when me appears\n  set my Ready to 1\n"));
underEngine.AddSheet(new GlobalHost(), parser.Parse(@"when every 1 seconds
  Barracks is under construction
  set Under to Under + 1"));
underEngine.Tick(1);
Check(underEngine.Variables.GetGlobal("Under").IsNone(),
      "`Barracks is under construction` blocks before placement");
underEngine.Construction.RegisterPlaced(underActor, "Barracks", underEngine.Variables);
underEngine.Tick(2);
Check(underEngine.Variables.GetGlobal("Under").AsNumber() == 1,
      "`Barracks is under construction` passes after placement");

// `build progress is higher than <n>` and the set/add dispatch.
var progressEngine = FreshEngine(new FakeWorld());
var progressActor = new FakeActor("Barracks");
progressEngine.AddSheet(progressActor, parser.Parse(@"when every 1 seconds
  build progress is higher than 40
  set my High to High + 1"));
progressEngine.Construction.RegisterPlaced(progressActor, "Barracks", progressEngine.Variables);
progressEngine.Construction.SetProgress(progressEngine.Variables, progressActor, 50);
progressEngine.Tick(1);
Check(progressEngine.Variables.GetLocal(progressActor, "High").AsNumber() == 1,
      "`build progress is higher than 40` passes at 50");
progressEngine.Construction.SetProgress(progressEngine.Variables, progressActor, 20);
progressEngine.Tick(2);
Check(progressEngine.Variables.GetLocal(progressActor, "High").AsNumber() == 1,
      "`build progress is higher than 40` blocks at 20");

var dispatchEngine = FreshEngine(new FakeWorld());
var dispatchActor = new FakeActor("Barracks");
dispatchEngine.AddSheet(dispatchActor, parser.Parse(@"when me appears
  set build progress to 50
  add 10 to build progress"));
dispatchEngine.Construction.RegisterPlaced(dispatchActor, "Barracks", dispatchEngine.Variables);
dispatchEngine.Tick(0);
Check(dispatchEngine.Construction.ProgressOf(dispatchActor) == 60,
      "`set build progress` and `add to build progress` dispatch to the ConstructionManager");

// The named forms let a global sheet drive a specific building.
var namedDispatchWorld = new FakeWorld();
var namedDispatchActor = new FakeActor("Barracks");
namedDispatchWorld.AddActor(namedDispatchActor);
var namedDispatchEngine = FreshEngine(namedDispatchWorld);
namedDispatchEngine.AddSheet(namedDispatchActor, parser.Parse("when me appears\n  set my Ready to 1"));
namedDispatchEngine.AddSheet(new GlobalHost(), parser.Parse(@"when every 1 seconds
  set build progress of Barracks to 25
  add 5 to build progress of Barracks"));
namedDispatchEngine.Construction.RegisterPlaced(namedDispatchActor, "Barracks", namedDispatchEngine.Variables);
namedDispatchEngine.Tick(1);
Check(namedDispatchEngine.Construction.ProgressOf(namedDispatchActor) == 30 &&
      namedDispatchEngine.Variables.GetLocal(namedDispatchActor, "BuildProgress").AsNumber() == 30,
      "`set/add build progress of Barracks` from a global sheet targets that building");

// Without an acting building and no named target, warn once instead of silently doing nothing.
var missingProgressEngine = FreshEngine(new FakeWorld());
int progressWarnings = 0;
missingProgressEngine.OnRuntimeWarning = message =>
{
    if (message.Contains("acting building", StringComparison.OrdinalIgnoreCase))
        progressWarnings++;
};
missingProgressEngine.AddSheet(new GlobalHost(), parser.Parse(@"when every 1 seconds
  set build progress to 10
  add 5 to build progress"));
missingProgressEngine.Tick(1);
missingProgressEngine.Tick(2);
Check(progressWarnings == 1,
      "a build-progress line with no acting building warns once");

// Grid helpers.
Check(ConstructionManager.SnapToGrid(0, 64) == 32 && ConstructionManager.SnapToGrid(63, 64) == 32 &&
      ConstructionManager.SnapToGrid(65, 64) == 96,
      "SnapToGrid returns cell centres");
Check(ConstructionManager.CellOf(63, 0, 64) == (0, 0) && ConstructionManager.CellOf(64, 129, 64) == (1, 2),
      "CellOf floors world positions into cells");

var gridVars = new EventVariables();
var grid = new ConstructionManager();
var gridA = new FakeActor("Barracks");
Check(grid.TryReserveCells(gridA, "Barracks", gridVars, 0, 0, 64),
      "a grid placement reserves an empty cell");
var gridB = new FakeActor("Barracks");
Check(!grid.TryReserveCells(gridB, "Barracks", gridVars, 1, 1, 64),
      "a second building cannot claim the same cell");

var footVars = new EventVariables();
footVars.SetGlobal("BarracksGridWidth", ValueBox.OfNumber(2));
footVars.SetGlobal("BarracksGridDepth", ValueBox.OfNumber(2));
var footGrid = new ConstructionManager();
var footA = new FakeActor("Barracks");
Check(footGrid.TryReserveCells(footA, "Barracks", footVars, 0, 0, 64),
      "a 2x2 building reserves its whole footprint");
var footInside = new FakeActor("Turret");
Check(!footGrid.TryReserveCells(footInside, "Turret", footVars, 0, 0, 64) &&
      !footGrid.TryReserveCells(footInside, "Turret", footVars, 64, 64, 64),
      "the footprint blocks its anchor and diagonal cells");
var footOutside = new FakeActor("Turret");
Check(footGrid.TryReserveCells(footOutside, "Turret", footVars, 128, 64, 64),
      "a cell just outside the footprint is still free");
footGrid.ReleaseCells(footA);
var footReuse = new FakeActor("Turret");
Check(footGrid.TryReserveCells(footReuse, "Turret", footVars, 0, 0, 64),
      "ReleaseCells frees the footprint for a later placement");

var cancelGrid = new ConstructionManager();
var cancelGridActor = new FakeActor("Barracks");
cancelGrid.RegisterPlaced(cancelGridActor, "Barracks", gridVars);
Check(cancelGrid.TryReserveCells(cancelGridActor, "Barracks", gridVars, 0, 0, 64),
      "cancel test reserves a cell");
cancelGrid.Cancel(gridVars, cancelGridActor);
var cancelGridReuse = new FakeActor("Turret");
Check(cancelGrid.TryReserveCells(cancelGridReuse, "Turret", gridVars, 0, 0, 64),
      "Cancel frees the grid cell");

var gridSizeVars = new EventVariables();
Check(ConstructionManager.ResolveGridSize(gridSizeVars, false, 0) == ConstructionManager.DefaultGridSize,
      "an unset BuildGridSize falls back to 64 for on-grid placement");
gridSizeVars.SetGlobal("BuildGridSize", ValueBox.OfNumber(32));
Check(ConstructionManager.ResolveGridSize(gridSizeVars, false, 0) == 32,
      "BuildGridSize sets the default cell size");
Check(ConstructionManager.ResolveGridSize(gridSizeVars, true, 16) == 16,
      "an explicit `on grid by <n>` overrides BuildGridSize");

string placeHint = null;
var badPlaceSpec = ActionWords.Parse(
    parser.Parse("when game starts\n  place Barracks\n").Events[0].Body[0].Words,
    message => placeHint = message);
Check(badPlaceSpec.Verb == ActionVerb.Unknown && placeHint != null &&
      placeHint.Contains("place", StringComparison.OrdinalIgnoreCase),
      "`place` without `at <target>` stays a friendly unknown action");

// ------------------------------------------------------------------
// Task 18 Part D: production queues & unit training
// ------------------------------------------------------------------

// Trigger classification.
var trainFinishedDesc = TriggerClassifier.Classify(parser.Parse(
    "when Barracks finishes training Marine\n  set Done to 1\n").Events[0]);
Check(trainFinishedDesc.Kind == TriggerKind.TrainingFinished &&
      trainFinishedDesc.RequireName == "Barracks" && trainFinishedDesc.RequireUnit == "Marine",
      "`when Barracks finishes training Marine` carries the building and unit names");

var trainFinishedOwnDesc = TriggerClassifier.Classify(parser.Parse(
    "when me finishes training Marine\n  set Done to 1\n").Events[0]);
Check(trainFinishedOwnDesc.Kind == TriggerKind.TrainingFinished &&
      trainFinishedOwnDesc.RequireName == "me" && trainFinishedOwnDesc.RequireUnit == "Marine",
      "`when me finishes training Marine` keeps the `me` building form");

var trainStartedDesc = TriggerClassifier.Classify(parser.Parse(
    "when Barracks starts producing Marine\n  set Started to 1\n").Events[0]);
Check(trainStartedDesc.Kind == TriggerKind.TrainingStarted &&
      trainStartedDesc.RequireName == "Barracks" && trainStartedDesc.RequireUnit == "Marine",
      "`when Barracks starts producing Marine` classifies to TrainingStarted");

var readyEmptyDesc = TriggerClassifier.Classify(parser.Parse(
    "when production queue is empty\n  set Empty to 1\n").Events[0]);
Check(readyEmptyDesc.Kind == TriggerKind.ProductionQueueEmpty && readyEmptyDesc.RequireName.Length == 0,
      "`when production queue is empty` listens to every building");

var namedEmptyDesc = TriggerClassifier.Classify(parser.Parse(
    "when Barracks production queue is empty\n  set Empty to 1\n").Events[0]);
Check(namedEmptyDesc.Kind == TriggerKind.ProductionQueueEmpty && namedEmptyDesc.RequireName == "Barracks",
      "`when Barracks production queue is empty` filters by building");

// Action parsing.
var trainProg = parser.Parse(@"when game starts
  train Marine at Barracks
  train Zergling at me
  train Marine
  cancel training Marine
  cancel training Zergling at Barracks");
var trainAtSpec = ActionWords.Parse(((RawActionNode)trainProg.Events[0].Body[0].Statement).Words, null);
Check(trainAtSpec.Verb == ActionVerb.Train && trainAtSpec.Thing == "Marine" && trainAtSpec.To == "Barracks",
      "`train Marine at Barracks` parses to Train with unit and building");
var trainOwnSpec = ActionWords.Parse(((RawActionNode)trainProg.Events[0].Body[1].Statement).Words, null);
Check(trainOwnSpec.Verb == ActionVerb.Train && trainOwnSpec.Thing == "Zergling" && trainOwnSpec.To == "me",
      "`train Zergling at me` keeps the acting-building target");
var trainBareSpec = ActionWords.Parse(((RawActionNode)trainProg.Events[0].Body[2].Statement).Words, null);
Check(trainBareSpec.Verb == ActionVerb.Train && trainBareSpec.Thing == "Marine" && trainBareSpec.To.Length == 0,
      "`train Marine` queues on the acting building");
var cancelTrainSpec = ActionWords.Parse(((RawActionNode)trainProg.Events[0].Body[3].Statement).Words, null);
Check(cancelTrainSpec.Verb == ActionVerb.CancelTraining && cancelTrainSpec.Thing == "Marine" &&
      cancelTrainSpec.To.Length == 0,
      "`cancel training Marine` parses to CancelTraining");
var cancelTrainNamedSpec = ActionWords.Parse(((RawActionNode)trainProg.Events[0].Body[4].Statement).Words, null);
Check(cancelTrainNamedSpec.Verb == ActionVerb.CancelTraining && cancelTrainNamedSpec.Thing == "Zergling" &&
      cancelTrainNamedSpec.To == "Barracks",
      "`cancel training Zergling at Barracks` keeps the named building");

// `set build time` is its own statement, so it never becomes a variable called "build time".
var buildTimeProg = parser.Parse(@"when game starts
  set build time to 10 seconds
  set build time of Barracks to 2.5 seconds
  set build time to my TrainSpeed");
Check(buildTimeProg.Events[0].Body[0].Statement is SetBuildTimeNode buildTimeBare &&
      buildTimeBare.Thing.Length == 0 && buildTimeBare.Value is NumberValue buildTimeBareValue &&
      buildTimeBareValue.Value == 10,
      "`set build time to 10 seconds` builds a SetBuildTimeNode");
Check(buildTimeProg.Events[0].Body[1].Statement is SetBuildTimeNode buildTimeNamed &&
      buildTimeNamed.Thing == "Barracks" && buildTimeNamed.Value is NumberValue buildTimeNamedValue &&
      buildTimeNamedValue.Value == 2.5,
      "`set build time of Barracks to 2.5 seconds` keeps the named target");
Check(buildTimeProg.Events[0].Body[2].Statement is SetBuildTimeNode buildTimeVariable &&
      buildTimeVariable.Value is VariableValue buildTimeVariableValue &&
      buildTimeVariableValue.Owner == "my" && buildTimeVariableValue.Name == "TrainSpeed",
      "`set build time to my TrainSpeed` reads a per-building variable");

// Condition parsing.
var idleCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  Barracks is idle").Events[0].Body[0].Words);
Check(idleCondition.Kind == ConditionKind.ProductionIdle && idleCondition.Thing == "Barracks",
      "`Barracks is idle` parses to ProductionIdle");
var queueCountCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  queue count is 2").Events[0].Body[0].Words);
Check(queueCountCondition.Kind == ConditionKind.QueueCount &&
      queueCountCondition.Right is NumberValue queueCountRight && queueCountRight.Value == 2,
      "`queue count is 2` parses to QueueCount");
var hasQueueCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me has build queue").Events[0].Body[0].Words);
Check(hasQueueCondition.Kind == ConditionKind.HasBuildQueue,
      "`me has build queue` parses to HasBuildQueue");

// Production state: queueing, data-driven duration, cancel and start/finish edges.
var productionVars = new EventVariables();
var productionManager = new ProductionManager();
var productionActor = new FakeActor("Barracks");
Check(productionManager.IsIdle(productionActor) && !productionManager.HasQueue(productionActor),
      "a building with no production is idle and has no queue yet");
productionVars.SetGlobal("MarineBuildTime", ValueBox.OfNumber(3));
productionVars.SetGlobal("ZerglingBuildTime", ValueBox.OfNumber(4));
productionManager.Enqueue(productionVars, null, productionActor, "Barracks", "Marine");
Check(!productionManager.IsIdle(productionActor) && productionManager.HasQueue(productionActor) &&
      productionManager.QueueCount(productionActor) == 1,
      "Enqueue starts a production queue and counts the pending unit");
Check(Math.Abs(productionManager.RemainingOf(productionActor) - 3) < 0.0001,
      "MarineBuildTime sets the order's duration");
productionManager.Enqueue(productionVars, null, productionActor, "Barracks", "Zergling");
Check(productionManager.QueueCount(productionActor) == 2 && productionManager.CurrentUnit(productionActor) == "Marine",
      "a second unit waits behind the active order");
productionManager.Tick(null, 0);
productionManager.Tick(null, 3);
Check(productionManager.CurrentUnit(productionActor) == "Zergling" &&
      productionManager.QueueCount(productionActor) == 1,
      "the first unit finishes and the queued unit starts");
Check(Math.Abs(productionManager.RemainingOf(productionActor) - 4) < 0.0001,
      "the next order reads its own ZerglingBuildTime");
productionManager.Tick(null, 7);
Check(productionManager.IsIdle(productionActor) && productionManager.QueueCount(productionActor) == 0,
      "the queue drains after the last unit finishes");

// `set build time` overrides the data default and the current order.
var timedVars = new EventVariables();
timedVars.SetGlobal("MarineBuildTime", ValueBox.OfNumber(20));
var timedManager = new ProductionManager();
var timedActor = new FakeActor("Barracks");
timedManager.SetBuildTime(timedVars, timedActor, "Barracks", 2);
timedManager.Enqueue(timedVars, null, timedActor, "Barracks", "Marine");
Check(Math.Abs(timedManager.RemainingOf(timedActor) - 2) < 0.0001,
      "`set build time` overrides the data-driven unit build time");
timedManager.SetBuildTime(timedVars, timedActor, "Barracks", 1);
Check(Math.Abs(timedManager.RemainingOf(timedActor) - 1) < 0.0001,
      "`set build time` changes the current order too");

// Cancel removes the matching unit; cancelling the active one promotes the next.
var cancelProductionManager = new ProductionManager();
var cancelProductionActor = new FakeActor("Barracks");
cancelProductionManager.Enqueue(productionVars, null, cancelProductionActor, "Barracks", "Marine");
cancelProductionManager.Enqueue(productionVars, null, cancelProductionActor, "Barracks", "Zergling");
Check(cancelProductionManager.Cancel(null, cancelProductionActor, "Zergling") &&
      cancelProductionManager.QueueCount(cancelProductionActor) == 1,
      "`cancel training` removes the matching queued unit");
Check(cancelProductionManager.Cancel(null, cancelProductionActor, "Marine") &&
      cancelProductionManager.IsIdle(cancelProductionActor),
      "cancelling the active unit leaves the building idle");
Check(!cancelProductionManager.Cancel(null, cancelProductionActor, "Marine"),
      "cancelling a unit that is not queued reports no change");

// Engine-level edges: start, finish and empty fire once at the right times.
var productionWorld = new FakeWorld();
var productionBuilding = new FakeActor("Barracks");
productionWorld.AddActor(productionBuilding);
var productionEngine = FreshEngine(productionWorld);
productionEngine.AddSheet(new GlobalHost(), parser.Parse(@"when Barracks starts producing Marine
  set Started to Started + 1
when Barracks finishes training Marine
  set Finished to Finished + 1
when Barracks production queue is empty
  set Emptied to Emptied + 1"));
productionEngine.Variables.SetGlobal("MarineBuildTime", ValueBox.OfNumber(3));
productionEngine.Production.Enqueue(productionEngine.Variables, productionEngine,
    productionBuilding, "Barracks", "Marine");
productionEngine.Tick(0);
Check(productionEngine.Variables.GetGlobal("Started").AsNumber() == 1,
      "`when <building> starts producing <unit>` fires when training begins");
Check(productionEngine.Variables.GetGlobal("Finished").IsNone(),
      "the finish edge waits for the build time");
productionEngine.Tick(3);
Check(productionEngine.Variables.GetGlobal("Finished").AsNumber() == 1 &&
      productionEngine.Variables.GetGlobal("Emptied").AsNumber() == 1,
      "training completes at its build time and the empty queue fires");
productionEngine.Tick(4);
Check(productionEngine.Variables.GetGlobal("Finished").AsNumber() == 1 &&
      productionEngine.Variables.GetGlobal("Emptied").AsNumber() == 1,
      "the finish and empty edges do not repeat while idle");

// A different unit's start event does not answer this one.
var unitFilterWorld = new FakeWorld();
var unitFilterBuilding = new FakeActor("Barracks");
unitFilterWorld.AddActor(unitFilterBuilding);
var unitFilterEngine = FreshEngine(unitFilterWorld);
unitFilterEngine.AddSheet(new GlobalHost(), parser.Parse("when Barracks starts producing Zergling\n  set Wrong to Wrong + 1"));
unitFilterEngine.Production.Enqueue(unitFilterEngine.Variables, unitFilterEngine,
    unitFilterBuilding, "Barracks", "Marine");
unitFilterEngine.Tick(0);
Check(unitFilterEngine.Variables.GetGlobal("Wrong").IsNone(),
      "`starts producing Zergling` ignores a Marine");

// Conditions read the acting building's queue, and an idle building passes even before
// it has ever trained anything.
var idleWorld = new FakeWorld();
var idleActor = new FakeActor("Barracks");
idleWorld.AddActor(idleActor);
var idleEngine = FreshEngine(idleWorld);
idleEngine.AddSheet(idleActor, parser.Parse("# placeholder\n"));
idleEngine.AddSheet(new GlobalHost(), parser.Parse(@"when every 1 seconds
  Barracks is idle
  set Idle to Idle + 1"));
idleEngine.Tick(1);
Check(idleEngine.Variables.GetGlobal("Idle").AsNumber() == 1,
      "`Barracks is idle` passes before any production");
idleEngine.Production.Enqueue(idleEngine.Variables, idleEngine, idleActor, "Barracks", "Marine");
idleEngine.Tick(2);
Check(idleEngine.Variables.GetGlobal("Idle").AsNumber() == 1,
      "`Barracks is idle` blocks while a unit is training");

var queueCondEngine = FreshEngine(new FakeWorld());
var queueCondActor = new FakeActor("Barracks");
queueCondEngine.AddSheet(queueCondActor, parser.Parse(@"when every 1 seconds
  queue count is 2
  me has build queue
  set my Ready to Ready + 1"));
queueCondEngine.Production.Enqueue(queueCondEngine.Variables, queueCondEngine,
    queueCondActor, "Barracks", "Marine");
queueCondEngine.Tick(1);
Check(queueCondEngine.Variables.GetLocal(queueCondActor, "Ready").IsNone(),
      "`queue count is 2` blocks with one unit queued");
queueCondEngine.Production.Enqueue(queueCondEngine.Variables, queueCondEngine,
    queueCondActor, "Barracks", "Zergling");
queueCondEngine.Tick(2);
Check(queueCondEngine.Variables.GetLocal(queueCondActor, "Ready").AsNumber() == 1,
      "`queue count is 2` and `me has build queue` pass with two units queued");

// Engine dispatch: `set build time` reaches the ProductionManager, named or acting.
var dispatchTimeEngine = FreshEngine(new FakeWorld());
var dispatchTimeActor = new FakeActor("Barracks");
dispatchTimeEngine.AddSheet(dispatchTimeActor, parser.Parse(@"when me appears
  set build time to 2 seconds"));
dispatchTimeEngine.Tick(0);
dispatchTimeEngine.Production.Enqueue(dispatchTimeEngine.Variables, dispatchTimeEngine,
    dispatchTimeActor, "Barracks", "Marine");
Check(Math.Abs(dispatchTimeEngine.Production.RemainingOf(dispatchTimeActor) - 2) < 0.0001,
      "`set build time` dispatches to the ProductionManager");

var namedTimeWorld = new FakeWorld();
var namedTimeActor = new FakeActor("Barracks");
namedTimeWorld.AddActor(namedTimeActor);
var namedTimeEngine = FreshEngine(namedTimeWorld);
namedTimeEngine.AddSheet(namedTimeActor, parser.Parse("# placeholder\n"));
namedTimeEngine.AddSheet(new GlobalHost(), parser.Parse(@"when every 1 seconds
  set build time of Barracks to 4 seconds"));
namedTimeEngine.Tick(1);
namedTimeEngine.Production.Enqueue(namedTimeEngine.Variables, namedTimeEngine,
    namedTimeActor, "Barracks", "Marine");
Check(Math.Abs(namedTimeEngine.Production.RemainingOf(namedTimeActor) - 4) < 0.0001,
      "`set build time of Barracks` from a global sheet targets that building");

string buildTimeHint = null;
try
{
    parser.Parse("when game starts\n  set build time to\n");
}
catch (ParseException error)
{
    buildTimeHint = error.Message;
}
Check(buildTimeHint != null && buildTimeHint.Contains("set build time", StringComparison.OrdinalIgnoreCase),
      "a malformed `set build time` gives the friendly shape hint");

// ------------------------------------------------------------------
// Task 18 Part E: orders & attack-move
// ------------------------------------------------------------------

// Trigger classification.
var orderCompleteDesc = TriggerClassifier.Classify(parser.Parse(
    "when order completes\n  set Done to 1\n").Events[0]);
Check(orderCompleteDesc.Kind == TriggerKind.OrderCompletes && orderCompleteDesc.RequireName.Length == 0,
      "`when order completes` listens on the acting unit");

var queuedOrderDesc = TriggerClassifier.Classify(parser.Parse(
    "when queued order starts\n  set Started to 1\n").Events[0]);
Check(queuedOrderDesc.Kind == TriggerKind.OrderStarts && queuedOrderDesc.RequireName.Length == 0,
      "`when queued order starts` classifies to OrderStarts");

var pathBlockedDesc = TriggerClassifier.Classify(parser.Parse(
    "when path blocked\n  set Blocked to 1\n").Events[0]);
Check(pathBlockedDesc.Kind == TriggerKind.PathBlocked && pathBlockedDesc.RequireName.Length == 0,
      "`when path blocked` classifies to PathBlocked");

var namedOrderDesc = TriggerClassifier.Classify(parser.Parse(
    "when attack-move order completes\n  set Done to 1\n").Events[0]);
Check(namedOrderDesc.Kind == TriggerKind.OrderCompletes && namedOrderDesc.RequireName == "attack-move",
      "`when attack-move order completes` filters by order name");

// Action parsing.
var orderProg = parser.Parse(@"when game starts
  attack-move to Enemy
  attack Enemy
  stop
  hold position
  add order to queue
  clear orders
  set rally point to Barracks");
var attackMoveSpec = ActionWords.Parse(((RawActionNode)orderProg.Events[0].Body[0].Statement).Words, null);
Check(attackMoveSpec.Verb == ActionVerb.AttackMove && attackMoveSpec.Thing == "Enemy",
      "`attack-move to Enemy` parses to AttackMove");
var attackSpec = ActionWords.Parse(((RawActionNode)orderProg.Events[0].Body[1].Statement).Words, null);
Check(attackSpec.Verb == ActionVerb.AttackOrder && attackSpec.Thing == "Enemy",
      "`attack Enemy` parses to AttackOrder");
var stopOrderSpec = ActionWords.Parse(((RawActionNode)orderProg.Events[0].Body[2].Statement).Words, null);
Check(stopOrderSpec.Verb == ActionVerb.StopOrder,
      "bare `stop` parses to StopOrder");
var holdSpec = ActionWords.Parse(((RawActionNode)orderProg.Events[0].Body[3].Statement).Words, null);
Check(holdSpec.Verb == ActionVerb.HoldPosition,
      "`hold position` parses to HoldPosition");
var addOrderSpec = ActionWords.Parse(((RawActionNode)orderProg.Events[0].Body[4].Statement).Words, null);
Check(addOrderSpec.Verb == ActionVerb.AddOrderToQueue,
      "`add order to queue` parses to AddOrderToQueue");
var clearOrdersSpec = ActionWords.Parse(((RawActionNode)orderProg.Events[0].Body[5].Statement).Words, null);
Check(clearOrdersSpec.Verb == ActionVerb.ClearOrders,
      "`clear orders` parses to ClearOrders");
var rallySpec = ActionWords.Parse(((RawActionNode)orderProg.Events[0].Body[6].Statement).Words, null);
Check(rallySpec.Verb == ActionVerb.SetRallyPoint && rallySpec.Thing == "Barracks",
      "`set rally point to Barracks` parses to SetRallyPoint");

// `my current order` is state, not a variable holding a value the author sets.
var orderStateProg = parser.Parse("when game starts\n  set Target to my current order");
var orderStateNode = (SetNode)orderStateProg.Events[0].Body[0].Statement;
Check(orderStateNode.Value is VariableValue orderStateValue &&
      orderStateValue.Owner == "my" && orderStateValue.Name == "current order",
      "`my current order` parses as the multiword order state");

// Condition parsing and evaluation.
var currentOrderCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  my current order is attack-move").Events[0].Body[0].Words);
Check(currentOrderCondition.Kind == ConditionKind.CurrentOrderIs,
      "`my current order is attack-move` parses to CurrentOrderIs");
var orderQueueCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  my order queue is 2").Events[0].Body[0].Words);
Check(orderQueueCondition.Kind == ConditionKind.Equal &&
      orderQueueCondition.Right is NumberValue orderQueueRight && orderQueueRight.Value == 2,
      "`my order queue is 2` parses to a numeric comparison");
var orderTargetCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  my order target is Enemy").Events[0].Body[0].Words);
Check(orderTargetCondition.Kind == ConditionKind.Equal &&
      orderTargetCondition.Right is TextValue orderTargetText && orderTargetText.Text == "Enemy",
      "`my order target is Enemy` compares the target name");

// Order state: start, queue, promote, stop/hold and rally points.
var orderVars = new EventVariables();
var orderManager = new OrderManager();
var orderUnit = new FakeActor("Marine");
var orderEnemy = new FakeActor("Enemy");
var firstOrder = new UnitOrder { Kind = UnitOrderKind.AttackMove, Name = "attack-move", Target = orderEnemy, TargetName = "Enemy" };
orderManager.StartOrder(orderVars, null, orderUnit, firstOrder);
Check(orderManager.CurrentOrderName(orderUnit) == "attack-move" &&
      orderManager.OrderTargetName(orderUnit) == "Enemy" &&
      orderManager.OrderCount(orderUnit) == 1 &&
      orderManager.HasOrders(orderUnit),
      "StartOrder publishes the active order and its target");
orderManager.AddOrder(orderVars, null, orderUnit,
    new UnitOrder { Kind = UnitOrderKind.Attack, Name = "attack", Target = orderEnemy, TargetName = "Enemy" });
Check(orderManager.CurrentOrderName(orderUnit) == "attack-move" && orderManager.OrderCount(orderUnit) == 2,
      "a new order waits behind the active one");
Check(orderManager.Complete(orderVars, null, orderUnit) &&
      orderManager.CurrentOrderName(orderUnit) == "attack" &&
      orderManager.OrderCount(orderUnit) == 1,
      "completing an order promotes the next one");
Check(!orderManager.Complete(orderVars, null, new FakeActor("Idle")),
      "completing an idle unit reports no change");
orderManager.Complete(orderVars, null, orderUnit);
Check(orderManager.CurrentOrderName(orderUnit) == "idle" && !orderManager.HasOrders(orderUnit),
      "draining the queue leaves the unit idle");
orderManager.Stop(orderVars, orderUnit);
Check(orderManager.CurrentOrderName(orderUnit) == "stop" && orderManager.OrderCount(orderUnit) == 0,
      "`stop` leaves the unit stopped");
orderManager.AddOrder(orderVars, null, orderUnit,
    new UnitOrder { Kind = UnitOrderKind.Move, Name = "move", TargetName = "there", HasPositionTarget = true });
Check(orderManager.CurrentOrderName(orderUnit) == "move" && orderManager.OrderCount(orderUnit) == 1,
      "a new order clears the stop state");
orderManager.Hold(orderVars, orderUnit);
Check(orderManager.CurrentOrderName(orderUnit) == "hold" && orderManager.IsHolding(orderUnit),
      "`hold position` leaves the unit holding");
orderManager.Clear(orderVars, orderUnit);
Check(orderManager.CurrentOrderName(orderUnit) == "idle" && !orderManager.IsHolding(orderUnit),
      "`clear orders` returns the unit to idle");

var rallyManager = new OrderManager();
var rallyBuilding = new FakeActor("Barracks");
rallyManager.SetRallyPoint(rallyBuilding, orderEnemy, "Enemy");
Check(rallyManager.HasRallyPoint(rallyBuilding) &&
      ReferenceEquals(rallyManager.RallyPointOf(rallyBuilding), orderEnemy) &&
      rallyManager.RallyTargetNameOf(rallyBuilding) == "Enemy",
      "`set rally point` stores the building's rally target");
rallyManager.Destroyed(rallyBuilding);
Check(!rallyManager.HasRallyPoint(rallyBuilding),
      "destroying a building drops its rally point");

// Engine-level edges: completion, queued promotion and path blocked.
var orderFlowWorld = new FakeWorld();
var orderFlowUnit = new FakeActor("Marine");
orderFlowWorld.AddActor(orderFlowUnit);
var orderFlowEngine = FreshEngine(orderFlowWorld);
orderFlowEngine.AddSheet(orderFlowUnit, parser.Parse(@"when order completes
  add 1 to my Completed
when queued order starts
  add 1 to my Started
when path blocked
  add 1 to my Blocked"));
orderFlowEngine.Orders.StartOrder(orderFlowEngine.Variables, orderFlowEngine, orderFlowUnit,
    new UnitOrder { Kind = UnitOrderKind.Move, Name = "move", TargetName = "Waypoint" });
orderFlowEngine.Tick(0);
Check(orderFlowEngine.Variables.GetLocal(orderFlowUnit, "Started").AsNumber() == 1,
      "`when queued order starts` fires when a new order becomes active");
orderFlowEngine.Orders.AddOrder(orderFlowEngine.Variables, orderFlowEngine, orderFlowUnit,
    new UnitOrder { Kind = UnitOrderKind.Attack, Name = "attack", TargetName = "Enemy" });
orderFlowEngine.Orders.Complete(orderFlowEngine.Variables, orderFlowEngine, orderFlowUnit);
orderFlowEngine.Tick(1);
Check(orderFlowEngine.Variables.GetLocal(orderFlowUnit, "Completed").AsNumber() == 1 &&
      orderFlowEngine.Variables.GetLocal(orderFlowUnit, "Started").AsNumber() == 2,
      "completing an order fires the complete edge and starts the queued order");
orderFlowEngine.Orders.ReportPathBlocked(orderFlowEngine, orderFlowUnit);
orderFlowEngine.Tick(2);
Check(orderFlowEngine.Variables.GetLocal(orderFlowUnit, "Blocked").AsNumber() == 1,
      "`when path blocked` fires for a blocked order");

// The condition and the computed `my ...` state agree with the queue.
var orderFlowContext = new EventContext
{
    Variables = orderFlowEngine.Variables,
    World = orderFlowWorld,
    Me = orderFlowUnit,
    Runtime = orderFlowEngine,
};
Check(orderFlowContext.ReadMy("current order").AsText() == "attack" &&
      orderFlowContext.ReadMy("order target").AsText() == "Enemy" &&
      orderFlowContext.ReadMy("order queue").AsNumber() == 1,
      "`my current order` / `my order target` / `my order queue` read the live queue");
var liveOrderCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  my current order is attack").Events[0].Body[0].Words);
Check(Conditions.Evaluate(liveOrderCondition, orderFlowContext),
      "`my current order is attack` matches the live order");
var liveQueueCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  my order queue is 1").Events[0].Body[0].Words);
Check(Conditions.Evaluate(liveQueueCondition, orderFlowContext),
      "`my order queue is 1` matches the live queue size");

// ------------------------------------------------------------------
// Task 21 Part A: attack range (`me is in range of <target>`)
// ------------------------------------------------------------------

var inRangeCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me is in range of Enemy").Events[0].Body[0].Words);
Check(inRangeCondition.Kind == ConditionKind.InRangeOf && inRangeCondition.Thing == "Enemy",
      "`me is in range of Enemy` parses to InRangeOf");

var rangeWorld = new FakeWorld();
var rangeUnit = new FakeActor("Marine");
var rangeEnemy = new FakeActor("Enemy");
rangeWorld.AddActor(rangeUnit);
rangeWorld.AddActor(rangeEnemy);
rangeWorld.SetDistance(rangeUnit, rangeEnemy, 40);
var rangeEngine = FreshEngine(rangeWorld);
rangeEngine.AddSheet(rangeUnit, parser.Parse("# placeholder\n"));
rangeEngine.AddSheet(rangeEnemy, parser.Parse("# placeholder\n"));
var rangeContext = new EventContext
{
    Variables = rangeEngine.Variables,
    World = rangeWorld,
    Me = rangeUnit,
    Runtime = rangeEngine,
};
Check(Conditions.Evaluate(inRangeCondition, rangeContext),
      "a unit that never wrote `my attack range` still reaches a target 40 away (default 50)");
rangeEngine.Variables.SetLocal(rangeUnit, "attack range", ValueBox.OfNumber(80));
rangeWorld.SetDistance(rangeUnit, rangeEnemy, 60);
Check(Conditions.Evaluate(inRangeCondition, rangeContext),
      "`my attack range` 80 reaches a target 60 away");
rangeWorld.SetDistance(rangeUnit, rangeEnemy, 120);
Check(!Conditions.Evaluate(inRangeCondition, rangeContext),
      "a target beyond `my attack range` reads out of range");

// The range is written and read by the language, not just by the manager.
var rangeSetWorld = new FakeWorld();
var rangeSetUnit = new FakeActor("Marine");
var rangeSetEnemy = new FakeActor("Enemy");
rangeSetWorld.AddActor(rangeSetUnit);
rangeSetWorld.AddActor(rangeSetEnemy);
rangeSetWorld.SetDistance(rangeSetUnit, rangeSetEnemy, 100);
var rangeSetEngine = FreshEngine(rangeSetWorld);
rangeSetEngine.AddSheet(rangeSetUnit, parser.Parse("when me appears\n  set my attack range to 120"));
rangeSetEngine.AddSheet(rangeSetEnemy, parser.Parse("# placeholder\n"));
rangeSetEngine.Tick(0);
Check(Math.Abs(OrderManager.DeclaredAttackRange(rangeSetEngine.Variables, rangeSetUnit) - 120) < 0.0001,
      "`set my attack range to 120` stores the multiword range");
var rangeSetContext = new EventContext
{
    Variables = rangeSetEngine.Variables,
    World = rangeSetWorld,
    Me = rangeSetUnit,
    Runtime = rangeSetEngine,
};
Check(Conditions.Evaluate(inRangeCondition, rangeSetContext),
      "`me is in range of Enemy` uses the range the sheet declared");
var rangeReadCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  my attack range is 120").Events[0].Body[0].Words);
Check(rangeReadCondition.Kind == ConditionKind.Equal && rangeReadCondition.Owner == "my" &&
      rangeReadCondition.Name == "attack range",
      "`my attack range is 120` reads the declared range back");
Check(Conditions.Evaluate(rangeReadCondition, rangeSetContext),
      "`my attack range is 120` matches what the sheet set");

var noRangeVars = new EventVariables();
var noRangeUnit = new FakeActor("Marine");
Check(OrderManager.DeclaredAttackRange(noRangeVars, noRangeUnit) == 0,
      "a sheet with no attack range declares 0 (and reads it silently)");
noRangeVars.SetLocal(noRangeUnit, "AttackRange", ValueBox.OfNumber(150));
Check(Math.Abs(OrderManager.DeclaredAttackRange(noRangeVars, noRangeUnit) - 150) < 0.0001,
      "the run-together `AttackRange` spelling declares the range too");

// `attack` walks to the edge of the range instead of into the target.
OrderManager.ApproachPoint(0, 0, 100, 0, 30, out double stopX, out double stopZ);
Check(Math.Abs(stopX - 70) < 0.0001 && Math.Abs(stopZ) < 0.0001,
      "an attack order stops at the edge of the unit's range");
OrderManager.ApproachPoint(0, 0, 10, 0, 30, out double closeX, out double closeZ);
Check(Math.Abs(closeX) < 0.0001 && Math.Abs(closeZ) < 0.0001,
      "a target already inside the range is never walked past");

// ------------------------------------------------------------------
// Task 22 Part A: unit types (land / air / sea)
// ------------------------------------------------------------------

var landCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me is land").Events[0].Body[0].Words);
Check(landCondition.Kind == ConditionKind.UnitTypeIs && landCondition.Name == "land",
      "`me is land` parses to UnitTypeIs");
var airCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me is air").Events[0].Body[0].Words);
var seaCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me is sea").Events[0].Body[0].Words);
Check(airCondition.Kind == ConditionKind.UnitTypeIs && airCondition.Name == "air" &&
      seaCondition.Kind == ConditionKind.UnitTypeIs && seaCondition.Name == "sea",
      "`me is air` and `me is sea` parse to UnitTypeIs");

var typeWorld = new FakeWorld();
var typeUnit = new FakeActor("Marine");
typeWorld.AddActor(typeUnit);
var typeEngine = FreshEngine(typeWorld);
typeEngine.AddSheet(typeUnit, parser.Parse("when me appears\n  set my unit type to \"air\""));
typeEngine.Tick(0);
var typeContext = new EventContext
{
    Variables = typeEngine.Variables,
    World = typeWorld,
    Me = typeUnit,
    Runtime = typeEngine,
};
Check(Conditions.Evaluate(airCondition, typeContext) && !Conditions.Evaluate(landCondition, typeContext) &&
      !Conditions.Evaluate(seaCondition, typeContext),
      "`set my unit type to \"air\"` makes `me is air` true and the others false");
Check(typeContext.ReadMy("unit type").AsText() == "air",
      "`my unit type` reads back what the sheet wrote");

var defaultTypeVars = new EventVariables();
var defaultTypeUnit = new FakeActor("Zombie");
Check(UnitTypes.TypeOf(defaultTypeVars, defaultTypeUnit) == "land",
      "a unit that never wrote `my unit type` is a land unit");
defaultTypeVars.SetLocal(defaultTypeUnit, "unit type", ValueBox.OfText("boat"));
Check(UnitTypes.TypeOf(defaultTypeVars, defaultTypeUnit) == "land",
      "an unknown unit type falls back to land");
defaultTypeVars.SetLocal(defaultTypeUnit, "UnitType", ValueBox.OfText("Sea"));
Check(UnitTypes.TypeOf(defaultTypeVars, defaultTypeUnit) == "sea",
      "the run-together `UnitType` spelling works, case-insensitively");

int typeWarnings = 0;
var typeWarnWorld = new FakeWorld();
var typeWarnUnit = new FakeActor("Marine");
typeWarnWorld.AddActor(typeWarnUnit);
var typeWarnEngine = FreshEngine(typeWarnWorld);
typeWarnEngine.OnRuntimeWarning = message =>
{
    if (message.Contains("unit type", StringComparison.OrdinalIgnoreCase))
        typeWarnings++;
};
typeWarnEngine.AddSheet(typeWarnUnit, parser.Parse("when me appears\n  set my unit type to \"boat\""));
typeWarnEngine.Tick(0);
Check(typeWarnings == 1,
      "an unknown unit type warns once instead of silently landing the unit");

// What each type may cross (Task 22 Part B feeds this the terrain map).
Check(UnitTypes.CanCross("land", UnitTypes.Ground.Land) &&
      !UnitTypes.CanCross("land", UnitTypes.Ground.Water),
      "ground units walk on land and are blocked by water");
Check(UnitTypes.CanCross("air", UnitTypes.Ground.Land) &&
      UnitTypes.CanCross("air", UnitTypes.Ground.Water),
      "air units ignore the ground under them");
Check(UnitTypes.CanCross("sea", UnitTypes.Ground.Water) &&
      !UnitTypes.CanCross("sea", UnitTypes.Ground.Land),
      "sea units are restricted to water");
Check(UnitTypes.CanCross("", UnitTypes.Ground.Land) &&
      !UnitTypes.CanCross("", UnitTypes.Ground.Water),
      "a unit with no type crosses like a land unit");
Check(!UnitTypes.CanCross("land", UnitTypes.Ground.Air) &&
      !UnitTypes.CanCross("sea", UnitTypes.Ground.Air) &&
      UnitTypes.CanCross("air", UnitTypes.Ground.Air),
      "open air cells only air units cross");

// ------------------------------------------------------------------
// Task 22 Part B: terrain passability
// ------------------------------------------------------------------

var markProg = parser.Parse("when game starts\n  mark water around Mouse by 300\n  mark land at Mouse");
var markAreaSpec = ActionWords.Parse(((RawActionNode)markProg.Events[0].Body[0].Statement).Words, null);
Check(markAreaSpec.Verb == ActionVerb.MarkTerrain && markAreaSpec.Thing == "water" &&
      markAreaSpec.To == "Mouse" && markAreaSpec.HasNumber && markAreaSpec.Number == 300,
      "`mark water around Mouse by 300` parses to MarkTerrain with a size");
var markCellSpec = ActionWords.Parse(((RawActionNode)markProg.Events[0].Body[1].Statement).Words, null);
Check(markCellSpec.Verb == ActionVerb.MarkTerrain && markCellSpec.Thing == "land" &&
      markCellSpec.To == "Mouse" && !markCellSpec.HasNumber,
      "`mark land at Mouse` marks one cell");
var markBadSpec = ActionWords.Parse(parser.Parse("when game starts\n  mark lava at Mouse").Events[0].Body[0].Words, null);
Check(markBadSpec.Verb == ActionVerb.Unknown, "an unknown terrain word makes `mark` unknown");

var terrain = new TerrainMap();
Check(terrain.KindAt(10, 10) == UnitTypes.Ground.Land && terrain.MarkedCells == 0,
      "an unmarked terrain map is land everywhere");
Check(terrain.IsPassable("land", 10, 10) && !terrain.IsPassable("sea", 10, 10),
      "sea units cannot stand on unmarked (land) ground");

int waterCells = terrain.MarkArea(1000, 1000, 200, UnitTypes.Ground.Water);
Check(waterCells > 0 && terrain.KindAt(1000, 1000) == UnitTypes.Ground.Water,
      "`mark water around` stores water cells");
Check(!terrain.IsPassable("land", 1000, 1000) && terrain.IsPassable("sea", 1000, 1000) &&
      terrain.IsPassable("air", 1000, 1000),
      "water blocks ground units, carries sea units and never blocks air");
Check(terrain.IsPassable("land", 5000, 5000),
      "ground outside the marked circle stays land");
terrain.MarkArea(1000, 1000, 200, UnitTypes.Ground.Land);
Check(terrain.IsPassable("land", 1000, 1000), "`mark land` clears the water back");

// One step at a time, and a unit already in water can always climb out.
var stepMap = new TerrainMap();
stepMap.Mark(0, 0, UnitTypes.Ground.Water);
Check(!stepMap.CanStep("land", -200, -200, 0, 0),
      "a ground unit cannot step into water");
Check(stepMap.CanStep("land", 0, 0, 300, 300),
      "a ground unit standing in water may walk out");
Check(!stepMap.CanStep("sea", 0, 0, 300, 300),
      "a sea unit cannot leave the water");
Check(stepMap.CanStep("air", -200, -200, 0, 0),
      "an air unit flies over water");

var voidMap = new TerrainMap();
voidMap.Mark(100, 100, UnitTypes.Ground.Air);
Check(!voidMap.IsPassable("land", 100, 100) && !voidMap.IsPassable("sea", 100, 100) &&
      voidMap.IsPassable("air", 100, 100),
      "an air cell only air units cross");

var sizedTerrainVars = new EventVariables();
sizedTerrainVars.SetGlobal(TerrainMap.CellSizeVariable, ValueBox.OfNumber(100));
var sizedMap = new TerrainMap();
sizedMap.ResolveCellSize(sizedTerrainVars);
sizedMap.Mark(150, 150, UnitTypes.Ground.Water);
Check(sizedMap.KindAt(199, 199) == UnitTypes.Ground.Water &&
      sizedMap.KindAt(99, 99) == UnitTypes.Ground.Land,
      "the TerrainCellSize global changes the grid");

var offMap = new TerrainMap { Enabled = false };
offMap.Mark(0, 0, UnitTypes.Ground.Water);
Check(offMap.IsPassable("land", 0, 0) && offMap.CanStep("land", -200, -200, 0, 0),
      "terrain blocking can be switched off");

// The actor's own `my unit type` drives the answer.
var terrainVars = new EventVariables();
var landUnit = new FakeActor("Marine");
var airUnit = new FakeActor("Bomber");
terrainVars.SetLocal(airUnit, "unit type", ValueBox.OfText("air"));
var typeMap = new TerrainMap();
typeMap.Mark(0, 0, UnitTypes.Ground.Water);
Check(!typeMap.IsPassableFor(landUnit, terrainVars, 0, 0) &&
      typeMap.IsPassableFor(airUnit, terrainVars, 0, 0),
      "`my unit type` decides who may cross a water cell");


// ------------------------------------------------------------------
// Task 23 Part A: attack types (melee / ranged)
// ------------------------------------------------------------------

var meleeCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me is melee").Events[0].Body[0].Words);
Check(meleeCondition.Kind == ConditionKind.AttackTypeIs && meleeCondition.Name == "melee",
      "`me is melee` parses to AttackTypeIs");
var rangedCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me is ranged").Events[0].Body[0].Words);
Check(rangedCondition.Kind == ConditionKind.AttackTypeIs && rangedCondition.Name == "ranged",
      "`me is ranged` parses to AttackTypeIs");

var attackWorld = new FakeWorld();
var attackUnit = new FakeActor("Marine");
attackWorld.AddActor(attackUnit);
var attackEngine = FreshEngine(attackWorld);
attackEngine.AddSheet(attackUnit, parser.Parse("when me appears\n  set my attack type to \"ranged\""));
attackEngine.Tick(0);
var attackContext = new EventContext
{
    Variables = attackEngine.Variables,
    World = attackWorld,
    Me = attackUnit,
    Runtime = attackEngine,
};
Check(Conditions.Evaluate(rangedCondition, attackContext) &&
      !Conditions.Evaluate(meleeCondition, attackContext),
      "`set my attack type to \"ranged\"` makes `me is ranged` true and `me is melee` false");
Check(attackContext.ReadMy("attack type").AsText() == "ranged",
      "`my attack type` reads back what the sheet wrote");

var plainAttackVars = new EventVariables();
var plainAttackUnit = new FakeActor("Zergling");
Check(AttackTypes.TypeOf(plainAttackVars, plainAttackUnit) == "melee",
      "a unit that never wrote `my attack type` strikes on contact (melee)");
plainAttackVars.SetLocal(plainAttackUnit, "AttackType", ValueBox.OfText("RANGED"));
Check(AttackTypes.TypeOf(plainAttackVars, plainAttackUnit) == "ranged",
      "the run-together `AttackType` spelling works, case-insensitively");
var oddAttackVars = new EventVariables();
var oddAttackUnit = new FakeActor("Militia");
oddAttackVars.SetLocal(oddAttackUnit, "attack type", ValueBox.OfText("sniper"));
Check(AttackTypes.TypeOf(oddAttackVars, oddAttackUnit) == "melee",
      "an unknown attack type falls back to melee");

int attackWarnings = 0;
var attackWarnWorld = new FakeWorld();
var attackWarnUnit = new FakeActor("Marine");
attackWarnWorld.AddActor(attackWarnUnit);
var attackWarnEngine = FreshEngine(attackWarnWorld);
attackWarnEngine.OnRuntimeWarning = message =>
{
    if (message.Contains("attack type", StringComparison.OrdinalIgnoreCase))
        attackWarnings++;
};
attackWarnEngine.AddSheet(attackWarnUnit, parser.Parse("when me appears\n  set my attack type to \"sniper\""));
attackWarnEngine.Tick(0);
Check(attackWarnings == 1,
      "an unknown attack type warns once instead of silently going melee");

// The type decides the reach a unit has before it declares `my attack range`.
var meleeRangeVars = new EventVariables();
var meleeRangeUnit = new FakeActor("Zergling");
var rangedRangeVars = new EventVariables();
var rangedRangeUnit = new FakeActor("Marine");
rangedRangeVars.SetLocal(rangedRangeUnit, "attack type", ValueBox.OfText("ranged"));
EventContext RangeContext(EventVariables variables, object unit) => new EventContext
{
    Variables = variables,
    World = new FakeWorld(),
    Me = unit,
    Runtime = null,
};
Check(Math.Abs(OrderManager.AttackRangeOf(RangeContext(meleeRangeVars, meleeRangeUnit)) -
               OrderManager.DefaultAttackRange) < 0.0001,
      "a melee unit that wrote no range reaches the melee default (50)");
Check(Math.Abs(OrderManager.AttackRangeOf(RangeContext(rangedRangeVars, rangedRangeUnit)) -
               OrderManager.DefaultRangedAttackRange) < 0.0001,
      "a ranged unit that wrote no range reaches the ranged default (300)");
rangedRangeVars.SetLocal(rangedRangeUnit, "attack range", ValueBox.OfNumber(120));
Check(Math.Abs(OrderManager.AttackRangeOf(RangeContext(rangedRangeVars, rangedRangeUnit)) - 120) < 0.0001,
      "a declared `my attack range` wins over the type's default");

Check(OrderManager.StopDistanceFor(RangeContext(meleeRangeVars, meleeRangeUnit)) == 0,
      "an `attack` order for a melee unit walks into contact (stop distance 0)");
var rangedStopVars = new EventVariables();
var rangedStopUnit = new FakeActor("Marine");
rangedStopVars.SetLocal(rangedStopUnit, "attack type", ValueBox.OfText("ranged"));
Check(Math.Abs(OrderManager.StopDistanceFor(RangeContext(rangedStopVars, rangedStopUnit)) -
               OrderManager.DefaultRangedAttackRange) < 0.0001,
      "an `attack` order for a ranged unit stops at its range instead of closing in");
meleeRangeVars.SetLocal(meleeRangeUnit, "attack range", ValueBox.OfNumber(80));
Check(Math.Abs(OrderManager.StopDistanceFor(RangeContext(meleeRangeVars, meleeRangeUnit)) - 80) < 0.0001,
      "a melee unit that declared a range stops at it too");

// The two type axes are independent: a ranged air unit is both.
var mixedVars = new EventVariables();
var mixedUnit = new FakeActor("Bomber");
mixedVars.SetLocal(mixedUnit, "unit type", ValueBox.OfText("air"));
mixedVars.SetLocal(mixedUnit, "attack type", ValueBox.OfText("ranged"));
var mixedContext = RangeContext(mixedVars, mixedUnit);
Check(UnitTypes.Is(mixedContext, "air") && AttackTypes.Is(mixedContext, "ranged") &&
      !UnitTypes.Is(mixedContext, "land") && !AttackTypes.Is(mixedContext, "melee"),
      "unit type and attack type are independent (a ranged air unit)");

// ------------------------------------------------------------------
// Task 23 Part B: attack matchups
// ------------------------------------------------------------------

var canAttackCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me can attack other").Events[0].Body[0].Words);
Check(canAttackCondition.Kind == ConditionKind.CanAttackTarget && canAttackCondition.Thing == "other",
      "`me can attack other` parses to CanAttackTarget");
var namedMatchupCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me can attack Bomber").Events[0].Body[0].Words);
Check(namedMatchupCondition.Kind == ConditionKind.CanAttackTarget &&
      namedMatchupCondition.Thing == "Bomber",
      "`me can attack Bomber` names its target");

Matchups.LoadData(null);
Check(Matchups.CanAttack(null, "land", "land") && Matchups.CanAttack(null, "land", "sea") &&
      !Matchups.CanAttack(null, "land", "air"),
      "by default ground units fight ground and sea but not air");
Check(Matchups.CanAttack(null, "sea", "sea") && !Matchups.CanAttack(null, "sea", "air"),
      "by default sea units fight sea but cannot hit air");
Check(Matchups.CanAttack(null, "air", "air") && Matchups.CanAttack(null, "air", "land") &&
      Matchups.CanAttack(null, "air", "sea"),
      "by default air units hit everything");

var matchupVars = new EventVariables();
matchupVars.SetGlobal(Matchups.VariableFor("land", "air"), ValueBox.OfBoolean(true));
Check(Matchups.CanAttack(matchupVars, "land", "air"),
      "`LandCanAttackAir` overrides the default at runtime");
matchupVars.SetGlobal(Matchups.VariableFor("air", "sea"), ValueBox.OfNumber(0));
Check(!Matchups.CanAttack(matchupVars, "air", "sea"),
      "`AirCanAttackSea` 0 switches that pair off");

Matchups.LoadData(parser.Parse(
    "when game starts\n  set LandCanAttackAir to true\n  set AirCanAttackLand to false"));
Check(Matchups.CanAttack(null, "land", "air") && !Matchups.CanAttack(null, "air", "land"),
      "Matchups.events authors the table before any sheet runs");
Matchups.LoadData(null);
Check(!Matchups.CanAttack(null, "land", "air"),
      "dropping the data file returns to the built-in defaults");
Check(Matchups.TrySplit("LandCanAttackAir", out string splitAttacker, out string splitDefender) &&
      splitAttacker == "land" && splitDefender == "air" &&
      !Matchups.TrySplit("ZombieHealth", out _, out _),
      "only `<Attacker>CanAttack<Defender>` names are read from the data file");

// The condition reads both units' own types.
var matchupWorld = new FakeWorld();
var marineActor = new FakeActor("Marine");
var bomberActor = new FakeActor("Bomber");
matchupWorld.AddActor(marineActor);
matchupWorld.AddActor(bomberActor);
var matchupEngine = FreshEngine(matchupWorld);
matchupEngine.AddSheet(marineActor, parser.Parse("# placeholder\n"));
matchupEngine.AddSheet(bomberActor, parser.Parse("# placeholder\n"));
matchupEngine.Variables.SetLocal(bomberActor, "unit type", ValueBox.OfText("air"));
var matchupContext = new EventContext
{
    Variables = matchupEngine.Variables,
    World = matchupWorld,
    Me = marineActor,
    Other = bomberActor,
    Runtime = matchupEngine,
};
Check(!Conditions.Evaluate(canAttackCondition, matchupContext),
      "a land unit cannot attack an air unit by default");
matchupEngine.Variables.SetGlobal(Matchups.VariableFor("land", "air"), ValueBox.OfBoolean(true));
Check(Conditions.Evaluate(canAttackCondition, matchupContext),
      "allowing `LandCanAttackAir` makes the condition pass");

// A mismatched shot is dropped before it reaches the target's sheet.
var matchupGateWorld = new FakeWorld();
var matchupShooter = new FakeActor("Marine");
var matchupTarget = new FakeActor("Bomber");
matchupGateWorld.AddActor(matchupShooter);
matchupGateWorld.AddActor(matchupTarget);
var matchupGateEngine = FreshEngine(matchupGateWorld);
matchupGateEngine.AddSheet(matchupShooter, parser.Parse("# placeholder\n"));
matchupGateEngine.AddSheet(matchupTarget,
    parser.Parse("when message \"damage\" arrives\n  add 1 to my Hits"));
matchupGateEngine.Variables.SetLocal(matchupTarget, "unit type", ValueBox.OfText("air"));
matchupGateEngine.SendRawMessage(
    new PushSignal { Kind = TriggerKind.Message, Name = "damage", Other = matchupShooter }, "Bomber");
matchupGateEngine.Tick(0);
Check(matchupGateEngine.Variables.GetLocal(matchupTarget, "Hits").IsNone(),
      "damage from a type that cannot attack the target never reaches its sheet");
matchupGateEngine.Variables.SetGlobal(Matchups.VariableFor("land", "air"), ValueBox.OfBoolean(true));
matchupGateEngine.SendRawMessage(
    new PushSignal { Kind = TriggerKind.Message, Name = "damage", Other = matchupShooter }, "Bomber");
matchupGateEngine.Tick(1);
Check(matchupGateEngine.Variables.GetLocal(matchupTarget, "Hits").AsNumber() == 1,
      "the same damage lands once the matchup allows the pair");

// ------------------------------------------------------------------
// Task 23 Part C: formation movement
// ------------------------------------------------------------------

Check(Formations.TryParse("wedge", out FormationKind wedgeKind) && wedgeKind == FormationKind.Wedge &&
      Formations.TryParse("COLUMN", out FormationKind columnKind) && columnKind == FormationKind.Column &&
      Formations.TryParse("row", out FormationKind rowKind) && rowKind == FormationKind.Line &&
      !Formations.TryParse("blob", out _),
      "line/row, wedge and column/file name the three formations");

var formationVars = new EventVariables();
Check(Formations.KindFrom(formationVars) == FormationKind.Line &&
      Math.Abs(Formations.SpacingFrom(formationVars) - Formations.DefaultSpacing) < 0.0001,
      "an unset Formation reads as line with the default spacing");
formationVars.SetGlobal(Formations.Variable, ValueBox.OfText("wedge"));
formationVars.SetGlobal(Formations.SpacingVariable, ValueBox.OfNumber(120));
Check(Formations.KindFrom(formationVars) == FormationKind.Wedge &&
      Math.Abs(Formations.SpacingFrom(formationVars) - 120) < 0.0001,
      "`set Formation to \"wedge\"` and `FormationSpacing` 120 are read back");
formationVars.SetGlobal(Formations.Variable, ValueBox.OfText("blob"));
Check(Formations.KindFrom(formationVars) == FormationKind.Line,
      "an unknown formation falls back to line");

// A line is a row across the direction of travel (+X heading), centred on the destination.
Formations.Slot(FormationKind.Line, 0, 3, 100, 1, 0, out double lineX0, out double lineZ0);
Formations.Slot(FormationKind.Line, 1, 3, 100, 1, 0, out double lineX1, out double lineZ1);
Formations.Slot(FormationKind.Line, 2, 3, 100, 1, 0, out double lineX2, out double lineZ2);
Check(Math.Abs(lineX0) < 0.0001 && Math.Abs(lineX1) < 0.0001 && Math.Abs(lineX2) < 0.0001,
      "a line never steps along the direction of travel");
Check(Math.Abs(Math.Abs(lineZ0) - 100) < 0.0001 && Math.Abs(lineZ1) < 0.0001 &&
      Math.Abs(Math.Abs(lineZ2) - 100) < 0.0001 && Math.Sign(lineZ0) != Math.Sign(lineZ2),
      "a line of three spaces the units one gap apart, centred on the point");

Formations.Slot(FormationKind.Column, 0, 3, 100, 0, 1, out double colX0, out double colZ0);
Formations.Slot(FormationKind.Column, 2, 3, 100, 0, 1, out double colX2, out double colZ2);
Check(Math.Abs(colX0) < 0.0001 && Math.Abs(colZ0) < 0.0001 &&
      Math.Abs(colX2) < 0.0001 && Math.Abs(colZ2 + 200) < 0.0001,
      "a column trails one gap behind the point for each unit");

Formations.Slot(FormationKind.Wedge, 0, 3, 100, 0, 1, out double wedgeX0, out double wedgeZ0);
Formations.Slot(FormationKind.Wedge, 1, 3, 100, 0, 1, out double wedgeX1, out double wedgeZ1);
Formations.Slot(FormationKind.Wedge, 2, 3, 100, 0, 1, out double wedgeX2, out double wedgeZ2);
Check(Math.Abs(wedgeX0) < 0.0001 && Math.Abs(wedgeZ0) < 0.0001,
      "a wedge keeps its leader on the point");
Check(Math.Abs(wedgeX1 - 100) < 0.0001 && Math.Abs(wedgeZ1 + 100) < 0.0001 &&
      Math.Abs(wedgeX2 + 100) < 0.0001 && Math.Abs(wedgeZ2 + 100) < 0.0001,
      "a wedge puts two flankers one gap out and one gap back");

Formations.Slot(FormationKind.Wedge, 0, 1, 100, 0, 1, out double loneX, out double loneZ);
Check(loneX == 0 && loneZ == 0, "a lone unit keeps the exact point it was given");
Formations.Slot(FormationKind.Line, 5, 3, 100, 0, 1, out double badX, out double badZ);
Check(badX == 0 && badZ == 0, "an index outside the group gets no offset");
Formations.Slot(FormationKind.Line, 1, 3, 0, 0, 1, out double noGapX, out double noGapZ);
Check(noGapX == 0 && noGapZ == 0, "zero spacing leaves the group on one point");

// The heading is what turns a row: the same line along +Z spreads over X instead of Z.
Formations.Slot(FormationKind.Line, 2, 3, 100, 0, 1, out double turnedX, out double turnedZ);
Check(Math.Abs(Math.Abs(turnedX) - 100) < 0.0001 && Math.Abs(turnedZ) < 0.0001,
      "a line turns with the direction the group is heading");

// ------------------------------------------------------------------
// Task 26: RTS camera controls
// ------------------------------------------------------------------

Check(Math.Abs(CameraRig.ClampZoom(0.01) - CameraRig.MinZoom) < 0.0001 &&
      Math.Abs(CameraRig.ClampZoom(99) - CameraRig.MaxZoom) < 0.0001 &&
      Math.Abs(CameraRig.ClampZoom(1.5) - 1.5) < 0.0001,
      "camera zoom is clamped between the closest and furthest factors");
Check(Math.Abs(CameraRig.ZoomedIn(1) - (1 + CameraRig.ZoomStep)) < 0.0001 &&
      Math.Abs(CameraRig.ZoomedOut(1) - (1 - CameraRig.ZoomStep)) < 0.0001 &&
      Math.Abs(CameraRig.ZoomBy(CameraRig.MaxZoom, 1) - CameraRig.MaxZoom) < 0.0001,
      "zoom in/out step by one notch and stop at the clamp");

// Zoom changes the framing without letting the camera reach the floor.
Check(Math.Abs(CameraRig.HeightFor(260, 2) - 130) < 0.0001 &&
      Math.Abs(CameraRig.HeightFor(260, 0.5) - 520) < 0.0001 &&
      Math.Abs(CameraRig.HeightFor(20, 1) - 20) < 0.0001,
      "zooming in halves the authored height, zooming out doubles it");
Check(Math.Abs(CameraRig.HeightFor(260, CameraRig.MaxZoom) - 65) < 0.0001 &&
      CameraRig.HeightFor(60, CameraRig.MaxZoom) >= CameraRig.MinHeight,
      "zoom never pushes the camera into the floor");
Check(Math.Abs(CameraRig.DepthFor(-170, 2) + 85) < 0.0001,
      "the depth offset scales with the same zoom as the height");
Check(Math.Abs(CameraRig.PanSpeedFor(600, 2) - 300) < 0.0001 &&
      Math.Abs(CameraRig.PanSpeedFor(600, 0.5) - 1200) < 0.0001,
      "panning is slower when zoomed in and faster when zoomed out");

// Edge panning: each edge asks for the ground direction under it, diagonals normalised.
Check(CameraRig.EdgeDirection(2, 400, 800, 800, 12, out double edgeLeftX, out double edgeLeftZ) &&
      edgeLeftX < 0 && edgeLeftZ == 0,
      "the left edge pans left");
Check(CameraRig.EdgeDirection(798, 400, 800, 800, 12, out double edgeRightX, out _) && edgeRightX > 0,
      "the right edge pans right");
Check(CameraRig.EdgeDirection(400, 2, 800, 800, 12, out double edgeTopX, out double edgeTopZ) &&
      edgeTopX == 0 && edgeTopZ > 0,
      "the top edge pans forward");
Check(CameraRig.EdgeDirection(400, 798, 800, 800, 12, out _, out double edgeBottomZ) && edgeBottomZ < 0,
      "the bottom edge pans backward");
Check(CameraRig.EdgeDirection(2, 2, 800, 800, 12, out double cornerX, out double cornerZ) &&
      Math.Abs(Math.Sqrt(cornerX * cornerX + cornerZ * cornerZ) - 1) < 0.0001,
      "a corner pans diagonally at the same speed as a straight edge");
Check(!CameraRig.EdgeDirection(400, 400, 800, 800, 12, out _, out _),
      "the middle of the screen does not pan");
Check(!CameraRig.EdgeDirection(400, 400, 0, 800, 12, out _, out _),
      "a zero-size viewport does not pan");

Check(CameraRig.KeyDirection(false, true, false, false, out double keyRightX, out double keyRightZ) &&
      keyRightX == 1 && keyRightZ == 0,
      "holding right pans right");
Check(!CameraRig.KeyDirection(true, true, true, true, out _, out _),
      "opposite keys held together cancel out");
Check(!CameraRig.KeyDirection(false, false, false, false, out _, out _),
      "no keys held does not pan");
Check(CameraRig.KeyDirection(true, false, true, false, out double diagX, out double diagZ) &&
      Math.Abs(Math.Sqrt(diagX * diagX + diagZ * diagZ) - 1) < 0.0001,
      "two keys held pan diagonally at the same speed");

CameraRig.Step(100, 100, 1, 0, 60, out double stepX, out double stepZ);
Check(Math.Abs(stepX - 160) < 0.0001 && Math.Abs(stepZ - 100) < 0.0001,
      "a pan step moves the focus by the distance it was given");
CameraRig.Step(100, 100, 1, 1, 60, out double diagStepX, out double diagStepZ);
Check(Math.Abs(Math.Sqrt((diagStepX - 100) * (diagStepX - 100) + (diagStepZ - 100) * (diagStepZ - 100)) - 60) < 0.0001,
      "a diagonal pan step moves 60 units in total, not 60 in each axis");
CameraRig.Step(100, 100, 0, 0, 60, out double stillX, out double stillZ);
Check(stillX == 100 && stillZ == 100, "no direction leaves the focus where it was");

Check(CameraRig.TryDirection("left", out double wordLeftX, out _) && wordLeftX == -1 &&
      CameraRig.TryDirection("Forward", out double wordForwardX, out double wordForwardZ) &&
      wordForwardX == 0 && wordForwardZ == 1 &&
      CameraRig.TryDirection("back", out _, out double wordBackZ) && wordBackZ == -1 &&
      !CameraRig.TryDirection("sideways", out _, out _),
      "pan words read left, right, forward and backward");

var cameraVars = new EventVariables();
Check(Math.Abs(CameraRig.ZoomFrom(cameraVars) - CameraRig.DefaultZoom) < 0.0001,
      "camera zoom starts at the authored framing");
cameraVars.SetGlobal(CameraRig.ZoomVariable, ValueBox.OfNumber(9));
Check(Math.Abs(CameraRig.ZoomFrom(cameraVars) - CameraRig.MaxZoom) < 0.0001,
      "an authored zoom beyond the clamp reads as the clamp");

// The new words parse from sheets, including the multiword `my camera ...` variable.
var zoomInEvent = parser.Parse("when me appears\n  camera zoom in\n");
var zoomInSpec = ActionWords.Parse(((RawActionNode)zoomInEvent.Events[0].Body[0].Statement!).Words, _ => { });
Check(zoomInSpec.Verb == ActionVerb.ZoomCamera && zoomInSpec.Dir == "in",
      "`camera zoom in` parses to ZoomCamera");

var zoomByEvent = parser.Parse("when me appears\n  zoom camera by 0.2\n");
var zoomBySpec = ActionWords.Parse(((RawActionNode)zoomByEvent.Events[0].Body[0].Statement!).Words, _ => { });
Check(zoomBySpec.Verb == ActionVerb.ZoomCamera && zoomBySpec.HasNumber &&
      Math.Abs(zoomBySpec.Number - 0.2) < 0.000001,
      "`zoom camera by 0.2` parses to ZoomCamera with its amount");

var panEvent = parser.Parse("when me appears\n  pan camera left by 100\n");
var panSpec = ActionWords.Parse(((RawActionNode)panEvent.Events[0].Body[0].Statement!).Words, _ => { });
Check(panSpec.Verb == ActionVerb.PanCamera && panSpec.Dir == "left" && panSpec.Number == 100,
      "`pan camera left by 100` parses to PanCamera");

var zoomCondition = parser.Parse("when game starts\n  if my camera zoom is 2\n  set Zoomed to 1\n");
Check(zoomCondition.Events.Count == 1,
      "`if my camera zoom is 2` joins the multiword camera variable");

// `my camera zoom` / `my camera position` read the free-pan state (Task 26 Part D).
var cameraSheetActor = new FakeActor("Rifleman");
var cameraWorld = new FakeWorld();
cameraWorld.AddActor(cameraSheetActor);
var cameraRuntime = new EventRuntime();
var cameraContext = new EventContext
{
    Variables = new EventVariables(),
    World = cameraWorld,
    Me = cameraSheetActor,
    Runtime = cameraRuntime,
};
cameraContext.Variables.SetGlobal(CameraRig.ZoomVariable, ValueBox.OfNumber(2));
cameraContext.Variables.SetGlobal(CameraRig.PanXVariable, ValueBox.OfNumber(120));
cameraContext.Variables.SetGlobal(CameraRig.PanZVariable, ValueBox.OfNumber(-80));
Check(Math.Abs(cameraContext.ReadMy("camera zoom").AsNumber() - 2) < 0.0001,
      "`my camera zoom` reads the zoom global");
Check(cameraContext.ReadMy("camera position").AsText() == "120, 0, -80",
      "`my camera position` reads the pan focus as x, 0, z");

// ------------------------------------------------------------------
// Data-driven resources (Task 25 audit fix)
// ------------------------------------------------------------------

ResourceManager.Reset();
var resourceDataVars = new EventVariables();
var resourceParser = new EventParser();
Check(ResourceManager.IsResource("Minerals") && ResourceManager.IsResource("supply used") &&
      ResourceManager.IsResource("ore") && !ResourceManager.IsResource("Credits"),
      "without a data file the built-in resources and their spellings still parse");
Check(ResourceManager.CanonicalNames.Count == 9 &&
      ResourceManager.SupplyUsedName == "SupplyUsed" &&
      ResourceManager.SupplyCapName == "SupplyMax" &&
      ResourceManager.DefaultCostName == "Minerals",
      "the built-in resource table is the nine names the scenes already use");

// `set Resources to "..."` in Resources.events replaces the list and adds spellings.
var resourceFile = resourceParser.Parse(
    "when game starts\n" +
    "  set Resources to \"Credits, Tiberium, Supply, SupplyCap\"\n" +
    "  set ResourceAliases to \"Credits=creds,gold; Tiberium=tib\"\n" +
    "  set SupplyUsedResource to \"Supply\"\n" +
    "  set SupplyCapResource to \"SupplyCap\"\n" +
    "  set DefaultCostResource to \"Credits\"\n");
ResourceManager.LoadData(resourceFile);
Check(ResourceManager.IsResource("credits") && ResourceManager.IsResource("gold") &&
      ResourceManager.IsResource("tib") && !ResourceManager.IsResource("Minerals"),
      "a data file declares its own resources and spellings");
Check(ResourceManager.CanonicalOrName("creds") == "Credits" &&
      ResourceManager.SupplyUsedName == "Supply" &&
      ResourceManager.SupplyCapName == "SupplyCap" &&
      ResourceManager.DefaultCostName == "Credits",
      "the declared names drive canonical lookup, supply and the default cost resource");

// A building costing the new resources is gated by them, and `<Building>Cost` means Credits.
resourceDataVars.SetGlobal("Credits", ValueBox.OfNumber(100));
resourceDataVars.SetGlobal("Tiberium", ValueBox.OfNumber(0));
resourceDataVars.SetGlobal("Supply", ValueBox.OfNumber(5));
resourceDataVars.SetGlobal("SupplyCap", ValueBox.OfNumber(10));
resourceDataVars.SetGlobal("BarracksCost", ValueBox.OfNumber(150));
var resourceCosts = new EventVariables();
resourceCosts.SetGlobal("Credits", ValueBox.OfNumber(200));
resourceCosts.SetGlobal("BarracksCost", ValueBox.OfNumber(150));
resourceCosts.SetGlobal("SupplyCap", ValueBox.OfNumber(10));
Check(!ResourceManager.CanAfford(resourceDataVars, "Barracks"),
      "`<Building>Cost` is charged against the declared default resource");
Check(ResourceManager.TryCost(resourceCosts, "Barracks", "Credits", out double creditsCost) &&
      creditsCost == 150 &&
      !ResourceManager.TryCost(resourceCosts, "Unknown", "Credits", out _),
      "a cost variable names any declared resource");
resourceCosts.SetGlobal("BarracksTiberiumCost", ValueBox.OfNumber(10));
resourceCosts.SetGlobal("Tiberium", ValueBox.OfNumber(4));
Check(!ResourceManager.CanAfford(resourceCosts, "Barracks"),
      "a declared resource with no stock blocks the purchase");
resourceCosts.SetGlobal("Tiberium", ValueBox.OfNumber(20));
Check(ResourceManager.CanAfford(resourceCosts, "Barracks"),
      "enough of every declared resource makes it affordable");
resourceCosts.SetGlobal("BarracksSupplyCost", ValueBox.OfNumber(5));
resourceCosts.SetGlobal("Supply", ValueBox.OfNumber(8));
Check(!ResourceManager.CanAfford(resourceCosts, "Barracks") &&
      Math.Abs(ResourceManager.SupplyCap(resourceCosts) - 10) < 0.0001,
      "supply costs are checked against the declared supply used and cap");

// `Credits is higher than 5` parses as a resource condition once the file declared it.
var creditCondition = resourceParser.Parse("when game starts\n  if credits is higher than 5\n  set Rich to 1\n");
Check(creditCondition.Events.Count == 1, "a declared resource spelling works in a condition");

// A file that declares nothing (or no file at all) restores the built-in nine.
ResourceManager.LoadData(resourceParser.Parse("when game starts\n  set Score to 1\n"));
Check(ResourceManager.IsResource("Minerals") && !ResourceManager.IsResource("Credits") &&
      ResourceManager.CanonicalNames.Count == 9,
      "a file with no declarations leaves the built-in resource table alone");
ResourceManager.LoadData(null);
Check(ResourceManager.IsResource("Gas") && ResourceManager.SupplyUsedName == "SupplyUsed",
      "loading no file restores the built-in resource table");

// ------------------------------------------------------------------
// Task 24: RTS minimap
// ------------------------------------------------------------------

var minimapVars = new EventVariables();
Check(Minimap.Enabled(minimapVars) &&
      Math.Abs(Minimap.ExtentFrom(minimapVars) - Minimap.DefaultExtent) < 0.0001 &&
      Math.Abs(Minimap.SizeFrom(minimapVars) - Minimap.DefaultSize) < 0.0001 &&
      Minimap.GridFrom(minimapVars) == Minimap.DefaultGrid,
      "the minimap starts on, 2000 world units across, 220 pixels and 32 cells per side");
minimapVars.SetGlobal(Minimap.EnabledVariable, ValueBox.OfNumber(0));
Check(!Minimap.Enabled(minimapVars), "`set Minimap to 0` hides the panel");
minimapVars.SetGlobal(Minimap.GridVariable, ValueBox.OfNumber(999));
Check(Minimap.GridFrom(minimapVars) == Minimap.MaxGrid, "the minimap grid is clamped");

Minimap.WorldToPanel(0, 0, 0, 0, 200, 1000, out double mapCenterX, out double mapCenterY);
Check(Math.Abs(mapCenterX - 100) < 0.0001 && Math.Abs(mapCenterY - 100) < 0.0001,
      "the world centre is the middle of the minimap");
Minimap.WorldToPanel(0, 1000, 0, 0, 200, 1000, out _, out double mapNorthY);
Check(Math.Abs(mapNorthY) < 0.0001, "world +Z runs up the minimap");
Minimap.WorldToPanel(1000, 0, 0, 0, 200, 1000, out double mapEastX, out _);
Check(Math.Abs(mapEastX - 200) < 0.0001, "world +X runs right across the minimap");
Minimap.PanelToWorld(150, 50, 0, 0, 200, 1000, out double mapRoundX, out double mapRoundZ);
Check(Math.Abs(mapRoundX - 500) < 0.0001 && Math.Abs(mapRoundZ - 500) < 0.0001,
      "a minimap point maps back to its world point");
Check(Minimap.InPanel(10, 190, 200) && !Minimap.InPanel(-1, 10, 200),
      "minimap points outside the panel are rejected");

Minimap.DragRect(150, 150, 50, 50, 0, 0, 200, 1000,
    out double dragMinX, out double dragMinZ, out double dragMaxX, out double dragMaxZ);
Check(Math.Abs(dragMinX + 500) < 0.0001 && Math.Abs(dragMaxX - 500) < 0.0001 &&
      Math.Abs(dragMinZ + 500) < 0.0001 && Math.Abs(dragMaxZ - 500) < 0.0001,
      "a minimap drag works from either corner");
Minimap.DragRect(100, 100, 300, 300, 0, 0, 200, 1000,
    out double clippedMinX, out double clippedMinZ, out double clippedMaxX, out double clippedMaxZ);
Check(Math.Abs(clippedMinX) < 0.0001 && Math.Abs(clippedMaxX - 1000) < 0.0001 &&
      Math.Abs(clippedMaxZ) < 0.0001 && Math.Abs(clippedMinZ + 1000) < 0.0001,
      "a drag that leaves the panel is clipped to what the panel shows");
Check(Minimap.InRect(-10, -10, 10, 10, 0, 0) && !Minimap.InRect(-10, -10, 10, 10, 20, 0),
      "a world rectangle knows what it contains");

// Terrain, fog and units become one drawable frame.
var minimapTerrain = new TerrainMap();
minimapTerrain.MarkArea(0, 0, 200, UnitTypes.Ground.Water);
var minimapFog = new FogOfWarManager();
minimapFog.RevealArea(0, 0, 0, 200);
var minimapUnits = new[]
{
    new MinimapUnit(100, 100, 0, true),
    new MinimapUnit(9000, 0, 1, false),
};
var minimapShot = Minimap.Build(minimapVars, minimapTerrain, minimapFog, 0, 0, 0, 400, 300, minimapUnits);
Check(minimapShot.Columns == Minimap.MaxGrid &&
      minimapShot.Cells.Length == minimapShot.Columns * minimapShot.Rows,
      "a minimap frame samples one cell per grid square");
Check(minimapShot.CellAt(minimapShot.Columns / 2, minimapShot.Rows / 2).Water,
      "marked water shows up on the minimap");
Check(minimapShot.CellAt(minimapShot.Columns / 2, minimapShot.Rows / 2).Fog != FogState.Unexplored,
      "the area the fog was revealed for is lit on the minimap");
Check(minimapShot.CellAt(0, 0).Fog == FogState.Unexplored,
      "unexplored ground stays dark on the minimap");
Check(minimapShot.Units.Count == 1 && minimapShot.Units[0].Selected,
      "units outside the panel are dropped and selection is kept");
Check(minimapShot.ViewWidth > 0 && minimapShot.ViewHeight > 0 &&
      Math.Abs(minimapShot.ViewX - (minimapShot.PanelSize - minimapShot.ViewWidth) * 0.5) < 0.0001 &&
      Math.Abs(minimapShot.ViewY - (minimapShot.PanelSize - minimapShot.ViewHeight) * 0.5) < 0.0001,
      "the camera view rectangle sits centred on the panel");

// ------------------------------------------------------------------
// Task 15 Part H: moveplatform (moving platforms)
// ------------------------------------------------------------------

var mp = parser.Parse("when me appears\n  moveplatform Plat1 x 200 in 2\n");
var mpSpec = ActionWords.Parse(mp.Events[0].Body[0].Words, _ => { });
Check(mpSpec.Verb == ActionVerb.MovePlatform && mpSpec.Thing == "Plat1" && mpSpec.Dir == "x",
      "`moveplatform Plat1 x 200 in 2` parses to MovePlatform with thing \"Plat1\" and axis x");
Check(mpSpec.HasNumber && mpSpec.Number == 200 && mpSpec.Duration == 2 &&
      mpSpec.Easing == TweenEasing.Linear,
      "`moveplatform Plat1 x 200 in 2` carries distance 200, duration 2, linear easing");

var mpEase = parser.Parse("when me appears\n  moveplatform Plat2 y -100 in 1.5 easing ease in out\n");
var mpEaseSpec = ActionWords.Parse(mpEase.Events[0].Body[0].Words, _ => { });
Check(mpEaseSpec.Verb == ActionVerb.MovePlatform && mpEaseSpec.Thing == "Plat2" && mpEaseSpec.Dir == "y",
      "`moveplatform Plat2 y -100 in 1.5 easing ease in out` parses to MovePlatform with axis y");
Check(mpEaseSpec.HasNumber && mpEaseSpec.Number == -100 && mpEaseSpec.Duration == 1.5 &&
      mpEaseSpec.Easing == TweenEasing.EaseInOut,
      "`moveplatform ... easing ease in out` carries distance -100, duration 1.5, EaseInOut");

var mpBadAxis = parser.Parse("when me appears\n  moveplatform Plat1 w 200 in 2\n");
var mpBadAxisSpec = ActionWords.Parse(mpBadAxis.Events[0].Body[0].Words, _ => { });
Check(mpBadAxisSpec.Verb == ActionVerb.Unknown, "a bad axis (w) makes moveplatform unknown");

var mpNoIn = parser.Parse("when me appears\n  moveplatform Plat1 x 200\n");
var mpNoInSpec = ActionWords.Parse(mpNoIn.Events[0].Body[0].Words, _ => { });
Check(mpNoInSpec.Verb == ActionVerb.Unknown, "moveplatform without `in <time>` is unknown");

var mpBadTime = parser.Parse("when me appears\n  moveplatform Plat1 x 200 in slow\n");
var mpBadTimeSpec = ActionWords.Parse(mpBadTime.Events[0].Body[0].Words, _ => { });
Check(mpBadTimeSpec.Verb == ActionVerb.Unknown, "moveplatform with a non-number time is unknown");

var mpBare = parser.Parse("when me appears\n  moveplatform\n");
var mpBareSpec = ActionWords.Parse(mpBare.Events[0].Body[0].Words, _ => { });
Check(mpBareSpec.Verb == ActionVerb.Unknown, "moveplatform with no subject is unknown");

// Runtime: OngoingPlatform ping-pong stepping (pure math, no Flax needed).
var plat = new PhysicsMovement.OngoingPlatform
{
    Axis = "x",
    Distance = 100,
    Duration = 2,
    Easing = TweenEasing.Linear,
    StartX = 10, StartY = 0, StartZ = 0,
};
Check(Math.Abs(plat.Progress - 0) < 0.0001, "a fresh platform starts at progress 0");
Check(Math.Abs(plat.AxisOffset - 0) < 0.0001, "a fresh platform has axis offset 0");

plat.Advance(1); // half the duration
Check(Math.Abs(plat.Progress - 0.5) < 0.0001, "linear progress is 0.5 at half duration");
Check(Math.Abs(plat.AxisOffset - 50) < 0.0001, "linear axis offset is half the distance at half duration");

bool done = plat.Advance(1); // completes the forward leg
Check(done && plat.Direction == -1, "the forward leg completes and direction flips to return");
Check(Math.Abs(plat.Elapsed - 0) < 0.0001, "elapsed resets to 0 when a leg completes");
Check(Math.Abs(plat.AxisOffset - 100) < 0.0001, "at the forward endpoint the offset equals the full distance");

plat.Advance(1); // half the return leg
Check(Math.Abs(plat.Progress - 0.5) < 0.0001, "return progress is 0.5 at half the return duration");
Check(Math.Abs(plat.AxisOffset - 50) < 0.0001, "return axis offset is half the distance");

done = plat.Advance(1); // completes the return leg
Check(done && plat.Direction == 1, "the return leg completes and direction flips back to forward");
Check(Math.Abs(plat.AxisOffset - 0) < 0.0001, "at the return endpoint the offset is back to 0 (no drift across legs)");

// Easing: EaseIn at half duration = 0.25
var easePlat = new PhysicsMovement.OngoingPlatform
{
    Axis = "z",
    Distance = 100,
    Duration = 2,
    Easing = TweenEasing.EaseIn,
    StartX = 0, StartY = 0, StartZ = 0,
};
easePlat.Advance(1);
Check(Math.Abs(easePlat.Progress - 0.25) < 0.0001, "EaseIn gives progress 0.25 at half duration (t*t)");

// Zero duration snaps immediately
var instantPlat = new PhysicsMovement.OngoingPlatform
{
    Axis = "x",
    Distance = 50,
    Duration = 0,
    StartX = 5, StartY = 0, StartZ = 0,
};
Check(instantPlat.Progress == 1, "a zero-duration platform snaps to progress 1");
Check(Math.Abs(instantPlat.AxisOffset - 50) < 0.0001, "a zero-duration platform applies the full distance");

// ---- Task 16 Part D: typed enemy definitions ----
var typeParser = new EventParser();
var zombieProg = typeParser.Parse(@"when me appears
  enemy type ""Zombie""
  my type is Zombie
  my health is lower than 60
  set Typed to 1
");
Check(zombieProg.Events.Count == 1 && zombieProg.Events[0].Body.Count == 4,
    "`enemy type \"Zombie\"` parses as a body line");
Check(zombieProg.Events[0].Body[0].Statement is RawActionNode typeAction &&
      typeAction.Words[0].Text == "enemy" && typeAction.Words[2].Text == "Zombie",
    "the `enemy type` line keeps the type name");

var typeRuntime = new EventRuntime();
var zombieActor = new object();
typeRuntime.AddSheet(zombieActor, zombieProg);
typeRuntime.Tick(0);
Check(typeRuntime.Variables.GetLocal(zombieActor, "Speed").AsNumber() == 2,
    "Zombie sets my Speed to 2");
Check(typeRuntime.Variables.GetLocal(zombieActor, "SightDistance").AsNumber() == 10,
    "Zombie sets my SightDistance to 10");
Check(typeRuntime.Variables.GetLocal(zombieActor, "Health").AsNumber() == 50,
    "Zombie sets my Health to 50");
CheckStrings("Claws", typeRuntime.Variables.GetLocal(zombieActor, "Weapon").AsText(),
    "Zombie sets my Weapon to \"Claws\"");
Check(typeRuntime.Variables.GetLocal(zombieActor, "Hostile").AsBoolean(),
    "Zombie sets my Hostile to true");
Check(!typeRuntime.Variables.GetLocal(zombieActor, "ExplosionDeath").AsBoolean(),
    "Zombie death is not an explosion");
CheckStrings("Zombie", typeRuntime.Variables.GetLocal(zombieActor, "Type").AsText(),
    "enemy type records my Type");
Check(typeRuntime.Variables.GetGlobal("Typed").AsNumber() == 1,
    "`my type is Zombie` and `my health is lower than 60` read the applied stats");

var turretProg = typeParser.Parse("when me appears\n  enemy type \"Turret\"\n");
var turretRuntime = new EventRuntime();
var turretActor = new object();
turretRuntime.AddSheet(turretActor, turretProg);
turretRuntime.Tick(0);
Check(turretRuntime.Variables.GetLocal(turretActor, "Speed").AsNumber() == 0,
    "Turret sets my Speed to 0 (it never moves)");
Check(turretRuntime.Variables.GetLocal(turretActor, "SightDistance").AsNumber() == 15,
    "Turret sets my SightDistance to 15");
Check(turretRuntime.Variables.GetLocal(turretActor, "Health").AsNumber() == 100,
    "Turret sets my Health to 100");
CheckStrings("Cannon", turretRuntime.Variables.GetLocal(turretActor, "Weapon").AsText(),
    "Turret sets my Weapon to \"Cannon\" (it still shoots)");
Check(turretRuntime.Variables.GetLocal(turretActor, "ExplosionDeath").AsBoolean(),
    "Turret dies in an explosion");

var overrideRuntime = new EventRuntime();
overrideRuntime.Variables.SetGlobal("ZombieHealth", ValueBox.OfNumber(80));
var overrideActor = new object();
overrideRuntime.AddSheet(overrideActor, zombieProg);
overrideRuntime.Tick(0);
Check(overrideRuntime.Variables.GetLocal(overrideActor, "Health").AsNumber() == 80,
    "a ZombieHealth variable overrides the built-in health");

var unknownRuntime = new EventRuntime();
string unknownWarning = "";
unknownRuntime.OnRuntimeWarning = message => unknownWarning = message;
var unknownActor = new object();
unknownRuntime.AddSheet(unknownActor, typeParser.Parse("when me appears\n  enemy type \"Ghost\"\n"));
unknownRuntime.Tick(0);
Check(unknownWarning.Contains("Ghost"), "an unknown enemy type warns instead of inventing stats");
Check(!unknownRuntime.Variables.HasLocal(unknownActor, "Type"),
    "an unknown enemy type writes nothing");

// A data file (EnemyTypes.events) is read before any sheet runs, so a type is complete on
// its first `me appears` — the engine bridge calls EnemyTypes.LoadData on load/reload.
EnemyTypes.LoadData(typeParser.Parse("when game starts\n  set ZombieSpeed to 120\n  set ZombieSightDistance to 250\n"));
var dataRuntime = new EventRuntime();
var dataActor = new object();
dataRuntime.AddSheet(dataActor, zombieProg);
dataRuntime.Tick(0);
Check(dataRuntime.Variables.GetLocal(dataActor, "Speed").AsNumber() == 120,
    "EnemyTypes.events tunes Zombie Speed before the sheet runs");
Check(dataRuntime.Variables.GetLocal(dataActor, "SightDistance").AsNumber() == 250,
    "EnemyTypes.events tunes Zombie SightDistance before the sheet runs");
Check(dataRuntime.Variables.GetLocal(dataActor, "Health").AsNumber() == 50,
    "a stat left out of the data file keeps its built-in value");
EnemyTypes.LoadData(null);

var sightProg = typeParser.Parse("when me sees Player by my SightDistance\n  set Spotted to 1\n");
var sightDescriptor = TriggerClassifier.Classify(sightProg.Events[0]);
Check(sightDescriptor.Kind == TriggerKind.Sees && sightDescriptor.DistanceVariable == "SightDistance",
    "`when me sees Player by my SightDistance` reads a per-enemy range");

// ---- Task 16 Part A: random-angle wander & still mode ----
var wanderParser = new EventParser();
var wanderDefaultSpec = ActionWords.Parse(RawWords(wanderParser, "when me appears\n  wander\n"), message => { });
Check(wanderDefaultSpec.Verb == ActionVerb.Wander && wanderDefaultSpec.HasNumber &&
      wanderDefaultSpec.Number == 5 && wanderDefaultSpec.Duration == 0,
    "bare `wander` walks at the default speed 5 with a random leg length");
var wanderSpec = ActionWords.Parse(RawWords(wanderParser, "when me appears\n  wander speed 7 duration 0.5\n"), message => { });
Check(wanderSpec.Verb == ActionVerb.Wander && wanderSpec.HasNumber && wanderSpec.Number == 7 &&
      Math.Abs(wanderSpec.Duration - 0.5) < 0.0001,
    "`wander speed 7 duration 0.5` parses both qualifiers");
var wanderBadDuration = ActionWords.Parse(RawWords(wanderParser, "when me appears\n  wander duration soon\n"), message => { });
Check(wanderBadDuration.Verb == ActionVerb.Unknown,
    "a non-number `wander duration` is a friendly error");
var stillSpec = ActionWords.Parse(RawWords(wanderParser, "when me appears\n  still\n"), message => { });
Check(stillSpec.Verb == ActionVerb.Still, "`still` parses as its own action");

Rng.Seed(12345);
var wanderer = new Wanderer();
var wanderActor = new object();
wanderer.Start(wanderActor, 5, 0);
Check(wanderer.IsWandering(wanderActor), "`wander` puts the actor in wandering mode");
wanderer.TryGetLeg(wanderActor, out WanderLeg firstLeg);
Check(firstLeg.TicksRemaining >= Wanderer.MinTicks && firstLeg.TicksRemaining <= Wanderer.MaxTicks,
    "a random wander leg lasts 20-60 ticks");
Check(firstLeg.AngleDegrees >= 0 && firstLeg.AngleDegrees < 360 && firstLeg.AngleDegrees == Math.Floor(firstLeg.AngleDegrees),
    "a wander heading is a whole angle from 0 to 359");
double ticksBefore = firstLeg.TicksRemaining;
wanderer.Advance(wanderActor, 0.016);
Check(wanderer.TryGetLeg(wanderActor, out WanderLeg afterOneTick) &&
      afterOneTick.TicksRemaining == ticksBefore - 1,
    "each tick spends one tick of the current leg");

var shortWanderer = new Wanderer();
var shortActor = new object();
shortWanderer.Start(shortActor, 3, 0.05);
shortWanderer.TryGetLeg(shortActor, out WanderLeg shortLeg);
Check(shortLeg.TicksRemaining == 3, "`duration 0.05` is three ticks at 60 ticks per second");
bool turned = false;
for (int tick = 0; tick < 3; tick++)
    turned = shortWanderer.Advance(shortActor, 0.016) || turned;
Check(turned, "a leg that runs out starts a new one (a fresh random angle)");
shortWanderer.TryGetLeg(shortActor, out WanderLeg newLeg);
Check(newLeg.TicksRemaining >= Wanderer.MinTicks && newLeg.TicksRemaining <= Wanderer.MaxTicks,
    "the replacement leg is again a random 20-60 ticks");

shortWanderer.SetStill(shortActor);
Check(shortWanderer.IsStill(shortActor) && !shortWanderer.IsWandering(shortActor),
    "`still` stops wandering and marks the actor still");
shortWanderer.Clear(shortActor);
Check(!shortWanderer.IsStill(shortActor), "a real move clears still mode");

var wanderRuntime = new EventRuntime();
var wanderContextActor = new object();
var wanderContext = new EventContext
{
    Variables = wanderRuntime.Variables,
    World = wanderRuntime.World,
    Me = wanderContextActor,
    Runtime = wanderRuntime,
};
var wanderingProg = new EventParser().Parse("when me appears\n  me is wandering\n");
var wanderingNode = Conditions.Parse(wanderingProg.Events[0].Body[0].Words);
Check(wanderingNode.Kind == ConditionKind.Wandering && !Conditions.Evaluate(wanderingNode, wanderContext),
    "`me is wandering` is false before wandering starts");
wanderRuntime.Wander.Start(wanderContextActor, 5, 0);
Check(Conditions.Evaluate(wanderingNode, wanderContext),
    "`me is wandering` is true while a wander leg is active");

var stillProg = new EventParser().Parse("when me appears\n  me is still\n");
var stillNode = Conditions.Parse(stillProg.Events[0].Body[0].Words);
Check(stillNode.Kind == ConditionKind.Still && !Conditions.Evaluate(stillNode, wanderContext),
    "`me is still` is false for a wandering actor");
wanderRuntime.Wander.SetStill(wanderContextActor);
Check(Conditions.Evaluate(stillNode, wanderContext),
    "`me is still` is true after `still`");

// ---- Task 16 Part B: hearing / sound reaction ----
var hearParser = new EventParser();
var hearDescriptor = TriggerClassifier.Classify(hearParser.Parse("when me hears \"gunshot\" by 12\n  set my mode to \"chase\"\n").Events[0]);
Check(hearDescriptor.Kind == TriggerKind.Hears && hearDescriptor.RequireName == "gunshot" &&
      hearDescriptor.Distance == 12,
    "`when me hears \"gunshot\" by 12` classifies to Hears with the sound name and range");
var hearStatDescriptor = TriggerClassifier.Classify(hearParser.Parse("when me hears \"gunshot\" by my SightDistance\n  set my mode to \"chase\"\n").Events[0]);
Check(hearStatDescriptor.Kind == TriggerKind.Hears && hearStatDescriptor.Distance == -1 &&
      hearStatDescriptor.DistanceVariable == "SightDistance",
    "`by my SightDistance` reads the listener's own variable as its hearing range");
var hearCondition = Conditions.Parse(hearParser.Parse("when me appears\n  me hears \"gunshot\" by 12\n").Events[0].Body[0].Words);
Check(hearCondition.Kind == ConditionKind.Hears && hearCondition.Thing == "gunshot" &&
      hearCondition.Distance == 12,
    "`me hears \"gunshot\" by 12` parses as a polled hearing condition");

// The bus: a sound carries as far as its radius (or the listener's `by`), and walls stop it.
var busWorld = new FakeWorld();
var busListener = new FakeActor("Guard");
var busGunman = new FakeActor("Player");
busWorld.AddActor(busListener);
busWorld.AddActor(busGunman);
busWorld.SetPosition(busListener, 0, 0);
var bus = new SoundBus();
bus.Emit("gunshot", busGunman, 10, 0, 0, SoundBus.DefaultRadius, 0);
Check(bus.IsAudible("gunshot", busListener, busWorld, 12),
    "a gunshot 10 units away is heard with `by 12`");
Check(!bus.IsAudible("gunshot", busListener, busWorld, 5),
    "`by 5` keeps that same gunshot out of range");
Check(!bus.IsAudible("footsteps", busListener, busWorld, 12),
    "a differently named sound is not what `hears \"gunshot\"` answers");
Check(!bus.IsAudible("gunshot", busGunman, busWorld, 12),
    "the shooter does not hear its own gunshot");
busWorld.SetPointBlocked(busListener, 10, 0, true);
Check(!bus.IsAudible("gunshot", busListener, busWorld, 12),
    "a wall between the listener and the shot blocks the sound");
busWorld.SetPointBlocked(busListener, 10, 0, false);
bus.Expire(SoundBus.Memory + 0.1);
Check(!bus.IsAudible("gunshot", busListener, busWorld, 12),
    "a sound stops being audible once it has expired");

// Inside the runtime: one gunshot fires the trigger once, behind a wall it never fires.
var hearWorld = new FakeWorld();
var hearEnemy = new FakeActor("Shooter");
var hearShooter = new FakeActor("Player");
hearWorld.AddActor(hearEnemy);
hearWorld.AddActor(hearShooter);
hearWorld.SetPosition(hearEnemy, 0, 0);
var hearEngine = FreshEngine(hearWorld);
hearEngine.AddSheet(hearEnemy, hearParser.Parse("when me hears \"gunshot\" by 12\n  add 1 to my Alerts\n"));
hearEngine.Tick(0);
Check(hearEngine.Variables.GetLocal(hearEnemy, "Alerts").AsNumber() == 0,
    "silence raises no hearing trigger");
hearEngine.Sounds.Emit("gunshot", hearShooter, 10, 0, 0, SoundBus.DefaultRadius, 1);
hearEngine.Tick(1);
Check(hearEngine.Variables.GetLocal(hearEnemy, "Alerts").AsNumber() == 1,
    "a gunshot within range and in line of sight fires `when me hears`");
hearEngine.Tick(1.1);
Check(hearEngine.Variables.GetLocal(hearEnemy, "Alerts").AsNumber() == 1,
    "the same gunshot does not fire again while it is still on the bus");
hearEngine.Sounds.Emit("gunshot", hearShooter, 10, 0, 0, SoundBus.DefaultRadius, 2);
hearEngine.Tick(2);
Check(hearEngine.Variables.GetLocal(hearEnemy, "Alerts").AsNumber() == 2,
    "a second gunshot fires again (a sound is an event, not a state)");
hearWorld.SetPointBlocked(hearEnemy, 10, 0, true);
hearEngine.Sounds.Emit("gunshot", hearShooter, 10, 0, 0, SoundBus.DefaultRadius, 3);
hearEngine.Tick(3);
Check(hearEngine.Variables.GetLocal(hearEnemy, "Alerts").AsNumber() == 2,
    "a gunshot behind a wall raises no hearing trigger");

// The condition reads the same bus, so it stays true while the sound is recent.
hearWorld.SetPointBlocked(hearEnemy, 10, 0, false);
hearEngine.Sounds.Clear();
var hearContext = new EventContext
{
    Variables = hearEngine.Variables,
    World = hearWorld,
    Me = hearEnemy,
    Runtime = hearEngine,
};
Check(!Conditions.Evaluate(hearCondition, hearContext), "`me hears` is false in silence");
hearEngine.Sounds.Emit("gunshot", hearShooter, 4, 0, 0, SoundBus.DefaultRadius, 4);
Check(Conditions.Evaluate(hearCondition, hearContext),
    "`me hears` is true while the gunshot is still recent");

// ---- Task 16 Part C: weapon & ammo data system ----
var weaponParser = new EventParser();
var parsedSetWeapon = weaponParser.Parse("when me appears\n  set weapon to \"Pistol\"\n");
Check(parsedSetWeapon.Events[0].Body[0].Statement is SetWeaponNode,
    "`set weapon to \"Pistol\"` parses as a weapon assignment");
var parsedWeaponShort = weaponParser.Parse("when me appears\n  weapon \"Shotgun\"\n");
Check(parsedWeaponShort.Events[0].Body[0].Statement is SetWeaponNode,
    "`weapon \"Shotgun\"` is the short form of the same action");
var parsedWeaponConditionLine = weaponParser.Parse("when me appears\n  weapon is Pistol\n");
Check(parsedWeaponConditionLine.Events[0].Body[0].IsCondition,
    "`weapon is Pistol` stays a condition line, not an action");
var parsedSetAmmo = weaponParser.Parse("when me appears\n  set ammo Pistol to 7\n");
Check(parsedSetAmmo.Events[0].Body[0].Statement is SetAmmoNode parsedSetAmmoNode &&
      parsedSetAmmoNode.Weapon == "Pistol",
    "`set ammo Pistol to 7` targets that weapon's count");
var parsedAddAmmo = weaponParser.Parse("when me appears\n  add -1 to ammo Pistol\n");
Check(parsedAddAmmo.Events[0].Body[0].Statement is AddAmmoNode parsedAddAmmoNode &&
      parsedAddAmmoNode.Weapon == "Pistol",
    "`add -1 to ammo Pistol` is the shot cost");
var parsedAddCurrentAmmo = weaponParser.Parse("when me appears\n  add -1 to ammo my Weapon\n");
Check(parsedAddCurrentAmmo.Events[0].Body[0].Statement is AddAmmoNode parsedCurrentAmmoNode &&
      parsedCurrentAmmoNode.WeaponVariable == "Weapon",
    "`add -1 to ammo my Weapon` spends the held weapon's ammo");
var parsedReload = weaponParser.Parse("when me appears\n  reload\n");
Check(parsedReload.Events[0].Body[0].Statement is ReloadNode, "`reload` parses as its own action");
var reloadDescriptor = TriggerClassifier.Classify(
    weaponParser.Parse("when reload completes\n  print \"done\"\n").Events[0]);
Check(reloadDescriptor.Kind == TriggerKind.ReloadComplete,
    "`when reload completes` classifies to ReloadComplete");

var weaponCondition = Conditions.Parse(
    weaponParser.Parse("when me appears\n  weapon is Pistol\n").Events[0].Body[0].Words);
Check(weaponCondition.Kind == ConditionKind.WeaponIs && weaponCondition.Name == "Pistol",
    "`weapon is Pistol` parses as a weapon condition");
var ammoConditionNode = Conditions.Parse(
    weaponParser.Parse("when me appears\n  ammo Pistol is 3\n").Events[0].Body[0].Words);
Check(ammoConditionNode.Kind == ConditionKind.AmmoIs && ammoConditionNode.Thing == "Pistol" &&
      ammoConditionNode.Right != null,
    "`ammo Pistol is 3` parses as an ammo condition");
var heldAmmoConditionNode = Conditions.Parse(
    weaponParser.Parse("when me appears\n  unless ammo my Weapon is 0\n").Events[0].Body[0].Words);
Check(heldAmmoConditionNode.Kind == ConditionKind.AmmoIs && heldAmmoConditionNode.Key == "Weapon",
    "`ammo my Weapon is 0` reads the held weapon's count");
var reloadingNode = Conditions.Parse(
    weaponParser.Parse("when me appears\n  reloading\n").Events[0].Body[0].Words);
Check(reloadingNode.Kind == ConditionKind.Reloading, "`reloading` parses as a condition");

// The sheet side: `set weapon` writes my Weapon, the shot spends one round, the data supplies
// the starting count, and `spawn Bullet` reads the held weapon's bullet type.
var gunWorld = new FakeWorld();
var gunEngine = FreshEngine(gunWorld);
var gunner = new FakeActor("Player");
gunEngine.AddSheet(gunner, weaponParser.Parse(@"when me appears
  set weapon to ""Pistol""
when Space is pressed
  unless reloading
  unless ammo my Weapon is 0
  add -1 to ammo my Weapon
"));
gunEngine.Tick(0);
CheckStrings("Pistol", gunEngine.Variables.GetLocal(gunner, "Weapon").AsText(),
    "`set weapon to \"Pistol\"` records my Weapon");
var gunContext = new EventContext
{
    Variables = gunEngine.Variables,
    World = gunWorld,
    Me = gunner,
    Runtime = gunEngine,
};
Check(WeaponData.ReadAmmo(gunContext, "Pistol") == 25,
    "an unfired weapon starts with the data's ammo");
Check(Conditions.Evaluate(weaponCondition, gunContext), "`weapon is Pistol` reads my Weapon");
gunEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Space" });
gunEngine.Tick(1);
Check(gunEngine.Variables.GetLocal(gunner, "PistolAmmo").AsNumber() == 24,
    "one shot spends one round of ammo");
var ammoNowNode = Conditions.Parse(
    weaponParser.Parse("when me appears\n  ammo Pistol is 24\n").Events[0].Body[0].Words);
Check(Conditions.Evaluate(ammoNowNode, gunContext), "`ammo Pistol is 24` reads the spent count");
Check(gunContext.ReadMy("Ammo").AsNumber() == 24,
    "`my Ammo` reads the held weapon's count (what the HUD line binds to)");

var heldPistol = WeaponData.ForActor(gunContext);
CheckStrings("Bullet", heldPistol.Bullet, "the held Pistol spawns the Bullet template");
gunEngine.Variables.SetLocal(gunner, "Weapon", ValueBox.OfText("Shotgun"));
var heldShotgun = WeaponData.ForActor(gunContext);
CheckStrings("ShotgunPellet", heldShotgun.Bullet, "switching weapons changes the bullet type");

// `reload` starts a timer, `reloading` suspends shooting, and the completion fires once.
var reloadGunWorld = new FakeWorld();
var reloadGunEngine = FreshEngine(reloadGunWorld);
var reloadGunner = new FakeActor("Player");
reloadGunEngine.AddSheet(reloadGunner, weaponParser.Parse(@"when me appears
  set weapon to ""Pistol""
when R is pressed
  reload
when reload completes
  add 1 to my Reloads
when Space is pressed
  unless reloading
  add 1 to my Shots
"));
reloadGunEngine.Tick(0);
reloadGunEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Space" });
reloadGunEngine.Tick(0.5);
Check(reloadGunEngine.Variables.GetLocal(reloadGunner, "Shots").AsNumber() == 1,
    "a weapon that is not reloading fires");
reloadGunEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "R" });
reloadGunEngine.Tick(1);
Check(reloadGunEngine.Weapons.IsReloading(reloadGunner, 1), "`reload` starts the weapon's timer");
reloadGunEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Space" });
reloadGunEngine.Tick(1.5);
Check(reloadGunEngine.Variables.GetLocal(reloadGunner, "Shots").AsNumber() == 1,
    "`unless reloading` suspends shooting during the reload");
reloadGunEngine.Tick(2.3);
Check(reloadGunEngine.Variables.GetLocal(reloadGunner, "Reloads").AsNumber() == 1,
    "`when reload completes` fires once the timer runs out");
reloadGunEngine.Tick(2.6);
Check(reloadGunEngine.Variables.GetLocal(reloadGunner, "Reloads").AsNumber() == 1,
    "a finished reload does not fire again on later ticks");

var clock = new WeaponClock();
var clockActor = new object();
clock.Start(clockActor, 0, 5);
Check(!clock.IsReloading(clockActor, 5) && clock.Completion(clockActor, 5) == 1,
    "a zero-second reload completes on the spot");
var cancelClock = new WeaponClock();
var cancelledActor = new object();
cancelClock.Start(cancelledActor, 3, 0);
cancelClock.Cancel(cancelledActor);
Check(!cancelClock.IsReloading(cancelledActor, 1) && cancelClock.Completion(cancelledActor, 4) == 0,
    "switching weapons cancels a reload without completing it");

// Ammo can be set outright, and never runs below zero.
var emptyWorld = new FakeWorld();
var emptyGunEngine = FreshEngine(emptyWorld);
var emptyGunner = new FakeActor("Player");
emptyGunEngine.AddSheet(emptyGunner, weaponParser.Parse(@"when me appears
  set weapon to ""Pistol""
  set ammo Pistol to 0
when R is pressed
  add -1 to ammo Pistol
"));
emptyGunEngine.Tick(0);
Check(emptyGunEngine.Variables.GetLocal(emptyGunner, "PistolAmmo").AsNumber() == 0,
    "`set ammo Pistol to 0` writes the actor's count");
emptyGunEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "R" });
emptyGunEngine.Tick(1);
Check(emptyGunEngine.Variables.GetLocal(emptyGunner, "PistolAmmo").AsNumber() == 0,
    "spending ammo past empty clamps at 0");

// A name nobody defined warns, but is still recorded so the sheet keeps working.
var unknownGunEngine = FreshEngine(new FakeWorld());
string unknownGunWarning = "";
unknownGunEngine.OnRuntimeWarning = message => unknownGunWarning = message;
var unknownGunner = new FakeActor("Player");
unknownGunEngine.AddSheet(unknownGunner, weaponParser.Parse("when me appears\n  set weapon to \"Banana\"\n"));
unknownGunEngine.Tick(0);
Check(unknownGunWarning.Contains("Banana"), "an unknown weapon warns instead of inventing stats");
CheckStrings("Banana", unknownGunEngine.Variables.GetLocal(unknownGunner, "Weapon").AsText(),
    "the unknown weapon name is still recorded");

// WeaponData.events is read before any sheet runs, so a table edit takes effect on the first shot.
WeaponData.LoadData(weaponParser.Parse(
    "when game starts\n  set PistolBullet to \"Rocket\"\n  set PistolReload to 0.4\n"));
WeaponData.TryGet(null, "Pistol", out WeaponStats tunedPistol);
CheckStrings("Rocket", tunedPistol.Bullet, "WeaponData.events tunes a weapon's bullet type");
Check(Math.Abs(tunedPistol.Reload - 0.4) < 0.0001 && tunedPistol.Damage == 10,
    "a field left out of the data file keeps its built-in value");
WeaponData.LoadData(weaponParser.Parse(
    "when game starts\n  set SMGBullet to \"Bullet\"\n  set SMGReload to 0.6\n"));
WeaponData.TryGet(null, "SMG", out WeaponStats tunedSmg);
Check(tunedSmg != null && tunedSmg.Bullet == "Bullet" && Math.Abs(tunedSmg.Reload - 0.6) < 0.0001,
    "a weapon that exists only as data variables is a valid weapon");
WeaponData.LoadData(null);

// The shipped data file and player sheet parse, and the table loads into real stats.
var weaponDataEvents = weaponParser.Parse(File.ReadAllText("Source/Game/GameFiles/WeaponData.events"));
Check(weaponDataEvents.Events.Count == 1 && weaponDataEvents.Events[0].Body.Count == 102,
    "WeaponData.events parses (six weapons, seventeen fields each)");
WeaponData.LoadData(weaponDataEvents);
WeaponData.TryGet(null, "Pistol", out WeaponStats filePistol);
Check(filePistol != null && filePistol.Ammo == 25 && Math.Abs(filePistol.Reload - 1.2) < 0.0001 &&
      filePistol.Bullet == "Bullet",
    "the shipped WeaponData.events defines the Pistol");
WeaponData.TryGet(null, "Rocket", out WeaponStats fileRocket);
Check(fileRocket != null && fileRocket.Explosive && Math.Abs(fileRocket.Blast - 320) < 0.0001 &&
      fileRocket.Speed < 200 && !fileRocket.Bounce,
    "the shipped WeaponData.events defines the Rocket's blast and slow flight");
WeaponData.LoadData(null);
var tkPlayerEvents = weaponParser.Parse(File.ReadAllText("Source/Game/GameFiles/TK_Player.events"));
Check(tkPlayerEvents.Events.Count > 0, "TK_Player.events parses (weapon swap, reload, ammo-gated fire)");

// ---- Task 16 Part C.5: weapon variants & wall impact ----
var variantParser = new EventParser();
var bounceSpec = ActionWords.Parse(RawWords(variantParser, "when me appears\n  bounce\n"), _ => { });
Check(bounceSpec.Verb == ActionVerb.Bounce, "`bounce` parses as a projectile reflection");
var explodeSpec = ActionWords.Parse(RawWords(variantParser, "when me appears\n  explode\n"), _ => { });
Check(explodeSpec.Verb == ActionVerb.Explode, "`explode` parses as a blast");
var explosiveAliasSpec = ActionWords.Parse(RawWords(variantParser, "when me appears\n  explosive\n"), _ => { });
Check(explosiveAliasSpec.Verb == ActionVerb.Explode, "`explosive` is the friendly alias of `explode`");
var explodeRadiusSpec = ActionWords.Parse(
    RawWords(variantParser, "when me appears\n  explode radius 250\n"), _ => { });
Check(explodeRadiusSpec.Verb == ActionVerb.Explode && Math.Abs(explodeRadiusSpec.Number - 250) < 0.0001,
    "`explode radius 250` carries its own blast radius");
var explodeBlastVarSpec = ActionWords.Parse(
    RawWords(variantParser, "when me appears\n  explode radius my blast\n"), _ => { });
Check(explodeBlastVarSpec.Verb == ActionVerb.Explode && explodeBlastVarSpec.AmountNode != null,
    "`explode radius my blast` reads the radius from the bullet's data");

// Per-weapon data: variants differ in the numbers that matter, not only in name.
WeaponData.TryGet(null, "Pistol", out WeaponStats variantPistol);
WeaponData.TryGet(null, "Shotgun", out WeaponStats variantShotgun);
WeaponData.TryGet(null, "Uzi", out WeaponStats variantUzi);
WeaponData.TryGet(null, "Grenade", out WeaponStats variantGrenade);
Check(variantShotgun.Multiplier == 6 && Math.Abs(variantShotgun.Spread - 12) < 0.0001,
    "the Shotgun fires six pellets across its spread");
Check(variantShotgun.Life < variantPistol.Life,
    "a shotgun pellet has shorter legs than a pistol round");
Check(variantUzi.FireRate < variantPistol.FireRate && variantUzi.Reload < variantPistol.Reload,
    "the Uzi fires faster and reloads faster than the Pistol");
Check(variantGrenade.Explosive && variantGrenade.Blast > fileRocket.Blast,
    "the Grenade explodes with the widest blast");
Check(!variantPistol.Explosive && !variantPistol.Bounce && !variantPistol.Pierce,
    "a pistol round is a plain bullet");

// The spread math: evenly fanned, centred on the aim, with the aim kept for a single pellet.
var shotgunAngles = BulletMath.SpreadAngles(12, 6);
Check(shotgunAngles.Length == 6 && Math.Abs(shotgunAngles[0] + 6) < 1e-9 &&
      Math.Abs(shotgunAngles[5] - 6) < 1e-9 && Math.Abs(shotgunAngles[2] + 1.2) < 1e-9,
    "six pellets fan evenly across the 12° spread");
Check(Math.Abs(shotgunAngles[0] + shotgunAngles[5]) < 1e-9 &&
      Math.Abs(shotgunAngles[1] + shotgunAngles[4]) < 1e-9,
    "the spread is symmetric around the aim");
Check(BulletMath.SpreadAngles(12, 1)[0] == 0 && BulletMath.SpreadAngles(12, 0).Length == 1,
    "one pellet keeps the exact heading");
var straightAngles = BulletMath.SpreadAngles(0, 4);
Check(straightAngles.Length == 4 && Array.TrueForAll(straightAngles, angle => angle == 0),
    "a weapon with no spread fires every pellet straight");

// The bounce math: a head-on hit reverses, a 45° graze mirrors, and the result is a unit vector.
BulletMath.Reflect(0, 1, 0, -1, out double bounceX, out double bounceZ);
Check(Math.Abs(bounceX) < 1e-9 && Math.Abs(bounceZ + 1) < 1e-9,
    "a head-on wall hit reverses the projectile");
BulletMath.Reflect(1, 1, -1, 0, out bounceX, out bounceZ);
Check(Math.Abs(bounceX + Math.Sqrt(0.5)) < 1e-9 && Math.Abs(bounceZ - Math.Sqrt(0.5)) < 1e-9,
    "a 45° graze reflects at the mirrored angle");
Check(Math.Abs((bounceX * bounceX + bounceZ * bounceZ) - 1) < 1e-9,
    "the reflected heading is a unit vector");
BulletMath.Reflect(2, 0, -3, 0, out bounceX, out bounceZ);
Check(Math.Abs(bounceX + 1) < 1e-9, "unnormalized inputs still reflect cleanly");
Check(BulletMath.WithinBlast(100, 0, 150) && !BulletMath.WithinBlast(200, 0, 150) &&
      !BulletMath.WithinBlast(0, 0, 0),
    "the blast radius decides who is caught in it");

// The bullet sheet the scene's templates share: flight, lifetime, wall impact, victim impact.
var tkBulletEvents = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/TK_Bullet.events"));
Check(tkBulletEvents.Events.Count == 5,
    "TK_Bullet.events parses (flight, lifetime, wall impact, enemy impact, player impact)");
var tkMuzzleEvents = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/TK4_MuzzleFlash.events"));
Check(tkMuzzleEvents.Events.Count == 1 && tkMuzzleEvents.Events[0].Body[0].IsCondition,
    "TK4_MuzzleFlash.events parses and gates the flash on the clone marker");
var tkExplosionEvents = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/TK4_Explosion.events"));
Check(tkExplosionEvents.Events.Count == 1 && tkExplosionEvents.Events[0].Body[0].IsCondition,
    "TK4_Explosion.events parses and gates the burst on the clone marker");
var wallBody = tkBulletEvents.Events[2].Body;
Check(wallBody.Count == 7 && wallBody[0].IsCondition && wallBody[3].IsCondition,
    "the wall event gates bounce and explode behind the bullet's own flags");
var wallBounce = ActionWords.Parse(((RawActionNode)wallBody[1].Statement!).Words, _ => { });
Check(wallBounce.Verb == ActionVerb.Bounce, "the wall event really calls `bounce`");
var wallExplode = ActionWords.Parse(((RawActionNode)wallBody[4].Statement!).Words, _ => { });
Check(wallExplode.Verb == ActionVerb.Explode, "the wall event really calls `explode`");
var victimBody = tkBulletEvents.Events[3].Body;
// The victim line reads the weapon's numbers through `my`, so check that shape really parses.
var knockSpec = ActionWords.Parse(
    RawWords(variantParser, "when me appears\n  push other back strength my knockback for 0.2 seconds\n"), _ => { });
Check(knockSpec.Verb == ActionVerb.Push && knockSpec.AmountNode is VariableValue knockVar &&
      knockVar.Owner == "my" && knockVar.Name == "knockback" && Math.Abs(knockSpec.Duration - 0.2) < 1e-9,
    "`push other back strength my knockback for 0.2 seconds` uses the weapon's own push");
var flightSpec = ActionWords.Parse(
    RawWords(variantParser, "when me appears\n  move forward speed my BulletSpeed\n"), _ => { });
Check(flightSpec.Verb == ActionVerb.Move && flightSpec.AmountNode is VariableValue flightVar &&
      flightVar.Owner == "my" && flightVar.Name == "BulletSpeed",
    "`move forward speed my BulletSpeed` flies at the stamped speed");
Check(victimBody.Count == 8 && victimBody[5].IsCondition && !victimBody[6].IsCondition,
    "the victim event damages, then stops or destroys the bullet (pierce keeps it flying)");

// ---- Task 16 Part E: TK4 particle effects (footprint, blood, gibs) ----
var footprintEvents = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/Footprint.events"));
Check(footprintEvents.Events.Count == 1 && footprintEvents.Events[0].Body[0].IsCondition,
    "Footprint.events parses and only runs on spawned clones");
var bloodEvents = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/Blood.events"));
Check(bloodEvents.Events.Count == 1 && bloodEvents.Events[0].Body[0].IsCondition,
    "Blood.events parses and only runs on spawned clones");
var gibEvents = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/Gib.events"));
Check(gibEvents.Events.Count == 1 && gibEvents.Events[0].Body[0].IsCondition,
    "Gib.events parses and only runs on spawned clones");

// Footprint gate: the trail falls while the player walks and stops when it stands.
var trailWorld = new FakeWorld();
var walker = new FakeActor("Player");
trailWorld.AddActor(walker);
var trailEngine = FreshEngine(trailWorld);
trailEngine.AddSheet(walker, variantParser.Parse(
    "when every 0.3 seconds\n  me is moving\n  add 1 to my Steps\n"));
trailEngine.Tick(0);
trailEngine.Tick(0.3);
Check(trailEngine.Variables.GetLocal(walker, "Steps").AsNumber() == 0,
    "a standing player leaves no footprints");
trailWorld.SetMoving(walker, true);
trailEngine.Tick(0.6);
trailEngine.Tick(0.9);
Check(trailEngine.Variables.GetLocal(walker, "Steps").AsNumber() == 2,
    "one footprint per 0.3 s while the player walks");
trailWorld.SetMoving(walker, false);
trailEngine.Tick(1.2);
Check(trailEngine.Variables.GetLocal(walker, "Steps").AsNumber() == 2,
    "the footprint trail stops when the player stands still");

// Blood rides the hurt: one spray per landed hit, at the body.
var bloodWorld = new FakeWorld();
var bloodVictim = new FakeActor("Player");
bloodWorld.AddActor(bloodVictim);
var bloodEngine = FreshEngine(bloodWorld);
bloodEngine.AddSheet(bloodVictim, variantParser.Parse(
    "when message \"hurt\" arrives\n  add 1 to my Sprays\n  add -10 to my Health\n"));
bloodEngine.Tick(0);
bloodEngine.RaisePush(bloodVictim, new PushSignal { Kind = TriggerKind.Message, Name = "hurt" });
bloodEngine.Tick(1);
Check(bloodEngine.Variables.GetLocal(bloodVictim, "Sprays").AsNumber() == 1 &&
      bloodEngine.Variables.GetLocal(bloodVictim, "Health").AsNumber() == -10,
    "a hurt message lands once and raises one blood spray");

// Gibs: the death watch fires once, not on every later tick.
var gibWorld = new FakeWorld();
var doomed = new FakeActor("Zombie");
gibWorld.AddActor(doomed);
var gibEngine = FreshEngine(gibWorld);
gibEngine.AddSheet(doomed, variantParser.Parse("when my health becomes 0\n  add 1 to my Bursts\n"));
gibEngine.Variables.SetLocal(doomed, "health", ValueBox.OfNumber(50));
gibEngine.Tick(0);
gibEngine.Variables.SetLocal(doomed, "health", ValueBox.OfNumber(0));
gibEngine.Tick(1);
gibEngine.Tick(2);
Check(gibEngine.Variables.GetLocal(doomed, "Bursts").AsNumber() == 1,
    "the gib burst fires once when health reaches 0");

// The shipped TK4 sheets carry the wiring those effects hang off.
var tkZombieEvents = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/Zombie.events"));
bool zombieTakesDamage = false, zombieDies = false;
foreach (var ev in tkZombieEvents.Events)
{
    var desc = TriggerClassifier.Classify(ev);
    if (desc.Kind == TriggerKind.Message && (desc.RequireName == "damage" || desc.Key == "damage"))
        zombieTakesDamage = true;
    if (desc.Kind == TriggerKind.WatchBecomes)
        zombieDies = true;
}
Check(zombieTakesDamage && zombieDies,
    "Zombie.events answers `damage` and bursts gibs on death (the TK4 enemy is killable)");
var tkEnemyEvents = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/Enemy.events"));
bool enemyTakesDamage = false, enemyDies = false;
foreach (var ev in tkEnemyEvents.Events)
{
    var desc = TriggerClassifier.Classify(ev);
    if (desc.Kind == TriggerKind.Message && (desc.RequireName == "damage" || desc.Key == "damage"))
        enemyTakesDamage = true;
    if (desc.Kind == TriggerKind.WatchBecomes)
        enemyDies = true;
}
Check(enemyTakesDamage && enemyDies, "Enemy.events sprays blood on damage and gibs on death");

// ---- Task 16 Part F: local multiplayer (2 players, one PC) ----
ConditionNode PlayerCondition(string line)
{
    var program = parser.Parse("when every 1 seconds\n  " + line + "\n");
    return Conditions.Parse(program.Events[0].Body[0].Words);
}

TriggerDescriptor ClassifyOne(string source)
{
    return TriggerClassifier.Classify(parser.Parse(source).Events[0]);
}

// The two-word per-player global is read by set / add / set text of.
var mpProgram = variantParser.Parse(@"when game starts
  set Player1 Score to 0
  add 1 to Player2 Kills
  set text of ScoreLabel to Player1 Score");
Check(mpProgram.Events[0].Body[0].Statement is SetNode mpSet && mpSet.Target.Name == "Player1 Score",
    "`set Player1 Score to 0` writes the two-word per-player global");
Check(mpProgram.Events[0].Body[1].Statement is AddNode mpAdd && mpAdd.Target.Name == "Player2 Kills",
    "`add 1 to Player2 Kills` counts a deathmatch kill");
Check(mpProgram.Events[0].Body[2].Statement is SetTextNode mpText &&
      mpText.Value is VariableValue mpTextValue && mpTextValue.Name == "Player1 Score",
    "a per-player counter also reads as a value (`set text of`)");
Check(variantParser.Parse("when game starts\n  set Player1Score to 4\n").Events[0].Body[0].Statement
        is SetNode runTogether && runTogether.Target.Name == "Player1 Score",
    "the run-together `Player1Score` reads as the same counter");

// Conditions.
var playerExistsCondition = PlayerCondition("player 1 exists");
Check(playerExistsCondition.Kind == ConditionKind.PlayerExists && playerExistsCondition.Player == 1,
    "`player 1 exists` reads player 1");
var playerMissingCondition = PlayerCondition("player 2 does not exist");
Check(playerMissingCondition.Kind == ConditionKind.PlayerExists && playerMissingCondition.Negated,
    "`player 2 does not exist` inverts the same check");
var playerNearCondition = PlayerCondition("player 2 is near me by 5");
Check(playerNearCondition.Kind == ConditionKind.PlayerNear && playerNearCondition.Player == 2 &&
      Math.Abs(playerNearCondition.Distance - 5) < 1e-9 && playerNearCondition.Thing == "me",
    "`player 2 is near me by 5` measures that player");
var playerTagCondition = PlayerCondition("player 1 has tag Player");
Check(playerTagCondition.Kind == ConditionKind.PlayerHasTag && playerTagCondition.Tag == "Player",
    "`player 1 has tag Player` reads the tag");
Check(PlayerCondition("all players dead").Kind == ConditionKind.AllPlayersDead,
    "`all players dead` parses as a condition with no condition mark");
var playerScoreCondition = PlayerCondition("Player1 Score is 3");
Check(playerScoreCondition.Kind == ConditionKind.Equal && playerScoreCondition.Name == "Player1 Score",
    "`Player1 Score is 3` compares the per-player counter");

// Triggers.
var joinsTrigger = ClassifyOne("when player 1 joins\n  print \"a\"\n");
Check(joinsTrigger.Kind == TriggerKind.PlayerJoins && joinsTrigger.Player == 1,
    "`when player 1 joins` classifies to PlayerJoins");
var diesTrigger = ClassifyOne("when player 2 dies\n  print \"a\"\n");
Check(diesTrigger.Kind == TriggerKind.PlayerDies && diesTrigger.Player == 2,
    "`when player 2 dies` classifies to PlayerDies");
var scoreTrigger = ClassifyOne("when player 1 score changes\n  print \"a\"\n");
Check(scoreTrigger.Kind == TriggerKind.PlayerScoreChanges && scoreTrigger.Player == 1,
    "`when player 1 score changes` watches that player's score");
var killTrigger = ClassifyOne("when player 1 kills player 2\n  print \"a\"\n");
Check(killTrigger.Kind == TriggerKind.PlayerKills && killTrigger.Player == 1 && killTrigger.Player2 == 2,
    "`when player 1 kills player 2` names killer and victim");
Check(ClassifyOne("when all players dead\n  print \"a\"\n").Kind == TriggerKind.AllPlayersDead,
    "`when all players dead` classifies to AllPlayersDead");

// `spawn <thing> at player <n>` keeps the player as the spawn spot.
var playerSpawnSpec = ActionWords.Parse(RawWords(variantParser, "when game starts\n  spawn Gib at player 2\n"), _ => { });
Check(playerSpawnSpec.Verb == ActionVerb.Spawn && playerSpawnSpec.Thing == "Gib" && playerSpawnSpec.To == "player 2",
    "`spawn Gib at player 2` spawns on that player");
var nearestSpawnSpec = ActionWords.Parse(RawWords(variantParser, "when game starts\n  spawn Bullet at nearest player\n"), _ => { });
Check(nearestSpawnSpec.Verb == ActionVerb.Spawn && nearestSpawnSpec.To == "nearest player",
    "`spawn Bullet at nearest player` keeps the nearest-player qualifier");

// Two players in one scene, each with its own sheet.
var mpWorld = new FakeWorld();
var mpP1 = new FakeActor("Player1");
var mpP2 = new FakeActor("Player2");
var mpGame = new FakeActor("Game");
var mpSeeker = new FakeActor("Zombie");
var mpBullet = new FakeActor("Bullet");
mpWorld.AddActor(mpP1);
mpWorld.AddActor(mpP2);
mpWorld.AddActor(mpGame);
mpWorld.AddActor(mpSeeker);
mpWorld.AddActor(mpBullet);
mpWorld.SetDistance(mpP1, mpP2, 12);
mpWorld.SetDistance(mpSeeker, mpP1, 9);
mpWorld.SetDistance(mpSeeker, mpP2, 3);
var mpEngine = FreshEngine(mpWorld);
mpEngine.AddSheet(mpGame, variantParser.Parse(@"when player 1 joins
  add 1 to P1Joins
when player 2 joins
  add 1 to P2Joins
when player 1 dies
  add 1 to P1Deaths
when player 2 dies
  add 1 to P2Deaths
when player 1 kills player 2
  add 1 to Player1 Kills
when Player1 Score changes
  add 1 to P1ScoreChanges
when all players dead
  add 1 to GameOvers"));
mpEngine.AddSheet(mpP1, variantParser.Parse(@"when every 1 seconds
  set TargetName to my TargetPlayer
when message ""ping"" arrives
  add 1 to my Pings"));
mpEngine.AddSheet(mpP2, variantParser.Parse(@"when message ""ping"" arrives
  add 1 to my Pings"));
mpEngine.Tick(0);
Check(mpEngine.FindPlayer(1) == mpP1 && mpEngine.FindPlayer(2) == mpP2,
    "two player actors each have a sheet");
Check(mpEngine.Variables.GetGlobal("P1Joins").AsNumber() == 1 &&
      mpEngine.Variables.GetGlobal("P2Joins").AsNumber() == 1,
    "each player raises `joins` once on the first poll");
Check(mpEngine.NearestPlayer(mpSeeker) == mpP2,
    "vision picks the closer of the two players");

// Targeting one player, or broadcasting to all of them.
mpEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "ping", Other = mpGame }, "player 2");
mpEngine.Tick(1);
Check(mpEngine.Variables.GetLocal(mpP2, "Pings").AsNumber() == 1 &&
      mpEngine.Variables.GetLocal(mpP1, "Pings").AsNumber() == 0,
    "`send message ... to player 2` reaches only player 2");
Check(mpEngine.Variables.GetGlobal("TargetName").AsText() == "Player2",
    "`my TargetPlayer` names the nearest player");
mpEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "ping", Other = mpGame }, "all players");
mpEngine.Tick(2);
Check(mpEngine.Variables.GetLocal(mpP1, "Pings").AsNumber() == 1 &&
      mpEngine.Variables.GetLocal(mpP2, "Pings").AsNumber() == 2,
    "`send message ... to all players` reaches both");

// Deathmatch: the bullet carries `my shooter`, so the kill credits that player.
mpEngine.Variables.SetLocal(mpBullet, "shooter", ValueBox.OfText("Player1"));
mpEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "damage", Other = mpBullet }, "Player2");
mpEngine.Tick(3);
Check(mpEngine.IsPlayerAlive(2), "a damage message alone does not kill a player");
mpEngine.Variables.SetLocal(mpP2, "Health", ValueBox.OfNumber(0));
mpEngine.Tick(4);
Check(mpEngine.Variables.GetGlobal("P2Deaths").AsNumber() == 1 &&
      mpEngine.Variables.GetGlobal("P1Deaths").AsNumber() == 0,
    "a player's health reaching 0 raises only that player's death trigger");
Check(mpEngine.Variables.GetGlobal("Player1 Kills").AsNumber() == 1,
    "deathmatch credits the kill to the other player (Player1 Kills = 1)");
Check(mpEngine.NearestPlayer(mpSeeker) == mpP1,
    "the nearest-player pick passes over a dead player while the other is alive");

// Co-op end: the run ends when the last player is gone, and only once.
mpEngine.Variables.SetLocal(mpP1, "Health", ValueBox.OfNumber(0));
mpEngine.Tick(5);
mpEngine.Tick(6);
Check(mpEngine.Variables.GetGlobal("GameOvers").AsNumber() == 1,
    "`when all players dead` fires once the last player dies");

// Score edge: changing the per-player counter raises the change trigger.
mpEngine.Variables.SetGlobal("Player1 Score", ValueBox.OfNumber(7));
mpEngine.Tick(7);
Check(mpEngine.Variables.GetGlobal("P1ScoreChanges").AsNumber() == 1,
    "`when Player1 Score changes` fires when that counter moves");
Check(mpEngine.FindPlayer(3) == null && mpEngine.PlayerNumberOf(mpP2) == 2,
    "only players 1 and 2 exist, and each actor knows its number");

// The shipped sheets carry the two-player wiring.
var tkEnemySheet = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/TK_Enemy.events"));
Check(tkEnemySheet.Events.Count == 9, $"TK_Enemy.events parses into 9 events (got {tkEnemySheet.Events.Count})");
var shippedGame = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/Game.events"));
bool gameTracksJoins = false, gameTracksDeaths = false, gameTracksKills = false, gameTracksAllDead = false;
foreach (var ev in shippedGame.Events)
{
    var desc = TriggerClassifier.Classify(ev);
    if (desc.Kind == TriggerKind.PlayerJoins) gameTracksJoins = true;
    if (desc.Kind == TriggerKind.PlayerDies) gameTracksDeaths = true;
    if (desc.Kind == TriggerKind.PlayerKills) gameTracksKills = true;
    if (desc.Kind == TriggerKind.AllPlayersDead) gameTracksAllDead = true;
}
Check(gameTracksJoins && gameTracksDeaths && gameTracksKills && gameTracksAllDead,
    "Game.events tracks joins, deaths, deathmatch kills and all-players-dead");
var shippedEnemy = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/Enemy.events"));
bool enemyTargetsNearest = false;
foreach (var ev in shippedEnemy.Events)
    if (TriggerClassifier.Classify(ev).Kind == TriggerKind.Sees &&
        TriggerClassifier.Classify(ev).RequireName == "player")
        enemyTargetsNearest = true;
Check(enemyTargetsNearest, "Enemy.events sees `player`, the group that matches Player1 or Player2");

// ---- Task 16 input follow-ups: per-player keys, gamepad buttons, rebinding ----

// The default map: player 1 on the arrows, player 2 on WASD.
var defaultInput = new PlayerInput();
Check(defaultInput.KeyFor(1, "Turn Left") == "Left Arrow" && defaultInput.KeyFor(1, "Shoot") == "Space" &&
      defaultInput.KeyFor(2, "Turn Left") == "A" && defaultInput.KeyFor(2, "Shoot") == "Left Ctrl",
    "player 1 turns with the arrows and shoots with Space; player 2 uses A/D and Left Ctrl");
Check(PlayerInput.TryReadAction("forward", out string shortForward) && shortForward == "Move Forward" &&
      PlayerInput.TryActionFor("Left Arrow", out string arrowAction) && arrowAction == "Turn Left",
    "an action reads by name or by short form, and a default key names its action");
Check(defaultInput.Resolve(2, "Left Arrow") == "A" && defaultInput.Resolve(1, "Left Arrow") == "Left Arrow" &&
      defaultInput.Resolve(2, "P") == "P" && defaultInput.Resolve(2, "shoot") == "Left Ctrl",
    "a written key or action resolves through that player's binding, and other keys stay themselves");
Check(defaultInput.OwnerOf("A") == 2 && defaultInput.OwnerOf("D1") == 1 && defaultInput.OwnerOf("P") == 0,
    "a key knows its owning player, and `D1` matches the bridge's spelling of 1");

// Routing: two player sheets in one scene hear only their own keys.
var inWorld = new FakeWorld();
var inP1 = new FakeActor("Player1");
var inP2 = new FakeActor("Player2");
var inGame = new FakeActor("Game");
inWorld.AddActor(inP1);
inWorld.AddActor(inP2);
inWorld.AddActor(inGame);
var inEngine = FreshEngine(inWorld);
inEngine.AddSheet(inGame, variantParser.Parse("when P is pressed\n  add 1 to Pauses\nwhen 1 is pressed\n  add 1 to GlobalDigits"));
inEngine.AddSheet(inP1, variantParser.Parse(@"
when Left Arrow is pressed
  add 1 to my Lefts
when Space is pressed
  add 1 to my Shots
when Left Arrow is held
  add 1 to my HeldTicks
when Shoot is held
  add 1 to my PadShots"));
inEngine.AddSheet(inP2, variantParser.Parse(@"
when Left Arrow is pressed
  add 1 to my Lefts
when Space is pressed
  add 1 to my Shots
when Shoot is held
  add 1 to my PadShots"));
inEngine.Tick(0);
int inClock = 0;
void PressKey(string key)
{
    inEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = key, PlayerA = inEngine.PlayerForKey(key) });
    inEngine.Tick(++inClock);
}
PressKey("A");
Check(inEngine.Variables.GetLocal(inP2, "Lefts").AsNumber() == 1 && inEngine.Variables.GetLocal(inP1, "Lefts").AsNumber() == 0,
    "pressing A turns player 2 only — player 1's sheet ignores player 2's keys");
PressKey("Left Arrow");
Check(inEngine.Variables.GetLocal(inP1, "Lefts").AsNumber() == 1 && inEngine.Variables.GetLocal(inP2, "Lefts").AsNumber() == 1,
    "one `when Left Arrow is pressed` line answers player 1's arrow and player 2's A");
PressKey("Space");
Check(inEngine.Variables.GetLocal(inP1, "Shots").AsNumber() == 1 && inEngine.Variables.GetLocal(inP2, "Shots").AsNumber() == 0,
    "player 1's Space shoots for player 1 only");
PressKey("Left Ctrl");
Check(inEngine.Variables.GetLocal(inP2, "Shots").AsNumber() == 1, "player 2 shoots with Left Ctrl");
PressKey("P");
Check(inEngine.Variables.GetGlobal("Pauses").AsNumber() == 1, "an unbound key (P) stays global and reaches every sheet");
PressKey("1");
Check(inEngine.Variables.GetGlobal("GlobalDigits").AsNumber() == 1,
    "a player's key still reaches a global sheet (a menu can answer `when 1 is pressed`)");

double p1Held = inEngine.Variables.GetLocal(inP1, "HeldTicks").AsNumber();
inWorld.SetHeld("W", true);
inEngine.Tick(++inClock);
inEngine.Tick(++inClock);
inWorld.SetHeld("W", false);
Check(inEngine.Variables.GetLocal(inP1, "HeldTicks").AsNumber() == p1Held,
    "holding player 2's W does not hold player 1's `Left Arrow`");

// Gamepad: player 2's pad (slot 1) drives the same actions; player 1's sheet stays quiet.
double p2PadBefore = inEngine.Variables.GetLocal(inP2, "PadShots").AsNumber();
double p1PadBefore = inEngine.Variables.GetLocal(inP1, "PadShots").AsNumber();
inWorld.SetGamepadButton(1, "A", true);
inEngine.Tick(++inClock);
inEngine.Tick(++inClock);
inWorld.SetGamepadButton(1, "A", false);
inEngine.Tick(++inClock);
Check(inEngine.Variables.GetLocal(inP2, "PadShots").AsNumber() > p2PadBefore,
    "player 2's gamepad A holds their Shoot action");
Check(inEngine.Variables.GetLocal(inP1, "PadShots").AsNumber() == p1PadBefore,
    "player 1's pad is a different slot, so player 2's button never reaches them");

// Rebinding: `rebind player 2 Shoot`, capture the next key, persist it.
var optWorld = new FakeWorld();
var optActor = new FakeActor("Options");
optWorld.AddActor(optActor);
var optEngine = FreshEngine(optWorld);
optEngine.AddSheet(optActor, variantParser.Parse(@"
when me appears
  rebind player 2 Shoot
when binding changes
  add 1 to Bound
  set BoundKey to my LastBoundKey
when message ""reload bindings"" arrives
  load bindings"));
optEngine.Tick(0);
Check(optEngine.IsCapturingKey, "`rebind player 2 Shoot` arms a capture");
Check(optEngine.TryCompleteCapture("J", null), "the next key press completes the capture");
Check(optEngine.Input.KeyFor(2, "Shoot") == "J" &&
      optEngine.Variables.GetGlobal("Player2 ShootKey").AsText() == "J",
    "the captured key becomes player 2's Shoot binding and is saved to `Player2 ShootKey`");
Check(!optEngine.IsCapturingKey && !optEngine.TryCompleteCapture("K", null),
    "the capture ends after one key");
optEngine.Tick(1);
Check(optEngine.Variables.GetGlobal("Bound").AsNumber() == 1 &&
      optEngine.Variables.GetGlobal("BoundKey").AsText() == "J",
    "`when binding changes` fires with the captured key in `my LastBoundKey`");
optEngine.Input.SetBinding(2, "Shoot", "K");
optEngine.RaisePush(optActor, new PushSignal { Kind = TriggerKind.Message, Name = "reload bindings" });
optEngine.Tick(2);
Check(optEngine.Input.KeyFor(2, "Shoot") == "J",
    "`load bindings` restores what the last save stored");

// After a rebind the old key is dead and the new one works, for that player only.
var rbWorld = new FakeWorld();
var rbOptions = new FakeActor("Options");
var rbP2 = new FakeActor("Player2");
rbWorld.AddActor(rbOptions);
rbWorld.AddActor(rbP2);
var rbEngine = FreshEngine(rbWorld);
rbEngine.AddSheet(rbOptions, variantParser.Parse(@"
when me appears
  rebind player 2 Shoot
when message ""rebind sprint"" arrives
  rebind player 2 Sprint"));
rbEngine.AddSheet(rbP2, variantParser.Parse("when Space is pressed\n  add 1 to my Shots"));
rbEngine.Tick(0);
rbEngine.TryCompleteCapture("J", null);
rbEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "J", PlayerA = rbEngine.PlayerForKey("J") });
rbEngine.Tick(1);
rbEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Space", PlayerA = rbEngine.PlayerForKey("Space") });
rbEngine.Tick(2);
Check(rbEngine.Variables.GetLocal(rbP2, "Shots").AsNumber() == 1 && rbEngine.PlayerForKey("J") == 2,
    "after `rebind`, the new key shoots for that player and the old one no longer does");

// A gamepad button can be captured too, and the sheet's held check answers it.
rbEngine.RaisePush(rbOptions, new PushSignal { Kind = TriggerKind.Message, Name = "rebind sprint" });
rbEngine.Tick(3);
Check(rbEngine.TryCompleteCapture(null, "LeftShoulder"), "a gamepad button completes a capture");
Check(rbEngine.Input.ButtonFor(2, "Sprint") == "LeftShoulder",
    "the captured button becomes player 2's Sprint button");
rbWorld.SetGamepadButton(1, "LeftShoulder", true);
Check(rbEngine.IsKeyHeldFor(rbEngine.FindActorSheet("Player2"), "Sprint"),
    "`Sprint is held` answers the captured gamepad button");

// Grammar: the trigger, the condition and the three actions.
Check(ClassifyOne("when binding changes\n  print \"a\"\n").Kind == TriggerKind.BindingChanges,
    "`when binding changes` classifies to BindingChanges");
Check(PlayerCondition("capturing a key").Kind == ConditionKind.Capturing,
    "`capturing a key` parses as a condition");
var rebindProgram = variantParser.Parse("when me appears\n  rebind player 2 Move Forward\n");
Check(rebindProgram.Events[0].Body[0].Statement is RawActionNode rebindRaw &&
      rebindRaw.Words[0].Text == "rebind" && rebindRaw.Words.Count == 5,
    "`rebind player 2 Move Forward` parses as a rebind action with the two-word action");
Check(variantParser.Parse("when me appears\n  save bindings\n").Events[0].Body[0].Statement is RawActionNode saveRaw &&
      saveRaw.Words[1].Text == "bindings",
    "`save bindings` parses as an action");
Check(variantParser.Parse("when me appears\n  load bindings\n").Events[0].Body[0].Statement is RawActionNode loadRaw &&
      loadRaw.Words[1].Text == "bindings",
    "`load bindings` parses as an action");
Check(WordRegistry.All.Any(e => e.Kind == "action" && e.Word == "rebind") &&
      WordRegistry.All.Any(e => e.Kind == "trigger" && e.Word == "when binding changes") &&
      WordRegistry.All.Any(e => e.Kind == "condition" && e.Word == "capturing a key"),
    "the registry documents rebind, `when binding changes` and `capturing a key`");

// A label can show what a player presses: `bind <label> to Player2 ShootKey`.
var bindSettingProgram = variantParser.Parse("bind P2ShootLabel to Player2 ShootKey\n");
Check(bindSettingProgram.Events.Count == 1 &&
      bindSettingProgram.Events[0].Body[0].Statement is SetTextNode bindSettingText &&
      bindSettingText.Target == "P2ShootLabel" &&
      bindSettingText.Value is VariableValue bindSettingValue && bindSettingValue.Name == "Player2 ShootKey",
    "`bind P2ShootLabel to Player2 ShootKey` reads the saved binding");

// The shipped options menu: every row binds a real setting, and every click rebinds one.
var optionsEvents = variantParser.Parse(File.ReadAllText("Source/Game/GameFiles/TK4Options.events"));
var boundSettings = new List<string>();
var rebindTargets = new List<string>();
var rebindPlayers = new List<long>();
foreach (var problemEvent in optionsEvents.Events)
    foreach (var line in problemEvent.Body)
    {
        if (line.Statement is SetTextNode optionText && optionText.Value is VariableValue optionValue &&
            optionValue.Name.Length > "Key".Length && optionValue.Name.EndsWith("Key"))
            boundSettings.Add(optionValue.Name);
        if (line.Statement is RawActionNode optionRaw && optionRaw.Words[0].Text == "rebind")
        {
            rebindPlayers.Add((long)optionRaw.Words[2].Number);
            var actionWordsWritten = new List<string>();
            for (int i = 3; i < optionRaw.Words.Count; i++)
                actionWordsWritten.Add(optionRaw.Words[i].Text);
            rebindTargets.Add(string.Join(" ", actionWordsWritten));
        }
    }
Check(boundSettings.Count == 20 && boundSettings.All(name => defaultInput.HasSetting(name)),
    $"TK4Options.events binds all 20 player settings (got {boundSettings.Count})");
Check(rebindTargets.Count == 20 && rebindTargets.All(action => PlayerInput.Find(action) != null) &&
      rebindPlayers.All(player => player == 1 || player == 2),
    $"TK4Options.events rebinds 20 real actions for players 1 and 2 (got {rebindTargets.Count})");

// Clicking a row on the options menu arms that player's action.
var optMenuWorld = new FakeWorld();
var optMenu = new FakeActor("TK4Options");
optMenuWorld.AddActor(optMenu);
var optMenuEngine = FreshEngine(optMenuWorld);
optMenuEngine.AddSheet(optMenu, optionsEvents);
optMenuEngine.Tick(0);
Check(optMenuEngine.Input.KeyFor(1, "Move Forward") == "Up Arrow" &&
      optMenuEngine.Input.KeyFor(2, "Shoot") == "Left Ctrl",
    "opening the options menu keeps the default bindings and saves them");
optMenuEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.Clicked, Name = "P1Forward" });
optMenuEngine.Tick(1);
Check(optMenuEngine.IsCapturingKey && optMenuEngine.TryCompleteCapture("K", null) &&
      optMenuEngine.Input.KeyFor(1, "Move Forward") == "K" &&
      optMenuEngine.Variables.GetGlobal("Player1 ForwardKey").AsText() == "K",
    "clicking Move Forward on the options menu rebinds player 1 and saves it");
Check(optMenuEngine.Variables.GetGlobal("Leaving").AsNumber() == 0,
    "the options menu changes scene only once, through its `Leaving` guard");

// ---- Task 16 Part G: level progression & statistics ----

// Grammar: the level triggers, the statistic conditions/values and the actions.
Check(ClassifyOne("when level starts\n  print \"a\"\n").Kind == TriggerKind.SceneStart,
    "`when level starts` is the level start edge (the same signal as `when scene starts`)");
Check(ClassifyOne("when level completes\n  print \"a\"\n").Kind == TriggerKind.LevelComplete,
    "`when level completes` classifies to LevelComplete");
Check(ClassifyOne("when all enemies dead\n  print \"a\"\n").Kind == TriggerKind.AllEnemiesDead,
    "`when all enemies dead` classifies to AllEnemiesDead");
var levelNumberCondition = PlayerCondition("level number is 3");
Check(levelNumberCondition.Kind == ConditionKind.Equal && levelNumberCondition.Name == "LevelNumber",
    "`level number is 3` compares the 1-based level number");
var levelNameCondition = PlayerCondition("level is \"Hospital\"");
Check(levelNameCondition.Kind == ConditionKind.Equal && levelNameCondition.Name == "LevelName",
    "`level is \"Hospital\"` compares the level name");
var killsCondition = PlayerCondition("kills Zombie is 5");
Check(killsCondition.Kind == ConditionKind.Equal && killsCondition.Name == "Kills Zombie",
    "`kills Zombie is 5` reads that type's kill counter");
var accuracyCondition = PlayerCondition("accuracy is lower than 50");
Check(accuracyCondition.Kind == ConditionKind.Lower && accuracyCondition.Name == "Accuracy",
    "`accuracy is lower than 50` compares the hit percentage");

var statReads = parser.Parse(@"when game starts
  set A to kills total
  set B to kills Zombie
  set C to accuracy
  set D to time elapsed
  set E to level number
  set F to level name");
string StatTarget(int index) =>
    ((SetNode)statReads.Events[0].Body[index].Statement).Value is VariableValue statValue ? statValue.Name : "?";
Check(StatTarget(0) == "Kills Total" && StatTarget(1) == "Kills Zombie" && StatTarget(2) == "Accuracy" &&
      StatTarget(3) == "Time Elapsed" && StatTarget(4) == "LevelNumber" && StatTarget(5) == "LevelName",
    "kills total / kills <type> / accuracy / time elapsed / level number / level name read as values");

var killNode = parser.Parse("when me appears\n  add 1 to kills Zombie\n").Events[0].Body[0].Statement;
Check(killNode is AddKillNode killByType && killByType.EnemyType == "Zombie",
    "`add 1 to kills Zombie` parses as a typed kill");
var typedKillNode = parser.Parse("when me appears\n  add 1 to kills my Type\n").Events[0].Body[0].Statement;
Check(typedKillNode is AddKillNode killByVariable && killByVariable.TypeVariable == "Type",
    "`add 1 to kills my Type` reads the type from the enemy");
Check(parser.Parse("when game starts\n  next level\n").Events[0].Body[0].Statement is RawActionNode nextRaw &&
      nextRaw.Words[0].Text == "next" && nextRaw.Words[1].Text == "level",
    "`next level` parses as an action");
Check(parser.Parse("when game starts\n  set level to 2\n").Events[0].Body[0].Statement is RawActionNode setLevelRaw &&
      setLevelRaw.Words[1].Text == "level" && setLevelRaw.Words[3].Number == 2,
    "`set level to 2` parses as a level action, not a variable called level");
Check(parser.Parse("when game starts\n  save stats\n  load stats\n  wipe game variables\n")
        .Events[0].Body[2].Statement is RawActionNode wipeRaw && wipeRaw.Words[0].Text == "wipe",
    "`save stats` / `load stats` / `wipe game variables` parse as actions");
Check(WordRegistry.All.Any(e => e.Kind == "trigger" && e.Word == "when all enemies dead") &&
      WordRegistry.All.Any(e => e.Kind == "action" && e.Word == "next") &&
      WordRegistry.All.Any(e => e.Kind == "action" && e.Word == "set level to <n>") &&
      WordRegistry.All.Any(e => e.Kind == "action" && e.Word == "wipe") &&
      WordRegistry.All.Any(e => e.Kind == "value" && e.Word == "kills total"),
    "the registry documents the level triggers, the statistics and the level actions");

// Five zombie deaths count five kills, for the type and for the run total.
var killsWorld = new FakeWorld();
var killsGame = new FakeActor("Game");
killsWorld.AddActor(killsGame);
var killsEngine = FreshEngine(killsWorld);
killsEngine.AddSheet(killsGame, parser.Parse("when message \"kill\" arrives\n  add 1 to kills Zombie"));
for (int i = 0; i < 5; i++)
{
    killsEngine.RaisePush(killsGame, new PushSignal { Kind = TriggerKind.Message, Name = "kill" });
    killsEngine.Tick(i);
}
Check(killsEngine.Variables.GetGlobal("Kills Zombie").AsNumber() == 5 &&
      killsEngine.Variables.GetGlobal("Kills Total").AsNumber() == 5,
    "five zombie kills read back as `kills Zombie` = 5 and `Kills Total` = 5");

// A generic enemy sheet counts its own type.
var typedWorld = new FakeWorld();
var typedEnemy = new FakeActor("Guard");
typedWorld.AddActor(typedEnemy);
var typedEngine = FreshEngine(typedWorld);
typedEngine.AddSheet(typedEnemy, parser.Parse("when message \"die\" arrives\n  add 1 to kills my Type"));
typedEngine.Variables.SetLocal(typedEnemy, "Type", ValueBox.OfText("Turret"));
typedEngine.RaisePush(typedEnemy, new PushSignal { Kind = TriggerKind.Message, Name = "die" });
typedEngine.Tick(0);
Check(typedEngine.Variables.GetGlobal("Kills Turret").AsNumber() == 1,
    "`add 1 to kills my Type` counts a kill for the type the enemy was given");

// Level start, `next level` and `set level to`.
var levelWorld = new FakeWorld();
var levelGame = new FakeActor("Game");
levelWorld.AddActor(levelGame);
var levelEngine = FreshEngine(levelWorld);
var advanced = new List<string>();
levelEngine.OnLevelAdvance = name => advanced.Add(name);
levelEngine.Levels.SetOrder(new[] { "Level1", "Level2", "Level3" });
levelEngine.AddSheet(levelGame, parser.Parse(@"when level starts
  add 1 to LevelStarts
when level completes
  add 1 to LevelCompletes
when message ""go"" arrives
  next level
when message ""jump"" arrives
  set level to 3"));
levelEngine.SessionStarted();
levelEngine.SceneStarted("Level1");
levelEngine.Tick(0);
Check(levelEngine.Variables.GetGlobal("LevelName").AsText() == "Level1" &&
      levelEngine.Variables.GetGlobal("LevelNumber").AsNumber() == 1,
    "a level start records Level1 as level 1");
Check(levelEngine.Variables.GetGlobal("LevelStarts").AsNumber() == 1,
    "`when level starts` fires on the scene load");
levelEngine.RaisePush(levelGame, new PushSignal { Kind = TriggerKind.Message, Name = "go" });
levelEngine.Tick(1);
Check(levelEngine.Variables.GetGlobal("LevelCompletes").AsNumber() == 1,
    "`next level` raises `when level completes` before the scene changes");
Check(advanced.Count == 1 && advanced[0] == "Level2",
    "`next level` loads the next scene in the level order");
levelEngine.RaisePush(levelGame, new PushSignal { Kind = TriggerKind.Message, Name = "jump" });
levelEngine.Tick(2);
Check(levelEngine.Variables.GetGlobal("LevelNumber").AsNumber() == 3 &&
      levelEngine.Variables.GetGlobal("LevelName").AsText() == "Level3",
    "`set level to 3` records the third level (and its name)");

// Statistics persist across a checkpoint, and `wipe game variables` starts a new run.
var statsWorld = new FakeWorld();
var statsGame = new FakeActor("Game");
statsWorld.AddActor(statsGame);
var statsEngine = FreshEngine(statsWorld);
statsEngine.Levels.SetOrder(new[] { "Level1", "Level2" });
statsEngine.AddSheet(statsGame, parser.Parse(@"when message ""kill"" arrives
  add 1 to kills Zombie
when message ""save"" arrives
  save stats
when message ""load"" arrives
  load stats
when message ""wipe"" arrives
  wipe game variables"));
statsEngine.SceneStarted("Level2");
void StatsMessage(string message, double time)
{
    statsEngine.RaisePush(statsGame, new PushSignal { Kind = TriggerKind.Message, Name = message });
    statsEngine.Tick(time);
}
foreach (int i in new[] { 0, 1, 2 })
    StatsMessage("kill", i);
StatsMessage("save", 3);
StatsMessage("kill", 4);
StatsMessage("kill", 5);
Check(statsEngine.Variables.GetGlobal("Kills Zombie").AsNumber() == 5,
    "two more kills after `save stats` read back as 5");
StatsMessage("load", 6);
Check(statsEngine.Variables.GetGlobal("Kills Zombie").AsNumber() == 3 &&
      statsEngine.Variables.GetGlobal("Kills Total").AsNumber() == 3,
    "`load stats` restores the saved kill counters");
StatsMessage("wipe", 7);
Check(statsEngine.Variables.GetGlobal("Kills Zombie").AsNumber() == 0 &&
      statsEngine.Variables.GetGlobal("LevelNumber").AsNumber() == 2,
    "`wipe game variables` clears the run's counters but keeps the current level");

// A player's shot and a landed hit drive `accuracy`; `time elapsed` counts the level.
var aimWorld = new FakeWorld();
var aimPlayer = new FakeActor("Player");
var aimEnemy = new FakeActor("Zombie");
aimWorld.AddActor(aimPlayer);
aimWorld.AddActor(aimEnemy);
var aimEngine = FreshEngine(aimWorld);
aimEngine.AddSheet(aimPlayer, parser.Parse("when Space is pressed\n  spawn Bullet at me offset 30"));
aimEngine.AddSheet(aimEnemy, parser.Parse("when message \"damage\" arrives\n  set my LastDmg to other's Damage"));
aimEngine.Variables.SetLocal(aimEnemy, "Type", ValueBox.OfText("Zombie"));
aimEngine.Variables.SetLocal(aimEnemy, "health", ValueBox.OfNumber(10));
aimEngine.SceneStarted("Level1");
aimEngine.Tick(0);
aimEngine.RaisePushAll(new PushSignal
{
    Kind = TriggerKind.KeyPressed,
    Name = "Space",
    PlayerA = aimEngine.PlayerForKey("Space"),
});
aimEngine.Tick(1);
Check(aimEngine.Variables.GetGlobal("Shots").AsNumber() == 1,
    "a player's `spawn Bullet` counts as a shot");
aimEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "damage", Other = aimPlayer }, "Zombie");
aimEngine.Tick(2);
Check(aimEngine.Variables.GetGlobal("Hits").AsNumber() == 1 &&
      Math.Abs(aimEngine.Variables.GetGlobal("Accuracy").AsNumber() - 100) < 1e-6,
    "a landed `\"damage\"` on an enemy counts as a hit (one shot, one hit → 100%)");
Check(Math.Abs(aimEngine.Variables.GetGlobal("Time Elapsed").AsNumber() - 2) < 1e-6,
    "`time elapsed` counts the seconds since this level started");

// `when all enemies dead` fires once, and only for a level that had enemies.
var clearWorld = new FakeWorld();
var clearGame = new FakeActor("Game");
var clearZombieA = new FakeActor("ZombieA");
var clearZombieB = new FakeActor("ZombieB");
clearWorld.AddActor(clearGame);
clearWorld.AddActor(clearZombieA);
clearWorld.AddActor(clearZombieB);
var clearEngine = FreshEngine(clearWorld);
clearEngine.AddSheet(clearGame, parser.Parse("when all enemies dead\n  add 1 to Clears"));
clearEngine.AddSheet(clearZombieA, parser.Parse("when me appears\n  set my mode to \"patrol\""));
clearEngine.AddSheet(clearZombieB, parser.Parse("when me appears\n  set my mode to \"patrol\""));
clearEngine.SceneStarted("Level1");
clearEngine.Tick(0);
Check(clearEngine.Variables.GetGlobal("Clears").AsNumber() == 0,
    "a level with no enemies is not a cleared level");
clearEngine.Variables.SetLocal(clearZombieA, "Type", ValueBox.OfText("Zombie"));
clearEngine.Variables.SetLocal(clearZombieA, "health", ValueBox.OfNumber(50));
clearEngine.Variables.SetLocal(clearZombieB, "Type", ValueBox.OfText("Zombie"));
clearEngine.Variables.SetLocal(clearZombieB, "health", ValueBox.OfNumber(50));
clearEngine.Tick(1);
Check(clearEngine.Variables.GetGlobal("Clears").AsNumber() == 0,
    "a living enemy keeps the level open");
clearEngine.Variables.SetLocal(clearZombieA, "health", ValueBox.OfNumber(0));
clearEngine.Tick(2);
Check(clearEngine.Variables.GetGlobal("Clears").AsNumber() == 0,
    "the level stays open while one enemy lives");
clearEngine.Variables.SetLocal(clearZombieB, "health", ValueBox.OfNumber(0));
clearEngine.Tick(3);
clearEngine.Tick(4);
Check(clearEngine.Variables.GetGlobal("Clears").AsNumber() == 1,
    "`when all enemies dead` fires once when the last enemy dies");

// The shipped sheets carry the wiring.
var shippedGameLevels = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Game.events"));
bool gameClearsLevel = false, gameAdvancesLevel = false;
foreach (var shippedEvent in shippedGameLevels.Events)
{
    if (TriggerClassifier.Classify(shippedEvent).Kind == TriggerKind.AllEnemiesDead)
        gameClearsLevel = true;
    foreach (var shippedLine in shippedEvent.Body)
        if (shippedLine.Statement is RawActionNode shippedRaw && shippedRaw.Words.Count >= 1 &&
            shippedRaw.Words[0].Text == "next")
            gameAdvancesLevel = true;
}
Check(gameClearsLevel && gameAdvancesLevel,
    "Game.events clears a level: `when all enemies dead` -> \"Level complete!\" -> `next level`");
var shippedZombieKills = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Zombie.events"));
bool zombieCountsKill = false;
foreach (var shippedEvent in shippedZombieKills.Events)
    foreach (var shippedLine in shippedEvent.Body)
        if (shippedLine.Statement is AddKillNode shippedKill && shippedKill.EnemyType == "Zombie")
            zombieCountsKill = true;
Check(zombieCountsKill, "Zombie.events counts its kills with `add 1 to kills Zombie`");

// ------------------------------------------------------------------
// Task 18 Part F: fog of war & visibility
// ------------------------------------------------------------------

// Trigger classification.
var areaRevealedDesc = TriggerClassifier.Classify(parser.Parse(
    "when area becomes revealed\n  set Seen to 1\n").Events[0]);
Check(areaRevealedDesc.Kind == TriggerKind.AreaRevealed,
      "`when area becomes revealed` classifies to AreaRevealed");

var areaHiddenDesc = TriggerClassifier.Classify(parser.Parse(
    "when area becomes hidden\n  set Lost to 1\n").Events[0]);
Check(areaHiddenDesc.Kind == TriggerKind.AreaHidden,
      "`when area becomes hidden` classifies to AreaHidden");

var entersFogDesc = TriggerClassifier.Classify(parser.Parse(
    "when Marine enters fog\n  set Fogged to 1\n").Events[0]);
Check(entersFogDesc.Kind == TriggerKind.EntersFog && entersFogDesc.RequireName == "Marine",
      "`when Marine enters fog` classifies to EntersFog and keeps the unit name");

var meEntersFogDesc = TriggerClassifier.Classify(parser.Parse(
    "when me enters fog\n  set Fogged to 1\n").Events[0]);
Check(meEntersFogDesc.Kind == TriggerKind.EntersFog && meEntersFogDesc.RequireName == "me",
      "`when me enters fog` listens on the acting unit");

// Action parsing.
var fogProg = parser.Parse(@"when game starts
  reveal area around me by 200
  hide area around me by 50
  set visibility to 300");
var revealSpec = ActionWords.Parse(((RawActionNode)fogProg.Events[0].Body[0].Statement).Words, null);
Check(revealSpec.Verb == ActionVerb.RevealArea && revealSpec.Number == 200,
      "`reveal area around me by 200` parses to RevealArea");
var hideAreaSpec = ActionWords.Parse(((RawActionNode)fogProg.Events[0].Body[1].Statement).Words, null);
Check(hideAreaSpec.Verb == ActionVerb.HideArea && hideAreaSpec.Number == 50,
      "`hide area around me by 50` parses to HideArea");
var visibilitySpec = ActionWords.Parse(((RawActionNode)fogProg.Events[0].Body[2].Statement).Words, null);
Check(visibilitySpec.Verb == ActionVerb.SetVisibility && visibilitySpec.Number == 300,
      "`set visibility to 300` parses to SetVisibility");

// Condition parsing.
var visibleToEnemyCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me is visible to enemy").Events[0].Body[0].Words);
Check(visibleToEnemyCondition.Kind == ConditionKind.IsVisibleToEnemy,
      "`me is visible to enemy` parses to IsVisibleToEnemy");
var hiddenCondition = Conditions.Parse(
    parser.Parse("when every 1 seconds\n  me is hidden").Events[0].Body[0].Words);
Check(hiddenCondition.Kind == ConditionKind.IsHidden,
      "`me is hidden` parses to IsHidden");

// Fog grid: vision, explored memory, and explicit reveal/hide.
var fog = new FogOfWarManager { CellSize = 10 };
var fogScout = new FakeActor("Scout");
var fogRaider = new FakeActor("Raider");
fog.SetActor(fogScout, 0, 0, 0, 25);
fog.SetActor(fogRaider, 1, 15, 0, 0);
fog.Tick(null);
Check(fog.IsVisibleTo(0, fogRaider),
      "an enemy inside a scout's vision is visible");
Check(fog.IsVisibleToEnemy(fogRaider) && !fog.IsHidden(fogRaider),
      "`me is visible to enemy` is true while the enemy sits in vision");
fog.SetActor(fogScout, 0, 500, 500, 25);
fog.Tick(null);
Check(!fog.IsVisibleTo(0, fogRaider),
      "moving vision away hides the enemy");
Check(fog.IsHidden(fogRaider),
      "`me is hidden` is true outside enemy vision");
Check(fog.IsExplored(0, 15, 0) && fog.StateAt(0, 15, 0) == FogState.Explored,
      "cells seen once stay explored after vision leaves");
fog.HideArea(0, 15, 0, 5);
Check(fog.StateAt(0, 15, 0) == FogState.Unexplored,
      "`hide area` reverts explored cells to unexplored");
fog.RevealArea(0, 15, 0, 5);
Check(fog.StateAt(0, 15, 0) == FogState.Visible,
      "`reveal area` makes cells visible");
fog.HideArea(0, 15, 0, 5);
fog.RevealAround(fogScout, 15);
Check(fog.StateAt(0, 500, 500) == FogState.Visible,
      "`reveal area around me` reveals the acting unit's cells");
fog.HideAround(fogScout, 15);
Check(fog.StateAt(0, 500, 500) == FogState.Unexplored,
      "`hide area around me` hides the acting unit's cells");
fog.SetVisionRadius(fogScout, 100);
Check(fog.VisionRadiusOf(fogScout) == 100,
      "`set visibility to` updates the actor's vision radius");

// Runtime integration: area edges and the enters-fog edge reach event sheets.
var fogEngine = FreshEngine(new FakeWorld());
var fogWatcher = new FakeActor("FogWatcher");
var fogEventProgram = parser.Parse(@"when area becomes revealed
  set Revealed to Revealed + 1
when Marine enters fog
  set Fogged to Fogged + 1");
fogEngine.Variables.SetGlobal("Revealed", ValueBox.OfNumber(0));
fogEngine.Variables.SetGlobal("Fogged", ValueBox.OfNumber(0));
fogEngine.AddSheet(fogWatcher, fogEventProgram);
fogEngine.Fog.CellSize = 10;
var fogLocal = new FakeActor("Scout");
var fogEnemy = new FakeActor("Marine");
fogEngine.Fog.SetActor(fogLocal, 0, 0, 0, 30);
fogEngine.Fog.SetActor(fogEnemy, 1, 10, 0, 0);
fogEngine.Tick(1);
Check(fogEngine.Variables.GetGlobal("Revealed").AsNumber() == 1,
      "`when area becomes revealed` fires when vision first covers cells");
fogEngine.Fog.SetActor(fogLocal, 0, 500, 500, 30);
fogEngine.Tick(2);
Check(fogEngine.Variables.GetGlobal("Fogged").AsNumber() == 1,
      "`when Marine enters fog` fires when local vision loses the unit");

var fogConditionContext = new EventContext { Runtime = fogEngine, World = fogEngine.World, Me = fogEnemy };
Check(Conditions.Evaluate(hiddenCondition, fogConditionContext),
      "`me is hidden` reads the live fog grid");
Check(!Conditions.Evaluate(visibleToEnemyCondition, fogConditionContext),
      "`me is visible to enemy` is false outside enemy vision");

// ------------------------------------------------------------------
// Task 18 Part G: damage & per-unit health
// ------------------------------------------------------------------

// Trigger classification: the `damage <amount>` message is a template, and the health
// threshold is its own polled trigger.
var damageMessageDesc = TriggerClassifier.Classify(parser.Parse(
    "when message \"damage <amount>\" arrives\n  set Hit to 1\n").Events[0]);
Check(damageMessageDesc.Kind == TriggerKind.Message && damageMessageDesc.MessagePattern &&
      damageMessageDesc.RequireName == "damage <amount>",
      "`when message \"damage <amount>\" arrives` classifies to a Message pattern");

var plainDamageDesc = TriggerClassifier.Classify(parser.Parse(
    "when message \"damage\" arrives\n  set Hit to 1\n").Events[0]);
Check(plainDamageDesc.Kind == TriggerKind.Message && !plainDamageDesc.MessagePattern,
      "a slot-less message name stays an exact match");

var healthLowDesc = TriggerClassifier.Classify(parser.Parse(
    "when my Health is lower than 30\n  set Wounded to 1\n").Events[0]);
Check(healthLowDesc.Kind == TriggerKind.HealthLow && healthLowDesc.WatchName == "Health" &&
      healthLowDesc.Threshold == 30,
      "`when my Health is lower than 30` classifies to HealthLow with its threshold");

var shieldLowDesc = TriggerClassifier.Classify(parser.Parse(
    "when Shield is lower than 5\n  set Broken to 1\n").Events[0]);
Check(shieldLowDesc.Kind == TriggerKind.HealthLow && shieldLowDesc.WatchName == "Shield",
      "the bare `Shield is lower than 5` form listens to the acting unit's shield");

Check(MessagePattern.IsPattern("damage <amount>") && !MessagePattern.IsPattern("damage"),
      "only quoted text with a <slot> is a message pattern");
Check(MessagePattern.TryMatch("damage <amount>", "damage 25", out var damageCaptures) &&
      damageCaptures.Count == 1 && damageCaptures[0].Key == "amount" && damageCaptures[0].Value == 25,
      "`damage 25` matches `damage <amount>` and captures 25");
Check(!MessagePattern.TryMatch("damage <amount>", "damage big", out _) &&
      !MessagePattern.TryMatch("damage <amount>", "hurt 25", out _) &&
      !MessagePattern.TryMatch("damage <amount>", "damage 25 now", out _),
      "non-numeric, wrongly named and longer messages do not match a pattern");

// Per-unit health: assignment clamps, damage spends armor then shields then health.
var healthState = new EventVariables();
var marineUnit = new FakeActor("Marine");
HealthManager.Set(healthState, marineUnit, "MaxHealth", 100);
HealthManager.Set(healthState, marineUnit, "Health", 60);
Check(HealthManager.Read(healthState, marineUnit, HealthManager.Health) == 60,
      "`set Health to 60` writes the unit's own health");
HealthManager.Set(healthState, marineUnit, "Health", 500);
Check(HealthManager.Read(healthState, marineUnit, HealthManager.Health) == 100,
      "`set Health to` clamps at MaxHealth");
HealthManager.Set(healthState, marineUnit, "Health", -5);
Check(HealthManager.Read(healthState, marineUnit, HealthManager.Health) == 0,
      "`set Health to` clamps at 0");
HealthManager.Set(healthState, marineUnit, "Health", 50);
HealthManager.Set(healthState, marineUnit, "MaxShield", 20);
HealthManager.Set(healthState, marineUnit, "Shield", 20);
HealthManager.Set(healthState, marineUnit, "Armor", 5);
var armorHit = HealthManager.Damage(healthState, marineUnit, 30);
Check(armorHit.Mitigated == 25 && armorHit.AbsorbedByShield == 20 &&
      HealthManager.Read(healthState, marineUnit, HealthManager.Shield) == 0 &&
      HealthManager.Read(healthState, marineUnit, HealthManager.Health) == 45,
      "damage spends armor, then the shield, then health (30 - 5 armor, 20 shield, 5 health)");
HealthManager.Add(healthState, marineUnit, "Health", -10);
Check(HealthManager.Read(healthState, marineUnit, HealthManager.Health) == 40,
      "`add -10 to Health` goes through armor (5) and takes 5 health");
HealthManager.Add(healthState, marineUnit, "Health", 1000);
Check(HealthManager.Read(healthState, marineUnit, HealthManager.Health) == 100,
      "healing clamps at MaxHealth");
HealthManager.Set(healthState, marineUnit, "MaxHealth", 30);
Check(HealthManager.Read(healthState, marineUnit, HealthManager.Health) == 30,
      "lowering MaxHealth pulls the current health down with it");

// An actor that never declared health keeps the old plain arithmetic (and no clamping).
var legacyState = new EventVariables();
var legacyUnit = new FakeActor("OldTimer");
HealthManager.Add(legacyState, legacyUnit, "health", -1);
Check(HealthManager.Read(legacyState, legacyUnit, "health") == -1,
      "an actor with no health variables keeps the plain arithmetic");

// Runtime: a `damage 25` message hands its number to the sheet and damage clamps at 0.
var healthEngine = FreshEngine(new FakeWorld());
var soldier = new FakeActor("Marine");
var marineHealthProgram = parser.Parse(@"when me appears
  set my MaxHealth to 60
  set my Health to 60
  set my Armor to 0
  set my Shield to 10
  set my MaxShield to 10

when message ""damage <amount>"" arrives
  add -10 to my Health

when my Health is lower than 30
  set Wounded to 1

when my Health becomes 0
  set Died to Died + 1");
healthEngine.AddSheet(soldier, marineHealthProgram);
healthEngine.Tick(0);
Check(healthEngine.Variables.GetLocal(soldier, "Health").AsNumber() == 60,
      "the Marine sheet seeds its own health from data");
healthEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "damage 25" }, "Marine");
healthEngine.Tick(1);
Check(healthEngine.Variables.GetLocal(soldier, "DamageTaken").AsNumber() == 25 &&
      healthEngine.Variables.GetLocal(soldier, "amount").AsNumber() == 25,
      "a `damage 25` message binds `my DamageTaken` and the slot name on the receiver");
Check(healthEngine.Variables.GetLocal(soldier, "Health").AsNumber() == 60,
      "the damage event ran (`add -10 to my Health`: the 10 shield absorbed all of it)");
for (int hit = 2; hit <= 9; hit++)
{
    healthEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "damage 25" }, "Marine");
    healthEngine.Tick(hit);
}
Check(healthEngine.Variables.GetLocal(soldier, "Health").AsNumber() == 0,
      "`add -<n> to Health` never takes health below 0");
Check(healthEngine.Variables.GetGlobal("Wounded").AsNumber() == 1,
      "`when my Health is lower than 30` fires once on the way down");
Check(healthEngine.Variables.GetGlobal("Died").AsNumber() == 1,
      "`when my Health becomes 0` fires exactly once");
healthEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "damage 25" }, "Marine");
healthEngine.Tick(20);
Check(healthEngine.Variables.GetGlobal("Died").AsNumber() == 1,
      "hits on a dead unit do not refire `when my Health becomes 0`");

// Part G follow-up: a negated variable as a value, so one action can spend a captured amount.
var negatedAdd = parser.Parse("when me appears\n  add -my DamageTaken to Health\n")
    .Events[0].Body[0].Statement as AddNode;
Check(negatedAdd?.Amount is VariableValue negatedAmount && negatedAmount.Owner == "my" &&
      negatedAmount.Name == "DamageTaken" && negatedAmount.Negated,
      "`add -my DamageTaken to Health` parses the negated amount");

var negatedSet = parser.Parse("when me appears\n  set Score to -my DamageTaken\n")
    .Events[0].Body[0].Statement as SetNode;
Check(negatedSet?.Value is VariableValue negatedValue && negatedValue.Negated &&
      negatedValue.Name == "DamageTaken",
      "`set Score to -my DamageTaken` keeps the negation on the value");

var negatedGlobalSet = parser.Parse("when me appears\n  set Debt to -Score\n")
    .Events[0].Body[0].Statement as SetNode;
Check(negatedGlobalSet?.Value is VariableValue negatedGlobal && negatedGlobal.Negated &&
      negatedGlobal.Name == "Score",
      "`set Debt to -Score` negates a plain global");

var negatedEngine = FreshEngine(new FakeWorld());
var gladiator = new FakeActor("Gladiator");
negatedEngine.AddSheet(gladiator, parser.Parse(@"when me appears
  set my MaxHealth to 40
  set my Health to 40
  set my MaxShield to 10
  set my Shield to 10

when message ""damage <amount>"" arrives
  add -my DamageTaken to Health"));
negatedEngine.Tick(0);
negatedEngine.SendRawMessage(new PushSignal { Kind = TriggerKind.Message, Name = "damage 25" }, "Gladiator");
negatedEngine.Tick(1);
Check(negatedEngine.Variables.GetLocal(gladiator, "Health").AsNumber() == 25 &&
      negatedEngine.Variables.GetLocal(gladiator, "Shield").AsNumber() == 0,
      "`add -my DamageTaken to Health` spends 25 through the shield (10) and then health");

// Part G follow-up: multiword health stat names keep their spelling and still clamp.
var multiEngine = FreshEngine(new FakeWorld());
var ogre = new FakeActor("Ogre");
multiEngine.AddSheet(ogre, parser.Parse(@"when me appears
  set my Max Health to 80
  set my Health to 100
  set my Max Shield to 10
  set my Shield to 40
  add 5 to Max Health"));
multiEngine.Tick(0);
Check(multiEngine.Variables.GetLocal(ogre, "Max Health").AsNumber() == 85 &&
      HealthManager.Read(multiEngine.Variables, ogre, HealthManager.MaxHealth) == 85,
      "`set my Max Health to 80` / `add 5 to Max Health` keep the written spelling");
Check(multiEngine.Variables.GetLocal(ogre, "Health").AsNumber() == 80,
      "`set my Health to 100` clamps at the multiword `my Max Health`");
Check(multiEngine.Variables.GetLocal(ogre, "Shield").AsNumber() == 10 &&
      HealthManager.Read(multiEngine.Variables, ogre, HealthManager.MaxShield) == 10,
      "`set my Shield to 40` clamps at the multiword `my Max Shield`");

// ------------------------------------------------------------------
// Task 18 Part H: extended IEventWorld (RTS state queries)
// ------------------------------------------------------------------

// A bare world has no economy, selection or vision: every query answers empty.
var arcDebugWorld = new DefaultEventWorld();
Check(arcDebugWorld.GetSelectedCount() == 0 && arcDebugWorld.GetSelection().Count == 0 &&
      arcDebugWorld.GetResource("Minerals") == 0 && !arcDebugWorld.IsRevealed(null, 1) &&
      arcDebugWorld.GetBuildProgress(null) == 0 && arcDebugWorld.GetOrderQueue(null).Count == 0,
      "the default world answers the RTS queries with empty/no-op values");

// The runtime owns the state the world reports.
var apiEngine = FreshEngine(new FakeWorld());
var apiMarine = new FakeActor("Marine");
apiEngine.AddSheet(apiMarine, parser.Parse("when me appears\n  set my Health to 10"));
apiEngine.Tick(0);
Check(apiEngine.SelectedCount == 0 && apiEngine.SelectedActors.Count == 0,
      "`GetSelection` is empty before anything is selected");
apiEngine.SelectActor(apiMarine);
Check(apiEngine.SelectedCount == 1 && ReferenceEquals(apiEngine.SelectedActors[0], apiMarine),
      "`GetSelection` reports the selected actor");

apiEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(325));
Check(apiEngine.ResourceValue("minerals") == 325 && apiEngine.ResourceValue("Supply Used") == 0,
      "`GetResource` reads a resource by any spelling and defaults to 0");

apiEngine.Fog.CellSize = 10;
var apiRaider = new FakeActor("Raider");
apiEngine.Fog.SetActor(apiRaider, 1, 15, 0, 0);
apiEngine.RevealArea(0, 15, 0, 5);
Check(apiEngine.IsRevealedFor(apiRaider, 0),
      "`RevealArea` makes an enemy visible to that player");
apiEngine.HideArea(0, 15, 0, 5);
Check(!apiEngine.IsRevealedFor(apiRaider, 0),
      "`HideArea` takes the enemy back out of that player's vision");

var apiBarracks = new FakeActor("Barracks");
apiEngine.Construction.RegisterPlaced(apiBarracks, "Barracks", apiEngine.Variables);
apiEngine.Construction.SetProgress(apiEngine.Variables, apiBarracks, 40);
Check(apiEngine.BuildProgressOf(apiBarracks) == 40 && apiEngine.BuildProgressOf(apiMarine) == 0,
      "`GetBuildProgress` reads the placed building's progress (0 for a non-building)");

var apiScout = new FakeActor("Scout");
apiEngine.Orders.StartOrder(apiEngine.Variables, apiEngine, apiScout,
    new UnitOrder { Kind = UnitOrderKind.Move, Name = "move", TargetName = "there" });
apiEngine.Orders.AddOrder(apiEngine.Variables, apiEngine, apiScout,
    new UnitOrder { Kind = UnitOrderKind.Attack, Name = "attack", TargetName = "Raider" });
var apiQueue = apiEngine.OrderQueueOf(apiScout);
Check(apiQueue.Count == 2 && apiQueue[0] == "move" && apiQueue[1] == "attack" &&
      apiEngine.OrderQueueOf(apiMarine).Count == 0,
      "`GetOrderQueue` lists the unit's orders, active first");

// ---- Task 16 Part I: the rest of TK4's enemy cast (data-driven) ----

// `wander speed my Speed` takes the pace from the actor's own stat, so a sheet can follow a
// type's data instead of repeating a number.
var perActorWander = ActionWords.Parse(RawWords(parser, "when me appears\n  wander speed my Speed\n"), message => { });
Check(perActorWander.Verb == ActionVerb.Wander && perActorWander.AmountNode is VariableValue wanderAmount &&
      wanderAmount.Name == "Speed",
    "`wander speed my Speed` takes the pace from the actor's own stat");

// The shipped data file is the whole cast: ten Part I types on top of Zombie and Turret.
var enemyTypeFile = File.ReadAllText("Source/Game/GameFiles/EnemyTypes.events");
var shippedTypeProgram = parser.Parse(enemyTypeFile);
EnemyTypes.LoadData(shippedTypeProgram);
WeaponData.LoadData(parser.Parse(File.ReadAllText("Source/Game/GameFiles/WeaponData.events")));
var tk4TypeNames = new[]
{
    "Zombie", "Turret", "Pistolboy", "ShotgunManiac", "UziRebel", "Commando",
    "BazookaBoy", "Civilian", "Punisher", "Flamer", "Punk", "GrenadeGay",
};

var typeSnapshot = new List<(string Name, double Speed, double Sight, double Health, string Weapon, bool Hostile, bool Explodes)>();
bool everyTypeApplies = true;
foreach (string typeName in tk4TypeNames)
{
    var applyRuntime = new EventRuntime();
    var applyActor = new object();
    applyRuntime.AddSheet(applyActor, parser.Parse("when me appears\n  enemy type \"" + typeName + "\"\n"));
    applyRuntime.Tick(0);
    if (!applyRuntime.Variables.HasLocal(applyActor, "Type") ||
        applyRuntime.Variables.GetLocal(applyActor, "Type").AsText() != typeName)
        everyTypeApplies = false;
    typeSnapshot.Add((
        typeName,
        applyRuntime.Variables.GetLocal(applyActor, "Speed").AsNumber(),
        applyRuntime.Variables.GetLocal(applyActor, "SightDistance").AsNumber(),
        applyRuntime.Variables.GetLocal(applyActor, "Health").AsNumber(),
        applyRuntime.Variables.GetLocal(applyActor, "Weapon").AsText(),
        applyRuntime.Variables.GetLocal(applyActor, "Hostile").AsBoolean(),
        applyRuntime.Variables.GetLocal(applyActor, "ExplosionDeath").AsBoolean()));
}
Check(everyTypeApplies, $"all {tk4TypeNames.Length} TK4 types apply via `enemy type` (data file only)");
Check(typeSnapshot.Select(t => t.Speed + "/" + t.Sight + "/" + t.Health).Distinct().Count() == tk4TypeNames.Length,
    "every type has its own speed/sight/health triple");

var typeBy = typeSnapshot.ToDictionary(t => t.Name);
Check(!typeBy["Civilian"].Hostile && typeBy["Civilian"].Weapon == "" && typeBy["Civilian"].Health == 30,
    "Civilian is non-hostile, unarmed, and as weak as its data says");
Check(typeBy["Turret"].Speed == 0, "Turret is still the one type that cannot walk");
Check(typeBy["Pistolboy"].Weapon == "Pistol" && typeBy["ShotgunManiac"].Weapon == "Shotgun" &&
      typeBy["UziRebel"].Weapon == "Uzi" && typeBy["BazookaBoy"].Weapon == "Rocket" &&
      typeBy["GrenadeGay"].Weapon == "Grenade" && typeBy["Flamer"].Weapon == "Flamer",
    "each gunner type carries its own weapon from the data table");
Check(typeBy["BazookaBoy"].Explodes && typeBy["Punisher"].Explodes && typeBy["Flamer"].Explodes &&
      typeBy["GrenadeGay"].Explodes && !typeBy["Punk"].Explodes,
    "only the volatile types die in an explosion");
Check(EnemyTypes.KnownTypeNames().Contains("Pistolboy") &&
      EnemyTypes.KnownTypeNames().Contains("Civilian") &&
      EnemyTypes.KnownTypeNames().Contains("GrenadeGay"),
    "the unknown-type help names every data-defined type");
Check(WeaponData.CanonicalFor(new EventVariables(), "Flamer") == "Flamer",
    "the Flamer weapon exists as data (WeaponData.events)");

// Turret vs Zombie: the same gates the shipped sheets use — a Turret never patrols.
string PatrolSheet(string typeName) =>
    "when me appears\n  enemy type \"" + typeName + "\"\nwhen every 1 seconds\n  my Speed is higher than 0\n  add 1 to Moves";

var turretWorld = new FakeWorld();
var turretBody = new FakeActor("Turret");
turretWorld.AddActor(turretBody);
var turretEngine = FreshEngine(turretWorld);
turretEngine.AddSheet(turretBody, parser.Parse(PatrolSheet("Turret")));
turretEngine.Tick(0);
turretEngine.Tick(1);
turretEngine.Tick(2);
Check(!turretEngine.Variables.HasGlobal("Moves") || turretEngine.Variables.GetGlobal("Moves").AsNumber() == 0,
    "a spawned Turret never patrols (Speed 0 closes the `my Speed is higher than 0` gate)");

var zombiePatrolWorld = new FakeWorld();
var zombieBody = new FakeActor("Zombie");
zombiePatrolWorld.AddActor(zombieBody);
var zombiePatrolEngine = FreshEngine(zombiePatrolWorld);
zombiePatrolEngine.AddSheet(zombieBody, parser.Parse(PatrolSheet("Zombie")));
zombiePatrolEngine.Tick(0);
zombiePatrolEngine.Tick(1);
zombiePatrolEngine.Tick(2);
Check(zombiePatrolEngine.Variables.GetGlobal("Moves").AsNumber() > 0,
    "a spawned Zombie patrols on the same sheet");

// The Civilian sheet: calm at the type's pace, then shot -> flee faster.
var civilianProgram = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Civilian.events"));
var civilianWorld = new FakeWorld();
var civilian = new FakeActor("Civilian");
var civilianBullet = new FakeActor("Bullet");
civilianWorld.AddActor(civilian);
var civilianEngine = FreshEngine(civilianWorld);
civilianEngine.AddSheet(civilian, civilianProgram);
civilianEngine.Tick(0);
Check(!civilianEngine.Variables.GetLocal(civilian, "Hostile").AsBoolean(),
    "Civilian.events puts a civilian on the non-hostile type");
Check(civilianEngine.Variables.GetLocal(civilian, "Health").AsNumber() == 30,
    "a civilian's health is the type's data, not a number in the sheet");
// The calm wander reads the type's pace (`wander speed my Speed`), and the flee leg its own
// faster one; the engine-free runtime does not run engine actions, so read the two lines.
var calmWanderNode = civilianProgram.Events[0].Body[3].Statement as RawActionNode;
Check(calmWanderNode != null && calmWanderNode.Words[1].Text == "speed" &&
      calmWanderNode.Words[2].Text == "my" && calmWanderNode.Words[3].Text == "Speed",
    "a calm civilian wanders at the type's own walking speed");
var fleeWanderNode = civilianProgram.Events[1].Body[5].Statement as RawActionNode;
Check(fleeWanderNode != null && fleeWanderNode.Words[1].Text == "speed" &&
      fleeWanderNode.Words[3].Text == "FleeSpeed",
    "the flee leg wanders at the civilian's own FleeSpeed");
Check(civilianEngine.Variables.GetLocal(civilian, "FleeSpeed").AsNumber() == 140,
    "FleeSpeed is derived from the type's Speed (70 + 70), not authored twice");
civilianEngine.Variables.SetLocal(civilianBullet, "damage", ValueBox.OfNumber(10));
civilianEngine.RaisePush(civilian, new PushSignal { Kind = TriggerKind.Message, Name = "damage", Other = civilianBullet });
civilianEngine.Tick(1);
Check(civilianEngine.Variables.GetLocal(civilian, "Health").AsNumber() == 20,
    "a shot takes the bullet's damage off the civilian");
Check(civilianEngine.Variables.GetLocal(civilian, "mode").AsText() == "flee",
    "a shot sends the civilian into flee mode");

// A civilian is not an enemy: a level with a civilian in it still clears (Part G trigger).
var civilianClearWorld = new FakeWorld();
var civilianClearGame = new FakeActor("Game");
var civilianClearZombie = new FakeActor("ZombieA");
var civilianBystander = new FakeActor("Bystander");
civilianClearWorld.AddActor(civilianClearGame);
civilianClearWorld.AddActor(civilianClearZombie);
civilianClearWorld.AddActor(civilianBystander);
var civilianClearEngine = FreshEngine(civilianClearWorld);
civilianClearEngine.AddSheet(civilianClearGame, parser.Parse("when all enemies dead\n  add 1 to Clears"));
civilianClearEngine.AddSheet(civilianClearZombie, parser.Parse("when me appears\n  set my mode to \"patrol\"\n"));
civilianClearEngine.AddSheet(civilianBystander, parser.Parse("when me appears\n  enemy type \"Civilian\"\n"));
civilianClearEngine.SceneStarted("Level1");
civilianClearEngine.Tick(0);
Check(!civilianClearEngine.Variables.HasGlobal("Clears") ||
      civilianClearEngine.Variables.GetGlobal("Clears").AsNumber() == 0,
    "a level populated only by civilians has no enemies to clear");
civilianClearEngine.Variables.SetLocal(civilianClearZombie, "Type", ValueBox.OfText("Zombie"));
civilianClearEngine.Variables.SetLocal(civilianClearZombie, "health", ValueBox.OfNumber(50));
civilianClearEngine.Tick(1);
civilianClearEngine.Variables.SetLocal(civilianClearZombie, "health", ValueBox.OfNumber(0));
civilianClearEngine.Tick(2);
civilianClearEngine.Tick(3);
Check(civilianClearEngine.Variables.GetGlobal("Clears").AsNumber() == 1,
    "a civilian still standing does not keep the level from completing");

// ---- Task 16 Part I follow-up: one shared gunner sheet, type taken from the actor name ----

// `my name` is the actor's own name, and `goto ... speed my Speed` reads the pace from a stat.
var gunnerWorld = new FakeWorld();
var gunnerPistolboy = new FakeActor("Pistolboy");
var gunnerCommando = new FakeActor("Commando");
var gunnerCivilian = new FakeActor("Civilian");
gunnerWorld.AddActor(gunnerPistolboy);
gunnerWorld.AddActor(gunnerCommando);
gunnerWorld.AddActor(gunnerCivilian);
var gunnerEngine = FreshEngine(gunnerWorld);
var gunnerProgram = parser.Parse(File.ReadAllText("Source/Game/GameFiles/TK_Gunner.events"));
Check(gunnerProgram.Events.Count == 10, "TK_Gunner.events parses into its ten events");
var nameReadEngine = FreshEngine(gunnerWorld);
nameReadEngine.AddSheet(gunnerPistolboy, parser.Parse("when me appears\n  set LastSeenName to my name\n"));
nameReadEngine.Tick(0);
Check(nameReadEngine.Variables.GetGlobal("LastSeenName").AsText() == "Pistolboy",
    "`my name` reads the actor's own name");
var perActorGoto = ActionWords.Parse(RawWords(parser, "when me appears\n  goto Waypoint speed my Speed\n"), message => { });
Check(perActorGoto.Verb == ActionVerb.Goto && perActorGoto.AmountNode is VariableValue gotoAmount &&
      gotoAmount.Name == "Speed",
    "`goto ... speed my Speed` walks at the actor's own pace");

gunnerEngine.AddSheet(gunnerPistolboy, gunnerProgram);
gunnerEngine.AddSheet(gunnerCommando, gunnerProgram);
gunnerEngine.AddSheet(gunnerCivilian, gunnerProgram);
gunnerEngine.Tick(0);
Check(gunnerPistolboy.Name == "Pistolboy" &&
      gunnerEngine.Variables.GetLocal(gunnerPistolboy, "Type").AsText() == "Pistolboy" &&
      gunnerEngine.Variables.GetLocal(gunnerPistolboy, "Health").AsNumber() == 45 &&
      gunnerEngine.Variables.GetLocal(gunnerPistolboy, "Weapon").AsText() == "Pistol",
    "a scene actor named Pistolboy gets the Pistolboy type from the shared sheet");
Check(gunnerEngine.Variables.GetLocal(gunnerCommando, "Type").AsText() == "Commando" &&
      gunnerEngine.Variables.GetLocal(gunnerCommando, "Speed").AsNumber() == 100 &&
      gunnerEngine.Variables.GetLocal(gunnerCommando, "SightDistance").AsNumber() == 300,
    "the same sheet gives a Commando its own speed and sight");
Check(gunnerEngine.Variables.GetLocal(gunnerCommando, "Armed").AsBoolean() &&
      !gunnerEngine.Variables.GetLocal(gunnerCivilian, "Armed").AsBoolean(),
    "an armed type shoots and an unarmed type bites (`my Armed` follows the type's weapon)");
Check(gunnerEngine.Variables.GetLocal(gunnerCommando, "mode").AsText() == "patrol",
    "a gunner starts patrolling on its own");
Check(!gunnerEngine.Variables.GetLocal(gunnerCivilian, "Hostile").AsBoolean(),
    "a non-hostile type wearing the gunner sheet stays passive");

// A name that is not a type warns once and leaves the actor on its defaults.
var strayWorld = new FakeWorld();
var strayActor = new FakeActor("Barrel");
strayWorld.AddActor(strayActor);
var strayEngine = FreshEngine(strayWorld);
string strayWarning = "";
strayEngine.OnRuntimeWarning = message =>
{
    if (message.Contains("enemy type")) strayWarning = message;
};
strayEngine.AddSheet(strayActor, parser.Parse("when me appears\n  enemy type my name\n"));
strayEngine.Tick(0);
Check(strayWarning.Contains("Barrel") && strayWarning.Contains("Pistolboy"),
    "an actor name that is not a type warns and lists the types it could be");

// A spawned copy keeps its type: "Pistolboy 2" is a Pistolboy, "Zombies" is not a Zombie.
var cloneWorld = new FakeWorld();
var cloneActor = new FakeActor("Pistolboy 2");
var pluralActor = new FakeActor("Zombies");
cloneWorld.AddActor(cloneActor);
cloneWorld.AddActor(pluralActor);
var cloneEngine = FreshEngine(cloneWorld);
int pluralWarnings = 0;
cloneEngine.OnRuntimeWarning = message =>
{
    if (message.Contains("enemy type")) pluralWarnings++;
};
cloneEngine.AddSheet(cloneActor, parser.Parse("when me appears\n  enemy type my name\n"));
cloneEngine.AddSheet(pluralActor, parser.Parse("when me appears\n  enemy type my name\n"));
cloneEngine.Tick(0);
Check(cloneEngine.Variables.GetLocal(cloneActor, "Type").AsText() == "Pistolboy",
    "a spawned copy named \"Pistolboy 2\" keeps the Pistolboy type");
Check(!cloneEngine.Variables.HasLocal(pluralActor, "Type") && pluralWarnings == 1,
    "a plural name is not a type match (\"Zombies\" warns instead of guessing)");

// The enemy sheets read the same data: no stat is repeated in code.
var shippedZombieSheet = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Zombie.events"));
bool zombieReadsStats = false;
foreach (var shippedEvent in shippedZombieSheet.Events)
    foreach (var shippedLine in shippedEvent.Body)
        if (shippedLine.Words.Count >= 3 && shippedLine.Words[0].Text == "enemy" &&
            shippedLine.Words[2].Text == "Zombie")
            zombieReadsStats = true;
Check(zombieReadsStats, "Zombie.events still gets its stats from `enemy type \"Zombie\"`");

// ---- Task 7 Part G2: wall negotiation for the no-NavMesh chase (C# steering) ----

// The word contract: an opt-in qualifier on the existing chase, so no sheet has to change.
var wallsSpec = ActionWords.Parse(
    RawWords(parser, "when me appears\n  move toward player speed 60 around walls\n"), _ => { });
Check(wallsSpec.Verb == ActionVerb.Move && wallsSpec.Thing == "player" && wallsSpec.AroundWalls &&
      wallsSpec.HasNumber && Math.Abs(wallsSpec.Number - 60) < 0.0001 && wallsSpec.Leftover.Count == 0,
    "`move toward player speed 60 around walls` parses with the opt-in flag and its speed");

var wallsDefaultSpeed = ActionWords.Parse(
    RawWords(parser, "when me appears\n  move toward player around walls\n"), _ => { });
Check(wallsDefaultSpeed.AroundWalls && wallsDefaultSpeed.HasNumber &&
      Math.Abs(wallsDefaultSpeed.Number - 5) < 0.0001 && wallsDefaultSpeed.Leftover.Count == 0,
    "`around walls` keeps the default speed 5 when no `speed` is written");

var wallsMySpeed = ActionWords.Parse(
    RawWords(parser, "when me appears\n  move toward player speed my Speed around walls\n"), _ => { });
Check(wallsMySpeed.AroundWalls && wallsMySpeed.AmountNode is VariableValue wallsSpeedValue &&
      wallsSpeedValue.Owner == "my" && wallsSpeedValue.Name == "Speed",
    "`move toward ... speed my Speed around walls` reads the pace from a variable");

var straightChase = ActionWords.Parse(
    RawWords(parser, "when me appears\n  move toward player speed 60\n"), _ => { });
Check(!straightChase.AroundWalls && straightChase.HasNumber &&
      Math.Abs(straightChase.Number - 60) < 0.0001,
    "the plain chase stays a straight line — wall negotiation is opt-in only");

var gotoWalls = ActionWords.Parse(
    RawWords(parser, "when me appears\n  goto player around walls\n"), _ => { });
Check(gotoWalls.Verb == ActionVerb.Goto && !gotoWalls.AroundWalls &&
      gotoWalls.Leftover.Any(token => token.Text == "walls"),
    "`around walls` is not a `goto` qualifier — the NavMesh already routes");

// The fan spends the whole per-tick budget on the innermost deviations, both sides.
var fanAngles = WallSteering.FanCandidates(SteerSide.Right, WallSteering.MaxRaysPerTick);
Check(fanAngles.Count == 6 &&
      fanAngles[0] == 15 && fanAngles[1] == -15 &&
      fanAngles[2] == 30 && fanAngles[3] == -30 &&
      fanAngles[4] == 45 && fanAngles[5] == -45,
    "the fan tries ±15/30/45 smallest-first within its 7-ray budget");

// A blocked direct probe samples the fan and steers at the smallest clear deviation.
static SteerQuery MakeSteerQuery(object actor, double x, double z, bool directClear, Func<double, bool> isClear)
{
    return new SteerQuery
    {
        Actor = actor,
        DeltaTime = 0.1,
        X = x,
        Z = z,
        ForwardX = 0,
        ForwardZ = 1,
        BearingX = 0,
        BearingZ = 1,
        DirectClear = directClear,
        IsClear = isClear,
        MinStep = 0.5,
    };
}

var steerActor = new FakeActor("Enemy_Walls");
var wallSteering = new WallSteering();
var steeredAngles = new List<double>();
var steerChoice = wallSteering.Step(MakeSteerQuery(steerActor, 0, 0, false, angle =>
{
    steeredAngles.Add(angle);
    return Math.Abs(angle - 15) < 0.001;
}));
Check(steerChoice.Steer && Math.Abs(steerChoice.DeviationDegrees - 15) < 0.001 &&
      steerChoice.Side == SteerSide.Right && steerChoice.RaysCast == 2 && steeredAngles.Count == 1 &&
      Math.Abs(steerChoice.HeadingX - Math.Sin(Math.PI / 12)) < 0.001 &&
      Math.Abs(steerChoice.HeadingZ - Math.Cos(Math.PI / 12)) < 0.001,
    "a blocked chase picks the smallest clear deviation (15° right) after two rays");

// Only the left side is clear: the same rule picks it.
var leftActor = new FakeActor("Enemy_Walls_Left");
var leftSteering = new WallSteering();
var leftChoice = leftSteering.Step(MakeSteerQuery(leftActor, 0, 0, false, angle => angle < 0));
Check(leftChoice.Steer && Math.Abs(leftChoice.DeviationDegrees + 15) < 0.001 &&
      leftChoice.Side == SteerSide.Left && leftChoice.RaysCast == 3,
    "a wall only on the right steers 15° left instead");

// Hysteresis: once a side is chosen, an equally-clear tick keeps it (no vibration).
var hysteresisTicks = new List<double>();
for (int i = 0; i < 3; i++)
    hysteresisTicks.Add(leftSteering.Step(MakeSteerQuery(leftActor, 0, 0, false, _ => true)).DeviationDegrees);
Check(hysteresisTicks.All(deviation => deviation < 0) &&
      Math.Abs(hysteresisTicks[0] + 15) < 0.001,
    "the chosen side sticks for ~0.3 s even when both sides become clear");

// ... and after the window the actor's own lean breaks the tie, so it may switch once.
var leanedQuery = MakeSteerQuery(leftActor, 0, 0, false, _ => true);
leanedQuery.ForwardX = -0.2588; // 15° left of the bearing
leanedQuery.ForwardZ = 0.9659;
var leanedChoice = leftSteering.Step(leanedQuery);
Check(leanedChoice.Side == SteerSide.Left && leanedChoice.DeviationDegrees < 0,
    "when both sides are equally clear the smaller turn (the actor's own lean) wins");

// A clear direct path spends exactly the one probe ray and no fan rays.
int fanRays = 0;
var clearChoice = new WallSteering().Step(MakeSteerQuery(
    new FakeActor("Enemy_Clear"), 0, 0, true, _ => { fanRays++; return true; }));
Check(!clearChoice.Steer && clearChoice.RaysCast == 1 && fanRays == 0,
    "a clear chase casts the probe only — zero fan rays");

// Stuck fallback: standing still for 0.4 s forces a sampling cycle even though the probe is clear.
var stuckActor = new FakeActor("Enemy_Stuck");
var stuckSteering = new WallSteering();
var stuckFirst = stuckSteering.Step(MakeSteerQuery(stuckActor, 0, 0, true, angle => Math.Abs(angle + 15) < 0.001));
Check(!stuckFirst.Steer && stuckFirst.RaysCast == 1,
    "a clear path does not sample before the stuck window elapses");
for (int i = 0; i < 3; i++)
    stuckSteering.Step(MakeSteerQuery(stuckActor, 0, 0, true, angle => Math.Abs(angle + 15) < 0.001));
var stuckChoice = stuckSteering.Step(MakeSteerQuery(stuckActor, 0, 0, true, angle => Math.Abs(angle + 15) < 0.001));
Check(stuckChoice.Stuck && stuckChoice.Steer &&
      Math.Abs(stuckChoice.DeviationDegrees + 15) < 0.001 && stuckChoice.RaysCast == 3,
    "a stuck chase (0.4 s without moving) samples the fan even when the probe reads clear");

// Moving again clears the stuck timer.
var unstuckActor = new FakeActor("Enemy_Unstuck");
var unstuckSteering = new WallSteering();
unstuckSteering.Step(MakeSteerQuery(unstuckActor, 0, 0, true, _ => true));
unstuckSteering.Step(MakeSteerQuery(unstuckActor, 5, 0, true, _ => true));
var unstuckChoice = unstuckSteering.Step(MakeSteerQuery(unstuckActor, 10, 0, true, _ => true));
Check(!unstuckChoice.Steer && unstuckChoice.RaysCast == 1,
    "walking again clears the stuck timer (no forced sampling)");

// No fan ray clear: the wall tangent of the hit that blocked the probe is followed instead.
var tangentQuery = MakeSteerQuery(new FakeActor("Enemy_Tangent"), 0, 0, false, _ => false);
tangentQuery.HasHitNormal = true;
tangentQuery.HitNormalX = 0;
tangentQuery.HitNormalZ = 1; // a wall facing back along the +Z approach
var tangentChoice = new WallSteering().Step(tangentQuery);
Check(tangentChoice.Steer && tangentChoice.Tangent &&
      Math.Abs(tangentChoice.DeviationDegrees - 90) < 0.001 &&
      Math.Abs(tangentChoice.HeadingX - 1) < 0.001 && Math.Abs(tangentChoice.HeadingZ) < 0.001 &&
      tangentChoice.RaysCast == WallSteering.MaxRaysPerTick,
    "a wall wider than the fan falls back to its tangent, still within 7 rays");

// The tangent flips to the side that makes progress toward a target behind the actor.
var tangentBackQuery = MakeSteerQuery(new FakeActor("Enemy_Tangent_Back"), 0, 0, false, _ => false);
tangentBackQuery.BearingX = -1;
tangentBackQuery.BearingZ = 0;
tangentBackQuery.ForwardX = -1;
tangentBackQuery.HitNormalX = 0;
tangentBackQuery.HitNormalZ = 1;
tangentBackQuery.HasHitNormal = true;
var tangentBackChoice = new WallSteering().Step(tangentBackQuery);
Check(tangentBackChoice.Steer && Math.Abs(tangentBackChoice.HeadingX + 1) < 0.001,
    "the wall tangent is flipped to the side that points at the target");

// Nothing clear and no normal to follow: keep the straight bearing, but never overspend rays.
var blindChoice = new WallSteering().Step(MakeSteerQuery(
    new FakeActor("Enemy_Blind"), 0, 0, false, _ => false));
Check(!blindChoice.Steer && blindChoice.RaysCast == WallSteering.MaxRaysPerTick,
    "a fully blocked fan with no hit normal keeps the bearing after exactly 7 rays");

// `stop moving` / a plain move forgets the committed side so the next chase starts fresh.
var forgetActor = new FakeActor("Enemy_Forget");
var forgetSteering = new WallSteering();
forgetSteering.Step(MakeSteerQuery(forgetActor, 0, 0, false, _ => true));
forgetSteering.Clear(forgetActor);
var freshQuery = MakeSteerQuery(forgetActor, 0, 0, false, _ => true);
freshQuery.ForwardX = -0.2588;
freshQuery.ForwardZ = 0.9659;
Check(forgetSteering.Step(freshQuery).DeviationDegrees < 0,
    "clearing the steering state lets the smaller-turn tie-break decide again");

// The G2 companion sheet: the chase carries the qualifier and the corner round-trip is events-only.
var wallsProgram = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Enemy_Walls.events"));
bool wallsChaseUsesQualifier = false;
int wallsChaseBlocks = 0;
foreach (var wallsEvent in wallsProgram.Events)
{
    foreach (var wallsLine in wallsEvent.Body)
    {
        if (wallsLine.Words.Count < 2 || wallsLine.Words[0].Text != "move" ||
            wallsLine.Words[1].Text != "toward")
            continue;
        wallsChaseBlocks++;
        if (wallsLine.Words.Any(word => word.Text == "walls"))
            wallsChaseUsesQualifier = true;
    }
}
Check(wallsProgram.Events.Count == 7 && wallsChaseBlocks == 1 && wallsChaseUsesQualifier,
    "Enemy_Walls.events parses and its chase is the wall-aware move");

// ---- Task 7 G2 follow-up: facing, character spacing and hitched-frame movement ----

// `facing forward` is the second opt-in qualifier on the same chase, after `around walls`.
var wallsFaceSpec = ActionWords.Parse(
    RawWords(parser, "when me appears\n  move toward player speed 60 around walls facing forward\n"), _ => { });
Check(wallsFaceSpec.Verb == ActionVerb.Move && wallsFaceSpec.Thing == "player" &&
      wallsFaceSpec.AroundWalls && wallsFaceSpec.FaceForward &&
      wallsFaceSpec.HasNumber && Math.Abs(wallsFaceSpec.Number - 60) < 0.0001 &&
      wallsFaceSpec.Leftover.Count == 0,
    "`move toward player speed 60 around walls facing forward` parses both qualifiers");

var faceDefaultSpeed = ActionWords.Parse(
    RawWords(parser, "when me appears\n  move toward player facing forward\n"), _ => { });
Check(faceDefaultSpeed.FaceForward && faceDefaultSpeed.HasNumber &&
      Math.Abs(faceDefaultSpeed.Number - 5) < 0.0001 &&
      !faceDefaultSpeed.AroundWalls && faceDefaultSpeed.Leftover.Count == 0,
    "`facing forward` alone keeps the default speed and the straight line");

var faceStraight = ActionWords.Parse(
    RawWords(parser, "when me appears\n  move toward player speed 60 facing forward\n"), _ => { });
Check(faceStraight.FaceForward && !faceStraight.AroundWalls &&
      Math.Abs(faceStraight.Number - 60) < 0.0001 && faceStraight.Leftover.Count == 0,
    "`move toward ... speed 60 facing forward` faces the walk without wall steering");

var plainMove = ActionWords.Parse(
    RawWords(parser, "when me appears\n  move forward speed 5\n"), _ => { });
Check(!plainMove.FaceForward && plainMove.Verb == ActionVerb.Move,
    "`facing forward` stays opt-in - a plain move does not turn the body");

var faceThenWalls = ActionWords.Parse(
    RawWords(parser, "when me appears\n  move toward player facing forward around walls\n"), _ => { });
Check(faceThenWalls.FaceForward && !faceThenWalls.AroundWalls && faceThenWalls.Leftover.Count > 0,
    "the qualifier order is fixed: `facing forward` comes after `around walls`");

// Separation: two bodies only react inside radius + neighbourRadius + Gap.
var farNeighbour = new List<SeparationNeighbour>
{
    new SeparationNeighbour { X = 100, Z = 0, Radius = 1 },
};
Check(!Separation.Push(0, 0, 1, farNeighbour, 1, 0, out _, out _, out _),
    "a neighbour outside radius + radius + gap produces no push");
Check(!Separation.Push(0, 0, 1, null, 1, 0, out _, out _, out _),
    "no neighbours means no push");

// Dead ahead: the escape is sideways, never a reversal.
var aheadNeighbour = new List<SeparationNeighbour>
{
    new SeparationNeighbour { X = 5, Z = 0, Radius = 1 },
};
bool aheadPush = Separation.Push(0, 0, 1, aheadNeighbour, 1, 0,
    out double aheadPushX, out double aheadPushZ, out double aheadWeight);
Check(aheadPush && Math.Abs(aheadPushX) < 0.001 && Math.Abs(aheadPushZ + 1) < 0.001 &&
      aheadWeight > 0 && aheadWeight < 1,
    "a neighbour dead ahead pushes exactly sideways (the actor sidesteps, never reverses)");

// A neighbour off to one side pushes away from it; weight grows as the overlap deepens.
var sideNeighbour = new List<SeparationNeighbour>
{
    new SeparationNeighbour { X = 5, Z = 3, Radius = 1 },
};
bool sidePush = Separation.Push(0, 0, 1, sideNeighbour, 1, 0,
    out double sidePushX, out double sidePushZ, out double sideWeight);
Check(sidePush && Math.Abs(sidePushX) < 0.001 && sidePushZ < -0.9,
    "a neighbour ahead-and-to-the-side pushes to the opposite side");

var touchingNeighbour = new List<SeparationNeighbour>
{
    new SeparationNeighbour { X = 2, Z = 0, Radius = 1 },
};
Separation.Push(0, 0, 1, touchingNeighbour, 1, 0, out _, out _, out double touchWeight);
Check(touchWeight > sideWeight && touchWeight <= 1.0,
    "the push weight grows as the overlap deepens and never exceeds 1");

var behindNeighbour = new List<SeparationNeighbour>
{
    new SeparationNeighbour { X = -5, Z = 0, Radius = 1 },
};
Check(!Separation.Push(0, 0, 1, behindNeighbour, 1, 0, out _, out _, out _),
    "a neighbour already behind the actor produces nothing (no reversing)");

var stackedNeighbour = new List<SeparationNeighbour>
{
    new SeparationNeighbour { X = 0, Z = 0, Radius = 1 },
};
bool stackedPush = Separation.Push(0, 0, 1, stackedNeighbour, 0, 1,
    out double stackedPushX, out double stackedPushZ, out _);
Check(stackedPush && Math.Abs(stackedPushX - 1) < 0.001 && Math.Abs(stackedPushZ) < 0.001,
    "two bodies on the exact same spot separate deterministically toward the actor's right");

// BlendHeading: zero push is byte-for-byte unchanged, any push stays a unit heading.
Separation.BlendHeading(1, 0, 0, 1, 0, out double untouchedX, out double untouchedZ);
Check(Math.Abs(untouchedX - 1) < 1e-9 && Math.Abs(untouchedZ) < 1e-9,
    "a zero-weight push leaves the walk heading exactly as it was");
Separation.BlendHeading(1, 0, 0, 1, 1, out double blendedX, out double blendedZ);
Check(Math.Abs(Math.Sqrt(blendedX * blendedX + blendedZ * blendedZ) - 1) < 1e-9 &&
      blendedX > 0.7 && blendedZ > 0.5 && blendedX < 1,
    "a full-weight push bends the heading sideways and keeps it normalized");

// MoveStep: a normal frame is untouched, a hitch is clamped in both directions.
Check(Math.Abs(PhysicsMovement.MoveStep(100, 0.016) - 1.6) < 1e-9,
    "an ordinary frame's step is speed x deltaTime");
Check(Math.Abs(PhysicsMovement.MoveStep(100, 1.0) - PhysicsMovement.MaxStepPerTick) < 1e-9,
    "a hitched frame is clamped to the per-tick cap");
Check(Math.Abs(PhysicsMovement.MoveStep(-100, 1.0) + PhysicsMovement.MaxStepPerTick) < 1e-9,
    "a negative (reverse) step is clamped too");
Check(PhysicsMovement.MoveStep(100, 0) == 0 && PhysicsMovement.MoveStep(100, -1) == 0,
    "no time (or a negative delta) moves nothing");

// Both shipped chases ask for the body to face its walk.
static bool ChaseFacesForward(ProgramFile program)
{
    foreach (var chaseEvent in program.Events)
        foreach (var chaseLine in chaseEvent.Body)
            if (chaseLine.Words.Count >= 2 && chaseLine.Words[0].Text == "move" &&
                chaseLine.Words[1].Text == "toward" &&
                !chaseLine.Words.Any(word => word.Text == "forward"))
                return false;
    return true;
}
var losChaseProgram = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Enemy_LOS.events"));
Check(ChaseFacesForward(wallsProgram) && ChaseFacesForward(losChaseProgram),
    "both enemy sheets chase with `facing forward`");

// ------------------------------------------------------------------
// Task 7 G4 phase 3: `jump` and the on-ground / airborne gates
// ------------------------------------------------------------------

// The word: `jump` alone is the default arc, `jump strength <n>` overrides it, and the amount
// may be the actor's own variable (an enemy whose sheet gives it a JumpPower).
var plainJump = parser.Parse("when every 0 seconds\n  jump");
var plainJumpSpec = ActionWords.Parse(plainJump.Events[0].Body[0].Words, null);
Check(plainJumpSpec.Verb == ActionVerb.Jump &&
      Math.Abs(plainJumpSpec.Number - PhysicsMovement.DefaultJumpStrength) < 1e-9,
    "`jump` parses to ActionVerb.Jump with the default take-off strength");

var strongJump = parser.Parse("when every 0 seconds\n  jump strength 500");
var strongJumpSpec = ActionWords.Parse(strongJump.Events[0].Body[0].Words, null);
Check(strongJumpSpec.Verb == ActionVerb.Jump && Math.Abs(strongJumpSpec.Number - 500) < 1e-9,
    "`jump strength 500` keeps the authored take-off strength");

var variableJump = parser.Parse("when every 0 seconds\n  jump strength my JumpPower");
var variableJumpSpec = ActionWords.Parse(variableJump.Events[0].Body[0].Words, null);
Check(variableJumpSpec.Verb == ActionVerb.Jump &&
      variableJumpSpec.AmountNode is VariableValue jumpAmount && jumpAmount.Name == "JumpPower",
    "`jump strength my JumpPower` reads the amount from the actor's own variable");

// The scripted arc (an actor with no body): up off the strength, down under gravity, landing
// exactly at the take-off height rather than sinking through it.
double jumpHeight = 0, jumpSpeed = PhysicsMovement.JumpVelocity(350), jumpPeak = 0;
bool jumpLanded = false;
for (int jumpTick = 0; jumpTick < 300 && !jumpLanded; jumpTick++)
{
    PhysicsMovement.JumpStep(jumpHeight, jumpSpeed, 981.0, 1.0 / 60.0, out jumpHeight, out jumpSpeed);
    jumpPeak = Math.Max(jumpPeak, jumpHeight);
    jumpLanded = PhysicsMovement.JumpLanded(jumpHeight, jumpSpeed);
}
Check(jumpLanded && jumpPeak > 50 && jumpPeak < 80,
    "a 350 cm/s jump rises about 62 cm and comes back down within the second");
Check(jumpHeight == 0 && jumpSpeed == 0,
    "the scripted arc lands exactly at its take-off height, not below it");
Check(Math.Abs(PhysicsMovement.JumpVelocity(-5) - PhysicsMovement.MinJumpStrength) < 1e-9,
    "a negative strength is floored to the minimum take-off instead of cancelling the jump");

// The conditions: `me is on ground` gates a jump, `me is airborne` is its opposite, and landing
// flips them back — the crate-and-back-down loop the phase's done-when describes.
var groundProg = parser.Parse(@"when every 0 seconds
  me is on ground
  add 1 to GroundTicks

when every 0 seconds
  me is airborne
  add 1 to AirTicks");
Check(groundProg.Events.Count == 2, "`me is on ground` / `me is airborne` parse inside an event body");
Check(Conditions.Parse(groundProg.Events[0].Body[0].Words).Kind == ConditionKind.OnGround,
    "`me is on ground` parses to ConditionKind.OnGround");
Check(Conditions.Parse(groundProg.Events[1].Body[0].Words).Kind == ConditionKind.Airborne,
    "`me is airborne` parses to ConditionKind.Airborne");

var jumpWorld = new FakeWorld();
var jumper = new FakeActor("Jumper");
jumpWorld.AddActor(jumper);
var jumpEngine = FreshEngine(jumpWorld);
jumpEngine.AddSheet(jumper, groundProg);
jumpWorld.SetGrounded(jumper, true);
jumpEngine.Tick(0);
Check(jumpEngine.Variables.GetGlobal("GroundTicks").AsNumber() == 1,
    "`me is on ground` is true for an actor standing on something");
Check(jumpEngine.Variables.GetGlobal("AirTicks").AsNumber() == 0,
    "`me is airborne` is false while the actor is grounded");

jumpWorld.SetGrounded(jumper, false);
jumpEngine.Tick(1);
Check(jumpEngine.Variables.GetGlobal("GroundTicks").AsNumber() == 1 &&
      jumpEngine.Variables.GetGlobal("AirTicks").AsNumber() == 1,
    "`me is airborne` flips true once the actor leaves the ground");

jumpWorld.SetGrounded(jumper, true);
jumpEngine.Tick(2);
Check(jumpEngine.Variables.GetGlobal("GroundTicks").AsNumber() == 2,
    "landing is detected — the on-ground gate goes true again");

// ------------------------------------------------------------------
// Task 7 G4 phase 4: `climb` / `climb down` / `stop climbing`
// ------------------------------------------------------------------

var climbProg = parser.Parse(@"when every 0 seconds
  climb Ladder");
var climbSpec = ActionWords.Parse(climbProg.Events[0].Body[0].Words, null);
Check(climbSpec.Verb == ActionVerb.Climb && climbSpec.Thing == "Ladder" && climbSpec.Dir == "" &&
      Math.Abs(climbSpec.Number - PhysicsMovement.DefaultClimbSpeed) < 1e-9,
    "`climb Ladder` parses to ActionVerb.Climb with the default pace, climbing up");

var fastClimbProg = parser.Parse(@"when every 0 seconds
  climb Ladder speed 120");
var fastClimbSpec = ActionWords.Parse(fastClimbProg.Events[0].Body[0].Words, null);
Check(fastClimbSpec.Verb == ActionVerb.Climb && Math.Abs(fastClimbSpec.Number - 120) < 1e-9,
    "`climb Ladder speed 120` keeps the authored pace");

var downClimbProg = parser.Parse(@"when every 0 seconds
  climb down Ladder");
var downClimbSpec = ActionWords.Parse(downClimbProg.Events[0].Body[0].Words, null);
Check(downClimbSpec.Verb == ActionVerb.Climb && downClimbSpec.Dir == "down" &&
      downClimbSpec.Thing == "Ladder",
    "`climb down Ladder` parses with the downward direction");

var varClimbProg = parser.Parse(@"when every 0 seconds
  climb Ladder speed my ClimbSpeed");
var varClimbSpec = ActionWords.Parse(varClimbProg.Events[0].Body[0].Words, null);
Check(varClimbSpec.Verb == ActionVerb.Climb &&
      varClimbSpec.AmountNode is VariableValue climbAmount && climbAmount.Name == "ClimbSpeed",
    "`climb Ladder speed my ClimbSpeed` reads the pace from the actor's variable");

var letGoProg = parser.Parse(@"when every 0 seconds
  stop climbing");
var letGoSpec = ActionWords.Parse(letGoProg.Events[0].Body[0].Words, null);
Check(letGoSpec.Verb == ActionVerb.StopClimbing, "`stop climbing` parses to ActionVerb.StopClimbing");

// The climb math: a tick moves the authored distance, the span clamps both ends, and reaching
// the end the climb was heading for is reported — that is what ends a climb at the top/bottom.
Check(Math.Abs(PhysicsMovement.ClimbStep(0, 0, 400, 150, 1, 1.0) - 150) < 1e-9,
    "a climb tick moves speed x deltaTime upward");
Check(Math.Abs(PhysicsMovement.ClimbStep(150, 0, 400, 150, -1, 1.0) - 0) < 1e-9,
    "`climb down` moves the same distance downward");
Check(Math.Abs(PhysicsMovement.ClimbStep(390, 0, 400, 150, 1, 1.0) - 400) < 1e-9 &&
      !PhysicsMovement.ClimbFinished(390, 0, 400, 1),
    "the climb clamps at the ladder's top instead of overshooting it");
Check(PhysicsMovement.ClimbFinished(400, 0, 400, 1) &&
      PhysicsMovement.ClimbFinished(0, 0, 400, -1) &&
      !PhysicsMovement.ClimbFinished(400, 0, 400, -1),
    "a climb finishes at the end it was heading toward, not the one it started from");
Check(Math.Abs(PhysicsMovement.ClimbStep(200, 0, 400, -150, 1, 1.0) - 350) < 1e-9,
    "a negative pace is treated as a magnitude — direction comes from `down`");

// `me is climbing` — the ladder state as a condition, so a sheet does not need its own flag.
var climbingProg = parser.Parse(@"when every 0 seconds
  me is climbing
  add 1 to ClimbTicks");
Check(Conditions.Parse(climbingProg.Events[0].Body[0].Words).Kind == ConditionKind.Climbing,
    "`me is climbing` parses to ConditionKind.Climbing");

var climbWorld = new FakeWorld();
var climber = new FakeActor("Climber");
climbWorld.AddActor(climber);
var climbEngine = FreshEngine(climbWorld);
climbEngine.AddSheet(climber, climbingProg);
climbEngine.Tick(0);
Check(climbEngine.Variables.GetGlobal("ClimbTicks").AsNumber() == 0,
    "`me is climbing` is false for an actor standing on the ground");

climbWorld.SetClimbing(climber, true);
climbEngine.Tick(1);
Check(climbEngine.Variables.GetGlobal("ClimbTicks").AsNumber() == 1,
    "`me is climbing` fires while the actor holds a ladder");

climbWorld.SetClimbing(climber, false);
climbEngine.Tick(2);
Check(climbEngine.Variables.GetGlobal("ClimbTicks").AsNumber() == 1,
    "`me is climbing` goes false again once the actor lets go");

// ------------------------------------------------------------------
// Task 9 E3: spell data table + `cast`
// ------------------------------------------------------------------

var castProg = parser.Parse("when every 0 seconds\n  cast \"Fireball\"\n");
var castSpec = ActionWords.Parse(castProg.Events[0].Body[0].Words, null);
Check(castSpec.Verb == ActionVerb.Cast && castSpec.Spell == "Fireball" && castSpec.Thing == "Fireball",
    "`cast \"Fireball\"` parses to ActionVerb.Cast with its spell name");

var castAtProg = parser.Parse("when every 0 seconds\n  cast \"Heal\" at player\n");
var castAtSpec = ActionWords.Parse(castAtProg.Events[0].Body[0].Words, null);
Check(castAtSpec.Verb == ActionVerb.Cast && castAtSpec.Spell == "Heal" && castAtSpec.To == "player",
    "`cast \"Heal\" at player` carries the spell and the target");

string castWarning = "";
var bareCastProg = parser.Parse("when every 0 seconds\n  cast Fireball\n");
var bareCastSpec = ActionWords.Parse(bareCastProg.Events[0].Body[0].Words, message => castWarning = message);
Check(bareCastSpec.Verb == ActionVerb.Unknown && castWarning.Contains("quotes"),
    "`cast` without a quoted spell name explains the quotes instead of guessing a target");

Check(Array.Exists(WordRegistry.All, entry => entry.Word == "cast"),
    "the word registry lists `cast` (docs, autocomplete and typo suggestions)");

// The table: `<Spell><Field>` globals, exactly like weapon data.
var spellParser = new EventParser();
SpellData.LoadData(spellParser.Parse(@"when game starts
  set FireballMana to 10
  set FireballCooldown to 1.2
  set FireballDamage to 20
  set FireballBlast to 180
  set FireballElement to ""fire""
  set FireballBullet to ""Fireball""
  set FireballSpeed to 420
"));
Check(SpellData.TryGet(null, "fireball", out SpellStats fireball) &&
      fireball.Name == "Fireball" && fireball.Mana == 10 && Math.Abs(fireball.Cooldown - 1.2) < 1e-9 &&
      fireball.Damage == 20 && fireball.Blast == 180 && fireball.Element == "fire" &&
      fireball.Bullet == "Fireball" && fireball.Speed == 420,
    "`<Spell><Field>` globals define a spell (casing-insensitive lookup)");
Check(!SpellData.TryGet(null, "Banana", out _), "a spell nobody defined is not invented");
Check(SpellData.KnownNames(null).Contains("Fireball"), "the unknown-spell help lists what the project has");

// Cooldowns: per actor AND per spell, so two spells on one sheet are independent.
var spellClock = new SpellClock();
var mage = new FakeActor("Mage");
spellClock.Start(mage, "Fireball", 1.2, 5);
Check(spellClock.IsCooling(mage, "Fireball", 5.5) &&
      Math.Abs(spellClock.Remaining(mage, "Fireball", 5.5) - 0.7) < 1e-9,
    "a started cooldown is live and reports the seconds left");
Check(!spellClock.IsCooling(mage, "Heal", 5.5), "another spell on the same actor is still ready");
Check(!spellClock.IsCooling(mage, "Fireball", 6.3), "the cooldown expires on its own");
spellClock.Start(mage, "Fireball", 1.2, 7);
spellClock.Clear(mage);
Check(!spellClock.IsCooling(mage, "Fireball", 7.1), "a destroyed actor's cooldowns are cleared");

// `my <Spell>Cooldown` is a computed readout (like `my Ammo`), never a stored live timer.
var spellWorld = new FakeWorld();
var spellMage = new FakeActor("Mage");
spellWorld.AddActor(spellMage);
var spellEngine = FreshEngine(spellWorld);
spellEngine.AddSheet(spellMage, spellParser.Parse("when game starts\n  set Mana to 30\n"));
spellEngine.Tick(0);
spellEngine.Spells.Start(spellMage, "Fireball", 1.2, 0);
var spellContext = new EventContext { Runtime = spellEngine, Variables = spellEngine.Variables, Me = spellMage };
Check(Math.Abs(spellContext.ReadMy("FireballCooldown").AsNumber() - 1.2) < 1e-9,
    "`my FireballCooldown` reads the live seconds left");
Check(!spellEngine.Variables.HasLocal(spellMage, "FireballCooldown"),
    "the cooldown readout is computed, not a mirrored variable");

// The shipped data file defines the two spells the task names.
var spellDataEvents = spellParser.Parse(File.ReadAllText("Source/Game/GameFiles/SpellData.events"));
Check(spellDataEvents.Events.Count == 1, "SpellData.events parses (one `when game starts` block)");
SpellData.LoadData(spellDataEvents);
SpellData.TryGet(null, "Fireball", out SpellStats fileFireball);
SpellData.TryGet(null, "Heal", out SpellStats fileHeal);
Check(fileFireball != null && fileFireball.Mana == 10 && Math.Abs(fileFireball.Cooldown - 1.2) < 1e-9 &&
      fileFireball.Bullet == "Fireball" && fileFireball.Element == "fire",
    "the shipped SpellData.events defines Fireball's mana, cooldown and element");
Check(fileHeal != null && fileHeal.Bullet == "" && fileHeal.Mana == 15,
    "Heal is a no-projectile spell (the message branch)");

// ------------------------------------------------------------------
// Task 9 E7: item rarity + affix generator
// ------------------------------------------------------------------

var rareSpawnProg = parser.Parse("when me appears\n  spawn Ring at me rarity \"Rare\" level 12\n");
var rareSpawnSpec = ActionWords.Parse(rareSpawnProg.Events[0].Body[0].Words, null);
Check(rareSpawnSpec.Verb == ActionVerb.Spawn && rareSpawnSpec.Thing == "Ring" &&
      rareSpawnSpec.To == "me" && rareSpawnSpec.HasRarity && rareSpawnSpec.Rarity == "Rare" &&
      rareSpawnSpec.HasItemLevel && Math.Abs(rareSpawnSpec.ItemLevel - 12) < 1e-9,
    "`spawn Ring at me rarity \"Rare\" level 12` parses the item qualifiers");

var plainSpawnProg = parser.Parse("when me appears\n  spawn Sword at me\n");
var plainSpawnSpec = ActionWords.Parse(plainSpawnProg.Events[0].Body[0].Words, null);
Check(plainSpawnSpec.Verb == ActionVerb.Spawn && !plainSpawnSpec.HasRarity && !plainSpawnSpec.HasItemLevel,
    "a plain `spawn` rolls nothing, so every existing item sheet is untouched");

var bothSpawnProg = parser.Parse("when me appears\n  spawn Ring at me offset 20 rarity \"Magic\"\n");
var bothSpawnSpec = ActionWords.Parse(bothSpawnProg.Events[0].Body[0].Words, null);
Check(bothSpawnSpec.HasSpawnOffset && Math.Abs(bothSpawnSpec.SpawnOffset - 20) < 1e-9 &&
      bothSpawnSpec.HasRarity && bothSpawnSpec.Rarity == "Magic",
    "`offset` and `rarity` may appear together, in either order");

// Base + affix data and the generator's pure math.
var itemParser = new EventParser();
ItemData.LoadData(itemParser.Parse(@"when game starts
  set RingSlot to ""ring""
  set RingArmor to 3
"));
Check(ItemData.TryGet(null, "ring", out ItemBase testRing) && testRing.Slot == "ring" && testRing.Armor == 3,
    "`<Base><Field>` globals define an item base");

AffixData.LoadData(itemParser.Parse(@"when game starts
  set RareMinAffixes to 1
  set RareMaxAffixes to 3
  set FieryStat to ""FireResist""
  set FieryMin to 5
  set FieryMax to 15
  set FieryItemLevel to 1
  set FieryWord to ""Fiery""
  set FoxyStat to ""Dexterity""
  set FoxyMin to 6
  set FoxyMax to 14
  set FoxyItemLevel to 10
  set FoxyWord to ""Fox""
  set FoxySuffix to true
"));
var testRarity = AffixData.RarityFor(null, "rare");
Check(testRarity != null && testRarity.MinAffixes == 1 && testRarity.MaxAffixes == 3,
    "a rarity row sets its affix count range");
Check(AffixData.RarityFor(null, "Common") != null && AffixData.RarityFor(null, "Rare").MaxAffixes == 3,
    "built-in rarity counts survive when the data file has no row for them");
Check(AffixData.RarityFor(null, "Legendary") == null, "an unknown rarity is reported, not invented");
Check(ItemGenerator.AffixCount(testRarity, 5, 0.0) == 1 &&
      ItemGenerator.AffixCount(testRarity, 5, 0.999) == 3,
    "the affix count spans the rarity's range");
Check(ItemGenerator.AffixCount(testRarity, 1, 0.9) == 1,
    "the count is clamped to the eligible pool (a small pool never over-rolls)");
var fieryDef = AffixData.DefOf(null, "Fiery");
Check(fieryDef != null && fieryDef.Stat == "FireResist" && fieryDef.Min == 5 && fieryDef.Max == 15,
    "an affix row reads its stat and roll range");
Check(ItemGenerator.RollValue(fieryDef, 1, 0) == 5 && ItemGenerator.RollValue(fieryDef, 1, 1) == 15,
    "an affix rolls inside its min..max range");
Check(ItemGenerator.RollValue(fieryDef, 11, 0) > ItemGenerator.RollValue(fieryDef, 1, 0),
    "higher item levels roll stronger affix values");
Check(ItemGenerator.ComposeName(new ItemBase { Name = "Ring" }, new List<AffixDef>
      {
          new AffixDef { Name = "Fiery", Word = "Fiery" },
          new AffixDef { Name = "Fox", Word = "Fox", Suffix = true },
      }) == "Fiery Ring of the Fox",
    "the item's display name puts prefixes before and suffixes after the base");
Check(ItemGenerator.Roll(null, "Banana", "Rare", 1) == null, "a base nobody defined rolls nothing");

// The shipped tables, and a real roll through them.
var itemDataEvents = itemParser.Parse(File.ReadAllText("Source/Game/GameFiles/ItemData.events"));
var affixDataEvents = itemParser.Parse(File.ReadAllText("Source/Game/GameFiles/AffixData.events"));
Check(itemDataEvents.Events.Count == 1, "ItemData.events parses");
Check(affixDataEvents.Events.Count == 1, "AffixData.events parses");
ItemData.LoadData(itemDataEvents);
AffixData.LoadData(affixDataEvents);
Check(ItemData.TryGet(null, "Sword", out ItemBase dataSword) && dataSword.Damage == 12 && dataSword.Slot == "weapon",
    "the shipped ItemData.events defines the Sword's base damage");
Check(AffixData.All(null).Count == 11, "the shipped AffixData.events defines eleven affixes");
Check(AffixData.RarityFor(null, "Rare").MaxAffixes == 3 && AffixData.RarityFor(null, "Common").MaxAffixes == 0,
    "the shipped rarities read Common 0 / Rare 1-3");

Rng.Seed(7);
ItemRoll rareRoll = ItemGenerator.Roll(null, "Ring", "Rare", 12);
Check(rareRoll != null && rareRoll.AffixNames.Count >= 1 && rareRoll.AffixNames.Count <= 3,
    "a rare ring rolls 1-3 affixes");
Check(rareRoll.Stats.Count == rareRoll.AffixNames.Count, "a ring with no base stats grants exactly its affixes");
CheckStrings("Rare", rareRoll.Rarity, "the roll records its rarity");
Check(rareRoll.DisplayName.Contains("Ring"), "the rolled item carries a display name");
bool duplicateStat = false;
for (int i = 0; i < rareRoll.Stats.Count; i++)
    for (int j = i + 1; j < rareRoll.Stats.Count; j++)
        if (rareRoll.Stats[i].Stat == rareRoll.Stats[j].Stat)
            duplicateStat = true;
Check(!duplicateStat, "the same stat never rolls twice on one item");

Check(ItemGenerator.Roll(null, "Ring", "Common", 5).AffixNames.Count == 0,
    "a common item rolls no affixes");

Rng.Seed(42);
ItemRoll repeatedA = ItemGenerator.Roll(null, "Ring", "Rare", 12);
Rng.Seed(42);
ItemRoll repeatedB = ItemGenerator.Roll(null, "Ring", "Rare", 12);
CheckStrings(string.Join(",", repeatedA.AffixNames), string.Join(",", repeatedB.AffixNames),
    "a seeded roll replays the exact same affixes");
Check(repeatedA.Stats.Count == repeatedB.Stats.Count, "...and the same stat count");

// Item-level gating: a level-1 roll never includes a level-10 affix.
Rng.Seed(3);
bool gatedAffixLeaked = false;
for (int i = 0; i < 50; i++)
{
    ItemRoll lowRoll = ItemGenerator.Roll(null, "Ring", "Rare", 1);
    foreach (string affixName in lowRoll.AffixNames)
        if (AffixData.DefOf(null, affixName).ItemLevel > 1)
            gatedAffixLeaked = true;
}
Check(!gatedAffixLeaked, "an affix above the item level never rolls");

Rng.Seed(11);
bool sawLateAffix = false;
for (int i = 0; i < 200; i++)
{
    ItemRoll highRoll = ItemGenerator.Roll(null, "Ring", "Rare", 12);
    if (highRoll.AffixNames.Contains("Foxy"))
        sawLateAffix = true;
}
Check(sawLateAffix, "the level-10 affix does roll at item level 12");

// Applying a roll: exactly the rolled numbers go on, and come back off.
var affixStore = new ItemAffixes();
var wearerVariables = new EventVariables();
var wearer = new FakeActor("Player");
var rolledRing = new FakeActor("Ring");
var manualRoll = new ItemRoll { Base = "Ring", Rarity = "Rare", DisplayName = "Fiery Ring" };
manualRoll.Stats.Add(new ItemStat("FireResist", 12));
manualRoll.Stats.Add(new ItemStat("damage", 3));
affixStore.Stamp(rolledRing, manualRoll);
Check(affixStore.HasRoll(rolledRing), "a stamped item remembers its roll");
Check(affixStore.Apply(wearerVariables, wearer, "Ring", rolledRing), "equipping a rolled item applies it");
Check(wearerVariables.GetLocal(wearer, "FireResist").AsNumber() == 12 &&
      wearerVariables.GetLocal(wearer, "damage").AsNumber() == 3,
    "the wearer gains exactly the rolled stats");
Check(affixStore.Remove(wearerVariables, wearer, "Ring"), "unequipping removes the roll");
Check(wearerVariables.GetLocal(wearer, "FireResist").AsNumber() == 0 &&
      wearerVariables.GetLocal(wearer, "damage").AsNumber() == 0,
    "the wearer's stats go back to exactly what they were");
Check(!affixStore.Apply(wearerVariables, wearer, "Sword", new FakeActor("Sword")),
    "a hand-authored item with no roll applies nothing (its own sheet does the work)");

// The bag keeps which actor was picked up, so `equip Ring` applies THAT ring's affixes.
var ringBagWorld = new FakeWorld();
var ringBagPlayer = new FakeActor("Player");
ringBagWorld.AddActor(ringBagPlayer);
var ringBagEngine = FreshEngine(ringBagWorld);
var droppedRing = new FakeActor("Ring");
ringBagEngine.Affixes.Stamp(droppedRing, manualRoll);
ringBagEngine.BagAdd(ringBagPlayer, "Ring", droppedRing);
Check(ringBagEngine.BagCount(ringBagPlayer, "Ring") == 1 && ringBagEngine.BagHas(ringBagPlayer, "Ring"),
    "`add Ring to my bag` still counts by name (`my bag` reads `Ring`)");
Check(ringBagEngine.TakeBagItem(ringBagPlayer, "Ring", out object takenRing) && ReferenceEquals(takenRing, droppedRing),
    "the bag reports which ring was carried out");
Check(ringBagEngine.BagCount(ringBagPlayer, "Ring") == 0, "taking an item decrements the count");
ringBagEngine.BagAdd(ringBagPlayer, "Ring", droppedRing);
ringBagEngine.TakeBagItem(ringBagPlayer, "Ring", out object wornRing);
ringBagEngine.EquipItem(ringBagPlayer, "Ring", wornRing);
Check(ringBagEngine.Variables.GetLocal(ringBagPlayer, "FireResist").AsNumber() == 12,
    "`equip` applies the rolled ring's stats to the wearer");
Check(ringBagEngine.IsEquipped(ringBagPlayer, "Ring"), "the wearer still records the item as worn");
ringBagEngine.UnequipItem(ringBagPlayer, "Ring");
Check(ringBagEngine.Variables.GetLocal(ringBagPlayer, "FireResist").AsNumber() == 0,
    "unequipping takes back exactly the rolled stats");

// ---- Task 9 F1: applied stats are keyed by the item instance, not by (wearer, name) ----
// Two rings are both called "Ring"; they must stack and unstack independently.
var twoRingStore = new ItemAffixes();
var twoRingVariables = new EventVariables();
var twoRingWearer = new FakeActor("Player");
var firstRing = new FakeActor("Ring");
var secondRing = new FakeActor("Ring");
var firstRoll = new ItemRoll { Base = "Ring", Rarity = "Rare", DisplayName = "Fiery Ring" };
firstRoll.Stats.Add(new ItemStat("FireResist", 10));
var secondRoll = new ItemRoll { Base = "Ring", Rarity = "Rare", DisplayName = "Strong Ring" };
secondRoll.Stats.Add(new ItemStat("FireResist", 25));
secondRoll.Stats.Add(new ItemStat("Strength", 4));
twoRingStore.Stamp(firstRing, firstRoll);
twoRingStore.Stamp(secondRing, secondRoll);
Check(twoRingStore.Apply(twoRingVariables, twoRingWearer, "Ring", firstRing),
    "the first of two same-named rings applies");
Check(twoRingStore.Apply(twoRingVariables, twoRingWearer, "Ring", secondRing),
    "the second ring applies instead of overwriting the first's record");
Check(twoRingVariables.GetLocal(twoRingWearer, "FireResist").AsNumber() == 35 &&
      twoRingVariables.GetLocal(twoRingWearer, "Strength").AsNumber() == 4,
    "two same-named rings stack (10 + 25 fire resist, +4 strength)");
Check(twoRingStore.Apply(twoRingVariables, twoRingWearer, "Ring", firstRing) &&
      twoRingVariables.GetLocal(twoRingWearer, "FireResist").AsNumber() == 35,
    "re-equipping the same instance is idempotent, not a second stack");
Check(twoRingStore.Remove(twoRingVariables, twoRingWearer, "Ring") &&
      twoRingVariables.GetLocal(twoRingWearer, "FireResist").AsNumber() == 25 &&
      twoRingVariables.GetLocal(twoRingWearer, "Strength").AsNumber() == 4,
    "unequipping one ring takes back only that ring's stats");
Check(twoRingStore.Remove(twoRingVariables, twoRingWearer, "Ring") &&
      twoRingVariables.GetLocal(twoRingWearer, "FireResist").AsNumber() == 0 &&
      twoRingVariables.GetLocal(twoRingWearer, "Strength").AsNumber() == 0,
    "unequipping the second ring returns the wearer to baseline");
Check(!twoRingStore.Remove(twoRingVariables, twoRingWearer, "Ring"),
    "unequipping a ring that is not worn removes nothing");

// The same two rings through the real path: bag -> equip -> unequip.
var twoRingWorld = new FakeWorld();
var twoRingPlayer = new FakeActor("Player");
var bagRingA = new FakeActor("Ring");
var bagRingB = new FakeActor("Ring");
twoRingWorld.AddActor(twoRingPlayer);
var twoRingEngine = FreshEngine(twoRingWorld);
twoRingEngine.Affixes.Stamp(bagRingA, firstRoll);
twoRingEngine.Affixes.Stamp(bagRingB, secondRoll);
twoRingEngine.BagAdd(twoRingPlayer, "Ring", bagRingA);
twoRingEngine.BagAdd(twoRingPlayer, "Ring", bagRingB);
twoRingEngine.TakeBagItem(twoRingPlayer, "Ring", out object equippedA);
twoRingEngine.EquipItem(twoRingPlayer, "Ring", equippedA);
twoRingEngine.TakeBagItem(twoRingPlayer, "Ring", out object equippedB);
twoRingEngine.EquipItem(twoRingPlayer, "Ring", equippedB);
Check(twoRingEngine.Variables.GetLocal(twoRingPlayer, "FireResist").AsNumber() == 35,
    "two same-named rings from the bag both count while worn");
twoRingEngine.UnequipItem(twoRingPlayer, "Ring");
twoRingEngine.UnequipItem(twoRingPlayer, "Ring");
Check(twoRingEngine.Variables.GetLocal(twoRingPlayer, "FireResist").AsNumber() == 0 &&
      twoRingEngine.Variables.GetLocal(twoRingPlayer, "Strength").AsNumber() == 0,
    "unequipping them in turn leaves no stats behind");

// A worn item that is destroyed hands its stats back (destroy on a ring, or a scene unload).
var brokenRingStore = new ItemAffixes();
var brokenRingVariables = new EventVariables();
var brokenRingWearer = new FakeActor("Player");
var brokenRing = new FakeActor("Ring");
var brokenRoll = new ItemRoll { Base = "Ring", Rarity = "Magic" };
brokenRoll.Stats.Add(new ItemStat("MaxMana", 15));
brokenRingStore.Stamp(brokenRing, brokenRoll);
brokenRingStore.Apply(brokenRingVariables, brokenRingWearer, "Ring", brokenRing);
Check(ReferenceEquals(brokenRingStore.WearerOf(brokenRing), brokenRingWearer) &&
      brokenRingVariables.GetLocal(brokenRingWearer, "MaxMana").AsNumber() == 15,
    "a worn instance reports its wearer");
brokenRingStore.Forget(brokenRingVariables, brokenRing);
Check(brokenRingVariables.GetLocal(brokenRingWearer, "MaxMana").AsNumber() == 0,
    "destroying a worn item takes its stats back off the wearer");
Check(!brokenRingStore.HasRoll(brokenRing),
    "a destroyed item also forgets its roll");

// ------------------------------------------------------------------
// Task 31: Audio — music, buses and fades
// ------------------------------------------------------------------

var musicSpec = ActionWords.Parse(RawWords(actionParser, "when scene starts\n  play music \"level1\""), null);
Check(musicSpec.Verb == ActionVerb.PlayMusic && musicSpec.Thing == "level1",
    "`play music \"level1\"` parses to PlayMusic");

var loopMusicSpec = ActionWords.Parse(RawWords(actionParser, "when scene starts\n  loop music \"menu\""), null);
Check(loopMusicSpec.Verb == ActionVerb.LoopMusic && loopMusicSpec.Thing == "menu",
    "`loop music \"menu\"` parses to LoopMusic");

var stopMusicSpec = ActionWords.Parse(RawWords(actionParser, "when scene starts\n  stop music"), null);
Check(stopMusicSpec.Verb == ActionVerb.StopMusic && stopMusicSpec.Thing.Length == 0,
    "`stop music` parses to StopMusic");

var fadeMusicSpec = ActionWords.Parse(RawWords(actionParser, "when PlayButton is clicked\n  fade music to 0 in 0.5"), null);
Check(fadeMusicSpec.Verb == ActionVerb.FadeMusic && fadeMusicSpec.Number == 0 &&
      Math.Abs(fadeMusicSpec.Duration - 0.5) < 1e-9,
    "`fade music to 0 in 0.5` parses the target and the seconds");

var duckMusicSpec = ActionWords.Parse(RawWords(actionParser, "when NPC is clicked\n  duck music by 0.6 for 4"), null);
Check(duckMusicSpec.Verb == ActionVerb.DuckMusic && Math.Abs(duckMusicSpec.Number - 0.6) < 1e-9 &&
      Math.Abs(duckMusicSpec.Duration - 4) < 1e-9,
    "`duck music by 0.6 for 4` parses the depth and the time");

var setMusicVolumeSpec = ActionWords.Parse(RawWords(actionParser, "when game starts\n  set music volume to 0.4"), null);
Check(setMusicVolumeSpec.Verb == ActionVerb.SetMusicVolume && Math.Abs(setMusicVolumeSpec.Number - 0.4) < 1e-9,
    "`set music volume to 0.4` parses to SetMusicVolume");

var setSoundVolumeSpec = ActionWords.Parse(RawWords(actionParser, "when game starts\n  set sound volume to 0.7"), null);
Check(setSoundVolumeSpec.Verb == ActionVerb.SetSoundVolume && Math.Abs(setSoundVolumeSpec.Number - 0.7) < 1e-9,
    "`set sound volume to 0.7` parses to SetSoundVolume");

var setMasterVolumeSpec = ActionWords.Parse(RawWords(actionParser, "when game starts\n  set master volume to 0.8"), null);
Check(setMasterVolumeSpec.Verb == ActionVerb.SetMasterVolume && Math.Abs(setMasterVolumeSpec.Number - 0.8) < 1e-9,
    "`set master volume to 0.8` parses to SetMasterVolume");

var setMusicFromVariableSpec = ActionWords.Parse(RawWords(actionParser, "when scene starts\n  set music volume to my SavedMusic"), null);
Check(setMusicFromVariableSpec.Verb == ActionVerb.SetMusicVolume && !setMusicFromVariableSpec.HasNumber &&
      setMusicFromVariableSpec.AmountNode != null,
    "a volume word takes a value, not just a literal: `set music volume to my SavedMusic`");

var positionalSoundSpec = ActionWords.Parse(RawWords(actionParser, "when Space is pressed\n  play sound \"shot\" at Enemy"), null);
Check(positionalSoundSpec.Verb == ActionVerb.PlaySound && positionalSoundSpec.Thing == "shot" &&
      positionalSoundSpec.To == "Enemy",
    "`play sound \"shot\" at Enemy` keeps the clip and the place");

var plainSoundSpec = ActionWords.Parse(RawWords(actionParser, "when Space is pressed\n  play sound \"shot\""), null);
Check(plainSoundSpec.Verb == ActionVerb.PlaySound && plainSoundSpec.To.Length == 0,
    "a plain `play sound` still has no positional target");

string fadeHint = "";
var badFade = ActionWords.Parse(RawWords(actionParser, "when game starts\n  fade music"), message => fadeHint = message);
Check(badFade.Verb == ActionVerb.Unknown && fadeHint.Contains("fade music to 0 in 2"),
    "a broken `fade` line explains the shape it wants");

string playMusicHint = "";
var badPlayMusic = ActionWords.Parse(RawWords(actionParser, "when game starts\n  play music"), message => playMusicHint = message);
Check(badPlayMusic.Verb == ActionVerb.Unknown && playMusicHint.Contains("play music"),
    "a bare `play music` explains that it needs a quoted clip name");

var musicEndProg = parser.Parse("when music ends\n  set MusicDone to 1\n");
var musicEndDesc = TriggerClassifier.Classify(musicEndProg.Events[0]);
Check(musicEndDesc.Kind == TriggerKind.MusicEnd && musicEndDesc.RequireName.Length == 0,
    "`when music ends` classifies to MusicEnd with no track filter");

var musicEndEngine = FreshEngine(new FakeWorld());
musicEndEngine.AddSheet(new FakeActor("Menu"), musicEndProg);
musicEndEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.MusicEnd, Name = "menu" });
musicEndEngine.Tick(0);
Check(musicEndEngine.Variables.GetGlobal("MusicDone").AsNumber() == 1,
    "`when music ends` fires for any track");

var namedMusicProg = parser.Parse("when music \"boss\" ends\n  set BossDone to 1\n");
var namedMusicDesc = TriggerClassifier.Classify(namedMusicProg.Events[0]);
Check(namedMusicDesc.Kind == TriggerKind.MusicEnd && namedMusicDesc.RequireName == "boss",
    "`when music \"boss\" ends` classifies to MusicEnd with the track name");
var namedMusicEngine = FreshEngine(new FakeWorld());
namedMusicEngine.AddSheet(new FakeActor("Arena"), namedMusicProg);
namedMusicEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.MusicEnd, Name = "menu" });
namedMusicEngine.Tick(0);
Check(namedMusicEngine.Variables.GetGlobal("BossDone").AsNumber() == 0,
    "a named music trigger ignores another track ending");
namedMusicEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.MusicEnd, Name = "Boss" });
namedMusicEngine.Tick(0);
Check(namedMusicEngine.Variables.GetGlobal("BossDone").AsNumber() == 1,
    "a named music trigger answers its own track, case-insensitively");

// ---- the mix itself: volumes, fades, ducks (pure, no Flax) ----
var mix = new AudioMix();
Check(mix.EffectiveMusicVolume == 1 && mix.EffectiveSoundVolume == 1,
    "a fresh mix plays everything at full volume");
mix.SetMasterVolume(0.5);
mix.SetMusicVolume(0.4);
mix.SetSoundVolume(0.6);
Check(Math.Abs(mix.EffectiveMusicVolume - 0.2) < 1e-9,
    "music volume is master x music (0.5 x 0.4 = 0.2)");
Check(Math.Abs(mix.EffectiveSoundVolume - 0.3) < 1e-9,
    "sound volume is master x sound (0.5 x 0.6 = 0.3)");
mix.SetMasterVolume(0);
Check(mix.EffectiveMusicVolume == 0 && mix.EffectiveSoundVolume == 0,
    "master at 0 silences music and effects");
mix.SetMasterVolume(2);
Check(mix.Master == 1, "volumes clamp at 1");
mix.SetSoundVolume(-1);
Check(mix.Sound == 0, "volumes clamp at 0");

var fadeMix = new AudioMix();
fadeMix.FadeMusicTo(0, 2);
fadeMix.Update(1);
Check(Math.Abs(fadeMix.Music - 0.5) < 1e-9 && fadeMix.Fading,
    "a 2 s fade to 0 is halfway after 1 s");
fadeMix.Update(1);
Check(fadeMix.Music == 0 && !fadeMix.Fading,
    "the fade lands exactly on its target and stops");
Check(fadeMix.MusicSetting == 1,
    "a fade to 0 keeps the authored music level for the next track");
fadeMix.StartMusic("level1", true);
Check(fadeMix.Fading && Math.Abs(fadeMix.FadeTarget - 1) < 1e-9,
    "starting a track after a fade-out ramps back to the authored level");
fadeMix.Update(AudioMix.CrossfadeSeconds);
Check(Math.Abs(fadeMix.Music - 1) < 1e-9 && !fadeMix.Fading,
    "the crossfade completes at the authored level");
fadeMix.FadeMusicTo(0.4, 1);
Check(Math.Abs(fadeMix.MusicSetting - 0.4) < 1e-9,
    "a fade to an audible level becomes the authored level");

var duckMix = new AudioMix();
duckMix.DuckMusic(0.6, 4);
duckMix.Update(AudioMix.DuckAttackSeconds);
Check(Math.Abs(duckMix.DuckMultiplier - 0.4) < 1e-9 && duckMix.Ducking,
    "duck 0.6 takes the music to 40% after the attack");
Check(Math.Abs(duckMix.EffectiveMusicVolume - 0.4) < 1e-9,
    "the duck reaches the music source's volume");
duckMix.Update(2.0);
Check(Math.Abs(duckMix.DuckMultiplier - 0.4) < 1e-9,
    "the duck holds while dialogue runs");
duckMix.Update(2.0);
Check(duckMix.DuckMultiplier == 1 && !duckMix.Ducking,
    "the duck restores the music when its time runs out");
duckMix.DuckMusic(0.5, 0);
Check(!duckMix.Ducking, "a duck with no time does nothing");

var stopMix = new AudioMix();
stopMix.StartMusic("menu", true);
Check(stopMix.Playing && stopMix.Track == "menu" && stopMix.TrackLooping,
    "`loop music` records the looping track");
stopMix.StopMusic();
Check(!stopMix.Playing && stopMix.Track == "",
    "`stop music` clears the track");
stopMix.StartMusic("boss", false);
Check(stopMix.Playing && !stopMix.TrackLooping,
    "`play music` records a one-shot track that `when music ends` can answer");

Check(WordRegistry.All.Any(entry => entry.Kind == "action" && entry.Word == "play music") &&
      WordRegistry.All.Any(entry => entry.Kind == "trigger" && entry.Word == "when music ends") &&
      WordRegistry.All.Any(entry => entry.Kind == "action" && entry.Word == "duck" &&
                                        entry.Example == "duck music by 0.6 for 4"),
    "the word book lists the new audio words");

// ---- `play music random from "a, b, c"` — a track picked out of a quoted list ----
var playRandomMusic = ActionWords.Parse(
    RawWords(actionParser, "when scene starts\n  play music random from \"track_a, track_b, track_c\""), null);
Check(playRandomMusic.Verb == ActionVerb.PlayMusic && playRandomMusic.Thing.Length == 0 &&
      playRandomMusic.Choices.Count == 3 && playRandomMusic.Choices[0] == "track_a" &&
      playRandomMusic.Choices[2] == "track_c",
    "`play music random from \"a, b, c\"` parses to PlayMusic with three choices");

var loopRandomMusic = ActionWords.Parse(
    RawWords(actionParser, "when scene starts\n  loop music random from \"menu,level1\""), null);
Check(loopRandomMusic.Verb == ActionVerb.LoopMusic &&
      loopRandomMusic.Choices.Count == 2 && loopRandomMusic.Choices[1] == "level1",
    "`loop music random from` works the same way, with or without spaces");

var spacedChoices = AudioMix.SplitChoices(" a , b , ");
Check(spacedChoices.Count == 2 && spacedChoices[0] == "a" && spacedChoices[1] == "b",
    "choice names are trimmed and a trailing comma is ignored");
Check(AudioMix.SplitChoices("").Count == 0 && AudioMix.SplitChoices(null).Count == 0,
    "an empty choice list has no names");

var threeChoices = new List<string> { "a", "b", "c" };
Check(AudioMix.PickChoice(threeChoices, 0) == "a", "a roll of 0 picks the first track");
Check(AudioMix.PickChoice(threeChoices, 0.5) == "b", "a mid roll picks the middle track");
Check(AudioMix.PickChoice(threeChoices, 0.999) == "c", "a high roll picks the last track");
Check(AudioMix.PickChoice(threeChoices, 1) == "c", "a roll of exactly 1 still lands on the last track");
Check(AudioMix.PickChoice(threeChoices, -1) == "a" && AudioMix.PickChoice(threeChoices, 2) == "c",
    "an out-of-range roll is clamped, never an index error");
Check(AudioMix.PickChoice(new List<string>(), 0.5) == "",
    "an empty list picks nothing instead of throwing");
bool everyRollPicked = true;
for (int i = 0; i < 100; i++)
{
    string picked = AudioMix.PickChoice(threeChoices, i / 100.0);
    if (picked != "a" && picked != "b" && picked != "c")
        everyRollPicked = false;
}
Check(everyRollPicked, "every roll in 0-1 picks one of the listed tracks");

string emptyChoicesHint = "";
var emptyChoices = ActionWords.Parse(
    RawWords(actionParser, "when scene starts\n  play music random from \"\""),
    message => emptyChoicesHint = message);
Check(emptyChoices.Verb == ActionVerb.Unknown && emptyChoicesHint.Contains("at least one clip name"),
    "an empty random list is a friendly error that names the shape");

string bareRandomHint = "";
var bareRandomMusic = ActionWords.Parse(
    RawWords(actionParser, "when scene starts\n  play music random"),
    message => bareRandomHint = message);
Check(bareRandomMusic.Verb == ActionVerb.Unknown && bareRandomHint.Contains("random from"),
    "`play music random` without `from` explains the list shape");

Check(WordRegistry.All.Any(entry => entry.Kind == "action" && entry.Word == "play music random from"),
    "the word book lists the random music list form");

// ------------------------------------------------------------------
// Task 27 Part A: target-pick mode (`aim`) and the `damage area` primitive
// ------------------------------------------------------------------

var aimProg = parser.Parse("when Q is pressed\n  aim \"psi storm\"\n");
var aimSpec = ActionWords.Parse(((RawActionNode)aimProg.Events[0].Body[0].Statement!).Words, _ => { });
Check(aimSpec.Verb == ActionVerb.Aim && aimSpec.Ability == "psi storm",
    "`aim \"psi storm\"` parses as Aim with the ability name");
Check((aimSpec.Accepts & TargetAccepts.Point) != 0 && (aimSpec.Accepts & TargetAccepts.Unit) != 0,
    "`aim` accepts a point or a unit by default");

var aimPoint = parser.Parse("when Q is pressed\n  aim \"psi storm\" at a point for 6 seconds\n");
var aimPointSpec = ActionWords.Parse(((RawActionNode)aimPoint.Events[0].Body[0].Statement!).Words, _ => { });
Check(aimPointSpec.Verb == ActionVerb.Aim && aimPointSpec.Accepts == TargetAccepts.Point &&
      Math.Abs(aimPointSpec.Duration - 6) < 0.0001,
    "`aim ... at a point for 6 seconds` narrows the kind and the timeout");

var aimSelf = parser.Parse("when Q is pressed\n  aim \"stim\" at me\n");
var aimSelfSpec = ActionWords.Parse(((RawActionNode)aimSelf.Events[0].Body[0].Statement!).Words, _ => { });
Check(aimSelfSpec.Verb == ActionVerb.Aim && aimSelfSpec.Accepts == TargetAccepts.Me,
    "`aim ... at me` accepts only the caster");

var cancelAimProg = parser.Parse("when Escape is pressed\n  cancel aim\n");
var cancelAimSpec = ActionWords.Parse(((RawActionNode)cancelAimProg.Events[0].Body[0].Statement!).Words, _ => { });
Check(cancelAimSpec.Verb == ActionVerb.CancelAim, "`cancel aim` parses as CancelAim");

var damageAreaProg = parser.Parse("when Q is pressed\n  damage area around Mouse by 30 for radius 180\n");
var damageAreaSpec = ActionWords.Parse(((RawActionNode)damageAreaProg.Events[0].Body[0].Statement!).Words, _ => { });
Check(damageAreaSpec.Verb == ActionVerb.DamageArea && damageAreaSpec.Thing == "Mouse" &&
      Math.Abs(damageAreaSpec.Number - 30) < 0.0001 && Math.Abs(damageAreaSpec.Radius - 180) < 0.0001,
    "`damage area around Mouse by 30 for radius 180` carries its spot, amount and radius");

var damageAimProg = parser.Parse("when my aim picks a target\n  damage area around my aim target by 30 for radius 180\n");
var damageAimSpec = ActionWords.Parse(((RawActionNode)damageAimProg.Events[0].Body[0].Statement!).Words, _ => { });
Check(damageAimSpec.Verb == ActionVerb.DamageArea && damageAimSpec.Thing == "my aim target",
    "`damage area around my aim target` reads the pick");

var aimTriggerProg = parser.Parse("when my aim picks a target\n  set Picked to 1\n");
var aimTriggerDesc = TriggerClassifier.Classify(aimTriggerProg.Events[0]);
Check(aimTriggerDesc.Kind == TriggerKind.AimPicked,
    "`when my aim picks a target` classifies to AimPicked");
var bareAimTrigger = parser.Parse("when aim picks a target\n  set Picked to 1\n");
Check(TriggerClassifier.Classify(bareAimTrigger.Events[0]).Kind == TriggerKind.AimPicked,
    "the bare `when aim picks a target` spelling classifies too");

var aimCondTokens = new List<Token>
{
    new Token { Type = TokenType.Word, Text = "my" },
    new Token { Type = TokenType.Word, Text = "aim" },
    new Token { Type = TokenType.Word, Text = "is" },
    new Token { Type = TokenType.Word, Text = "a" },
    new Token { Type = TokenType.Word, Text = "unit" },
};
Check(Conditions.Parse(aimCondTokens).Kind == ConditionKind.AimIsUnit,
    "`my aim is a unit` parses to AimIsUnit");

var aimValueProg = parser.Parse("when Q is pressed\n  set AimX to my aim x\n  set AimZ to my aim z\n  set AimName to my aim target\n");
var aimXNode = (SetNode)aimValueProg.Events[0].Body[0].Statement!;
var aimZNode = (SetNode)aimValueProg.Events[0].Body[1].Statement!;
var aimNameNode = (SetNode)aimValueProg.Events[0].Body[2].Statement!;
Check(aimXNode.Value is VariableValue ax && ax.Owner == "my" && ax.Name == "aim x" &&
      aimZNode.Value is VariableValue az && az.Name == "aim z" &&
      aimNameNode.Value is VariableValue aimNameVar && aimNameVar.Name == "aim target",
    "`my aim x` / `my aim z` / `my aim target` read as one variable each");

// The pure state: kinds, reach, validity, timeout, cancel.
var targeting = new TargetingManager();
var aimCaster = new FakeActor("HighTemplar");
var aimVictim = new FakeActor("Marine");
var aimSession = targeting.Begin(aimCaster, "psi storm", TargetAccepts.Point | TargetAccepts.Unit,
    300, 8, 0, 0);
Check(targeting.IsAiming && Math.Abs(aimSession.Remaining - 8) < 0.0001,
    "starting an aim arms it with its timeout");
Check(targeting.TryPick(aimCaster, aimVictim, 100, 500) == null && !aimSession.HasPick,
    "a pick outside the caster's reach is refused");
var landedPick = targeting.TryPick(aimCaster, aimVictim, 100, 100);
Check(landedPick != null && aimSession.HasPick && aimSession.PickIsUnit &&
      ReferenceEquals(aimSession.PickActor, aimVictim) && Math.Abs(aimSession.PickX - 100) < 0.0001,
    "an in-range unit pick lands and records the unit and point");
Check(!targeting.IsAiming && targeting.SessionOf(aimCaster) != null,
    "a landed pick stops the waiting mode but keeps the pick readable");
Check(targeting.TryPick(aimCaster, aimVictim, 0, 0) == null,
    "a finished aim refuses a second pick");

var pointOnlyAim = targeting.Begin(aimCaster, "blizzard", TargetAccepts.Point, 0, 8, 0, 0);
Check(targeting.TryPick(aimCaster, aimVictim, 10, 0) == null && !pointOnlyAim.HasPick,
    "a point-only aim refuses a unit");
Check(targeting.TryPick(aimCaster, 10, 0) != null && !pointOnlyAim.PickIsUnit,
    "a point-only aim accepts the ground and records no unit");

var selfAim = targeting.Begin(aimCaster, "stim", TargetAccepts.Me, 0, 8, 0, 0);
Check(targeting.TryPick(aimCaster, aimVictim, 0, 0) == null && !selfAim.HasPick,
    "a self aim refuses another unit");
Check(targeting.TryPick(aimCaster, aimCaster, 0, 0) != null,
    "a self aim accepts the caster");

var ruledAim = targeting.Begin(aimCaster, "psi storm", TargetAccepts.Unit, 0, 8, 0, 0, _ => false);
Check(targeting.TryPick(aimCaster, aimVictim, 0, 0) == null && !ruledAim.HasPick,
    "the session's validity rule can refuse a unit (matchup / fog)");

var reachAim = targeting.Begin(aimCaster, "psi storm", TargetAccepts.Unit, 100, 8, 0, 0);
targeting.MoveCaster(aimCaster, 500, 0);
Check(targeting.TryPick(aimCaster, aimVictim, 550, 0) != null,
    "the reach follows the caster when it moves");

var expiryAim = targeting.Begin(aimCaster, "psi storm", TargetAccepts.Unit, 0, 2, 0, 0);
Check(targeting.Tick(1).Count == 0 && targeting.IsAiming,
    "an aim survives a tick shorter than its timeout");
var expiredAims = targeting.Tick(1.5);
Check(expiredAims.Count == 1 && ReferenceEquals(expiredAims[0], aimCaster) && !targeting.IsAiming,
    "an aim lapses when its timeout runs out");

var cancelTargeting = new TargetingManager();
cancelTargeting.Begin(aimCaster, "psi storm", TargetAccepts.Unit, 0, 8, 0, 0);
Check(cancelTargeting.Cancel(aimCaster) && cancelTargeting.SessionOf(aimCaster) == null &&
      !cancelTargeting.IsAiming,
    "cancel aim drops the session");
Check(!cancelTargeting.Cancel(aimCaster), "cancelling when nothing is aiming answers false");

var replaceTargeting = new TargetingManager();
var firstAim = replaceTargeting.Begin(aimCaster, "psi storm", TargetAccepts.Unit, 0, 8, 0, 0);
var secondAim = replaceTargeting.Begin(aimCaster, "stim", TargetAccepts.Me, 0, 8, 0, 0);
Check(!ReferenceEquals(firstAim, secondAim) && ReferenceEquals(replaceTargeting.Current, secondAim) &&
      ReferenceEquals(replaceTargeting.SessionOf(aimCaster), secondAim),
    "a second aim replaces the first, so only one picker is ever live");
Check(new TargetingManager().Begin(aimCaster, "x", TargetAccepts.Unit, 0, 0, 0, 0).Timeout ==
      TargetingManager.DefaultTimeout,
    "an aim without a timeout uses the 8 second default");

// The trigger reaches the caster's sheet and carries the ability name.
var aimPushWorld = new FakeWorld();
var aimPushCaster = new FakeActor("HighTemplar");
var aimPushTarget = new FakeActor("Marine");
var aimPushEngine = FreshEngine(aimPushWorld);
aimPushEngine.AddSheet(aimPushCaster,
    parser.Parse("when my aim picks a target\n  set my Picked to 1\n  set my PickedTag to other's Tag\n"));
var aimPushStray = new FakeActor("Stray");
aimPushEngine.AddSheet(aimPushStray, parser.Parse("when my aim picks a target\n  set my StrayPicked to 1\n"));
aimPushEngine.SceneStarted("Level1");
aimPushEngine.Variables.SetLocal(aimPushTarget, "Tag", ValueBox.OfText("Marine"));
aimPushEngine.Tick(1.0);
aimPushEngine.RaisePush(aimPushCaster, new PushSignal
{
    Kind = TriggerKind.AimPicked,
    Name = "psi storm",
    Other = aimPushTarget,
});
aimPushEngine.Tick(1.1);
Check(aimPushEngine.Variables.GetLocal(aimPushCaster, "Picked").AsNumber() == 1,
    "`when my aim picks a target` runs on the caster's sheet");
Check(aimPushEngine.Variables.GetLocal(aimPushCaster, "PickedTag").AsText() == "Marine",
    "the picked unit arrives as `other`");
Check(aimPushEngine.Variables.GetLocal(aimPushStray, "StrayPicked").IsNone() ||
      aimPushEngine.Variables.GetLocal(aimPushStray, "StrayPicked").AsNumber() == 0,
    "another actor's sheet does not hear the pick");

Check(WordRegistry.All.Any(entry => entry.Kind == "trigger" && entry.Word == "when my aim picks a target"),
    "the word book lists the aim trigger");
Check(WordRegistry.All.Any(entry => entry.Kind == "action" && entry.Word == "aim"),
    "the word book lists `aim`");
Check(WordRegistry.All.Any(entry => entry.Kind == "action" && entry.Word == "damage" &&
      entry.Example.Contains("damage area", StringComparison.Ordinal)),
    "the word book lists `damage area`");

// ------------------------------------------------------------------
// Task 27 Part B — per-player tech and weapon values
// ------------------------------------------------------------------

var techVars = new EventVariables();
Check(Tech.LevelOf(techVars, 0, "UpgradeStim") == 0,
    "a tech level nobody wrote reads 0");
techVars.SetGlobal("Player2 UpgradeStim", ValueBox.OfNumber(1));
Check(Tech.LevelOf(techVars, 1, "UpgradeStim") == 1 &&
      Tech.LevelOf(techVars, 0, "UpgradeStim") == 0,
    "`Player2 UpgradeStim` is player 2's level alone");
Tech.SetLevel(techVars, 0, "UpgradeStim", 3);
Check(Tech.LevelOf(techVars, 0, "UpgradeStim") == 3 &&
      Tech.LevelOf(techVars, 1, "UpgradeStim") == 1,
    "SetLevel writes that player's global and leaves the other alone");
techVars.SetGlobal("Player1 UpgradeStim", ValueBox.OfNumber(-2));
Check(Tech.LevelOf(techVars, 0, "UpgradeStim") == 0,
    "a negative level reads as 0 — research only goes up");

// The parser joins `Player2 UpgradeStim` (and the run-together spelling) into one global.
var techProgram = parser.Parse(@"when game starts
  set Player2 UpgradeStim to 1
  set Player1 PistolDamage to 4
  set Player2 PistolDamageBonusMultiplier to 1.5
  set Player1UpgradeArmor to 2
  set Player1 Score to 5");
CheckStrings("Player2 UpgradeStim",
    ((SetNode)techProgram.Events[0].Body[0].Statement).Target.Name,
    "`set Player2 UpgradeStim to 1` parses as one global");
CheckStrings("Player1 PistolDamage",
    ((SetNode)techProgram.Events[0].Body[1].Statement).Target.Name,
    "`set Player1 PistolDamage to 4` parses as one global");
CheckStrings("Player2 PistolDamageBonusMultiplier",
    ((SetNode)techProgram.Events[0].Body[2].Statement).Target.Name,
    "the per-player multiplier global parses too");
CheckStrings("Player1 UpgradeArmor",
    ((SetNode)techProgram.Events[0].Body[3].Statement).Target.Name,
    "the run-together `Player1UpgradeArmor` reads as the same variable");
CheckStrings("Player1 Score",
    ((SetNode)techProgram.Events[0].Body[4].Statement).Target.Name,
    "`Player1 Score` keeps the Players spelling");

// A condition reads the level research raised (conditions gate the event, so the level is
// already there when the sheet runs).
var techCondEngine = FreshEngine(new FakeWorld());
techCondEngine.Variables.SetGlobal("Player2 UpgradeStim", ValueBox.OfNumber(1));
techCondEngine.AddSheet(new GlobalHost(), parser.Parse(@"when game starts
  if Player2 UpgradeStim is 1
  set Seen to Seen + 1"));
techCondEngine.SessionStarted();
techCondEngine.Tick(0);
Check(techCondEngine.Variables.GetGlobal("Seen").AsNumber() == 1,
    "`if Player2 UpgradeStim is 1` reads the per-player level");

// Weapons: base × multiplier + bonus, per player.
var techWeaponVars = new EventVariables();
Check(WeaponData.Effective(techWeaponVars, 0, "Pistol", "Damage").AsNumber() == 10,
    "an un-upgraded pistol keeps its base damage");
techWeaponVars.SetGlobal("Player1 PistolDamage", ValueBox.OfNumber(4));
Check(WeaponData.Effective(techWeaponVars, 0, "Pistol", "Damage").AsNumber() == 14,
    "`Player1 PistolDamage` is a bonus on that player's damage");
Check(WeaponData.Effective(techWeaponVars, 1, "Pistol", "Damage").AsNumber() == 10,
    "player 1's bonus never buffs player 2's guns");
techWeaponVars.SetGlobal("Player2 PistolDamageBonusMultiplier", ValueBox.OfNumber(1.5));
techWeaponVars.SetGlobal("Player2 PistolDamage", ValueBox.OfNumber(2));
Check(WeaponData.Effective(techWeaponVars, 1, "Pistol", "Damage").AsNumber() == 17,
    "base × multiplier + bonus (10 × 1.5 + 2)");
Check(WeaponData.Effective(techWeaponVars, 1, "Pistol", "FireRate").AsNumber() == 0.25,
    "a field with no player value keeps its base");
techWeaponVars.SetGlobal("Player2 PistolBullet", ValueBox.OfText("Rocket"));
CheckStrings("Rocket", WeaponData.Effective(techWeaponVars, 1, "Pistol", "Bullet").AsText(),
    "a text field takes a per-player override");
CheckStrings("Bullet", WeaponData.Effective(techWeaponVars, 0, "Pistol", "Bullet").AsText(),
    "the other player keeps the shared bullet type");
WeaponData.TryGet(techWeaponVars, "Pistol", out WeaponStats plainPistol);
Check(plainPistol != null && plainPistol.Damage == 10 &&
      WeaponData.Effective(techWeaponVars, 0, "Pistol", "Damage").AsNumber() == 14,
    "WeaponData.events keeps the base numbers, the player values sit on top");

// The acting unit's player comes from the fog, falling back to `my Player`.
var techWorld = new FakeWorld();
var techMarine = new FakeActor("Marine");
techWorld.AddActor(techMarine);
var techEngine = FreshEngine(techWorld);
techEngine.Variables.SetLocal(techMarine, "Weapon", ValueBox.OfText("Pistol"));
techEngine.Fog.SetActor(techMarine, 1, 0, 0, 100);
techEngine.Variables.SetGlobal("Player2 PistolDamage", ValueBox.OfNumber(5));
var techContext = new EventContext
{
    Me = techMarine,
    Variables = techEngine.Variables,
    Runtime = techEngine,
};
Check(Tech.PlayerIndexOf(techContext) == 1,
    "the fog's player is the acting unit's player");
var scopedPistol = WeaponData.ForActor(techContext);
Check(scopedPistol != null && scopedPistol.Damage == 15,
    "`spawn Bullet` stamps the shooter's own damage (10 + 5)");

var techFallback = new FakeActor("Gunner");
techEngine.Variables.SetLocal(techFallback, "Player", ValueBox.OfNumber(1));
techEngine.Variables.SetLocal(techFallback, "Weapon", ValueBox.OfText("Pistol"));
var techFallbackContext = new EventContext
{
    Me = techFallback,
    Variables = techEngine.Variables,
    Runtime = techEngine,
};
Check(Tech.PlayerIndexOf(techFallbackContext) == 1 &&
      WeaponData.ForActor(techFallbackContext).Damage == 15,
    "a unit the fog does not know falls back to `my Player`");
var techLocal = new FakeActor("Worker");
techEngine.Variables.SetLocal(techLocal, "Weapon", ValueBox.OfText("Pistol"));
var techLocalContext = new EventContext
{
    Me = techLocal,
    Variables = techEngine.Variables,
    Runtime = techEngine,
};
Check(Tech.PlayerIndexOf(techLocalContext) == 0 &&
      WeaponData.ForActor(techLocalContext).Damage == 10,
    "a unit with no player is player 1 and keeps the base damage");

// ------------------------------------------------------------------
// Task 27 Part C — buffs and debuffs
// ------------------------------------------------------------------

BuffInstance NewBuff(string name, double duration, BuffRule rule, string stat, double amount,
    double multiplier = 1, bool multiply = false)
{
    var instance = new BuffInstance { Name = name, Duration = duration, Rule = rule };
    var modifier = new BuffModifier { Stat = stat };
    if (multiply)
    {
        modifier.Multiplier = multiplier;
        modifier.HasMultiplier = true;
    }
    else
    {
        modifier.Amount = amount;
        modifier.HasAmount = true;
    }
    instance.Modifiers.Add(modifier);
    return instance;
}

var buffVars = new EventVariables();
var buffMarine = new FakeActor("Marine");
buffVars.SetLocal(buffMarine, "Speed", ValueBox.OfNumber(100));
var buffs = new Buffs();
Check(buffs.Add(buffVars, buffMarine, NewBuff("stim", 8, BuffRule.Refresh, "Speed", 40), out bool stimStarted) &&
      stimStarted,
    "a buff starts and reports the start");
Check(buffVars.GetLocal(buffMarine, "Speed").AsNumber() == 140 &&
      buffVars.GetLocal(buffMarine, "BaseSpeed").AsNumber() == 100,
    "the buff writes the aggregate and records the base (100 + 40)");
Check(Math.Abs(buffs.TimeLeft(buffMarine, "stim") - 8) < 0.0001,
    "`my \"stim\" time left` reads the full duration");

// Refresh: a second press restarts the timer without doubling the bonus.
Check(buffs.Add(buffVars, buffMarine, NewBuff("stim", 8, BuffRule.Refresh, "Speed", 40), out bool refreshed) &&
      !refreshed,
    "a refreshed buff runs on but reports no new start");
var stimEnds = buffs.Tick(buffVars, 3);
Check(stimEnds.Count == 0 && Math.Abs(buffs.TimeLeft(buffMarine, "stim") - 5) < 0.0001 &&
      buffVars.GetLocal(buffMarine, "Speed").AsNumber() == 140,
    "refresh keeps one copy: the timer counts down and the bonus is not doubled");

// Stack: two copies add up.
var stackBuffs = new Buffs();
var stackVars = new EventVariables();
var stackMarine = new FakeActor("Stacker");
stackVars.SetLocal(stackMarine, "Speed", ValueBox.OfNumber(100));
stackBuffs.Add(stackVars, stackMarine, NewBuff("stim", 8, BuffRule.Stack, "Speed", 40), out _);
stackBuffs.Add(stackVars, stackMarine, NewBuff("stim", 8, BuffRule.Stack, "Speed", 40), out bool stackedStarted);
Check(stackedStarted && stackBuffs.CountOf(stackMarine, "stim") == 2 &&
      stackVars.GetLocal(stackMarine, "Speed").AsNumber() == 180,
    "`stack` runs a second copy and the bonuses add up");

// Ignore: while one runs, the second is refused.
var ignoreBuffs = new Buffs();
var ignoreVars = new EventVariables();
var ignoreMarine = new FakeActor("Ignorer");
ignoreVars.SetLocal(ignoreMarine, "Armor", ValueBox.OfNumber(1));
ignoreBuffs.Add(ignoreVars, ignoreMarine, NewBuff("shield", 8, BuffRule.Ignore, "Armor", 3), out _);
Check(!ignoreBuffs.Add(ignoreVars, ignoreMarine, NewBuff("shield", 8, BuffRule.Ignore, "Armor", 3), out _) &&
      ignoreVars.GetLocal(ignoreMarine, "Armor").AsNumber() == 4,
    "`ignore` refuses a second copy while the first runs");

// Overlapping buffs on one stat: base × multipliers + amounts.
var mixBuffs = new Buffs();
var mixVars = new EventVariables();
var mixMarine = new FakeActor("Mixer");
mixVars.SetLocal(mixMarine, "Speed", ValueBox.OfNumber(100));
mixBuffs.Add(mixVars, mixMarine, NewBuff("adrenaline", 8, BuffRule.Refresh, "Speed", 0, 1.5, true), out _);
mixBuffs.Add(mixVars, mixMarine, NewBuff("haste", 8, BuffRule.Refresh, "Speed", 40), out _);
Check(mixVars.GetLocal(mixMarine, "Speed").AsNumber() == 190,
    "overlapping buffs combine as base × multipliers + amounts (100 × 1.5 + 40)");
Check(mixBuffs.Remove(mixVars, mixMarine, "adrenaline") &&
      Math.Abs(mixVars.GetLocal(mixMarine, "Speed").AsNumber() - 140) < 0.0001,
    "ending one buff leaves the other's bonus in place");
Check(!mixBuffs.Remove(mixVars, mixMarine, "nothing"),
    "removing a buff that is not running answers false");

// Expiry restores every stat and drops the Base<Stat> helper.
var expiryBuffs = new Buffs();
var expiryVars = new EventVariables();
var expiryMarine = new FakeActor("Expirer");
expiryVars.SetLocal(expiryMarine, "Speed", ValueBox.OfNumber(100));
expiryVars.SetLocal(expiryMarine, "MaxHealth", ValueBox.OfNumber(80));
expiryBuffs.Add(expiryVars, expiryMarine, NewBuff("stim", 0.5, BuffRule.Refresh, "Speed", 40), out _);
expiryBuffs.Add(expiryVars, expiryMarine, NewBuff("fortify", 0.5, BuffRule.Refresh, "Max Health", 20), out _);
Check(expiryVars.GetLocal(expiryMarine, "MaxHealth").AsNumber() == 100,
    "a two-word health stat is buffed through its canonical spelling");
var expiryEnds = expiryBuffs.Tick(expiryVars, 0.6);
Check(expiryEnds.Count == 2 && expiryVars.GetLocal(expiryMarine, "Speed").AsNumber() == 100 &&
      expiryVars.GetLocal(expiryMarine, "MaxHealth").AsNumber() == 80,
    "expiry restores every buffed stat and reports each end");
Check(!expiryVars.HasLocal(expiryMarine, "BaseSpeed") &&
      !expiryVars.HasLocal(expiryMarine, "BaseMaxHealth"),
    "the Base<Stat> helper goes away with the buff");
Check(expiryBuffs.Tick(expiryVars, 1).Count == 0 && expiryBuffs.ActiveNames(expiryMarine).Count == 0,
    "an expired buff is not reported twice");

// A sheet's own BaseSpeed comes back, and Clear restores everything.
var savedVars = new EventVariables();
var savedActor = new FakeActor("Saver");
savedVars.SetLocal(savedActor, "Speed", ValueBox.OfNumber(100));
savedVars.SetLocal(savedActor, "BaseSpeed", ValueBox.OfNumber(90));
var savedBuffs = new Buffs();
savedBuffs.Add(savedVars, savedActor, NewBuff("stim", 1, BuffRule.Refresh, "Speed", 40), out _);
Check(savedVars.GetLocal(savedActor, "BaseSpeed").AsNumber() == 100,
    "the buff records the pre-buff value as BaseSpeed");
savedBuffs.Tick(savedVars, 1.5);
Check(savedVars.GetLocal(savedActor, "BaseSpeed").AsNumber() == 90,
    "a sheet's own BaseSpeed is restored when the buff ends");

var clearVars = new EventVariables();
var clearActor = new FakeActor("Cleared");
clearVars.SetLocal(clearActor, "Speed", ValueBox.OfNumber(100));
var clearBuffs = new Buffs();
clearBuffs.Add(clearVars, clearActor, NewBuff("stim", 8, BuffRule.Refresh, "Speed", 40), out _);
clearBuffs.Clear(clearVars, clearActor);
Check(clearVars.GetLocal(clearActor, "Speed").AsNumber() == 100 && !clearBuffs.IsActive(clearActor, "stim"),
    "Clear ends an actor's buffs and puts its stats back");
// A destroyed actor's buffs must stop existing too: nothing left to tick, nothing left to
// rewrite, and no `when buff ... ends` fired for an actor that is already gone.
clearVars.SetLocal(clearActor, "Speed", ValueBox.OfNumber(7));
Check(clearBuffs.Tick(clearVars, 30).Count == 0 &&
      clearBuffs.ActiveActors().Count == 0 &&
      clearVars.GetLocal(clearActor, "Speed").AsNumber() == 7,
    "after Clear the manager never ticks or rewrites that actor again");

// A sheet that never buffs keeps writing `my Speed` by hand.
var plainVars = new EventVariables();
var plainActor = new FakeActor("Walker");
plainVars.SetLocal(plainActor, "Speed", ValueBox.OfNumber(55));
new Buffs().Tick(plainVars, 1);
Check(plainVars.GetLocal(plainActor, "Speed").AsNumber() == 55 &&
      !plainVars.HasLocal(plainActor, "BaseSpeed"),
    "a sheet that never adds a buff is untouched");

// The words: add / remove, defaults, rules and modifiers.
var addBuffProgram = parser.Parse("when Q is pressed\n  add buff \"stim\" to me for 8 seconds adding 40 to my Speed\n");
var addBuffSpec = ActionWords.Parse(
    ((RawActionNode)addBuffProgram.Events[0].Body[0].Statement!).Words, _ => { });
Check(addBuffSpec.Verb == ActionVerb.AddBuff && addBuffSpec.BuffName == "stim" &&
      addBuffSpec.To == "me" && Math.Abs(addBuffSpec.Duration - 8) < 1e-9 &&
      addBuffSpec.BuffModifiers.Count == 1 && addBuffSpec.BuffModifiers[0].Stat == "Speed",
    "`add buff \"stim\" to me for 8 seconds adding 40 to my Speed` parses");

var multiplyBuffProgram = parser.Parse(
    "when Q is pressed\n  add buff \"stim\" for 6 seconds stack multiplying my Speed by 1.5\n");
var multiplyBuffSpec = ActionWords.Parse(
    ((RawActionNode)multiplyBuffProgram.Events[0].Body[0].Statement!).Words, _ => { });
Check(multiplyBuffSpec.Verb == ActionVerb.AddBuff && multiplyBuffSpec.To == "me" &&
      multiplyBuffSpec.BuffRule == BuffRule.Stack &&
      multiplyBuffSpec.BuffModifiers.Count == 1 && multiplyBuffSpec.BuffModifiers[0].Multiply &&
      Math.Abs(multiplyBuffSpec.BuffModifiers[0].Number - 1.5) < 1e-9,
    "`add buff ... for 6 seconds stack multiplying my Speed by 1.5` parses");

var chainedBuffProgram = parser.Parse(
    "when Q is pressed\n  add buff \"stim\" to me for 8 seconds adding 40 to my Speed and multiplying my Speed by 1.5\n");
var chainedBuffSpec = ActionWords.Parse(
    ((RawActionNode)chainedBuffProgram.Events[0].Body[0].Statement!).Words, _ => { });
Check(chainedBuffSpec.BuffModifiers.Count == 2 &&
      !chainedBuffSpec.BuffModifiers[0].Multiply && chainedBuffSpec.BuffModifiers[1].Multiply,
    "modifier clauses chain with `and`");

var removeBuffProgram = parser.Parse("when Q is pressed\n  remove buff \"stim\" from other\n");
var removeBuffSpec = ActionWords.Parse(
    ((RawActionNode)removeBuffProgram.Events[0].Body[0].Statement!).Words, _ => { });
Check(removeBuffSpec.Verb == ActionVerb.RemoveBuff && removeBuffSpec.BuffName == "stim" &&
      removeBuffSpec.To == "other",
    "`remove buff \"stim\" from other` parses");

string shortenHint = "";
Action<string> GrabShortHint = message => shortenHint = message;
var bareBuffProgram = parser.Parse("when Q is pressed\n  add buff \"stim\" to me\n");
var bareBuffSpec = ActionWords.Parse(
    ((RawActionNode)bareBuffProgram.Events[0].Body[0].Statement!).Words, GrabShortHint);
Check(bareBuffSpec.Verb == ActionVerb.Unknown && shortenHint.Contains("duration"),
    "`add buff` without a duration explains the shape");

// Triggers and the value form.
var buffStartProgram = parser.Parse("when buff \"stim\" starts\n  set Started to 1\n");
var buffStartDesc = TriggerClassifier.Classify(buffStartProgram.Events[0]);
Check(buffStartDesc.Kind == TriggerKind.BuffStarts && buffStartDesc.RequireName == "stim",
    "`when buff \"stim\" starts` classifies to BuffStarts with the name");
var buffEndProgram = parser.Parse("when buff \"stim\" ends\n  set Done to 1\n");
var buffEndDesc = TriggerClassifier.Classify(buffEndProgram.Events[0]);
Check(buffEndDesc.Kind == TriggerKind.BuffEnds && buffEndDesc.RequireName == "stim",
    "`when buff \"stim\" ends` classifies to BuffEnds");

var timeLeftProgram = parser.Parse("when game starts\n  set Left to my \"stim\" time left\n");
var timeLeftSet = (SetNode)timeLeftProgram.Events[0].Body[0].Statement!;
Check(timeLeftSet.Value is VariableValue timeLeftValue && timeLeftValue.Owner == "my" &&
      timeLeftValue.Name == "buff stim time left",
    "`my \"stim\" time left` reads as one variable");

// End to end: the word buffs a named actor, and both pushes land on its own sheet.
var buffWorld = new FakeWorld();
var buffCaster = new FakeActor("Medic");
var buffedMarine = new FakeActor("Marine");
buffWorld.AddActor(buffCaster);
buffWorld.AddActor(buffedMarine);
var buffEngine = FreshEngine(buffWorld);
buffEngine.AddSheet(buffedMarine, parser.Parse(@"when buff ""stim"" starts
  set my Started to 1
  set my StarterTag to other's Tag
when buff ""stim"" ends
  set my Ended to 1"));
buffEngine.Variables.SetLocal(buffedMarine, "Speed", ValueBox.OfNumber(100));
buffEngine.Variables.SetLocal(buffCaster, "Tag", ValueBox.OfText("Medic"));
var buffContext = new EventContext
{
    Me = buffCaster,
    Variables = buffEngine.Variables,
    Runtime = buffEngine,
};
var stimSpec = new ActionSpec { BuffName = "stim", To = "Marine", Duration = 1 };
stimSpec.BuffModifiers.Add(new BuffModifierWord { Stat = "Speed", Number = 40 });
Check(BuffCommands.ExecuteAdd(buffContext, stimSpec) &&
      buffEngine.Variables.GetLocal(buffedMarine, "Speed").AsNumber() == 140,
    "`add buff \"stim\" to Marine` buffs the named actor");
buffEngine.Tick(0);
Check(buffEngine.Variables.GetLocal(buffedMarine, "Started").AsNumber() == 1 &&
      buffEngine.Variables.GetLocal(buffedMarine, "StarterTag").AsText() == "Medic",
    "`when buff \"stim\" starts` runs on the buffed actor with the source as `other`");
buffEngine.Tick(1);
Check(buffEngine.Variables.GetLocal(buffedMarine, "Ended").AsNumber() == 1 &&
      buffEngine.Variables.GetLocal(buffedMarine, "Speed").AsNumber() == 100,
    "`when buff \"stim\" ends` runs when the timer runs out and the stat is restored");

Check(WordRegistry.All.Any(e => e.Kind == "action" && e.Word == "add buff"),
    "the word book lists `add buff`");
Check(WordRegistry.All.Any(e => e.Kind == "trigger" && e.Word.Contains("when buff")),
    "the word book lists the buff triggers");
Check(WordRegistry.All.Any(e => e.Kind == "value" && e.Word.Contains("time left")),
    "the word book lists the buff time-left value");

// ------------------------------------------------------------------
// Task 27 Part D: research queues and the Upgrades.events table
// ------------------------------------------------------------------

// Parser shapes: a quoted upgrade name, the acting-building form and both cancels.
var rdParse = parser.Parse("when game starts\n  research \"Stim Pack\" at Barracks\n  research \"Psi Storm\"\n  research StimPack\n  cancel research \"Stim Pack\" at Barracks\n  cancel research \"Psi Storm\"\n");
var rdAtSpec = ActionWords.Parse(((RawActionNode)rdParse.Events[0].Body[0].Statement!).Words, _ => { });
Check(rdAtSpec.Verb == ActionVerb.Research && rdAtSpec.Thing == "Stim Pack" && rdAtSpec.To == "Barracks",
    "`research \"Stim Pack\" at Barracks` parses to Research with the upgrade and building");
var rdOwnSpec = ActionWords.Parse(((RawActionNode)rdParse.Events[0].Body[1].Statement!).Words, _ => { });
Check(rdOwnSpec.Verb == ActionVerb.Research && rdOwnSpec.Thing == "Psi Storm" && rdOwnSpec.To.Length == 0,
    "`research \"Psi Storm\"` queues on the acting building");
var rdBareSpec = ActionWords.Parse(((RawActionNode)rdParse.Events[0].Body[2].Statement!).Words, _ => { });
Check(rdBareSpec.Verb == ActionVerb.Research && rdBareSpec.Thing == "StimPack",
    "an unquoted one-word upgrade name also parses");
var rdCancelSpec = ActionWords.Parse(((RawActionNode)rdParse.Events[0].Body[3].Statement!).Words, _ => { });
Check(rdCancelSpec.Verb == ActionVerb.CancelResearch && rdCancelSpec.Thing == "Stim Pack" &&
      rdCancelSpec.To == "Barracks",
    "`cancel research \"Stim Pack\" at Barracks` parses to CancelResearch");
var rdCancelOwnSpec = ActionWords.Parse(((RawActionNode)rdParse.Events[0].Body[4].Statement!).Words, _ => { });
Check(rdCancelOwnSpec.Verb == ActionVerb.CancelResearch && rdCancelOwnSpec.To.Length == 0,
    "`cancel research \"Psi Storm\"` keeps the acting building");

// Triggers: both researched forms, the bare edges and the queue edges.
var rdFinish = TriggerClassifier.Classify(parser.Parse("when Barracks finishes researching \"Stim Pack\"\n  set Done to 1\n").Events[0]);
Check(rdFinish.Kind == TriggerKind.ResearchFinished && rdFinish.RequireName == "Barracks" &&
      rdFinish.RequireUnit == "Stim Pack",
    "`when Barracks finishes researching \"Stim Pack\"` carries the building and upgrade");
var rdStart = TriggerClassifier.Classify(parser.Parse("when Barracks starts researching \"Stim Pack\"\n  set Done to 1\n").Events[0]);
Check(rdStart.Kind == TriggerKind.ResearchStarted && rdStart.RequireName == "Barracks" &&
      rdStart.RequireUnit == "Stim Pack",
    "`when Barracks starts researching \"Stim Pack\"` carries both names too");
Check(TriggerClassifier.Classify(parser.Parse("when research starts\n  set Done to 1\n").Events[0]).Kind == TriggerKind.ResearchStarted,
    "`when research starts` is the bare start edge");
Check(TriggerClassifier.Classify(parser.Parse("when research finishes\n  set Done to 1\n").Events[0]).Kind == TriggerKind.ResearchFinished,
    "`when research finishes` is the bare finish edge");
var rdBareEmpty = TriggerClassifier.Classify(parser.Parse("when research queue is empty\n  set Done to 1\n").Events[0]);
Check(rdBareEmpty.Kind == TriggerKind.ResearchQueueEmpty && rdBareEmpty.RequireName.Length == 0,
    "`when research queue is empty` listens to every building");
var rdNamedEmpty = TriggerClassifier.Classify(parser.Parse("when Barracks research queue is empty\n  set Done to 1\n").Events[0]);
Check(rdNamedEmpty.Kind == TriggerKind.ResearchQueueEmpty && rdNamedEmpty.RequireName == "Barracks",
    "`when Barracks research queue is empty` filters by building");

// Upgrades.events: rows are read from literals, suffix by suffix.
Upgrades.Reset();
Upgrades.LoadData(parser.Parse("when game starts\n  set StimPackTime to 30\n  set StimPackCost to 100\n  set StimPackRequires to \"Barracks\"\n  set StimPackLevels to 2\n  set ArmorTime to 5\n  set Unrelated to 1\n"));
bool rdFound = Upgrades.TryGet("Stim Pack", out UpgradeData rdRow);
Check(rdFound && Math.Abs((rdRow.Time ?? -1) - 30) < 1e-9 &&
      Math.Abs((rdRow.Cost ?? -1) - 100) < 1e-9 && rdRow.Requires == "Barracks" &&
      Math.Abs(rdRow.MaxLevel - 2) < 1e-9,
    "Upgrades.events declares time, cost, required building and level cap for one upgrade");
Check(Upgrades.KeyOf("Stim Pack") == "StimPack" && Upgrades.KeyOf("StimPack") == "StimPack",
    "the spaced and run-together spellings reach the same upgrade row");
Check(!Upgrades.TryGet("Unrelated", out _), "a global with no upgrade suffix is ignored");
Check(Math.Abs(Upgrades.MaxLevelOf("Psi Storm") - 1) < 1e-9,
    "an upgrade with no row defaults to a single level");
Upgrades.Reset();
Check(!Upgrades.TryGet("Stim Pack", out _), "a reload with no file forgets every upgrade");

// Duration precedence: the live global, then the file, then the building, then the shared one.
var rdVars = new EventVariables();
Check(Math.Abs(Upgrades.DurationOf(rdVars, "StimPack", "Barracks") - Upgrades.DefaultResearchTime) < 1e-9,
    "research with no data takes the default time");
rdVars.SetGlobal("ResearchTime", ValueBox.OfNumber(12));
Check(Math.Abs(Upgrades.DurationOf(rdVars, "StimPack", "Barracks") - 12) < 1e-9,
    "`ResearchTime` overrides the default");
rdVars.SetGlobal("BarracksResearchTime", ValueBox.OfNumber(7));
Check(Math.Abs(Upgrades.DurationOf(rdVars, "StimPack", "Barracks") - 7) < 1e-9,
    "`BarracksResearchTime` overrides the shared global");
rdVars.SetGlobal("StimPackTime", ValueBox.OfNumber(30));
Check(Math.Abs(Upgrades.DurationOf(rdVars, "StimPack", "Barracks") - 30) < 1e-9,
    "`StimPackTime` overrides everything, so a sheet can retune one upgrade");

// The queue: charge up front, run, then raise the finish and apply the level.
var rdEngine = FreshEngine(new FakeWorld());
rdEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(250));
rdEngine.Variables.SetGlobal("StimPackCost", ValueBox.OfNumber(100));
rdEngine.Variables.SetGlobal("StimPackTime", ValueBox.OfNumber(2));
var rdBuilding = new FakeActor("Barracks");
rdEngine.AddSheet(rdBuilding, parser.Parse(@"when me starts researching ""Stim Pack""
  set my Started to 1
when me finishes researching ""Stim Pack""
  set my Finished to 1
when my research queue is empty
  set my Emptied to 1"));
rdEngine.AddSheet(new GlobalHost(), parser.Parse(@"when research finishes
  set AnyFinished to 1
when Barracks research queue is empty
  set NamedEmpty to 1"));
Check(rdEngine.Research.Enqueue(rdEngine.Variables, rdEngine, rdBuilding, "Barracks", "Stim Pack", 0) == ResearchStartResult.Started,
    "`research \"Stim Pack\" at Barracks` starts a queue");
Check(rdEngine.Variables.GetGlobal("Minerals").AsNumber() == 150,
    "the cost is charged when the order starts, not when it finishes");
Check(rdEngine.Research.QueueCount(rdBuilding) == 1 && rdEngine.Research.CurrentUpgrade(rdBuilding) == "Stim Pack",
    "the queue reports the upgrade it is working on");
rdEngine.Tick(0);
Check(rdEngine.Variables.GetLocal(rdBuilding, "Started").AsNumber() == 1,
    "`when me starts researching` fires on the building's own sheet");
rdEngine.Tick(1);
Check(Math.Abs(rdEngine.Research.ProgressOf(rdBuilding) - 50) < 1e-9 &&
      Math.Abs(rdEngine.Research.Remaining(rdBuilding) - 1) < 1e-9,
    "half the research time reads as 50% progress and 1 second left");
Check(Math.Abs(rdEngine.Variables.GetLocal(rdBuilding, "ResearchProgress").AsNumber() - 50) < 1e-9 &&
      rdEngine.Variables.GetLocal(rdBuilding, "ResearchName").AsText() == "Stim Pack",
    "the building carries `my ResearchProgress` and `my ResearchName` for a progress bar");
rdEngine.Tick(2);
Check(rdEngine.Variables.GetLocal(rdBuilding, "Finished").AsNumber() == 1 &&
      rdEngine.Variables.GetGlobal("AnyFinished").AsNumber() == 1,
    "`when me finishes researching` and the bare `when research finishes` both fire");
Check(rdEngine.Variables.GetLocal(rdBuilding, "Emptied").AsNumber() == 1 &&
      rdEngine.Variables.GetGlobal("NamedEmpty").AsNumber() == 1,
    "`when research queue is empty` fires bare and by building name");
Check(Math.Abs(rdEngine.Variables.GetGlobal("Player1 StimPack").AsNumber() - 1) < 1e-9,
    "finishing raises the owner's `Player1 StimPack` level");
Check(rdEngine.Variables.GetLocal(rdBuilding, "ResearchProgress").AsNumber() == 0 &&
      rdEngine.Research.IsIdle(rdBuilding),
    "a drained queue reads idle and resets its progress");

// Cancelling refunds exactly what was paid; a missing entry is a quiet false.
var rdCancelEngine = FreshEngine(new FakeWorld());
rdCancelEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(300));
rdCancelEngine.Variables.SetGlobal("StimPackCost", ValueBox.OfNumber(100));
var rdCancelBuilding = new FakeActor("Barracks");
rdCancelEngine.Research.Enqueue(rdCancelEngine.Variables, rdCancelEngine, rdCancelBuilding, "Barracks", "Stim Pack", 0);
Check(rdCancelEngine.Variables.GetGlobal("Minerals").AsNumber() == 200, "starting research spends 100");
Check(rdCancelEngine.Research.Cancel(rdCancelEngine.Variables, rdCancelEngine, rdCancelBuilding, "Stim Pack") &&
      rdCancelEngine.Variables.GetGlobal("Minerals").AsNumber() == 300,
    "cancelling refunds the full price");
Check(!rdCancelEngine.Research.Cancel(rdCancelEngine.Variables, rdCancelEngine, rdCancelBuilding, "Stim Pack"),
    "cancelling something that is not queued returns false");

// The refund factor is data: 0.5 refunds half.
var rdHalfEngine = FreshEngine(new FakeWorld());
rdHalfEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(300));
rdHalfEngine.Variables.SetGlobal("StimPackCost", ValueBox.OfNumber(100));
rdHalfEngine.Variables.SetGlobal("ResearchRefund", ValueBox.OfNumber(0.5));
var rdHalfBuilding = new FakeActor("Barracks");
rdHalfEngine.Research.Enqueue(rdHalfEngine.Variables, rdHalfEngine, rdHalfBuilding, "Barracks", "Stim Pack", 0);
rdHalfEngine.Research.Cancel(rdHalfEngine.Variables, rdHalfEngine, rdHalfBuilding, "Stim Pack");
Check(rdHalfEngine.Variables.GetGlobal("Minerals").AsNumber() == 250,
    "`ResearchRefund` 0.5 gives back half the price");

// Per-player banks: player 2 pays its own minerals and gets its own level.
var rdBankEngine = FreshEngine(new FakeWorld());
rdBankEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(500));
rdBankEngine.Variables.SetGlobal("Player2 Minerals", ValueBox.OfNumber(200));
rdBankEngine.Variables.SetGlobal("StimPackCost", ValueBox.OfNumber(100));
rdBankEngine.Variables.SetGlobal("StimPackTime", ValueBox.OfNumber(1));
var rdBankBuilding = new FakeActor("Barracks");
rdBankEngine.Research.Enqueue(rdBankEngine.Variables, rdBankEngine, rdBankBuilding, "Barracks", "Stim Pack", 1);
Check(rdBankEngine.Variables.GetGlobal("Player2 Minerals").AsNumber() == 100 &&
      rdBankEngine.Variables.GetGlobal("Minerals").AsNumber() == 500,
    "a player-2 building spends `Player2 Minerals`, never the player-1 stockpile");
rdBankEngine.Tick(0);
rdBankEngine.Tick(1);
Check(Math.Abs(rdBankEngine.Variables.GetGlobal("Player2 StimPack").AsNumber() - 1) < 1e-9 &&
      !rdBankEngine.Variables.HasGlobal("Player1 StimPack"),
    "the finished upgrade lands on player 2's tech only");

// Refusals: unaffordable, already queued, capped, and the wrong building.
var rdRefuseVars = new EventVariables();
rdRefuseVars.SetGlobal("Minerals", ValueBox.OfNumber(50));
rdRefuseVars.SetGlobal("StimPackCost", ValueBox.OfNumber(100));
var rdRefuseBuilding = new FakeActor("Barracks");
var rdRefuseManager = new ResearchManager();
Check(rdRefuseManager.Enqueue(rdRefuseVars, null, rdRefuseBuilding, "Barracks", "Stim Pack", 0) == ResearchStartResult.CannotAfford &&
      rdRefuseVars.GetGlobal("Minerals").AsNumber() == 50 &&
      rdRefuseManager.QueueCount(rdRefuseBuilding) == 0,
    "an unaffordable upgrade is refused without spending anything");
rdRefuseVars.SetGlobal("Minerals", ValueBox.OfNumber(500));
Check(rdRefuseManager.Enqueue(rdRefuseVars, null, rdRefuseBuilding, "Barracks", "Stim Pack", 0) == ResearchStartResult.Started &&
      rdRefuseManager.Enqueue(rdRefuseVars, null, rdRefuseBuilding, "Barracks", "Stim Pack", 0) == ResearchStartResult.AlreadyQueued,
    "the same upgrade cannot be queued twice at one building");
var rdCapVars = new EventVariables();
Tech.SetLevel(rdCapVars, 0, "StimPack", 1);
Check(new ResearchManager().Enqueue(rdCapVars, null, new FakeActor("Barracks"), "Barracks", "Stim Pack", 0) == ResearchStartResult.AtMaxLevel,
    "an upgrade already at its level cap is refused");
Upgrades.LoadData(parser.Parse("when game starts\n  set StimPackRequires to \"Barracks\"\n"));
var rdWrongManager = new ResearchManager();
Check(rdWrongManager.Enqueue(new EventVariables(), null, new FakeActor("Factory"), "Factory", "Stim Pack", 0) == ResearchStartResult.WrongBuilding,
    "`StimPackRequires` refuses research at a different building");
Upgrades.Reset();

// A second order starts when the first is cancelled or finishes, and a destroyed building drops
// its queue without a refund.
var rdQueueEngine = FreshEngine(new FakeWorld());
rdQueueEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(500));
rdQueueEngine.Variables.SetGlobal("StimPackCost", ValueBox.OfNumber(50));
rdQueueEngine.Variables.SetGlobal("ArmorCost", ValueBox.OfNumber(50));
var rdQueueBuilding = new FakeActor("Barracks");
rdQueueEngine.Research.Enqueue(rdQueueEngine.Variables, rdQueueEngine, rdQueueBuilding, "Barracks", "Stim Pack", 0);
rdQueueEngine.Research.Enqueue(rdQueueEngine.Variables, rdQueueEngine, rdQueueBuilding, "Barracks", "Armor", 0);
Check(rdQueueEngine.Research.QueueCount(rdQueueBuilding) == 2 &&
      rdQueueEngine.Research.CurrentUpgrade(rdQueueBuilding) == "Stim Pack",
    "the second upgrade waits behind the first");
rdQueueEngine.Research.Cancel(rdQueueEngine.Variables, rdQueueEngine, rdQueueBuilding, "Stim Pack");
Check(rdQueueEngine.Research.CurrentUpgrade(rdQueueBuilding) == "Armor" &&
      rdQueueEngine.Variables.GetGlobal("Minerals").AsNumber() == 450,
    "cancelling the active order promotes the next one and refunds only the cancelled price");
rdQueueEngine.Research.Destroyed(rdQueueBuilding);
Check(!rdQueueEngine.Research.HasQueue(rdQueueBuilding) && rdQueueEngine.Research.IsIdle(rdQueueBuilding),
    "a destroyed building drops its queue");

// The default: a manager nobody used never writes anything.
var rdUntouchedVars = new EventVariables();
var rdUntouchedBuilding = new FakeActor("Barracks");
var rdUntouched = new ResearchManager();
Check(rdUntouched.IsIdle(rdUntouchedBuilding) && rdUntouched.ProgressOf(rdUntouchedBuilding) == 0 &&
      rdUntouched.Remaining(rdUntouchedBuilding) == 0 &&
      !rdUntouchedVars.HasLocal(rdUntouchedBuilding, ResearchManager.ProgressVariable),
    "a scene that never researches is untouched");

Check(WordRegistry.All.Any(e => e.Kind == "action" && e.Word == "research") &&
      WordRegistry.All.Any(e => e.Kind == "action" && e.Example.StartsWith("cancel research")),
    "the word book lists `research` and `cancel research`");
Check(WordRegistry.All.Any(e => e.Kind == "trigger" && e.Word.Contains("finishes researching")) &&
      WordRegistry.All.Any(e => e.Kind == "trigger" && e.Word == "when research queue is empty"),
    "the word book lists the research triggers");

// ------------------------------------------------------------------
// Task 27 Part E: shared cooldowns
// ------------------------------------------------------------------

// The timers: one loop advances them, expiry is exact, and actors stay independent.
var cd = new Cooldowns();
var cdMarine = new FakeActor("Marine");
var cdOther = new FakeActor("Zergling");
Check(cd.Ready(cdMarine, "stim") && cd.Remaining(cdMarine, "stim") == 0,
    "a timer nobody started is ready");
cd.Start(cdMarine, "stim", 10);
Check(!cd.Ready(cdMarine, "stim") && Math.Abs(cd.Remaining(cdMarine, "stim") - 10) < 1e-9,
    "`put \"stim\" on cooldown for 10 seconds` starts a live timer");
cd.Tick(4);
Check(Math.Abs(cd.Remaining(cdMarine, "stim") - 6) < 1e-9, "one tick loop advances the timer");
cd.Tick(6);
Check(cd.Ready(cdMarine, "stim") && cd.Remaining(cdMarine, "stim") == 0,
    "the timer is exactly ready when its time is up");
cd.Tick(5);
Check(cd.Ready(cdMarine, "stim"), "an expired timer never goes negative");
cd.Start(cdMarine, "stim", 3);
cd.Start(cdMarine, "stim", 5);
Check(Math.Abs(cd.Remaining(cdMarine, "stim") - 5) < 1e-9 && !cd.Ready(cdMarine, "stim"),
    "starting the same timer again refreshes it instead of stacking");
cd.Start(cdMarine, "stim", 0);
Check(cd.Ready(cdMarine, "stim"), "`for 0 seconds` leaves the timer ready");
cd.Start(cdMarine, "stim", 4);
cd.Start(cdMarine, "storm", 2);
Check(!cd.Ready(cdMarine, "stim") && !cd.Ready(cdMarine, "storm") && cd.Count(cdMarine) == 2,
    "two abilities on one actor cool down independently");
cd.Start(cdOther, "stim", 4);
cd.Tick(2);
Check(cd.Ready(cdMarine, "storm") && !cd.Ready(cdMarine, "stim") && cd.Count(cdMarine) == 1,
    "the single loop expires only the timers that ran out");
Check(!cd.Ready(cdOther, "stim"), "another actor's same-named timer is untouched");
cd.Clear(cdMarine);
Check(cd.Ready(cdMarine, "stim") && cd.Count(cdMarine) == 0 && !cd.Ready(cdOther, "stim"),
    "Clear drops one actor's timers on death and leaves the others");
cd.Reset();
Check(cd.Ready(cdOther, "stim"), "Reset clears every timer");
Check(Cooldowns.VariableName("stim") == "cooldown stim" &&
      Cooldowns.TryReadVariable("cooldown stim", out string cdName) && cdName == "stim" &&
      !Cooldowns.TryReadVariable("cooldown", out _),
    "the value form maps to `cooldown <name>`");

// The words: action, condition and value.
var cdProgram = parser.Parse("when Q is pressed\n  put \"stim\" on cooldown for 10 seconds\n");
var cdSpec = ActionWords.Parse(((RawActionNode)cdProgram.Events[0].Body[0].Statement!).Words, _ => { });
Check(cdSpec.Verb == ActionVerb.PutOnCooldown && cdSpec.Thing == "stim" &&
      Math.Abs(cdSpec.Duration - 10) < 1e-9,
    "`put \"stim\" on cooldown for 10 seconds` parses to PutOnCooldown");
string cdHint = "";
Action<string> grabCdHint = message => cdHint = message;
var bareCdProgram = parser.Parse("when Q is pressed\n  put \"stim\" on cooldown\n");
var bareCdSpec = ActionWords.Parse(
    ((RawActionNode)bareCdProgram.Events[0].Body[0].Statement!).Words, grabCdHint);
Check(bareCdSpec.Verb == ActionVerb.Unknown && cdHint.Contains("10 seconds"),
    "`put` without a duration explains the shape");

var readyNode = Conditions.Parse(new List<Token>
{
    new Token { Type = TokenType.Word, Text = "my" },
    new Token { Type = TokenType.Str, Text = "stim" },
    new Token { Type = TokenType.Word, Text = "is" },
    new Token { Type = TokenType.Word, Text = "ready" },
});
Check(readyNode.Kind == ConditionKind.CooldownReady && readyNode.Name == "stim" && !readyNode.Negated,
    "`my \"stim\" is ready` parses to CooldownReady");
var notReadyNode = Conditions.Parse(new List<Token>
{
    new Token { Type = TokenType.Word, Text = "my" },
    new Token { Type = TokenType.Str, Text = "stim" },
    new Token { Type = TokenType.Word, Text = "is" },
    new Token { Type = TokenType.Word, Text = "not" },
    new Token { Type = TokenType.Word, Text = "ready" },
});
Check(notReadyNode.Kind == ConditionKind.CooldownReady && notReadyNode.Negated,
    "`my \"stim\" is not ready` inverts the same condition");

var cdValueProgram = parser.Parse("when game starts\n  set Left to my \"stim\" cooldown\n");
var cdValueSet = (SetNode)cdValueProgram.Events[0].Body[0].Statement!;
Check(cdValueSet.Value is VariableValue cdValue && cdValue.Owner == "my" &&
      cdValue.Name == "cooldown stim",
    "`my \"stim\" cooldown` reads as one variable");

// End to end: the word starts the acting unit's timer, the gate follows it, the value reads it.
var cdEngine = FreshEngine(new FakeWorld());
var cdUnit = new FakeActor("Marine");
// The bridge half of an action word: FlaxActions parses the line and hands it to the manager.
cdEngine.OnRawAction = (context, words) =>
{
    var spec = ActionWords.Parse(words, _ => { });
    if (spec.Verb == ActionVerb.PutOnCooldown)
        cdEngine.Cooldowns.Start(context.Me, spec.Thing, spec.Duration);
};
cdEngine.AddSheet(cdUnit, parser.Parse("when me appears\n  put \"stim\" on cooldown for 2 seconds\n"));
cdEngine.Tick(0);
Check(!cdEngine.Cooldowns.Ready(cdUnit, "stim") &&
      Math.Abs(cdEngine.Cooldowns.Remaining(cdUnit, "stim") - 2) < 1e-9,
    "`put ... on cooldown` starts the acting unit's timer");
var cdContext = new EventContext
{
    Variables = cdEngine.Variables,
    World = cdEngine.World,
    Me = cdUnit,
    Runtime = cdEngine,
};
Check(!Conditions.Evaluate(readyNode, cdContext),
    "`my \"stim\" is ready` is false while the timer runs");
Check(Conditions.Evaluate(notReadyNode, cdContext),
    "`my \"stim\" is not ready` is true while the timer runs");
Check(Math.Abs(cdContext.ReadMy("cooldown stim").AsNumber() - 2) < 1e-9,
    "`my \"stim\" cooldown` reads the live seconds left");
cdEngine.Tick(1);
Check(Math.Abs(cdContext.ReadMy("cooldown stim").AsNumber() - 1) < 1e-9,
    "the runtime advances the timer once per tick");
cdEngine.Tick(2);
Check(Conditions.Evaluate(readyNode, cdContext) && !Conditions.Evaluate(notReadyNode, cdContext),
    "the gate turns true when the timer runs out");
Check(cdContext.ReadMy("cooldown stim").AsNumber() == 0,
    "the value reads 0 once ready");

// The old polling is untouched: `when every 1 seconds` still fires.
var pollEngine = FreshEngine(new FakeWorld());
pollEngine.AddSheet(new GlobalHost(), parser.Parse("when every 1 seconds\n  add 1 to Polled\n"));
pollEngine.Tick(0);
pollEngine.Tick(1.5);
Check(pollEngine.Variables.GetGlobal("Polled").AsNumber() >= 1,
    "a `when every 1 seconds` cooldown poll still works alongside the shared timers");

Check(WordRegistry.All.Any(e => e.Kind == "action" && e.Word == "put"),
    "the word book lists `put ... on cooldown`");
Check(WordRegistry.All.Any(e => e.Kind == "condition" && e.Word == "my \"<name>\" is ready"),
    "the word book lists the cooldown condition");
Check(WordRegistry.All.Any(e => e.Kind == "value" && e.Word == "my \"<name>\" cooldown"),
    "the word book lists the cooldown value");

// ------------------------------------------------------------------
// Task 27 Part F: example content (Marine / Barracks / HighTemplar)
// ------------------------------------------------------------------

var marineFile = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Marine.events"));
var barracksFile = parser.Parse(File.ReadAllText("Source/Game/GameFiles/Barracks.events"));
var templarFile = parser.Parse(File.ReadAllText("Source/Game/GameFiles/HighTemplar.events"));
Check(marineFile.Events.Count >= 3 && barracksFile.Events.Count >= 4 && templarFile.Events.Count >= 4,
    "the three Part F example sheets parse");

List<ActionSpec> SpecsOf(ProgramFile file)
{
    var specs = new List<ActionSpec>();
    foreach (var ev in file.Events)
        foreach (var line in ev.Body)
            if (line.Statement is RawActionNode raw)
            {
                var spec = ActionWords.Parse(raw.Words, _ => { });
                if (spec.Verb != ActionVerb.Unknown)
                    specs.Add(spec);
            }
    return specs;
}

var marineSpecs = SpecsOf(marineFile);
Check(marineSpecs.Any(s => s.Verb == ActionVerb.AddBuff && s.BuffName == "stim" &&
      Math.Abs(s.Duration - 8) < 1e-9 && s.BuffModifiers.Count == 1 &&
      s.BuffModifiers[0].Stat == "Speed" && Math.Abs(s.BuffModifiers[0].Number - 40) < 1e-9),
    "Marine's stim is an 8 second buff adding 40 to Speed");
Check(marineSpecs.Any(s => s.Verb == ActionVerb.PutOnCooldown && s.Thing == "stim" &&
      Math.Abs(s.Duration - 10) < 1e-9),
    "Marine's stim starts its 10 second shared cooldown");

var barracksSpecs = SpecsOf(barracksFile);
Check(barracksSpecs.Any(s => s.Verb == ActionVerb.Research && s.Thing == "Stim Pack"),
    "Barracks researches Stim Pack through the manager");
Check(barracksSpecs.Any(s => s.Verb == ActionVerb.Train && s.Thing == "Marine"),
    "Barracks trains a Marine through the production queue");

var templarSpecs = SpecsOf(templarFile);
var stormAimSpec = templarSpecs.FirstOrDefault(s => s.Verb == ActionVerb.Aim);
Check(stormAimSpec != null && stormAimSpec.Ability == "psi storm" &&
      (stormAimSpec.Accepts & TargetAccepts.Point) != 0,
    "HighTemplar aims psi storm at a point");
var stormDamageSpec = templarSpecs.FirstOrDefault(s => s.Verb == ActionVerb.DamageArea);
Check(stormDamageSpec != null && stormDamageSpec.Thing == "my aim" &&
      stormDamageSpec.AmountNode is NumberValue stormDamage && stormDamage.Value == 30 &&
      Math.Abs(stormDamageSpec.Radius - 180) < 1e-9,
    "HighTemplar's storm damages a 180 radius around the pick for 30");
Check(TriggerClassifier.Classify(templarFile.Events.First(e => e.Trigger.Any(t => t.Text == "picks"))).Kind == TriggerKind.AimPicked,
    "HighTemplar reacts to `when my aim picks a target`");

Upgrades.LoadData(parser.Parse(File.ReadAllText("Source/Game/GameFiles/Upgrades.events")));
Check(Upgrades.RequiresOf("Stim Pack") == "Barracks" &&
      Math.Abs(Upgrades.DeclaredCostOf("Stim Pack") - 100) < 1e-9,
    "Upgrades.events prices Stim Pack at 100 and restricts it to the Barracks");

// Headless acceptance: the Marine sheet really stims through the runtime's own managers.
var fWorld = new FakeWorld();
var fEngine = FreshEngine(fWorld);
var fMarine = new FakeActor("Marine");
fEngine.OnRawAction = (context, words) =>
{
    var spec = ActionWords.Parse(words, _ => { });
    if (spec.Verb == ActionVerb.AddBuff)
    {
        var buff = new BuffInstance { Name = spec.BuffName, Duration = spec.Duration, Source = context.Me };
        foreach (var m in spec.BuffModifiers)
            buff.Modifiers.Add(new BuffModifier
            {
                Stat = m.Stat,
                Amount = m.Number,
                HasAmount = !m.Multiply,
                Multiplier = m.Multiply ? m.Number : 1,
                HasMultiplier = m.Multiply,
            });
        fEngine.Buffs.Add(fEngine.Variables, context.Me, buff, out _);
    }
    else if (spec.Verb == ActionVerb.PutOnCooldown)
    {
        fEngine.Cooldowns.Start(context.Me, spec.Thing, spec.Duration);
    }
};
fEngine.AddSheet(fMarine, marineFile);
fEngine.Tick(0);
Check(fEngine.Variables.GetLocal(fMarine, "Speed").AsNumber() == 90 &&
      fEngine.Variables.GetLocal(fMarine, "Health").AsNumber() == 45,
    "Marine starts from its sheet's stats");
fEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Q" });
fEngine.Tick(1);
Check(fEngine.Variables.GetLocal(fMarine, "Mana").AsNumber() == 75,
    "pressing Q spends the 25 mana stim cost");
Check(fEngine.Variables.GetLocal(fMarine, "Speed").AsNumber() == 130,
    "the stim buff raises Marine Speed by 40 for its duration");
Check(!fEngine.Cooldowns.Ready(fMarine, "stim"),
    "the stim shared timer is running after the cast");
fEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "Q" });
fEngine.Tick(2);
Check(fEngine.Variables.GetLocal(fMarine, "Mana").AsNumber() == 75,
    "a second Q while cooling down spends nothing");
for (int fTick = 3; fTick <= 9; fTick++)
    fEngine.Tick(fTick);
Check(fEngine.Variables.GetLocal(fMarine, "Speed").AsNumber() == 90,
    "the stim buff puts Speed back when its 8 seconds end");
Check(!fEngine.Cooldowns.Ready(fMarine, "stim"),
    "the 10 second cooldown outlives the 8 second buff");
for (int fTick = 10; fTick <= 11; fTick++)
    fEngine.Tick(fTick);
Check(fEngine.Cooldowns.Ready(fMarine, "stim"),
    "stim is ready again after its cooldown");

// Headless acceptance: the Barracks sheet researches Stim Pack and applies the level.
var bWorld = new FakeWorld();
var bEngine = FreshEngine(bWorld);
var fBarracks = new FakeActor("Barracks");
bEngine.OnRawAction = (context, words) =>
{
    var spec = ActionWords.Parse(words, _ => { });
    if (spec.Verb == ActionVerb.Research)
        bEngine.Research.Enqueue(bEngine.Variables, bEngine, context.Me,
            bEngine.World.NameOf(context.Me), spec.Thing, 0);
};
bEngine.Variables.SetGlobal("Minerals", ValueBox.OfNumber(200));
bEngine.Variables.SetGlobal("StimPackTime", ValueBox.OfNumber(2));
bEngine.AddSheet(fBarracks, barracksFile);
bEngine.Tick(0);
bEngine.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyPressed, Name = "R" });
bEngine.Tick(1);
Check(bEngine.Research.CurrentUpgrade(fBarracks) == "Stim Pack" &&
      bEngine.Variables.GetGlobal("Minerals").AsNumber() == 100,
    "Barracks' R starts Stim Pack research and charges its 100 price");
bEngine.Tick(3);
Check(Math.Abs(bEngine.Variables.GetGlobal("Player1 StimPack").AsNumber() - 1) < 1e-9,
    "the finished research raises Player1 StimPack for that player only");
Check(!bEngine.Variables.HasGlobal("Player2 StimPack"),
    "the other player's tech is untouched");
Upgrades.Reset();

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

    private readonly Dictionary<object, (double X, double Z)> positions = new Dictionary<object, (double X, double Z)>();
    private readonly HashSet<(object, double, double)> blockedPoints = new HashSet<(object, double, double)>();

    /// <summary>Places an actor on the ground plane, so point distances (sound positions) work.</summary>
    public void SetPosition(object actor, double x, double z)
    {
        positions[actor] = (x, z);
    }

    /// <summary>Blocks (a wall) the straight line from an actor to a world point.</summary>
    public void SetPointBlocked(object from, double x, double z, bool on)
    {
        if (on)
            blockedPoints.Add((from, x, z));
        else
            blockedPoints.Remove((from, x, z));
    }

    public bool IsKeyHeld(string key)
    {
        return held.Contains(key);
    }

    /// <summary>"pad button" pairs held on the fake gamepads (player 1 → pad 0).</summary>
    private readonly HashSet<string> gamepad = new HashSet<string>();

    public void SetGamepadButton(int pad, string button, bool on)
    {
        if (on)
            gamepad.Add(pad + " " + button);
        else
            gamepad.Remove(pad + " " + button);
    }

    public bool IsGamepadButtonHeld(int pad, string button)
    {
        return gamepad.Contains(pad + " " + button);
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

    private readonly HashSet<object> grounded = new HashSet<object>();

    public bool IsOnGround(object actor)
    {
        return grounded.Contains(actor);
    }

    /// <summary>Sets the ground answer behind `me is on ground` / `me is airborne`.</summary>
    public void SetGrounded(object actor, bool on)
    {
        if (on)
            grounded.Add(actor);
        else
            grounded.Remove(actor);
    }

    private readonly HashSet<object> climbing = new HashSet<object>();

    public bool IsClimbing(object actor)
    {
        return climbing.Contains(actor);
    }

    /// <summary>Sets the ladder answer behind `me is climbing`.</summary>
    public void SetClimbing(object actor, bool on)
    {
        if (on)
            climbing.Add(actor);
        else
            climbing.Remove(actor);
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

    // Task 18 Part H: the RTS queries are answered by the runtime in these tests, not by the
    // fake world, so the world keeps the same empty defaults as DefaultEventWorld.
    public IReadOnlyList<object> GetSelection() => Array.Empty<object>();
    public int GetSelectedCount() => 0;
    public double GetResource(string name) => 0;
    public bool IsRevealed(object actor, int playerId) => false;
    public void RevealArea(int playerId, double x, double z, double radius) { }
    public void HideArea(int playerId, double x, double z, double radius) { }
    public double GetBuildProgress(object building) => 0;
    public IReadOnlyList<string> GetOrderQueue(object unit) => Array.Empty<string>();

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

    /// <summary>An actor with no recorded position is infinitely far away, so it hears nothing.</summary>
    public double DistanceToPoint(object from, double x, double z)
    {
        if (!positions.TryGetValue(from, out var spot))
            return double.MaxValue;
        double dx = spot.X - x;
        double dz = spot.Z - z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public bool HasLineOfSightToPoint(object from, double x, double z)
    {
        return !blockedPoints.Contains((from, x, z));
    }

    private readonly HashSet<(object, object)> blocking = new HashSet<(object, object)>();
}

