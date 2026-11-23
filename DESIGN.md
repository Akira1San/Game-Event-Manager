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
- **Reuse across files: `include "<file>"`.** A sheet can pull another GameFiles `.events` file
  in whole, at the line where the include sits: `include "FPS_Controls"` (quotes, bare name and a
  trailing `.events` all read the same). That is how the first-person input wiring is written once
  —   `GameFiles/FPS_Controls.events` holds W/A/S/D, Shift sprint, Ctrl crouch, Space jump and the
  cursor toggle, `GameFiles/Platformer_Controls.events` holds the walk and the jump, and a player
  sheet is a few feel lines plus the include. A sheet that wants a key for itself (the platformer's
  ladder grab) puts its event **above** the include: events run top to bottom, `climb` registers
  the climb immediately, and the shared line's `unless me is climbing` then skips the walk in the
  same tick. Resolution happens after
  every file parses (`Events/ProgramIncludes.cs`, called by `EventFiles.LoadAll`), so a library may
  name another library; each name is included once per sheet, so writing the include twice cannot
  double-fire a block, and a missing file (with a "did you mean …?"), a loop, or a chain deeper than
  eight files is a friendly error on the including file. Included blocks keep their own line numbers in diagnostics and `my` still
  means the including actor — nothing is copied at runtime, and the included file is not a special
  kind of sheet: attach it to an actor directly and it behaves like any other. The words themselves
  stay in the sheet language; `include` only decides which file they live in, so behaviour is never
  hidden in C#.

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
| `camera follows <thing>` | `camera follows Player` | follows from where the camera was **placed**: the offset is the camera's position minus the target's, so a camera authored to the side of the player stays to the side and keeps its rotation. It eases into that offset (`CameraFollowDamping`, per second, default 6; 0 snaps), so the view trails a moving subject and settles back when it stops; aiming at something else releases the follow. `camera at` still frames from the `CameraHeight` / `CameraDepth` / `CameraPitch` globals — `CameraDepth = -CameraHeight / tan(CameraPitch)` centres, and `CameraPitch 90` is straight down (TK4) |
| `look at <thing>` | `look at Player` | one-shot snap: turn toward the target, once |
| `face <thing>` | `face Player` | continuous: keeps turning toward the target while it moves (BGE Track To) â€” the turret/chaser word |
| `teleport me/other to <thing>` | `teleport me to SpawnPoint` | |
| `save checkpoint` | `save checkpoint` | Task 36 Part F: the acting actor's own transform, written as the per-actor variables `CheckpointX` / `CheckpointY` / `CheckpointZ` / `CheckpointYaw`, so the save file carries it and `my CheckpointX` reads it |
| `respawn at <thing>` | `respawn at Checkpoint2` | the same warp as `teleport` (moves and pushes cancelled, fall speed, jump flags and climbs cleared, a dynamic body stopped); health, state and animation are the sheet's business |
| `push <target> direction <dir> strength <n>` | `push other up strength 10` | follows the target's move mode: impulse (dynamic), swept (kinematic), collide-and-slide (character), else a transform nudge. Add `for <n> seconds` (e.g. `push other back strength 100 for 0.2 seconds`) to keep shoving every tick — that is how TK4 knockback reads; without it the push is one tick |
| `make <target> unmovable` | `make me solid` | physics toggle |
| `destroy <me/other/thing>` | `destroy me` | |
| `spawn <thing> at <thing>` | `spawn Coin at SpawnPoint` | clones an actor already placed in the scene (the template stays put, hidden if you like); `offset <n>` (`spawn Bullet at me offset 30`) slides the copy `n` units along the spot's facing, XZ only |
| `spawn prefab "<name>" at <thing>` | `spawn prefab "Hero" at SpawnPoint` | instantiates an imported `.prefab`; name ignores folder/extension/case; copies get unique names (`Hero`, `Hero 2`, …) and every actor inside runs its own `when me appears` |
| `spawn scene "<name>" at <thing>` | `spawn scene "HeroScene" at SpawnPoint` | loads a `.scene` **on top of** the current scene (additive) and moves only that scene's root actors to the spot — existing actors never move |
| `play sound "<file>"` | `play sound "hit.wav"` | clip **name** only: `"hit"`, `"hit.wav"`, `"sounds/hit"` are the same imported clip |
| `play sound "<file>" at <thing>` | `play sound "shot" at Enemy` | a positional one-shot parented to that actor (attenuates only for a clip imported as 3D) |
| `stop sound` | `stop sound` | stops this object's playing clips (from a global sheet: every clip) — never the music |
| `play music "<name>"` / `loop music "<name>"` | `loop music "menu"` | one track at a time; a new track fades in from the current music level |
| `stop music` | `stop music` | stops the track and keeps the music level where it is |
| `fade music to <n> in <s>` | `fade music to 0 in 2` | n is 0–1; a fade to 0 keeps the authored music level for the next track |
| `duck music by <n> for <s>` | `duck music by 0.6 for 4` | lowers the music by that share, then ramps back up |
| `set music/sound/master volume to <n>` | `set music volume to 0.4` | the three volume groups; they write `MusicVolume` / `SoundVolume` / `MasterVolume` globals |
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
`Score + 1` is *only* legal inside `set Score to Score + 1` (up to three operators, `+ - * /`).

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
- **Camera-relative movement is opt-in** (`Move Relative To Camera` on the `EventSheet`, Task_FPS
  Phase 2): the four planar words resolve against the camera's view heading flattened onto the
  ground instead of the body's facing, so a strafe slides along the screen without rotating the
  actor. A player with a camera child gets the same vector either way while the yaw rides the
  body, but the flag is what keeps strafing a strafe once the body faces somewhere else (aiming,
  a turret, a locked-on target). Without a camera child it warns once per actor and falls back to
  the facing; `up`/`down`, `move toward`, `face` and `look at` are untouched by the flag.
- **Stance** (`crouch` / `stand` / `toggle crouch`, Task_FPS Phase 2) shrinks the actor's
  `CharacterController` capsule to 55% of the authored height (floor 20 cm, step offset re-clamped)
  and sinks the actor by half the lost height, because Flax keeps the actor origin at the capsule
  centre: the feet return to the floor and the head (plus a camera child) drops. Every `move` while
  crouched runs at half the authored speed, so pace variables stay in the sheet, and the stance is
  mirrored into `my Stance` for conditions. Reaching the same stance twice is a no-op, so a held
  key cannot sink the actor further; an actor without a controller still crouches (head only).
  The sink is a **position offset**, so `teleport` / `respawn` drop it before writing the new
  position (the authored capsule comes back and `my Stance` reads `"stand"` — the crouch belonged
  to the old spot), and a new scene forgets all stance bookkeeping because its actors are new
  objects. Without that, a respawned crouched body would land half a capsule underground and a
  later `stand` would lift it into the air.
- **Pace** (`sprint` / `walk` / `toggle sprint`, Task_FPS Phase 3) is the stance treatment applied
  to speed: the word changes a multiplier, never the sheet's speed numbers, so `move forward
  speed my Pace` stays the one place the pace lives and Shift does not need a second copy of every
  line. The multiplier is `my SprintSpeed` (default 2, floored at 1) and it **multiplies** the
  stance factor, so a crouched sprint is 1× — crouching cannot be used to cancel a sprint and
  sprinting cannot be used to cancel a crouch.
- **Stamina is a pool the bridge spends, not a counter a sheet decrements.** `my Stamina` is
  drained at 50/s only while a sprinting actor is actually moving (measured from its position, so
  timing never depends on whether the event tick ran before the bridge tick), refilled at 16/s
  otherwise up to `my MaxStamina` (default 100), and the first tick at 0 forces `walk` and mirrors
  `my Sprinting` back to 0. `sprint` is refused while the pool is empty, so holding the key through
  an exhaustion cannot restart the sprint the moment a trickle comes back. Per-second numbers
  rather than per-tick ones keep the feel identical at 30 and 144 fps, and a sheet that never
  declares `my Stamina` sprints for free — the bridge will not invent a pool on an NPC that merely
  uses the pace words (the same reason stance bookkeeping is keyed by actor). Ownership of the pool
  is explicit: the drain/refill only runs on a sheet that actually used a pace word (its first
  `sprint` / `walk` / `toggle sprint` claims the pool) or whose `EventSheet` ticked **Sprint
  Stamina**. `TK_Player` keeps its own `my Stamina` with `add` lines for the TK4 sprint, so before
  that gate every tick quietly topped it back up to 100 and rewrote a hand-written bar; a sheet's
  own counter is now left exactly as its events wrote it.
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
- `respawn at <thing>` (Task 36 Part F) is that same warp reached by name, so a death block does
  not need a second teleport spelling; the checkpoint itself is four ordinary per-actor variables
  (`CheckpointX` / `Y` / `Z` / `Yaw`), which is why a save file restores the last one for free.
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

**Cross-blending and upper-body layering (Task 33, decided):** v1 swapped graphs instead of
blending, so switching clips could pop. That is now a designed path rather than an open question:
cross-blends and layering go through an authored Anim Graph whose `Animation Slot` node is played
with `PlaySlotAnimation(...)` and its blend-in/blend-out times (0.2 s by default, tuned by feel).
The virtual `InitAsAnimation` graph stays as the zero-setup fallback — nothing has to be authored
for plain clip playback -- and that fallback is the one remaining hard-swap path, because it
replaces the whole pose, legs included. Layering is the case that does require a graph asset; see
*Animation depth and layering* in section 9.

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

**A value can be computed (Task 29):** one idea grew three additions — three operators in place of the
one-operator ceiling, numeric helpers in place of the `if` blocks around caps and rounding, and
`field` in place of copying a data-file number into a sheet. Each one is a *value*, so they compose
without new statements:

```text
when me appears
  set my Damage to field "Damage" of "Sword" in "item" * 2 + my Strength   # a table row inside a formula
  set my Health to clamp my Health to 0 and my MaxHealth                   # a potion can't overfill
  set my Gold to my Gold * 1.1                                             # tax, then round it
  set my Gold to round my Gold
```

What each addition does, and where it stops:

**Math, narrowed but real:** a `set ... to ...` line takes up to **three** operators with normal
precedence (`* /` before `+ -`), so `set my damage to my strength * 2 + weapon damage` is one
readable line (Task 29 Part A). Grouping is still parked: `weapon * (1 + strength / 10)` needs the
sum in a helper variable on the line above, and a fourth operator is a friendly "split this line"
error that names the first half to keep. Anything past that parks for Section 9 - a deliberate
shape, not a hidden surprise.

**Numeric helpers** are value forms too (Task 29 Part B), so a potion never needs an `if` and a HUD
never shows 41.99999: `clamp my Mana to 0 and my MaxMana` (bounds work either way round),
`min`/`max <a> and <b>`, `round`/`floor`/`ceil`/`abs <value>`, and `10% of my Gold` - the
percentage is an ordinary operand, so `round 10% of 55` reads 6, and `add 10% of my Gold to my Gold`
works because `add` takes any value. A helper wraps the single value after it, not a whole formula:
`round my Gold * 1.1` rounds the gold and then multiplies, so store the product and round it on the
next line. They read where the event parser reads a value (`set`, `add`, `deduct`, weapon/ammo/build
forms, text) and they nest, but they deliberately do not leak into conditions or trigger matching.

**Data lookup** (Task 29 Part C) reads a shipped table without inventing a format: `field "Damage" of
"Sword" in "item"` is the `<Row><Field>` global `SwordDamage` that `ItemData.events` already sets, so
editing the data file changes the game with no C# edit and no second copy of the numbers. Row and
field match case-insensitively and ignore spaces (`field "FireRate" of "Shotgun Maniac"` reaches
`ShotgunManiacFireRate`), the result is an ordinary value so it multiplies, rounds and nests, and a
row or field that does not exist warns once - naming the global it looked for, listing the fields
that row has or the rows that have that field, and offering the nearest name through `WordDistance`
(the same two-edit threshold `UnknownWord` and the file loader use, so `Damag` suggests `Damage` and
`Zombiee` suggests `Zombie` while a name with no near match gets no silly guess) - then reads 0.

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
- **Lane variety without a sheet per type (Task 7 G4 phase 2):** the cheapest shape is to name
  the **template actor after its type** (`Zombie`, `Knife`, `Brute`) and read it with
  `enemy type my name`, the word `TK_Gunner.events` already uses. One `.events` file then serves
  the whole lane through explicit `EventSheet` components, the type supplies pace/health/sight as
  data, and `kills my Type` counts each type separately with no `my type is "..."` branching.
  The catch: a template whose name is not a known type warns once and keeps the built-in
  defaults, so the naming is load-bearing — the G3 `Arcade_Enemy` template was renamed to
  `Zombie` for this. Variety is per-cadence (3 s Zombie, 6 s Knife, 9 s Brute under one alive
  cap) rather than random, because there is no `random` form of `spawn`.
- **Knockdown and combo ownership (Task 7 G4 phase 1):** the lane enemy's `my state` is the
  switch — `walk` gates the walk and the swing, a hit flips it to `down` (0.4 s) then `getup`
  (0.2 s) then back to `walk`, and standing up adds one `Combo`. Two details worth keeping:
  the hit block deals damage first and only then gates the flip (`my state is not "down"`), so
  a burst still hurts a downed enemy without restarting its wait blocks; and the `Combo`
  decay lives on the single spawner actor, because a decay block in a shared enemy sheet runs
  once per clone and would drain the global several times faster than one point per two
  seconds. The knockdown is entered with `when state is down` (the state entry edge) so both
  the shot and the melee `hurt` message feed one chain instead of duplicating the waits.
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
- **Parentheses in math:** `set damage to weapon * (1 + strength / 10)` still can't be written -
  `set` reads up to three operators with precedence (Task 29 Part A), but no grouping. Parked:
  either a `formula` line or brackets in the value grammar - decide when an actual game needs it.
- **XP level-up thresholds:** automatic "level up when XP crosses Level*100" needs multi-op
  math (same parking). For now: one `becomes` event per level cap, or a helper variable.
- **Floating combat text:** damage numbers popping above hit actors are a nicety, not a gap
  (`show text` + spawn covers the need later). Parked as flavor.
- **Dedicated dialogue machinery:** portraits, typewriter text, voice lines, follow-up-choice
  chains. v0.1 composes dialogue from the Menus batch instead (see Point & click section);
  real machinery joins only when an actual game outgrows buttons-and-panels.
- **Richer camera work:** pans, zooms, cutscene framing beyond `camera looks at` /
  `camera follows`. Minimal pair ships with the point & click batch; the rest is parked.
- **Save files (Task 30 Part A, decided):** a save is one pretty-printed JSON file at
  `<ProjectFolder>/Saves/<slot>.json` (`default.json` for the default slot), written and read by
  `Runtime/SaveFile.cs`. This is not a Flax asset and does not live under `Content/`. The root
  carries `"format": "event-manager-save"` and `"version": 1`; a file missing either is reported
  as damaged instead of half-read, a newer schema is refused without touching the running game,
  and an older schema loads with a warning. Slot names are trimmed and every character that is
  not a letter, digit, space, `-` or `_` becomes `_`, so `save game to "../x"` still lands inside
  `Saves/`; writes go through a `.tmp` file and one replacing move so a crash cannot leave a
  half-written slot. The schema already names every section Parts B–D will fill — scene name,
  additive scene list, `LevelName`/`LevelNumber`, every global, one record per sheet/actor
  (`SourceName`, actor name, player, physics flag, active, position/orientation/scale, all
  per-object variables, bag stacks, equipped names, `CurrentState`, `spawnedFrom`), spawned
  prefab/scene/clone roots, and the RNG seed. `SaveVariable` converts a `ValueBox` both ways, so
  numbers, text, booleans and never-set values keep their kind. `save stats` / `load stats`
  remain the in-memory checkpoint; `save game` / `load game` are the disk pair and belong to
  Part E. Loading (Part B) replaces the loaded scenes with the saved level, re-adds the overlays
  in saved order, and only then writes the globals: a reloaded scene runs its own
  `when scene starts` and the runtime seeds `LevelName`, `LevelNumber` and `Time Elapsed`, so
  restoring first would be overwritten by the level just reloaded. Globals are replaced rather
  than merged; the level clock is rebased to the saved `Time Elapsed` so it keeps counting; the
  scene section wins over a disagreeing `LevelName` global; and a save whose level scene is
  missing is refused before the running game is touched.
  Actor records (Part C, decided): one record per real sheet (`GlobalHost` is not an actor), sorted
  by actor name, carrying source, actor name, player, physics flag, active flag, world transform,
  every per-object variable, bag stacks, equipped names, `CurrentState` and `spawnedFrom`. A load
  applies them after the scene has reloaded and after the globals: per-object variables are
  **merged** over the fresh sheet (the save's value wins, an authored default it never wrote stays),
  while the bag and equipment are **replaced**, because what the actor carried at save time must not
  merge with what the reload handed it. Equipment is re-recorded by name only, never re-applied as
  affixes, since the stats it pushed are already among the restored per-object variables. A live
  actor whose sheet is missing is rebuilt from the saved `Source` (`EventsManager.ProgramForSource`
  + `runtime.AddSheet`) before its state is applied. A restored sheet clears `PushQueue` and
  `Waiting` and writes `CurrentState` and `LastState` together: the `when me appears` that a rebuilt
  sheet queues must not run, and `when state is` must not see a false entry edge, because a load is
  a moment in the run rather than a new actor arriving. A saved actor the scene cannot provide is
  warned about and skipped. The bridge supplies the engine half: `CaptureSaveTransform` /
  `CaptureSaveActive` read the world transform and flag, `ApplySaveTransform` warps through
  `TeleportSubject` (cancelling moves and stopping a dynamic body) before writing orientation and
  scale, and `ApplySaveActive` restores the flag.
  Spawned roots (Part D, decided): only a spawned **root** is recorded — a prefab or clone brings
  its own children back, and a spawned/added scene is already restored with its whole tree by the
  scene section. The kind is stamped at spawn time through `EventRuntime.MarkSpawnedRoot`
  (`prefab` for `spawn prefab`, `clone` for `spawn <template>` and `place`), never inferred at
  save time; a bare `MarkSpawned` source (a prefab child, a scene-instanced tree) is not a root.
  A record carries the source word exactly as written, the root's actor name including the spawn
  suffix, parent actor name (`CaptureSaveParent` reports "" for a scene parent), transform and
  active flag. The load recreates prefabs first, then clones — a clone's template may be a prefab
  root the same save recreates — writes the saved name verbatim (the actor records are keyed by it
  and spawn naming already made it unique, so `UniqueInstanceName` is not re-run), attaches the
  sheet tree (`AttachSpawnedTree` for a prefab, `AttachSpawnedActor` for a clone), and only then
  parents every root and writes transforms and active flags, so parenting cannot move a restored
  child afterwards. `SaveSnapshot.Load` runs this before the actor records, so `findActor` can find
  the recreated roots. A missing source or parent is a warning (the root is skipped or left at the
  scene root); a `scene` record is skipped because Part B already re-adds it, and an unknown kind
  is skipped with a warning. Transient audio/effect actors and placed/scene children are not
  separate records, and duplicate child names across prefab instances remain the existing runtime
  ambiguity.
  Slots, autosave and errors (Part E, decided): the words are `save game` / `save game to "<slot>"`,
  `load game` / `load game from "<slot>"`, `autosave`, `delete save "<slot>"`, the triggers
  `when game saves` / `when game loads`, and the condition `save slot exists "<slot>"`. A blank slot
  means `default`; `autosave` is a fixed `autosave` slot so a checkpoint never overwrites a save
  the player made, and `load game from "autosave"` resumes it. `Runtime/SaveGames.cs` owns the
  words and returns a `Result` (`Saved`, `Loaded`, `Deleted`, `Missing`, `Damaged`,
  `NewerVersion`, `Refused`, `Failed`) with the normalized slot and one friendly sentence; the
  bridge displays it through `FlaxActions.Warn`, so a broken slot never throws at a sheet. A load
  reads the file before changing anything, which keeps a missing, damaged or newer save from
  half-loading; a save whose scene the project no longer has is `Refused` for the same reason.
  `when game saves` is raised after the write and `when game loads` after the full restore, both
  through `RaisePushAll` with the slot name, so a listener runs against the restored sheets on the
  next tick. `save game` captures the runtime, not the acting sheet, so the same words work on the
  global Game sheet. `save slot exists` reads `EventRuntime.SaveProjectFolder`, set by
  `EventsManager` from `EventFiles.LocateProjectFolder` (the folder holding both `Content` and
  `Source`); a runtime without that folder says no. `delete save` removes one file and treats an
  already-gone slot as `Missing`. `save stats` / `load stats` stay the in-memory checkpoint and still
  run before action parsing, so the disk pair never changes their behaviour.
  The test shape (Part F, decided): each part keeps its focused runtime tests, and one end-to-end
  test drives the real slot coordinator and file — capture a run, write it, load it into a scene
  that never saw those actors, and assert the done-when list in one place: globals, per-object
  values, bag, equipment, the prefab root and its weapon child, the missing/extra actor split, and a
  hot reload after the restore. Old-schema, damaged-file and two-slot-isolation checks stay with the
  slot rules in Part E rather than being repeated; the end-to-end test is about the seams, so a
  regression in the order (scene, globals, spawns, actors) still fails even when each half passes.
  Not saved in v1, by design: in-flight `wait` continuations, active tweens, particle
  lifetimes, queued pushes and the current path — those restart cleanly rather than resuming
  mid-air.
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

- **Screen shake is one offset, never a saved position (Task 21 Part E):** `shake camera strength
  <n> for <s> seconds` starts a wobble on the active camera, and `Runtime/CameraShake.cs` owns the
  maths — a linear decay to zero plus a deterministic pseudo-random offset per (seed, tick), so a
  shake always settles and always wobbles the same way. The bridge applies it *after* the follow /
  snap writes each tick, on top of whatever framing the rig chose, and keeps the captured framing
  so the last tick restores it exactly; the camera's own position is never the shaken one, which is
  what stops two overlapping calls from drifting. A second call is a refresh, not a stack: the
  stronger strength wins and the longer remaining time is kept. `camera free` and `camera switch`
  drop a running shake with the rest of the rig.

- **Ladders as one ownership handoff, not a physics mode:** G4 phase 4 adds `climb <thing>`,
  `climb down <thing>` (optional `speed <n>` / `my ClimbSpeed`) and `stop climbing`, and the climb
  simply owns the body for as long as it lasts: X/Z lock to the ladder actor's axis, `ClimbStep`
  drives Y at the pace, and the span (from the ladder's Y to its collider height, or
  `my LadderHeight`, or 400 cm) clamps both ends. Reaching the end the climb was heading toward
  ends it, so a sheet steps off the top with `move forward`; `stop climbing` lets go anywhere and
  hands Y back to gravity with the stored fall speed cleared. Starting a climb cancels the
  actor's pending moves so a chase cannot fight it, and the words are opt-in per sheet — lane
  enemies never climb because no lane sheet says so. Same route in every body mode (a
  CharacterController moves itself, everything else writes `Position` and a dynamic body is held
  at rest while it climbs). `me is climbing` exposes that state as a condition (through
  `IEventWorld.IsClimbing`), so a climb loop gates on the ladder itself instead of a flag variable.
  A **multi-exit ladder is authoring, not a word** (Task 21 Part C, `GameFiles/LadderTall.events`):
  the climb already ends at its span end (the top exit), `stop climbing` keeps the let-go height
  (the mid exit at any landing), and `move toward nearest Ledge` picks the exit marker by 3D
  distance, so the height the player let go at chooses the landing. A per-ledge *proximity* block
  cannot do that job: `when me is near <thing>` is an X/Z cylinder with no height and it fires on
  the enter edge, which a climb has already consumed on the way up to the ladder — so the height
  word `me is above <thing> by <n>` does it instead (`ConditionKind.Above` compares two actors'
  Y with a margin through `IEventWorld.Height`), which makes an exit automatic at the landing it
  names. `near` stays X/Z-only on purpose: re-measuring every existing range and chase gate in 3D
  would change what shipped sheets already mean. Parked: ladders
  you can shoot down, moving ladders, and a top-exit that picks the destination platform
  automatically (`nearest Ledge` picks a marker, not a platform).

- **Audio as one music player plus three volumes (Task 31):** there is exactly one music
  `AudioSource` behind `play music` / `loop music`; a second track replaces the first rather than
  stacking, and `stop sound` never touches it. The rules live in `Runtime/AudioMix.cs` (pure,
  unit tested) and the bridge owns the source and applies `EffectiveMusicVolume` /
  `EffectiveSoundVolume` every tick, so `Master × Music` and `Master × Sound` are the only
  arithmetic. `set music volume` writes `MusicVolume` and the level; `fade music to <n> in <s>`
  ramps the *current* level, and a fade to 0 deliberately keeps the setting so a scene change is
  `fade music to 0 in 0.5 → wait → load scene → loop music "level1"`, where the new track fades
  itself in over 0.35 s. `duck music by <n> for <s>` is a separate temporary multiplier (ramp,
  hold, ramp back), so a dialogue duck cannot change the authored level. Music survives
  `load scene`: the mixer remembers the track and playhead and the bridge respawns the source,
  because a Flax scene unload destroys every actor in it. `when music ends` fires once when a
  non-looping track stops (the optional `when music "<name>" ends` only answers that track).
  Clip names are still literals, but `play music random from "a, b, c"` (and the `loop music`
  form) resolves the quoted comma-separated list through the shared `Rng` and plays the chosen
  one — a per-level random track without an if-block per track. `AudioMix.SplitChoices` trims the
  names and drops empty parts, `PickChoice` clamps the roll so a list can never index out of
  range, and the missing-clip warning reports the rolled name plus the imported clips.
  `play sound ... at <thing>` parents a one-shot to that actor with `AllowSpatialization` on and
  a 200-unit min distance; **Flax only attenuates a clip imported as a 3D sound**, so a 2D clip
  plays at the same volume from anywhere — that is an asset setting, not a missing word. Plain
  `play sound` and `play step sound` stay 2D. Parked: per-source pitch/pan words, audio themes
  that persist across the whole game, and a real bus mixer asset (the three globals are the
  interface a settings UI binds).

- **Jumping without a jump system:** G4 phase 3 adds only a word and a query — `jump [strength n]`,
  `me is on ground`, `me is airborne` — and lets the existing body rules carry it. A
  CharacterController jumps by writing its stored fall speed (the number `CharacterStep` already
  decays), a dynamic body by setting `LinearVelocity.Y`, and an actor with no body (or a kinematic
  one) by a scripted `JumpStep` arc that moves its `Position.Y` from the take-off height. "Airborne"
  is the jump's own flag rather than the ground ray, because at take-off the floor is still inside
  the   probe — that flag is also what refuses a second take-off, so holding the key cannot chain
  jumps. A double jump is a counted exception rather than a removed gate (Task 21 Part A):
  `jump again` passes in the air only while the actor's `my Jumps` still has a take-off left —
  default 1 per landing, so a sheet that never sets it cannot double jump; 2 allows one air jump,
  0 forbids it for good. The bridge keeps the spent count, so a scripted-arc actor gets the same
  allowance as a body, and landing, `respawn at` or a scene restart hands the whole count back.
  A wall jump is the other counted exception (Task 21 Part B): `wall jump` needs air, a wall within
  60 cm of the capsule, and the same four side probes `me is touching wall` reads; it sets the
  take-off strength and pushes away from the nearest wall at 220 cm/s, which a CharacterController
  carries in the bridge's per-character motion until it lands (a dynamic body keeps it in
  `LinearVelocity`, while a bodyless actor gets only the vertical arc). Parking a wall jump's own
  stance or a wall-slide speed is deliberate — the push and the condition are the kit.
  The on-ground query answers from the controller's ground check when there is one, else a
  short downward probe that ignores the actor's own colliders and triggers; a world without physics
  reports grounded, so the gate never blocks a sheet in a test or a bare scene. Parked: coyote time,
  jump buffering, variable-height jumps (releasing mid-air) and moving-platform carry —
  a platform moving under a jumping actor is not rideable yet. The scripted arc writes `Position.Y`
  directly rather than through `Move`, so it is unswept: a transform-mode actor can clip a ceiling.
  A Dynamic sheet with no `RigidBody` on itself or an ancestor silently takes that same arc.

---

- **Cursor stack and automatic cursors (Task 48):** the pointer has one resolved answer per tick,
  and `CursorState.For` is the whole rule: `hide cursor`, then the `push cursor` stack, then the
  sheet's own `set cursor`, then an armed `aim`, then the automatic hover rule, then `default`.
  The stack never empties — popping past the bottom leaves `default` — so a mode that forgets its
  pop cannot strand the game on the combat cursor, which is also why `aim` pushes nothing itself:
  it fills the aim slot, and the runtime ends that mode on a pick, a cancel or a timeout. Cursor
  art stays content (`Cursor_<Name>` actors with `Cursor fps` / `hotspot` / `fallback` / `image`),
  and a rule only asks for a name the scene actually authored, so a project that never mentions
  cursors keeps the plain OS pointer and pays no raycast. Parked: per-ability aim cursors
  (`aim "bolt"` → `Cursor_Bolt`), per-player cursors for local multiplayer, and a per-rule fallback
  instead of `Cursor hand` / `Cursor attack` / `Cursor drag` globals.

---

- **Animation depth and layering (Task 33, decided):** four decisions, because the virtual
  `InitAsAnimation` graph can only sample one clip straight to the output — it has no nodes to mask,
  blend or inject into, so speed, layers and root motion all need somewhere else to live.
  *Upper-body layering is authored content.* A character that must shoot while walking ships a base
  Anim Graph: a `Blend With Mask` node whose **Pose A is the locomotion pose** and whose **Pose B is
  an `Animation Slot` node** feeding the final output, plus a `SkeletonMask` asset (here
  `UpperBody`). The mask rule is **spine and up, hips and legs excluded** — spine, chest, neck,
  head, shoulders, arms and hands enabled; **hips, root, legs and feet disabled**. Excluding the hips
  is load-bearing, not cosmetic: the shoot clip animates the legs too, and the mask is the only thing
  that stops those channels reaching the output, so the walk keeps owning the pelvis and feet. A mask
  that includes the hips overrides the walk's pelvis motion and visibly detaches the legs.
  *Slot words.* `play anim "<name>" upper body` is
  `PlaySlotAnimation("UpperBody", clip, 1, 0.2, 0.2, loopCount)` (0 once, -1 forever) and
  `stop anim upper body` is `StopSlotAnimation("UpperBody")` — the legs keep walking through both.
  The slot is asked for 1× because the actor's speed already rides on `AnimatedModel.UpdateSpeed`,
  which scales the whole graph including slots: passing it twice would play the layer at speed².
  A layered one-shot is tracked apart from the full-body clip, so a held trigger does not restart it
  and `when anim "<name>" ends` still fires for it. Slot playback must never go through
  `InitAsAnimation`, which replaces the whole pose; that substitution is exactly the bug the layer
  exists to fix, so a model without an authored base graph is warned about instead.
  *Speed is live, not baked.* `set anim speed to <n>` is one number per actor, written to
  `AnimatedModel.UpdateSpeed`, so it changes the running clip with no restart — slot clips included,
  since the graph scales as a whole — and it persists across clip changes with each new clip
  inheriting it. A full-body clip on the virtual graph bakes speed at graph build, so a
  live change there rebuilds the graph (a restart) — a documented limit that disappears once a
  character's playback is routed through slots.
  *Clip timing comes from the clip, not a guessed `wait`.* `when anim "<name>" reaches <n>%` polls the
  playing clip's progress — the time since `StartTime`, scaled by the actor's speed, clamped at 100%
  for a one-shot and left to keep counting for a loop — and fires on the crossing edge, once per play;
  the bridge stamps every `play anim` / `loop anim` with a play token, so a restart re-arms it and a
  speed change moves the percent with the picture. Markers are content, not an API:
  `when anim "<name>" hits "<marker>"` is fed by `MarkerAnimEvent`, a project `AnimEvent` the author
  drops on the clip's Animation Event track with the marker's name typed in. Flax keeps an Animation
  asset's event list editor-side, so there is nothing to read from C# — a clip without that event has
  no markers, and `reaches <n>%` is the declared fallback.
  *Root motion belongs to the locomotion clip only.* `AnimatedModel.RootMotionTarget` is assigned once
  on the base graph and drives nothing else; slot clips never write the actor transform, so an
  upper-body clip cannot double-apply displacement or slide the feet. The single-clip fallback graphs
  keep `rootMotion: false` — there the clip is the whole pose and the sheet's `move` / `goto` owns the
  transform, so clip motion would double the displacement. `set root motion on` / `off` (default off)
  is therefore a narrow word: Flax exposes the mode only as the `InitAsAnimation` build flag and
  `RootMotionTarget` at runtime, never as a property of an authored graph, so the bridge can only aim
  an authored graph's already-extracted motion at the sheet's actor, and `off` cannot disable it.
  A clip that actually moves its actor needs an authored graph with root motion enabled plus a clip
  imported with root motion — content, not code — and its motion adds to `move`, so an actor should be
  driven by one or the other.
  **Content dependency:** the bridge cannot synthesize any of this, so Task 33 C is not startable
  until a character's base graph and mask exist as project assets. Bone names in the mask must be
  matched against the real skeleton — other skeletons (the BGE dragon, for instance) need their own
  mask, and a mask built for the wrong skeleton silently blends nothing.

- **Self-test sheets: a case is a block, not a sequence (Task 51, decided).** The vocabulary is
  four words — `test "<label>"`, `expect <condition>`, `pass`, `when self test ends` — plus a
  per-file *Self Test* flag on the `EventSheet`. **A case is an ordinary block**, run when its
  `when` fires, not a sequencer: the language gains no control flow, a sheet still reads as an
  example, and the next case is entered by the message the previous one sends. That flatness is
  the whole reason it fits this syntax — a runner with stages would be a second language beside
  the first. **`expect` re-parses its tail through `Conditions.Parse`**, so there is no second
  comparison grammar to drift: every condition the language already accepts (including Task 28's
  `count of …`, when it lands) is fair game, and a malformed one gets the ordinary friendly
  condition error at load instead of a test-specific one.
  *Judging is deliberately unkind.* `pass` reached before the 5 s timeout is a pass; a false
  `expect` fails and quotes the expectation with both values; a case whose gate never opens is
  `SKIP`, never `PASS`, because the silently-not-run case is the bug the sheet exists to kill; and
  a warning raised inside a case fails it.
  *Two tiers, one file.* The editor plays `SelfTest.scene` and prints `PASS n / FAIL n / SKIP n`;
  the unit suite plays the same `.events` against `FakeWorld`. A case the fake world cannot reach
  is tagged `(editor only)` in its label, and the exclusion is stated out loud rather than
  inferred: `SelfTest.ExcludedLabels` makes `RunEvent` report those cases `SKIP` without running
  their bodies, and a suite check compares the tags with the documented list **in file order**, so
  demoting a case needs two edits and cannot happen quietly. The tier earns its keep — its first
  run found two real sheet bugs (an edge that also fires on a downward crossing; an assertion
  against an actor the resolver cannot see).

- **A boss is a schedule, not a hit system (Task 21 Part D):** `GameFiles/Boss.events` runs the
  whole fight from the words the arcade lane already uses, and the one new-looking part — the bar
  at the top of the screen — is the Task 34 progress-bar pair, not a HUD feature. The shape is
  deliberate at three points. **Phases are health edges, not level tests:**
  `when my Health is lower than 200` / `< 100` are the `HealthLow` triggers, so each fires on the
  downward crossing and re-arms if healing lifts the boss back over it, and the sheet keeps
  `my Phase` for everything that needs to *read* the phase (the gated attack blocks). Phase 3 is
  two lines — `set my Phase to 3` and `set my Speed to my EnragedSpeed` — which is why the enraged
  boss needs no second movement or retreat block: the same `goto` becomes a dash. The enraged
  speed is *derived once*, in `me appears` (`set my EnragedSpeed to my Speed * 2`), rather than
  multiplying the live speed at the gate: a health edge that re-arms when healing lifts the boss
  back over the threshold would otherwise double its pace again on every later crossing. **The attack is
  a guarded roll:** `chance 50%` + `otherwise` picks close or ranged, but a `when every` event
  re-fires every tick and a `wait` suspends only its own run, so the block opens on
  `my Attacking is 0`, sets it, and clears it at the end of *both* branches — and the counter and
  the flag come **before** the roll, because an `otherwise` body runs only when its condition
  failed and its gate holds until the next condition line, so cleanup written under the branch
  would be swallowed by the else. **The retreat is a timer plus authored empties:** a 0.25 s poll
  sees `my Attacks is 3`, `set state retreat` runs once on the state entry, and
  `goto random HidePoint` walks to whichever hide empty is picked — `random <thing>` needs the
  shared name, so every hide point is called `HidePoint` (Flax allows duplicate names). The
  teleport idea cannot be authored yet (`teleport me to random HidePoint` is not a thing the
  teleport parser reads), which is why the enraged boss doubles its speed instead of blinking
  across the arena. The type is data, as with every other enemy: `Boss` is a template name and
  `BossSpeed` / `BossHealth` / `BossSightDistance` / `BossWeapon` / `BossHostile` /
  `BossExplosionDeath` live in `EnemyTypes.events`, so `kills Boss` is its own counter and the
  sheet never hard-codes a number. The bar is `show BossBar` + `set progress of BossBar to
  my HealthPct`, with the percentage worked out on the line above it because
  `set progress of` takes a number and not a sum, and `+ 1` keeps the number above 1 (the bridge
  reads anything over 1 as a percentage, so a bare 1 would paint a full bar); `hide BossBar` drops
  it on death. A `bind progress of BossBar to my Health` would hand the bar 300, which is why the
  example polls the health edge instead. The same pass fixed the one real sheet bug the audit
  found: `Enemy.events`' death block ended `destroy me` under an `otherwise`, and that gate holds
  until the next condition line, so an `ExplosionDeath` type (the Turret) spawned its explosion
  and lived on — the destroy now sits under its own `if my Health is lower than 1`. Parked: a
  boss health bar that needs no authored `BossBar` actor, ring/fan bullet patterns, and
  `teleport <thing> to random <name>`. **Every beat is a clip, not a pose:** `Roar` on waking,
  `Walk` while it chases and walks back, `Run` for the retreat and the enraged chase, `Attack`
  started *before* the close attack's wait so the hit lands partway through the swing, `Throw` as
  the ranged telegraph (also played before its wait, so the clip is the tell), `Flinch` on a hit and `Die` on death — the last one because `destroy me` used to cut
  the death off in the same tick, so the death block switches to a `dying` state (which closes
  every fight block at once), plays `Die`, waits 0.8 s and only then destroys. A model without
  one of those clips warns once and the block still runs, so a placeholder mesh plays the fight. **Where it attacks from is data too:** three or more empty actors named `AttackPoint`, parented to the player, give the front/back/flank entrance — `goto random AttackPoint` picks one, then `goto nearest player` closes the last stretch before the swing. No offset maths and no new word: the sheet can only walk to *named things*, and `goto` walks the floor, so a point floating above the player is flattened onto the ground plane (an aerial entrance needs `jump`, not an empty). Once health is below 200 the boss also calls in help: a polled timer sets a `summon` state, a `chance` ladder picks Pistolboy / UziRebel / ShotgunManiac (a random *type* has no word — `spawn` takes one template), `set my Reach to random 110 to 170` spreads the arrival, and `my Summons` caps the help at four so a long fight cannot fill the room. Every way out of `fight` clears `my Attacking` — `retreat`, `flinch` and `dying` — because an attack run skipped by its own `my state is "fight"` guard would otherwise leave the flag at 1 and the boss would never attack again; and the summon timer carries `my Attacking is 0`, so the roar never fires across a close swing. The enraged throw is a three-knife fan: one knife from `KnifeCenter` and two from the rotated `KnifeLeft` / `KnifeRight` empties, all parented to the boss, with a 0.08 s stagger between them and a state guard after each gap so a hit stops the rest of the throw. `spawn` copies the spot's orientation and a projectile flies along its own facing, so the fan lives entirely in the empties' rotation — no aiming code. The earlier phase keeps its single plain throw.

- **A follow keeps the shot you placed (Task 52):** `camera follows <thing>` used to build its
  offset from the `CameraHeight` / `CameraDepth` globals, so a camera authored to the side of the
  player jumped onto them the moment the verb ran. The offset is now the camera's own position
  minus the target's (`CameraFollow.Offset`), captured once when the follow starts, and the
  authored rotation is left alone — a follow moves the camera, it never re-aims it. The follow
  also stopped snapping: each tick the camera closes `1 - exp(-damping * dt)` of the remaining
  gap, so it trails a moving subject and glides the last stretch back into its offset when the
  subject stops (the TK / Mario spring-back). The damping is `CameraFollowDamping`, per second,
  default **6** — about 63% of the gap every 1/6 s — and `0` means snap, which is exactly what
  every scene that used the verb before this task got. Because the easing is an exponential of
  `dt` rather than a fixed per-frame lerp, 30, 60 and 144 fps feel the same. Zoom still scales
  the offset, so a zoomed follow stays a zoomed follow. `camera at`, `camera looks at`,
  `camera switch`, the free camera and the shake are untouched, and a scene that never writes
  `camera follows` sees no change at all. The demo wiring is the shape the task asked for: the
  camera's script lives in `PlayerCamera.events` (`camera switch PlayerCamera` +
  `camera follows Player`) and the `PlayerCamera` actor's blank `Event File` resolves to it by
  name, so no player sheet owns the camera. Parked: an authored `camera follows <thing> offset
  <n>` word, and a follow that also eases rotation.

- **First person is a camera mode, not a second rig (Task_FPS Phase 1):** an `EventSheet` with
  `First Person` ticked makes its actor a first-person player. The mouse turns the body (yaw on
  world Y) and tilts the player's own camera child (pitch on local X, clamped to ±89°); the pure
  `MouseLook` class owns the maths so the clamp, the wrap and the invert rules are unit tested.
  Two decisions keep it out of everything else's way. **It is a mode of `CameraState`**, beside
  follow / at / aim / free — so it releases those rigs on entry, they release it on use, the scene
  teardown clears it, and the free camera's `OwnsPosition` never fires while a player is looking.
  **Yaw turns the body**, which is why the existing movement words became view-relative with no
  new verb: `move forward` follows the view and strafing leaves the facing alone. A dedicated
  `MoveRelativeToCamera` flag would only matter for a body that must not face where it looks, so
  it stays parked. The eyes are found with `FlaxWorld.FindCameraUnder` — a camera under the
  player, never "whichever camera comes first" — so a scene with a cutscene camera still aims the
  right one. Two limits are deliberate: a shake does not wobble a first-person camera (there is
  no framing to offset; it is cleared rather than fighting the parented rig), and the legacy
  `LookAtMouse` rule stops while the mode is on. Feel lives in `LookSensitivity` (degrees per
  pixel, default 0.12), `LookInvertX` / `LookInvertY`, and pointer capture is the new
  `lock cursor` / `unlock cursor` / `toggle cursor lock` family: the OS mouse is centred and
  hidden, every cursor rule is
  outranked, and stopping play always restores it. Capture and release both move the pointer by
  themselves, so the look rig drops the one frame of mouse delta that follows either transition —
  otherwise the recentre jump would snap the view (the flag is windowed to a quarter second, so a
  stale capture can never swallow a real movement). `toggle cursor lock` flips capture, which is
  what lets one key (Escape in the shipped sheet) hand the pointer back and take it again on the
  next press — a sheet cannot toggle with two `when Escape is pressed` blocks, because both gates
  are read in the same tick after the first block has already written its variable. While the
  pointer is captured the engine also
  pins it back to the window centre at the end of every input update and caches that centre as the
  previous position, which zeroes the absolute `MousePositionDelta` for scripts exactly when look
  control needs it; the rig therefore reads `Input.MouseMoveRelative` while captured (raw pixels,
  fired before the recentre, falling back to the `Mouse X` / `Mouse Y` axes divided back out of
  their Input Settings sensitivity on a window that reports no relative moves) and the ordinary
  delta while the pointer is free. `GameFiles/FPS_Player.events` is the shipped
  sheet (capture, WASD, run gate, jump, Escape releases the pointer). Parked: crouch, ADS/FOV,
  per-axis look triggers, and camera-relative movement for a body that does not turn.

- **One `<Row><Field>` table loader (Task 9 F10):** every data table is rows of fields authored as one
  global per row and field (`PistolDamage`, `ZombieHealth`, `SwordArmor`, `RareMinAffixes`), so loading one
  is one shared walk: `Runtime/DataTable.cs` reads a data file's literal `set` lines, splits a name into the
  row and the field it ends with, and keeps the value. A live global of the same name always wins over the
  file, and a row that exists only as live globals is a valid row. Fields match longest-first **in the order
  the table passes them**, which is why `AffixData` lists `MinAffixes` before `Min`. `WeaponData`,
  `SpellData`, `ItemData`, `AffixData` and `EnemyTypes` are now a field list plus their built-in defaults and
  their field-to-stat switch; `Matchups` is a pair table and reuses only the literal walk (`DataFile.ForEachLiteral`).
  Adding `Loot.events` or `Buffs.events` is a `DataTable`, a field array and the switch that writes the stats —
  no loader code.

## 10. The one-paragraph pitch

Event Manager is a text-first game-logic system for non-programmers, inspired by Blender Game
Engine's logic bricks and GDevelop's event sheets, but built for plain readable files instead of
panels and wires. You write stacked events â€” a `when` line, optional `unless` lines, then indented
actions â€” in a fixed ~40-word vocabulary, saved as `.events` files that attach to Flax actors
(either by an EventSheet component you place, or automatically by actor name). A once-written C# interpreter runs them live and hot-reloads instantly; no code is ever
generated. The result reads like stage directions for your game, and small games get made.
