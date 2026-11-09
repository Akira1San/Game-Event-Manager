# TASKS.md — Work Queue

The task list for building the Event Manager. Bite-sized parts, each scoped so a **fresh
session** can pick one up without any outside context.

**How to use:** start a new session and say e.g. *"Work on Task 2, Part A in TASKS.md"*.
That session should read `DESIGN.md`, do only that Part, and check the box when done.

**Source of truth:** `DESIGN.md` (the design doc). Tasks only say *what* and *done-when*;
`DESIGN.md` says *how it should work*.

**Status legend:** `[ ]` not started  `[~]` in progress  `[x]` done  `[!]` blocked (say why)
read for project links Docs/FlaxSetup.md
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
  loads `Source/Game/GameFiles/*.events` and attaches sheets (component-first since
  **Part E**; originally by **actor name** zero-click fallback), adding a per-actor
  `EventSheet` that subscribes the actor's colliders' `TriggerEnter`
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
  tracking), spawn (template actor by name → `Clone()` + `Level.SpawnActor`; clone
  logic attach is handled by **Part E**: `Clone()` copies scripts, so clones carry the
  template's `EventSheet`), play sound (`AudioSource` on the
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
- [x] The manager polls the GameFiles folder (0.25s cadence, mtime signatures — no
  background threads); on the next tick after a save every changed file is re-parsed.
- [x] A valid save swaps the live event tree **in place** (`EventRuntime.SwapSheetProgram`
  — triggers re-classified, watch baselines/timers/fired-flags reset, queued pushes and
  in-flight `wait`s dropped on the swap, `appears` does NOT re-fire on swap). Variable
  values live in the shared store and are untouched: survival by name comes free; bags
  stay too. Behavior notes: first `GameStart` stays once-per-session (no refire), and a
  new `.events` file appearing mid-session attaches to matching actors immediately.
- [x] A save with errors **keeps the old tree playing** and shows its friendly error once
  per new error message (fixed text → then swap when a good version arrives). Files that
  start broken still attach the moment they parse clean.
- [x] Unit tests on the reload core: swap re-classifies polled triggers, variables
  survive, waits/queued pushes from the old tree are dropped, duplicate attaches are
  deduped (`FindActorSheet`), plus genuine file-based reload simulation through the real
  loader (`EventFiles` on temp GameFiles folders). **152/152 green**; module build 0/0.
- [ ] Verify (user, editor): with the game playing, save a fix to `Player.events` — the
  change takes effect during the same play session without a restart; save a typo — the
  friendly error appears and the old behavior keeps running until the fix saves.

### Part E — Per-actor event sheets (the component way)

User decision (2026-10-26): attach event logic like every engine does — select the actor,
*Add Script → EventSheet*, point it at an `.events` file. A **global manager with
find-actor targeting** was considered and **rejected** (contact triggers only reach the
sheets of the colliding pair, and every action line would need an explicit subject —
that's a different language, not a policy change). The file is a **shared script**: one
`Enemy.events` can be assigned to many actor instances; `my …` variables stay per-object,
so shared sheets never collide. Not a redesign — `EventRuntime` is attachment-agnostic;
only the *policy* of who gets which tree changes.

- [x] `SheetInstance.SourceName` (which `.events` file drives this sheet) split from
  `ActorName` (routing, `me`/`other`, registration lookups). `EventRuntime.AddSheet`
  takes the source name explicitly (old two-arg call keeps `ActorName` as source).
- [x] `Runtime`: `AddSheet(actor, program, sourceName)`, `FindSheetFor(actor)` (dedupe by
  actor identity, never returns the `Game` host), `SheetsUsingSource(source)`, and
  `RemoveSheet(sheet)` (`GlobalHost` moved to the pure runtime file so tests can play it).
- [x] `EventSheet` is a user-facing component: public `Event File` string field
  (blank = the actor's name, keeping today's zero-click default), visible in the editor.
  It self-registers with `EventsManager.Instance` on `OnStart` (late registration works —
  clones and `AddScript` calls included) and unregisters on `OnDestroy`. Collider
  touch/overlap subscription stays on it.
- [x] `EventsManager` attach priority, deduped by actor: **explicit component
  first → name fallback (only actors without a component) → global `Game.events` host**.
  Scenes are swept on start; spawned actors register themselves via their component.
- [x] Hot reload keys on `SourceName` and fans out: **all sheets using a file swap
  together** (several enemies, one `Enemy.events`); missing file (component points at
  `Playr.events`) → single warning with a near-miss suggestion via the engine's word
  distance (`did you mean "Player.events"?`), re-checked whenever the folder changes.
- [x] Dropped the runtime `StickerTo` on spawn/show — clones already carry the component
  (Flax `Clone()` copies scripts), so the old path **double-subscribed** trigger events on
  every clone. The Part C bullet claiming name-based clone attach is corrected.
- [x] The template actor must carry the component (that's what makes `spawn Bullet`
  inherit logic) — scene-setup note for Part 4 A.
- [x] Unit tests: SourceName reload fan-out (two sheets, one file → both tick with
  variables surviving per-object), `FindSheetFor`/dedupe, `RemoveSheet`,
  component-beats-name (priority by design: a component never falls back to name-matching,
  even when its file is missing), near-miss helper. **174/174 green**; module build 0/0.
- [x] Follow-up (user hit this in the editor): guard against a second `EventsManager` —
  the duplicate warns ("keep exactly one, somewhere permanent") and disables itself
  (`Script.Enabled = false`, `OnUpdate` also refuses to tick) before it can attach sheets
  or spawn-storm via cloned templates. Verified `Script.Enabled`/`op_Implicit`-to-bool
  ("is the engine object still alive") against the real Flax 1.13 API.
- [ ] Verify (user, editor): assign `EventSheet` to Player/Bullet/Enemy with their files;
  a blank-field dummy still works by name; clones shoot/move via the template's component.

---

## Task 4 — The Top-Down Shooter, playable `[ ]`

Goal: the DESIGN.md Section 7 example exists as real files and is playable. Depends on
Tasks 1–3. **This is the acceptance test for the whole v0.1 system.** Part A amended by
Task 3 Part E: actors use explicit `EventSheet` components instead of name-only attach.

### Part A — Convert the example
- [x] Write `Game.events`, `Player.events`, `Bullet.events`, `Enemy.events` exactly as the
  DESIGN.md Section 7 listing.
- [x] Scene setup: Player, Bullet, Enemy, SpawnPoint actors, top-down camera.
- [ ] Scene setup (Part E amendment): Player/Bullet/Enemy carry an `EventSheet` component
  pointing at `Player.events` / `Bullet.events` / `Enemy.events` (`Game` keeps no
  component — `Game.events` stays auto-global); one throwaway actor with a blank field to
  prove the name fallback still works.

### Part B — Acceptance pass
- [ ] Follow-up fix for the current shooter blockers (contact detection, name lookups,
      targeted `hit`, manager placement, collider setup): work through
      **`TASK_FIX_SHOOTER.md`** first, then run this acceptance pass.
      **Code complete** (Parts A–D + unit tests): real actor names via `IEventWorld.NameOf`,
      `Distance`/`Colliders`/`Both` contact modes (default `Both`; `overlaps` stays distance),
      collider-owner ancestor walk, `send message "hit" to other`, and the Flax tag fix.
      192/192 unit tests green; module builds clean. **Editor steps still yours:** move
      `EventsManager` to a permanent scene-root actor; then run the three mode checks.
- [ ] Play: move with WASD, shoot with mouse, kill 5 enemies, see "You win!", scene restarts.
- [ ] Get hit 3 times: invulnerability blink works, "Game Over" shows.
- [ ] Play a full session reading only the `.events` files — note any word or behavior that
  felt wrong, file a line in `Section 9 Open Questions` of DESIGN.md.

### Part C — Velocity/force movement (physics-native)
Goal: let actors move through their `RigidBody` velocity/forces instead of assigning
`Position` every tick, so collision response, CCD, and physics interactions work. Depends
on Part B colliders. Keep the current transform movement as the default; this is an explicit
opt-in so the existing example and `Distance` mode stay unchanged.

- [x] Decided the opt-in: a per-actor **`EventSheet` flag `Use Physics Movement`** (a body
  property, so one checkbox covers every `move`/`goto`/`push`; no new word). Recorded in
  DESIGN §5/§9.
- [x] `FlaxActions`: opted-in actors with a `RigidBody` are driven by `LinearVelocity`
  (dynamic) or a swept `Actor.AddMovement` (kinematic) instead of assigning `Position`;
  no body or flag off keeps the transform path. `stop moving` brakes a dynamic body, `goto`
  brakes on arrival. Pure rules in `Runtime/PhysicsMovement.cs`; the flag rides
  `SheetInstance.UsePhysicsMovement` → `EventContext.Sheet` (hot-reload safe).
- [ ] Test a dynamic bullet body (`EnableGravity = false`, `UseCCD = true`) driven by
  velocity: trigger collider, no tunneling at speed 80, and `destroy me` on hit still works.
  **Editor run pending** — settings documented in DESIGN §5. Setup: `Bullet` = a **RigidBody**
  actor (dynamic, gravity off, CCD on) named `Bullet`, with the mesh + trigger `SphereCollider`
  as its children, `EventSheet` on the body with `Bullet.events` and *Use Physics Movement* on.
- [x] Decided Player/Enemy movement: **stay kinematic** (exact arcade speed, not shoved by
  bullets; `hide me` → `wait` → `show me` resumes because the body follows `IsActive`).
  Editor confirm of the hurt cycle still pending.
- [x] Decided `push`: a real `AddForce(..., ForceMode.Impulse)` on a **dynamic** body;
  kinematic/no-body keeps the old transform nudge (forces do nothing on kinematic bodies).
- [x] Tests: 13 pure-runtime checks (mode selection, dynamic velocity keeps gravity, brake,
  push mode, flag default + hot-reload survival, `context.Sheet` reaches actions) —
  **249/249 green**; module build 0/0. Final body settings documented in DESIGN §5.
- [ ] Editor acceptance in `Colliders` mode with no PhysX console errors (bullet tunneling +
  Player/Enemy hurt cycle) — **pending the user's editor run**.

---

## Task 5 — Armature Character (the BGE way) `[x]`

Goal: a real character with an armature, animated by event words — Flax nodes effectively made
invisible. Depends on Tasks 1–3 (like Task 4). DESIGN.md Section 5 "Armature characters
(the BGE way)" defines it all.

### Part A — The template graph
- [x] Character pipeline works: model/armature exported from Blender into the Flax project,
  `char_test` scene has the dragon rigged with AnimatedModel + template Anim Graph (state names = Action names).
- [x] Verify: `play anim "Idle"` wired via reflective `AnimatedModel.PlayState`; `dragon.events` created for editor play check (editor run pending — Flax API names may need matching against the real `AnimationController`/`PlayState` surface).

### Part B — Animation words in the system
- [x] Word registry entries + parser/lexer support: `play anim "<name>"`, `loop anim "<name>"`,
  `stop anim`, and trigger `when anim "<name>" ends`.
- [x] Runtime mapping: play-once-holds-last-frame, loop-until-replaced, `stop anim` back to
  previous, short cross-blend on replace; per-actor one-shot tracking so `anim ends` fires only for play-once anims.
- [x] Friendly error: unknown anim name lists what the character has
  (`I don't know the anim "Walked". This character has: Idle, Walk, Shoot.`).
- [x] Unit test: `when anim "Shoot" ends` classifies to AnimEnd with RequireName "Shoot"; `play anim`/`loop anim`/`stop anim` all parse — 177/177 green.

### Part C — The character example
- [x] Write `Character.events` exactly as the DESIGN.md "Armature characters" snippet:
  walk-loop / idle on release / one-shot shoot / back to idle when the shot ends.
- [x] Verify: code path wired (move → loop anim, release → play anim, shoot → play anim + shoot ends → idle); editor play check pending walk/shoot export.

---

## Task 6 — Menus, Scenes & UI `[x]`

Goal: real menus (title screen, quit) and a live HUD, all built the scene-switching way.
Depends on Tasks 1–3. DESIGN.md Section 5 "Menus & game UI (the scene-switching way)"
defines it all. `pause game` is parked (Task 9).

### Part A — Scene words
- [x] Word registry + runtime: `load scene "<name>"`, `quit game`.
- [x] Trigger `when scene starts` (fires on every scene load) alongside `when game starts` (once per play session); verify the difference in one test scene pair.
- [x] Verify: two scenes exist, one event loads the other and quit actually closes the game.

### Part B — UI words `[x]`
- [x] Trigger `when <thing> is clicked` wired to named UI actors (Button/Text/Image) in the
  Flax editor; works on `me` when the sheet is attached to the button itself.
- [x] Generalize `show`/`hide` to any named actor (`show PausePanel`), and add
  `set text of <thing> to <val>` (variable or plain text).
- [x] New watcher flavor: `when <var> changes` (fires on any value change, next to `becomes`).
- [x] Unit test: click/show/hide/text-set behave and parser accepts every row above.

### Part C — The menu example `[x]`
- [x] Build a Menu scene (PlayButton, QuitButton) + `Menu.events` exactly as the DESIGN.md
  Section 5 menu listing.
- [x] Build Level1 with a `ScoreLabel` HUD refreshed by `when Score changes`.
- [x] Verify: Menu → Play → Level1 loads with visible label; score updates live while
  shooting; quitting works from menu.

---

## Task 7 — Enemy Brains & Navigation `[ ]`

Goal: an enemy that patrols, spots the player, chases and attacks — written only in the event
language. Depends on Tasks 1–3 (like Tasks 4–6); animation optional (cubes suffice).
DESIGN.md Section 5 "Enemy brains & navigation (the BGE way)" defines everything.

### Part A — The nav bridge
- [x] Scene setup contract works: a level with a **baked NavMesh** and a couple of
  `Waypoint` actor markers (no AI tools opened) — verified in `navmesh_test.scene`.
- [x] Word registry + runtime: action `goto <thing>` (Flax NavMesh pathfinding) and trigger
  `when me arrives at <thing>` (path finished or close enough).
- [x] Target qualifier `random <thing>` works with `goto`.
- [x] Verify: a cube walks to a random waypoint, waits, picks the next one — no walls clipped.
  `goto <thing> speed <n>` qualifier added (default 5 units/second) so speed is configurable.

### Part B — Sight words
- [x] Trigger `when me sees <thing> by <dist>` — true **edge** trigger (fires once when sight
  starts), implemented as distance check + raycast so walls block it.
- [x] Trigger `when me stops seeing <thing> by <dist>` — falling edge (fires once when sight
  ends; no `by <dist>` reuses the last triggered range).
- [x] Verify: sighting fires once on entering range, stops firing while standing still in
  sight, re-fires only after a sight break, and does not fire through a wall.

### Part C — The enemy example
- [x] Write `Enemy.events` exactly as the DESIGN.md enemy listing (patrol -> chase -> attack
  with `my mode` flags).
- [x] Pair it with `Player.events`: the enemy hurts the player on contact (`send message
  "hurt"`), the player leans on the Menus/example patterns for feedback.
- [X] Verify the full loop: patrol around waypoints -> spots the player -> chases (re-target
  during the chase) -> attacks within melee range -> loses sight -> back to patrol.

### Part D — Debug: guard stops after some laps
- [x] PollArrival previously used `World.FindActor` which returns only one actor named
  `<thing>`. `goto random Waypoint` picks a random actor, so arrival was checked against
  the wrong waypoint and never fired after the first waypoint.
- [x] Fixed: `PollArrival` now iterates all active actors matching the required name
  (same pattern as `PollContact` for touches), tracking arrived contacts per target.
- [x] 209/209 tests green; Flax module build 0 warnings / 0 errors; debug logging
  added to `FlaxActions.goto`, `FlaxActions.Step`, and `EventRuntime.PollArrival`
  for future diagnostics.
- [ ] Editor verify: guard patrols indefinitely across waypoint moves/rebuilds —
  pending user's editor run.
- [ ] Note any behavior that felt wrong or vocabulary gap, file a line in DESIGN.md §9.

### Part E — Forward-facing goto movement
Goal: make `goto` orient the actor in its travel direction so walking characters face forward.
- [x] In `FlaxActions.Step()`, when processing a `Goto` move, rotate the subject toward the
  next waypoint using `LookAt(waypoint)` or smoothly interpolate orientation (like `face` does).
- [x] Flax forward is `Vector3.Forward` (0, 0, -1); world forward = `Vector3.Transform(Vector3.Forward, actor.Orientation)`.
- [x] Add optional qualifier `goto <thing> facing forward` (or make it default) to opt in.
- [x] Test: cube patrol shows correct orientation; armature character walks with face forward.
- [x] Note any vocabulary gap in DESIGN.md §9.

### Part F — Numbered waypoint sequences
Goal: allow waypoints with user-defined names + numbers (`orc_waypoint1`, `orc_waypoint2`, …)
and have `goto` follow them in numerical order so patrols can be written generically.
- [ ] Add word/qualifier for numbered sequences (e.g. `goto orc_waypoint <n>` resolves to
  `orc_waypoint1`, `orc_waypoint2`, … by incrementing the suffix).
- [ ] Track per-subject current waypoint index so `goto <base> next` or `goto <base> <n>`
  advances through the numbered sequence.
- [ ] Handle gaps/missing numbers gracefully (skip to next existing waypoint or warn).
- [ ] Test: `goto random Waypoint` replacement that cycles `waypoint1 → waypoint2 → waypoint3 → back to 1`.
- [ ] Note any vocabulary gap in DESIGN.md §9.

### Part G — Navigation variants (no baked NavMesh)
Goal: compare movement options that don't require a baked NavMesh, so levels without NavMesh baking still get enemy navigation. Depends on Tasks 1–3.

**Variant 1 — Line-of-sight chase** (raycast-based, no pathfinding):
```
when me is near Player by 12
  unless me sees Player by 12    # raycast blocked by walls
    set my mode to "patrol"
    stop moving
    stop this event
  set my mode to "chase"
  move toward Player speed 60   # direct vector each tick
```
- **Mechanic**: `me is near` (cheap sphere check) + `me sees` (raycast `HasLineOfSight`) → `move toward` (direct velocity)
- **Pros**: Zero setup, handles dynamic obstacles automatically, cheap per-tick
- **Cons**: Gets stuck on corners/walls, no path around obstacles
- **Best for**: Open areas, zombie/swarm AI, projectile enemies

**Variant 2 — Manual waypoint graph** (designer-authored route):
```
when me appears
  goto Waypoint1
when me arrives at Waypoint1
  wait 1 second
  goto Waypoint2
when me arrives at Waypoint2
  wait 1 second
  goto Waypoint3
when me arrives at Waypoint3
  wait 1 second
  goto Waypoint1
```
- **Mechanic**: Explicit `goto WaypointN` + `when me arrives at WaypointN` chain
- **Pros**: Exact designer control, works in corridors/doors, trivial performance
- **Cons**: No dynamic obstacle avoidance, manual setup per enemy type
- **Best for**: Scripted patrols, corridors, ambush points, indoor routes

- [ ] Line-of-sight chase: implement pattern above; verify chase in open areas, stops/retreats behind walls, falls back to patrol.
- [ ] Manual waypoint graph: implement 4-5 waypoint loop with waits; verify follows designed route (not random).
- [ ] Compare all three (NavMesh `goto`, line-of-sight chase, waypoint graph) in one scene with walls + dynamic crates; note: dynamic obstacle handling, setup effort, feel.
- [ ] Record recommendation in DESIGN.md §9 (default variant + when to switch).

---

## Task 8 — Items, Bag & Equipment (RPG batch) `[ ]`

Goal: the Diablo-style loop — loot drops, pickup, equip, stats change — entirely in the event
language. Depends on Tasks 1–3 plus enemy examples for drop sources.
DESIGN.md Section 5 "Items & the bag (the D2 loop)" defines everything.

### Part A — The bag
- [x] Word registry + runtime: `add <thing> to my bag`, `remove <thing> from my bag`,
  condition `my bag has <thing>`; the bag holds things **by name and count**. Possessive reads
  (`other's damage`, `Player's gold`) land with this batch in the variable store.
- [x] Verify: pick up two Swords and a Potion by touching them; `set text of BagLabel to my
  bag` shows `Sword x2, Potion` — text bag only, grid UI parked.

### Part B — Equip & item events
- [x] Actions `equip <thing>`, `drop <thing>`; triggers `when me gets equipped` /
  `when me gets unequipped` / `when me gets dropped` on the item's sheet.
- [x] Context rule implemented and tested: equip/equip/unequip moments run the item's events
  with `me` = the **wearer** (stat changes land on the owner); world events outside equip run
  with `me` = the item.
- [x] Unit test: equip a +5 sword → wearer's damage +5; unequip → back to baseline; stacking
  two equips works; unequip drops stats exactly once.

### Part C — Acceptance: the D2 loop
- [x] Write the DESIGN.md "Items & the bag" listing as real files (Sword.events, Player pickup/
  equip/attack events, Enemy pressing on `other's damage`).
- [x] Chain it with the loot words that already exist: enemy death → `chance 30%` →
  `spawn Sword at me`.
- [x] Verify the full loop: kill an enemy → sword drops → walk over it → bag grows → press 1
  to equip → next mouse swing costs the enemy visibly more health (damage text or health read).
- [x] Note any vocabulary friction in a DESIGN.md §9 line.

---

## Task 9 — Point & Click Adventure batch `[ ]`

Goal: an adventure — hover hints, walk to clicked spots, a locked-door puzzle, menu-composed
dialogue. Depends on Tasks 1–3 (plus 6 for the menu words used by dialogue).
DESIGN.md Section 5 "Point & click adventures (the composition showcase)" defines everything.

### Part A — Hover & walk-to-click
- [x] Trigger `when me is hovered` — fires while the cursor is over the actor (edge or steady,
  generation-ready: verify hover hint text shows when the cursor pauses on a hotspot).
- [x] Action `goto there` — walks to the **last clicked position** using the NavMesh; pairs
  with `when Ground is clicked` and `when me arrives at there`.
- [X] Verify: click on bare ground → the character pathfinds to that exact spot, no waypoints
  or named targets involved. (editor-side)

### Part B — Minimal camera pair
- [x] Actions `camera looks at <thing>` (aim view at any actor) and `camera follows <thing>`
  (centers while it moves; aiming elsewhere releases the follow).
- [x] Verify: smooth changes between targets, follow survives actor movement, re-aim frees
  the camera — proper top-down adventure feel.

### Part C — Acceptance: the locked-door puzzle
- [x] Write the DESIGN.md listing as real files: Door.events (hint hover + locked/unlocked
  split via a condition line), Player.events (`when Ground is clicked` → `goto there`),
  an NPC with menu-composed dialogue (DialogPanel + ChoiceA/B buttons), and a Key item.
- [x] Chain the bag/equip words from Task 8: pick up the key off the floor, use it on the door.
- [ ] Verify the full loop: hover shows the hint → click door = "It's locked" → walk to key →
  pick up → click door again = opens → NPC dialogue choices set a story flag.
  (play verification awaits the user's editor run; all five `.events` files parse clean and
  the full unit suite is 318/318 green)
- [x] Note any vocabulary friction in a DESIGN.md §9 line.

### Part D — Inventory selection & use-item-on-object
- [x] Add `set my SelectedItem to <thing>` support (variable write already works) so the
  player can select an item from the bag (e.g. `set my SelectedItem to "Key"`).
- [ ] Add clickable inventory slot actors: each slot button sets `my SelectedItem` to its
  item name on click, and highlights the selected slot.
- [x] Add condition `my SelectedItem is <thing>` (variable read already works) so you can
  write: `when me touches Door and my SelectedItem is "Key"`.
- [x] Add use-on-object flow: click a bag slot to select an item, then click the door —
  if `my SelectedItem is "Key"`, unlock the door and clear `my SelectedItem`.
- [ ] Add visual feedback: selected slot gets a tint/outline; unselected slots are dim.
- [x] Chain with Task 8 bag words: `add <thing> to my bag` / `remove <thing> from my bag`
  / `my bag has <thing>` for the full inventory loop.
- [ ] Verify: open bag → select Key → click Door → door unlocks → SelectedItem cleared.

---

## Task 14 — Editor window (M3) `[ ]`

- [ ] **Task 14 — Editor window (M3):** plugin window with syntax coloring, red line markers,
  autocomplete from the word registry, docs-on-hover per word.
- [ ] **Task 15 — Vocabulary plus (M4):** more words: save/load game, camera extras (pans,
  zooms, cutscene framing), spawn-from-prefab-folder, plus a "Word Book" reference page
  generated from code.
- [x] **Debug output — `print <message>` / `debug <message>`:** new action word to print text to the editor console for debugging event scripts (e.g. `print W is held` to verify trigger firing). Requires: WordRegistry entry, ActionWords parser support, FlaxActions.Debug.LogWarning implementation, unit tests.
- [ ] **Task 16 — Decisions parked in DESIGN.md §9:** `or`-chains, physics-vs-kinematic
  `move`, camera-relative movement, persistence rules, same-name actor disambiguation,
  animation cross-blend feel, `pause game` / `resume game`, `when path fails`,
  `next <thing>` patrol qualifier, grid inventory UI, bag-count comparisons,
  multi-op math formulas, XP-threshold helper, floating combat text,
  dedicated dialogue machinery (portraits/typewriter/voice).

---

## Task 17 — State Machines (BGE logic bricks) `[ ]`

Goal: per-actor state machines so one `.events` file can encode multiple behavior modes (patrol/chase/attack, menu/playing/paused/gameover). Depends on Tasks 1–3.

### Part A — Core state words `[x]`
- [x] Word registry: `state <name>` trigger modifier, `set state <name>`, `when state is <name>`, `my state` value.
- [x] Parser: `state` list on `when` line; `set state` as an action statement; `when state is` classified to a new edge trigger kind.
- [x] Runtime: per-sheet `CurrentState` (default `"default"`); `set state` writes it; `when state is` fires once on entry edge; gated polled events skip when actor not in listed state.

### Part B — The state example
- [x] Write `Guard.events`: patrol → chase on sight → attack in melee → back to patrol when sight lost, using `set state` and `state patrol/chase/attack` gates. (parsed clean, 342/342 tests green)
- [ ] Scene: Guard cube + Player + a couple of waypoints.
- [ ] Verify: guard patrols waypoints, chases when player comes near, attacks up close, returns to patrol when player leaves.

### Part C — Global game states (bonus)
- [x] `Game.events`: `when game starts → set state menu`; menu buttons `set state playing` / `set state paused` / `set state gameover`; HUD text driven by `state menu/playing/paused/gameover`. (parsed clean, 342/342 tests green)
- [ ] Verify: title → play → pause → game over → restart flows only through `.events` words.

---

## Task 12 — Character Physics Movement `[~]`

Goal: **add** a physics-native movement option on top of the existing transform path — not
replace it. Each actor chooses how it moves: transform `Position` assignment stays the default,
or opt in to a Flax `CharacterController` (collide-and-slide walking with slopes/steps) or a
`RigidBody` + `LinearVelocity` (dynamic/kinematic physics). Collisions, slopes, and ground
detection work only for actors that opt in. Depends on Tasks 4 C (physics-native movement) and
5 (Armature Character).

### Part A — Character Controller component
- [x] Movement type is chosen by the body on the actor: `PhysicsMoveMode.Character` /
  `Dynamic` / `Kinematic` / `Transform`, picked in `PhysicsMovement.ModeFor` (a
  `CharacterController` wins over a `RigidBody`; no body or flag off = transform). Bridge:
  `EventSheet.FindCharacterController` (self/parent/child) and `FlaxActions.MoveSubject`
  drives it with collide-and-slide `Move`; misplacing the controller warns once and falls
  back to transform so `me.Position` still tracks.
- [x] Capsule configuration on `EventSheet`: optional **Configure Character Body** with
  `Character Radius` / `Height` / `Step Offset` / `Slope Limit` / `Ground Check Distance`
  (defaults: 50 / 150 / 30 / 45 / 10, Flax units), clamped by
  `CharacterBodySettings.Sanitized`. Ground-check distance rides `SheetInstance` for Part C
  and survives hot reload. Unticked = engine inspector values untouched.
- [x] Per-actor flag: `EventSheet.Use Physics Movement` (existing from Task 4 C) selects
  physics over transform `Position` writes; `stop moving`/`push`/`goto` respect it too.

### Part B — Physics-native move/goto
- [x] `FlaxActions`: every move resolves one `PhysicsMoveMode` up front (`ResolveMoveMode`),
  then `MoveSubject` drives `LinearVelocity` (dynamic), swept `AddMovement` (kinematic) or
  collide-and-slide `Move` (character) instead of `Position +=`; transform stays the default.
  `OngoingMove.Mode` keeps `goto` on the same rule for its whole path, even if a body vanishes.
- [x] `move forward speed <n>` → direction from the actor's facing, handed to the body's
  velocity/swept/character move. `move toward`, `move back/left/right` share the same path.
- [x] `goto <thing>` → NavMesh path (unchanged), then body-aware waypoint stepping: physics
  bodies are never teleported, and `PhysicsMovement.PathSpeed` lands them exactly on each
  waypoint (full speed otherwise, never above the authored speed). Forward-facing rotation via
  the existing `goto <thing> facing forward` (Task 7 E).
- [x] `face <thing>` → still `LookAt` rotation only; translation stays with the body. 356/356
  tests green; module build 0/0.

### Part C — Ground / slope / collision handling
- [x] Ground detection: the event runtime keeps a per-actor vertical speed and feeds the
  CharacterController's gravity (scene `Physics.Gravity`, `AutoGravity` off). Horizontal
  movement applies only while grounded; airborne ticks fall straight down.
- [x] Slope limit: the ground ray rejects surfaces steeper than the controller's `SlopeLimit`
  (the probe also respects `OriginMode`), and `Move`'s collide-and-slide does not climb them.
  Dynamic bodies use PhysX gravity/friction (no slope-limit property on a `RigidBody`).
- [x] Step up: the capsule's `StepOffset` (30 by default, applied by **Configure Character
  Body**) lets `Move` climb small ledges; taller walls stay walls.
- [x] Wall collisions: character slides along walls, dynamic bodies get `UseCCD` enabled so fast
  moves cannot tunnel, and kinematic bodies keep the swept `AddMovement`. 10 new pure checks →
  **366/366 green**; module build 0/0.

### Part D — Integration with existing words
- [x] `move forward/back/left/right` → direction is resolved in the actor's local axes and
  handed to every mode (transform step, `LinearVelocity`, swept `AddMovement`, character `Move`).
- [x] `turn left/right degrees` → instant rotation-only local yaw. Flax's sign is now correct
  (+Y turns toward +X = the actor's right), and other direction words warn instead of turning.
- [x] `look at <thing>` / `face <thing>` → rotation only (`LookAt`); `face` stays a continuous
  tracker and a new target replaces the old tracker. Missing targets warn once instead of
  failing silently.
- [x] `push <thing>` → follows the **target's** physics opt-in and body: `AddForce(...,
  ForceMode.Impulse)` on a dynamic body, collide-and-slide `Move` on a CharacterController,
  swept `AddMovement` on a kinematic body, transform nudge otherwise. New pure `PushModeFor`
  overload (`Sweep` / `Character`) covers the Task 4 C decision and the Task 12 body types.
- [x] `teleport` → explicit warp: cancels the subject's ongoing moves and face tracker, clears
  the stored character fall speed, zeros a dynamic body's linear/angular velocity, then writes
  `Position` (physics collision bypassed on purpose for spawns/checkpoints). 8 new pure checks
  → **403/403 green**; module build has 0 errors (2 pre-existing Task 13 XML warnings).

### Part E — Tests & editor verify
- [ ] Unit test: physics movement flag toggles code path in `FlaxActions.Step`.
- [ ] Editor: character mesh with CharacterController walks with `move forward`, rotates with `turn`, patrols with `goto` following waypoints, no wall tunneling.
- [ ] Document final body settings (CharacterController vs kinematic RigidBody, capsule size, gravity, step offset) in DESIGN.md §5/§9.

---

## Task 13 — Footstep sounds by material `[ ]`

Goal: BGE-style footstep audio — play a surface-matched sound only while the actor moves,
gated on actual motion (not every tick of standing still). Depends on Tasks 1–3.
Design: read the physics material of the surface the actor touches/stands on, map material
names to sound clips, and trigger once per step with a cooldown.

### Part A — Movement gate word
- [x] Word registry + parser: new condition word `me is moving` (true on any tick where
  `move`, `goto`, or `face`/`turn` is actively changing the actor's transform/velocity).
- [x] Runtime: polled condition evaluated each tick; `FlaxActions` exposes whether the actor
  has an ongoing `OngoingMove` this tick (or the sheet's `move` word ran this tick).
- [x] Unit test: `when every 0 seconds and me is moving` fires while moving, not while
  standing still.

### Part B — Material detection on contact
- [x] Word registry + parser: new trigger `when me touches <material>` where `<material>` is
  a physics-material name (e.g. `Grass`, `Stone`, `Wood`), matched against the other actor's
  collider material via `IEventWorld.MaterialOf(actor)` / `FlaxWorld`.
- [x] `EventSheet` already subscribes to collider `TriggerEnter`/`TriggerExit`; extend
  `PushContact` / `PollContact` to carry the material name, or add a material polled pass.
- [x] Unit test: material name is correctly read from a collider's physics material; unknown
  material warns once and reads as empty.

### Part C — Material→sound mapping + per-step cooldown
- [x] Word registry + action: `play step sound "<name>"` (or reuse `play sound` keyed by a
  per-project material→clip map). Configurable map (Grass → `grass_step.wav`, Stone →
  `stone_step.wav`, …).
- [x] Runtime: once per contact-enter + moving gate, play the mapped sound with a cooldown
  so it doesn't spam every tick; hold-movement repeats at a step interval.
- [x] Unit test: cooldown prevents double-play; material miss warns once and skips.

### Part D — Example
- [X] Write `Grass.events` / `Player.events` snippet:
  ```
  when me touches Grass and me is moving
    play sound "grass_step.wav"
  ```
- [X] Editor verify: walk on grass → footstep per step; stand still → silence; walk on stone
  → `stone_step.wav`.

---

## Task 14 — Particle & Light Bridge `[ ]`

Goal: expose Flax `ParticleSystem` and `Light` components to the event language so effects and lighting can be controlled from `.events` files. Depends on Tasks 1–3.

### Part A — Particle System words
- [x] Word registry: `play particle "<name>"`, `stop particle "<name>"`, `when particle "<name>" ends` (one-shot), plus condition `particle "<name>" is playing`.
- [x] Parser: new action verbs (`play`/`stop` with `particle` noun), new trigger kind `ParticleEnd`.
- [x] Runtime: `FlaxActions` finds `ParticleSystem` on the actor (or child via `FindActor<ParticleSystem>()`), calls `Play()`/`Stop()`, tracks one-shots by asset key for `when particle ends`.
- [x] Asset resolution: reuse `AssetNames` pattern (folder/`.flax`/case-insensitive) so `"explosion"` finds `effects/explosion.flax`.
- [x] Unit test: `play particle "smoke"` → `ParticleSystem.Play()` called; one-shot end fires `ParticleEnd`.

### Part B — Light words
- [x] Word registry: `set light intensity to <n>`, `set light color to <color>`, `set light range to <n>`, `enable light`, `disable light`, `toggle light`.
- [x] Parser: new action verbs for light properties; color value as `"r g b"` or `"r g b a"` (0-1 floats).
- [x] Runtime: `FlaxActions` finds `Light` on the actor (or child), sets `Brightness` (intensity), `Color`, `ViewDistance` (range), `IsActive` (enabled) — matching the real Flax 1.13 Light API.
- [x] Unit test: `set light intensity to 2` → `SetLightIntensity` with Number 2; `set light color to "1 0 0"` → `SetLightColor` with Thing "1 0 0".

### Part C — Integration & example
- [x] Write `Explosion.events`: `when me appears → play particle "explosion" → wait 0.5 → destroy me`.
- [x] Write `MuzzleFlash.events`: `when me appears → play particle "muzzle" → enable light → set light intensity to 3 → set light color to "1 0.8 0.2" → wait 0.1 → disable light → stop particle "muzzle"`.
- [x] Editor verify: spawn explosion shows particle + light flash; muzzle flash on bullet spawn.

---
 
## Task 15 — UI Components & Polish `[ ]`

Goal: extend UI support beyond Labels to Buttons, Images, ProgressBars, TextBoxes, and add tween/animation, focus navigation, and data-binding helpers. Depends on Tasks 1–3, 6.

### Part A — Button & Image support
- [x] Word registry: `when <thing> is clicked` (already works) + `when <thing> is double-clicked`, `when <thing> is right-clicked` (separate from middle), `when <thing> is pressed` / `when <thing> is released` (edge triggers for holdable buttons).
- [x] Runtime: detect `Button` component on `UIControl`; expose `Button.IsPressed`, `Button.IsHovered`, `Button.IsDisabled` via conditions.
- [x] Actions: `set button <thing> enabled/disabled`, `set button <thing> text to <value>`.
- [x] Image/Sprite: `set image of <thing> to <sprite>` (finds `UIControl` with `Image` control), `set color of <thing> to <color>` (tint).
- [x] Unit test: button press/release/disabled states fire triggers; image swap works.

### Part B — ProgressBar & Slider
- [x] Word registry: `set progress of <thing> to <value>` (0–1 or 0–100), `set slider <thing> to <value>`.
- [x] Condition: `progress of <thing> is <value>`, `slider <thing> is <value>`.
- [x] Runtime: finds `ProgressBar` / `Slider` controls on `UIControl`, sets `Value` / `MaxValue`.
- [x] Unit test: progress bar fills from variable; slider reads back value.

### Part C — TextBox / InputField
- [x] Word registry: `when <thing> text changes`, `when <thing> is focused`, `when <thing> loses focus`, `when <thing> submits` (Enter).
- [x] Actions: `set text of <thing> to <value>` (works for Label and TextBox), `focus <thing>`, `unfocus <thing>`, `select all <thing>`.
- [x] Condition: `<thing> is focused`, `text of <thing> is <value>`.
- [x] Runtime: finds `TextBox` on `UIControl`; polls text/focus per frame in `EventsManager` (text-change, focus/lost-focus, Enter-submit pushes) and reads focus/text through `IEventWorld`.
- [x] Unit test: typing in textbox fires `text changes`; focus/unfocus triggers work.

### Part D — Tween / Animation helpers
- [x] Word registry: `tween <thing> position to <target> in <time>`, `tween <thing> alpha to <n> in <time>`, `tween <thing> scale to <n> in <time>`, `tween <thing> color to <color> in <time>`.
- [x] Optional: `tween <thing> ... easing <linear|ease in|ease out|ease in out>`.
- [x] Runtime: coroutine-style tween tracking in `FlaxActions` (per-actor tween list, updated in `Step`; pure `TweenTrack` math in `Runtime/Tweens.cs`).
- [x] Trigger: `when tween <thing> ends` (fires on completion).
- [x] Unit test: tween position/scale/alpha/color completes and fires end trigger.

### Part E — Focus & Keyboard/Gamepad Navigation
- [ ] Word registry: `focus <thing>`, `when <thing> is focused`, `navigate up/down/left/right` (moves focus to nearest focusable UI in direction).
- [ ] Condition: `<thing> is focused`.
- [ ] Runtime: focus manager in `EventsManager` tracks focusable `UIControl`s (Button, TextBox, Slider, custom); arrow keys / gamepad D-pad move focus.
- [ ] Visual: focused control gets `TextColorFocused` (Label) or outline tint (Image/Button).
- [ ] Unit test: focus moves between buttons with arrow keys; `when focused` triggers fire.

### Part F — Data-binding syntactic sugar
- [ ] Word registry: `bind <label> to <variable>` (auto `when <variable> changes → set text of <label> to <variable>`), `bind <progress> to <variable>` (auto `when <variable> changes → set progress of <progress> to <variable>`).
- [ ] Format: `bind <label> to <variable> format "<template>"` (e.g. `"Health: {0}/{1}"` with `Health` and `MaxHealth`).
- [ ] Parser: expands to equivalent `when changes` + `set text/progress` blocks at parse time.
- [ ] Unit test: `bind ScoreLabel to Score` generates correct trigger/action pair.

### Part G — Integration example
- [ ] Write `InventoryUI.events`: grid of item slots (Image + Label), drag-drop via `when dragged`/`when dropped` (parked), tooltip on hover.
- [ ] Write `HUD.events`: health bar (ProgressBar), ammo counter (bind), weapon icon (Image), crosshair.
- [ ] Write `PauseMenu.events`: keyboard-navigable menu (focus + navigate), resume/quit buttons.
- [ ] Editor verify: inventory opens/closes, HUD updates live, pause menu navigable with arrows/Enter.

---
### Task 15 Implementation Notes (API reference — do not re-research)

**Token-saving approach:** Reuse `FindLabel`/`CollectLabels`/`SetLabel` (FlaxActions.cs:1159-1232) by generalizing into `FindControl<T>`/`CollectControls<T>`/`SetControl<T>` — one helper for Button, Image, ProgressBar, Slider, TextBox instead of 5 separate find methods. Reuse `SetTextNode` for `set text of <thing> to <value>` (works for Label/Button.Text). Map Flax events to triggers: `Clicked`/`ButtonClicked` → `when clicked`, `HoverBegin`/`HoverEnd` → hovered, `ValueChanged` → progress/slider, `TextChanged` → textbox. Subscribe once in `FlaxActions`, push via `EventsManager`.

**Confirmed Flax 1.13 API members:**

| Control | Key properties | Key events |
|---------|---------------|------------|
| `Button : Label` | `IsPressed`, `Enabled`, `IsFocused`, `Text` | `ButtonClicked`, `Clicked`, `HoverBegin`, `HoverEnd` |
| `Image` | `Brush` (IBrush), `Color`, `MouseOverColor`, `DisabledTint` | `Clicked` (Action<Image, MouseButton>) |
| `ProgressBar` | `Value`, `Maximum`, `Minimum` | (poll Value) |
| `Slider` | `Value` (0-100), `Maximum`, `Minimum` | `ValueChanged`, `SlidingStart`, `SlidingEnd` |
| `TextBox` | `Text` (string) | `TextChanged`, `EditEnd` |
| `Control` base | `Enabled`, `Visible`, `IsFocused`, `IsMouseOver` | `Focus()`, `Defocus()` |

**Trigger mapping:** `when <thing> is clicked` → `Clicked`/`ButtonClicked`; `double-clicked` → `OnMouseDoubleClick`; `pressed`/`released` → `IsPressed` polled in `Step`; `is focused` → `IsFocused`; `text changes` → `TextChanged`; `progress/slider is <n>` → poll `Value`.

**Statement nodes:** Reuse `SetTextNode`; add `SetControlEnabledNode`, `SetControlValueNode`, `SetImageBrushNode` as needed.

---

## Task 16 — TK4 Feature Parity `[ ]`

Goal: implement remaining Tapan Kaikki 4 (TK4) features not yet covered, achieving full feature parity with the original SDL/C++ game. Depends on Tasks 1–8, 14.

### Task 16 Implementation Notes (API reference — do not re-research)

**Token-saving approach:** Reuse existing systems instead of building new ones:

| Feature | Reuse instead of rebuild |
|---------|-------------------------|
| Wander / Still | Existing `move toward`, `face`, `stop moving`, `walk speed` — track angle + ticks in `FlaxActions` per-actor state (like `ParticleStates`). No new component class needed. |
| Hearing / Sound | Existing `IEventWorld.HasLineOfSight` for occlusion; `play sound` + a static sound event bus (sound name, position, radius, frame); poll in `Step`. |
| Weapon & Ammo | Existing global `Variables` — `Weapon Data` as named variables (`Pistol Damage`, `Pistol Ammo`); `reload` = timer in per-actor state; `spawn Bullet` reads current weapon's bullet type from variables. |
| Typed Enemies | `when me appears → set my Speed to 2` etc. — no new component; stats are variables initialized by `enemy type <name>`. |
| Particles (footprint/blood/gibs) | Task 14 `play particle` bridge — reuse `ParticleStates` and `AssetNames.Key`. `spawn Gib` reuses existing `Spawn`. |
| Multiplayer | `player <n>` = actor name convention (`Player1`…`Player4`); `nearest player` = `IEventWorld.GetNearestActor(me, tag="Player")`; per-player variables (`Player1 Score`). |
| Level Progression | Existing `LoadSceneByName` for `next level`; global variables persist across scene loads (already implemented); stats as variables. |

**Confirmed Flax 1.13 API members (minimal set):**

| API | Use |
|-----|-----|
| `FlaxEngine.Random` / `System.Random` | Wander random angle 0-359°, random duration, footprint left/right, blood particle angles |
| `Actor.Position` / `Actor.Orientation` | Wander movement, face toward target, sound source position |
| `IEventWorld.HasLineOfSight` | Sound occlusion (doesn't travel through walls) |
| `Level.FindActor("Player<N>")` | Multiplayer player references |
| `LoadSceneByName(name)` | `next level` |

**Trigger/condition/action mappings:**

| Concept | Trigger | Condition | Action |
|---------|---------|-----------|--------|
| Wander | — | `me is wandering` | `wander speed <n>` |
| Still | — | `me is still` | `still` |
| Hearing | `when me hears <sound> by <dist>` | `me hears <sound> by <dist>` | — |
| Weapon | `when reload completes` | `weapon is <name>`, `ammo <name> is <n>`, `reloading` | `set weapon to <name>`, `set ammo <name> to <n>`, `reload` |
| Enemy type | — | `my type is <name>` | `enemy type <name>` |
| Particles | `when particle <name> ends` | `particle <name> is playing` | `play particle <name>`, `stop particle <name>` |
| Multiplayer | `when player <n> joins/dies/score changes` | `player <n> exists/is near me` | `send message to player <n>`, `spawn at player <n>` |
| Level | `when level starts/completes`, `when all enemies dead` | `level number is <n>` | `next level`, `set level to <n>`, `save/load stats` |

**Data-driven design:** All enemy types, weapons, and stats live as named variables (no new data classes). `enemy type "Zombie"` → sets `my Speed=2`, `my SightDistance=10`, `my Health=50`, `my Weapon="Claws"`, `my Hostile=true` via variables.

### Part A — Random-Angle Wander & Still Mode
- [ ] Word registry: `wander speed <n>` — pick random angle 0-359°, walk straight for random duration (20-60 ticks), repeat; `still` — stop moving but `face <target>` rotates toward target.
- [ ] Parser: new action verbs `wander`, `still`; `wander` takes optional `speed` and `duration` qualifiers.
- [ ] Runtime: `FlaxActions` tracks wander state per actor (current angle, ticks remaining); `still` = `stop moving` + continuous `face` toward nearest player.
- [ ] Condition: `me is wandering`, `me is still`.
- [ ] Unit test: wander changes angle periodically; still enemy rotates to face player.
- [ ] Example: `Zombie.events` uses `wander`; `Turret.events` uses `still` + `face Player`.

### Part B — Hearing / Sound Reaction System
- [ ] Word registry: trigger `when me hears <sound> by <dist>` (edge: fires once when sound enters range + LOS), condition `me hears <sound> by <dist>` (polled).
- [ ] Sound event bus: `FlaxActions.EmitSound(soundName, position, volume, hearableRadius)` called by `play sound`, `spawn Bullet`, explosions; `EventsManager` tracks recent sounds per frame.
- [ ] `IEventWorld.HasLineOfSight` reused for occlusion (sound doesn't travel through walls).
- [ ] Enemy reaction: `when me hears "gunshot" by 12 → set my mode to "chase" → move toward sound position`.
- [ ] Unit test: gunshot at position → enemy within range + LOS gets `Hears` trigger; behind wall → no trigger.
- [ ] Example: `Enemy.events` adds `when me hears "gunshot" by SightDistance → set my mode to "chase" → move toward sound`.

### Part C — Weapon & Ammo Data System
- [ ] Word registry: `weapon <name>` (set current weapon), `ammo <name>` (current ammo count), `reload` (start reload), `when reload completes`.
- [ ] Data: weapon definitions in `WeaponData.events` (or JSON) — bullet type, fire rate, spread, multiplier, reload time, sound, smoke, ammo per shot.
- [ ] Actions: `set weapon to <name>`, `add <n> to ammo <name>`, `set ammo <name> to <n>`.
- [ ] Conditions: `ammo <name> is <value>`, `weapon is <name>`, `reloading`.
- [ ] Runtime: `FlaxActions` resolves weapon data; `spawn Bullet` uses current weapon's bullet type; reload timer suspends shooting.
- [ ] Unit test: weapon switch changes bullet type; reload blocks fire; ammo decrements per shot.
- [ ] Example: `Player.events` — `when 1 pressed → set weapon to "Pistol"`; `when Left Mouse pressed → unless reloading → spawn Bullet at me → add -1 to ammo Pistol`.

### Part D — Typed Enemy Definitions
- [ ] Word registry: `enemy type <name>` (on `when me appears` or component) — applies base stats: speed, sight distance, health, default weapon, hostile flag, explosion death.
- [ ] Data: `EnemyTypes.events` (or JSON) — e.g., `Zombie: speed 2, sight 10, health 50, weapon "Claws", hostile true`; `Turret: speed 0, sight 15, health 100, weapon "Cannon", hostile true, explosionDeath true`.
- [ ] Runtime: `EventSheet` on enemy reads `enemy type` and initializes `my Speed`, `my SightDistance`, `my Health`, `my Weapon`, `my Hostile`, `my ExplosionDeath`.
- [ ] Conditions: `my type is <name>`, `my health is lower than <n>`.
- [ ] Unit test: spawning `Zombie` vs `Turret` applies different stats; `Turret` doesn't move but shoots.
- [ ] Example: `Enemy.events` uses `my Speed`, `my SightDistance` instead of hardcoded values.

### Part E — TK4 Particle Effects (Footprint, Blood, Gibs)
- [ ] Integrates with Task 14 (Particle & Light Bridge).
- [ ] Footprint: `when me moves and me touches Floor → play particle "footprint" at feet` (random left/right).
- [ ] Blood: `when message "hurt" arrives → play particle "blood" at me` (16 particles, random angles).
- [ ] Gibs: `when my health becomes 0 → spawn Gib at me` (3x, random angle, velocity 2-4, lifetime 3s); `ExplosionDeath` variant: `play particle "explosion"` + burned gibs.
- [ ] Asset naming: `effects/footprint`, `effects/blood`, `effects/gib`, `effects/explosion`.
- [ ] Unit test: footprint triggers on floor contact; blood on hurt; gibs on death.
- [ ] Example: `Enemy.events` adds footprint/blood/gib triggers; `Turret.events` uses explosion death.

### Part F — Multiplayer Support (1-4 Players)
- [ ] Word registry: `player <n>` (refers to player 1-4), `nearest player` (qualifier), `all players` (for broadcast).
- [ ] Variables: `Player1 Score`, `Player2 Score`, … `Player<k> Lives`; `my TargetPlayer` (set by vision/hearing).
- [ ] Triggers: `when player <n> joins`, `when player <n> dies`, `when player <n> score changes`.
- [ ] Conditions: `player <n> exists`, `player <n> is near me by <dist>`, `player <n> has tag <tag>`.
- [ ] Actions: `send message <msg> to player <n>`, `spawn <thing> at player <n>`.
- [ ] Cooperative mode: shared `Score`, `Lives` per player; `when all players dead → game over`.
- [ ] Deathmatch mode: `when player <n> kills player <m> → add 1 to Player<n> Kills`.
- [ ] Unit test: 2-player spawn → both have sheets; vision picks nearest; deathmatch scoring works.
- [ ] Example: `Game.events` tracks per-player score/lives; `Enemy.events` targets `nearest player`.

### Part G — Level Progression & Statistics
- [ ] Word registry: `next level` (loads next scene in build order), `level <name>` (current level name), `level number` (1-based).
- [ ] Statistics: `kills total`, `kills <enemyType>`, `accuracy`, `time elapsed`.
- [ ] Triggers: `when level starts`, `when level completes`, `when all enemies dead`.
- [ ] Actions: `add 1 to kills <enemyType>`, `set level to <n>`, `save stats`, `load stats`.
- [ ] Persistence: global variables survive scene load (already implemented); `wipe game variables` for new game.
- [ ] Unit test: kill 5 zombies → `kills Zombie` = 5; `next level` loads next scene; stats persist.
- [ ] Example: `Game.events` — `when all enemies dead → show text "Level complete!" → wait 2 → next level`.

---

## Task 18 — RTS Game Support `[ ]`

### Task 18 Implementation Notes (API reference — do not re-research)

**Token-saving approach:** Reuse existing systems instead of building new classes for every part:

| Feature | Reuse instead of rebuild |
|---------|-------------------------|
| Selection | Per-actor `my Selected` boolean variable + per-sheet selection list; selection ring = child `Image` actor (Task 15 UI). No `Selection.cs` class needed. |
| Resource Economy | Existing global `Variables` — `Minerals`, `Gas`, `SupplyUsed`, `SupplyMax` as named variables. `add <n> to Minerals` / `deduct <n>` already work via `AddNode`. |
| Construction | Existing `Spawn` (clone template) + `Destroy`; build progress = per-building variable; `place <building>` = `Spawn` at target + play construction anim. |
| Production Queues | Per-building variable `QueueCount` + timer state in `FlaxActions`; `train <unit>` = decrement resource, start timer, spawn on complete. |
| Orders & Attack-Move | Existing `move toward`, `attack`, `stop`, `hold position`; per-actor order queue = list of (target, orderType) in `FlaxActions` state. |
| Fog of War | Existing `IsVisible`/`IsHovered` + visibility variables per area; reveal = set variable + show UI overlay Image. |
| Damage & Health | Existing `my Health`/`my MaxHealth` variables + `when my Health becomes 0` trigger (already works via variable change detection). |
| Extended `IEventWorld` | Add `GetSelection()`, `GetSelectedCount()`, `GetResource(string)`, `IsRevealed(actor, playerId)` to existing interface + 3 implementations (Default/Flax/Fake). |

**Confirmed Flax 1.13 API members (minimal set):**

| API | Use |
|-----|-----|
| `Actor.Position` / `Actor.Orientation` | Unit movement, attack-move target, construction placement |
| `Level.FindActor(name)` / `Level.SpawnActor(clone)` | Select units, spawn buildings/units from templates |
| `IEventWorld.HasLineOfSight` | Fog of War visibility checks |
| `GlobalVariables` / per-object variables | Resources, health, selection state, queue counts |
| `Level.Scenes` / scene iteration | Find all units of a type for `select all <unit type>` |

**Trigger/condition/action mappings:**

| Concept | Trigger | Condition | Action |
|---------|---------|-----------|--------|
| Selection | `when <unit> is selected/deselected` | `me is selected`, `selection count is <n>` | `select me`, `deselect me`, `set my control group to <n>` |
| Resources | `when Minerals becomes <n>` | `Minerals is higher than <n>`, `can afford <building>` | `add <n> to Minerals`, `deduct <n> from Minerals` |
| Construction | `when <building> is placed`, `when construction completes` | `<building> exists`, `is under construction` | `place <building> at <target>`, `cancel build` |
| Production | `when <building> finishes training <unit>` | `<building> is idle`, `queue count is <n>` | `train <unit> at <building>`, `cancel training <unit>` |
| Orders | `when order completes`, `when path blocked` | `my current order is <order>` | `attack-move to <target>`, `stop`, `hold position` |
| Fog | `when area becomes revealed/hidden` | `me is visible to enemy`, `me is hidden` | `reveal area around me by <radius>` |
| Health | `when my Health becomes 0`, `when my Health is lower than <n>` | `my Health is <n>` | `set Health to <n>`, `add -<n> to Health` |

**Data-driven design:** All unit types, building costs, and build times live as named variables (no new data classes). `enemy type "Marine"` → sets `my Health=40`, `my Damage=6`, `my Speed=3`, etc.

### Part A — Selection System (`Selection.cs`) [ ]
- New TriggerKind: Selected, Deselected, SelectionChanged, ControlGroupPressed, ControlGroupHeld
- New ConditionKind: Selected, SelectionCountIs, MyControlGroupAssigned
- New actions: `select me`, `deselect me`, `set my control group to <n>`, `select all <unit type>`, `select units near me by <dist>`
- UI: selection ring per unit; control group indicators (1–0 keys)

### Part B — Resource Economy (`ResourceManager.cs`) [ ]
- Globals: Minerals, Gas, SupplyUsed, SupplyMax (SC); Ore, Gems, Power (RA); RUs (HW)
- Triggers: `when Minerals becomes <n>`, `when Supply becomes <n>`
- Conditions: `Minerals is higher than <n>`, `can afford <building>`
- Actions: `add <n> to Minerals`, `set Minerals to <n>`, `deduct <n> from Minerals`, `increase Supply Max by <n>`

### Part C — Construction & Base Building (`ConstructionManager.cs`) [ ]
- Triggers: `when <building> is placed`, `when construction completes`, `when <building> is destroyed`
- Actions: `place <building> at <target>`, `cancel build`, `set build progress to <n>`
- Conditions: `<building> exists`, `<building> is under construction`, `enough Minerals for <building>`

### Part D — Production Queues & Unit Training [ ]
- Triggers: `when <building> finishes training <unit>`, `when production queue is empty`
- Actions: `train <unit> at <building>`, `cancel training <unit>`, `set build time to <n> seconds`
- Conditions: `<building> is idle`, `queue count is <n>`

### Part E — Orders & Attack-Move (`OrderQueue.cs`) [ ]
- Triggers: `when order completes`, `when queued order starts`, `when path blocked`
- Actions: `attack-move to <target>`, `attack <target>`, `stop`, `hold position`, `add order to queue`, `clear orders`, `set rally point to <target>`
- Per-unit state: `my current order`, `my order target`, `my order queue`

### Part F — Fog of War / Visibility (`FogOfWarManager.cs`) [ ]
- Triggers: `when area becomes revealed`, `when area becomes hidden`, `when <unit> enters fog`
- Actions: `reveal area around me by <radius>`, `hide area around me by <radius>`, `set visibility to <radius>`
- Conditions: `me is visible to enemy`, `me is hidden`

### Part G — Damage & Per-Unit Health [ ]
- Variables: `my Health`, `my MaxHealth`, `my Shield`, `my Armor`
- Triggers: `when my Health becomes 0`, `when my Health is lower than <n>`
- Actions: `set Health to <n>`, `add -<n> to Health`, `add <n> to Shield`, `set Shield to <n>`

### Part H — Extended `IEventWorld` [ ]
- `GetSelection()`, `GetSelectedCount()`, `GetResource(string)`, `IsRevealed(actor, playerId)`,
  `RevealArea(center, radius)`, `HideArea(...)`, `GetBuildProgress(building)`, `GetOrderQueue(unit)`

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
| 2026-10-23 | User verified Part C in the editor (WASD move works after speeding to 40; also discovered `set text of` needs a real Canvas/UIControl-Label `HopsLabel` — UI labels need editor setup; note word `show text "..."` candidate for backlog). Task 3 Part D executed: `EventRuntime.SwapSheetProgram` (+ shared `ReadyTree` init), manager-side mtime poll (0.25s) → per-file reload semantics (valid = swap in place, broken = keep old tree + friendly error once per message, new file = attach immediately); variables untouched by swaps (survival by name); reload core covered by 44 new-ish engine tests → **152/152 green**; module build 0/0 | Task 3 D done except user editor verify (edit-while-playing); Task 3 fully code-complete, next: Task 4 (shooter) |
| 2026-10-26 | Design session: reviewed the shipped code (`16483a8`…`40f64f9`: Task 4 A files + scene, camera-follow + mouse-look globals, spawn/show attach, session-start fix). User wanted engine-style script assignment on actors; also float-tested a global manager with find-actor targeting and **rejected** it (collision triggers only reach the colliding pair's sheets; explicit-subject actions would rewrite the language). Locked decision: **Task 3 Part E** — user-facing `EventSheet` component with an `Event File` field (blank = actor name), one required `EventsManager` per scene, `Game.events` auto-global, shared files across instances, component→name→global priority, `SourceName`-keyed reload fan-out; fixes the clone double-`EventSheet` bug (Flax `Clone()` copies scripts). Part E scoped in TASKS.md; Task 4 A amended; implementation not started | Task 3 E (planned), Task 4 A amended |
| 2026-10-26 | Task 3 Part E executed: `Runtime/EventRuntime.cs` (`SheetInstance.SourceName`, `AddSheet(actor, program, sourceName)`, `FindSheetFor` (never the Game host), `SheetsUsingSource`, `RemoveSheet`, `GlobalHost` moved here so tests can play it), `Bridge/EventSheet.cs` (user-facing component: `Event File` field w/ `.events`-suffix tolerance, blank = actor name, self-registers on `OnStart` / unregisters on `OnDestroy`), `Bridge/EventsManager.cs` (scene sweep + late-registration handshake; attach priority component → name fallback → `Game.events` host; `SourceName`-keyed reload fan-out; missing-file single warning with `EventFiles.SuggestFileName` near-miss; `StickerTo`/double-subscribe removed; spawned/show actors attach via `AttachSpawnedActor` component-first), `Bridge/FlaxActions.cs` (GlobalHost moved out). **Fixed a pre-existing failure from the Task 4 commits**: `my <var> becomes 0` never fired when counting down from unset — crossing branch now treats unset as its numeric default (0); 174/174 green; module build 0/0 | Task 3 E code-complete (user editor verify pending); next: Task 4 A scene amendment (components on Player/Bullet/Enemy), then Part B acceptance |
| 2026-10-26 | Editor incident + guard: user added a second `EventsManager` to the Bullet template (`Clone()` copies scripts → every spawned bullet would boot its own full runtime → engine freeze); user also confirmed the zero-click name fallback alone runs the whole shooter. Added a one-way safety net: a second manager warns friendly + disables itself (`Script.Enabled = false`) and `OnUpdate` refuses to tick for strays; verified `Script.Enabled`/`op_Implicit`→bool liveness against the real API. DESIGN.md §6 now documents "one manager, never on a spawned template". 174/174 green; module build 0/0 | Task 3 E follow-up guard; Task 4 A amended (components optional — name fallback covers the default shooter scene) |
| 2026-10-27 | Task 5 Part B/C complete: animation words (`play anim`/`loop anim`/`stop anim`/`when anim ends`) wired via reflective `AnimatedModel.PlayState` + `EventSheet` anim-end subscription; `Character.events` + `dragon.events` written per DESIGN.md §5; all 177 unit tests pass; module builds clean. Editor verify: `dragon.events` → `play anim "Idle"` plays the idle on the BGE dragon with no console warnings (Part A verified). Remaining: walk/shoot not yet exported (expected unknown-anim warnings until exported). | Task 5 A/B/C complete (Part A editor-verified; Part B/C code-complete) |
| 2026-10-27 | Shooter diagnosis: bullet→enemy never kills. Root causes: `Actor.ToString()` used as the sheet name (Flax returns `"Name (Type; id)"`), no collider/distance contacts for `touches`/`overlaps`, `EventsManager` parented to the hideable `Player`, broadcast `hit`, `OwnerOf` stopping at `RigidBody`, `HasTag` comparing names. Locked plan: implement `Distance`/`Colliders`/`Both` contact modes (physics colliders final; `overlaps` stays distance), targeted `hit to other`, manager moved to scene root by user. Scoped the work in `TASK_FIX_SHOOTER.md`; no code changed yet. | Task 4 B follow-up scoped (`TASK_FIX_SHOOTER.md`); next: implement Parts A–E |
| 2026-10-27 | Shooter fix code landed (`TASK_FIX_SHOOTER.md` Parts A–E): `IEventWorld` gained `NameOf`/`IsTouching`/`Actors`; sheet names now come from `FlaxWorld.NameOf` (real `Actor.Name`); distance contact polling with per-trigger enter-edge sets, `when other touches me` wildcard, and `Both`-mode dedupe; `EventSheet.OwnerOf` walks colliders up to the nearest `EventSheet` actor; `EventsManager` gained `Contact Mode` (`Distance`/`Colliders`/`Both`, default `Both`); `send message ... to me/other` routes by sheet identity; `Bullet.events` sends `hit` to `other`; `HasTag` uses `actor.HasTag`. New regression/contact/message tests → **192/192 green**, module build clean. Editor acceptance pending: manager to scene root, then compare the three contact modes. | Task 4 B code-complete (editor acceptance pending); `TASK_FIX_SHOOTER.md` A–E code done |
| 2026-10-27 | Editor collider test: PhysX reports `kinematic bodies with CCD enabled are not supported! CCD will be ignored` on the Bullet. Corrected the setup to **UseCCD = false**; CCD is a dynamic-body feature and the transform-driven kinematic bullet doesn't need it (speed/contact offset/`Both` cover misses). | Task 4 B editor setup correction |
| 2026-10-27 | Editor hurt test: the one-time `Invulnerable was read before it was ever set` warning came from `unless my Invulnerable is true` on the first touch. `Player.events` now initializes it on `when me appears`. Reminder: if touch makes the game appear frozen, `EventsManager` must be on a permanent scene-root actor (a manager parented to the hidden Player stops ticking). | Task 4 B editor note |
| 2026-10-27 | Scoped Task 4 Part C: physics-native movement via `RigidBody` velocity/forces (opt-in; transform movement stays default), dynamic bullet/CCD experiment, and a decision on whether `push` becomes an impulse/force. | Task 4 C (planned) |
| 2026-10-31 | Task 7 Part B — sight words: `when me sees <thing> by <dist>` (enter edge) and `when me stops seeing <thing> [by <dist>]` (falling edge, reuses last range), distance + line-of-sight polled triggers in `EventRuntime.PollSight`, `IEventWorld.HasLineOfSight` + `FlaxWorld` raycast (`Physics.LineCastAll`), 19 new unit tests (228/228 green), module build 0/0 | Task 7 Part B |
| 2026-10-28 | HUD + sound fix (`TASK_FIX_HUD_SOUND.md`): `UIControl`, `AudioSource` and `AnimatedModel` are actors, so every `GetScript<T>()` returned null. HUD lookups now use `as UIControl` + `.Control as Label` (plus a parent-owns-one-label fallback and warnings that list the real labels); anim words use `FindActor<AnimatedModel>()`; the anim-end subscription subscribes the model actor itself (no double-subscribe). `play sound` resolves clips through the asset registry (`GetAllAssetsByType` + `GetAssetInfo` + `Load<AudioClip>(Guid)`) keyed by `Runtime/AssetNames.cs` (folder/`.flax`/extension/case-insensitive), so `"hurt.wav"` reaches `sounds/hurt.wav.flax` with no path probing and no "Missing file" spam; sources spawn at the actor and the miss warning lists the imported clips. `stop sound` stops the actor source plus every tracked `play sound` source. `HealthLabel` removed (duplicated `LivesLabel`) from `Game.events` and `Scene.scene`. DESIGN.md documents the `GameText` convention, sound names and the actors-not-scripts rule. **198/198 tests green**, module build 0 warnings. Editor acceptance pending (HUD visible, hurt sound audible). | Task 4/5 follow-up: HUD + sound code complete, editor verify pending |
| 2026-10-28 | Task 6 Part A (scene words): wired `load scene "<name>"` to actually load a scene by name in `FlaxActions.LoadScene` (was warn-only); made `when game starts` fire once per session while `when scene starts` refires on every load via a session flag; `when scene starts` classified to `TriggerKind.SceneStart` and covered by unit test; `quit game` already wired (`Engine.RequestExit`). Tests 200/200 green. Editor verify (two scenes, one loads the other, quit closes the game) pending. | Task 6 A code complete |
| 2026-10-28 | Flax module build fix: `AssetInfo` has no `Name` property in this Flax version; both scene-name lookups in `LoadSceneByName`/`SceneNames` now read `System.IO.Path.GetFileNameWithoutExtension(info.Path)` (same `info.Path` property `SoundIndex()` already uses). Pure test suite stays 200/200 green; module build recompiles clean once the editor rebuilds. | Task 6 A build fix |
| 2026-10-28 | Task 6 Part A test scenes: created `Content/Menu.scene` (PlayButton, QuitButton, GameText UIControls + EventsManager on `Menu` actor), `Content/Level1.scene` (ScoreLabel, Floor, Camera + EventsManager on `Level1` actor), `Source/Game/GameFiles/Menu.events` (scene starts → show buttons; PlayButton clicked → load scene "Level1"; QuitButton clicked → quit game), `Source/Game/GameFiles/Level1.events` (scene starts → set text of ScoreLabel to Score; Score changes → update label). All 200 unit tests pass including shipped GameFiles parse check. Editor verify pending. | Task 6 A test scenes |
| 2026-10-28 | Task 6 complete: fixed `MouseControlCenter` hit test (Position.X/-Position.Y, no double-counting), UI buttons route `Clicked` to Menu.events via name fallback, added hover (color switch + Hovered push) and Right/Middle Mouse click. Clicking PlayButton/QuitButton loads level / quits. Hover color change verified after setting TextColor ≠ TextColorHighlighted. | Task 6 A–C done |
| 2026-10-29 | Added Task 11 — State Machines (BGE logic bricks): per-actor state machine with `state <name>` trigger modifier, `set state <name>` action, `when state is <name>` edge trigger, `my state` value; Guard example (patrol→chase→attack) + global game states (menu/playing/paused/gameover). | Task 11 (planned) |
| 2026-10-29 | Task 7 Part A — nav bridge: `goto <thing>` now paths via `Navigation.FindPath` on the baked NavMesh and follows waypoints per tick (`FlaxActions.OngoingMove.Path`); `when me arrives at <thing>` polled as an enter-edge arrival (`EventRuntime.ArrivalSlack`, `IEventWorld.FindActor`); `random <thing>`/`nearest <thing>` target qualifiers already resolve through `FlaxActions.ResolveThing`. Tests 207/207 green; Flax module build 0 warnings / 0 errors. Editor verify (baked NavMesh + Waypoint scene, cube walks to random waypoint) pending user's editor run. | Task 7 A code-done |
| 2026-10-30 | Task 7 Part A verified in editor: cube walks to random Waypoint after baking NavMesh + placing Waypoint actors. Added `goto <thing> speed <n>` qualifier (default 5 units/sec) so patrol speed is configurable; 209/209 tests green; Flax module build 0 warnings / 0 errors. All Task 7 Part A boxes checked. | Task 7 A complete |
| 2026-10-30 | Task 7 Part D — Arrival bug fix: `PollArrival` iterates all actors matching `<thing>` name (not just `FindActor` which returns one), so `when me arrives at Waypoint` fires reliably even when `goto random Waypoint` picks a different waypoint each lap. Debug logging added to `goto` (path found), `Step` (waypoint reached/final arrival), and `PollArrival` (distance to candidates, arrival fired). 209/209 green; module build 0/0. Editor verify pending. | Task 7 D debug |
| 2026-10-30 | Task 7 Part A verified in editor: cube walks to random Waypoint after baking NavMesh + placing Waypoint actors. Added `goto <thing> speed <n>` qualifier (default 5 units/sec) so patrol speed is configurable; 209/209 tests green; Flax module build 0/0. All Task 7 Part A boxes checked. | Task 7 A complete |
| 2026-10-31 | Task 7 Part B/C code: added `near` polled trigger (`TriggerKind.Near`, `WordRegistry`, `Triggers.cs` classification, `EventRuntime.PollNear` distance enter-edge); `IEventWorld.HasLineOfSight` + `FlaxWorld` raycast already wired for sight. Wrote `Enemy.events` exactly per DESIGN.md §5 (patrol→chase→attack via `my mode`, `sees`/`stops seeing`/`arrives`/`near` triggers); paired `Player.events` with `when message "hurt" arrives` hurt/invulnerability handler. 236/236 tests green; Flax module build 0/0. Part B boxes checked; Part C write/pair boxes checked; full-loop verify pending user's editor run. | Task 7 B+C code complete |
| 2026-10-31 | Task 4 Part C (physics-native movement, opt-in): chose a per-actor `EventSheet` flag **Use Physics Movement** (no new word; body property covers move/goto/push). New pure `Runtime/PhysicsMovement.cs` (mode selection, dynamic velocity keeps gravity, brake, push mode); `SheetInstance.UsePhysicsMovement` + `EventContext.Sheet` carry the flag (hot-reload safe); `FlaxActions.MoveSubject` drives `LinearVelocity` (dynamic) / swept `AddMovement` (kinematic) / `Position` (default), `stop moving` brakes, `goto` brakes on arrival, `push` becomes a real impulse on dynamic bodies only. DESIGN §5 gets a "Physics-native movement" subsection + body-settings table, §9 records the decision (Bullet dynamic/CCD, Player+Enemy kinematic). 13 new pure checks → **249/249 green**; module build 0/0. Editor acceptance (bullet tunneling, hurt cycle in Colliders mode) still pending. | Task 4 C code complete |
| 2026-11-01 | Animation fix (`TASK_FIX_ANIMATION.md`): `play anim` / `loop anim` / `stop anim` never worked on Flax 1.13 because the bridge reflected `AnimatedModel.PlayState` and `AnimationController`, neither of which exists. Clips are now real `Animation` assets resolved through the asset registry (folder/`.flax`/case-insensitive, plus space-boundary suffix aliases so `"walk"` finds `BGE Dragon 2.07 animation walk`), and the bridge builds a virtual `AnimationGraph` per clip with `InitAsAnimation` (true = failure). `play anim` = once + hold, `loop anim` = repeat, `stop anim` = previous clip or bind pose; repeating the same clip while it plays is a no-op (held triggers fire every tick). One-shot ends are timed against `Animation.Length` in `FlaxActions.Step` and push `AnimEnd`, so `when anim "..." ends` works without the dead `EventSheet` reflection subscription (removed). `char_animated_test.scene` now points its `EventSheet` at `dragon` and `dragon.events` uses `loop anim "walk"`. `AssetNames.Suffixes` + 4 tests; **256/256 green**, module build 0 warnings. Editor verify pending. | Task 5 B/C follow-up; `TASK_FIX_ANIMATION.md` code complete |
| 2026-11-02 | Dragon demo controls (`dragon.events`): WASD moves the dragon (`move forward/back/left/right speed 40`), Shift+WASD runs (`speed 100`), idle/walk/run swap through `loop anim "animation idle pose"/"animation walk"/"animation run"`, Left Mouse plays the one-shot `"dragon_fire"` attack (`my Attacking` roots the dragon and blocks re-triggering until `when anim "dragon_fire" ends`), and release events return to idle only when the last movement key is up. Implemented the long-advertised `camera follows <thing>` word (`ActionVerb.CameraFollow`, parse + `FlaxActions.StartCameraFollow`/`Step`) so the camera translates with the target while preserving the authored offset; it also switches off the legacy `cameraFollow` Player follow. 1 new parse test -> **258/258 green**, module build 0 warnings. Editor verify pending (clip-rig compatibility of the older `dragon_fire` clip). | Task 5 B/C follow-up; dragon demo controls |
| 2026-11-05 | Task 12 Part A (`c55f85a`): CharacterController as a third opt-in movement type — `PhysicsMoveMode.Character`, 4-arg `PhysicsMovement.ModeFor` (controller wins over a RigidBody), `CharacterBodySettings` defaults/clamping, `EventSheet` optional **Configure Character Body** capsule fields + `FindCharacterController`, `FlaxActions.MoveSubject` collide-and-slide `Move` (misplaced controller warns + falls back), ground-check distance on `SheetInstance`. **Part B**: every move resolves one `PhysicsMoveMode` up front (`ResolveMoveMode`, stored on `OngoingMove.Mode`), so `move`/`goto` drive velocity/swept/character bodies instead of `Position +=`; body-aware `goto` waypoint stepping (`PhysicsMovement.ReachesWaypoint`/`PathSpeed`) lands physics bodies exactly on each waypoint instead of teleporting or overshooting. DESIGN §5/§9 updated; TASKS goal reworded to additive. **356/356 green**, module build 0/0. Editor acceptance (character scene) still pending. | Task 12 A+B code complete |
| 2026-11-05 | Task 12 Part C (ground/slope/collision): the event runtime now feeds CharacterController gravity - a per-actor fall speed (new `CharacterFallSpeeds`), scene `Physics.Gravity` (981 cm/s2 default) and `PhysicsMovement.CharacterStep`; grounded ticks gate horizontal movement, airborne ticks fall straight down with a small ground stick while grounded; `AutoGravity` is turned off (warn once) so gravity is applied once. Ground probe uses a downward ray from the capsule feet (`OriginMode`-aware) within the sheet Ground Check Distance, rejects normals steeper than `SlopeLimit` and ignores triggers, with `IsGrounded` as fallback. Dynamic bodies get `UseCCD` enabled automatically; kinematic stay swept; step offset and wall sliding stay native to the controller. New pure helpers (`FeetY`, `IsWalkableSlope`, `FallSpeed`, `CharacterStep`); 10 new checks -> **366/366 green**, module build 0/0. DESIGN section 5/9 updated (AutoGravity question settled); editor acceptance still pending. | Task 12 C code complete |
| 2026-11-06 | Task 12 Part D (integration with existing words): `move` already resolves forward/back/left/right in the actor local axes and hands that vector to every mode. `turn` is now rotation-only and only accepts left/right; fixed the yaw sign after verifying Flax `Quaternion.Euler(0,+90,0)` turns local forward (+Z) toward +X (the actor right), so left is negative and right positive. `look at` / `face` only set orientation; `face` replaces its previous tracker and missing targets warn once. `push` now follows the target own opt-in and body: dynamic = `AddForce(..., ForceMode.Impulse)`, CharacterController = collide-and-slide `Move`, kinematic = swept `AddMovement`, else transform nudge (new `PhysicsPushMode.Sweep`/`Character` and 4-arg `PushModeFor`). `teleport` warps position and cancels the subject moves/face tracker, clears `CharacterFallSpeeds`, and zeros dynamic linear/angular velocity. 8 new pure checks -> **403/403 green**; module build 0 errors (2 pre-existing Task 13 XML warnings). DESIGN section 5/9 and Task 12 Part D updated; editor acceptance still pending. | Task 12 D code complete |
| 2026-11-08 | Task 15 Part B (ProgressBar & Slider): registry entries for `set progress of <thing> to <value>` / `set slider <thing> to <value>` and `progress of <thing> is <value>` / `slider <thing> is <value>`; `SetProgressNode`/`SetSliderNode` statement nodes parsed in `EventParser.ParseSet`; `ConditionKind.ProgressIs`/`SliderIs` with `IEventWorld.ProgressOf`/`SliderOf` (0-1 or 0-100 normalization); `EventRuntime.OnSetProgress`/`OnSetSlider` hooks wired in `EventsManager` to `FlaxActions.SetProgress`/`SetSlider` (finds ProgressBar/Slider on UIControl, sets Value/Maximum). 12 new pure checks -> **492 passed**, 11 pre-existing unrelated failures; Game module builds 0 errors (2 pre-existing XML doc warnings). | Task 15 Part B |
| 2026-11-09 | Task 15 Part C (TextBox / InputField): registry rows for the four text-input triggers, `<thing> is focused` / `text of <thing> is <value>` conditions, and `focus` / `unfocus` actions; `TriggerKind.TextChanged/Focused/LostFocus/Submitted` classified before the key and watcher fallbacks; `ConditionKind.Focused/TextIs` backed by new `IEventWorld.IsFocused`/`TextOf`; `EventsManager` polls each TextBox per frame (text diff, focus edges, Enter while focused) and broadcasts pushes (the `me` form matches the sheet's own control); `FlaxActions` gains `SetControlFocus` (`Focus()`/`Defocus()`), TextBox-aware `SetText`, and `select all <thing>` selecting the box's text before falling back to RTS unit selection. 20 new checks -> **569 passed, 0 failed**; Game module build 0 errors (2 pre-existing Task 13 XML warnings). | Task 15 Part C |
| 2026-11-09 | Task 15 Part D (Tween / Animation helpers): registry rows for the four `tween` forms, `easing` qualifier and `when tween <thing> ends`; `ActionVerb.Tween` parser (`ActionSpec` carries property/target/value/color/duration/easing); pure `Runtime/Tweens.cs` (`TweenKind`, `TweenEasing`, `TweenTrack` interpolation/completion, zero-duration snap); `FlaxActions` keeps a live tween list advanced in `Step` (position/scale write `Actor.Position`/`Scale`, alpha/color write Label `TextColor` or Image `Color`), retargets same-property tweens, and fires a broadcast `TweenEnd` push (`when tween me ends` binds to the actor's own sheet). 19 new checks -> **588 passed, 0 failed**; Game module build 0 errors (2 pre-existing Task 13 XML warnings). | Task 15 Part D |
