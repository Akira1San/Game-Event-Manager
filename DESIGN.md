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

A `.events` file is attached to an actor by **matching name**:

- `Coin.events` automatically attaches to every actor named `Coin` in the scene.
- Inside it, **`me`** means that actor, and **`other`** means the actor involved in the trigger
  (the one touching, the one hit, the one which sent a message).
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
protect part of an event without making a new one.

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
| `when other touches me` | | needs physics (or distance check) |
| `when me touches <thing>` | `when me touches Water` | tag- or name-based |
| `when me overlaps <thing>` | | distance-based, no physics needed |
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
| `when anim "<name>" ends` | `when anim "Shoot" ends` | armature characters â€” see below |

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

### Actions

| Word | Example | Notes |
|---|---|---|
| `move <dir> speed <n>` | `move forward speed 5` | dirs: forward/back/left/right/up/down |
| `move toward <thing> speed <n>` | `move toward Player speed 3` | |
| `turn <dir> degrees <n>` | `turn left degrees 90` | |
| `goto <thing>` | `goto Player` | navigation: walks there using the baked NavMesh (walls avoided for free); pairs with `arrives at` |
| `goto there` | `goto there` | point & click: walks to the **last clicked position** (pairs with `when Ground is clicked`) |
| `camera looks at <thing>` | `camera looks at Chest` | aim the view at any actor |
| `camera follows <thing>` | `camera follows Player` | keeps them centered while they move; aiming at something else releases the follow |
| `look at <thing>` | `look at Player` | one-shot snap: turn toward the target, once |
| `face <thing>` | `face Player` | continuous: keeps turning toward the target while it moves (BGE Track To) â€” the turret/chaser word |
| `teleport me/other to <thing>` | `teleport me to SpawnPoint` | |
| `push <target> direction <dir> strength <n>` | `push other up strength 10` | physics impulse |
| `make <target> unmovable` | `make me solid` | physics toggle |
| `destroy <me/other/thing>` | `destroy me` | |
| `spawn <thing> at <thing>` | `spawn Coin at SpawnPoint` | thing must be a prefab or scene-named object |
| `play sound "<file>"` | `play sound "hit.wav"` | |
| `set <var> to <value>` | `set Score to 0` | |
| `add <n> to <var>` | `add 1 to Score` | |
| `send message "<name>" to <thing>` | `send message "die"` | broadcast if no target |
| `stop this event` | | exit the current event early |
| `wait <n> seconds` | `wait 0.5 seconds` | pauses THIS event, not the game |
| `hide me` / `show me` | `show PausePanel` | show/hide works on **me or any named actor** â€” the core of plain UI logic |
| `set text of <thing> to <val>` | `set text of ScoreLabel to Score` | live values on the screen; plain text works too |
| `play anim "<name>"` | `play anim "Walk"` | armature: plays once, then holds the last frame (BGE "Play") |
| `add <thing> to my bag` | `add Sword to my bag` | picks items up; the bag holds things by name, counted |
| `remove <thing> from my bag` | `remove Potion from my bag` | used/consumed things |
| `equip <thing>` | `equip Sword` | takes it from the bag and attaches the item's sheet â€” its `equipped` event fires |
| `drop <thing>` | `drop Sword` | unequips if worn, takes from bag, spawns the item at my feet |
| `loop anim "<name>"` | `loop anim "Walk"` | armature: repeats until something new plays (BGE "Loop") |
| `stop anim` | `stop anim` | armature: stops and goes back to the previous animation (BGE "Stop") |
| `show text "<txt>"` | `show text "Game Over"` | HUD text (also below) |
| `hide text` | `hide text` | removes the HUD text |
| `restart scene` | `restart scene` | loads the current scene again (also resets the spawned things) |
| `load scene "<name>"` | `load scene "Level1"` | replaces the current scene (BGE "Set Scene" / GDevelop go-to-scene) |
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

Runtime knobs are okay as a single **speed/value** word â€” no math expressions, no nesting math.
`Score + 1` is *only* legal inside `set Score to Score + 1` (one-op form: `+ - * /`).

### Armature characters (the BGE way)

Animated characters work like the old Blender Game Engine Action Actuator: pick an animation,
play it by name. You never touch Flax's visual Anim Graph editor by hand â€” a **boring template
Anim Graph ships with the system**, where each state exists purely to hold *one animation*, and
each state is **named after a Blender Action**. The event words do the rest:

- `play anim "Walk"` maps to Flax's `AnimatedModel.PlayState("Walk")` â€” play a state by name,
  exactly like BGE playing an Action by name. No nodes, no wires.

**Setup contract for a character** (same muscle memory as naming Actions for bricks):

1. Name each Action carefully in Blender â†’ export to Flax.
2. Add the shipped template Anim Graph to the character (one click â€” it's the standard one).
3. Match names: the state name = the Action name = what you write in the `anim` word.

**Friendly errors in BGE spirit:** playing an unknown name is a name check against the rig â€”
`I don't know the anim "Walked". This character has: Idle, Walk, Shoot.`

Example (a walking, shooting character â€” animation only, logic is the usual syntax):

```
# --- Player.events (armature character) ---
when W is held
  move forward speed 5
  loop anim "Walk"

when W is released
  play anim "Idle"

when Left Mouse is pressed
  play anim "Shoot"
  spawn Bullet at me

when anim "Shoot" ends
  play anim "Idle"
```

Rules: `play anim` runs once and holds the last frame; `loop anim` repeats until something else
plays; playing a new animation replaces the current one (with a short cross-blend); `when anim
"â€¦" ends` fires for one-shots only.

### Menus & game UI (the scene-switching way)

Menus are **another scene**, not a special system â€” the old BGE habit (`Set Scene` actuator),
also GDevelop's go-to-scene. Same rules as everywhere else apply:

- **Visuals live in the editor, logic lives in text.** You place buttons, texts, panels and
  images as scene actors (any name: `PlayButton`, `ScoreLabel`, `PausePanel`) in the Flax editor.
  The words never draw anything â€” they click, show, hide and fill them, like any other actor.
- **A menu scene + a `Menu.events` global sheet = a working menu.** Menus come and go by
  `load scene`; in-game UI (HUD, panels) is just actors that `show`/`hide`.

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
  loop anim "Walk"

when me arrives at Waypoint
  wait random 0.5 to 2 seconds
  goto random Waypoint

when me sees Player by 8                   # one-shot the moment the player is spotted
  set my mode to "chase"
  loop anim "Run"
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
  play anim "Idle"
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
| `EventSheet` (per-actor small script) | hooks Flax events (OnUpdate, OnTriggerâ€¦) and reports them up | once |
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

- **Auto-attach rule:** an actor named `Coin` + a file `Coin.events` = logic sticks on. Zero clicks.
- **Manual override (later):** an EventSheet component in the editor can point at any file, for
  renames or shared sheets.

### Trigger mechanics: polled vs. pushed

Some triggers must be checked every frame (**polled**): keys held, timers, variable watches,
distance checks. Others arrive as Flax callbacks (**pushed**): collisions (`OnTriggerEnter`),
input presses (`Input.GetAction`), `OnStart`.

`EventSheet` subscribes to the pushed ones once. Every tick the runtime walks all sheets
top-to-bottom: pushed events fire their queue first, then polled events are checked.

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

Every concept from Sections 3â€“5 appears in this ~60-line game: attachment by name, `me`/`other`,
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
- **Physics vs. kinematic movement:** does `move forward` use rigidbody physics or transform move?
  Proposal: transform move by default; `push` is the physics word. Confirm by feel in M2.
- **Persistence:** do global variables survive scene *reload* (not just scene *change*)? Proposal for now: yes, and a `wipe game variables` action to explicitly reset.
- **`me touches` without physics:** distance-based overlap implemented but name `touches` being
  used for both is confusing. Maybe rename to `me gets close to`? Feel it in M2.
- **Scenes with same-name actors** (multiple Coins): currently all copies of the actor get the
  sheet â€” good. But what distinguishes copies in text? Maybe auto unique-id word (`my copy number`)
  rather than solving namespacing now.
- **Animation cross-blend:** how long should the blend be when one animation replaces another?
  Proposal: something small (~0.2 s), tuned by feel in M2Â½. Also parked: does holding W restart
  `loop anim "Walk"` every tick (it should be harmless), and should `move` words auto-play their
  own animation (v0.1 answer: no â€” animation is always explicit, like BGE).
- **Pause game:** `pause game` / `resume game` are deliberately parked. They need their own
  rule for *what keeps ticking while paused* (proposal: menu/UI sheets and clicks stay live,
  world actors, timers and physics freeze) â€” decide when a game actually needs it, likely
  together with the Menus batch (Task 6).
- **Pathfinding edge cases:** what happens when `goto` can't reach the target (player on a
  pillar, waypoint inside a wall)? Proposal: `goto` gives up after a few seconds and â€” for now â€”
  quietly stops walking; a `when path fails` trigger is parked for when a game actually needs it.
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

---

## 10. The one-paragraph pitch

Event Manager is a text-first game-logic system for non-programmers, inspired by Blender Game
Engine's logic bricks and GDevelop's event sheets, but built for plain readable files instead of
panels and wires. You write stacked events â€” a `when` line, optional `unless` lines, then indented
actions â€” in a fixed ~40-word vocabulary, saved as `.events` files that attach to Flax actors by
name. A once-written C# interpreter runs them live and hot-reloads instantly; no code is ever
generated. The result reads like stage directions for your game, and small games get made.
