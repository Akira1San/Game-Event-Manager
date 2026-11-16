# Tasks_2.md — Mechanics for “Complete Game Creation”

The follow-up queue to `TASKS.md`, `tasks_RTS.md` and `Task 16 — TK4 Feature Parity.md`.
These are the mechanics the Event Manager still lacks when the goal is not one genre demo but
*any small game can be finished, shipped and modded*.

**How to use:** start a fresh session and say *“Work on Task 30, Part B in Tasks_2.md”*.
That session should read `DESIGN.md` for language behavior, do only that Part, and check the box.

**Source of truth:** `DESIGN.md` for how the language behaves. This file is the scope, the
examples and the reason each task exists. Every completed task ends by recording its final
decision in `DESIGN.md` §9 (Open Questions), the same as the existing batches.

**Status legend:** `[ ]` not started `[~]` in progress `[x]` done `[!]` blocked (say why)

**Numbering:** starts at Task 28 because `tasks_RTS.md` ends at Task 27.

---

## Why these tasks exist

The engine already covers a lot: movement (transform / character / dynamic / kinematic),
NavMesh `goto`, steering, sight and hearing, weapons and ammo, typed enemies, items and bags,
UI widgets, tweens, states, particles, lights, local 2-player, RTS selection/economy/fog/minimap,
and a registry-driven vocabulary. The remaining gaps are not “more genres”; they are the
primitives that every genre otherwise re-implements as copied blocks or new C#:

1. **Sets and queries** — “all of them”, “how many”, “the weakest one”.
2. **Expressions and data lookup** — formulas and table reads.
3. **Persistence** — a save file that survives quitting.
4. **Presentation** — music, camera feedback, animation depth, dynamic UI, dialogue.
5. **Genre kits** — platformer, RPG depth, racing, cards, roguelike.
6. **Shipping** — settings, localization, accessibility, replay, packaging, tooling.

## Priority at a glance

| Task | Name | Priority | Main cost | Depends on |
|---|---|---|---|---|
| 28 | Sets, counts & filtered targets | P0 | parser + runtime | Tasks 1–3 |
| 29 | Expressions & data lookup | P0 | parser + runtime | Tasks 1–3 |
| 30 | Save/load game & world snapshot | P0 | C# + file format | Tasks 1–3 |
| 31 | Audio: music, buses, fades | P0/P1 | Flax bridge | Tasks 1–3 |
| 32 | Camera & screen feedback | P1 | Flax bridge | Tasks 1–3, 15 |
| 33 | Animation depth | P1 | Flax bridge | Task 5, 12 |
| 34 | Dynamic UI: lists, grids, inventory | P1 | Flax bridge | Tasks 6, 15 |
| 35 | Dialogue & narrative | P1 | mixed | Tasks 6, 15 |
| 36 | Platformer / arcade movement kit | P1 | C# + events | Tasks 4, 12 |
| 37 | RPG combat depth | P1 | mixed | Tasks 8, 9E |
| 38 | Target filters, groups & squad AI | P2 | runtime | Task 7, 18 |
| 39 | Input breadth: axes, touch, mouse | P1/P2 | Flax bridge | Tasks 3, 16 |
| 40 | Roguelike & procedural generation | P2 | C# generator | Tasks 28, 29 |
| 41 | Racing & sports kit | P2 | C# + events | Task 12 |
| 42 | Card, board & turn-based | P2 | C# + events | Tasks 17, 28 |
| 43 | Settings, localization, accessibility | P2 | C# + bridge | Tasks 30, 15 |
| 44 | Determinism, replay & profiling | P2 | engine | Task 30 |
| 45 | Online multiplayer & split screen | P3 | decision | Tasks 16, 30 |
| 46 | Packaging, templates & modding | P2/P3 | pipeline | Tasks 19, 30 |
| 47 | Live debugger & event trace | P1 | editor plugin | `task_plugin_edtor.md` |

**Do not treat this list as a mandate to grow the vocabulary.** The project rule stays:
if two genres need it, it is a primitive and belongs in C#; if one genre needs it, ship it as a
`.events` example or template first. Every task below names which side it falls on.

## Conventions for every new word

- A word ships as: `WordRegistry` row + parser support (`ActionWords` / `Triggers` / `Conditions`)
  + runtime behavior + bridge + unit tests + `keywords.html` / examples docs.
- Pure rules live in `Source/Game/Runtime/*.cs` and are tested without Flax; the Flax calls live
  in `Source/Game/Bridge/*.cs`. This is the existing split and keeps the test project useful.
- Friendly errors are mandatory: a wrong shape explains the shape, an unknown name lists what
  exists, and a missing asset warns once.
- Existing `.events` files must keep working untouched. New words are opt-in.
- Prefer **plural targets** over nested blocks. The language is flat by design (2-space indent,
  one condition block level). Do not introduce a nested `for each` body unless an action truly
  needs per-item state.
- Any new math stays one line. If a formula needs parentheses, that is Task 29, not a local hack.

---

## Task 28 — Sets, Counts & Filtered Targets `[ ]`

Goal: let a sheet talk about **groups**, not one actor. Today `nearest` and `random` pick one
actor; `when all enemies dead` is a special case; every other “all of them” need is copied blocks
or a hand-maintained counter. This unblocks waves, AoE, loot showers, tower-defense targeting,
quest counters and RTS army logic without nested control flow.

Depends on Tasks 1–3. This is a **language primitive** (P0).

### Proposed words

| Kind | Word | Example | Why |
|---|---|---|---|
| value | `count of <thing>` | `set text of EnemyLabel to count of Enemy` | replaces hand-maintained `EnemiesAlive` |
| value | `count of <thing> near <thing> by <dist>` | `set Nearby to count of Enemy near Player by 300` | local pressure, AoE checks |
| condition | `count of <thing> is <value>` | `count of Enemy is 0` | wave/room-clear gates |
| target | `all <thing>` | `destroy all Enemy` | group verbs |
| target | `all <thing> near <thing> by <dist>` | `damage all Enemy near Player by 120` | AoE without a projectile |
| target | `nearest <thing> with lowest <var>` | `damage nearest Enemy with lowest Health by my Damage` | finishing blows, tower defense |
| target | `nearest <thing> with highest <var>` | `face nearest Enemy with highest Health` | threat targeting |
| target | `farthest <thing>` | `look at farthest Enemy` | sniper/artillery behaviour |
| action | `damage <target> by <n>` | `damage all Enemy near me by 20` | a direct damage word, so groups are useful |
| action | `damage <target> by <n> <type>` | `damage other by 10 fire` | forward-compatible with Task 37 |

Group forms are accepted by one-shot verbs only: `damage`, `destroy`, `push`, `send message`,
`hide` / `show`, `set text of`, `look at`, `face`, `play particle`, `play sound at`. Movement verbs
(`move`, `goto`) stay single-target; “everyone walk there” is a formation/order task (Task 38).

### Examples

```text
# ---- wave control without a manual counter ----
when every 3 seconds
  unless count of Enemy is higher than 6
  spawn Zombie at SpawnPoint

# ---- HUD reads the count directly ----
when count of Enemy changes
  set text of EnemyLabel to count of Enemy

# ---- a melee sweep: every enemy near the player ----
when me is near Player by 4
  damage all Enemy near Player by 120
  push all Enemy near Player back strength 40 for 0.2 seconds
  play particle "shockwave"

# ---- a turret picks the weakest target, not the nearest ----
when every 0.5 seconds
  my Target is "none"
  set my Target to "lowest"
  damage nearest Enemy with lowest Health by my Damage
```

`count of Enemy changes` uses the existing `when <var> changes` watcher; the value word is
computed and can be watched like any global. If that is awkward in the current trigger grammar,
the fallback is `when every 0.2 seconds` + a `my LastCount` comparison for the first version.

### Parts

- [ ] **Part A — `count of` as a value and condition.** Runtime resolves a name to all live actors
      (same name rules as `touches`, including plural/`Player` groups), excludes inactive and
      destroyed actors, and reads 0 for an unknown name with a one-time warning.
- [ ] **Part B — Plural targets.** `all <thing>` and `all <thing> near <thing> by <dist>` parse into
      a target list; one-shot verbs iterate in stable order (scene order, then add order). Empty
      list = quiet no-op.
- [ ] **Part C — Filtered nearest/farthest.** `nearest <thing> with lowest/highest <var>` and
      `farthest <thing>` use per-object variables; missing variable reads 0; ties break by
      distance then name so behaviour is deterministic.
- [ ] **Part D — Direct `damage` word.** `damage <target> by <n>` goes through the same path as a
      `"damage"` message: `my Armor` reduces, `my Shield` absorbs, `my Health` clamps at 0, and
      `when my Health becomes 0` fires once. No projectile is implied.
- [ ] **Part E — Tests and docs.** Empty group, one actor, many actors, name groups (`Player`,
      `Player1/Player2`), inactive/destroyed filtering, tie-break determinism, damage path parity
      with messages, `count of` inside `set text of`, and a full wave example.

### Done-when

- A wave spawner holds at 6 enemies by reading `count of Enemy`, with no `EnemiesAlive` variable.
- One `damage all Enemy near Player by 120` line hits every enemy in range exactly once.
- A turret reliably shoots the lowest-health enemy, with no per-tick target flip-flop.
- `count of Enemy` reads 0 after the last one is destroyed, and the old `when all enemies dead`
  example still behaves the same.

### Edge cases / parks

- **Nested `for each`:** parked. If a later task needs per-item state (“for each enemy, set its
  colour based on its health”), decide then whether to allow a deeper indent or a `for each`
  action that only changes the implicit target for the following lines.
- **Group movement:** not here. `move all Enemy` is a formation/order problem (Task 38), not a
  loop problem.
- **Counting by tag/type:** `count of Enemy` uses the actor name. Counting `my Type is Zombie`
  across different actor names is a follow-up (Task 38) unless the name convention already covers it.

---

## Task 29 — Expressions & Data Lookup `[ ]`

Goal: remove the one-operator ceiling where it actually hurts: RPG damage formulas, XP curves,
difficulty scaling, item stats and resistances. Also turn the existing naming-convention data
files (`WeaponData.events`, `EnemyTypes.events`, `UnitTypes.events`) into readable tables an
author can query at runtime.

Depends on Tasks 1–3. **Language primitive** (P0), but keep the shape deliberately small.

### Proposed words

| Kind | Word | Example | Why |
|---|---|---|---|
| value | multi-op expression | `set my Damage to my Strength * 2 + 5` | damage/XP formulas |
| value | `clamp <value> to <min> and <max>` | `set my Mana to clamp my Mana to 0 and my MaxMana` | potions and regen |
| value | `min <a> and <b>` / `max <a> and <b>` | `set my Speed to max my BaseSpeed and 10` | buffs, caps |
| value | `round <value>` / `floor` / `ceil` / `abs` | `set my Gold to round my Gold * 1.1` | clean HUD numbers |
| value | `percent of` | `add 10% of my Gold to my Gold` | tax, interest, scaling |
| value | `field "<field>" of "<row>" in "<table>"` | `set my Damage to field "Damage" of my Weapon in "weapon"` | data-driven items/units |
| value | `localized "<key>"` | `set text of TitleLabel to localized "menu.play"` | Task 43 |
| action | `set <var> to <expression>` | existing `set`, grown | no new statement |

Expressions stay one line and use normal precedence. Parentheses are optional in v1; if they
are added, they must not leak into trigger matching or the condition grammar.

### Examples

```text
# ---- RPG damage formula, still one readable line ----
set my Damage to my Strength * 2 + my WeaponDamage
set my Damage to my Damage + 20% of my Damage

# ---- potions respect the cap without a manual if ----
add 40 to my Mana
set my Mana to clamp my Mana to 0 and my MaxMana

# ---- a weapon's stat is read from the same data file the game already ships ----
when me appears
  set weapon to "Sword"
  set my Damage to field "Damage" of "Sword" in "weapon"
  set my FireRate to field "FireRate" of "Sword" in "weapon"

# ---- level scaling without one event per level ----
set my ZombieHealth to ZombieHealth * (1 + LevelNumber / 10)
```

`field "Damage" of my Weapon in "weapon"` maps to the existing convention: row `Sword` + field
`Damage` + table prefix `weapon` reads the global `SwordDamage`. A missing row or field warns once
and reads 0, never a silent wrong number.

### Parts

- [x] **Part A — Expression parser.** Allow 2–3 operators per value with precedence
      (`* /` before `+ -`), keep unary minus and the existing one-op forms working, and reject
      anything longer with a friendly “split this line” error. Existing files must parse unchanged.
      *Landed:* `set` reads up to three operators, folded by `Runtime/Expressions.cs` (`*` and `/`
      first, then `+ -` left to right with the old text-join and divide-by-zero rules). One
      operator keeps the previous `SetNode.Op`/`Right` shape, so every shipped `.events` file parses
      exactly as before; two or three become an `ExpressionValue`. A fourth operator fails with
      “split it in two” and shows the first half to keep. Parentheses stay parked with the rest of
      Section 9.
- [x] **Part B — Numeric helpers.** `clamp`, `min`, `max`, `round`, `floor`, `ceil`, `abs`,
      `percent of`; all are value forms, usable anywhere a number is legal.
      *Landed:* one `HelperValue` node and `ValueEvaluator.EvaluateHelper`; `clamp` accepts its bounds
      either way round, `round` sends halves away from zero, `<n>% of <v>` is the percentage form of
      `percent of` and stays unrounded (`round 10% of 55` reads 6). All operands go through the same
      `ReadValue`, so helpers nest and take `random`, variables and other helpers. They read wherever
      the event parser reads a value; conditions and trigger matching are untouched on purpose.
- [x] **Part C — Data lookup.** `field "<field>" of "<row>" in "<table>"`; the table prefix is the
      same one `WeaponData.events` / `EnemyTypes.events` / `UnitTypes.events` already use.
      Resolution is case-insensitive and ignores spaces in the row name the same way asset names do.
      *Landed:* `FieldValue` + `Runtime/DataLookup.cs` (the `<Row><Field>` naming convention,
      spaces dropped) and `EventContext.ReadField`, which resolves through
      `EventVariables.FindGlobalName` (exact, then a cached case-insensitive index) and reads the
      global, so `field "Damage" of "Sword" in "item"` is `ItemData.events`' `SwordDamage`.
      A missing row or field warns once through `EventRuntime.WarnMissingField`, names the global it
      looked for, lists the fields that row has (or the rows that have that field) and reads 0.
      The table name is carried for those messages; resolution itself is the `<Row><Field>` global.
- [x] **Part D — Friendly errors.** Unknown field lists the fields the table actually has when it
      can be discovered; unknown row suggests the nearest row name; too-long expression points at
      the operator to split.
      *Landed:* `DataLookup.MissingMessage` + `Nearest` do the lookup hints — an unknown field lists
      `"Sword" has Damage and Slot.` and adds `Did you mean "Damage"?` within the same two-edit
      `WordDistance` threshold `UnknownWord` and `EventFiles.SuggestFileName` use; an unknown row
      lists the rows that do have the field and adds `Did you mean row "Zombie"?`; a name with no
      near match gets no guess. Part A's too-long `set` line names the operator to split at and shows
      the half to keep (`set running to 1 + 2`). All three are asserted together in the tests.
- [ ] **Part E — Tests and docs.** Precedence, unary minus, clamp bounds written high-to-low,
      percent rounding, table reads for a shipped weapon and enemy type, missing row/field warnings,
      and a regression test that every existing `set` line in `GameFiles/*.events` still parses.

### Done-when

- A Diablo-style formula (`str * 2 + weapon damage`) is one line, no helper variables.
- A potion fills to the cap and never above it, with no `if` block.
- Changing `SwordDamage` in `WeaponData.events` changes the equipped sword’s damage without
  editing C# or the player sheet.
- The whole shipped test suite stays green after the grammar change.

### Edge cases / parks

- **Full expression language:** no functions, no variables-with-spaces, no multi-line formulas.
  If a project needs that, it is a different feature and should be a `formula` block, not a
  quiet growth of `set`.
- **String formatting:** `bind ... format` already covers HUD text. Do not duplicate it here.
- **Boolean operators:** `or` stays parked; see Task 38 for the practical alternatives.

---

## Task 30 — Save/Load Game & World Snapshot `[ ]`

Goal: a real save file. Today `save stats` / `load stats` is an in-memory checkpoint of globals
only (`EventVariables.cs:14,45`; the task notes call it per-session). Quitting the game loses
everything, and actor positions, bags, equipment and spawned objects are not captured at all.

Depends on Tasks 1–3. **Shipping primitive** (P0). This is the biggest gap between “demo” and
“game someone can finish”.

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `save game` | `save game` (default slot) |
| action | `save game to "<slot>"` | `save game to "slot1"` |
| action | `load game` | `load game` |
| action | `load game from "<slot>"` | `load game from "slot1"` |
| action | `delete save "<slot>"` | `delete save "slot1"` |
| trigger | `when game saves` / `when game loads` | `when game loads` |
| value | `save slot exists "<slot>"` | `unless save slot exists "slot1"` |
| action | `autosave` | `autosave` at a checkpoint |

### What a save contains

| Saved | Why |
|---|---|
| Scene name + additive scene list | restore the same level and overlays |
| Every global variable | score, lives, flags, quest state, level number |
| Every sheet’s `SourceName`, `ActorName`, `Player`, `UsePhysicsMovement` | rebuild the right logic on the right actor |
| Actor `Position`, `Orientation`, `Scale`, `IsActive` | visual and gameplay state |
| Per-object variables, bag contents, equipped item names, `CurrentState` | RPG/strategy state |
| `my spawnedFrom` marker + spawned actor list | restore clones/prefabs/scene instances |
| RNG seed | deterministic continuation / replay |

**Not saved (v1):** in-flight `wait` continuations, active tweens, particle lifetimes, queued
pushes and current path. Those restart cleanly. Document this in `DESIGN.md` §9 so nobody expects
a paused fireball to resume mid-air.

### Examples

```text
# ---- a pause/save menu ----
when SaveButton is clicked
  save game to "slot1"
  show text "Game saved."

when LoadButton is clicked
  unless save slot exists "slot1"
  show text "No save found."
  otherwise
  load game from "slot1"

when game loads
  set state playing
  show text "Welcome back."

# ---- checkpoint autosave on the level ----
when me touches Checkpoint
  set CheckpointReached to true
  autosave
  play sound "checkpoint"
```

### Parts

- [ ] **Part A — File format and location.** A versioned JSON (or Flax `JsonAsset`-friendly) file
      under the project’s save folder, with a schema version field. The format is an implementation
      detail; the words are the contract.
- [ ] **Part B — Global + scene snapshot.** Persist every global, the current scene and additive
      scenes, and the level name/number. Loading reloads the scene then restores variables in the
      right order.
- [ ] **Part C — Per-actor snapshot.** Walk every `SheetInstance`: source name, actor name, player,
      transform, active state, all per-object variables, bag, equipment, state. Rebuild sheets by
      source name after the scene loads, then apply values.
- [ ] **Part D — Spawned actors.** Recreate prefab/scene/clone instances recorded in the save, using
      the existing spawn naming and `AttachSpawnedTree` path, then restore their sheet state.
- [ ] **Part E — Slots, autosave, deletion, errors.** Default slot, named slots, autosave, delete,
      corrupt-file warning, version-mismatch refusal with a friendly message, and a “save from a
      global sheet” path.
- [ ] **Part F — Tests.** Round-trip globals, per-object values, bag and equipment; load into a
      clean scene; missing actor in the save; extra actor in the scene; old schema version; corrupted
      file; two slots isolated; hot reload does not clobber restored values.

### Done-when

- Quit the game, relaunch, `load game from "slot1"` restores score, lives, player position, health,
  bag contents and the equipped sword.
- A prefab-spawned hero and its weapon child come back with their per-object variables.
- A corrupt save shows a friendly error and leaves the current session running.
- `save stats` / `load stats` still work as the lightweight in-session checkpoint.

### Edge cases / parks

- **Save migration:** v1 refuses a newer schema and warns on an older one. Migration code comes
  only when a real schema change exists.
- **Save anywhere vs save rooms:** both are `save game`; the design choice is content, not code.
- **Multiplayer:** local 2-player saves both players’ sheets; online is Task 45.
- **Screenshots/thumbnails:** parked, nice-to-have for slot UI.

---

## Task 31 — Audio: Music, Buses & Fades `[x]`

Goal: turn `play sound` / `play step sound` into a small audio system. Every genre needs looping
music, a music switch on scene/boss/state change, volume settings and fades. Today there is no
music word, no volume word and no fade.

Depends on Tasks 1–3. **Presentation primitive** (P0/P1), mostly Flax bridge work.

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `play music "<name>"` | `play music "level1"` |
| action | `loop music "<name>"` | `loop music "menu"` |
| action | `stop music` | `stop music` |
| action | `fade music to <n> in <s>` | `fade music to 0 in 2` |
| action | `set music volume to <n>` | `set music volume to 0.4` |
| action | `set sound volume to <n>` | `set sound volume to 0.7` |
| action | `set master volume to <n>` | `set master volume to 0.8` |
| action | `play sound "<name>" at <thing>` | `play sound "shot" at me` |
| trigger | `when music ends` | `when music ends` |
| action | `duck music by <n> for <s>` | `duck music by 0.5 for 1.5` |

Volumes are 0–1. A missing clip warns once and lists the project’s clips, matching the existing
sound-name rules (folder, extension and case ignored).

### Examples

```text
# ---- menu to level ----
when scene starts
  loop music "menu"

when PlayButton is clicked
  fade music to 0 in 0.5
  wait 0.5 seconds
  load scene "Level1"

when scene starts
  loop music "level1"

# ---- boss / danger switch ----
when state becomes "boss"
  fade music to 0 in 1
  wait 1 seconds
  loop music "boss"

# ---- dialogue ducking ----
when NPC is clicked
  duck music by 0.6 for 4
  show DialogPanel
```

### Parts

- [x] **Part A — Music source.** A dedicated looping `AudioSource` (or Flax audio service if the
      API is cleaner) that survives scene loads, with a single active track and crossfade support.
- [x] **Part B — Volume groups.** Master / music / sound volumes as globals, applied to every
      source the bridge owns. Settings UI can bind them (Task 43).
- [x] **Part C — Fades and ducking.** Per-source volume ramps advanced in `Step`, no coroutines.
      `fade music`, `duck music`, and resume-after-duck.
- [x] **Part D — 3D/positional sound.** `play sound at <thing>` creates/uses an `AudioSource` on the
      actor; plain `play sound` stays non-positional. Verify the Flax 1.13 source properties.
- [x] **Part E — Tests and docs.** Volume arithmetic, fade completion, duck/restore, missing clip
      warning, looping versus one-shot, music survives `load scene`, `stop sound` does not stop music.

### Done-when

- Menu music loops, fades out, level music fades in; a boss track replaces it without a pop.
- Master volume at 0 silences music and effects; music volume affects only music.
- A gunshot played “at the enemy” attenuates with distance (or is clearly documented as 2D if the
  engine’s source cannot do it in this project).

---

## Task 32 — Camera & Screen Feedback `[ ]`

Goal: the “juice” layer. `camera follows/looks at/free/zoom/pan` already exist; what is missing is
impact feedback and scene transitions: shake, fade, flash, FOV, letterbox, and a camera that can
travel a cutscene rail.

Depends on Tasks 1–3 and 15 (tweens). **Presentation primitive** (P1).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `shake camera strength <n> for <s>` | `shake camera strength 2 for 0.3` |
| action | `fade screen to "<color>" in <s>` | `fade screen to "0 0 0" in 1` |
| action | `flash screen "<color>" in <s>` | `flash screen "1 1 1" in 0.1` |
| action | `set camera fov to <n>` | `set camera fov to 60` |
| action | `letterbox on` / `letterbox off` | `letterbox on` |
| action | `camera move to <thing> in <s>` | `camera move to BossCam in 2` |
| trigger | `when camera arrives` | `when camera arrives` |
| action | `camera follows <thing> offset <n>` | `camera follows Player offset 50` |

### Examples

```text
# ---- hit feedback ----
when message "damage" arrives
  shake camera strength 1.5 for 0.2
  flash screen "1 0 0" in 0.1

# ---- death transition ----
when my Health becomes 0
  fade screen to "0 0 0" in 1.5
  wait 1.5 seconds
  load scene "Menu"

# ---- boss intro ----
when state becomes "boss"
  letterbox on
  camera move to BossCam in 2
  wait 2.5 seconds
  camera follows Player
  letterbox off
```

`shake` is an additive offset on top of the current follow/look target, so it never fights the
normal camera words. `fade`/`flash` are a full-screen UI overlay (an `Image` under a `UICanvas`),
not a post-process dependency.

### Parts

- [ ] **Part A — Camera shake.** Strength, duration, decay curve; additive in `CameraRig`; multiple
      shakes combine or replace by strongest; deterministic in tests.
- [ ] **Part B — Fade/flash overlay.** A generated full-screen `Image` control owned by the bridge;
      color and alpha ramp; fades survive `add scene`/`load scene` long enough to cover the change.
- [ ] **Part C — FOV, letterbox, follow offset.** Bridge properties on the active camera, restored
      when the scene changes.
- [ ] **Part D — Cutscene rail.** `camera move to <thing>` is a tween on the camera transform, plus
      `when camera arrives`; `camera follows` cancels it.
- [ ] **Part E — Tests and docs.** Shake offset returns to zero, fade alpha reaches 0/1, flash
      restores, FOV clamps, letterbox toggles, rail arrival fires once.

### Done-when

- A hit visibly shakes the camera and a death fades to black before the menu loads.
- A cutscene camera move ends exactly on the marker and releases control to `camera follows`.
- No camera word ever leaves the view permanently offset after a scene reload.

---

## Task 33 — Animation Depth `[ ]`

Goal: `play anim` / `loop anim` / `stop anim` cover the BGE basics, but action games need speed
control, hit windows and layering. Without this, fighting/action RPG/platformer timing lives in
`wait` guesses.

Depends on Tasks 5 and 12. **Presentation primitive** (P1).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `set anim speed to <n>` | `set anim speed to 1.5` |
| trigger | `when anim "<name>" reaches <n>%` | `when anim "Attack" reaches 60%` |
| trigger | `when anim "<name>" hits "<marker>"` | `when anim "Attack" hits "impact"` |
| action | `play anim "<name>" upper body` | `play anim "Shoot" upper body` |
| action | `loop anim "<name>" upper body` | `loop anim "Aim" upper body` |
| action | `stop anim upper body` | `stop anim upper body` |
| action | `set root motion on` / `off` | `set root motion off` |
| trigger | `when anim "<name>" starts` | `when anim "Attack" starts` |

### Examples

```text
# ---- attack timing driven by the clip, not a guessed wait ----
when Left Mouse is pressed
  play anim "Attack"

when anim "Attack" reaches 60%
  damage nearest Enemy by my Damage
  play sound "swing"

# ---- upper body shooting while the legs keep walking ----
when Left Mouse is pressed
  play anim "Shoot" upper body

when anim "Shoot" ends
  stop anim upper body

# ---- speed up a walk cycle with movement speed ----
when every 0.2 seconds
  me is moving
  set anim speed to my Speed / 60
```

### Parts

- [ ] **Part A — Anim speed.** Per-sheet playback speed; persists across clip changes; a new clip
      inherits the current speed.
- [ ] **Part B — Time/percent and marker triggers.** Poll the active clip’s time; edge-trigger once
      per play. Markers read Flax animation event tracks if available; if not, fall back to a
      percentage declared in the word and document the limit.
- [ ] **Part C — Upper-body layer.** Use an animation slot / layered graph. If Flax 1.13’s current
      bridge cannot layer, implement the word as “replace” with a warning and park true layering.
- [ ] **Part D — Root motion.** Toggle whether the clip’s root motion moves the actor or is ignored.
      Document interaction with `move` and `goto`.
- [ ] **Part E — Tests and docs.** Speed arithmetic, 0%/100% edges, marker once, start/end edges,
      upper body stop restores base, root motion toggle does not double-move the actor.

### Done-when

- A sword swing deals damage at the clip’s impact moment, not at a hand-tuned `wait`.
- A character can shoot while walking without the legs stopping.
- `set anim speed` changes a walk cycle live and hot-reloads safely.

---

## Task 34 — Dynamic UI: Lists, Grids & Inventory `[ ]`

Goal: stop requiring one hand-authored actor per row/slot. A bag, quest log, scoreboard, ability
bar and vendor all need a UI that fills from data. Task 15 already parks a grid inventory; this
task makes the underlying list/grid words general.

Depends on Tasks 6 and 15. **Presentation primitive** (P1).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `clear list <thing>` | `clear list QuestList` |
| action | `add row to list <thing> with <value>` | `add row to list QuestList with "Find the key"` |
| action | `set row <n> of list <thing> to <value>` | `set row 1 of list QuestList to my Quest` |
| trigger | `when row <n> of list <thing> is clicked` | `when row 2 of list QuestList is clicked` |
| action | `clear grid <thing>` | `clear grid BagGrid` |
| action | `set slot <n> of grid <thing> to <item>` | `set slot 1 of grid BagGrid to Sword` |
| trigger | `when slot <n> of grid <thing> is clicked` | `when slot 1 of grid BagGrid is clicked` |
| action | `set tooltip of <thing> to <value>` | `set tooltip of SwordSlot to "12 damage"` |
| action | `show group <tag>` / `hide group <tag>` | `show group bag` |
| action | `set slot <n> of grid <thing> empty` | `set slot 3 of grid BagGrid empty` |

### Examples

```text
# ---- a text quest log ----
when QuestAccepted changes
  clear list QuestList
  add row to list QuestList with my Quest
  add row to list QuestList with "Reward: 100 gold"

# ---- a bag grid that shows names and selects on click ----
when my bag changes
  clear grid BagGrid
  set slot 1 of grid BagGrid to my FirstItem
  set slot 2 of grid BagGrid to my SecondItem

when slot 1 of grid BagGrid is clicked
  set my SelectedItem to my FirstItem
  set tooltip of BagGrid to my SelectedItem

# ---- a scoreboard ----
when scene starts
  clear list ScoreList
  add row to list ScoreList with Player1 Score
  add row to list ScoreList with Player2 Score
```

The first version may require pre-authored row/slot actors named `Row1`, `Slot1`, … and the words
fill them; a later version can create controls. Either way, the event text stays the same.

### Parts

- [ ] **Part A — List words.** Clear/add/set; rows are labels in a vertical container; overflow
      scrolls or clips; empty rows hide.
- [ ] **Part B — Grid words.** Clear/set/empty; slot = an image + label; selection highlight via
      `tween`/`set color`.
- [ ] **Part C — Click routing.** Row/slot index → the matching trigger; clicks on empty slots do
      not fire; the row’s value is readable as `my ClickedRow` / `my ClickedSlot`.
- [ ] **Part D — Tooltips and groups.** Hover text on slots, and `show/hide group <tag>` for whole
      panels (bag, quest log, pause menu) without naming every child.
- [ ] **Part E — Inventory example.** A real bag window: open/close, select item, tooltip, use
      SelectedItem on a door (chains with Task 9 D), and a vendor list if time allows.
- [ ] **Part F — Tests and docs.** Row count, empty values, index bounds, click mapping, hot reload
      keeps the last filled rows, and a text-only fallback still works.

### Done-when

- Picking up a sword adds a visible row/slot without any new scene actor.
- Clicking slot 1 selects that item and the door flow from Task 9 D works unchanged.
- A list of 20 quests can scroll instead of overflowing the screen.

---

## Task 35 — Dialogue & Narrative `[ ]`

Goal: the menu-composed dialogue works for two-line NPCs, but adventures/RPGs outgrow it quickly:
typewriter text, portraits, branching choices, a backlog and voice lines. Keep the data in
`.events`; add only the presentation pieces the buttons cannot express.

Depends on Tasks 6 and 15. **Mixed** (P1): some words, mostly data composition.

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `type text "<text>" into <thing> speed <n>` | `type text "Hello, traveler." into DialogLabel speed 30` |
| trigger | `when text finishes` | `when text finishes` |
| action | `set portrait of <thing> to "<sprite>"` | `set portrait of PortraitBox to "innkeeper"` |
| action | `set speaker to "<name>"` | `set speaker to "Innkeeper"` |
| action | `show choice "<text>"` | `show choice "I need a room."` |
| trigger | `when choice picked` | `when choice picked` |
| value | `my picked choice` | `if my picked choice is 1` |
| action | `clear dialogue` | `clear dialogue` |
| action | `add to backlog "<text>"` | `add to backlog "Innkeeper: Hello."` |

### Examples

```text
# ---- a branching intro, data in a normal .events file ----
when NPC is clicked
  show DialogPanel
  set speaker to "Innkeeper"
  set portrait of PortraitBox to "innkeeper"
  type text "Welcome. A room is 5 gold." into DialogLabel speed 30

when text finishes
  show choice "I'll take a room."
  show choice "Just passing through."

when choice picked
  if my picked choice is 1
  add -5 to my Gold
  set text of DialogLabel to "Room's the second door."
  otherwise
  set text of DialogLabel to "Safe travels."

when ChoiceA is clicked
  set my picked choice to 1
  send message "choice picked"
```

The choice buttons can stay ordinary scene buttons (`ChoiceA`, `ChoiceB`); the new words only
add typing speed, portrait/speaker handling, choice numbering and a backlog.

### Parts

- [ ] **Part A — Typewriter.** Reveal characters over time, respect `speed`, allow “click to skip”,
      and fire `when text finishes`.
- [ ] **Part B — Portraits and speaker.** Find the image/label on the named actor, swap the asset by
      name (same resolver as `play sound` / `set image`), and keep the speaker label separate.
- [ ] **Part C — Choices.** Number visible choice buttons, capture the picked index, clear the
      choices on pick. Buttons remain ordinary actors so they can be styled in the editor.
- [ ] **Part D — Backlog.** Append lines to a scrollable list (Task 34) for adventure/RPG history.
- [ ] **Part E — Tests and example.** Typing completion exactly once, skip, choice 1 vs choice 2,
      portrait miss warns once, and a three-node conversation with a story flag.

### Done-when

- An NPC conversation types out, shows two choices, branches to different lines and sets a flag.
- The same conversation can be replayed from the backlog.
- A project that never uses these words keeps the existing show/hide/button dialogue working.

---

## Task 36 — Platformer / Arcade Movement Kit `[ ]`

Goal: the missing verbs for jumping, ground state, ladders, one-way platforms and checkpoints.
Task 7 G4 phases 3–4 propose `jump` and `climb`; this task is the full kit plus the respawn words
that make a platformer a game. The character controller already computes grounded/slope/gravity
internally in `PhysicsMovement.cs`; this task exposes it to events.

Depends on Tasks 4 and 12. **Mixed** (P1).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `jump` | `jump` |
| action | `jump strength <n>` | `jump strength 420` |
| condition | `me is on ground` | `me is on ground` |
| condition | `me is airborne` | `me is airborne` |
| action | `climb <thing>` | `climb Ladder` |
| action | `stop climbing` | `stop climbing` |
| condition | `me is climbing` | `me is climbing` |
| action | `set gravity scale to <n>` | `set gravity scale to 0.5` |
| action | `respawn at <thing>` | `respawn at Checkpoint2` |
| action | `save checkpoint` | `save checkpoint` |
| trigger | `when me touches Checkpoint` | existing contact trigger |

### Examples

```text
# ---- a platformer controller ----
when Space is pressed
  unless me is airborne
  jump strength 420
  play anim "Jump" once

when me is on ground
  unless W is held
  loop anim "Idle"

# ---- a ladder ----
when W is held
  if me is near Ladder by 1.5
  climb Ladder
  otherwise
  move forward speed 180

when S is pressed
  me is climbing
  stop climbing

# ---- checkpoints and death ----
when me touches Checkpoint
  save checkpoint
  set my SpawnPoint to "Checkpoint2"
  play particle "checkpoint"

when my Health becomes 0
  fade screen to "0 0 0" in 0.5
  wait 0.5 seconds
  respawn at Checkpoint2
  fade screen to "1 1 1" in 0.5
```

One-way platforms can be a collider filter/tag plus `jump` landing rules; the first version can
use a `Platform` tag and a downward-only collision rule in the controller bridge.

### Parts

- [ ] **Part A — Ground query exposure.** `me is on ground` / `me is airborne` from the existing
      character ground probe (and the dynamic-body touch check), with a documented transform-mode
      fallback.
- [ ] **Part B — Jump.** Vertical impulse on a CharacterController/dynamic body; refused while
      airborne unless the sheet allows double jump; jump anim optional; transform-mode scripted arc.
- [ ] **Part C — Ladders.** Lock X/Z to the ladder actor, drive Y at climb speed, exit at top/bottom,
      and ignore gravity while climbing.
- [ ] **Part D — One-way platforms.** Downward pass-through from below, solid from above; opt-in via
      tag or body setting; documented PhysX limitation.
- [ ] **Part E — Checkpoints and respawn.** `save checkpoint` stores the last safe transform; `respawn`
      is a teleport that clears velocity, moves and waits. Optional lives/`respawn at start` word.
- [ ] **Part F — Tests and example scene.** Jump height/timing, no double jump, ground edge, ladder
      exit, one-way platform up/down, respawn clears momentum, checkpoint survives death but not
      a new game.

### Done-when

- A cube jumps onto a crate, lands, cannot re-jump mid-air, and dies/respawns at the last checkpoint.
- A character climbs a ladder to a platform and steps off at the top.
- A one-way platform is passable from below and solid from above in the character mode.

### Edge cases / parks

- Wall jumps, double jumps, multi-exit ladders and moving-platform passengers are out of scope,
  same as Task 7 G4.
- `coyote time` / jump buffering are tuning values, not words; park unless a game needs them.

---

## Task 37 — RPG Combat Depth `[ ]`

Goal: the shared combat core for Diablo-like RPGs and RTS abilities. Task 9 E2–E7 and Task 27
A–E already propose pieces; this task is the *combined, prioritized* version so the same buff,
cooldown, damage-type and target-pick code is built once.

Depends on Tasks 8, 9E, 27. **Mixed** (P1). Overlaps are intentional; complete this once, then
the two genre batches become content.

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `add buff "<name>" to <target> for <s>` | `add buff "burn" to other for 3` |
| action | `remove buff "<name>" from <target>` | `remove buff "burn" from me` |
| trigger | `when buff "<name>" starts` / `ends` | `when buff "burn" ends` |
| value | `my "<name>" time left` | `set text of BurnLabel to my "burn" time left` |
| action | `put "<name>" on cooldown for <s>` | `put "fireball" on cooldown for 2` |
| condition | `my "<name>" is ready` | `my "fireball" is ready` |
| value | `my "<name>" cooldown` | `set text of FireLabel to my "fireball" cooldown` |
| action | `damage <target> by <n> <type>` | `damage other by 20 fire` |
| action | `damage area around <thing> by <n> for radius <r>` | `damage area around Mouse by 30 for radius 180` |
| action | `aim "<spell>"` / `cancel aim` | `aim "meteor"` |
| trigger | `when aim picks a target` | `when aim picks a target` |
| action | `spawn item from "<table>" at <thing>` | `spawn item from "Loot" at me` |
| action | `roll "<rarity>" from "<table>"` | `roll "Rare" from "Loot"` |

### Examples

```text
# ---- burning damage over time ----
when message "fire" arrives
  add buff "burn" to other for 3
  damage other by 10 fire

when buff "burn" starts
  set my BurnTicks to 3

when every 1 seconds
  my BurnTicks is higher than 0
  damage me by 5 fire
  add -1 to my BurnTicks

# ---- a spell with mana and a cooldown ----
when Q is pressed
  my "fireball" is ready
  my Mana is higher than 9
  add -10 to my Mana
  put "fireball" on cooldown for 1.2
  aim "meteor"

when aim picks a target
  damage area around my aim target by 30 for radius 180
  play particle "explosion"

# ---- resistances ----
when message "fire" arrives
  set my LastDmg to other's damage
  set my LastDmg to my LastDmg - my FireResist
  add -my LastDmg to my Health
```

### Parts

- [ ] **Part A — Buff/debuff core.** Name, duration, stack rule (`refresh` / `stack` / `ignore`),
      modifiers, expiry, and base-value capture so overlapping buffs restore correctly. Pure
      `Runtime/Buffs.cs`, tested with the FakeWorld.
- [ ] **Part B — Cooldowns.** Per-actor named timers, one tick loop, `ready`/`time left` reads, clear
      on death, and no polling trigger per unit.
- [ ] **Part C — Damage types and resistances.** A damage message/action carries a type; the victim
      subtracts the matching resistance; immunity is a large resistance, not a new word.
- [ ] **Part D — Area damage and target-pick.** `damage area around ...` sends real damage through
      the existing armor/shield/health path and the matchup gate; `aim` enters a pick mode with
      range/validity/timeout, a marker and a cancel.
- [ ] **Part E — Loot tables and affixes.** Weighted table data in `.events`; `spawn item from` and
      `roll` read it; the affix generator (Task 9 E7) composes item stats from base + N affixes.
- [ ] **Part F — Tests and example.** Stack/refresh/ignore, expiry restores base, cooldown readiness,
      fire vs fire-resist, AoE hits exactly once, aim cancel/timeout, loot table distribution and
      an equipped +5 sword still working.

### Done-when

- A fireball spends mana, starts a cooldown, is refused while cooling down, and damages only valid
  targets in its radius.
- A fire-resistant zombie takes little damage from fire and normal damage from steel.
- A burn buff ticks, expires, and restores the pre-buff stats without drift.
- A dropped rare ring rolls 1–3 affixes and its `my` stats reflect them.

### Edge cases / parks

- Skill trees, synergies, runewords, set items, crafting, vendors and gamblers stay parked
  (Task 9 E out-of-scope).
- Buffs on buildings, research speed modifiers and stacking rules beyond refresh/stack/ignore are
  later decisions.
- Ability data (`Abilities.events`) may be added once the command card needs it; not required for v1.

---

## Task 38 — Target Filters, Groups & Squad AI `[ ]`

Goal: the remaining AI/order gaps after Task 28 gives sets and counts. Civilians need to flee,
squads need to follow and hold formation, tower defense needs “first/strongest/closest”, patrols
need numbered routes and failure handling.

Depends on Tasks 7 and 18, plus Task 28. **Runtime/C#** (P2), with content examples.

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `move away from <thing> speed <n>` | `move away from nearest Enemy speed 120` |
| action | `follow <thing>` | `follow Player` |
| action | `stop following` | `stop following` |
| action | `hold formation` | `hold formation` |
| target qualifier | `first` / `closest` / `strongest` / `weakest` | `attack closest Enemy` |
| action | `goto next <thing>` | `goto next Waypoint` |
| trigger | `when path fails` | `when path fails` |
| condition | `me is in cover` | `me is in cover` |
| action | `take cover` | `take cover` |
| trigger | `when me is attacked` | `when me is attacked` |
| condition | `me is blocked` | `me is blocked` |
| value | `nearest cover distance` | `set text of CoverLabel to nearest cover distance` |

### Examples

```text
# ---- a civilian flees ----
when message "damage" arrives
  set my FleeFrom to other
  move away from other speed 120
  wait 3 seconds
  still

# ---- a squad follows the player in formation ----
when me appears
  follow Player
  hold formation

when order completes
  hold formation

# ---- numbered patrol with a failure recovery ----
when me appears
  goto next Waypoint

when me arrives at Waypoint
  wait 1 seconds
  goto next Waypoint

when path fails
  print "patrol path failed, retrying"
  wait 0.5 seconds
  goto next Waypoint

# ---- tower-defense targeting ----
when every 0.5 seconds
  attack first Enemy in range
```

### Parts

- [ ] **Part A — Departure/follow orders.** `move away from`, `follow`, `stop following`; follow
      keeps a spacing ring and releases on target loss.
- [ ] **Part B — Formation hold.** Reuse `Formations.Slot`; a squad with `hold formation` keeps its
      slot relative to the leader instead of stacking.
- [ ] **Part C — Targeting qualifiers.** `first`, `closest`, `strongest`, `weakest` on `attack` and
      ranged words; deterministic tie-breaks; range checks respect `my attack range`.
- [ ] **Part D — Patrol sequences and path failure.** `goto next <thing>` cycles same-name waypoints
      in scene order or numeric suffix order; `when path fails` fires once per failed path and can
      retry/advance.
- [ ] **Part E — Cover and attacked triggers.** `take cover` picks a tagged `Cover` actor, `me is in
      cover` tests it; `when me is attacked` unifies “damage message arrived” with the attacker as
      `other` where available.
- [ ] **Part F — Tests and example.** Flee direction, follow spacing, formation slot stability,
      target selection, path failure retry, cover reachability, attacked trigger context.

### Done-when

- A civilian runs away from the shooter, stops after 3 s, and does not jitter at a wall.
- Three soldiers follow the player without occupying the same spot.
- A patrol survives a blocked waypoint and keeps cycling.
- A turret chooses strongest/closest/first as authored, not always nearest.

---

## Task 39 — Input Breadth: Axes, Touch & Mouse Values `[ ]`

Goal: keyboard keys, mouse buttons and gamepad buttons exist. What is missing for many genres:
analog movement, mouse/touch position as values, touch triggers, and camera-relative movement.

Depends on Tasks 3 and 16. **Flax bridge** (P1/P2).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| value | `input axis "<name>"` | `set my MoveY to input axis "MoveY"` |
| value | `mouse x` / `mouse z` | `set my AimX to mouse x` |
| trigger | `when screen is touched` | `when screen is touched` |
| value | `touch x` / `touch z` | `set my TargetX to touch x` |
| action | `move forward speed <n> relative to camera` | camera-relative movement |
| trigger | `when key combo "Left Shift+W" is pressed` | modifiers and combos |
| action | `lock cursor` / `unlock cursor` | first-person/aiming |
| value | `gamepad left x` / `left y` | raw analog stick |

### Examples

```text
# ---- twin-stick / analog movement ----
when every 0.02 seconds
  set my MoveX to input axis "MoveX"
  set my MoveY to input axis "MoveY"
  move forward speed my MoveY
  move right speed my MoveX

# ---- click/tap to move on mobile ----
when screen is touched
  set my TargetX to touch x
  set my TargetZ to touch z
  goto there

# ---- camera-relative WASD in a third-person game ----
when W is held
  move forward speed 5 relative to camera
```

Camera-relative movement is parked in `DESIGN.md` §9; this task records the decision and ships it
as an optional qualifier so existing top-down sheets are untouched.

### Parts

- [ ] **Part A — Analog axes.** Read Flax input axes (keyboard fallback = -1/0/1), exposed as a
      value; no new trigger loop required.
- [ ] **Part B — Mouse/touch values.** Screen/world position of the cursor or touch; `goto there`
      already stores the last click, so this is the read-only companion.
- [ ] **Part C — Touch triggers.** Tap/press/release with position; no multi-touch gestures in v1.
- [ ] **Part D — Camera-relative qualifier.** `relative to camera` on `move`; test all four body
      modes and the existing top-down default.
- [ ] **Part E — Cursor lock and combos.** Optional; document platform differences.
- [ ] **Part F — Tests and docs.** Axis sign, dead zone, touch position, camera-relative vectors,
      no regression for `move forward speed 5`.

### Done-when

- A gamepad stick moves a character with variable speed, not just full-speed buttons.
- A tap on the ground walks the player there on a touch device.
- Camera-relative WASD works while the same sheet’s top-down mode stays unchanged.

---

## Task 40 — Roguelike & Procedural Generation `[ ]`

Goal: seeded runs, weighted random, shuffling and room/dungeon composition. The language has
`chance` and `random a to b`; it has no seed, no list shuffle and no template-driven layout.

Depends on Tasks 28 and 29. **C# generator + data** (P2).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `seed random with <n>` | `seed random with my RunSeed` |
| value | `random seed` | `set my RunSeed to random seed` |
| action | `shuffle <thing>` | `shuffle LootPool` |
| action | `spawn room "<name>" at <thing>` | `spawn room "Crypt" at RoomSlot` |
| action | `spawn from pool "<name>" at <thing>` | `spawn from pool "Enemies" at SpawnPoint` |
| action | `next run` | `next run` |
| value | `my run number` | `set text of RunLabel to my run number` |

### Examples

```text
# ---- a deterministic run ----
when game starts
  set my RunSeed to random seed
  seed random with my RunSeed
  set my Floor to 1

# ---- room chain ----
when me arrives at RoomExit
  add 1 to my Floor
  spawn room random from "Rooms" at RoomSlot
  spawn from pool "Enemies" at RoomSlot

# ---- a weighted loot pool ----
when my Health becomes 0
  spawn from pool "DeathDrops" at me
  next run
```

### Parts

- [ ] **Part A — Seed exposure.** `seed random with` and `random seed`; every existing `random` and
      `chance` follows the seeded sequence; the seed is saved by Task 30.
- [ ] **Part B — Shuffle and weighted pools.** `shuffle` a named actor pool; `spawn from pool` uses
      weights; no repetition until the pool is exhausted (bag-of-marbles).
- [ ] **Part C — Room/dungeon templates.** Load `.scene`/prefab room templates, connect exits, and
      validate doorway alignment; start with hand-authored room files, not a full generator.
- [ ] **Part D — Run lifecycle.** `next run` wipes run variables, reseeds, reloads the first scene;
      meta-progression stays in a separate save slot.
- [ ] **Part E — Tests and example.** Same seed = same sequence, shuffle fairness, pool depletion,
      room attach offsets, run reset does not wipe meta progress.

### Done-when

- Two runs with the same seed produce the same loot/enemy sequence.
- A three-room dungeon assembles from templates with connected exits.
- `next run` starts a fresh floor while keeping unlocked meta upgrades.

---

## Task 41 — Racing & Sports Kit `[ ]`

Goal: laps, checkpoints, timers, positions and a vehicle body. The engine’s physics already
supports dynamic bodies and `moveplatform`; what is missing is race logic and vehicle handling.

Depends on Task 12. **C# + events** (P2).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `start timer "<name>"` / `stop timer "<name>"` | `start timer "lap"` |
| value | `my "<name>" time` | `set text of LapLabel to my "lap" time` |
| action | `set lap to <n>` / `add 1 to lap` | `add 1 to my Lap` |
| trigger | `when lap completes` | `when lap completes` |
| action | `respawn at last checkpoint` | `respawn at last checkpoint` |
| value | `my race position` | `set text of PosLabel to my race position` |
| action | `vehicle throttle <n>` | `vehicle throttle 1` |
| action | `vehicle steer <n>` | `vehicle steer -0.5` |
| action | `vehicle brake` | `vehicle brake` |

### Examples

```text
# ---- a lap ----
when me touches StartLine
  my Lap is higher than 0
  add 1 to my Lap
  stop timer "lap"
  set my BestLap to my "lap" time
  start timer "lap"

when scene starts
  set my Lap to 0
  start timer "lap"

when me touches WrongWay
  respawn at last checkpoint

# ---- a simple vehicle ----
when every 0.02 seconds
  vehicle throttle input axis "Throttle"
  vehicle steer input axis "Steer"
```

Vehicle words are optional: a project can drive a dynamic body with `push`/`move` instead. Document
the difference so nobody expects arcade handling from a raw `RigidBody`.

### Parts

- [ ] **Part A — Named timers.** Start/stop/read, pause-aware, saved by Task 30.
- [ ] **Part B — Laps and checkpoints.** Ordered checkpoint list, lap validation (wrong-way/missed
      checkpoint), best-lap storage.
- [ ] **Part C — Race position.** Rank actors by lap + checkpoint + distance; read for any actor.
- [ ] **Part D — Vehicle words.** Throttle/steer/brake over Flax wheel joints or a simplified
      body-force model; verify Flax 1.13’s vehicle support before committing.
- [ ] **Part E — Tests and demo.** Lap count, wrong-way rejection, timer accuracy, position order,
      respawn to last checkpoint, vehicle turns at low/high speed.

### Done-when

- Three laps count correctly, a missed checkpoint does not count, and best lap shows on the HUD.
- A vehicle accelerates, steers and brakes with gamepad axes; a non-vehicle sheet is unaffected.

---

## Task 42 — Card, Board & Turn-Based `[ ]`

Goal: turn order, decks, hands, draw/discard and undo. States and timers can fake this, but a real
card/board game needs a turn manager and hidden information.

Depends on Tasks 17 and 28. **C# + events** (P2).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `start turn` / `end turn` | `end turn` |
| trigger | `when turn starts` / `when turn ends` | `when turn starts` |
| value | `current player` | `set text of TurnLabel to current player` |
| action | `draw <n> cards` | `draw 5 cards` |
| action | `shuffle deck` | `shuffle deck` |
| action | `discard "<card>"` | `discard "Fireball"` |
| action | `play card "<card>"` | `play card "Fireball"` |
| trigger | `when card drawn` | `when card drawn` |
| action | `undo last action` | `undo last action` |

### Examples

```text
# ---- a simple two-player turn loop ----
when game starts
  shuffle deck
  set current player to 1
  start turn

when end turn button is clicked
  end turn

when turn starts
  add 1 to my Turn
  draw 1 cards
  set text of TurnLabel to current player

when card drawn
  play sound "card"
  add 1 to my HandCount

when play card "Fireball"
  damage other by 6
  discard "Fireball"
```

Deck contents can be a normal `.events` data file listing card names and counts, read at boot.
The first version may keep the deck as a named list rather than a new collection type.

### Parts

- [ ] **Part A — Turn manager.** Current player, turn number, start/end edges, optional phases
      (`draw` → `main` → `end`).
- [ ] **Part B — Deck/hand/discard.** Named piles, draw, shuffle, discard, empty-deck reshuffle;
      counts readable as values.
- [ ] **Part C — Undo.** Snapshot the last action’s state (a small, bounded history) for card/board
      play; do not promise undo for real-time games.
- [ ] **Part D — Hidden information.** Per-player hand visibility; a card actor shows its face only
      to its owner.
- [ ] **Part E — Tests and demo.** Draw/discard counts, reshuffle, turn edges, undo restores hand
      and board, hot reload keeps the turn state.

### Done-when

- Two players alternate turns, draw cards, play one and the turn advances.
- Reshuffling an empty deck does not lose cards.
- Undo returns the last played card to hand and restores the target’s health.

---

## Task 43 — Settings, Localization, Accessibility & Achievements `[ ]`

Goal: the product layer around a finished game. Key rebinding already exists; this task covers
the rest of a settings menu, translated text, accessibility options and achievements.

Depends on Tasks 30 and 15. **C# + bridge** (P2).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `set resolution to "<w>x<h>"` | `set resolution to "1920x1080"` |
| action | `set fullscreen to <bool>` | `set fullscreen to true` |
| action | `set language to "<code>"` | `set language to "fr"` |
| value | `localized "<key>"` | `set text of TitleLabel to localized "menu.play"` |
| action | `set ui scale to <n>` | `set ui scale to 1.25` |
| action | `set subtitles on` / `off` | `set subtitles on` |
| action | `show subtitle "<text>" for <s>` | `show subtitle "The gate is locked." for 3` |
| action | `unlock achievement "<name>"` | `unlock achievement "first_blood"` |
| trigger | `when achievement unlocked` | `when achievement unlocked` |
| action | `save settings` / `load settings` | `save settings` |

### Examples

```text
# ---- settings menu ----
when ApplyButton is clicked
  set resolution to "1920x1080"
  set fullscreen to true
  set master volume to 0.8
  set language to "fr"
  save settings
  set text of TitleLabel to localized "menu.play"

# ---- an achievement ----
when my Health becomes 0
  unlock achievement "first_death"
  show subtitle "You died. Try again." for 3
```

### Parts

- [ ] **Part A — Settings persistence.** Resolution, fullscreen, volumes (Task 31), language, UI
      scale; saved to disk separately from game saves and applied at boot.
- [ ] **Part B — Localization.** A string table keyed by `menu.play` etc.; `localized` reads it;
      switching language refreshes bound labels; missing key shows the key and warns once.
- [ ] **Part C — Accessibility.** Subtitle display, UI scale, colorblind-safe palette hook,
      hold-vs-toggle option, difficulty assists as documented globals.
- [ ] **Part D — Achievements.** Unlock once, persist, fire a trigger, optionally show a toast.
- [ ] **Part E — Tests and docs.** Settings round-trip, language switch, missing key, subtitle
      timing, achievement unlock-once across saves.

### Done-when

- A settings menu changes resolution, volume and language, and the choices survive a restart.
- Switching to French changes visible menu text without editing each event.
- An achievement unlocks once and remains after quitting.

---

## Task 44 — Determinism, Replay & Performance `[ ]`

Goal: make runs reproducible for debugging, roguelikes and racing ghosts, and give the project a
way to see where time goes before adding a 1,000-unit RTS.

Depends on Task 30. **Engine** (P2).

### Proposed words / tools

| Kind | Word | Example |
|---|---|---|
| action | `start recording "<name>"` | `start recording "run1"` |
| action | `stop recording` | `stop recording` |
| action | `play replay "<name>"` | `play replay "run1"` |
| action | `set fixed tick to <n>` | `set fixed tick to 60` |
| value | `frame time` | `print frame time` |
| action | `profile on` / `profile off` | `profile on` |

### Examples

```text
# ---- a seeded roguelike run ----
when game starts
  set my RunSeed to random seed
  seed random with my RunSeed
  start recording "last_run"

# ---- a bug report ----
when F9 is pressed
  stop recording
  print "replay saved"
```

A replay stores the seed plus input edges per tick; playback re-simulates the same world. This
requires deterministic physics and RNG, so it is honest to scope it as “best effort” until the
bridge is proven stable.

### Parts

- [ ] **Part A — Seeded RNG end-to-end.** Every `random`/`chance` uses the runtime RNG; no hidden
      `System.Random` per subsystem.
- [ ] **Part B — Fixed tick and input recording.** Record key/mouse edges and the seed; playback
      drives the same `InputEdges` path.
- [ ] **Part C — Profiler.** Per-sheet tick time, event count, raycast/sheet counters, and a
      `frame time` value; an overlay toggle for development builds.
- [ ] **Part D — Budgets and interest management.** Skip sheets far outside the camera/active set
      only when a project opts in; default stays “tick everything” so behaviour never changes
      silently.
- [ ] **Part E — Tests and docs.** Same seed + same inputs = same result; replay of a short session;
      profiler counters sane; no regression in the normal path.

### Done-when

- A recorded 30-second run replays to the same score and actor positions.
- The profiler identifies the most expensive sheets in an RTS stress scene.
- Turning profiling off returns behaviour and timing to exactly the previous state.

---

## Task 45 — Online Multiplayer & Split Screen `[ ]`

Goal: **decide the scope**, not build netcode yet. Local 2-player and shared-screen co-op exist;
split screen and online are unbuilt. This task records the decision so the other tasks do not
accidentally assume networked state.

Depends on Tasks 16 and 30. **Decision task** (P3).

### Questions to answer in `DESIGN.md` §9

- [ ] Is online multiplayer in scope for this project at all, or is local-only the promise?
- [ ] If online: authoritative server, listen server or peer-to-peer? Which transport?
- [ ] Which state is replicated: transforms, animation state, variables, spawned actors, messages?
- [ ] Do events run on both peers, or only the owner/server?
- [ ] How are `send message` and per-object variables synchronized?
- [ ] Does a save file include other players’ state? (Task 30 assumes local only.)
- [ ] Split screen: per-player camera viewports and input routing; parked in Task 16.

### Recommendation to record

Local co-op is the shipping promise. Online is a separate product decision with its own milestone;
do not grow the event vocabulary for it until the transport and authority model are chosen.

### Done-when

- `DESIGN.md` §9 states “local only” or names the online milestone and its authority model.
- The save/load and input tasks explicitly say “local players only” or link to the online plan.

---

## Task 46 — Packaging, Templates & Modding `[ ]`

Goal: make a finished game shippable and moddable. The `.events` files are already data; this task
turns that into a workflow: templates a beginner can start from, a one-click export, and a mod
folder that can add events/assets without rebuilding the engine.

Depends on Tasks 19 and 30. **Pipeline** (P2/P3).

### Proposed words / tools

| Kind | Word | Example |
|---|---|---|
| action | `load mod "<name>"` | `load mod "extra_levels"` |
| value | `mod is loaded "<name>"` | `mod is loaded "extra_levels"` |
| action | `reload mods` | `reload mods` |
| tool | genre templates | `TopDownCharacter.events`, `EnemyTemplate.events`, `Platformer.events`, `RTSPlayer.events` |
| tool | export preset | scenes, assets, default save folder, version stamp |

### Examples

```text
# ---- a mod-aware boot ----
when game starts
  load mod "extra_levels"
  unless mod is loaded "extra_levels"
  print "extra_levels mod missing, using base game"
```

Templates should be real projects, not code generation: copy the folder, open it, play. The
existing Task 19 parameterized sheets are the basis.

### Parts

- [ ] **Part A — Template gallery.** A top-down twin-stick, a platformer, an RPG starter, an RTS
      starter and a point-and-click starter, each with one playable scene and commented `.events`.
- [ ] **Part B — Export/packaging checklist.** Scenes in the build list, default save folder,
      version/commit stamp, no editor-only assets, a clean first-run state.
- [ ] **Part C — Mod folder format.** `.events` files plus assets under a `Mods/<name>/` folder;
      conflicts warn and keep the base game running; hot reload continues to work.
- [ ] **Part D — Docs generation.** Generate the Word Book / `keywords.html` from `WordRegistry`
      (the existing plan) and include it in the export.
- [ ] **Part E — Acceptance.** A new user copies a template, changes one line, plays; a mod adds a
      scene and a word-compatible item without touching C#.

### Done-when

- A template opens, plays and hot-reloads with no missing assets.
- A mod folder adds a level and is visible in a level list.
- The exported build starts with no editor dependencies and a clean save folder.

---

## Task 47 — Live Debugger & Event Trace `[ ]`

Goal: the editor plugin (`task_plugin_edtor.md`) covers coloring, errors, autocomplete and hover
docs. What is still missing is runtime visibility: which events fired this tick, which variables
changed, why a trigger did not fire, and a way to pause/step a sheet.

Depends on the editor plugin (`task_plugin_edtor.md`) and Tasks 1–3. **Tooling** (P1).

### Proposed tools

| Tool | Example | Why |
|---|---|---|
| event trace | “fired `when W is held` → `move forward` 3 ticks ago” | the #1 debugging need |
| watch window | pin `my Health`, `Score`, `count of Enemy` | no `print` spam |
| trigger inspector | show last evaluation, edge state, cooldown | why didn’t it fire? |
| action log | last 50 actions per sheet with values | reproduce a bug |
| pause / step | pause one sheet or the whole world | inspect a transient state |
| error panel | file, line, friendly message, jump to line | already planned in the plugin |

### Examples of output

```text
[Player.events] tick 1203
  when W is held            -> pass
  move forward speed 5      -> moved 2.5 cm
  when Left Mouse is pressed -> not fired (no press this tick)

[Enemy.events] tick 1203
  when me sees Player by 8  -> not fired (ray blocked by Wall_3 at 4.2 m)
```

### Parts

- [ ] **Part A — Runtime trace buffer.** Ring buffer of matched triggers/actions with tick numbers,
      bounded and disabled unless the debugger asks for it.
- [ ] **Part B — Watch values.** Read globals and per-object values by actor + name; no changes to
      the runtime data model.
- [ ] **Part C — Trigger inspector.** Show the last evaluation and the reason for a false result
      where cheap (distance, ray hit, edge state).
- [ ] **Part D — Pause/step.** Freeze sheet ticking or the whole world; UI sheets stay live; resume.
- [ ] **Part E — Tests and docs.** Trace overhead is zero when off, buffer bounds, watched value
      survives hot reload, pause does not corrupt timers.

### Done-when

- A non-firing event can be diagnosed in the editor without adding `print` lines.
- A watched value updates live during play and survives a hot reload.
- Disabling the debugger restores the normal frame cost exactly.

---

## Suggested execution order

Do not run these by number. The dependency-friendly order is:

1. **Task 28** (sets/counts/filtered targets) — everything else gets shorter.
2. **Task 30** (save/load) — turns demos into games.
3. **Task 29** (expressions/data lookup) — unblocks RPG/strategy formulas.
4. **Task 31 + 32** (audio + camera feedback) — cheapest “real game” upgrade.
5. **Task 34 + 35** (dynamic UI + dialogue) — the content-facing layer.
6. **Task 36 + 37** (platformer kit + RPG combat core) — two genre pillars.
7. **Task 38 + 39** (AI orders + input breadth) — polish for action/strategy.
8. **Task 33 + 43 + 44** (animation depth, settings/localization, replay/perf) — shipping quality.
9. **Tasks 40–42** (roguelike, racing, cards) — only when a project asks.
10. **Tasks 45–47** (multiplayer decision, packaging, debugger) — scope and workflow.

## Explicitly not in this file

These stay in their existing queues or are deliberately composed from events:

- **Editor plugin M3** — `task_plugin_edtor.md`.
- **RTS AI brain, abilities, research** — `tasks_RTS.md` Tasks 25 and 27 (Task 37 here shares the
  buff/cooldown/target-pick core).
- **TK4 parity, weapons, typed enemies, level stats** — `Task 16 — TK4 Feature Parity.md`.
- **Grid inventory, bag counts, floating combat text, `or`-chains, multi-op math, pause policy,
  richer camera, saving** — already logged as parked in `DESIGN.md` §9; the tasks above are the
  formal home once they are picked up.

## Definition of done for this file

A task is only `[x]` when:

- the words parse and the runtime behaves as the examples show;
- a project that never uses the new words still parses and plays exactly as before;
- unit tests cover the pure rules and at least one shipped `.events` example uses the feature;
- `WordRegistry`, `keywords.html` / examples docs and `DESIGN.md` §9 are updated;
- the editor play-check is either done or explicitly left as a user step with exact setup notes.
