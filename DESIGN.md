# Event Manager â€” Design Document

**Status:** v0.1 draft
**Target engine:** Flax Engine (https://flaxengine.com/) via C# scripting
**Audience:** non-programmers building games

---

## 1. Vision & Goals

A game-logic system where you *write game events as plain, readable text* â€” instead of wiring
visual bricks (Blender Game Engine) or clicking panels (GDevelop).

The core loop for the user:

1. Write an event in a `.events` text file.
2. See it run in Flax, live.
3. Adjust, repeat.

### What this is

- A **stacked event system**: `Trigger â†’ Conditions â†’ Actions`, written as plain sentences.
- A **simulation script**, not programming: no braces, no classes, no types to declare.
- A fixed, small vocabulary (roughly 40 words to start), so learning is learning *words*, not syntax.

### What this is not

- Not a general programming language. Loops, classes, complex math stay out.
- Not a code generator. Game logic is never transformed into C# (see Section 6).
- Not a GUI (yet). Text files are the interface; a syntax-colored editor window is a later milestone.

### Design principles

1. **Readable beats clever.** A person should re-read their own logic six months later and understand it instantly.
2. **Small vocabulary.** Memorizing ~40 words should be enough to make a complete small game.
3. **Forgiving.** Errors are friendly sentences with line numbers, never stack traces.
4. **Instant.** Saving the text file updates the running game. No compiling.
5. **Escape hatch exists.** Advanced C# is possible *beside* the system (custom words), never required to *use* it.

---

## 2. Prior Art

### Blender Game Engine (logic bricks)

BGE attached **sensors, controllers, actuators** (bricks) directly onto each object and wired them
visually.

- **We keep:** the brick vocabulary mindset (sensors = triggers, actuators = actions) and per-object
  locality â€” logic "living on" the thing it affects.
- **We drop:** the visual wires and controller logic combos (AND/OR gates, Python bridges). In text,
  calls to NOT attach wires differently. Message events replace most controller-wiring use cases.

### GDevelop

GDevelop uses **event sheets**: rows of conditions with action lists, all global, objects referenced
by name.

- **We keep:** the "stacked events" layout â€” one thing after another, top to bottom â€” and the
  tight relation words (`touches`, `is pressed`).
- **We drop:** the panel-by-panel editing. Text does that job with less friction. (GDevelop also
  *generates* nothing â€” its events run on a fixed runtime. We deliberately mirror this, see Section 6.)

### Rejected alternative syntaxes

Recorded for the record; we chose Style 3.

| Style | Looks like | Why rejected (for now) |
|---|---|---|
| BGE-style bricks in text | `sensor KeyW [pressed] -> action Move [5]` | Indentation was doing double duty as "arrows"; felt machine-like, not human. |
| GDevelop-sheet keywords | `When: Coin collides with Player / Then: ...` | Reads like a form; nested conditions get flat and awkward. |
| **Style 3 â€” when/if/do sentences (chosen)** | `when other touches me:` + indented lines | Most natural to read; one concept (indentation) replaces wires *and* nesting. |

### Brick borrow map (BGE â†’ our words)

The full accounting of what happened to every brick family â€” so "did we forget a BGE
gimmick?" is always one table away.

| BGE component | Our word(s) |
|---|---|
| Always / Delay sensor | `when scene starts` / `when me appears` / `when every N seconds` + `wait` |
| Keyboard / Mouse / Mouseover sensors | `when W is pressed/held/released` (Left/Right/Middle Mouse named like keys) / `when wheel scrolls up/down` / `when me is hovered` |
| Collision / Touch sensor | `when other touches me`, `when me touches <thing>` / `when me overlaps <thing>` (no physics) |
| Near / Radar / Ray sensor | `me is near <thing> by <dist>` condition / `me sees <thing> by <dist>` (distance **+ raycast**, edge-firing) |
| Message sensor | `when message "<name>" arrives` |
| Property sensors (equal / included) | `when <var> becomes <value>` / `when <var> changes` / `my bag has <thing>` |
| Random sensor | `chance <n>%` + the `random <min> to <max>` value form |
| AND / NOR / controllers | `and` / `unless` / `not` (OR deliberately parked â€” Â§9) |
| Motion actuator | `move` / `turn` / `push` / `goto` (NavMesh) / `goto there` |
| Servo Control actuator | **parked** (physics-aggregated motion) |
| Track To actuator | `face <thing>` (continuous) / `look at <thing>` (snap) |
| Action actuator | `play anim` / `loop anim` / `stop anim` / `when anim "<name>" ends` |
| Sound actuator | `play sound "<file>"` (volume/pitch variety parked) |
| Property / Score actuators | `set` / `add` |
| Edit Object actuator | `spawn` / `destroy` / `teleport` (Replace-Mesh morphing parked) |
| Camera actuator | `camera looks at` / `camera follows` (zoom, pans, cutscene framing parked) |
| Scene actuator | `load scene` / `restart scene` / `quit game` |
| Visibility actuator | `show` / `hide` (any named actor) |
| State actuator (object state machine) | replaced **by design**: `my` mode flags + stacked events (see Enemy brains) |
| Constraint actuator | **parked** ("keep me inside the arena") |
| Steering actuator (flee/avoid) | partially: `goto`/`move toward` = seek; flee (`move away from`) **parked** |
| Network / 2D-Filter actuators | out of v0.1 scope |
| Custom Python (the BGE escape hatch) | custom C# words (same role â€” advanced, optional) |

---

## 3. The Event Model

Every event is three parts, always in this order:

```
        TRIGGER                 CONDITION(S)                ACTION(S)
   "what turns this on"     "is it allowed right now"    "what actually happens"
```

Written as stacked lines:

```text
when other touches me            <- trigger (may carry its own condition words)
  unless other is a Bomb       <- condition
  add 1 to Score               <- action
  play sound "pickup.wav"      <- action, runs after the line above
  destroy me                   <- action
```

### Rules of the model

- **One `when` per event.** An event starts with `when`; every indented line below it belongs to it.
- **Top-down, top wins.** Events are checked top-to-bottom, in the order written. Predictable.
- **Once per tick.** An event fires at most once per game tick, even if its trigger happens twice
  (e.g. two collisions in one frame). Use `repeat: <n>` if you want multiple ticks.
- **All matching events run.** If two different events are true in the same tick, both run â€”
  using the order above. There is no hidden early-exit.
- **Actions are ordered.** They run one after another, exactly as written, in the same tick.

### Attachment: per-object + global

An `.events` file reaches an actor one of two ways — **explicit first, name fallback second**:

- **EventSheet component (the engine way):** select the actor → *Add Script → EventSheet* →
  set its **Event File** field (e.g. `Player`). Clones inherit it, so a `spawn Bullet` copy
  carries its logic automatically. One file can be assigned to **many actors** — a single
  `Enemy.events` can drive five prefabs, and `my …` variables stay per-copy.
- **Name matching (the zero-click fallback):** an actor with *no* EventSheet component and
  the same name as a file gets it automatically — `Coin.events` + actor `Coin` = logic
  sticks on. A throwaway test actor needs nothing but its name.
- **Priority:** a component beats a name match, always; an actor with a component is never
  also name-matched (even if its file is missing — the missing file gets a warning with a
  "did you mean …?" suggestion, then attaches the moment the file exists).
- **Global sheet:** `Game.events` attaches to the scene-wide host by itself — no component,
  no actor named `Game` needed. One `EventsManager` script in the scene is still required
  (it ticks the runtime); it warns the one time when it's missing. **Never add a second
  one — and never on a spawned template**: each clone would boot its own runtime
  (`Clone()` copies scripts, so every `Bullet` copy would carry a manager that runs
  `Game.events` again). A duplicate is now turned away with a friendly warning.
  Put the one manager on a **permanent scene-root actor** — never on an actor the game
  hides or spawns: `IsActive = false` (from `hide me`) also disables the manager's
  `OnUpdate`, so pending waits could never resume.

Inside a sheet, **`me`** means that actor, and **`other`** means the actor involved in the
trigger (the one touching, the one hit, the one which sent a message).
- A file named **`Game.events`** (or anything in a `Global/` folder) is a global sheet: it runs
  scene-wide, `me` refers to the scene itself, and `other` is usually undefined.

This gives BGE's locality (logic lives on the object) with GDevelop's simplicity (no wires, no
clicks â€” name a file and a CS file uses an actor and the logic sticks).

### Pronouns

| Word | Means |
|---|---|
| `me` | the actor this event sheet is attached to |
| `other` | the actor that caused the trigger (collision partner, â€¦â€¦ ) |
| `game` | scene/global scope prefix (e.g. `add 1 to Score` is global; `go Score`) |
| `my <name>` | a variable that belongs to `me` specifically |

### Variables

- Auto-created, typed by the value you give: number, word (text), true/false.
  ```
  set Score to 0
  set my name to "Bob"
  set chasing to true
  ```
- **Capitalized names** (`Score`, `Lives`) are global (survive scene changes within a session).
- **`my â€¦` names** belong to that actor; each copy of the actor has its own.
- **Possessive reads:** any actor's variable can be read with an apostrophe-s â€” `other's damage`,
  `Player's gold`. You can read a neighbor's counters; you can only ever *write* to your own.
- Reading an unset variable gives 0 / "" / false with a one-time friendly warning.

---

## 4. The Language (v0.1 specification)

### Shape

- One event per block. `when â€¦` unindented; everything else indented (2 spaces).
- Comments start with `#`.
- No punctuation requirements: no semicolons, no braces, no `end` keyword. End of block = less indentation.
- One line = one action. Keep lines short; if it feels long, split or add a condition.

### The tiny vocabulary

This is roughly the full "grammar" of the language: **`when` `unless` `and` `not` `set â€¦ to` `to` `me` `other` `game` `my`**.
Everything else in Section 5 (move, destroy, touches, â€¦) is *word entries* you can add or extend.

| Keyword | Use | Example |
|---|---|---|
| `when` | starts an event â€” must be first word | `when W is pressed` |
| `unless` | adds a condition line | `unless me is dead` |
| `if` | the same condition line, read positively | `if my health is lower than 1` |
| `otherwise` / `else` | runs the lines below when the condition above was false | `otherwise` |
| `and` | joins conditions on one line | `when other touches me and other is a Player` |
| `not` | negative inside a condition | `unless me is not near Door` |
| `set` / `to` | assign a variable | `set Score to Score + 1` |
| `add` | increment a variable | `add 5 to Points` |
| `me` | self reference | `destroy me` |
| `other` | trigger partner | `push other forward to 2` |
| `my` | per-object variable prefix | `my health` |
| `repeat` | loop an action n times | `repeat 3: spawn Coin` |

`or`-chains are *not* in v0.1 (see Section 9). Almost every use case is two separate `when` blocks
instead.

### Conditions (inline)

Conditions ride on `when` or their own `unless` line:

```
when other touches me and lives is higher than 0
  unless Score is 0
  ...
```

An `unless` line gates every action **below it**, until the next `unless` line. That way you can
protect part of an event without making a new one. Two or more condition lines written one under
another are a single block and must **all** pass (they stack with `and`: `unless Shift is held` then
`my Stamina is higher than 0` runs only while both hold). `if <condition>` is that same line read the
positive way, and `otherwise` (alias `else`) opens the block that runs when the condition directly
above it was false — same 2-space indent, no second condition needed. Two `if`/`otherwise` pairs
can follow each other in one body; each pair re-gates from its own condition line.

```
when Escape is pressed
  if my Paused is true
  set my Paused to false
  otherwise
  set my Paused to true
```

### Friendly errors (hard rules)

1. Errors **always** name the file, the line number, and the offending words.
2. Error text is a full human sentence: `Line 4: I don't know the word "errLoaded". Did you mean "destroy"?`
3. Unknown words give you a near-miss suggestion (edit distance against the vocabulary).
4. Nothing silently runs with errors. If one line fails, the file still loads with that one line skipped
   (so you keep your hot flow), and the error shows in the Flax console + as a red marker by the line.
5. Indentation errors are explained, not scolded: `Line 7 looks like an action but nothing above it was a 'when'.`

---

## 5. Word Catalog (v0.1)

Deliberately small. Each word ships with a one-line example and, later, "green text" coloring
like GDevelop, so the editor teaches the vocabulary passively.

### Triggers (start lines)

| Word | Example | Notes |
|---|---|---|
| `when <key> is pressed` | `when W is pressed`, `when Right Mouse is pressed` | keyboard + mouse buttons â€” buttons are **named like keys**: `Left Mouse`, `Right Mouse`, `Middle Mouse` |
| `when <key> is released` | `when Space is released`, `when Right Mouse is released` | same key-slot as above |
| `when <key> is held` | `when Left Shift is held`, `when Right Mouse is held` | fires every tick while held â€” hold-to-aim lives here |
| `when wheel scrolls up` | `when wheel scrolls up` | fires **once per detent** (a fast spin = several events, which is usually what you want) â€” zoom, cycling, scrolling lists |
| `when wheel scrolls down` | `when wheel scrolls down` | same as above, the other way; no amount-read word in v0.1 (grammar cost â‰¥ benefit) |
| `when other touches me` | | contact enter-edge — physics collider or distance, selectable |
| `when me touches <thing>` | `when me touches Water` | name-based contact — physics collider or distance, selectable |
| `when me overlaps <thing>` | | distance-based in every mode, no physics needed |
| `when game starts` | | global sheets â€” fires **once per play session** (very first scene) |
| `when scene starts` | | fires on **every** scene load â€” the menu/level reset word |
| `when <thing> is clicked` | `when PlayButton is clicked` | menus: works on `me` too, if the sheet is attached to the button |
| `when me arrives at <thing>` | `when me arrives at Waypoint` | navigation: fires when `goto` finishes (path done or close enough) |
| `when me sees <thing> by <dist>` | `when me sees Player by 8` | **edge trigger**: fires once when sight *starts* â€” distance + raycast, walls block |
| `when me stops seeing <thing> by <dist>` | `when me stops seeing Player by 8` | **edge trigger**: fires once when sight *ends* (leave out `by <dist>` to use the last seen range) |
| `when me gets equipped` | | fires **on the item's sheet** â€” the item is attached to its owner (see Items & the bag, below) |
| `when me gets unequipped` | | fires on the item's sheet when taken off or dropped |
| `when me gets dropped` | | fires when the item leaves the bag back into the world |
| `when me is hovered` | | point & click: cursor over the actor â€” hotspot highlight word |
| `when me appears` | | on spawn / scene load |
| `when every <n> seconds` | `when every 2 seconds` | timer loop |
| `when <var> becomes/becomes not <value>` | `when my health becomes 0` | watch a variable |
| `when <var> changes` | `when Score changes` | same watcher, fires on *any* value change â€” HUD refreshes |
| `when message "<name>" arrives` | `when message "die" arrives` | events between objects |
| `when anim "<name>" ends` | `when anim "dragon_fire" ends` | armature characters â€” see below |

### Conditions (inline / `unless` lines)

| Word | Example |
|---|---|
| `<var> is <value>` /`is not` | `Score is 3` |
| `<var> is higher/lower than <value>` | `my health is lower than 1` |
| `<thing> exists` /`does not exist` | `Key exists` |
| `me is near <thing> by <dist>` | `me is near Player by 2` |
| `<key> is held` | `Shift is held` |
| `chance <n>%` | `chance 20%` |
| `<thing> is/has tag <tag>` | `other has tag Enemy` |
| `my bag has <thing>` | `my bag has Sword` |

**Branching inside a body.** `if <condition>` is a condition line like `unless`; `otherwise`
(alias `else`) runs when the condition directly above it was false. Both stay at the same 2-space
indent as the condition line, and one key can toggle state without a second event:

```
when Escape is pressed
  if my Paused is true
  set my Paused to false
  unload scene "PausePanel"
  otherwise
  set my Paused to true
  add scene "PausePanel"
```

### Actions

| Word | Example | Notes |
|---|---|---|
| `move <dir> speed <n>` | `move forward speed 5` | dirs: forward/back/left/right/up/down; physics opt-in per actor (below) |
| `move toward <thing> speed <n>` | `move toward Player speed 3` | |
| `turn <dir> degrees <n>` | `turn left degrees 90` | |
| `goto <thing>` | `goto Player` | navigation: walks there using the baked NavMesh (walls avoided for free); pairs with `arrives at` |
| `goto there` | `goto there` | point & click: walks to the **last clicked position** (pairs with `when Ground is clicked`) |
| `camera looks at <thing>` | `camera looks at Chest` | aim the view at any actor |
| `camera follows <thing>` | `camera follows Player` | keeps them centered while they move; aiming at something else releases the follow. Framing comes from the `CameraHeight` / `CameraDepth` / `CameraPitch` globals — `CameraDepth = -CameraHeight / tan(CameraPitch)` centres the target, and `CameraPitch 90` is straight down (TK4) |
| `look at <thing>` | `look at Player` | one-shot snap: turn toward the target, once |
| `face <thing>` | `face Player` | continuous: keeps turning toward the target while it moves (BGE Track To) â€” the turret/chaser word |
| `teleport me/other to <thing>` | `teleport me to SpawnPoint` | |
| `push <target> direction <dir> strength <n>` | `push other up strength 10` | follows the target's move mode: impulse (dynamic), swept (kinematic), collide-and-slide (character), else a transform nudge. Add `for <n> seconds` (e.g. `push other back strength 100 for 0.2 seconds`) to keep shoving every tick — that is how TK4 knockback reads; without it the push is one tick |
| `make <target> unmovable` | `make me solid` | physics toggle |
| `destroy <me/other/thing>` | `destroy me` | |
| `spawn <thing> at <thing>` | `spawn Coin at SpawnPoint` | clones an actor already placed in the scene (the template stays put, hidden if you like); `offset <n>` (`spawn Bullet at me offset 30`) slides the copy `n` units along the spot's facing, XZ only |
| `spawn prefab "<name>" at <thing>` | `spawn prefab "Hero" at SpawnPoint` | instantiates an imported `.prefab`; name ignores folder/extension/case; copies get unique names (`Hero`, `Hero 2`, …) and every actor inside runs its own `when me appears` |
| `spawn scene "<name>" at <thing>` | `spawn scene "HeroScene" at SpawnPoint` | loads a `.scene` **on top of** the current scene (additive) and moves only that scene's root actors to the spot — existing actors never move |
| `play sound "<file>"` | `play sound "hit.wav"` | clip **name** only: `"hit"`, `"hit.wav"`, `"sounds/hit"` are the same imported clip |
| `stop sound` | `stop sound` | stops this object's playing clips (from a global sheet: every clip) |
| `set <var> to <value>` | `set Score to 0` | |
| `add <n> to <var>` | `add 1 to Score` | |
| `send message "<name>" to <thing>` | `send message "die"` | broadcast if no target |
| `stop this event` | | exit the current event early |
| `wait <n> seconds` | `wait 0.5 seconds` | pauses THIS event, not the game |
| `hide me` / `show me` | `show PausePanel` | show/hide works on **me or any named actor** â€” the core of plain UI logic |
| `set text of <thing> to <val>` | `set text of ScoreLabel to Score` | live values on the screen; plain text works too |
| `play anim "<name>"` | `play anim "dragon_fire"` | armature: plays once, then holds the last frame (BGE "Play") |
| `add <thing> to my bag` | `add Sword to my bag` | picks items up; the bag holds things by name, counted |
| `remove <thing> from my bag` | `remove Potion from my bag` | used/consumed things |
| `equip <thing>` | `equip Sword` | takes it from the bag and attaches the item's sheet â€” its `equipped` event fires |
| `drop <thing>` | `drop Sword` | unequips if worn, takes from bag, spawns the item at my feet |
| `loop anim "<name>"` | `loop anim "animation walk"` | armature: repeats until something new plays (BGE "Loop") |
| `stop anim` | `stop anim` | armature: stops and goes back to the previous animation (BGE "Stop") |
| `show text "<txt>"` | `show text "Game Over"` | HUD text (also below) |
| `hide text` | `hide text` | removes the HUD text |
| `restart scene` | `restart scene` | loads the current scene again (also resets the spawned things) |
| `load scene "<name>"` | `load scene "Level1"` | replaces the current scene (BGE "Set Scene" / GDevelop go-to-scene) |
| `add scene "<name>"` | `add scene "HUD"` | loads the scene **on top of** the current one (overlay/UI): nothing already in the level moves, and the overlay's actors get their sheets plus `when scene starts` |
| `unload scene "<name>"` | `unload scene "HUD"` | removes one additive scene (closes the overlay); never unloads the last scene |
| `quit game` | `quit game` | closes the game |

**Target qualifiers:** any target position/actor can carry **`nearest <thing>`** to pick the closest
actor, e.g. `look at nearest Enemy` â€” or **`random <thing>`** to pick any actor with that name,
e.g. `goto random Waypoint` (waypoint wheels, patrol routes).

**Random values:** anywhere a `<value>` is allowed, **`random <min> to <max>`** is also legal â€”
one grammar rule, no new keyword:

```
set Dice to random 1 to 4
wait random 0.5 to 2 seconds
move forward speed random 2 to 4
```

**Spawn rotation:** a spawned copy inherits `me`'s rotation, so a bullet spawned at `me` with
`move forward` flies the exact direction you happened to be facing when it was spawned.

**Spawn sources:** every spawned actor remembers what produced it — the template name for
`spawn <thing>`, the asset name for `spawn prefab "<name>"`, the scene name for
`spawn scene "<name>"` — and `my spawnedFrom` reads it (empty for an actor placed by hand).
Repeat prefab spawns get numbered names (`Hero`, `Hero 2`, …) so each copy stays individually
targetable; the sheet keeps the asset name, so `send message` and name lookups still reach it.

Runtime knobs are okay as a single **speed/value** word â€” no math expressions, no nesting math.
`Score + 1` is *only* legal inside `set Score to Score + 1` (one-op form: `+ - * /`).

### Physics-native movement (opt-in)

`move` and `goto` set `Position` every tick by default — no colliders required, and the
shooter example stays exactly as written. To let an actor move through a physics body
instead (collision response, CCD, slopes, steps), tick **Use Physics Movement** on its
`EventSheet`. That one checkbox does not pick a movement type by itself — **the body on the
actor picks the flavour**:

- **`CharacterController`** (recommended for walking characters): collide-and-slide `Move`,
  so walls stop the actor, slopes up to the limit are walkable, and small steps are climbed.
  This is the body Task 12 adds; it needs no `RigidBody`.
- **Dynamic `RigidBody`**: driven by `LinearVelocity`; physics shoves it, gravity applies,
  and `UseCCD` prevents fast tunnelling.
- **Kinematic `RigidBody`**: moved with a swept `AddMovement` step instead of a teleport, so
  PhysX reports trigger/collision contacts along the way.
- **No body, or the flag off** = the old transform path. Existing games are untouched.

A `CharacterController` wins when an actor somehow carries both bodies: it is the
purpose-built walking body.

- **The opt-in is per actor, not per line.** Movement is a property of the body, so one
  checkbox covers every `move`, `goto`, `stop moving` and `push` that actor runs. No new word.
- **Setup contract:** the actor carrying the `EventSheet` should be the body itself, with the
  mesh and colliders as its children — the body's motion then carries the visuals. If the
  body is a child of the named actor, the runtime warns once (the mesh would stay behind).

**Character body settings (Task 12 A):** a `CharacterController` has its own inspector, and
that stays the source of truth. For convenience the `EventSheet` can also apply a capsule at
start: tick **Configure Character Body** and set `Character Radius`, `Character Height`,
`Character Step Offset`, `Character Slope Limit`, `Character Ground Check Distance`. The
defaults match Flax's own (radius 50, height 150, step 30, slope 45, ground check 10 — Flax
units are centimetres). Unticked = nothing is overwritten. Values are clamped so the step
offset can never exceed the capsule height.

**Dynamic bodies** are driven by `LinearVelocity`: move direction × speed, keeping the
current vertical speed when the move has no vertical part (gravity still works). `stop
moving` brakes the horizontal velocity; `goto` brakes on arrival.

**How `move` / `goto` / `face` use the body (Task 12 B):**

- `move forward/back/left/right speed <n>` resolves the direction from the actor's facing and
  hands it to the chosen body — `LinearVelocity` (dynamic), a swept `AddMovement` (kinematic),
  collide-and-slide `Move` (character), or `Position` (transform).
- `goto <thing>` bakes the same NavMesh path in every mode. Transform moves snap to each
  waypoint; physics bodies are **never teleported** — they move at the authored speed and use a
  landing speed on the tick that reaches a waypoint, so they stop exactly on it instead of
  overshooting (dynamic) or stalling a frame (character). Arrival still brakes/teleports as
  before. `goto <thing> facing forward` turns the nose toward the next waypoint (Task 7 E).
- `face <thing>` stays rotation-only (`LookAt`) and never fights the body's translation.

**Ground, slopes and collisions (Task 12 C):**

- **The event runtime feeds the character's gravity.** `CharacterController.Move` applies no
  gravity, so the bridge keeps a per-actor vertical speed, uses the scene's `Physics.Gravity`
  (981 cm/s² by default) and passes the resulting displacement to `Move` every tick. The
  controller's own `AutoGravity` is switched off (with a once-per-actor warning) so gravity is
  never applied twice.
- **Ground detection gates horizontal movement.** A short downward ray starts just below the
  capsule (using `OriginMode` to find the feet) and reaches out by the sheet's **Character Ground
  Check Distance**; `IsGrounded` is the fallback. While airborne the character falls straight
  down (no air steering) until it lands. Grounded ticks add a tiny downward stick so the
  character follows slopes and small steps instead of stepping off and falling each frame.
- **Slope limit** is enforced twice: the ground probe rejects a surface whose normal is steeper
  than `SlopeLimit` (so a too-steep face is not ground and the character slides/falls), and the
  controller's collide-and-slide `Move` refuses to climb it. Moving down a slope stays attached
  because of the ground stick.
- **Step up** is the capsule's own `Character Step Offset` (30 by default): small ledges are
  climbed by `Move`, taller walls are not.
- **Walls stop/slide.** A `CharacterController` slides along walls, a dynamic body is resolved by
  PhysX (and the runtime enables `UseCCD` so a fast move cannot tunnel), and a kinematic body
  uses a swept `AddMovement`. Transform movement stays a plain position write — still the
  default for games with no colliders.

**Word integration (Task 12 D):**

- `move forward/back/left/right` is always resolved in the actor's **local axes** (the facing
  rotates the direction), then handed to whichever mode is active — the same vector drives a
  transform step, a body's velocity, a swept move or a character's collide-and-slide.
- `turn left/right degrees <n>` is rotation-only and instant. In Flax a positive Y yaw turns
  local forward (+Z) toward +X, i.e. to the actor's **right**, so left is a negative yaw and right
  is positive. Any other direction word is rejected with a warning instead of silently turning
  the actor the wrong way.
- `look at` and `face` change **orientation only** (`LookAt`), never position. `look at` is a
  one-shot; `face` is a continuous tracker, and a new `face` target replaces the previous tracker
  so two trackers cannot fight over the same orientation each tick.
- `push <target>` follows the **target's own** opt-in and body, not the pusher's: dynamic body =
  `AddForce(..., ForceMode.Impulse)`, `CharacterController` = collide-and-slide `Move`, kinematic
  body = swept `AddMovement`, and no body / flag off = the old one-tick transform nudge.
- `teleport me to <thing>` is an explicit warp for spawns and checkpoints: it cancels the
  subject's ongoing moves (so a path or face tracker cannot drag it back), clears the stored
  character fall speed, zeros a dynamic body's linear/angular velocity, then writes `Position`.
  Physics collision is bypassed on purpose.

**Body settings that ship with the shooter:**

| Actor | Body | Settings | Why |
|---|---|---|---|
| `Bullet` | dynamic | `EnableGravity = false`, `UseCCD = true`, trigger `SphereCollider` | at speed 80 a thin trigger must not tunnel; CCD is a dynamic-body feature |
| `Player` / `Enemy` | kinematic | `EnableGravity = false` (or the transform path) | exact arcade speed, not shoved by bullets; `hide me` / `wait` / `show me` resumes because the body follows `IsActive` |
| Walking character (armature) | `CharacterController` | radius 50, height 150, step 30, slope 45, ground check 10 (`Configure Character Body` defaults) | collide-and-slide walking: walls stop it, slopes and small steps just work; no `RigidBody` needed |

### Armature characters (the BGE way)

Animated characters work like the old Blender Game Engine Action Actuator: pick an animation,
play it by name. You never touch Flax's visual Anim Graph editor by hand and no graph asset has to
be authored, assigned or wired: the bridge builds a tiny virtual Anim Graph per clip in code, so
each clip simply plays. The event words do the rest:

- `loop anim "animation walk"` finds the `Animation` asset whose name ends in "walk" (folder,
  extension and case are ignored) and repeats it -- exactly like BGE playing an Action by name.
  `play anim` uses the same lookup but plays once and holds the last frame. No nodes, no wires.

**Setup contract for a character** (same muscle memory as naming Actions for bricks):

1. Name each Action carefully in Blender -> export to Flax (each becomes an `Animation` asset).
2. Add an `AnimatedModel` to the character and assign the exported `SkinnedModel`. The
   `EventSheet` may sit on the model itself or on a parent actor.
3. Match names: what you write in the `anim` word must match the clip name. Verbose imported
   names still answer to their tail, so `"walk"` finds `BGE Dragon 2.07 animation walk`.

**How the words behave (Flax 1.13):**

- `play anim "<name>"` -- one shot; the last frame is held.
- `loop anim "<name>"` -- repeats until something else plays.
- `stop anim` -- restores the previous clip, or the bind pose when there is none.
- `when anim "<name>" ends` -- fires once, after a one-shot's `Animation.Length`; the name match
  ignores case.
- Repeating the same word while that clip is still playing is a no-op, so putting
  `loop anim "animation walk"` under `when W is held` does not restart the clip every tick.
- Assigning a virtual graph overrides any graph authored on the model. That is intentional:
  the event words own the animation.

**Known limitation:** v1 swaps graphs instead of blending, so switching clips can pop. If
cross-blending becomes necessary, ship one graph with an Animation Slot node and use
`PlaySlotAnimation(...)` with blend-in/blend-out times.

**Friendly errors in BGE spirit:** playing an unknown name lists the project's clips --
`I don't know the anim "walked". This project has: bge dragon 2.07 animation idle pose, bge dragon 2.07 animation run, bge dragon 2.07 animation walk.`

Example (a walking, shooting character -- animation only, logic is the usual syntax):

```
# --- Player.events (armature character) ---
when W is held
  move forward speed 5
  loop anim "animation walk"

when W is released
  loop anim "animation idle pose"

when Left Mouse is pressed
  play anim "dragon_fire"
  spawn Bullet at me

when anim "dragon_fire" ends
  loop anim "animation idle pose"
```

Rules: `play anim` runs once and holds the last frame; `loop anim` repeats until something else
plays; playing a new animation replaces the current one; `when anim "..." ends` fires for
one-shots only.

### Menus & game UI (the scene-switching way)

Menus are **another scene**, not a special system â€” the old BGE habit (`Set Scene` actuator),
also GDevelop's go-to-scene. Same rules as everywhere else apply:

- **Visuals live in the editor, logic lives in text.** You place buttons, texts, panels and
  images as scene actors (any name: `PlayButton`, `ScoreLabel`, `PausePanel`) in the Flax editor.
  The words never draw anything â€” they click, show, hide and fill them, like any other actor.
- **A menu scene + a `Menu.events` global sheet = a working menu.** Menus come and go by
  `load scene`; in-game UI (HUD, panels) is just actors that `show`/`hide`.

Overlays that live in their own scene use `add scene "<name>"` and close with `unload scene "<name>"`; `load scene` wipes every scene, overlays included.

```text
# ------ Menu.events (global sheet, lives in the Menu scene) ------
when scene starts
  set Score to 0
  show PlayButton
  show QuitButton

when PlayButton is clicked
  load scene "Level1"

when QuitButton is clicked
  quit game

# ------ HUD, inside Level1 ------
when scene starts
  show ScoreLabel
  set text of ScoreLabel to 0

when Score changes
  set text of ScoreLabel to Score
```

Note the last event: `when <var> changes` is allowed next to `becomes` â€” same watcher, any value.

**HUD details (and the one magic name):**

- A label is a `UIControl` actor holding a `FlaxEngine.GUI.Label`, parented under a
  `UICanvas`. Screen-space canvases resize with the window, so anchor the labels.
- `show text` / `hide text` use the actor named **`GameText`** - the only name the words
  assume. Create one if you want centre-screen messages.
- `set text of <name>` takes any label actor by name. Naming a panel works when it owns
  exactly one label; with several, name the label itself.
- A label hidden with `hide <name>` comes back on the next `show text` / `set text of`.
- Labels keep the text the editor saved until an event writes one; an empty `Text` means
  nothing shows yet.

**Sound names:** clips are looked up by name, so folders, the `.flax` extension and letter
case never matter (`"shot"`, `"Shot.WAV"`, `"sounds/shot.flax"` all work). A miss warns once
and lists the clips the project has. `stop sound` stops this object's sounds; on a global
sheet it stops every sound the game started.

**Bridge note:** `UIControl`, `AudioSource` and `AnimatedModel` are **actors**, not
scripts - the bridge finds them as actors (`FindActor<T>()` / `as`), never `GetScript<T>()`.

Pause menus (`pause game` / `resume game`) are deliberately **not** in v0.1 â€” the tricky rule
("UI keeps ticking, world actors freeze") needs its own decision; see Section 9.

### Enemy brains & navigation (the BGE way)

Blender's Game Engine never needed behavior trees to make an enemy patrol, spot the player or
attack: you stacked ordinary logic bricks on top of each other and used a flag or two as the
"mode". We keep exactly that shape â€” **the event text *is* the behavior tree**:

- **States are `my` variables.** `my mode is "chase"` replaces the BT blackboard; separate small
  events replace BT branches. In fact, readable at 3 AM beats clever graphs here.
- **Sight is an edge, not a stream.** `when me sees Player by 8` fires *once* when sight starts;
  `when me stops seeing Player` fires *once* when it ends. No tick-spam, no extra flag juggling.
- **Pathfinding is one word.** `goto <thing>` walks there using a **baked NavMesh** â€” walls
  avoided for free by the engine. `when me arrives at <thing>` tells you it got there.

**Setup contract for an enemy** (visuals in the editor, like always):

1. Bake a **NavMesh** for every level once (scene step, one click, baked in the editor).
2. Drop actors named `Waypoint` where the enemy should wander between.
3. Write the brain below â€” no AI tool opened at any point.

```text
# ---------------- Enemy.events (patrol -> chase -> attack) ----------------
when me appears
  goto random Waypoint                  # pathfinds around walls automatically
  loop anim "animation walk"

when me arrives at Waypoint
  wait random 0.5 to 2 seconds
  goto random Waypoint

when me sees Player by 8                   # one-shot the moment the player is spotted
  set my mode to "chase"
  loop anim "animation run"
  goto Player

when every 0.2 seconds                     # chase keeps re-targeting the moving player
  my mode is "chase"
  goto Player

when me stops seeing Player                # went out of sight â€” resume patrol
  set my mode to "patrol"

when me is near Player by 1.5
  my mode is "chase"
  play anim "Attack"
  send message "hurt" to Player
```

**Why not Flax Behavior Trees (and where they live):** Flax ships a full BT system (visual
graphs with MoveTo nodes, blackboards, decorators). For non-programmers, exposing it would mean
learning a second logic system â€” while our stacked events already express the same shapes in a
familiar text. Behavior Trees stay parked as an **advanced escape hatch**, on the same shelf as
custom C# words, for the day a project genuinely needs them.

### Items & the bag (the D2 loop)

An RPG's inventory needs no second system, no database, no tables. An **item is just an actor
whose `.events` file carries its stats** â€” the same idea as every other actor in the system.

- **The bag is one of `my` variables, understood as content.** `add Sword to my bag` puts a name
  in; the bag holds things **by name and count** (`Sword x3`).
- **`equip <thing>` links the item's sheet to me.** The item's `when me gets equipped` event
  fires (typically: push stat changes onto the owner); `unequip` / `drop` run the mirror event
  and take the stat changes back. The link lasts until unequip â€” **an item worn is living
  logic on you**: while its equip/unequip events run, `me` is the **wearer**, so
  `add 5 to my damage` in `Sword.events` raises *the wearer's* stat. Out in the world (dropped,
  lying on ground), the item's sheet runs normally with `me` = the item itself.
- **Loot is already there.** `chance 30%` + `spawn Sword at me` on an enemy's death is the whole
  loot table. No drops word needed.
- **Bag UI reads as text first:** `set text of BagLabel to my bag` shows everything held
  (`Sword, Potion x3`). A proper grid inventory is parked (Section 9) until the text proves the
  bag words need nothing else.

```text
# ---------------- Sword.events (item actor) ----------------
when me gets equipped
  add 5 to my damage

# ---------------- Player.events ----------------
when me touches Sword
  add Sword to my bag
  play sound "pickup.wav"

when 1 is pressed
  unless my bag has Sword
  equip Sword

when Left Mouse is pressed
  send message "damage" to nearest Enemy

# ---------------- Enemy.events (the receiving end) ----------------
when message "damage" arrives
  add -(other's damage) to my health      # possessive read: the attacker's stat
```

**Honest limits:** the math stays one-operator inside `set â€¦ to â€¦` â€” so an ARPG formula like
`damage = weapon Ã— (1 + strength Ã· 10)` can't be written in v0.1. Compose it (messages and helper
variables) or park richer math for later (Section 9); it stays a deliberate shape, not a hidden
surprise.

### Point & click adventures (the composition showcase)

The adventure genre is the system's proof of strength: almost everything a point & click game
does is **composed from existing words, not new machinery**. Clicking things, walking to clicks,
inventories, locked doors, dialogue â€” all verbs below are words already cataloged plus a
handful of additions of this era (`hovered`, `goto there`, the camera pair).

```text
# --- Door.events (the unlock puzzle; using an item = holding something) ---
when me is hovered
  show text "A locked door."

when me is clicked
  unless Player's holding is "Key"
  play sound "locked.wav"
  show text "It's locked."

when me is clicked
  Player's holding is "Key"
  play sound "unlock.wav"
  set my Locked to false
  remove Key from Player's bag
  show text "The door creaks open."
```

Dialogue is **not a system â€” it's shown menu parts** (the Menus batch already gives all pieces):

```text
# --- NPC.events: talk, options appear ---
when me is clicked
  show DialogPanel
  show ChoiceA
  show ChoiceB

# --- ChoiceA.events: a button actor picks a branch ---
when me is clicked
  set text of DialogPanel to "Can you fix my flask?"
  hide ChoiceA
  hide ChoiceB
  set my Asked to true
```

**Walking to a clicked spot** (adventure cameras love this): the ground is just another actor,
`there` is the click's position:

```text
# --- Player.events ---
when Ground is clicked
  goto there

when me arrives at there
  loop anim "animation idle pose"
```

**Verdict recorded:** dedicated dialogue machinery (portraits, typewriter text, voice lines,
follow-ups) is deliberately parked in Section 9. The menu-button composition above is the v0.1
way â€” patient, and zero new words.

---

## 6. Architecture (on Flax)

### The one-time promise

**Game logic is never compiled, generated, or transformed into C#.** The `.events` files are
**data** â€” like a level file. The C# that exists is a **fixed interpreter**, written once,
identical for every game. Users never write or see it.

```
your .events file                EventRuntime (fixed C#, written once)
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”      â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ when other touches me: â”‚      â”‚ 1. reads the text file                    â”‚
â”‚   add 1 to Score       â”‚ â”€â”€â”€â–¶ â”‚ 2. parses into an in-memory event tree    â”‚
â”‚   destroy me           â”‚      â”‚ 3. every game tick: checks triggers       â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜      â”‚ 4. runs matching actions in order         â”‚
                                â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

This mirrors how GDevelop actually works internally (a fixed engine runs the events; nothing is
generated per-sheet).

### What C# exists, and why (against future confusion)

| Component | Role | Written how often |
|---|---|---|
| `EventLexer` / `EventParser` | text â†’ event tree, with friendly errors | once |
| `EventRuntime` (scene manager) | owns all sheets, ticks them, routes `me`/`other` | once |
| `EventSheet` | the user-facing component actors carry: Event File field + collider hooks; clones inherit it | per actor |
| `Words.*` (TriggerWords, ActionWords) | each word = tiny C# function bridging to the Flax API | per word (~40 files, each ~5 lines) |
| `EventAsset` | wraps the text file as a Flax asset, so it shows in Content window | once |

All of it lives in one plugin/module. **New vocabulary = one new small C# entry. Nothing per-game is generated.**

### Files on disk

```
Project/
  GameLogic/
    Game.events          # global sheet
    Player.events        # auto-attaches to actors named "Player"
    Coin.events
    Bomb.events
```

- **EventSheet component (now shipped):** select the actor → *Add Script → EventSheet* → set its
  **Event File** (blank = actor name). Clones inherit it; one file can drive many actors.
- **Auto-attach fallback:** an actor named `Coin` + a file `Coin.events` = logic sticks on.
  Zero clicks — still there for throwaway tests. Explicit components always win.

### Trigger mechanics: polled vs. pushed

Some triggers must be checked every frame (**polled**): keys held, timers, variable watches,
distance checks. Others arrive as Flax callbacks (**pushed**): collisions (`OnTriggerEnter`),
input presses (`Input.GetAction`), `OnStart`.

`EventSheet` subscribes to the pushed ones once. Every tick the runtime walks all sheets
top-to-bottom: pushed events fire their queue first, then polled events are checked.

### Contacts: distance, colliders, or both

`EventsManager.Contact Mode` chooses how `touches` is detected:

- **`Distance`** — no colliders needed. Each tick, active actors are compared by name and
  bounding-sphere distance; a pair fires once on enter, and again only after it separates.
- **`Colliders`** — Flax `Collider.TriggerEnter` events (the final target setup: Bullet
  `SphereCollider` trigger + Enemy `BoxCollider` solid, both under kinematic `RigidBody`s).
- **`Both`** — collider pushes plus distance polling, deduped so a pair fires once.

`overlaps` stays **distance-based in every mode**; the Player needs no collider. Contact
ownership walks up from a collider to the nearest actor carrying an `EventSheet`
(`SphereCollider` → `RigidBody` → `Bullet`).

### Hot reload

- Watching the `.events` files. On save: re-parse; if valid, swap the live event tree **in place**,
  keeping variable values whose names still exist. Running the game during edit = you see the fix
  the same second.
- Parse errors never kill the running session â€” the old tree keeps playing until a good version arrives.

### Performance envelope (honest note)

Interpreted text is slower than compiled C# â€” and irrelevant at hobby scale. A sheet check is a
few microseconds; 200 events Ã— 300 actors â‰ˆ nothing per frame. optimization passes (dirty checks,
skip events with no trigger signal, selective update when paused/offscreen) are a later
milestone, only if a project actually needs it.

---

## 7. Example Game: "Top-Down Shooter" (written entirely in the syntax)

Scene setup (in the Flax editor, top-down camera): actors named **Player**, **Bullet**, **Enemy**,
**SpawnPoint** â€” plus a global **Game.events** sheet. That's every concept of Sections 3â€“4 in one
place: attached movement & shooting on `Player`, spawn-and-forget behavior on `Bullet` and `Enemy`,
and scoring/game-over on the global sheet.

```
# ---------------- Player.events ----------------

# ---- movement ----
when W is held
  move forward speed 5

when S is held
  move back speed 5

when A is held
  move left speed 5

when D is held
  move right speed 5

# ---- shooting: aim at the closest enemy, fire ----
when Left Mouse is pressed
  look at nearest Enemy
  spawn Bullet at me
  play sound "shot.wav"

# ---- getting hurt: brief invulnerability by hiding ----
when me overlaps Enemy
  unless my Invulnerable is true
  play sound "hurt.wav"
  add -1 to Lives
  set my Invulnerable to true
  hide me
  wait 1 second
  show me
  set my Invulnerable to false

# ---------------- Bullet.events ----------------
when me appears
  move forward speed 20

when me touches Enemy
  send message "hit"
  destroy me

when every 1 seconds
  destroy me

# ---------------- Enemy.events ----------------
when every 0.1 seconds
  move toward Player speed 2

when message "hit" arrives
  add -1 to my health

when my health becomes 0
  add 1 to Score
  play sound "boom.wav"
  destroy me

# ---------------- Game.events (global sheet) ----------------
when game starts
  set Score to 0
  set Lives to 3
  show text "Shoot the enemies!"
  wait 2 seconds
  hide text

when every 5 seconds
  unless Score is 5
  spawn Enemy at SpawnPoint

when Score becomes 5
  show text "You win!"
  wait 3 seconds
  restart scene

when Lives becomes 0
  show text "Game Over"
  wait 3 seconds
  restart scene
```

Every concept from Sections 3â€“5 appears in this ~60-line game: attachment (EventSheet components or the name fallback), `me`/`other`,
global variables (`Score`, `Lives`) vs. per-object ones (`my Invulnerable`, `my health`),
`nearest`-qualified aiming, spawning bullets that inherit your facing, message events between
objects (`send message "hit"`), timers (`every N seconds`), waits, mouse input â€” and a full
win/lose loop. That is the entire tutorial pitch.

**Movement note for M2:** `move left/right/forward/back` are top-down (world-relative) by default;
a future camera/`relative to camera` knob is logged in Section 9.

---

## 8. Milestones

| Milestone | Deliverable | Skip-if blocker |
|---|---|---|
| **M1 â€” this doc** | You can read it and agree with yourself in 3 months. | â€” |
| **M2 â€” heart** | Parser + EventRuntime running in Flax: the "Top-Down Shooter" game playable from text. | Friendly errors, hot reload |
| **M2Â½ â€” armature** | BGE-style animation words (`play anim` / `loop anim` / `stop anim` / `when anim ends`) + the shipped template Anim Graph, verified on a walking, shooting character. | depends M2 |
| **M2Â¾ â€” items & bag** | The D2 loop: bag words, equip/fire-item-events, possessive stat reads, chance-loot â€” verified by pickup â†’ equip â†’ stats change. | depends M2 |
| **M2â…ž â€” point & click** | `hovered` trigger, `goto there`, minimal camera pair â€” verified by a playable locked-door puzzle with menu-composed dialogue. | depends M2 |
| **M3 â€” quality** | Editor plugin window: syntax coloring, error markers (red squiggles per line), word autocomplete via Ctrl+Space, docs-on-hover (each yellow word gets its one-line example as tooltip). | M2 |
| **M4 â€” comfort** | More words (animation phrases, save/load variables, camera words, spawn from prefab-folder), plus a small "Word Book" reference page generated from the code. | M3 |

Order dogma: **M2 before M3.** A working interpreter with ugly plain-text files beats a pretty
window around a broken runtime.

---

## 9. Open Questions (log, not blockers)

- **`or`-chains:** skip in v0.1 (use two `when` blocks) â€” revisit with real usage data.
- **Physics vs. kinematic movement (decided in Task 4 C, extended by Task 12 A):** transform move
  stays the default. Physics-native movement is a per-actor opt-in (`EventSheet` → *Use Physics
  Movement*), and the body chooses the flavour: **`CharacterController`** gets collide-and-slide
  walking (slopes, steps, wall sliding), dynamic bodies get `LinearVelocity`, kinematic bodies get
  swept `AddMovement`, no body/flag off keeps `Position`. The bullet is the dynamic experiment
  (`EnableGravity = false`, `UseCCD = true`); Player/Enemy stay kinematic so speed is exact and
  bullets don't shove them. `push` follows the target's own opt-in and body: an impulse on
  dynamic bodies, a swept move on kinematic ones, collide-and-slide `Move` on a character, and a
  transform nudge when the flag is off.
  **Decided in Task 12 C:** the event runtime feeds the CharacterController's vertical speed
  (scene `Physics.Gravity`, ground probe + ground stick); `AutoGravity` stays off so gravity is
  applied exactly once. Horizontal movement only applies while grounded. Dynamic bodies keep
  PhysX gravity and get `UseCCD` enabled automatically; kinematic bodies stay swept.
  **Decided in Task 12 D:** `turn left/right` is an instant local yaw with Flax's sign fixed
  (left = negative Y, right = positive Y), `look at` / `face` stay rotation-only, and `teleport`
  is the one word that deliberately warps position while cancelling moves and body velocity.
  Still to feel in the editor: whether Player/Enemy should ever go dynamic and whether `goto`
  should rotate toward its next waypoint (Task 7 E).
- **Persistence:** do global variables survive scene *reload* (not just scene *change*)? Proposal for now: yes, and a `wipe game variables` action to explicitly reset.
- **`me touches` without physics:** supported now via `Contact Mode = Distance` (bounding-sphere
  contact); `overlaps` remains distance-based in every mode. The two words still overlap
  conceptually — maybe rename to `me gets close to`? Feel it in M2.
- **Scenes with same-name actors** (multiple Coins): currently all copies of the actor get the
  sheet â€” good. But what distinguishes copies in text? Maybe auto unique-id word (`my copy number`)
  rather than solving namespacing now.
- **Animation cross-blend:** how long should the blend be when one animation replaces another?
  Proposal: something small (~0.2 s), tuned by feel in M2Â½. Also parked: does holding W restart
  `loop anim "animation walk"` every tick (it should be harmless), and should `move` words auto-play their
  own animation (v0.1 answer: no â€” animation is always explicit, like BGE).
- **Pause game:** `pause game` / `resume game` are deliberately parked. They need their own
  rule for *what keeps ticking while paused* (proposal: menu/UI sheets and clicks stay live,
  world actors, timers and physics freeze) â€” decide when a game actually needs it, likely
  together with the Menus batch (Task 6).
- **Direct-vector chase needs a poll, not `near` (Task 7 G1):** `move toward` steps the subject
  once by `speed × deltaTime`, and `when me is near <thing> by <dist>` is an **enter-edge** trigger
  (fires once, re-arms only after the target leaves). So the line-of-sight chase cannot be steered
  from `near`; it needs `when every 0.02 seconds` (one call per frame) + a `my mode is "chase"`
  gate + `move toward player speed 60`. A patrolling `goto` must also be cancelled with
  `stop moving` when the chase/hunt starts, because an ongoing move is not replaced by a `move`
  word — otherwise the NavMesh path and the direct vector fight. `sees` stays a trigger, not a
  condition; the `my mode` string carries the state. No `path exists` word exists (parked), so the
  movement verb itself is the NavMesh/no-NavMesh switch.
- **Wall negotiation for the no-NavMesh chase (Task 7 G2, decided):** the chase rounds obstacles
  through a **`move toward <thing> speed <n> around walls`** qualifier rather than a new action
  word — one verb, one optional phrase, and every existing sheet still walks a straight line.
  Mechanism: **corner sampling primary**, wall-tangent fallback. Each chasing tick casts one
  straight probe toward the target (length `min(distance, 40)`, measured from the collider centre
  plus the actor's half-width). When it is clear nothing else happens — **no fan rays**. When it
  is blocked, a fan at ±15°, ±30°, ±45° … is sampled smallest-first for the first clear ray (the
  tightest path), capped at **7 rays per tick including the probe**, so only ±15/30/45 fit at the
  default budget. If no fan ray is clear, the hit normal gives the wall tangent
  (`cross(up, normal)`, flipped toward the target) and that is driven for the same short window.
  A chosen side is kept for **0.3 s** (hysteresis: no left/right vibration); an actor that moves
  less than a quarter of its expected step for **0.4 s** forces one sampling cycle even when the
  probe reads clear (shallow concave corners). The heading goes through the normal `MoveSubject`,
  so transform, dynamic, kinematic and CharacterController movement all behave the same. Rules
  live in `Runtime/Steering.cs` (pure, unit tested); `FlaxActions` only supplies the raycast.
  Guards: it only runs for `move toward ... around walls` — never for `goto` (the NavMesh already
  routes) and never for `hunt` (G1's arc); a plain `move` or `stop moving` forgets the committed
  side; self/children and the chase target/children are ignored by every probe. Walls therefore
  need colliders — the same requirement `me sees` already has — and probes run at the collider
  centre height like the sight ray, so a low crate is not seen.
  **Events-side note:** G1's sheet flips to `hunt` the instant the sight ray breaks, which is
  exactly when a corner needs negotiating, so the G2 companion sheet (`Enemy_Walls.events`) adds a
  short `round` state: sight lost → keep the wall-aware chase for 1.5 s → *then* hand over to the
  blind `hunt` arc → patrol. `sees player` cancels the hand-off by flipping the mode back.
  **Documented limit:** a deep U-shaped pocket can have a "clear" lateral ray that never leaves
  the pocket; the chase then stalls and `hunt`/patrol takes over. Agent-to-agent avoidance,
  separation/flocking and full local pathfinding stay out of scope (other actors are simply
  obstacles to the probes). **Vocabulary friction:** there is still no ray/normal query word
  (`me is blocked`, `path exists`), so this lives in C# while `sees` stays a trigger rather than
  a condition; the qualifier reads well enough that `chase <thing> speed <n>` was not added.
- **Pathfinding edge cases:** what happens when `goto` can't reach the target (player on a
  pillar, waypoint inside a wall)? Proposal: `goto` gives up after a few seconds and â€” for now â€”
  quietly stops walking; a `when path fails` trigger is parked for when a game actually needs it.
- **Forward-facing goto vocabulary:** `goto <thing> facing forward` is opt-in (Task 7 E). There is
  no `facing backward` or per-speed turn smoothing yet â€” rotation is an instant `LookAt` toward
  the next waypoint each tick, matching `face`. Park a turn-speed qualifier if a game needs easing.
- **Patrol precision:** `random <thing>` is enough for wandering, but true *routes* (visit A then
  B then C in order) are clunky in v0.1. A `next <thing>` qualifier (cycle through same-name
  waypoints) is parked until a project asks for it.
- **Grid inventory UI:** v0.1 shows the bag as text (`set text of BagLabel to my bag`). A real
  bag window with runtime-filled slots is a bigger build, parked until the text bag proves
  the bag words are complete.
- **Bag counts in text:** `my bag has Sword` handles "do I own one". "Give me the count"
  (`my bag count of Sword is higher than 2`) needs a value-expression â€” parked with the math
  rules until needed.
- **Multi-op math:** `set damage to weapon * (1 + strength / 10)` is not v0.1 (one-op rule).
  Parked: either a `formula` line or a slightly grown expression shape â€” decide when an actual
  game needs it, don't pre-grow.
- **XP level-up thresholds:** automatic "level up when XP crosses Level*100" needs multi-op
  math (same parking). For now: one `becomes` event per level cap, or a helper variable.
- **Floating combat text:** damage numbers popping above hit actors are a nicety, not a gap
  (`show text` + spawn covers the need later). Parked as flavor.
- **Dedicated dialogue machinery:** portraits, typewriter text, voice lines, follow-up-choice
  chains. v0.1 composes dialogue from the Menus batch instead (see Point & click section);
  real machinery joins only when an actual game outgrows buttons-and-panels.
- **Richer camera work:** pans, zooms, cutscene framing beyond `camera looks at` /
  `camera follows`. Minimal pair ships with the point & click batch; the rest is parked.
- **Saving:** do global variables persist to disk across game restarts? Later; a `save`/`load game`
  word pair is the natural shape when it matters.
- **Project name:** "Event Manager" is a placeholder. Candidates for a real name welcome.
- **`unless my bag has Sword` gate:** DESIGN §5's Player.equip line reads `unless my bag has Sword /
  equip Sword`, which only equips when the sword is NOT already in the bag — the opposite of the
  intended "press 1 to equip" flow. Once equipped the sword leaves the bag, so the gate never lets
  you re-equip after a drop. Either drop the `unless` (plain `equip Sword` on `1`) or gate on an
  equipped flag instead. Noted here so a later era can fix the wording without changing the runtime.
- **Negated possessive reads:** `add -(other's damage)` does not parse — the lexer gloms `-` onto
  `other's` as one token. Work around it with `set my LastDmg to other's damage` then
  `set my health to my health - my LastDmg` (note the `my` prefix on the right-hand read).
- **Arcade lane enemy (Task 7 G3):** the cheapest variant — `when every 0.2 seconds` +
  `move toward player speed 60` + `me is near player by 1.5` + a spawner with an
  `EnemiesAlive` cap — needed no new words, but three runtime details surfaced. (1) A
  **condition** `me is near <thing>` looks the actor up by exact name through the runtime's
  registered sheets, so the portable lowercase `player` that triggers accept did not resolve
  there (the spawner had to write `me is near Player by 60`). **Fixed in the G3 follow-up:**
  conditions now look names up through `EventRuntime.FindThing` — `player` / `nearest player`
  are the closest player to the condition's subject, `player 2` / `Player2` one local player,
  everything else an ordinary name — and the near condition parses those two-word player
  references, so `me is near player by 60` reads the same in a trigger and in a condition. (2) Health is the per-unit
  stat: `add -10 to my health` canonicalises to `my Health`, but `when my health becomes 0`
  watches the plain variable name, so a mixed-case sheet silently keeps two values — write
  `my Health` on both lines (or make the watch read stats too). (3) `TK_Bullet`'s damage only
  answers actors literally named `Enemy`, and a lane names its enemy `Arcade_Enemy`, so the
  enemy takes the hit from its own sheet (`when me touches Bullet`); a tag- or group-shaped
  contact (`when me touches something with tag Enemy`) is the general fix. A lane-clamp word
  (`stay in lane <z> by <n>`) stays parked — the scene uses the design route instead (walls
  and camera framing).
- **Possessive bag checks across actors:** `my bag has <thing>` and `remove <thing> from my bag`
  only ever address the sheet's own actor (`me`). Another actor (a door, an NPC) cannot read or
  change Player's bag directly, so the locked-door puzzle (Task 9 C) tracks key possession with a
  global flag (`HasKey`) set on pickup instead of checking `Player's bag`. A cross-actor bag
  condition and `remove ... from <actor>'s bag` are parked for a later era.

---

## 10. The one-paragraph pitch

Event Manager is a text-first game-logic system for non-programmers, inspired by Blender Game
Engine's logic bricks and GDevelop's event sheets, but built for plain readable files instead of
panels and wires. You write stacked events â€” a `when` line, optional `unless` lines, then indented
actions â€” in a fixed ~40-word vocabulary, saved as `.events` files that attach to Flax actors
(either by an EventSheet component you place, or automatically by actor name). A once-written C# interpreter runs them live and hot-reloads instantly; no code is ever
generated. The result reads like stage directions for your game, and small games get made.
