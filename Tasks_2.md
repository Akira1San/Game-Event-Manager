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
7. **Verification** — a self-test sheet that *runs* the editor play-checks instead of leaning on
   eyeballs and memory.

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
| 37 | Diablo-style RPG kit | P1 | mixed | Tasks 1–3, 27, 29 |
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
| 48 | Animated game cursors | P1 | Flax bridge + table | Tasks 3, 15 |
| 49 | Mouse button action mapping | P1 | bridge + table | Tasks 3, 16, 29 |
| 50 | Dialogue files & conversation trees | P1 | file format + runtime | Tasks 34, 35 |
| 51 | Self-test sheet: automated play-checks | P0 | runtime + events | Tasks 1–3 |

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

## Task 29 — Expressions & Data Lookup `[x]`

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
- [x] **Part E — Tests and docs.** Precedence, unary minus, clamp bounds written high-to-low,
      percent rounding, table reads for a shipped weapon and enemy type, missing row/field warnings,
      and a regression test that every existing `set` line in `GameFiles/*.events` still parses.
      *Landed:* the regression test re-parses every `set` line in every `GameFiles/*.events` sheet
      (including `test/`) on its own under a `when game starts` trigger — 468 lines today — and fails
      with the file and message if one stops parsing. `keywords.html` now introduces the value forms
      as one story ("a value can also be computed") before listing math, helpers and `field`;
      `DESIGN.md` opens Part A-D with a four-line example that uses all three, then explains what each
      does and where it stops.

### Done-when

- A Diablo-style formula (`str * 2 + weapon damage`) is one line, no helper variables.
- A potion fills to the cap and never above it, with no `if` block.
- Changing `SwordDamage` in `WeaponData.events` changes the equipped sword’s damage without
  editing C# or the player sheet.
- The whole shipped test suite stays green after the grammar change.

### Edge cases / parks

- **Full expression language:** no user-defined functions, no variables-with-spaces, no multi-line
  formulas. Sharing whole blocks between files is `include "<file>"` (a file-level line, expanded
  at load), not a function: there are no parameters and no return value. If a project needs those,
  it is a different feature and should be a `formula` block, not a quiet growth of `set`.
- **String formatting:** `bind ... format` already covers HUD text. Do not duplicate it here.
- **Boolean operators:** `or` stays parked; see Task 38 for the practical alternatives.

---

## Task 30 — Save/Load Game & World Snapshot `[~]`

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

- [x] **Part A — File format and location.** A versioned JSON (or Flax `JsonAsset`-friendly) file
      under the project’s save folder, with a schema version field. The format is an implementation
      detail; the words are the contract.
      *Landed:* `Runtime/SaveFile.cs` owns the contract — one pretty-printed JSON file at
      `<ProjectFolder>/Saves/<slot>.json`, `default.json` for the default slot, with
      `"format": "event-manager-save"` and `"version": 1`. Slot names are trimmed and every
      character that is not a letter, digit, space, `-` or `_` becomes `_`, so a save can never
      leave `Saves/`. Writes go through a `.tmp` file and one replaced move. `TryRead` reports
      missing / damaged / newer / older instead of throwing, a newer schema is refused untouched,
      an older one loads with a warning, and a file without the format marker or version is
      damaged rather than half-read. The `SaveData` sections are already fixed for Parts B–D
      (scene + additive list + level, globals, per-actor transform/active/locals/bag/equipment/
      state/spawn marker, spawned prefab/scene/clone records, RNG seed); `SaveVariable` converts
      `ValueBox` values both ways. Nothing is wired to the words yet — that is Part E.
- [x] **Part B — Global + scene snapshot.** Persist every global, the current scene and additive
      scenes, and the level name/number. Loading reloads the scene then restores variables in the
      right order.
      *Landed:* `Runtime/SaveSnapshot.cs` owns the non-actor half of a save. `Capture` takes the
      scene section the bridge knows — `FlaxActions.CaptureSaveScene` lists the loaded scenes in
      load order, naming each by its asset file name so a copied scene reloads — and adds every
      global sorted by name with number/text/boolean kinds intact, plus `LevelName`/`LevelNumber`
      read from the globals (falling back to the level order's own state when a sheet wiped
      them). `Load` reloads the level, puts its overlays back in order (`FlaxActions.AddSavedScene`
      mirrors `add scene`, sheets and `when scene starts` included), and only then writes the
      globals: a reloaded scene's own `when scene starts` and the runtime's scene-start seeding
      would otherwise overwrite what the save restored. The globals are replaced, not merged, so
      a variable the save never had is gone; `Levels.Resume` rebases the level clock so
      `time elapsed` keeps counting from the save instead of restarting with the reload; and a
      save whose level scene is missing is refused before anything changes. The words stay Part E.
- [x] **Part C — Per-actor snapshot.** Walk every `SheetInstance`: source name, actor name, player,
      transform, active state, all per-object variables, bag, equipment, state. Rebuild sheets by
      source name after the scene loads, then apply values.
      *Landed:* `Runtime/SaveActors.cs` captures one `SaveActor` per real sheet (the global
      `GlobalHost` is skipped) sorted by actor name, with `Source`, actor name, player,
      `UsePhysicsMovement`, active flag, world transform, every per-object variable (sorted, kinds
      intact), bag stacks, equipped names, `CurrentState` and `spawnedFrom`. The engine-free
      runtime has no transform or active flag, so `SaveSnapshot.Capture` takes optional readers and
      the bridge supplies `FlaxActions.CaptureSaveTransform` / `CaptureSaveActive`; without them a
      record gets the identity transform and counts as active. A restore runs after the scene has
      reloaded and applies over the fresh sheet: per-object variables are **merged** (a saved value
      wins, an authored default the save never wrote survives), while the bag and equipment are
      **replaced** (clear, then set) because what the actor carried at save time is not something
      to merge. A live actor whose sheet is missing is rebuilt from the saved `Source` through
      `EventsManager.ProgramForSource` + `runtime.AddSheet`, then gets its state. A restored sheet
      clears `PushQueue` and `Waiting` and writes `CurrentState` and `LastState` together, so the
      `when me appears` a rebuilt sheet queues does not run and `when state is` sees no false entry
      edge — a load is a moment in the run, not an arrival. A saved actor this scene cannot provide
      is warned about and reported in `SaveActors.Result.Missing`, not fatal. The bridge's
      `ApplySaveTransform` warps through `TeleportSubject` (cancelling moves and stopping a dynamic
      body) before writing orientation and scale, and `ApplySaveActive` restores the flag.
      `SaveSnapshot.Load` now restores globals, then actor records, in that order. The words stay
      Part E.
- [x] **Part D — Spawned actors.** Recreate prefab/scene/clone instances recorded in the save, using
      the existing spawn naming and `AttachSpawnedTree` path, then restore their sheet state.
      *Landed:* `Runtime/SaveSpawns.cs` captures one `SaveSpawned` per spawned **root** — never a
      scene child — sorted by actor name. The kind is recorded when the spawn happens
      (`EventRuntime.MarkSpawnedRoot`), not guessed at save time: `spawn prefab` marks `prefab`,
      `spawn <template>` and `place` mark `clone`, while `spawn scene` adds a whole tree the scene
      section already restores, so this build writes no `scene` records. `MarkSpawned` alone (a
      source with no root mark, e.g. a prefab child or a scene-instanced tree) never becomes a
      record, and `RemoveActorSheet` / a scene reload forget the markers. A record keeps the source
      word exactly as written, the spawned root's actor name (suffix included), its parent actor
      name, transform and active flag. A load recreates prefabs first, then clones (a clone's
      template may be a prefab root the same save recreates), applies the saved name **verbatim**
      (actor records are keyed by it, and the spawn naming already made it unique), attaches the
      sheet tree through `EventsManager.AttachSpawnedTree` / `AttachSpawnedActor`, and only then
      parents every root and writes transforms and active flags — after parenting, so a parent
      cannot move a restored child. `SaveSnapshot.Load` recreates the spawns before the actor
      records, so `findActor` can see the recreated roots. Bridge helpers: `CaptureSaveParent`
      (empty for a scene parent), `CreateSavedSpawned`, `FinishSavedSpawned`, `SetSaveParent`. A
      missing source or parent is warned about and the root is skipped (or left at the scene root),
      while an unknown kind is skipped; `scene` records are left to Part B's scene section. The
      words stay Part E.
- [x] **Part E — Slots, autosave, deletion, errors.** Default slot, named slots, autosave, delete,
      corrupt-file warning, version-mismatch refusal with a friendly message, and a “save from a
      global sheet” path.
      *Landed:* `Runtime/SaveGames.cs` is the engine-free coordinator behind `save game`
      (`save game to "<slot>"`), `load game` (`load game from "<slot>"`), `autosave` and
      `delete save "<slot>"`. A blank slot means `SaveFile.DefaultSlot` (`default`); `autosave`
      always writes `SaveFile.AutosaveSlot` (`autosave`), so a checkpoint never overwrites a save
      the player made, and `load game from "autosave"` resumes it. Every attempt returns a
      `SaveGames.Result` with `Outcome` (`Saved`/`Loaded`/`Deleted`/`Missing`/`Damaged`/
      `NewerVersion`/`Refused`/`Failed`), the normalized slot and one friendly message; the bridge
      passes `FlaxActions.Warn`, so a typo, missing file, damaged JSON or newer schema is a warning
      and never an exception. A load reads the file **before** touching anything, so missing,
      damaged and newer saves leave the running game alone; a scene the project no longer has is
      refused by `SaveSnapshot.Load` for the same reason. `when game saves` fires after the file is
      written; `when game loads` fires after the scene reloaded and globals, spawns and actors are
      restored (`RaisePushAll`, so it runs on the restored sheets next tick). Both triggers carry the
      slot name. `save slot exists "<slot>"` reads the disk through the new
      `EventRuntime.SaveProjectFolder`, which `EventsManager` sets from
      `EventFiles.LocateProjectFolder` (the folder holding `Content` and `Source`), so a runtime
      without a project folder answers no instead of throwing. `save stats` / `load stats` remain the
      in-memory checkpoint and still run before action parsing. The bridge wires
      `CaptureSaveScene` + the Part C/D helpers to `SaveGames.Save/Load`; `delete save` removes one
      file and reports an already-gone slot as missing. The words are in `WordRegistry` and
      `keywords.html`.
- [x] **Part F — Tests.** Round-trip globals, per-object values, bag and equipment; load into a
      clean scene; missing actor in the save; extra actor in the scene; old schema version; corrupted
      file; two slots isolated; hot reload does not clobber restored values.
      *Landed:* one end-to-end block drives the real slot files through `SaveGames.Save` /
      `SaveGames.Load`. It saves a run with a prefab-spawned Hero and its weapon child, then loads
      that slot into a scene that has never seen either actor: the spawn root and its child are
      recreated, and the level, overlay, globals, per-object values, bag stacks, equipped sword,
      state, transform and active flag all come back. The same scene carries an extra actor the
      save never knew (its own values survive) and the save carries a `Ghost` the scene cannot
      provide (one warning, the rest still loads); `when me appears` stays suppressed after the
      restore. A `SwapSheetProgram` hot reload after the load keeps the restored globals, locals,
      bag and equipment. The focused cases stay in Parts B–E — clean-scene ordering, missing and
      extra actors, the old schema, the damaged file and two isolated slots — so this block tests
      the seams between them rather than repeating each half.

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

## Task 33 — Animation Depth `[x]`

Goal: `play anim` / `loop anim` / `stop anim` cover the BGE basics, but action games need speed
control, hit windows and layering. Without this, fighting/action RPG/platformer timing lives in
`wait` guesses.

Depends on Tasks 5 and 12. **Presentation primitive** (P1). Part C additionally depends on **content
authoring**: a base Anim Graph asset with a `Blend With Mask` node and an `Animation Slot`, plus a
`SkeletonMask` asset, created in the Flax editor and shipped with the project. The bridge cannot
synthesize them — `InitAsAnimation` builds a single-clip graph with no nodes to blend or inject into,
so Part C is not startable until those assets exist.

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

- [x] **Part A — Anim speed.** Per-sheet playback speed, passed as the `speed` argument of
      `AnimatedModel.PlaySlotAnimation` so a change takes effect on the running clip with no restart.
      The value persists across clip changes and a new clip inherits it. Clips played on the
      full-body `InitAsAnimation` path have speed baked in at graph build, so a live change there
      rebuilds the graph; document that as a restart, or route all playback through slots to remove
      the limit.
- [x] **Part B — Time/percent and marker triggers.** `when anim "<name>" reaches <n>%` polls the
      active clip’s progress (`AnimPlayback.Progress`, speed-scaled) and edge-triggers once per play,
      re-armed by the bridge’s per-play token. `when anim "<name>" hits "<marker>"` reads Flax
      animation event tracks through the project’s own `MarkerAnimEvent` (an authored `AnimEvent`):
      Flax exposes an Animation’s event list to the editor only, so a marker has to be placed on the
      clip. A clip with no markers falls back to `reaches <n>%`, as the registry documents.
- [x] **Part C — Upper-body layer.** `play anim "<name>" upper body` and `loop anim "<name>" upper
      body` call `AnimatedModel.PlaySlotAnimation("UpperBody", clip, 1, 0.2, 0.2, loop ? -1 : 0)`;
      `stop anim upper body` calls `StopSlotAnimation("UpperBody")`. The speed argument stays 1 on
      purpose: the actor's speed already rides on `AnimatedModel.UpdateSpeed` (Part A), which scales
      the whole graph including slots, so asking the slot for it as well would apply it twice. A
      layered play is tracked separately from the full-body clip, so a held trigger does not restart
      it, `stop anim` drops it with the graph it lived on, a mid-clip speed change re-times its end,
      and `when anim "<name>" ends` fires for a layered one-shot (which is what the docs example's
      `stop anim upper body` hangs off). Slot playback never goes through `InitAsAnimation`, which
      replaces the whole pose including the legs — the bug this part exists to fix — so a model with
      no authored base graph, or whose graph is one of the bridge's own single-clip graphs, gets a
      single warning instead.
      **Content dependency, now the only thing left:** the character's base Anim Graph (a `Blend With
      Mask` node whose Pose A is the locomotion pose and Pose B an `Animation Slot` named `UpperBody`,
      feeding the final output) and a `SkeletonMask` for it with spine, chest, neck, head, shoulders,
      arms and hands enabled and **hips, root, legs and feet disabled** are authored in the Flax editor.
      The bridge cannot synthesize them, and no character in this project ships them yet, so the words
      are ready but the layer is unplaytested until those assets exist.
- [x] **Part D — Root motion.** `set root motion on` / `off` is a per-sheet action word
      (`ActionVerb.SetRootMotion`, `ActionSpec.RootMotionOn`) with the default **off**, so no existing
      sheet changes behaviour. Engine-free `Runtime/RootMotion.cs` holds the rules the bridge obeys:
      `OutcomeFor(hasGraph, bridgeBuiltGraph)` says what the word can actually do — `NoGraph` warns
      that nothing is animated yet, `FallbackIgnores` warns that a bridge-built single-clip graph never
      extracts root motion (there the clip *is* the whole pose, and `move` / `goto` owns the transform,
      so both would double the displacement), and `AuthoredGraph` is the only case with a real effect:
      the graph's `AnimatedModel.RootMotionTarget` is pointed at the sheet's actor **once** (without a
      target Flax applies the motion to the model itself, usually a child of that actor). Ownership
      rule, enforced by construction: **root motion belongs to the locomotion clip only** — nothing in
      the bridge enables it for a slot, so an upper-body clip cannot double-apply displacement or
      slide the feet (`RootMotion.SlotsMove` is false).
      **What the code cannot do, and needs content for:** Flax's C# API exposes root motion only as the
      `InitAsAnimation` build flag for a virtual graph and as `RootMotionTarget` at runtime — there is
      no property to flip extraction on an authored graph. So with only the bridge's own graphs, the
      toggle can do nothing at all, and `set root motion off` cannot switch off an authored graph that
      extracts motion; a clip actually moving its actor needs an authored base Anim Graph with root
      motion enabled (Extract Node / Center of Mass plus flags) and a clip imported with root motion —
      both are editor content. Interaction with `move` / `goto`: clip motion is applied **in addition
      to** the sheet's own movement, so drive an actor one way or the other, not both.
- [x] **Part E — Tests and docs.** The table's last word is built here too: `when anim "<name>"
      starts` (`TriggerKind.AnimStart`, matched loosely like `ends`) is pushed by the bridge when a
      play really begins — `play anim` / `loop anim`, whole-body or layered — so a held trigger that
      re-plays nothing pushes nothing.
      Suite checks: speed arithmetic (`EndTime` scales, out-of-range speeds clamp, `RetimedEnd` keeps
      what was played), `reaches 0%` firing on the first tick and not again, `reaches 100%` waiting
      for the last frame, one marker crossing firing once rather than per tick, and the start/end
      edges answering their own clip only.
      The example sheet `Source/Game/GameFiles/Animation.events` is the acceptance sheet: a hit at
      `reaches 60%`, the `hits "impact"` alternative, a shooting layer over a walk, live speed and
      `set root motion on`, with editor setup notes in its header. The suite loads it and proves every
      action line is a shipped word and that the four Task 33 trigger kinds all appear, so a doc that
      drifted from the language fails in CI instead of in the editor.
      **Two bullets stay editor play-checks** — they exist only on content this project does not ship
      yet, so no suite check can prove them (Task 51 Part E: that is the written reason — a case can
      exercise words, not content the project has never shipped): (1) `stop anim upper body` blends the masked bones back
      onto the locomotion pose with the legs still walking (base graph + `SkeletonMask`); (2) `set
      root motion on` moves the actor once, without double-moving it beside `move` (authored graph
      with root motion enabled + a clip imported with root motion). Play-check list:
        1. Left Mouse: the swing damages at ~60% of the clip, not before, and `when anim "Attack"
           starts` fires once per press.
        2. Hold Left Shift: the swing and the walk visibly speed up with no restart or pop.
        3. Walk with W, then Right Mouse: the upper body shoots while the legs keep walking; the
           layer fades out when the clip ends, and `stop anim upper body` leaves the walk intact.
        4. With root motion content: walking moves the actor once (watch it beside `move`), and
           `set root motion off` does not double it.

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

- [x] **Part A — List words.** Clear/add/set; rows are labels in a vertical container; overflow
      scrolls or clips; empty rows hide.
      New engine-free `Runtime/Lists.cs` (`ListManager` / `ListModel`) holds the rows per list, so
      they are runtime state and survive a hot reload and a scene change; `SceneStarted` repaints
      them, like the backlog. The four statements are their own nodes, parsed before the generic
      `clear` / `add` / `set` shapes: `clear list <thing>`, `add row to list <thing> with <value>`,
      `set row <n> of list <thing> to <value>` and the agreed fill word `fill list <thing> from my
      bag`. Rows are numbered from 1 (a literal 0 is a friendly load-time error); `set row` past
      the end pads blank rows, so a scoreboard can fill row 2 before row 1 and row 3 stays row 3;
      a blank value holds its index but shows nothing; `fill list` **replaces** the list with one
      row per bag stack in the bag's stable order (`Sword x2`), so `when my bag changes` keeps a
      bag window exactly in sync instead of growing duplicate rows. Every word raises
      `OnListChanged(name, rows)`; the bridge's `FlaxActions.SetList` paints `QuestListRow1`,
      `QuestListRow2`, … or `Row1`, `Row2`, … children of an actor named `QuestList`, hides blank
      rows, and warns once naming the row to create (and the overflow row count) — the authored
      rows can sit in a scroll panel, so a 20-line log scrolls instead of overflowing. Registry +
      `keywords.html` rows. 18 new checks -> **2396 passed, 0 failed**;
      `dotnet build Source/Game.csproj -c Editor.Windows.Debug` 0 errors (13 pre-existing doc
      warnings). Editor play-check pending (authored row labels, the scroll panel and a real
      repaint after a hot reload).
- [x] **Part B — Grid words.** Clear/set/empty; slot = an image + label; selection highlight via
      `tween`/`set color`.
      New engine-free `Runtime/Grids.cs` (`GridManager` / `GridModel` / `GridSlot`) holds each grid's
      slots as runtime state, so a hot reload keeps them and `SceneStarted` repaints them beside the
      lists. The four statements are their own nodes: `clear grid <thing>`, `set slot <n> of grid
      <thing> to <value>`, `set slot <n> of grid <thing> empty` and the same one fill word shape as
      Part A, `fill grid <thing> from my bag`. Slots are numbered from 1 (a literal 0 is a friendly
      load-time error, and a line that neither says `to` nor `empty` names both shapes); writing
      past the end pads empty slots; `empty` clears one slot's text and icon without touching the
      numbering. A bare item name is kept as written (`to Sword`), while a global that really
      exists still reads its value (`set Gold to 42` → the slot shows 42); an undefined bare word is
      never evaluated, so the ordinary “read before set” warning does not fire for an item name.
      `fill grid` replaces the grid with one slot per bag stack in the bag's stable order, the text
      carrying the count (`Sword x2`) while the icon keeps the bare name so the picture resolves.
      Every word raises `OnGridChanged(name, slots)`; `FlaxActions.SetGrid` paints
      `BagGridSlot1`, `BagGridSlot2`, … or `Slot1`, `Slot2`, … children of an actor named `BagGrid`,
      keeps the slot frame on screen when the slot is empty (the Label blanks and the optional
      `Icon` child hides), resolves the icon through the same loose texture index as `set image`
      with no warning for an item that has no art, and warns once naming the slot to create plus the
      overflow count. Selection highlight stays authored with `tween` / `set color`, so no word was
      added for it. Registry + `keywords.html` rows. 20 new checks -> **2416 passed, 0 failed**;
      `dotnet build Source/Game.csproj -c Editor.Windows.Debug` 0 errors (13 pre-existing doc
      warnings) and `Flax.Build -target=GameEditorTarget` exits 0. Editor play-check pending (a real
      grid of authored slots with icons, a selection tint, and a hot reload repaint).
- [x] **Part C — Click routing.** Row/slot index → the matching trigger; clicks on empty slots do
      not fire; the row’s value is readable as `my ClickedRow` / `my ClickedSlot`.
      Two trigger shapes, `when row <n> of list <thing> is clicked` and `when slot <n> of grid
      <thing> is clicked`, classify to `TriggerKind.Clicked` with a new `ClickedCell` marker and the
      1-based index (parsed before the generic `is clicked` shape, so the phrase never becomes an
      actor name). Matching walks up from the clicked actor: `<Thing>Row1` / `<Thing>Slot1`, a
      `Row1` / `Slot1` child of an actor named after the list/grid, or anything inside one (a slot's
      `Icon`), which needed one new world query — `IEventWorld.ParentOf` — implemented by
      `FlaxWorld`, `DefaultEventWorld` and the test world. A cell fires only while the runtime model
      still holds text there (`Lists.RowsFor` / `Grids.SlotsFor`), so a blank row from `add row` and
      a slot cleared by `set slot … empty` never fire; a click on an unrelated actor fires nothing.
      Just before the body runs, `BindClickedCell` writes the cell's text onto the sheet answering
      the click, so `my ClickedRow` / `my ClickedSlot` is readable (`Sword x2` for a counted stack).
      Ordinary `when <thing> is clicked` is untouched. Registry + `keywords.html` rows for both
      triggers and both value names. 12 new checks -> **2428 passed, 0 failed**; `dotnet build
      Source/Game.csproj -c Editor.Windows.Debug` 0 errors and `Flax.Build -target=GameEditorTarget`
      exits 0. A follow-up fixed the clipping hole this note used to record: the bridge's fallback
      screen-rect test (`MouseOverUIControl`, used when Flax's canvas delivered no mouse event) now
      builds the rect from `Control.ScreenPos`/`Width`/`Height` and intersects it with every parent
      that clips (`ContainerControl.ClipChildren` or a `ScrollableControl`), through the engine-free
      `Runtime/UiHitTest.cs`, so the hidden part of a partially scrolled row cannot be clicked; a
      collapsed parent or a zero-size control is never clickable, and `VisibleInHierarchy` is
      honoured. The engine test still broadcasts a click to every `UIControl` under the cursor (a
      container hears a click its child also hears), and clipping for the visibility of the control
      tree relies on Flax's own `IsMouseOver`, which already walks clipped children. Known limits
      recorded here rather than hidden: the trigger is data-driven, so hand-authored rows that no `add row` / `set row` line filled do
      not answer clicks; `my ClickedRow` keeps the last clicked value after its block ends; and a
      mistyped cell trigger is an unknown trigger that never fires, like every other mistyped trigger
      (the language has no load-time error path for trigger shapes). Editor play-check pending (a
      real grid of slots with icons, a scroll panel, and a tint on the selected slot).
- [x] **Part D — Tooltips and groups.** `set tooltip of <thing> to <value>` writes the hover text
      of the actor's UI control (`Control.TooltipText`), so a tooltip can be a literal or a value —
      `set tooltip of BagGrid to my SelectedItem` follows the selection; an empty text clears it, and
      an actor with no control on a `UIControl` gets one warning naming it. Flax shows only the
      hovered control's own tooltip (`Control.OnMouseMove` asks that control's `ShowTooltip`, which
      is false for empty text) and never walks up to a parent, so the text is stamped on the whole
      control subtree through the engine-free `Runtime/UiTooltipTargets.Collect` — the slot's child
      Button/Label/icon answers the cursor, not just the `UIControl` root. `show group <tag>` /
      `hide group <tag>` show or hide every actor carrying that Flax tag, checked before the plain
      `show <thing>` form so `group` never becomes an actor name. This needed one new world query:
      `IEventWorld.Actors()` deliberately skips inactive actors (contact polls must not tick
      templates), so a panel closed by `hide group` would have been invisible to `show group`.
      `ActorsWithTag` walks every scene actor whether or not it is active, implemented by
      `FlaxWorld` (`CollectTagged`), `DefaultEventWorld` and the test world. Tags are authored on the
      actor in the editor — there is no runtime `tag` word — and tagging the panel actor is enough
      because its children follow their parent's active state. `EventRuntime.OnSetTooltip` carries
      the tooltip to the bridge beside `OnSetText`. Registry + `keywords.html` rows for all three
      words. 12 new checks (literal and variable tooltips, the value resolved through the runtime
      hook, the friendly parse error, both group words, the bare `show group` hint, a hidden
      panel found by tag then again after showing it, and the tooltip walk: child art included,
      root first, a leaf is its own only target, a missing root has none) -> **2447 passed,
      0 failed**; `dotnet build Source/Game.csproj -c Editor.Windows.Debug` and
      `Flax.Build -target=GameEditorTarget` pass. Known limits: the bridge's group loop (setting
      `IsActive` on each member, re-attaching spawned ones) is not unit-tested, only the query under
      it; `show group` turns on every tagged member unconditionally, so a member hidden
      individually is shown too; tooltips are set on the controls and are not stored in the runtime,
      so they do not survive a scene reload; and a template actor carrying the tag would be woken
      by `show group`, so leave templates untagged. The subtree stamp was reasoned from Flax's own
      `Control.OnMouseEnter` / `OnMouseMove` source (a child control only shows its own text), but
      the visible hover behaviour is still an editor play-check.
- [x] **Part E — Inventory example.** A real bag window: open/close, select item, tooltip, use
      SelectedItem on a door (chains with Task 9 D), and a vendor list if time allows.
      *Landed:* the playable half is two authored files — no new words and no new C#:
      `Content/scenes/UI_Bag.scene` (a `Player` carrying the `EventSheet` for `Bag`, the `BagPanel`
      tagged `bag` with its `BagGrid` and six `BagGridSlotN` controls, a label inside each slot, a
      `SelectedLabel` and a `BagButton`, every control `ClipChildren`) and
      `Source/Game/GameFiles/Bag.events`. The sheet hides the window at scene start with
      `hide group bag`, opens it on **B** with `show group bag` + `fill grid BagGrid from my bag`,
      and each of the six slots has its own `when slot <n> of grid BagGrid is clicked` block that
      copies `my ClickedSlot` into `my SelectedItem`, writes `SelectedLabel` and stamps
      `set tooltip of BagGridSlot<n>` — the per-slot target matters, because a tooltip on `BagGrid`
      itself would show the last clicked item's name over every slot. Collecting is a real world
      click rather than a seed line: `when SwordPickup is clicked` / `when PotionPickup is clicked`
      add the item, `hide` the pickup and refill the grid, so a slot appears the moment something
      is picked up. The pickup actor must be the collider the camera ray hits and must not hang
      under an actor carrying an `EventSheet` (the click resolves to the nearest sheet actor, so
      under `Player` it would arrive as `Player`), and one sitting behind the panel's 240×200
      screen rectangle cannot be clicked while the window is open — a UI hit consumes the click
      before the world raycast. Its clicks also exposed two scene layout faults, fixed in the
      scene JSON: slots 4–6 were stacked on top of each other (two at (0,100)/(110,50) and one at
      (110,100)) with their labels offset outside the box, and one slot had no label at all.
      Played by hand (B opens the window, a pickup adds a slot, slot 1 shows `Sword` in
      `SelectedLabel`, the tooltip names the slot that was clicked, empty slots stay silent);
      **2452 passed, 0 failed**, `dotnet build Source/Game.csproj -c Editor.Windows.Debug` 0 errors,
      `Flax.Build -target=GameEditorTarget` exit 0. Limits: hover text exists only after a slot is
      clicked (there is no cell-hover trigger), a pickup needs one authored block per actor because
      a click trigger names one actor (no tag wildcard), the vendor list was left out, and the
      Task 9 D door cross-link is untouched — the door already reads `my SelectedItem` and nothing
      in it changed.
- [x] **Part F — Tests and docs.** Each item now has a check behind it: row count (`CountOf` /
      `RowsFor`, the five-row fill and the case-insensitive `questlist`), empty values (a blank
      `add row` keeps its numbering and shows nothing; `set slot … empty` clears text and icon),
      index bounds (slot 0 is a load-time friendly error, writing past the end pads, reading an
      unknown list or grid is empty rather than a ghost record), click mapping (Part C's trigger
      checks, the `Icon` child mapping up, an empty cell never firing), and hot reload
      (`SwapSheetProgram` then the list/grid still holds the old rows and appends the new one).
      The text-only fallback got the four checks it was missing: a grid slot and a list row fill in
      a world with **no scene actors at all** (the words are data; warning about missing row/slot
      actors is the bridge's job), an unknown list or grid reads empty, a slot with no icon name
      keeps its text, and twenty quests are twenty rows in order with no truncation — scrolling
      them is the scene's job. Registry + `keywords.html` rows arrived with Parts A–D. 5 new checks
      -> **2452 passed, 0 failed**. Still editor play-checks rather than units: the bridge painting
      authored rows/slots (and warning once when they are missing), a repaint after a hot reload,
      and a 20-row scroll panel; the Task 34 done-when items (pickup adds a row/slot with no new
      actor, slot 1 selects and the Task 9 D door flow, a 20-quest list scrolls) depend on the
      Part E bag scene.

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

- [x] **Part A — Typewriter.** Reveal characters over time, respect `speed`, allow “click to skip”,
      and fire `when text finishes`.
      `Runtime/Typewriter.cs` owns the timing (`TypingSession` / `Typewriter`): a line is
      `floor(elapsed × speed)` characters, a second `type text` into the same label replaces the
      line still typing, and each line announces itself exactly once. `type text "<text>" into
      <thing> speed <n>` parses to a `TypeTextNode` (the words and the speed may both be values)
      and writes its growing prefix through `OnSetText`, so the label shows exactly what the
      clock allows. `EventRuntime.SkipTyping()` completes every line still typing, and
      `EventsManager` asks it first on a left click — the click skips the line instead of
      pressing the choice underneath. `when text finishes` (plus the named `when text of
      <thing> finishes`) is a broadcast trigger, so a Game sheet can hear any finished line.
      23 new checks -> **2184 passed, 0 failed**; GameEditorTarget build 0 errors (pre-existing
      XML-doc warnings only). Editor play-check pending — Task 51 Part E: no case, because what
      remains is a label typing on an authored `DialogLabel` with a font and panel you can look at;
      the typing, skip, choice and backlog logic is played from disk through `Innkeeper.events`.
- [x] **Part B — Portraits and speaker.** Find the image/label on the named actor, swap the asset by
      name (same resolver as `play sound` / `set image`), and keep the speaker label separate.
      `set portrait of <thing> to "<sprite>"` is a `SetPortraitNode` executed through
      `EventRuntime.OnSetPortrait`; the bridge's `FlaxActions.SetPortrait` calls the same private
      `SwapImageBrush` as `set image of`, so both use `LoadTexture` (the folder/extension/case-
      ignoring index shared with the cursor art) and a miss warns once with the pictures the
      project has. `set speaker to "<name>"` (`SetSpeakerNode`, any value) records the name in the
      `Speaker` global and hands it to `OnSetSpeaker`, which writes only the scene's own
      `SpeakerLabel` actor — the portrait control is never touched, and `set speaker to ""`
      clears both. Missing actors/labels warn once and name the actor to create.
      9 new checks -> **2193 passed, 0 failed**; GameEditorTarget build 0 errors. Editor
      play-check (a portrait actually appearing in a scene) pending, since it needs authored
      Image/Label actors and imported portrait textures. Task 51 Part E: no case — an asset swap
      onto authored actors is visual; the resolver and its warn-once are unit-covered.
- [x] **Part C — Choices.** Number visible choice buttons, capture the picked index, clear the
      choices on pick. Buttons remain ordinary actors so they can be styled in the editor.
      `show choice "<text>"` is a `ShowChoiceNode`; `Runtime/Choices.cs` (`ChoiceManager`,
      `ChoiceSet`, `ChoiceOption`) finds the next authored button slot — `Choice1`, else
      `ChoiceA` — numbers it (`1. I'll take a room.`), writes the label through `OnSetText` and
      shows the actor through the new `OnSetChoiceVisible` → `FlaxActions.SetChoiceVisible`.
      Two lines are two choices (up to 9); a pick removes the set, so the next line starts at 1
      again. `EventRuntime.RaiseClick` intercepts a click on a registered button: it writes
      `my picked choice` on the sheet that showed the choices, hides every button of the set, and
      pushes the message `"choice picked"` to that sheet — which is why `when choice picked` is
      classified as that message, so a hand-wired `ChoiceA` that sets its own index and runs
      `send message "choice picked"` reaches the same trigger. A button no set is using keeps
      its ordinary click; a missing slot warns once and names the actor to create.
      17 new checks -> **2210 passed, 0 failed**; GameEditorTarget build 0 errors. Editor
      play-check (a real clicked button in a scene) pending. Task 51 Part E: no case — a real click
      needs the authored buttons and the editor's input; `RaiseClick`, the numbering and the pick
      are unit-covered.
- [x] **Part D — Backlog.** Append lines to a scrollable list (Task 34) for adventure/RPG history.
      Task 34's list words do not exist yet, so the backlog uses Task 34's own first-version
      convention: the runtime keeps the history and the scene's authored row labels show it.
      `add to backlog "<text>"` is an `AddBacklogNode`; `Runtime/Backlog.cs` (`BacklogManager`,
      `BacklogLine`) keeps the lines oldest-first, ignores a blank line, and caps them at the
      `BacklogLimit` global (100 by default). The history is game state, so it survives a scene
      change and a hot reload — a conversation can be replayed from it. `EventRuntime.OnBacklogChanged`
      hands the lines to `FlaxActions.SetBacklog`, which fills `BacklogRow1`, `BacklogRow2`, … (up
      to the first gap) with the newest lines, newest in the last row, hides the rows the history
      does not fill, and warns once naming the actors to create; the rows are ordinary UIControl
      actors, so font, width and a scroll panel around them are editor content. Rows repaint on
      scene start too, so a new scene shows the history it already has.
      `clear dialogue` is a `ClearDialogueNode`: one word dismisses this sheet's dialogue — the
      line still typing is dropped without answering `when text finishes` (it was closed, not
      read; new `Typewriter.TakeActor`), every label that sheet's `type text` wrote empties
      (remembered by the new `Runtime/Dialogue.cs` `DialogueState`), its numbered choices hide
      (`ChoiceManager.Take`), and the speaker it wrote clears (`Speaker` global + `SpeakerLabel`)
      while a speaker another sheet wrote survives. The backlog is history, so it is left alone.
      17 new checks -> **2227 passed, 0 failed**; GameEditorTarget build 0 errors. Editor
      play-check (authored `BacklogRow` actors actually filling) pending. Task 51 Part E: no case —
      filling authored rows is visual; the history, its cap and its survival are unit-covered.
- [x] **Part E — Tests and example.** Typing completion exactly once, skip, choice 1 vs choice 2,
      portrait miss warns once, and a three-node conversation with a story flag.
      `Source/Game/GameFiles/Innkeeper.events` is the shipped conversation, wired only from these
      words: a greeting node (speaker, portrait, typed line at 40 cps, two numbered choices and a
      backlog line), a choice node (branches on `my picked choice`, sets the `MetInnkeeper` and
      `RentedRoom` story flags, adds the reply to the backlog) and a close node (`clear dialogue` +
      `hide DialogPanel`, reached by the message `"close dialogue"`). The suite plays that file
      from disk through both branches, the returning-guest branch, the close node and a
      click-to-skip, and the existing shipped-file parse audit covers it. Completion exactly once,
      skip, choice 1 vs choice 2 and the missing-choice warning stay covered by their parts'
      checks; a missing portrait's warn-once lives in the bridge, so it remains unverified until an
      editor play-check. 24 new checks -> **2251 passed, 0 failed**. Only editor play-checks
      remain for Task 35 (authored `DialogLabel`, `SpeakerLabel`, portrait, `Choice1`/`Choice2` and
      `BacklogRow*` actors in a scene) — and Task 51 Part E leaves all of them reason-carrying, not
      cases: they are authored UI actors and one picture, while the conversation itself (both
      branches, the flag, the backlog, the close, click-to-skip) is played from disk in the suite.

### Done-when

- An NPC conversation types out, shows two choices, branches to different lines and sets a flag.
- The same conversation can be replayed from the backlog.
- A project that never uses these words keeps the existing show/hide/button dialogue working.

---

## Task 36 — Platformer / Arcade Movement Kit `[~]`

Goal: the missing verbs for jumping, ground state, ladders, one-way platforms and checkpoints.
Task 7 G4 phases 3–4 shipped `jump` and `climb`; this task is the rest of the kit — the respawn
words and the platform options that make a platformer a game. The character controller already
computes grounded/slope/gravity internally in `PhysicsMovement.cs`; Part A exposed it to events.

Depends on Tasks 4 and 12. **Mixed** (P1).

**As shipped (2026-11-16, Task 7 G4 phases 3–4 — commits `7b19336` and `6469e44`):**
`jump` / `jump strength <n>` (default 350 cm/s, floored at 20; CharacterController, dynamic body
or scripted arc; optional `Jump` clip), `me is on ground` / `me is airborne`
(`ConditionKind.OnGround` / `Airborne` over `IEventWorld.IsOnGround`), `climb <thing>` /
`climb down` / `stop climbing` and `me is climbing`. Parts A and C below are therefore done. The
airborne gate on `jump` is still unconditional, which is what makes a double jump a **counted
exception** rather than a removed gate: that count is **Task 21 Part A** in `TASKS.md`, the one
owner of double jump (`my Jumps` + `jump again`), and it is not re-specified here.

### Proposed words

| Kind | Word | Example | State |
|---|---|---|---|
| action | `jump` | `jump` | shipped (G4 p3) |
| action | `jump strength <n>` | `jump strength 420` | shipped (G4 p3) |
| condition | `me is on ground` | `me is on ground` | shipped (G4 p3) |
| condition | `me is airborne` | `me is airborne` | shipped (G4 p3) |
| action | `climb <thing>` | `climb Ladder` | shipped (G4 p4) |
| action | `stop climbing` | `stop climbing` | shipped (G4 p4) |
| condition | `me is climbing` | `me is climbing` | shipped (G4 p4) |
| action | `set gravity scale to <n>` | `set gravity scale to 0.5` | Part D, new |
| action | `save checkpoint` | `save checkpoint` | shipped (Part F) |
| action | `respawn at <thing>` | `respawn at Checkpoint2` | shipped (Part F) |
| trigger | `when me touches Checkpoint` | existing contact trigger | already shipped |

`set gravity scale to <n>` scales the gravity of the sheet's own actor (the `me` of the sheet, the
same way `jump` applies to `me`); a per-target form is a later suffix, not part of this task.

### Examples

```text
# ---- a platformer controller ----
when Space is pressed
  unless me is airborne
  jump strength 420
  play anim "Jump" once

# `me is on ground` is a condition, not a trigger: poll it and gate the line.
when every 0.1 seconds
  me is on ground
  unless W is held
  loop anim "Idle"

# ---- a ladder ----
# distances are engine units (cm), the same as G4's `when me is near Ladder by 100`.
when W is held
  if me is near Ladder by 100
  climb Ladder
  otherwise
  move forward speed 180

when S is pressed
  me is climbing
  stop climbing

# ---- checkpoints and death ----
when me touches Checkpoint
  save checkpoint
  play particle "checkpoint"

when my Health becomes 0
  fade screen to "0 0 0" in 0.5
  wait 0.5 seconds
  respawn at Checkpoint2
  fade screen to "1 1 1" in 0.5
```

### Parts

- [x] **Part A — Ground query exposure.** Shipped 2026-11-16: `me is on ground` / `me is airborne`
      read the existing character ground probe (`FlaxActions.IsGrounded` — the controller's own ray
      plus its slope limit, a short downward ray from the pivot for every other actor, trigger
      volumes ignored), the jump's own airborne flag, and Task 28's altitude, so a lifted unit
      still reads airborne. Transform mode is covered by the same pivot ray; a sheet whose
      `GroundCheckDistance` is 0 always reads airborne.
- [x] **Part B — Jump.** Shipped 2026-11-16, with the counted second take-off on 2026-11-22
      (**Task 21 Part A**, the single owner of double jump, which reuses this same
      `ActionVerb.Jump` path). `jump` / `jump strength <n>` / `jump again` /
      `jump again strength <n>` routes by body (CharacterController fall speed, dynamic
      `LinearVelocity.Y`, scripted `PhysicsMovement.JumpStep` arc for kinematic and bodyless
      actors), plays a `Jump` clip only when the actor has a skinned model and the project has the
      clip, and refuses while airborne unless the word is `jump again` and the actor's
      `my Jumps` still has a take-off left (default 1 per landing, so a bare `jump` can never
      chain; 2 allows one air jump, 0 forbids it). The count comes back on landing, `respawn at`
      and a scene restart. Two known limits of the shipped arc are not covered by a test or the
      play-check: it writes `Position.Y` directly instead of going through `Move`, so it is
      unswept and a transform-mode actor can clip a ceiling it jumps into; and a Dynamic sheet
      whose `RigidBody` is missing or not on an ancestor silently falls back to that arc, without
      the child-body warning the move path gives.
- [x] **Part C — Ladders.** Shipped 2026-11-16: `climb <thing>` / `climb down` with an optional
      `speed <n>`, `stop climbing`, `me is climbing`. X/Z lock to the ladder actor's axis, Y is
      driven at the climb speed (default 150 cm/s), the span is the ladder's collider height or
      `my LadderHeight` or 400 cm, reaching the end of the climb ends it so `move forward` steps
      off the top, gravity is not applied while climbing, and starting a climb cancels the
      actor's pending moves.
- [ ] **Part D — Gravity scale.** A per-actor multiplier on the gravity the bridge already
      applies: `1` normal, `0.5` half, `0` none. Flax `AutoGravity` stays off, so the scale is
      written in the three places gravity already exists — the character path
      (`CharacterFallSpeeds` decayed by `PhysicsMovement.FallSpeed`), the scripted arc
      (`PhysicsMovement.JumpStep`), and a dynamic body's `GravityScale` — and nowhere else.
      Document the interaction: a `0`-scale actor never lands, so `me is airborne` stays true and
      `jump` refuses (the gate reads the same probe); a moon-jump sheet lowers the jump strength
      instead. Tests: `0.5` roughly halves the scripted peak, `0` never lands, and resetting to `1`
      restores the normal arc.
- [ ] **Part E — One-way platforms.** **The heaviest part of the kit — do it last, or park it
      until a project asks.** PhysX has no one-way collision, so the bridge has to fake it:
      - Character and kinematic actors: toggle the collider pair (`Physics.IgnoreCollision` /
        `CanCollide`) against each opted-in platform while the actor's feet are below the
        platform's top and rising, and restore it once they clear the top.
      - The ground probe must learn the same rule, or an actor rising through a platform "lands"
        on the underside it just passed: `IsGrounded` / `IsCharacterGrounded` skip a platform the
        actor is still below.
      - Dynamic bodies: the same toggle, plus zeroing `LinearVelocity.Y` on landing so the body
        does not bounce through the restored collider.
      - Opt-in: a `Platform` physics material (the contact triggers already match on material
        names) or a per-actor flag; anything not opted in stays solid.
      - Documented PhysX limitation: the toggle is frame-order sensitive, so a fast fall can
        tunnel past the restore frame — note a fall-speed cap and a thin platform collider.
- [x] **Part F — Checkpoints and respawn.** Shipped 2026-11-20. `save checkpoint` takes the
      acting actor's own pose in the bridge and writes it through `Runtime/Checkpoints.cs` as four
      per-actor variables — `CheckpointX` / `CheckpointY` / `CheckpointZ` / `CheckpointYaw`
      (degrees, decomposed from Flax's `Orientation` quaternion by `Checkpoints.YawOf`, because
      nothing else in the bridge reads Euler angles off it). One word each on purpose, so a sheet
      reads `my CheckpointX` like any other variable; the variables are ordinary locals, so Task
      30's save file carries the checkpoint for free and a new game simply never writes one.
      `TryRead` asks `HasLocal` first, so looking for a checkpoint that was never saved warns
      nothing. `respawn at <thing>` resolves the checkpoint actor (qualifier or duplicate number
      included) and warps through the existing `TeleportSubject` — cancelling the actor's moves
      and pushes, clearing fall speed, the jump flags and climbs, and stopping a dynamic body —
      which is why a respawn also restores Task 21's jump count. It does not touch health, state
      or animation: a sheet that dies at `my Health` 0 sets its own health back first. Not
      shipped (still optional): a bare `respawn` returning to the stored pose.
- [x] **Part G — Tests and the example sheet.** Shipped 2026-11-20.
      `Source/Game/GameFiles/Platformer.events` is the kit in one sheet, with its movement keys in
      `Platformer_Controls.events`: W walks and grabs a ladder, Space jumps at 420, S lets go, `me is on ground` / `is airborne` / `is climbing` /
      `me is moving` pick the Idle / Run / Fall / Climb poses, `when me touches Checkpoint` runs
      `save checkpoint` behind a `my CheckpointSaved` gate (one saved point never re-saves, because a
      body can leave a marker and step back in, and a respawn lands right on top of one), K is a
      debug death, and `when my Health is lower than 1` respawns at
      `Checkpoint` behind a `my Dead` guard (so a death is a moment, not a re-entering state). Its
      header is the scene setup, so the file carries its own instructions. Split 2026-11-23: the
      movement keys (W walks at `my WalkSpeed`, Space jumps at `my JumpPower`) live in the shared
      `include "Platformer_Controls"`, while the ladder grab stays above the include so `climb`
      registers first and the shared `unless me is climbing` line skips the walk in that tick —
      neither file repeats the other. The suite loads the sheet the way the runtime does (include
      expanded) and proves every action line is a shipped word, that the ladder grab is still
      checked before the shared walk line, and that the sheet
      really exercises `jump`, `climb`, `stop climbing`, `save checkpoint` and `respawn at`, and
      that the checkpoint block still opens on its gate and closes it after the save (with
      `when me appears` starting the flag at 0, so a new game saves again) — an example that drifted
      into an unknown word, or lost a guard, now fails a check instead of the editor. Jump
      height, the double-jump refusal and the ground states are already unit-covered by G4's own
      checks and Task 21 A; the bridge and the editor are not, so **the play-check below is still
      yours.**

      *(Task 51 Part E — four of the five steps below are now cases in `SelfTest.scene`, run there in
      the editor: `jump takes off and lands`, `ladder climb starts and lands`, `checkpoint saves on
      touch`, `respawn at returns to the marker`. All four are `(editor only)`, so the headless tier
      reports them `SKIP` instead of quietly passing; what no case can judge is the number on step 1
      (≈90 cm), the pose on step 2, and the label surviving death on step 5.)*

      **Editor play-check — `PlatformerTest` scene.** Setup is in the sheet's header (Player with a
      CharacterController + EventSheet `Platformer`, a thin `Ladder` BoxCollider against the
      platform, a `Checkpoint` actor, a `CheckpointLabel` in a UICanvas, optional Idle / Run /
      Jump / Fall / Climb / Death clips). Run it in this order:

      1. **Jump height.** Stand still, press Space, mark the peak. `jump strength 420` at the
         default gravity (981 cm/s²) should reach ≈90 cm — `420² / (2 × 981)` — a little over one
         60 cm crate. Note the measured height and whether the Jump pose plays at take-off.
      2. **Ground edge.** Walk off a ledge without jumping: Fall should take over the tick the
         ground probe misses and Idle should be back on landing, with no stuck pose in between.
         Repeat stepping off a platform onto a *lower* one and note the pose — that is the case the
         old ground check got wrong.
      3. **Ladder exit.** Hold W into the ladder: it climbs at 150 cm/s to the top of the collider
         and then W walks the character onto the landing, because the span ends the climb by
         itself. Press S mid-ladder to let go and keep your height. If holding W at the top
         re-grabs the ladder, note it — that is the 40 cm gate in the sheet, and the fix is moving
         the ladder out or lowering the distance.
      4. **Respawn clears momentum.** Touch `Checkpoint`, then hold W and press K while moving. The
         character should come back at rest on the marker — no slide, no leftover push, no fall
         speed carried through the warp — and a Space press before landing should refuse, because
         the warp clears the jump flag too.
      5. **Checkpoint survives death, not a new game.** Read `CheckpointLabel` before the first
         touch (0: `my CheckpointX` is unset, so it warns once and reads 0), after touching the
         marker (its X), and after a death and respawn (unchanged — nothing clears a per-actor
         variable). Then stop and restart the scene: the fresh actor has no `CheckpointX`, so the
         label reads 0 again and the marker is the only respawn point left. While touching the
         marker, also watch the console: "checkpoint saved" prints **once**, and walking out of the
         marker and back in prints nothing, because `my CheckpointSaved` stays set until the scene
         restarts.

      One limit this list cannot cover: the stored pose is read by the save file (Task 30), not by
      a word — a bare `respawn` is still optional and unshipped — so with one marker the saved
      pose and the marker agree, while a second checkpoint needs its own `respawn at Checkpoint2`
      line, because `respawn at` resolves an actor name rather than the stored pose.

### Done-when

- A cube jumps onto a crate, lands, cannot re-jump mid-air (proven by the shipped G4 checks), and
  dies/respawns at the last checkpoint.
- A character climbs a ladder to a platform and steps off at the top (four of its mechanics are
  `(editor only)` cases in `SelfTest.scene`, so the check is that scene's run).
- A one-way platform is passable from below and solid from above in the character mode.
- A sheet with `set gravity scale to 0` floats, and `0.5` visibly halves the jump peak.

### Edge cases / parks

- Wall jumps, multi-exit ladders and moving-platform passengers are out of scope, same as Task 7
  G4. **Double jumps are Task 21 Part A** — this task only relies on the shipped airborne gate,
  and a project that never asks for `jump again` keeps single jumps.
- `coyote time` / jump buffering are tuning values, not words; park unless a game needs them.
- `me is on ground` is a condition, not a trigger. A **landing edge does exist**: `when me lands`
  shipped with Task 28 Part C (`Triggers.cs`, beside `when <building> lifts off`), so the older
  claim here that it would be "new work" was stale and is corrected (Task 51 Part E).
  `Platformer.events` still polls, because it picks a pose from the state every tick rather than
  reacting to one edge — a sheet that wants the moment itself should use the trigger.

---

## Task 37 — Diablo-style RPG kit: items, equipment & combat depth `[~]`

Goal: the whole Diablo-like RPG build in one place — bag → equip → loot → spells → typed damage →
resistances, equip slots, XP and affixes. **This task is its single home.** `TASKS.md` Task 8
(Items, Bag & Equipment) and Task 9 Parts E/F (the D2 combat kit and its follow-up queue) were
moved here on 2026-11-23; `TASKS.md` Task 9 keeps only its point-and-click adventure parts.

Depends on Tasks 1–3, plus Task 27 (buffs, cooldowns, aim — shipped) and Task 29 (expressions).
**Mixed** (P1). `Task_FPS.md` and `tasks_RTS.md` consume this kit as content; they do not re-plan
it.

**Where to look:** the combat primitives are §Parts A–F, the D2 loop is §The D2 loop, the phase
plan is §The phase plan (`E1`–`E7`) and the ranked follow-ups are §The follow-up queue
(`F1`–`F12`).

**Old labels map here**, so comments and `Session Log.md` rows still resolve:

| Old reference | Where it is now |
|---|---|
| `TASKS.md` Task 8 Part A/B/C | §The D2 loop (bag, equip, acceptance) |
| `TASKS.md` Task 9 E1–E7 | §The phase plan |
| `TASKS.md` Task 9 F1–F12 | §The follow-up queue |
| `TASKS.md` Task 37 (old cross-links) | §Parts A–F |

**Reviewed 2026-11-23 — four of the six combat parts already shipped, under Task 27 and F2, and
none of them is RTS-scoped.** `Runtime/Buffs.cs`, `Runtime/Cooldowns.cs` and `Runtime/Targeting.cs`
are plain C# over the fake world with no Flax types, and the parser, `FlaxActions` and
`EventRuntime` treat their words like any other word, so a Diablo sheet gets `add buff "burn" to
other for 3`, `put "fireball" on cooldown for 1.2` and `aim "meteor"` today by writing them.
`damage <target> by <n> <type>` landed with F2 the same day. What is left of the combat core is
**F6** (the loot-table lookup) and the one combined play-run that proves the loop; nothing here
should be re-designed from scratch.

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
| action | `damage area around <thing> by <n> [<type>] for radius <r>` | `damage area around Mouse by 30 fire for radius 180` |
| action | `aim "<spell>"` / `cancel aim` | `aim "meteor"` |
| trigger | `when aim picks a target` | `when aim picks a target` |
| action | `spawn item from "<table>" at <thing>` | `spawn item from "Loot" at me` |
| action | `roll "<rarity>" from "<table>"` | `roll "Rare" from "Loot"` |

**Where each row stands today** (word shapes as shipped, checked against `WordRegistry`):

| Rows | State |
|---|---|
| `add buff` · `remove buff` · `when buff "…" starts` / `ends` · `my "…" time left` | **shipped** — Task 27 Part C, `Runtime/Buffs.cs` |
| `put "…" on cooldown for <s>` · `my "…" is ready` · `my "…" cooldown` | **shipped** — Task 27 Part E, `Runtime/Cooldowns.cs` |
| `aim` · `cancel aim` · `when aim picks a target` · `damage area around …` | **shipped** — Task 27 Part A, `Runtime/Targeting.cs` |
| `damage <target> by <n> <type>` | **shipped** — F2 (§The follow-up queue), `Runtime/DamageTypes.cs`; the same `<type>` also works on `damage area` |
| `spawn item from "<table>"` · `roll "<rarity>" from "<table>"` | **missing** — the generator exists behind `spawn <thing> … rarity "<name>" level <n>`; the table word is F6 |

### Examples

```text
# ---- burning damage over time ----
# attacker's sheet: hit first, then hand the burn to the victim
when message "fire" arrives
  damage other by 10 fire
  add buff "burn" to other for 3

# victim's sheet: `when buff "burn" starts` is pushed to the *buffed* actor (and
# `my "burn" time left` is read there too), so the ticking block belongs here, not
# on the attacker — on the attacker's sheet it would burn the attacker
when buff "burn" starts
  set my BurnTicks to 3

when every 1 seconds
  my BurnTicks is higher than 0
  damage me by 5 fire
  add -1 to my BurnTicks

# ---- a spell with mana and a cooldown ----
# caster's sheet; `my "fireball" is ready` and `my "fireball" cooldown` are Task 27 Part E
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
# victim's sheet. This is the events-only fallback from E4 — **retired**: Part C / F2 made
# `damage … by <n> fire` subtract `my FireResist` itself, so the block is no longer needed
when message "fire" arrives
  set my LastDmg to other's damage
  set my LastDmg to my LastDmg - my FireResist
  add -my LastDmg to my Health
```

### Parts

- [x] **Part A — Buff/debuff core.** Name, duration, stack rule (`refresh` / `stack` / `ignore`),
      modifiers, expiry, and base-value capture so overlapping buffs restore correctly. Pure
      `Runtime/Buffs.cs`, tested with the FakeWorld.
      *Landed as Task 27 Part C.* `BuffRule` (`Refresh` default / `Stack` / `Ignore`),
      `BuffInstance` (name, duration, remaining, rule, source, modifiers), `BuffModifier`
      (`Stat` + amount or multiplier), one `Tick(dt)` in `EventRuntime.Tick`, base capture through
      `my Base<Stat>` with the aggregate written back and the base restored when the last buff on
      that stat ends, `Clear` on death and `Reset` on scene start. Words as listed above, plus
      `multiplying my <stat> by 1.5` / `adding <value> to my <stat>` clauses. 33 checks in
      `Tests/Program.cs`; no new work is needed for the RPG — a Diablo sheet and a Marine sheet use
      the same manager. Still parked, exactly as written below: buffs on buildings and research
      speed modifiers.
- [x] **Part B — Cooldowns.** Per-actor named timers, one tick loop, `ready`/`time left` reads, clear
      on death, and no polling trigger per unit.
      *Landed as Task 27 Part E.* `Runtime/Cooldowns.cs` keys seconds-remaining by `(actor, name)`
      (case-insensitive), `Start` refreshes, `Tick(dt)` walks the table once with the same one-second
      hitch clamp the buffs use, `Ready` is `Remaining <= 0`, and `ForgetSubject` clears an actor's
      timers on destroy. 29 checks. **One correction to the part as written:** the words and the
      table shipped, the *unification* did not — `SpellClock` (`cast`) and `WeaponClock` (`reload`)
      still run beside it, and there is no `when "<spell>" is ready` edge, only the condition. That
      is **F5** (§The follow-up queue), still open, and it should be done there rather than reopened
      here.
- [x] **Part C — Damage types and resistances.** A damage message/action carries a type; the victim
      subtracts the matching resistance; immunity is a large resistance, not a new word.
      *Landed as Task 9 F2 (2026-11-23).* `damage <target> by <n> [<type>]` is the new word, and
      `damage area around <spot> by <n> [<type>] [for radius <r>]` takes the same type, so splash no
      longer bypasses a resistance the direct hit respects. `Runtime/DamageTypes.cs` (pure) maps a
      type to `<Word>Resist`, takes that percentage off (clamped 0..100, so `my FireResist 999` is
      immunity) and spends the rest through `HealthManager.Damage` — armor, shield, health — while the
      `"damage"` message still reaches the victim for its own effects. A type the victim does not
      resist deals its full number (`steel` against `my FireResist 50`). That settles the four
      decisions this part carried: (1) one word serves every element, so E4 needs no per-element
      `send message` block per weapon; (2) the type never has to reach the victim's sheet — the engine
      spends it — so there is no new value word; (3) F2's engine-side subtraction supersedes E4's
      three sheet lines, which are retired rather than written beside it; (4) `damage area` gained the
      same type qualifier in the same change. But both words push a `"damage"` message *and* apply the
      number, so a victim sheet must not subtract `my DamageTaken` / `other's damage` a second time;
      and neither word consults the matchup table (the shipped AoE never did either) — a sheet that
      wants that gate writes `me can attack`. Crit/leech/pierce (**F12**, §The follow-up queue)
      remain optional future consumers.
- [x] **Part D — Area damage and target-pick.** `damage area around ...` sends real damage through
      the existing armor/shield/health path with a shared type; `aim` enters a pick mode with
      range/validity/timeout, a marker and a cancel.
      *Landed as Task 27 Part A:* `aim "<ability>" [at a point|a unit|me] [for <n> seconds]` (default
      8 s, one aim at a time, a second `aim` replaces the first), `cancel aim`, the trigger
      `when my aim picks a target` pushed to the caster's sheet with the ability in `Name` and the
      unit in `Other`, the values `my aim x` / `my aim z` / `my aim target` and the condition
      `my aim is a unit`. Reach is `my attack range`, unit picks also pass fog visibility and the
      matchup gate, and the caster is always a legal self pick. `damage area around <spot> by <n>
      [for radius <r>]` (radius 150 default) sends real `"damage"` from the caster through
      armor → shield → health, with F2’s `<type>` qualifier (there is no matchup gate; Part C
      explains why) — no caster destruction, no effect of its own,
      and bullets are skipped so splash never chains. Clicks/Escape/right-click and the optional
      `AimMarker` scene actor are the bridge half.       34 unit checks in `Tests/Program.cs`; the editor
      play-check is pending — Task 51 Part E: no case, because a clicked aim needs a scene and a
      cursor value (Task 39 Part B); the rules are the 34 unit checks. The three table rows above (`damage area`, `aim`/`cancel aim`,
      `when aim picks a target`) all ship — except that a spot written as `Mouse` in the example
      resolves only if a scene actor is named `Mouse`; a cursor-position value is Task 39 Part B.
- [ ] **Part E — Loot tables and affixes.** Weighted table data in `.events`; `spawn item from` and
      `roll` read it; the affix generator (Task 9 E7) composes item stats from base + N affixes.
      *Half landed:* the generator shipped as Task 9 E7 — `ItemGenerator.Roll` (base + rarity + item
      level, 1–4 affixes by rarity, values rolling in range and growing 5% per level, no duplicate
      stats, deterministic under a seed) and the spawn word is already qualifier-shaped:
      `spawn Ring at me rarity "Rare" level 12` writes `my FireResist` / `my ItemName` / `my ItemLevel`
      on the clone before its sheet attaches, and `equip` / `unequip` add and subtract exactly the
      recorded numbers (F1). **What is left is the table lookup only** (= **F6** in §The follow-up
      queue): a weighted `Loot.events`, `spawn item from "Loot" at <spot>`, `roll "<rarity>" from
      "Loot"`, and an `EnemyTypes` `Level` so a death passes its own level. F6 must call
      `ItemGenerator.Roll` rather than reimplement the roll, and should reuse the shared table loader
      (**F10**, §The follow-up queue) instead of adding a seventh copy.
- [ ] **Part F — Tests and example.** Stack/refresh/ignore, expiry restores base, cooldown readiness,
      fire vs fire-resist, AoE hits exactly once, aim cancel/timeout, loot table distribution and
      an equipped +5 sword still working.
      *Most of this list is already green from the parts that shipped:* buff rules and base restore
      (33 checks), cooldown arithmetic and both polarities (29), aim reach/validity/timeout/cancel and
      the AoE parse (34), item roll/apply/remove exactness and hand-authored `+5` composing (24 + 12).
      Genuinely uncovered, and all of it blocked on Part C or E: typed damage against a resistance,
      AoE carrying a type, loot-table distribution, and one **combined RPG run** — a single sheet
      that fireballs a `FireResist 50` zombie, applies a burn whose tick lands through the resist,
      refuses a second cast while cooling down, and drops a rolled ring the player equips. That sheet
      is content and belongs in `GameFiles/` with a headless play in `Tests`, the way `Marine.events`
      and `Bag.events` are pinned today. **Typed damage and typed splash are now green too**
      (**F2**, 12 checks: fire vs `FireResist 50`, `steel` ignoring it, each splash victim meeting
      its own resist, `999` as immunity), so what Part F still owes is the loot-table distribution
      (F6) and that one combined sheet.

### The D2 loop: bag, equip, acceptance (moved from `TASKS.md` Task 8)

Goal: the Diablo-style loop — loot drops, pickup, equip, stats change — entirely in the event
language. Depends on Tasks 1–3 plus enemy examples for drop sources.
DESIGN.md Section 5 "Items & the bag (the D2 loop)" defines everything.

### D2 Part A — The bag (formerly `Task 8` Part A)
- [x] Word registry + runtime: `add <thing> to my bag`, `remove <thing> from my bag`,
  condition `my bag has <thing>`; the bag holds things **by name and count**. Possessive reads
  (`other's damage`, `Player's gold`) land with this batch in the variable store.
- [x] Verify: pick up two Swords and a Potion by touching them; `set text of BagLabel to my
  bag` shows `Sword x2, Potion` — text bag only, grid UI parked.

### D2 Part B — Equip & item events (formerly `Task 8` Part B)
- [x] Actions `equip <thing>`, `drop <thing>`; triggers `when me gets equipped` /
  `when me gets unequipped` / `when me gets dropped` on the item's sheet.
- [x] Context rule implemented and tested: equip/equip/unequip moments run the item's events
  with `me` = the **wearer** (stat changes land on the owner); world events outside equip run
  with `me` = the item.
- [x] Unit test: equip a +5 sword → wearer's damage +5; unequip → back to baseline; stacking
  two equips works; unequip drops stats exactly once.

### D2 Part C — Acceptance: the D2 loop (formerly `Task 8` Part C)
- [x] Write the DESIGN.md "Items & the bag" listing as real files (Sword.events, Player pickup/
  equip/attack events, Enemy pressing on `other's damage`).
- [x] Chain it with the loot words that already exist: enemy death → `chance 30%` →
  `spawn Sword at me`.
- [x] Verify the full loop: kill an enemy → sword drops → walk over it → bag grows → press 1
  to equip → next mouse swing costs the enemy visibly more health (damage text or health read).
- [x] Note any vocabulary friction in a DESIGN.md §9 line.

### The phase plan: D2 combat kit, E1–E7 (moved from `TASKS.md` Task 9 Part E)

Goal: grow the finished D2 loop (bag → equip → loot) into combat: melee weapon types, mana and
spells, damage types and resistances, equip slots, XP/levels, and finally item affixes. Phases are
independent — a session can stop after any of them.

| Phase | Content | Cost |
|---|---|---|
| E1 | Melee weapon bundle (Sword/Axe) | events-only |
| E2 | Mana, potions, mana HUD | events-only |
| E3 | `SpellData` table + `cast` word | C# (one action word) |
| E4 | Damage types + resistances | events-only |
| E5 | Equip slots + attributes | events-only |
| E6 | XP / level curve | events-only |
| E7 | Item rarity / affixes | C# (generator) |

**E1 — Melee weapon bundle (events-only)**
`WeaponData` already carries the fields a melee weapon needs; the only missing notion is "this
weapon does not spawn a bullet", so an **empty `Bullet`** is the marker. There is no `Reach` or
`SwingRate` field — reuse `FireRate` for swing timing and keep reach as a literal (see the trap
below).

- [ ] `WeaponData.events`: `Sword` (`SwordBullet ""`, `SwordDamage 12`, `SwordFireRate 0.6`,
      `SwordKnockback 60`, `SwordAmmoPerShot 0`) and `Axe` (`Damage 20`, `FireRate 1.1`).
- [ ] Player attack branch, no projectile:
```
when Left Mouse is pressed
  weapon is Sword
  unless reloading
  me is near Enemy by 6
  send message "damage" to nearest Enemy
  play sound "swing"
```
- [ ] `Sword.events` (item actor) keeps the equip/unequip stat push (already in `test/Sword.events`).
- [ ] **Range trap:** a `near` **condition** only accepts a literal distance —
      `by my Reach` parses but leaves the distance at 0 (`Conditions.cs:516` requires a number
      token). Use a literal per weapon, or branch one block per melee weapon.
- [ ] Done-when: the sword's swing does nothing outside 6 units, the HUD reads `Weapon: Sword`,
      and swapping to the Pistol restores ranged fire.
- [ ] Naming note: the item actor `Sword` and weapon data `Sword` are different namespaces and both
      work, but prefer `Short Sword` for the data name to avoid confusion.

**E2 — Mana and potions (events-only)**
- [ ] `my Mana` / `my MaxMana`; regen `when every 1 seconds → my Mana is lower than my MaxMana →
      add 1 to my Mana`.
- [ ] Mana potion: item actor named `ManaPotion`, `remove ManaPotion from my bag` + `add 40 to my Mana`,
      clamped with an `if` line (the language has no `min()`).
- [ ] **Bag-name trap:** `add`/`remove`/`my bag has` take a **single word** — `remove Mana Potion
      from my bag` and `remove "Mana Potion" …` both fail to parse ("`remove` looks like this"),
      so bag items must be one token (`ManaPotion`, `HealthPotion`, `TownScroll`).
- [ ] HUD: `bind ManaLabel to my Mana and my MaxMana format "Mana: {0}/{1}"`.
- [ ] Done-when: a potion fills to the cap and never above it; mana regenerates while below the cap.

**E3 — Spell data table + `cast` (C#, one word)** `[x]`
Same shape as `WeaponData`: `<Spell><Field>` globals in a new `SpellData.events`
(`Mana, Cooldown, Damage, Blast, Element, Bullet, Speed, Life, Sound`), plus one action word.

- [x] `SpellData.events`: `Fireball` (Mana 10, Cooldown 1.2, Damage 20, Blast 180, Element fire,
      Bullet Fireball) and `Heal` (Mana 15, no projectile).
- [x] Words `cast "<spell>"` / `cast "<spell>" at <thing>` + registry entries + keywords.html rows.
- [x] Runtime: refuse when `my Mana` is short or the cooldown is live; otherwise spend, then spawn
      the spell's bullet or run its non-projectile branch.
- [x] Cooldown state per spell (`my <Spell>Cooldown`), mirroring the reload timer.
- [x] Unit tests: mana refusal, cooldown refusal, one cast spends once, unknown spell warns once.
- [ ] Done-when: two spells with different mana/cooldowns fire from the same player sheet.
      **Editor run pending** — needs E2's `my Mana` block on the player sheet and a `Fireball`
      actor in the scene to stage the projectile clones.
      *(Task 51 Part E — no case: it needs authored sheets and an actor in a scene; the spend and
      cooldown rules are unit-covered.)*

**As shipped (2026-11-16):** `Runtime/Spells.cs` — `SpellStats`, `SpellData` (the
`<Spell><Field>` table: `LoadData`, `TrySplitField`, `CanonicalFor`, `KnownNames`, `TryGet`,
built-in-free so nothing exists that data did not author) and `SpellClock` (per **actor + spell**
timers, `Start`/`IsCooling`/`Remaining`/`Clear`). `ActionVerb.Cast` + `ActionSpec.Spell` parse both
shapes (`ActionWords`; a bare `cast Fireball` explains the quotes). `FlaxActions.CastSpell` refuses
quietly while cooling down or short of `my Mana` (a shortage is normal play), spends the mana
clamped at 0 in code (the language has no `min()`), starts the clock, plays the spell's sound, then
**either** clones `Bullet` at the caster/the `at` target — oriented at the target, with
`my damage`/`my blast`/`my BulletSpeed`/`my Life`/`my TimeLeft`/`my Element`/`my Spell` stamped on
it — **or**, for a spell with an empty `Bullet`, sends the message `"cast <Spell>"` to the target
(or the caster) so events decide what happens (`Heal` is the shipped example: no healing code in
C#, just `when message "cast Heal" arrives`). `my <Spell>Cooldown` is a **computed** readout in
`EventContext.ReadMy` (like `my Ammo`), so a HUD binds live without a mirrored timer, and only real
spell names are intercepted — an ordinary `my Cooldown` variable still reads as one. Cooldowns are
cleared when the actor is destroyed (`ForgetSubject`) so a clone never inherits a stale timer.
`SpellData.LoadData` runs at both the first load and the hot reload, so a data edit takes effect
mid-session. Decisions from the review: message-based non-projectile spells, engine clock exposed
as a computed value, element stamped on the projectile (E4 is then content), no range field — the
projectile's `Life`/`Speed` is the range. 25 new checks (parse both shapes + the bare-cast hint,
table load/lookup/case, clock liveness/expiry/clear/per-spell isolation, computed cooldown read,
shipped data file) → **1476/1476 green**; module build 0 errors.

**E4 — Damage types and resistances (events-only)**
- [ ] Messages carry the element: `"damage"`, `"fire"`, `"cold"`, `"poison"`.
- [ ] Victim subtracts resistance in three lines (math is one operation per line):
      `set my LastDmg to other's damage` → `set my LastDmg to my LastDmg - my FireResist` →
      `set my health to my health - my LastDmg`.
- [ ] Immunity = a large resistance (`my FireResist 999`) rather than a new word.
- [ ] Done-when: one fireball hurts a normal zombie and barely scratches a fire-resistant one.

**E5 — Equip slots and attributes (events-only)**
- [ ] `my WeaponSlot` / `my ArmorSlot` / `my RingSlot` strings; `equip` refuses while the slot is
      taken until the old item is `drop`ped (or auto-swap: unequip, then equip).
- [ ] Attributes `my Strength/Dexterity/Vitality/Energy` feeding health/mana/damage with one-op
      formulas.
- [ ] Done-when: two rings cannot both occupy the ring slot; strength above a threshold raises
      melee damage.

**E6 — XP and levels (events-only)**
- [ ] `add <n> to XP` on kill; threshold blocks (`when XP becomes 100 → set my Level to 2`), or the
      parked `XP-threshold helper` word once the block count gets silly.
- [ ] HUD bind plus a level-up message.
- [ ] Done-when: clearing a room levels the player once and thresholds never re-fire.

**E7 — Item rarity and affixes (C#, generator)** `[x]`
- [x] Generator that composes an item's stats from a base plus N random affixes, driven by
      `ItemLevel` and rarity. Items stay hand-authored sheets until this exists.
- [ ] Done-when: a dropped ring rolls 1–3 affixes and its `my` stats reflect them.
      **Editor run pending** — place a `Ring` actor in the scene (the template the clone stages
      from), add a sheet with `spawn Ring at me rarity "Rare" level 12`, and read the clone's
      `my FireResist` / `my ItemName`.
      *(Task 51 Part E — no case: a rolled ring needs a spawned clone in a scene; the generator is
      unit-covered.)*

**As shipped (2026-11-16):** `Runtime/Items.cs` — `ItemBase`/`ItemData` (`<Base>Slot`,
`Damage`, `Armor`, `ItemLevel` from `ItemData.events`), `AffixDef`/`RarityDef`/`AffixData`
(`<Affix>Stat/Min/Max/ItemLevel/Word/Suffix` plus `<Rarity>MinAffixes/MaxAffixes`, built-in
rarity counts Common 0 · Magic 1–2 · Rare 1–3 · Unique 3–4 from one `AffixData.events`),
`ItemStats` (the stat whitelist — an affix whose stat is not a real variable name is skipped with
a warning instead of quietly creating one), `ItemGenerator` (pure: pool filtered by item level,
count clamped to the pool, stats never repeat, values roll inside min..max and grow 5% per level
above the affix's own minimum, `ComposeName` builds “Fiery Ring of the Fox”), and `ItemAffixes`
(per-instance rolls + exactly what each equip added). The word is a **qualifier on `spawn`**, not a
new word: `spawn <thing> at <spot> [offset <n>] [rarity "<name>"] [level <n>]`, in any order, both
optional — absent `rarity` keeps today's behaviour byte-for-byte. On a rolled spawn the clone gets
its stats written as its own `my` variables (`my FireResist`, `my damage`, `my Rarity`,
`my ItemName`, `my ItemLevel`) **before** its sheet attaches, so `when me appears` already sees
them. Equip applies rolls engine-side: the bag now remembers **which actor** each name came from
(`BagAdd(owner, thing, item)`, `TakeBagItem`, so `my bag` still reads `Ring x2`), `equip` takes
that instance and `EquipItem` applies its recorded stats, `UnequipItem` subtracts exactly those
numbers (recorded, never recomputed — no drift). Hand-authored items (`Sword.events` →
`add 5 to my damage`) are untouched and compose with rolled ones; destroyed actors forget both
their roll and their wearer records. The item level defaults to a global `ItemLevel` when the
`level` qualifier is absent. 24 new checks (both spawn shapes + qualifier order + plain spawn
unchanged, base/affix/rarity tables, count clamping, seeded determinism, no duplicate stats, item
level gating in both directions, value growth, display name, apply/remove exactness, hand-authored
item applies nothing, bag instance identity, equip/unequip through the runtime) →
**1476/1476 green**; module build 0 errors (9 pre-existing XML-doc warnings).

**Out of scope:** skill trees, synergies, runewords, set items, charms, crafting, vendors, gambling,
mercenaries.

### The follow-up queue, F1–F12 (moved from `TASKS.md` Task 9 Part F)

Review of E1–E7 (2026-11-16) produced this queue. Order is by value, not by phase: F1–F4 are
correctness and vocabulary holes that make later phases cheaper, F5–F6 are the first slice of
§Parts, F7–F12 are depth. Each item is scoped to be picked up alone; the E-phase it serves is in
brackets.

- [x] **F1 — Fix generated-item stat bookkeeping (C#, small) [E5/E7].** `ItemAffixes.applied` is
      keyed `(wearer, name)` while the equipped set is a `HashSet<string>` of names, so two rare
      rings (both `Ring`) overwrite each other: the first ring's stats stay on the wearer forever
      and are never subtracted. A worn item that is destroyed also keeps its stats. Key the
      record by the **item actor** (and store the name for display), and subtract on destroy.
      Done-when: two different rolled rings equipped in sequence stack and unstack exactly; a worn
      ring destroyed mid-session leaves the wearer's stats at baseline. Pure `Runtime` + tests.
      **As shipped (2026-11-16):** `ItemAffixes` keeps an ordered `WornItem` list keyed by the item
      actor (item, wearer, name, and the stats actually added). `Apply` is idempotent for the same
      wearer and moves an instance between wearers without double-stacking; `Remove(variables,
      wearer, name)` — all `unequip` knows — takes back the **oldest** record with that pair, so
      two rings both called `Ring` unstack one at a time; `Forget(variables, item)` now subtracts
      before dropping the roll, and `FlaxActions.ForgetSubject` passes the variables, so
      destroying a worn item no longer leaves a permanent bonus. New `WearerOf(item)` for
      tests/HUDs.
      12 new checks (two same-named rings stack/unstack through the store *and* through
      bag → `EquipItem` → `UnequipItem`, re-equip is idempotent, removing an unworn ring is a
      no-op, a destroyed worn item returns the wearer to baseline and forgets its roll) →
      **1488/1488 green**; module build 0 errors.
- [x] **F2 — A real damage path: `damage <target> by <n> <type>` (C#, medium) [E4, Part C].**
      `ExecuteSend` requires a literal message name, so a projectile stamped with `my Element`
      cannot pick its message today; E4 written now means one `send message "fire"` block per
      element per weapon. Add the action: resolve the target, subtract the victim's `<Type>Resist`,
      then go through the existing armor/shield/health path (the landed note below drops this line's
      matchup-gate part). Keep `send message`
      for scripted events. Done-when: a fireball hurts an unresisted zombie and barely scratches a
      `FireResist 50` one with no per-element branches in any sheet; the same call with `steel`
      ignores FireResist.
      *Landed 2026-11-23.* `damage <target> by <n> [<type>]` (`ActionVerb.Damage`; target = an actor,
      `other`, `me`, `nearest player`, or the `my aim target` pick), and the same type qualifier on
      `damage area around <spot> by <n> [<type>] [for radius <r>]`. New pure
      `Runtime/DamageTypes.cs` maps a type to its stat (`fire` → `FireResist`, `cold`, `poison`, any
      `<Word>Resist`), takes the victim's percentage off (clamped 0..100, so `my FireResist 999` is
      immunity rather than healing) and hands the rest to `HealthManager.Damage` — armor → shield →
      health, the path `add -<n> to Health` already uses. A type the victim does not resist deals its
      full number (`steel` against `FireResist 50`). Both words also push the `"damage"` message, so a
      victim's effects, i-frames and `other's damage` reads are unchanged; the number is applied once,
      by the engine, so a sheet must not subtract it again. Two honest corrections: `damage area` now
      applies the damage itself instead of trusting the victim's sheet (Marine has no `"damage"`
      handler, so the shipped storm dealt nothing), and **no matchup-table gate was added** — neither
      word consults `Matchups`, and the old claim that AoE enforced it was never true of the message
      path either; a sheet that wants a gate writes `me can attack`. `HighTemplar.events`' storm is
      `30 fire`. 12 new checks (both word shapes carry the type, `my FireResist 50` halves 30 fire, the
      resisted number then spends armor and shield, `steel` ignores `FireResist`, a typed splash spends
      each victim's own resistance, `999` is immunity, the word book row) -> **2591 passed, 0
      failed**; Game module build 0 errors.
- [ ] **F3 — Variables in numeric qualifiers (C#, small) [E1, E4].** `near Enemy by my Reach`
      parses and then reads 0 (`Conditions.cs` wants a number token), which forces one melee block
      per weapon and will bite spell range/cleave/aura sizes. Accept a number, a global or
      `my <name>` everywhere a distance is read, in conditions and triggers. Done-when: a weapon
      sheet with `by my Reach` reaches its authored distance, and `when me is near` by a variable
      re-arms on the right edge.
- [ ] **F4 — Mana bounds in the engine (C#, small) [E2, E3].** Health already clamps through
      `HealthManager.Add` (`0..MaxHealth`, armor/shield first); do the same for `Mana`/
      `MaxMana` so E2's potion and regen do not need a three-line `if` dance (there is no `min()`),
      and `cast`'s manual clamp becomes the same rule instead of a special case. Done-when: a potion
      fills to the cap and never past it; regen stops at `MaxMana`; the E3 cast spends through the
      same path.
- [ ] **F5 — One timer table + a ready edge (C#, medium) [Part B].** `WeaponClock` has
      completion edges, `SpellClock` does not, and §Parts proposes `put "<name>" on cooldown for
      <s>` — a second cooldown system over the one that ships. Generalize into a named-timer table
      keyed `(actor, name)` used by `reload`, spells and future buffs; expose
      `when "<spell>" is ready` and keep `my "<spell>" cooldown` as the readout. Done-when: a HUD
      icon flips on the ready edge; `reload` and `cast` share one storage; an existing sheet's
      behaviour is unchanged.
- [ ] **F6 — Loot tables (C# + data, medium) [E7, Part E].** A weighted table file
      (`Loot.events`) resolved by `spawn item from "Loot" at <spot>` (and `roll "<rarity>" from
      "Loot"`), choosing base + rarity + item level and then calling `ItemGenerator.Roll` — never
      reimplementing the roll. `EnemyTypes` gains a `Level` stat so a death can pass its own level.
      Done-when: a level-12 zombie drops a level-appropriate ring from a table, no drop numbers in
      any enemy sheet, and `Foxy` (level 10+) actually appears in play.
- [ ] **F7 — Cast feedback + HUD check (C#, small) [E3].** `cast` refuses silently on mana or
      cooldown (right for play, wrong for feedback). Push `when cast fails "<spell>"` (or a
      `"cast failed <Spell>"` message) so a sheet can flash a bar. Verify `bind <label> to my
      <Spell>Cooldown` — the expanded `when <var> changes` watch re-reads the computed value every
      tick, so it may work but churn the label 60×/s. Done-when: a failed cast triggers feedback;
      the cooldown label updates without per-frame churn.
- [ ] **F8 — Extract the cast decision (C#, small) [E3].** Move "unknown / cooling / short mana /
      spend + start" out of `FlaxActions.CastSpell` into a pure `Runtime/SpellCasting.Decide(...)`
      returning a verdict, matching how `Steering`/`PhysicsMovement`/`Separation` keep rules out of
      the bridge. Done-when: mana refusal, cooldown refusal and one-spend-per-cast are unit tests
      rather than a doc paragraph, and the bridge only executes the verdict.
- [ ] **F9 — Equip slots from `ItemData.Slot` (C#, small) [E5].** `ItemData.events` already ships
      `RingSlot`/`AmuletSlot`/`SwordSlot` and nothing reads them, so two rings both "work". Gate
      `equip` on the slot variable and either refuse or auto-unequip the old item. Done-when: the
      second ring is refused (or swaps the first, with its stats fully removed) and a sword cannot
      sit in the ring slot.
- [x] **F10 — One `<Owner><Field>` table loader (C#, medium refactor) [all data systems].**
      `WeaponData`, `SpellData`, `ItemData`, `AffixData`, `EnemyTypes` and `Matchups` are six copies
      of the same split-literal-read logic; the next tables (Abilities, Buffs, Loot) would be
      copies seven and eight. Extract a small generic table (field list, `LoadData`,
      `CanonicalFor`, `KnownNames`, layered globals-over-data) with no behavior change. Done-when:
      the six tables are thin classes over the helper, the suite is still green, and a new data
      table is ~10 lines.
      *As shipped (2026-11-23):* new engine-free `Runtime/DataTable.cs` — `DataTable` (a file name,
      the field list in matching order, `LoadData` over the literal `set` lines, `TrySplit`,
      `CanonicalRow`/`CanonicalFor`, `HasRow`/`HasField`, `TryRead` (live global → data file),
      `MergeWith`/`SortedRowNames`/`KnownNames`) plus `DataFile.ForEachLiteral`/`TryLiteral`, the one
      walk over a data file's `set <name> to <literal>` lines. `WeaponData`, `SpellData`, `ItemData`,
      `AffixData` and `EnemyTypes` are now a `DataTable` beside their built-in defaults and their
      field-to-stat switch; `Matchups` keeps its pair key and reuses only the literal walk. Fields
      still match longest-first in the order a table passes them, so `AffixData`'s `MinAffixes`
      before `Min` is unchanged, and a live `<Row><Field>` global still wins over the file. No
      behaviour change: **2613 passed, 0 failed**; `dotnet build Source/Game.csproj -c
      Editor.Windows.Debug` 0 errors (14 pre-existing doc warnings). A seventh table is a field
      array, a `DataTable` and the switch that writes the stats.
- [ ] **F11 — Item identity the player can see (C# + data, medium) [E7, Task 34].** The bag tracks
      which actor an item came from, but sheets still read `Ring x2` and `equip Ring` always takes
      the first carried instance. Show instance names (`my bag items` / display names from
      `my ItemName`), and allow choosing (`equip Ring 2`). Done-when: two different rings are
      distinguishable and the intended one is equipped. The grid UI itself stays with Task 34.
- [ ] **F12 — Data depth that costs no code (data only, small) [E4/E5/E7].** `AllResist`,
      `FirePierce`, `CritChance`, `LifeLeech` are new names in `ItemStats.Names` plus affix rows;
      base-specific affix pools (a ring should not roll weapon damage) via an `<Affix>Bases` field;
      a proper tier ladder instead of one hand-placed late affix. Code for crit/leech/pierce
      actually **spending** those stats belongs with F2. Done-when: the pool reflects the base and
      slot, and every new stat is read by a real consumer.

**Blocked / later (do not start from this list):** damage numbers and hit feedback (needs F2);
crit/leech/penetration behavior (needs F2); save/load of rolled items — `ItemAffixes` is in-memory
and actor-keyed while `save stats` stores globals (belongs with the save/load task); cast time /
channel / interrupt, spell schools and spellbooks (nothing in the kit needs them yet); ground-
targeted spells (`aim`), which need a point value first.

**Where E3/E7 meet the combat primitives (decided 2026-11-16):** §Parts is the *primitives*
(`put "x" on cooldown`, `damage <target> by <n> <type>`, `spawn item from "<table>"`); E3/E7
shipped the *composites*. Do not fold `cast` backwards — it is the right authoring shape and can
become a thin composition later with no `.events` file changing. Instead: F5 gives Part B its
storage, F2 gave Part C its word, F6 gives Part E its generator, and Part A (buffs) reuses the
"apply exactly / remove exactly" bookkeeping from `ItemAffixes` (F1 is the prerequisite).

**Reviewed 2026-11-23, F2 landed:** Parts A, B and D shipped under Task 27 (C, E, A) and F2
shipped the same day, so **F6 and the one combined play-run are what remain** — do not reopen
this as a fresh design. `damage area` took the same `<type>` as the direct hit, and F2’s
engine-side `<Type>Resist` subtraction superseded E4’s three sheet lines instead of living
beside them.

### Done-when

- A fireball spends mana, starts a cooldown, is refused while cooling down, and damages only valid
  targets in its radius. **Met** by Task 27 A/E + `cast` (`SpellClock`); the only gap is that the
  refusal is silent (**F7**) and that `cast`'s clock is not yet the shared table (F5).
- A fire-resistant zombie takes little damage from fire and normal damage from steel. **Met in the
  engine** by **F2** — `damage <target> by <n> <type>` reads the victim's `<Type>Resist` — but no
  zombie sheet wears the resist yet: that is the combined RPG run in Part F.
- A burn buff ticks, expires, and restores the pre-buff stats without drift. **Met** by Task 27
  Part C; the tick itself is the sheet's (`when buff "burn" starts` + a `when every` block on the
  victim), and `my "burn" time left` is there if the sheet prefers to gate on the timer.
- A dropped rare ring rolls 1–3 affixes and its `my` stats reflect them. **Met** by E7/F1 (§The
  phase plan) through `spawn Ring … rarity "Rare" level <n>`; only the table-driven drop is missing
  (Part E / F6).

### Edge cases / parks

- Skill trees, synergies, runewords, set items, crafting, vendors and gamblers stay parked
  (E out-of-scope in §The phase plan).
- Buffs on buildings, research speed modifiers and stacking rules beyond refresh/stack/ignore are
  later decisions. (Task 27 Part C shipped refresh/stack/ignore and no building path, so this is
  unchanged.)
- A second buff system must not be built. If a future RPG need looks unmet, check `Runtime/Buffs.cs`,
  `Runtime/Cooldowns.cs` and `Runtime/Targeting.cs` first — the cost of a duplicate manager here is
  an `.events` file that means two things on two genres.
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
| action | `lock cursor` / `unlock cursor` / `toggle cursor lock` | first-person/aiming |
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

## Task 48 — Animated Game Cursors `[x]`

Goal: a game cannot choose its cursor today. Nothing in the project touches the pointer, so every
scene shows the platform arrow, and neither an RTS's animated `Attack`/`Move` cursor nor a
point-and-click title's `Hand`/`Examine` cursor is possible from `.events`. A cursor is also the one
piece of feedback a player sees every frame, so it belongs to the vocabulary, not to a per-game C#
script.

Depends on Tasks 3 and 15 (UI). **Flax bridge + runtime table** (P1).

### What Flax gives us (verified against the 1.13 docs)

- `Screen.CursorVisible` hides/shows the pointer and `Screen.CursorLock` takes `None`, `Locked` or
  `Clipped` — `Clipped` is the right mode for an RTS or any mouse-heavy game.
- `CursorType` carries `Default`, `Hand`, `Cross`, `No`, `IBeam`, `SizeNS`, `SizeAll`, `Hidden` and
  `Image`; `CursorType.Image` is loaded through `Window.LoadCursorImage`.
- There is **no animated OS cursor**: one static image at a time, no frame stepping, no hotspot math
  beyond the image's own offset. Animated cursors therefore have to be drawn by the game — hide the
  OS pointer and move a UI `Image` under the mouse, which is what RTS games do anyway and what makes
  tinting, scaling and hotspot changes free.

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `set cursor "<name>"` | `set cursor "Attack"` |
| action | `set cursor to default` | `set cursor to default` |
| action | `hide cursor` / `show cursor` | `hide cursor` |
| action | `push cursor "<name>"` / `pop cursor` | a mode that must not leak |
| value | `my cursor` | `when my cursor is "Attack"` |
| trigger | `when cursor changes` | one block per state change |

### Examples

```text
# ---- an RTS targeting mode owns the pointer, and gives it back ----
when Q is pressed
  push cursor "Attack"
  aim attack

when aim is done
  pop cursor

# ---- point and click: hover a hotspot, change the pointer ----
when me is hovered
  set cursor "Hand"

when me loses hover
  set cursor to default
```

### Parts

- [x] **Part A — The pure cursor model.** `Runtime/Cursors.cs`: `CursorDef` (`Name`, `Frames[]`,
      `Fps`, `HotspotX/Y`, `OsFallback`), `CursorLibrary` (name → def, case-insensitive, `"default"`
      always present), `CursorStack` (`Push`/`Pop`/`Peek`, popping past the bottom keeps the
      default), and `CursorState.For(library, stack, hover, aiming)` as one resolved answer per tick
      — so a scene cannot leave the pointer on the combat cursor.
- [x] **Part B — The bridge.** `Bridge/CursorView.cs`: when the resolved cursor has frames, hide the
      OS pointer (`Screen.CursorVisible = false`) and move a scene-authored `UIControl` whose
      control is an `Image` to `Input.MousePosition`, minus the declared hotspot; step frames by
      `Time.GameTime * Fps` so animation needs no coroutine and no tick loop of its own. When it has
      no art, restore the OS pointer and set `CursorType.Default/Hand/Cross/No/Hidden` (through
      `Window.LoadCursorImage` for `CursorType.Image`). Nothing in the bridge decides *when* a cursor
      is right; it only draws what the runtime resolved.
- [x] **Part C — Cursor art stays content.** A cursor is a scene actor named `Cursor_<Name>` with an
      `Image` control (one frame) or `Cursor_<Name>` plus child frames the view toggles. Frames,
      fps, hotspot and OS fallback come from the actor's own variables (`set my Cursor fps to 12`,
      `set my Cursor hotspot to 4, 4`), the same way `LiftHeight` already carries a per-actor number.
      No art pipeline, no asset importer, no new content format.
- [x] **Part D — Automatic sources, all optional.** `aim` pushes the targeting cursor and pops on
      end; hovering a `selectable` actor answers `Hand`; hovering a hostile actor under the fog rules
      answers `Attack`; hovering a `BagItem`/hotspot answers `Hand`; dragging a marquee answers
      `Cross`. The mapping is a table in `Runtime/Cursors.cs` (`CursorRules`), edited there or
      overridden by a sheet, never a hardcoded branch per game.
- [x] **Part E — Tests and docs.** Pure checks: push/pop order and the never-empty bottom, name
      resolution and case, `Fps` frame selection including `0` (static), hotspot subtraction,
      `CursorRules` for hover/hostile/aiming/dragging, and that a resolved cursor with no art always
      answers an OS fallback instead of hiding the pointer with nothing to replace it. `keywords.html`
      rows, `WordRegistry` rows, `DESIGN.md` §9 note on the cursor stack.

### Done-when

- A sheet can set, push, pop and hide the cursor with no C# in the game project.
- An animated cursor runs at its declared fps while the game holds 60 fps, and the OS pointer is
  hidden only while drawn art is active.
- Leaving an aiming mode restores the default cursor even if the mode's own sheet forgot to pop.
- A point-and-click scene that never mentions an RTS word gets hover cursors; an RTS scene shows
  the targeting cursor while aiming without a single new block in its own sheet.

---

## Task 49 — Mouse Button Action Mapping `[ ]`

Goal: yes, it is hardcoded. `EventsManager.InputEdges` answers the buttons by name:
`MouseButton.Left` is click + selection + the marquee drag, `MouseButton.Right` is `RightClicked` plus
`IssueDefaultOrder`, `MouseButton.Middle` only broadcasts `MiddleClicked` and does nothing itself, and
Shift is read directly (`Input.GetKey(KeyboardKeys.Shift)`) as "append to selection / queue the
order". The drag threshold, the double-click window and the decision that only the left button starts
a marquee are literals in the same file. A game that wants middle-mouse select and left-mouse move
cannot ask for it; the RTS layout is the only layout.

Depends on Tasks 3, 16 and 29. **Runtime table + bridge** (P1).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `bind left mouse to <action>` | `bind left mouse to move` |
| action | `bind middle mouse to <action>` | `bind middle mouse to select` |
| action | `bind right mouse to <action>` | `bind right mouse to select` |
| action | `unbind middle mouse` | back to no action |
| action | `set mouse append key to <key>` | `set mouse append key to Ctrl` |
| value | `my mouse action` | which action a button holds |

Actions: `select` (click-select, with the existing marquee drag), `move` (the Part C default order),
`attack-move`, `pan` (drag the camera, middle-drag in most editors), `menu`, `none`. A button has one
action; assigning a second replaces the first with a warning, so a scene never silently gets two.

### Examples

```text
# ---- the RTS default, written out (this is what every existing scene gets anyway) ----
bind left mouse to select
bind right mouse to move
bind middle mouse to none

# ---- a point-and-click adventure: left is the only verb ----
bind left mouse to move
bind right mouse to none

# ---- the user's ask: middle selects, left moves ----
bind middle mouse to select
bind left mouse to move
```

### Parts

- [ ] **Part A — The pure table.** `Runtime/MouseMap.cs`: `MouseAction` (`Select`, `Move`,
      `AttackMove`, `Pan`, `Menu`, `None`) and `MouseMap` (`ActionFor(MouseButton)`, `Bind`,
      `Unbind`, `Defaults()`). `Defaults()` reproduces the current behaviour exactly — left select,
      right move, middle none, Shift append, 4 px drag, the existing double-click window — so a scene
      that never binds anything is unchanged, which is this task's first acceptance test.
- [ ] **Part B — The bridge reads the table, not literals.** `EventsManager.InputEdges` asks
      `MouseMap.ActionFor(button)` and dispatches; the marquee starts only for the button whose
      action is `Select`, the order only for `Move`/`AttackMove`, `Pan` drives `UpdateCameraPan`'s
      drag path, and `Menu` opens whatever the sheet listens for. `HandleMouseClick` keeps its
      `TriggerKind` vocabulary, so `when <actor> is clicked` and the UI button path do not change
      underneath the feature.
- [ ] **Part C — Modifiers and timing are data.** `set mouse append key to Shift` (the selection-add
      and order-queue modifier), a readable double-click window and drag threshold, and a
      `when <button> is double-clicked` that comes from the same table instead of a constant. Two
      buttons may share one action (middle + left select) without fighting over the drag.
- [ ] **Part D — Raw button triggers.** `when left mouse is pressed` / `is released` (and middle /
      right), which today only exist for actors and UI controls; guards against the `Left`/`Right`
      key names that already occupy those words, and `my mouse action` as the read-only companion so
      a HUD or tutorial can name the button the player is expected to press.
- [ ] **Part E — Tests and docs.** Pure checks: `Defaults()` matching today's behaviour, bind and
      rebind, `Unbind` to `None`, two buttons on one action, `ActionFor` for an unbound button,
      the modifier read and the timing values being read rather than constant. A parsed-sheets check
      that the RTS test scene and the shipped examples still parse with no `bind … mouse` line, and
      `keywords.html` / `WordRegistry` rows plus a `DESIGN.md` §9 decision entry.

### Done-when

- A game can move selection to the middle button and the order to the left one with two lines, with
  no C# in the game project and no change to any `.events` word's shape.
- A scene that binds nothing behaves exactly as it does today.
- The marquee, the ordering and the UI button path keep working when the buttons move.
- `world.IsMouseButtonDown`-style values and the actor/UI click triggers stay in one vocabulary, so a
  tutorial can say "left mouse" and mean the button the player actually has bound.

---

## Task 50 — Dialogue Files & Conversation Trees `[ ]`

Goal: Task 35 gave dialogue its presentation; it still has to be *written as event code*. A
conversation is prose and a graph, not a sequence of clicks: a writer adding a node should not
scroll past `when` blocks, a translator should be able to swap a file, and a ten-node conversation
should not become ten events whose shape only the programmer remembers. This task gives dialogue a
file of its own (`.dialog`), compiled into exactly the nodes Task 35 already runs, and two words —
`talk to "<name>"` and `when dialogue ends`. The words stay in `.events`; the dialogue file holds
no event words at all, only a closed set of presentation tags, so the parser, the runtime and the
keyword audit keep one vocabulary.

Depends on Tasks 35 (dialogue nodes) and 34 (backlog rows). **File format + runtime** (P1).

### What lives where

- `.events` — the words: who talks, when, what the game does about it.
- `.dialog` — the words *spoken*: nodes, lines, choices, jumps and presentation tags.
- Event words never appear in `.dialog`, and dialogue tags never appear in `.events`. Recorded here
  so it is not re-litigated later: a file that can contain arbitrary words is a second language, and
  the whole vocabulary's audit (`WordRegistry`, `keywords.html`, the parse-the-examples test) stops
  covering it.

### The file

`Source/Game/GameFiles/*.dialog` — a `Dialogue/` subfolder is fine, the loader searches recursively
exactly like `.events`. One conversation per file; the file's name is the name `talk to` takes.
Hot reload works the same way as `.events`: saving mid-play swaps the tree when it is valid, and a
broken file keeps the old tree with one friendly error per problem.

### Grammar

| Syntax | Meaning |
|---|---|
| `# comment` | ignored to the end of the line |
| `== <node> ==` | starts a node; `[A-Za-z0-9 _-]`, unique ignoring case; **the first node in the file is the entry node** |
| text on its own line | one spoken line: types into the label at the node's speed, then joins the backlog |
| `@label <thing>` | the label this node types into (default `DialogLabel`), reset per node |
| `@speed <n>` | characters per second for this node (default 40, `0` = instant), reset per node |
| `@speaker <name>` | the speaker from here on (`set speaker to`); `@speaker` alone clears it |
| `@portrait <thing> <sprite>` | the portrait from here on (`set portrait of`); `@portrait` alone clears it |
| `@flag <name>` | sets `my <name>` true when the node is entered |
| `> <text> -> <target>` | a choice: a numbered button; picking it jumps to `<target>` |
| `-> <target>` | jump once the node's last line has finished; `end` closes the dialogue |

`@speaker` and `@portrait` carry across nodes until changed or cleared (a conversation usually has
one narrator), while `@label` and `@speed` are per node — scene wiring and pacing belong to the node,
not to the rest of the file. Tags apply to the lines below them; there is no inline form.

Every spoken line joins the backlog as `"<speaker>: <text>"`, or the bare text when the node has no
speaker — so a conversation written in a file is replayable for free, the same way the Part D shipper
is.

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `talk to "<name>"` | `talk to "Innkeeper"` |
| trigger | `when dialogue ends` | `when dialogue ends` |

`talk to "<name>"` resolves the file the way `.events` and asset names resolve (folder, extension and
case ignored, near-miss suggestion when nothing matches `Innkeeper.dialog`). The conversation runs on
the sheet that wrote the line; a second `talk to` from the same sheet replaces the running one, the
same way a second `type text` replaces a line. On a missing file it warns once, naming the near-miss
and the folder to put the file in, and does nothing else.

`when dialogue ends` is broadcast to the sheet that ran `talk to`, exactly once, when the tree closes
(`-> end`) or a node runs out of lines, choices and jumps. A sheet that was removed mid-conversation
simply hears nothing.

### How a node compiles

A `.dialog` file never reaches the runtime as text: the loader compiles each node into a short list of
the Task 35 nodes, so the presentation words keep running the conversation they already run.

| `.dialog` | Compiled node |
|---|---|
| `@flag <name>` (node entry) | `SetNode` — `my <name>` to true |
| `@speaker <name>` (when it changed) | `SetSpeakerNode` |
| `@portrait <thing> <sprite>` (when it changed) | `SetPortraitNode` |
| a spoken line | `AddBacklogNode` for the history line, then `TypeTextNode` into the label at the speed |
| `> <text> -> <target>` | one `ShowChoiceNode` per choice, shown **after** the last line finishes |
| `-> <target>` | a runner jump |
| `-> end` | `ClearDialogueNode`, then `when dialogue ends` |

The runner (`Runtime/DialogueRunner.cs`) is a cursor over the compiled nodes, pushed forward by Task
35's finish path — never by `wait` — so a speed change and click-to-skip keep behaving exactly as they
do in a hand-written sheet. `my picked choice` and `when choice picked` keep working unchanged: the
same pick both jumps the tree and answers a hand-written reaction, so a sheet can listen in.

### Validation and audit

Loader errors name the file, line and node:

- no node at all, a duplicate node name, an unknown tag, or a tag with the wrong number of parts;
- `@speed` that is not a number or is negative, and `@flag` with no name;
- a choice or jump whose target is neither a node nor `end`, and a choice with no text;
- a node that has both choices and a jump (the jump could never run).

Warnings instead of errors:

- an unreachable node (no path from the entry node) — a dead node is a writer's leftover, not a crash;
- a jump-only cycle (`a -> b`, `b -> a`) is warned about at load, and the runner caps consecutive
  jumps (16) before closing with one warning, so a data bug can never hang a frame.

Audit, enforced by the suite:

- every shipped `.dialog` parses, compiles and validates — the same shape as “every shipped `.events`
  file parses”, so a broken conversation fails the build instead of the game;
- every tag in the closed set above has a row in the dialogue-file section of `keywords.html`, and each
  documented tag example is parsed by the suite (tags get the word audit's treatment without becoming
  words);
- `talk to` and `when dialogue ends` get ordinary `WordRegistry` rows and keyword rows, both directions
  enforced as usual;
- an equivalence check: `Innkeeper.dialog` + `talk to "Innkeeper"` compiles to the same nodes the
  hand-written `Innkeeper.events` executes, and the Part E conversation checks run against both.

### Examples

```text
# --- Innkeeper.dialog ---
== greet ==
@speaker Innkeeper
@portrait InnkeeperPortrait innkeeper
A warm room, three coins a night. Interested?
> I'll take it. -> rent
> Just looking. -> browse

== rent ==
@flag RentedRoom
Third door on the left. Sleep well.
-> end

== browse ==
Suit yourself. The door is always open.
-> end
```

```text
# --- Innkeeper.events: the whole sheet that runs it ---
when me is clicked
  talk to "Innkeeper"

when dialogue ends
  hide DialogPanel

when message "close dialogue" arrives
  clear dialogue
  hide DialogPanel
```

### Parts

- [ ] **Part A — The format.** `Events/DialogueFileParser.cs` (grammar above, friendly errors with
      file/line/node) and `Runtime/DialogueProgram.cs` (`DialogueNodeDef`, `DialogueLine`,
      `DialogueChoice`, `DialogueTag`; each node compiled to `List<StatementNode>` of the Task 35
      nodes). Pure: no Flax types, tested from strings.
- [ ] **Part B — `talk to` and the runner.** `Runtime/DialogueRunner.cs` (cursor, jump, `-> end`),
      `EventRuntime.TalkTo` / `EnsureDialogue`, the `.dialog` file index and its near-miss warning,
      hot reload of a changed file, the second-`talk to`-replaces rule, and `when dialogue ends`
      classification plus broadcast.
- [ ] **Part C — Choices, backlog and closing.** Choices shown only once the last line finishes,
      numbering through `ChoiceManager` so `my picked choice` / `when choice picked` are unchanged,
      each line added to the backlog (`speaker: text`), `clear dialogue` on `-> end`, and the
      jump-hop guard.
- [ ] **Part D — Validation, audit and docs.** Reachability and cycle warnings, the suite audit over
      `GameFiles/**/*.dialog`, the tag rows in `keywords.html`, `WordRegistry` rows for the two words,
      and a `DESIGN.md` §9 entry recording that dialogue files carry no event words.
- [ ] **Part E — Tests and the shipped example.** `Innkeeper.dialog` beside `Innkeeper.events`, both
      played by the same conversation checks (greeting, both branches, the flag, the backlog, the
      close, click-to-skip), the shipped-file audit, and the editor play-check note.

### Done-when

- A writer can add a node and a choice with no `.events` edit at all, and the conversation still types
  out, numbers its buttons, branches and fills the backlog.
- `Innkeeper.dialog` + `talk to "Innkeeper"` plays the same conversation as `Innkeeper.events`,
  node for node — including the story flag and the backlog lines.
- A typo'd target, an unknown tag or a duplicate node fails the suite naming the file, line and node;
  an unreachable node and a jump-only cycle warn without hanging.
- A project with no `.dialog` files and no `talk to` behaves exactly as it does today, and every
  hand-written Task 35 sheet keeps working unchanged.
- Both words and all six tags are audited in both directions, and dialogue text can be translated by
  swapping files without touching a single word of logic.

### Parked for later

- Conditions in the file (`@if my RentedRoom is true`, `> choice if …`) and `talk to "<name>" at
  "<node>"` — until they exist, a returning-guest branch is either a second file or a choice inside
  the tree, not an entry-point decision.
- Voice lines per line, per-language variants (`Innkeeper.de.dialog`), importing Twine/Ink/Yarn
  exports, and a node-graph view in the editor plugin.

---

## Task 51 — Self-Test Sheet: Automated Play-Checks `[~]`

Goal: stop asking a human to open the editor, hold W, and *look* at a cube. Right now every shipped
mechanic ends with a `[ ] Verify (user, editor)` line — jump height, checkpoint respawn, steering,
the hurt cycle, the platformer gate — and those lines never close, so the task list can no longer
tell a proven part from an unproven one. This task gives the project a **self-test sheet**: an
ordinary `.events` file that drives the game and *asserts* what happened, so the play-check becomes
one editor run that prints `PASS`/`FAIL` per case, and the same sheet also runs headless inside the
unit suite.

The sheet is the point, not the words: it proves the language can express its own verification.
A feature with no case is a feature nobody has run.

Depends on Tasks 1–3 (nothing else). **Runtime + events** (P0). No Flax API changes.

### The shape

A **case is a block.** No new control flow, no sequencer: each case is one `when` block that sets a
situation up, waits, and asserts. The runner watches every block that contains an `expect` or a
`pass`, times it, and reports one line per case. This keeps the file readable as an example and
keeps the language flat, which is the same rule every other task follows.

```text
# ---------------- SelfTest.events ----------------
when me appears
  set my Passed to 0
  set my Failed to 0

# ---- case: a jump clears the 40 cm ladder gate ----
test "jump clears 40 cm"
when me appears
  teleport me to JumpPad
  wait 0.1 seconds
  press jump                                    # whatever the shipped jump word is called
  wait 1 second
  expect my Height is higher than 40
  pass

# ---- case: the checkpoint remembers the pose ----
test "checkpoint respawn returns to the marker"
when me appears
  teleport me to CheckpointA
  wait 0.1 seconds
  save
  teleport me to FarAway
  wait 0.5 seconds
  respawn at CheckpointA
  wait 0.2 seconds
  expect me is near CheckpointA by 5
  pass
```

### Proposed words

| Kind | Word | Example | Why |
|---|---|---|---|
| action | `test "<label>"` | `test "jump clears 40 cm"` | names the case; defaults to the block's order when omitted |
| action | `expect <condition>` | `expect my Health is 0` | reuses the existing condition grammar — no second comparison language |
| action | `pass` | `pass` | closes the case as passed (the label comes from `test`) |
| trigger | `when self test ends` | `when self test ends` | one summary hook: `set text of ResultLabel to my TestFailures` |

`expect` takes **any condition the language already accepts** — `is` / `is not` / `higher` /
`lower` / `near` / `exists` / `has tag` / `count of … is …` once Task 28 ships — because it is
parsed by `Conditions.Parse`, not by a new grammar. A shape that fails to parse fails like any other
condition (friendly error, at load).

### How a case is judged

- **Passed** = `pass` was reached before the timeout (default 5 s, `self test timeout 12 seconds` to
  change it).
- **Failed** = an `expect` was false (the report prints the expectation's own text and both values),
  or the case timed out with its last reached line quoted.
- **Skipped, not passed** = a case that never entered its body (its gate never opened) is reported as
  `SKIP` with the gate's text — a silently-not-run case is exactly the bug this task exists to kill.
- A case that errors or warns during the run (`unknown anim`, missing actor) is reported as `FAIL`
  with the warning attached, not swallowed.

### The runner

- `Runtime/SelfTest.cs` (engine-free): case discovery from a compiled `EventSheet` (blocks carrying
  `expect`/`pass`), per-case state machine (idle → running → passed/failed/skipped), timeout,
  ordered report lines, and the final counts as globals `TestPassed` / `TestFailed` so a label can
  show them.
- `EventRuntime` gains the hook: when a sheet is marked as a self-test sheet, its tick pushes case
  transitions into the runner and raises `self test ends` once every case has settled.
- The **marking is per file**, not per actor: an `EventSheet` component flag `Self Test` (default
  off), plus the convention that a sheet named `SelfTest.events` is one. A project with no such
  file and no `expect` line behaves exactly as today.

### Two tiers of the same sheet

1. **In the editor (the play-check).** `SelfTest.scene` holds the props the cases need (a jump pad,
   two checkpoints, a far-away marker, one dummy with a collider). Play once; the console prints
   `PASS 12 / FAIL 1` with the failing case named, and a `ResultLabel` shows the same counts. This is
   the run that replaces "hold W and look".
2. **In the suite (the build gate).** The `Tests` project already compiles `Runtime/*` and builds fake
   worlds by hand; it runs the **same** `SelfTest.events` file through the pure runner and fails the
   build on any `FAIL`. Bridge-only cases are tagged `test "… (editor only)"` and are excluded from the
   headless tier explicitly rather than silently passing it.

### What the first sheet covers

One case per shipped mechanic, chosen from the open `Verify (user, editor)` lines in this file and
`TASKS.md` — movement, jump and the 40 cm ladder gate, `goto` arrival, `sees`/`stops seeing` edges,
contact enter-edge vs. per-tick, `send message` routing, `save`/`respawn at` and the `CheckpointSaved`
gate, spawn/destroy and clone sheets, timers and `wait`, variables and edges, `expect count of …`
once Task 28 lands. Task 36 Part G's five-step platformer check becomes five cases in one block set.

### Parts

- [X] **Part A — `expect` and `pass`.** Parser + `ActionWords` + `WordRegistry` rows, `expect`
      re-parsing its tail through `Conditions.Parse`, `EventRuntime` evaluation with the case state
      in `Runtime/SelfTest.cs`. Friendly error for `expect` with no condition (`expect is 0`),
      and for `pass` outside a case. No new comparison semantics anywhere.
- [x] **Part B — The runner and its report.** Case discovery, timeout (default 5 s, per-sheet
      `self test timeout <n> seconds`), SKIP detection for gates that never open, warning capture,
      `TestPassed`/`TestFailed` globals, `when self test ends`, and one console line per case in
      sheet order. Deterministic: no `random` in a case unless the seed is fixed by `Rng.Seed`.
- [x] **Part C — `SelfTest.scene` + `SelfTest.events`.** The props, the actor, the `Self Test` flag,
      the `ResultLabel`, and the first case set (the coverage list above). Written so a reader can
      learn the language from it — it is a documentation artefact as much as a test. Shipped:
      `Content/scenes/SelfTest.scene` (authored JSON: Player with a CharacterController +
      `EventSheet SelfTest` and *Self Test* ticked, the pads/markers/ladder/checkpoint/dummy/breakable
      props, the inactive `SelfTest_Coin` template, `ResultLabel`, one `EventsManager`),
      `GameFiles/SelfTest.events` (13 cases: variables, timers/`wait`, variable edges, `goto`
      arrival, message routing, contact enter-edge, `sees`/`stops seeing`, jump, ladder, checkpoint
      save + `CheckpointSaved` gate, `respawn at`, spawn + clone sheet, `destroy`; cases are chained
      by `send message "case N" to me` so one body runs at a time), plus the two small helper sheets
      `SelfTest_Dummy.events` and `SelfTest_Coin.events`. Eight cases are tagged `(editor only)` —
      that is the documented Part D list. Three small runtime additions the run needed: the
      `EventSheet` *Self Test* flag (with the `SelfTest.events` filename convention) exposed as
      `SheetInstance.IsSelfTest`; `SelfTest.Reset()` called once from `EventsManager.OnStart`, so a
      second Play reports only itself instead of the previous run's cases; and one
      `self test: PASS n / FAIL n / SKIP n` summary line after the per-case report. 8 new checks in
      the suite -> **2342 passed, 0 failed**; `dotnet build Source/Game.csproj` 0 warnings/0 errors
      and `Flax.Build -target=GameEditorTarget` exits 0. The editor Play itself (the actual move,
      jump and ladder look) is still the user's run — nothing in this part watched the scene play.
      Editor-only cases have no fake-world assertions yet; that is Part D.
- [x] **Part D — The headless tier.** `Tests/Program.cs` runs the shipped file through the pure
      runner against a fake world, asserts the exact case count and the PASS/FAIL split, and fails
      the suite on any `FAIL`. A `(editor only)` tag marks the cases the fake world cannot reach; a
      suite check asserts those are exactly the documented list, so a case cannot be quietly
      demoted to editor-only when a fake-world fix would do. Shipped: the suite parses
      `SelfTest.events` with the same parser the editor uses, registers it on a `Player` sheet in a
      `FakeWorld` (with `Dummy` and `ResultLabel`, plus `SelfTest_Dummy.events` so the routing case
      really runs on another sheet), and ticks 50 ms frames for ~20 s. The one bridge verb the five
      bridge-free cases reach is `goto <waypoint>`, so the tier's `OnRawAction` stands in for the
      walk (it lands the distance the `when me arrives at` poll reads) — the arrival edge and every
      assertion stay real runtime behaviour. The run settles at **13 cases, PASS 5 / FAIL 0 /
      SKIP 8**, and the suite checks the count, the zero FAIL, the five passing labels in order, the
      eight SKIP labels and a single `when self test ends`. Exclusion is explicit in the runner, not
      silent: `SelfTest.ExcludedLabels` (+ `IsExcluded`, cleared by `Reset`) makes `RunEvent` report
      an excluded case SKIP without running its body, and a suite check compares the file's
      `(editor only)` tags with the documented eight **in file order**, so a case cannot be demoted
      to the list without editing both. The tier found two real sheet bugs, both fixed here: case 3
      set `Count` from 3 down to 1, and `becomes 3` also fires on a downward crossing of 3, so the
      edge counted twice (3 → 4 → 3 is one equality edge); and `expect me is near Marker by 150`
      can never be true, because `FindNamedActor` only resolves actors that carry a sheet and a
      waypoint has none — the case now asserts the arrival edge itself, which works in both tiers.
      8 new checks -> **2350 passed, 0 failed**; `dotnet build Source/Game.csproj -c
      Editor.Windows.Debug` 0 errors (13 pre-existing XML-doc warnings, none in the new code). The editor Play of `SelfTest.scene` is still the
      user's run; Part E shipped below.
- [x] **Part E — Status reconciliation and docs.** Every `[ ] Verify (user, editor)` bullet in
      `TASKS.md` and `Tasks_2.md` either names its case or carries one line saying why it cannot be
      automated (feel, audio, visual); the stale claims get fixed at the same time. `keywords.html`
      rows, `DESIGN.md` §9 entry (why a case is a block, and why `expect` reuses the condition
      grammar), and a `Task 51` line in the execution order.
      Shipped: the two open `Verify (user, editor)` bullets in `TASKS.md` (hot reload during Play;
      assigning `EventSheet` in a scene), the older `TASKS.md` notes that said "editor run pending"
      (the one-file mini-game, the dynamic bullet and the `Colliders` acceptance, the guard patrol,
      Task 5's animation play and character example, Task 7's patrol/chase, `Enemy_Walls`' chase /
      separation / four body modes, the arcade lane, the point-and-click loop, spells, loot
      affixes), and the editor play-check notes in this file (animation and root motion, dialogue
      typing, portrait, choices, backlog, the platformer list, aim) each gained a bracketed
      `Task 51 Part E` line, and each line is one of two kinds: **the case that covers
      it** — the four `(editor only)` cases in `SelfTest.scene` (`jump takes off and lands`, `ladder
      climb starts and lands`, `checkpoint saves on touch`, `respawn at returns to the marker`) — or
      **the reason no case can exist**: a baked NavMesh or authored scene, a real click or cursor
      value, a picture, a font, a measured height, a feel. Task 35's four notes are all
      reason-carrying, since what remains there is authored UI actors and one portrait.
      The stale claim is corrected: the park note under the platformer's edge cases said a landing
      state needs a poll "until someone adds a `when me lands` trigger", but that trigger shipped
      with Task 28 Part C (`Triggers.cs`, beside `when <building> lifts off`) — it now says so and
      explains that
      `Platformer.events` polls because it picks a pose each tick, not because the edge is missing.
      `keywords.html` already carried all four words (`test "<label>"`, `pass`, `expect`, `when self
      test ends`) and the `self test timeout <n> seconds` row from Parts A–B, so no row was added
      there; `DESIGN.md` §9 gained the entry on why a case is a block, why `expect` re-parses
      through `Conditions.Parse`, why a gate that never opens is `SKIP`, and how the two tiers share
      one file. The execution-order item (`Task 51`, "do it early despite being last in the list")
      already existed from the spec session and was left as written. Docs and status only, no code
      and no check changed — the suite is untouched at **2350 passed, 0 failed**. The editor Play of
      `SelfTest.scene` (and with it the eight `(editor only)` cases) is still the user's run.

### Done-when

- One editor run of `SelfTest.scene` prints a full `PASS`/`FAIL`/`SKIP` report and a count, with no
  human judgement required for anything but the cases explicitly marked visual or feel.
- The unit suite runs the same `.events` file and fails the build when a case fails — so a mechanic
  that regresses breaks CI, not just a play session.
- `expect` accepts every condition the language accepts (including the Task 28 forms when they land)
  and a malformed one gets the ordinary friendly condition error at load.
- A case whose gate never opens reports `SKIP`, never `PASS`, and a case that times out quotes the
  line it was stuck on.
- A project with no `SelfTest.events` and no `expect` line parses and plays exactly as before; the
  words are opt-in and the only shipped user is the self-test sheet itself.
- Every open editor play-check in this file and `TASKS.md` is either covered by a case or has a
  written reason it is not — the `[ ]` verify lines become a short, honest list instead of a debt
  nobody tracks.

### Parked for later

- Input record/replay and frame-accurate determinism (Task 44), screenshot comparison, running the
  editor from CI, and agent-driven play — all of them need this sheet's assertions first.
- Property-based cases (`test random 100 runs`) and a coverage figure per word: both are cheap once
  the runner exists, and both are noise until the first case set is stable.

---

## Task 52 — Camera Follow Centered `[x]`

Goal: `camera follows <thing>` keeps the camera centred above the subject at the authored
height/depth, and settles back into centre with a smooth spring when the player stops moving.
Today `StartCameraFollow` builds the offset from default `CameraHeight`/`CameraDepth` globals
(often 0), so a placed camera jumps onto the player and sits on top of them. This task makes the
camera follow at centre with damping, keeping the authored rotation.

Depends on Tasks 1–3, 26. **Presentation primitive** (P1).

### Proposed words

| Kind | Word | Example |
|---|---|---|
| action | `camera follows <thing>` | `camera follows Player` (centres above the player at authored height/depth) |

The verb already exists in `WordRegistry`; this task is the **behavior fix** to make it centre on
the subject instead of keeping the camera's real placed position, and add damping.

### Examples

```text
# ---- PlayerCamera.events: the camera's own sheet ----
when game starts
  camera switch PlayerCamera
  camera follows Player

when scene starts
  camera switch PlayerCamera
  camera follows Player

# ---- player.events: untouched ----
when Ground is clicked
  goto there speed 100

when me arrives at there
  loop anim "animation idle pose"
```

### Parts

- [x] **Part A — Centre the follow offset.** `StartCameraFollow` (`Bridge/FlaxActions.cs`) reads
      `CameraHeight`/`CameraDepth` globals into `new CameraPoint(0, camHeight, camDepth)` instead of
      `camera.Position - target.Position`, so the camera centres above the player instead of keeping
      its placed side position. The camera keeps its authored rotation — the `Quaternion.Euler`
      CameraPitch reset is gone.
- [x] **Part B — Smooth follow with damping.** `UpdateCameraFollow` (`Bridge/EventsManager.cs`)
      lerps the camera toward `target.Position + offset` each tick instead of snapping, so when the
      player stops the camera slowly drifts back into centre (spring-back feel like TK/Mario).
      Frame-rate independent `dt`; `CameraFollowDamping` global tunes it, default 6/s, 0 = snap.
- [x] **Part C — `PlayerCamera.events` owns the camera.** New `Source/Game/GameFiles/PlayerCamera.events`
      holds `camera switch PlayerCamera` + `camera follows Player` on both `when game starts` and
      `when scene starts` (the latter re-arms on every Play). Remove any camera lines from
      `player.events` so the camera script lives in its own file, not the player's.
- [x] **Part D — Scene wiring.** Add an `EventSheet` component pointing at `PlayerCamera` to the
      `PlayerCamera` actor in `Content/scenes/AdventureTest.scene`.
- [x] **Part E — Tests and docs.** Unit check that `StartCameraFollow` centres the offset;
      editor play-check: camera starts centred above the player, follows smoothly keeping centre,
      and settles back into centre after the player stops. Record the damping value and follow speed
      in `DESIGN.md` §9.
      Shipped: `Runtime/CameraFollow.cs` (`Offset` = camera minus target, `Alpha` =
      `1 - exp(-damping * dt)`, `DampingFrom` reading the `CameraFollowDamping` global, default **6**/s,
      `0` = snap) with 7 checks, `StartCameraFollow` centring the offset and leaving the rotation alone,
      `UpdateCameraFollow(delta)` easing toward `target + offset` instead of snapping (zoom still scales
      the offset), `PlayerCamera.events` holding `camera switch PlayerCamera` + `camera follows Player`
      with the `PlayerCamera` actor's blank `Event File` resolving to it, and the leftover commented
      camera lines removed from `Adventure_player.events` (they left a `when` with no body, so that
      shipped file failed to parse). `keywords.html` and `DESIGN.md` §9 updated.
      Editor play-check pending (feel of the 6/s damping).

### Done-when

- A camera follows centred above the player at the authored height/depth.
- When the player stops moving, the camera smoothly returns to centre rather than snapping.
- The camera lives in `PlayerCamera.events`, not `player.events`.
- No existing camera word or sheet changes behavior when `camera follows` is not used.

---

## Suggested execution order

Do not run these by number. The dependency-friendly order is:

1. **Task 28** (sets/counts/filtered targets) — everything else gets shorter.
2. **Task 30** (save/load) — turns demos into games.
3. **Task 29** (expressions/data lookup) — unblocks RPG/strategy formulas.
4. **Task 31 + 32** (audio + camera feedback) — cheapest “real game” upgrade.
5. **Task 34 + 35** (dynamic UI + dialogue) — the content-facing layer.
6. **Task 36 + 37** (platformer kit + Diablo RPG kit) — two genre pillars.
7. **Task 38 + 39** (AI orders + input breadth) — polish for action/strategy.
8. **Task 33 + 43 + 44** (animation depth, settings/localization, replay/perf) — shipping quality.
9. **Tasks 40–42** (roguelike, racing, cards) — only when a project asks.
10. **Tasks 45–47** (multiplayer decision, packaging, debugger) — scope and workflow.
11. **Tasks 48–49** (cursors, mouse mapping) — cheap input polish that any genre feels; Task 48 first
    because a remapped button is much easier to try with a cursor that says which mode is armed.
12. **Task 50** (dialogue files) — right after Task 35 ships and only when a real conversation
    outgrows one sheet; it adds no presentation, only a place for prose to live.
13. **Task 51** (self-test sheet) — do it early despite being last in the list. Every item above ends
    with an editor play-check, so each one done before this task adds another line a human has to
    hold in their head; after it, a part is finished when a case says so. If only two things ship
    this quarter, pick this and Task 28.

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
