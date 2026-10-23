# TASKS.md — Work Queue

The task list for building the Event Manager. Bite-sized parts, each scoped so a **fresh
session** can pick one up without any outside context.

**How to use:** start a new session and say e.g. *"Work on Task 2, Part A in TASKS.md"*.
That session should read `DESIGN.md`, do only that Part, and check the box when done.

**Source of truth:** `DESIGN.md` (the design doc). Tasks only say *what* and *done-when*;
`DESIGN.md` says *how it should work*.

**Status legend:** `[ ]` not started  `[~]` in progress  `[x]` done  `[!]` blocked (say why)

---

## Task 1 — Flax Project Scaffold `[x]`

Goal: a Flax project exists with the Event Manager skeleton, so every later task has a home.

### Part A — Project setup
- [x] Engine environment: Flax + matching .NET SDK installed & verified (user confirmed).
- [x] Flax project created (C# flavor) inside this repo at `New/EventManager/EventManager/`
  (.flaxproj present, default module **Game** under `Source/Game/`, opens in the editor).
- [x] 10-second check: open the project and watch a scene play (user added a scene and
  confirmed: plays fine, no output errors — 2026-10-23).

### Part B — Folder skeleton
- [x] `.gitignore` added **before any commit**, modeled on the official FlaxSamples one
  (reference copy at `Docs/FlaxSamples.gitignore-ref.txt`; covers `Binaries/`, `Cache/`,
  `Logs/`, solution/csproj files, user settings). Verified with `git status` +
  `git check-ignore`: only Source, Content, docs and `.flaxproj` get tracked.
- [x] Created inside the game module: `Source/Game/Events/` (parser + runtime code) and
  `Source/Game/GameFiles/` (the `.events` text files) — `GameFiles/` seeded with an empty
  `Game.events` so git tracks the folder.
- [x] Created placeholder classes from the DESIGN.md architecture table (Section 6),
  inside `Source/Game/Events/` (namespace `Game`): `EventLexer`, `EventParser`,
  `EventRuntime`, `EventSheet`, `WordRegistry`.
- [x] Verify: `dotnet build` of `Source/Game.csproj` — **Build succeeded, 0 warnings,
  0 errors** (Flax 1.13). Editor-side "Compile Scripts" still worth one glance
  (same code, editor compiler).

---

## Task 2 — Parser (front end) `[x]`

Goal: text goes in, a validated event tree comes out, with friendly errors. **No Flax needed —
pure C#, runnable in isolation.** (DESIGN.md Sections 3 & 4 define behavior.)

### Part A — Lexer: text → tokens `[x]`
- [x] `Source/Game/Events/EventLexer.cs`: reads `.events` text, tracks line numbers; skips
  `#` comments (anywhere in lines), quoted strings, numbers (decimal + negative via
  InvariantCulture), words (anything else runs together, incl. `-1`, `20%`, `other's`),
  indents — **2 spaces = one level**, strict (tabs / odd widths / 2-level jumps are
  friendly `LexException`s with line numbers).
- [x] Token language now exists: `Token.cs` (`TokenType` + `Token` with `Line`),
  `LexException.cs` (`Line N: <human sentence>`). Dedents are emitted before the next
  event's tokens and before `EndOfFile`; EOF anchors to the last physical line.
- [x] Test runner harness: `Tests/EventManagerTests.csproj` (console, references the
  Events sources directly — no Flax at all; runs via `dotnet run --project Tests`).
- [x] Unit tests: 17 green — expected token lists, comments/blanks, dedents before the
  next event and before EOF, nested levels+stacked dedents, value forms (`-1`, `0.5`,
  `20%`, strings), line numbers across blank lines, all 5 friendly-error cases, plus a
  **doc self-test**: every `when`-starting code fence in DESIGN.md must lex clean
  (10 listings; DESIGN.md was normalized 4→2 spaces to match the spec).

### Part B — Parser: tokens → event tree
- [x] Parse blocks: one `when` line + condition lines (`unless`) + indented actions
  (`EventParser.cs` + `ParseException.cs` mirroring the lexer's `Line N: …` shape).
  Blocks: trigger tokens collected until line end; empty body, indented-orphan lines,
  too-deep body lines and nested `when`s (both at body depth and extra-indented) are
  friendly errors.
- [x] Accept the value forms everywhere a value is legal: numbers, `"text"`, `true`/`false`
  (needed by the shooter's `set my Invulnerable to true`), Capitalized variables,
  `my <name>` targets/reads, `random <low> to <high>` (one grammar rule — bounds are
  numbers or variables), and the one-op math form only inside `set A to B op C`.
  Statement nodes: `SetNode` / `AddNode` (+ `RawActionNode` for verb lines);
  `add` with math is rejected pointing at `set`.
- [x] Body-line classification: `unless` / bare condition lines (any condition mark:
  `is has becomes changes exists does near chance`) → `IsCondition`; first-word verb →
  raw action; `set`/`add` → statements with leftover checks. Friendly errors per
  DESIGN §4: unknown words get near-miss "did you mean" (Levenshtein ≤ 2 over the seed
  vocabulary) or the plain "I don't know what … means yet.
- [x] Unit test: the whole Top-Down Shooter example from DESIGN.md Section 7 parses with
  zero errors — 16 events, every event has trigger + body, `add 1 to Score`, `destroy me`
  verified as nodes. 40/40 tests green (10 lexer, 1 doc-lex self-test, 24 parser
  incl. 10 friendly-error cases); module build 0 warnings / 0 errors.

### Part C — Word registry: the vocabulary as data
- [x] `WordRegistry.cs`: the v0.1 word list from DESIGN.md Section 5 checked in as data —
  24 trigger templates (with machine-checkable shapes + alt shapes like
  `stops seeing … by <dist>` / `becomes not`), 8 conditions, 36 action entries, each with
  its concrete one-line example (later feeds autocomplete/docs). Hardcoded `KnownWords`
  removed — conditions marks, action verbs and near-miss suggestion candidates (incl. the
  structural words `when/unless/…`) now derive from the registry.
- [x] Parser validates trigger **shape** against the registry templates (placeholder
  matcher: `<key>/<thing>` = words, `<n>/<dist>` = number, `<name>` = quoted string,
  `<value>` = rest-of-line); inline trailing conditions after `and` are matched on the
  base part; a dangling `and` is a friendly error. Unknown triggers explain the shape
  instead of "unknown word". Got the `set text of <thing> to <val>` statement working
  (new `SetTextNode`) so the full menu vocabulary parses.
- [x] Unit test: near-miss typo suggestions (registry-driven), every registered word's
  example parses (66 examples checked), alt trigger shape, `when door is open` /
  `when W is pressings` rejected with the shape-explaining error. 47/47 green;
  module build 0 warnings / 0 errors (explicit usings again).

---

## Task 3 — Runtime (back end) `[~]`

Goal: the event tree actually *lives* in a running Flax game. Depends on Task 2.
(DESIGN.md Section 6 defines execution: polled vs pushed triggers, once-per-tick,
top-to-bottom order.)

### Part A — Variables
- [x] Variable store (`Runtime/EventVariables.cs`): global (Capitalized `set` targets) +
  per-object (`my …`, keyed by host object so each copy is separate), auto-typed by the
  value written — `ValueBox` kinds None/Number/Text/Boolean, re-typed by each write,
  defaults read as 0 / "" / false with a **one-time friendly warning** per name (warning
  sink injectable: `OnWarning`). `Task 1` marked `[x]` (user's editor play check passed).
- [x] Value evaluation for all value forms (`Runtime/ValueEvaluator.cs`): numbers, `"text"`,
  `true`/`false`, variables via a resolver delegate (Part B plugs the real context), the
  one-op math form, and `random <min> to <max>` — whole-number rolls for integer bounds
  (asks `Rng.Seed(n)` for replayable sequences), fractional rolls otherwise, bounds swap
  quietly if written high-to-low.
- [x] Unit test: the store (defaults + one-time warning, typing, per-object isolation,
  hot-reload survival w/ copies-not-aliases) and evaluation (integral dice luck-free,
  seeded exact sequences, fractional + swapped bounds, variable bounds, math ops,
  friendly divide-by-zero) — 75/75 green; module build 0/0.
  (`Tests/EventManagerTests.csproj` now covers `Source/Game/{Events,Runtime}/*.cs`; the
  `**` wildcard is avoided — it would swallow Flax's `Game.Build.cs`/`Game.Gen.cs`.)

### Part B — Execution engine
- [x] Trigger descriptors (`Runtime/Triggers.cs`): every trigger token list classifies into a
  `TriggerKind` with parameters (key names, required partner/thing names, timer seconds,
  watch variable + target value, message/anim names) — pushed kinds flow through a
  per-sheet queue, polled kinds (`is held`, `every N seconds`, `becomes`/`changes`
  watches) are evaluated each tick.
- [x] Tick engine (`Runtime/EventRuntime.cs`, `Conditions.cs`, `EventWorld.cs`), exactly per
  DESIGN.md §3/§6: resumes due `wait` continuations first, then pumps the pushed queues
  (each event fires **at most once per tick** even with multiple queued pushes; two
  same-tick pushes don't run it twice), then the polled pass. All matching events run
  in written order, actions one-by-one; sheet order = add order, top wins.
- [x] `wait` (incl. `wait random a to b seconds` — seeded via `Rng`, suspends only that
  event, resumption carries the gate state), `stop this event`, `unless` +
  bare-condition gating (each condition line re-gates until the next one), `send
  message` routed same-day semantics: queued on the receiving sheet's push queue
  (processed next tick), broadcast when untargeted. Messages / contacts push via
  `PushContact`, `RaisePush(All)`, `SendRawMessage`; `Game.events`
  helpers: `SessionStarted`, `SceneStarted` (scene start refires per session call),
  `when me appears` auto-pushes on sheet add.
- [x] Engine-native statements: `set` (one-op math; `+` on text values joins them — noted
  as an engine convenience), `add`, `set text of` → `OnSetText` hook; everything else
  dispatches raw to the `OnRawAction` hook (Part C wires the real words there).
  Engine-native condition kinds: `is/is not/higher/lower`, `exists`/`does not exist`,
  `near by` (via `IEventWorld.Distance`), `key is held`, `chance N%` (Rng), `has tag`,
  `my bag has` (bag store pre-built for the items task); unknown conditions warn once
  and read false.
- [x] Unit test: order-dependence scenario (all matching events in order, once per tick
  with duplicate pushes), pushed-before-polled, `wait` resume before fresh polled pass,
  session/scene/appears lifecycle, gating chains, stop-early, held-key repetition,
  global + per-object watch edges, message fan-out/targeting, gates by chance/bag/near/
  exists/tags — **108/108 green**; module build 0/0 (explicit usings quirk once more in
  `Triggers.cs`).

### Part C — Flax bridge (words become real)
- [x] `EventSheet` script per actor + auto-attach (`EventsManager`): the manager script
  loads `Source/Game/GameFiles/*.events`, attaches by **actor name** (zero clicks),
  adds a per-actor `EventSheet` that subscribes the actor's colliders' `TriggerEnter`
  / `TriggerExit` events and pushes `Touches` / `Overlaps` up. `Game.events` attaches
  to a global host named `Game` (scene-wide; its `me` actions warn once — no body).
  Game start fires once per play session, scene start every manager wake; runtime is
  ticked from `OnUpdate` (`Time.GameTime`, resume → pushed → polled order).
- [x] Input edges pushed each frame: keyboard + Left/Right/Middle Mouse press & release
  (`FlaxWorld.IsKeyHeld` polls held state for the engine; the manager pushes the raw
  edges), wheel up/down via `Input.MouseScrollDelta`.
- [x] Action words → real Flax API (`FlaxActions`): move (heading-based, per-tick), stop
  moving, turn, look at (snap `Actor.LookAt`), face (continuous, tracked), teleport,
  push, goto (walks until arrival), destroy (`Object.Destroy`, also clears its
  tracking), spawn (template actor by name → `Clone()` + `Level.SpawnActor` — same-name
  `.events` files attach to clones automatically), play sound (`AudioSource` on the
  actor) / stop sound, hide/show (`IsActive`), set text of ( finds a
  `FlaxEngine.GUI.Label` on the thing's `UIControl`), restart scene
  (`Level.LoadScene(current GUID)`), load scene (warns — needs the scene-name
  wiring in the menu task), quit game (`Engine.RequestExit`), play/stop anim (warns —
  visuals wiring later). Per-object tags: real Flax `Actor.HasTag` backs
  `other has tag Enemy`.
- [x] Pure parts (still unit tested without Flax): `ActionWords` parses every action verb
  to a spec (25 parse checks + friendly unknown/bent-verb hints), `EventFiles` locates
  the GameFiles folder (walks up parents, loose `GameFiles` accepted) and loads all
  `.events` files sorted, a broken file shows its friendly parse error and yields an
  **empty tree** so hot reload can keep the old one playing. 108+25 → **133/133
  green**; module build 0/0 (checked against the real `FlaxEngine.CSharp.dll` —
  `Orientation` is a `Quaternion` (no Yaw), `Collider` carries the trigger events,
  children are arrays).
- [ ] Verify: play a one-file mini-game — press W → cube moves — **needs the user's
  editor run** (see session log for the exact steps).

### Part D — Hot reload
- [ ] Watch `.events` files; on save: reparse; if valid, swap the live tree in place and
  keep existing variable values; if it has errors, keep playing the old tree + show the
  friendly error.
- [ ] Verify: fix a typo in a `.events` file while the game runs in the editor and see the
  fix take effect with no restart.

---

## Task 4 — The Top-Down Shooter, playable `[ ]`

Goal: the DESIGN.md Section 7 example exists as real files and is playable. Depends on
Tasks 1–3. **This is the acceptance test for the whole v0.1 system.**

### Part A — Convert the example
- [ ] Write `Game.events`, `Player.events`, `Bullet.events`, `Enemy.events` exactly as the
  DESIGN.md Section 7 listing.
- [ ] Scene setup: Player, Bullet, Enemy, SpawnPoint actors, top-down camera.

### Part B — Acceptance pass
- [ ] Play: move with WASD, shoot with mouse, kill 5 enemies, see "You win!", scene restarts.
- [ ] Get hit 3 times: invulnerability blink works, "Game Over" shows.
- [ ] Play a full session reading only the `.events` files — note any word or behavior that
  felt wrong, file a line in `Section 9 Open Questions` of DESIGN.md.

---

## Task 5 — Armature Character (the BGE way) `[ ]`

Goal: a real character with an armature, animated by event words — Flax nodes effectively made
invisible. Depends on Tasks 1–3 (like Task 4). DESIGN.md Section 5 "Armature characters
(the BGE way)" defines it all.

### Part A — The template graph
- [ ] Character pipeline works: model/armature exported from Blender into the Flax project,
  one test character rigged with the standard template Anim Graph (state names = Action names).
- [ ] Verify: `PlayState("Walk")` called from a quick manual checklist plays the walk cycle.

### Part B — Animation words in the system
- [ ] Word registry entries + parser/lexer support: `play anim "<name>"`, `loop anim "<name>"`,
  `stop anim`, and trigger `when anim "<name>" ends`.
- [ ] Runtime mapping: play-once-holds-last-frame, loop-until-replaced, `stop anim` back to
  previous, short cross-blend on replace (start ~0.2 s, tune by feel).
- [ ] Friendly error: unknown anim name lists what the character has
  (`I don't know the anim "Walked". This character has: Idle, Walk, Shoot.`).
- [ ] Unit test: ends-trigger fires for one-shots only, and loop/stop transitions behave.

### Part C — The character example
- [ ] Write `Character.events` exactly as the DESIGN.md "Armature characters" snippet:
  walk-loop / idle on release / one-shot shoot / back to idle when the shot ends.
- [ ] Verify: loop runs while holding W, stops cleanly on release, shoot plays once and
  re-enters idle by itself.

---

## Task 6 — Menus, Scenes & UI `[ ]`

Goal: real menus (title screen, quit) and a live HUD, all built the scene-switching way.
Depends on Tasks 1–3. DESIGN.md Section 5 "Menus & game UI (the scene-switching way)"
defines it all. `pause game` is parked (Task 9).

### Part A — Scene words
- [ ] Word registry + runtime: `load scene "<name>"`, `quit game`.
- [ ] Trigger `when scene starts` (fires on every scene load) alongside `when game starts`
  (once per play session); verify the difference in one test scene pair.
- [ ] Verify: two scenes exist, one event loads the other and quit actually closes the game.

### Part B — UI words
- [ ] Trigger `when <thing> is clicked` wired to named UI actors (Button/Text/Image) in the
  Flax editor; works on `me` when the sheet is attached to the button itself.
- [ ] Generalize `show`/`hide` to any named actor (`show PausePanel`), and add
  `set text of <thing> to <val>` (variable or plain text).
- [ ] New watcher flavor: `when <var> changes` (fires on any value change, next to `becomes`).
- [ ] Unit test: click/show/hide/text-set behave and parser accepts every row above.

### Part C — The menu example
- [ ] Build a Menu scene (PlayButton, QuitButton) + `Menu.events` exactly as the DESIGN.md
  Section 5 menu listing.
- [ ] Build Level1 with a `ScoreLabel` HUD refreshed by `when Score changes`.
- [ ] Verify: Menu → Play → Level1 loads with visible label; score updates live while
  shooting; quitting works from menu.

---

## Task 7 — Enemy Brains & Navigation `[ ]`

Goal: an enemy that patrols, spots the player, chases and attacks — written only in the event
language. Depends on Tasks 1–3 (like Tasks 4–6); animation optional (cubes suffice).
DESIGN.md Section 5 "Enemy brains & navigation (the BGE way)" defines everything.

### Part A — The nav bridge
- [ ] Scene setup contract works: a level with a **baked NavMesh** and a couple of
  `Waypoint` actor markers (no AI tools opened).
- [ ] Word registry + runtime: action `goto <thing>` (Flax NavMesh pathfinding) and trigger
  `when me arrives at <thing>` (path finished or close enough).
- [ ] Target qualifier `random <thing>` works with `goto`.
- [ ] Verify: a cube walks to a random waypoint, waits, picks the next one — no walls clipped.

### Part B — Sight words
- [ ] Trigger `when me sees <thing> by <dist>` — true **edge** trigger (fires once when sight
  starts), implemented as distance check + raycast so walls block it.
- [ ] Trigger `when me stops seeing <thing> by <dist>` — falling edge (fires once when sight
  ends; no `by <dist>` reuses the last triggered range).
- [ ] Verify: sighting fires once on entering range, stops firing while standing still in
  sight, re-fires only after a sight break, and does not fire through a wall.

### Part C — The enemy example
- [ ] Write `Enemy.events` exactly as the DESIGN.md enemy listing (patrol -> chase -> attack
  with `my mode` flags).
- [ ] Pair it with `Player.events`: the enemy hurts the player on contact (`send message
  "hurt"`), the player leans on the Menus/example patterns for feedback.
- [ ] Verify the full loop: patrol around waypoints -> spots the player -> chases (re-target
  during the chase) -> attacks within melee range -> loses sight -> back to patrol.
- [ ] Note any behavior that felt wrong or vocabulary gap, file a line in DESIGN.md §9.

---

## Task 8 — Items, Bag & Equipment (RPG batch) `[ ]`

Goal: the Diablo-style loop — loot drops, pickup, equip, stats change — entirely in the event
language. Depends on Tasks 1–3 plus enemy examples for drop sources.
DESIGN.md Section 5 "Items & the bag (the D2 loop)" defines everything.

### Part A — The bag
- [ ] Word registry + runtime: `add <thing> to my bag`, `remove <thing> from my bag`,
  condition `my bag has <thing>`; the bag holds things **by name and count**. Possessive reads
  (`other's damage`, `Player's gold`) land with this batch in the variable store.
- [ ] Verify: pick up two Swords and a Potion by touching them; `set text of BagLabel to my
  bag` shows `Sword x2, Potion` — text bag only, grid UI parked.

### Part B — Equip & item events
- [ ] Actions `equip <thing>`, `drop <thing>`; triggers `when me gets equipped` /
  `when me gets unequipped` / `when me gets dropped` on the item's sheet.
- [ ] Context rule implemented and tested: equip/equip/unequip moments run the item's events
  with `me` = the **wearer** (stat changes land on the owner); world events outside equip run
  with `me` = the item.
- [ ] Unit test: equip a +5 sword → wearer's damage +5; unequip → back to baseline; stacking
  two equips works; unequip drops stats exactly once.

### Part C — Acceptance: the D2 loop
- [ ] Write the DESIGN.md "Items & the bag" listing as real files (Sword.events, Player pickup/
  equip/attack events, Enemy pressing on `other's damage`).
- [ ] Chain it with the loot words that already exist: enemy death → `chance 30%` →
  `spawn Sword at me`.
- [ ] Verify the full loop: kill an enemy → sword drops → walk over it → bag grows → press 1
  to equip → next mouse swing costs the enemy visibly more health (damage text or health read).
- [ ] Note any vocabulary friction in a DESIGN.md §9 line.

---

## Task 9 — Point & Click Adventure batch `[ ]`

Goal: an adventure — hover hints, walk to clicked spots, a locked-door puzzle, menu-composed
dialogue. Depends on Tasks 1–3 (plus 6 for the menu words used by dialogue).
DESIGN.md Section 5 "Point & click adventures (the composition showcase)" defines everything.

### Part A — Hover & walk-to-click
- [ ] Trigger `when me is hovered` — fires while the cursor is over the actor (edge or steady,
  generation-ready: verify hover hint text shows when the cursor pauses on a hotspot).
- [ ] Action `goto there` — walks to the **last clicked position** using the NavMesh; pairs
  with `when Ground is clicked` and `when me arrives at there`.
- [ ] Verify: click on bare ground → the character pathfinds to that exact spot, no waypoints
  or named targets involved.

### Part B — Minimal camera pair
- [ ] Actions `camera looks at <thing>` (aim view at any actor) and `camera follows <thing>`
  (centers while it moves; aiming elsewhere releases the follow).
- [ ] Verify: smooth changes between targets, follow survives actor movement, re-aim frees
  the camera — proper top-down adventure feel.

### Part C — Acceptance: the locked-door puzzle
- [ ] Write the DESIGN.md listing as real files: Door.events (hint hover + locked/unlocked
  split via a condition line), Player.events (`when Ground is clicked` → `goto there`),
  an NPC with menu-composed dialogue (DialogPanel + ChoiceA/B buttons), and a Key item.
- [ ] Chain the bag/equip words from Task 8: pick up the key off the floor, use it on the door.
- [ ] Verify the full loop: hover shows the hint → click door = "It's locked" → walk to key →
  pick up → click door again = opens → NPC dialogue choices set a story flag.
- [ ] Note any vocabulary friction in a DESIGN.md §9 line.

---

## Task 10 — Later (do not start until 1–9 done) `[ ]`

- [ ] **Task 10 — Editor window (M3):** plugin window with syntax coloring, red line markers,
  autocomplete from the word registry, docs-on-hover per word.
- [ ] **Task 11 — Vocabulary plus (M4):** more words: save/load game, camera extras (pans,
  zooms, cutscene framing), spawn-from-prefab-folder, plus a "Word Book" reference page
  generated from code.
- [ ] **Task 12 — Decisions parked in DESIGN.md §9:** `or`-chains, physics-vs-kinematic
  `move`, camera-relative movement, persistence rules, same-name actor disambiguation,
  animation cross-blend feel, `pause game` / `resume game`, `when path fails`,
  `next <thing>` patrol qualifier, grid inventory UI, bag-count comparisons,
  multi-op math formulas, XP-threshold helper, floating combat text,
  dedicated dialogue machinery (portraits/typewriter/voice).

---

## Session Log (append one line after each session)

| Date | Session did | Tasks/Parts touched |
|---|---|---|
| 2026-10-22 | Wrote DESIGN.md; rewrote example as Top-Down Shooter | Task 1 A–B (prep) |
| 2026-10-22 | Added BGE-style animation words + new Task 5 (Armature Character); renumbered later tasks to 6–8 | Task 5 A–C (planned) |
| 2026-10-22 | Added Menus/Scenes/UI words + new Task 6; renumbered later tasks to 7–9; pause parked in §9 | Task 6 A–C (planned) |
| 2026-10-22 | Added enemy/nav words (goto, arrives at, sees/stops seeing, random) + new Task 7; renumbered later tasks to 8–10; path-fails and patrol precision parked in §9 | Task 7 A–C (planned) |
| 2026-10-22 | Added RPG batch (bag, equip, item-actor events, possessive reads) + new Task 8; renumbered later tasks to 9–11; grid UI/counts/multi-op math parked in §9 | Task 8 A–C (planned) |
| 2026-10-22 | Added point & click batch (hovered, goto there, minimal camera pair) + new Task 9; renumbered later tasks to 10–12; dialogue machinery and camera extras parked in §9 | Task 9 A–C (planned) |
| 2026-10-22 | Mouse patch catalog-wide: buttons named like keys (Left/Right/Middle Mouse), wheel up/down edge rows; Task 3 wiring line extended | Task 2 C, Task 3 C (doc patch) |
| 2026-10-23 | Environment + scaffold: Flax & .NET SDK verified; project created at `New/EventManager/EventManager/` (module Game, nested path — documented); `GameLogic/` doc naming updated to real `Source/Game/` paths; FlaxSamples gitignore reference saved to `Docs/`; `MyProject` at default location = user sandbox, not the project | Task 1 A (marked), Part B prep |
| 2026-10-23 | BGE borrow sweep: `random <min> to <max>` value form (parses + evaluates anywhere values are legal, seedable in tests), `face <thing>` continuous tracking word, Enemy patrol jitter example, full brick-borrow map in DESIGN.md §2 | Task 2 B, Task 3 A/C |
| 2026-10-23 | Task 1 Part B executed: `.gitignore` in project root (FlaxSamples-based, verified), `Source/Game/Events/` + `Source/Game/GameFiles/` (+ seed `Game.events`), 5 placeholder classes (namespace Game), `dotnet build` green 0/0 | Task 1 B done, Task 2 next (Part A: lexer) |
| 2026-10-23 | Task 2 Part A executed: lexer (tokens/line numbers/friendly errors, strict 2-space indents) + test harness (`Tests/`, 17 green incl. doc self-test over 10 DESIGN.md listings); DESIGN.md listings normalized 4→2 spaces; module rebuilt green. NOTE: Flax module needs explicit `using System…` (no implicit usings there) | Task 2 A done, next: Part B (parser) |
| 2026-10-23 | Task 2 Part B executed: parser → event tree (`ParseException`, `Ast.cs` = ProgramFile/EventNode/BodyLineNode + SetNode/AddNode/RawActionNode, `EventParser.cs`); value forms incl. `random` ranges, `true`/`false` (new `BooleanValue`), `my` vars, one-op `set` math; friendly errors with near-miss suggestions; found + fixed real gap: `when` inside an event body. §7 shooter parses 16/16 events; 40/40 tests green; module build 0/0 | Task 2 B done, next: Part C (word registry) |
| 2026-10-23 | Task 2 Part C executed: `WordRegistry.cs` (24 trigger shapes + conditions + action entries w/ examples, replaces hardcoded `KnownWords`); trigger-shape validation w/ placeholders, `and` splits, dangling-`and` error; `set text of` → `SetTextNode` so menu vocab parses; every registered example parses (66); 47/47 tests green; module build 0/0 (explicit usings quirk again) | Task 2 C done — Task 2 complete, next: Task 3 (runtime) |
| 2026-10-23 | User confirmed Task 1's editor play check (scene added, plays clean) → Task 1 `[x]`. Task 3 Part A executed: `Runtime/ValueBox.cs` (typed values, conversions), `Runtime/EventVariables.cs` (globals + per-object hosts, one-time unset warnings, `CarryValuesFrom` hot-reload survival), `Runtime/ValueEvaluator.cs` (all value forms, seeded `Rng`, one-op math); 75/75 tests green; module build 0/0 | Task 3 A done, next: Part B (execution engine) |
| 2026-10-23 | Task 3 Part B executed: `Runtime/Triggers.cs` (trigger classifier → descriptors/push signals), `Runtime/Conditions.cs` (condition parse + eval), `Runtime/EventWorld.cs` (key/dist/tag world interface), real `Runtime/EventRuntime.cs` (tick engine: wait-resume → pushed queues → polled every/held/watch, once-per-tick, gates, stop, send-message routing, bags prep); engine text-concat convenience for `set X to X + "txt"`; 108/108 tests green; module build 0/0 | Task 3 B done, next: Part C (Flax bridge) |
| 2026-10-23 | Task 3 Part C executed: `Bridge/EventsManager.cs` (manager: locate GameFiles, parse, attach-by-name, per-actor `Bridge/EventSheet.cs` collider subscription → Touches/Overlaps, input press/release/held edges + wheel, runtime tick), `Bridge/FlaxWorld.cs` (`IEventWorld` on real Input/Tag), `Bridge/FlaxActions.cs` (move/turn/look at/face/teleport/push/goto/destroy/spawn/sound/hide-show/set text/restart/quit; `play anim` + `load scene` warn for later updates), `Runtime/ActionWords.cs` + `Events/EventFiles.cs` (pure, tested across compile). Flax API mismatches (Orientation/Quaternion, trigger events on Collider not Actor, `Vector3*Quaternion`) caught by build-against-`FlaxEngine.CSharp.dll` + a reflection dump tool; 133/133 tests green; module build 0/0 | Task 3 C done except editor verify (user must play: W → move cube), then Part D (hot reload) |
