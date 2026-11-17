# tasks_RTS.md — RTS Game Support Work Queue

StarCraft / Red Alert / Homeworld feature parity for the EventManager.

Source of truth: Docs/rts-event-manager-support.md (design doc). Tasks say *what*;
the design doc says *how*.

Status legend: [ ] not started  [~] in progress  [x] done  [!] blocked

---

## Task 18 — RTS Core Foundations [ ]

Goal: add the new RTS trigger/condition/action types to `Triggers.cs` / `Conditions.cs` /
`FlaxActions.cs` plus the supporting code components (Selection, OrderQueue, ResourceManager,
ConstructionManager, FogOfWarManager, extended `IEventWorld`). Parse gracefully if unknown;
zero breaking changes to existing `.events` files.

### Part A — Selection System (`Selection.cs`) [x]
- New TriggerKind: Selected, Deselected, SelectionChanged, ControlGroupPressed, ControlGroupHeld
- New ConditionKind: Selected, SelectionCountIs, MyControlGroupAssigned
- New action verbs: `select me`, `deselect me`, `set my control group to <n>`,
  `select all <unit type>`, `select units near me by <dist>`
- UI integration: selection ring/highlight actor per selected unit; control group indicators (1–0 keys)
- StarCraft click/box-select; Red Alert control groups; Homeworld strike groups

### Part B — Resource Economy (`ResourceManager.cs`) [x]
- Global variables: Minerals, Gas, SupplyUsed, SupplyMax, FoodCap, Ore, Gems, Power, RUs
- New triggers: `when Minerals becomes <n>`, `when Supply becomes <n>`, `when ResourcesChange`
- New conditions: `Minerals is higher than <n>`, `Supply Used is lower than Supply Max`,
  `can afford <building>` (inline cost check)
- New actions: `add <n> to Minerals`, `set Minerals to <n>`, `deduct <n> from Minerals`,
  `increase Supply Max by <n>`
- StarCraft minerals/gas/supply; Red Alert ore/gems/power; Homeworld RUs (resource units)

### Part C — Construction & Base Building (`ConstructionManager.cs`) [x]
- New triggers: `when <building> is placed`, `when construction completes`,
  `when <building> is destroyed`
- New actions: `place <building> at <target>`, `cancel build`, `set build progress to <n>`
- New conditions: `<building> exists`, `<building> is under construction`,
  `enough Minerals for <building>`
- Free-form placement by default; opt-in `place … on grid` snaps to cells and reserves footprints
- Red Alert-style base construction; StarCraft-style building placement; Homeworld shipyard/subsystems

#### Part C follow-ups (from code review — not blocking) [x]
- [x] `set build progress to <n>` / `add <n> to build progress` only act on `context.Me`, so they silently no-op on a global sheet. Add an optional named form (`set build progress of <building> to <n>`, `add <n> to build progress of <building>`) and/or warn once when there is no acting actor.
- [x] `when construction completes` has an empty `RequireName`, so it fires on every sheet for any building completion. Add a per-building form (`when my construction completes` / named) or document that a building's own sheet must ignore completions for other buildings.
- [x] Clarify the `ConstructionManager.Cancel` comment/docs: cancel destroys the placed actor silently (no `BuildingDestroyed` signal); "cancelling is not destruction" refers only to the missing signal.
- Not planned: automatic cost deduction on `place` (authors gate with `enough` / `can afford` and `deduct` explicitly), and `GetBuildProgress` on `IEventWorld` (Part H scope).

### Part D — Production Queues & Unit Training [x]
- New triggers: `when <building> finishes training <unit>`,
  `when <building> starts producing <unit>`, `when production queue is empty`
- New actions: `train <unit> at <building>`, `cancel training <unit>`,
  `set build time to <n> seconds`
- New conditions: `<building> is idle`, `queue count is <n>`, `me has build queue`
- StarCraft Barracks/CommandCenter training; Red Alert unit production; Homeworld strike-group fabrication

### Part E — Orders & Attack-Move (`OrderQueue.cs`) [x]
- New triggers: `when order completes`, `when queued order starts`, `when path blocked`
- New actions: `attack-move to <target>`, `attack <target>`, `stop`, `hold position`,
  `add order to queue`, `clear orders`, `set rally point to <target>`
- Per-unit order state: `my current order`, `my order target`, `my order queue`
- StarCraft attack-move; Red Alert attack-move; Homeworld formations and strike-group orders

### Part F — Fog of War / Visibility (`FogOfWarManager.cs`) [x]
- New triggers: `when area becomes revealed`, `when area becomes hidden`,
  `when <unit> enters fog`
- New actions: `reveal area around me by <radius>`, `hide area around me by <radius>`,
  `set visibility to <radius>`
- New conditions: `me is visible to enemy`, `me is hidden`
- Visibility mask per player (grid-based); units reveal a radius; buildings reveal footprint;
  explored persists vs reverts to fog (see Open Questions)
- Homeworld sensor range/stealth; StarCraft fog of war; Red Alert shroud

### Part G — Damage & Per-Unit Health [x]
- Extend: `my Health` / `my MaxHealth` per-unit variables; `my Shield` / `my Armor` (optional)
  → `my Shield` / `my MaxShield` / `my Armor` too (`Runtime/HealthManager.cs` keeps them in range)
- New triggers: `when my Health becomes 0`, `when my Health is lower than <n>`,
  `when message "damage <amount>" arrives`
  → the first already worked via `when <var> becomes`; the threshold is a new polled edge, and the
  message text is now a template whose `<slot>` captures a number into `my <slot>` / `my DamageTaken`
- New actions: `set Health to <n>`, `add -<n> to Health`, `add <n> to Shield`,
  `set Shield to <n>`
  → all clamp (`0..MaxHealth` / `0..MaxShield`); `add -<n> to Health` and `deduct n from Health`
  run the damage pipeline: armor, then shield, then health (never below 0)
- Bare `Health` / `Shield` / `Armor` on a unit's own sheet address that unit's stat; global sheets
  keep writing the global of that name. Sheets that never declared health keep the old arithmetic.
- StarCraft unit HP/shields; Red Alert unit health; Homeworld subsystem damage

#### Part G follow-ups (from implementation — not blocking) [x]
- [x] `add -my <var> to Health` works: values accept a leading minus (`-my DamageTaken`,
  `-other's damage`, `-Score`), so the captured damage number runs the full armor → shield →
  health pipeline in one action. `set X to -my Y` and conditions' right-hand values negate too.
- [x] Multiword stat names parse and keep their spelling: `set my Max Health to 80`,
  `add 5 to Max Health`, `set my Max Shield to 10` and the bare forms all resolve, clamp, and
  read back through `my MaxHealth` (and vice versa).

### Part H — Extended `IEventWorld` [x]
- `GetSelection()` → list of selected actors
- `GetSelectedCount()` → int
- `GetResource(string name)` → value
- `IsRevealed(actor, playerId)` → bool
- `RevealArea(center, radius)` / `HideArea(...)`
- `GetBuildProgress(building)` → float
- `GetOrderQueue(unit)` → list

All eight are on `IEventWorld` now. `FlaxWorld` forwards them to the live `EventRuntime`
(null-safe), `DefaultEventWorld` answers empty/no-op, and the runtime exposes the shared
accessors (`SelectedActors`, `SelectedCount`, `ResourceValue`, `IsRevealedFor`, `RevealArea`,
`HideArea`, `BuildProgressOf`, `OrderQueueOf`). Fog areas take engine-free `(playerId, x, z,
radius)` instead of a `Vector3` so the interface stays usable from tests; `GetOrderQueue`
returns order names, active first (`OrderManager.OrderNames`).

---

## Task 19 — RTS `.events` Content [ ]

Goal: write the RTS `.events` files plus StarCraft / Red Alert / Homeworld-specific examples.
Files live in `Source/Game/GameFiles/` alongside `Player.events`, `Enemy.events`, `Game.events`.

### Part A — StarCraft-style examples [ ]
- `UnitTypes.events` — the RTS unit table: cost, build time, supply, hit points, attack type,
  range, speed, what it can attack and which building trains it (read at boot like
  `Matchups.events`; `EnemyTypes.events` stays the non-RTS typed-enemy file)
- `Marine.events` — ranged infantry: selection ring, combat, per-unit HP
- `Zergling.events` — melee infantry
- `Barracks.events` — production building, train Marine
- `CommandCenter.events` — economy HQ, passive mineral income
- `SupplyDepot.events` — supply provider
- `Global.events` — minerals/gas/supply global state

### Part B — Red Alert-style examples [ ]
- Ore refinery, gem field, power plant
- Base defenses: turret, pillbox, SAM site
- Ore collector/truck, harvester
- Superweapon placeholder trigger (Iron Curtain / Chronosphere)
- Tesla coil, flame tower
- Red Alert power deficit mechanics (power shortage slows production)

### Part C — Homeworld-style examples [ ]
- Fighter / corvette / frigate / capital ship with 3D movement
- Strike group formation orders
- Resource collector + carrier/mothership docking
- Hyperspace jump trigger
- Sensor ping / stealth detection
- Research subsystem

---

## Task 20 — RTS Acceptance & Polish [ ]

Goal: playable RTS mini-scene(s) verifying selection, resources, building, production,
attack-move, and fog of war.

### Part A — Selection + resources demo [ ]
- Select units via click / box-select; selection rings show
- Minerals/gas accumulate; HUD labels update on change
- Can-afford gates training

### Part B — Base building + production demo [ ]
- Place Barracks; build progress timer; unit spawns on complete
- Rally point; auto-select new unit

### Part C — Combat + fog of war demo [ ]
- Attack-move to target; order queue; stop/hold position
- Units reveal fog; unseen areas hidden
- Per-unit HP; destroy at 0

### Part D — Cross-game comparison notes [ ]
- Record StarCraft / Red Alert / Homeworld behavior gaps in `DESIGN.md` §9
- Decide Open Questions below

---

## Task 21 — RTS Command Panel & Orders [x]

Goal: add attack-move, attack, stop, hold position, rally point, and per-unit order queue so a command panel can issue real RTS orders.

The verbs, triggers and per-unit state landed with Task 18 Part E (`OrderManager.cs`, `ActionWords`,
`Triggers`, `FlaxActions`). Task 21 filled the two gaps that kept a panel from being written against
it: the range condition, and `attack` walking into its target instead of stopping at range.

### Part A — Attack-move & attack [x]
- New action verb: `attack-move to <target>` — move toward target, engaging any enemy that enters range along the way
  → `ActionVerb.AttackMove`, `OrderManager.StartOrder` + `FlaxActions.BeginOrderMove`; the
target is an actor or `there`, and the sheet's own `me is in range of Enemy` block is what
engages along the way (the bridge has no notion of "enemy")
- New action verb: `attack <target>` — stop moving and attack the named target in range
  → `ActionVerb.AttackOrder`. Stops at `my attack range` (`OrderManager.ApproachPoint` walks to
the line, one range short) and leaves the order active; a target already in range is not walked
into. A sheet that never declared a range keeps the old contact behaviour, so melee units still
reach their target.
- New trigger: `when order completes`, `when queued order starts`
  → `TriggerKind.OrderCompletes` / `OrderStarts`, raised by `OrderManager.Complete` / a promoted
queue entry, plus the `<order> order completes` name filter
- New condition: `me is in range of <target>` (compares distance to my attack range)
  → `ConditionKind.InRangeOf`; XZ distance against `my attack range` (`DeclaredAttackRange`,
spellings `attack range` / `AttackRange`), defaulting to `OrderManager.DefaultAttackRange` (50)
so an existing melee sheet works as written. `set my attack range to <n>` and
`my attack range is <n>` parse the multiword name like the health stats.

### Part B — Stop & hold position [x]
- `stop` — cancel current order and go idle (`stop moving` + `Orders.Stop`, reads `stop`)
- `hold position` — freeze in place, do not pursue (`still` + `Orders.Hold`, reads `hold`)
- `clear orders`, `add order to queue` (copy of the active order behind it; an idle unit starts it)
- Per-unit order state: `my current order`, `my order target`, `my order queue` (also
`GetOrderQueue` on `IEventWorld`)

### Part C — Rally point [x]
- `set rally point to <target>` — newly trained units auto-move there on spawn: the rally target
lives on the building's `UnitOrderQueue` and `FlaxActions.SpawnClone` issues the new unit a normal
`move` order to it (`ApplyRally`), so it reads `my current order` = `move` and arrives like any
other move

### Notes
- Bridge-side behaviour (NavMesh approach, rally spawn, stop/hold bodies) has no unit tests — the
pure parts (`ApproachPoint`, attack-range read, `InRangeOf`, order queue edges) are covered by the
`Tests/` suite. Editor play-check of a command panel is still Task 20/22 work.

---

## Task 22 — RTS Unit Types & Terrain [x]

Goal: add land / air / sea unit types and per-terrain passability so units respect what they can walk/fly/swim on.

### Part A — Unit type [x]
- Per-unit variable: `my unit type` (`land`, `air`, `sea`)
  → ordinary per-actor local (`Runtime/UnitTypes.cs`), written with
`set my unit type to "air"` (the parser joins the multiword name, like `my attack range`) and
readable as a value (`set TypeLabel to my unit type`, `set text of TypeLabel to my unit type`). Both
spellings (`unit type`, `UnitType`) resolve. A unit that never wrote one — every sheet older
than this word — reads as `land`. An unknown spelling still writes, so a sheet can correct
itself, but warns once listing the three types.
- Conditions: `me is land`, `me is air`, `me is sea`
  → `ConditionKind.UnitTypeIs`, canonicalised so `me is Sea` and `me is sea` agree
- Ground units blocked by water; air units ignore ground obstacles; sea units restricted to water
  → the rules live in `UnitTypes.CanCross(type, UnitTypes.Ground.Land/Water/Air)` and are
enforced by Part B's map for `move`, `goto`, orders and `place`.

### Part B — Walkable area / terrain [x]
- Terrain passability map (land/water/air cells)
  → `Runtime/TerrainMap.cs`: a sparse grid of 64-unit cells (override with the
`TerrainCellSize` global) whose ground is `land` (default), `water` or `air`. Nothing is stored
until a sheet marks a cell, so an unmarked scene behaves exactly as before. Authored with two
word shapes: `mark water at Mouse` (one cell) and `mark water around Mouse by 300` (every cell
whose centre is in the circle); `land` marks clear a cell back. `TerrainMap.MarkedCells` and
`KindAt` are queryable for HUDs.
- Movement (`goto`, `move`) consults terrain per unit type — `UnitTypes.CanCross` is the rule
  → `FlaxActions.TerrainBlocksStep` runs before every step of `MoveSubject` (all four move
modes), before an order's path starts (`BeginOrderMove` reports `when path blocked` instead of
walking), and per waypoint on a `goto`/order path (the move ends and `when path blocked` fires).
A unit already standing on impassable ground may always step out, so terrain marked under a
unit never traps it. Air units pass everything; ground and sea units are stopped one cell early.
- Buildings only placeable on valid terrain for their type
  → `place` checks the spawned clone's cell against its own `my unit type` (read after its sheet
ran, so a sea building can declare itself on `when me appears`). An invalid spot warns once, frees
any grid reservation and destroys the clone.
- Enforce Part A's blocking rules through the map (`me is land` units stop at water, air units
ignore it, sea units stay on it) — done by the same `CanCross` table.

### Notes
- Bridge-side gating (stepping, blocked orders, placement refusal) has no unit tests; the pure
map (`TerrainMap`, `CanStep`, the escape rule, cell-size override, off switch) and the `mark`
parser are covered by `Tests/`. Editor play-check (paint water with `Mouse`, walk a land unit at
it, fly an air unit over it) still pending.

---

## Task 23 — RTS Combat Matchups & Formations [x]

Goal: add close vs range attack, attack matchup matrix, and formation movement for grouped units.

### Part A — Attack types [x]
- Per-unit variable: `my attack type` (`melee` / `ranged`)
  → ordinary per-actor local (`Runtime/AttackTypes.cs`), written with
`set my attack type to "ranged"` (parser joins the multiword name) and readable as a value
(`set TypeLabel to my attack type`). Both spellings (`attack type`, `AttackType`) resolve; a unit
that never wrote one — every older sheet — is melee. An unknown spelling still writes but warns
once, listing the two types. Conditions `me is melee` / `me is ranged` (`ConditionKind.AttackTypeIs`),
which stack with the unit type: a ranged air unit answers both.
- `my attack range` — distance at which ranged attacks can hit
  → already first-class since Task 21 (`set my attack range to 120`, `my attack range is 120`,
readable as a value, used by `me is in range of`). Part A adds the type-based default:
`OrderManager.DefaultRangeFor` gives melee `DefaultAttackRange` (50) and ranged
`DefaultRangedAttackRange` (300), so `AttackRangeOf` and the `attack` order
(`OrderManager.StopDistanceFor`) do the right thing before a sheet writes a number. A declared
range always wins.
- Close combat: damage on contact; ranged combat: projectile flies to target
  → melee keeps the old behaviour: `attack` walks into contact (stop distance 0), so a sheet's
contact `"damage"`/`send message` lands. Ranged closes only to its attack range and stops there,
leaving the strike to the sheet (`spawn Bullet`, `spawn <projectile>`, `send message "damage"`) —
the engine has no projectile to fire by itself. Documented on `AttackTypes`.

### Part B — Attack matchups [x]
- Data-driven table: which unit types can attack which (land vs land, land vs air, air vs land, air vs air)
  → `Runtime/Matchups.cs` + a new `GameFiles/Matchups.events`. One global per pair named
`<Attacker>CanAttack<Defender>` (`LandCanAttackLand`, `LandCanAttackAir`, `AirCanAttackLand`,
`SeaCanAttackAir`, … nine in all). `CanAttack` reads the live global first (so any sheet can
retune the table mid-game), then the file loaded by `EventsManager` at boot/reload, then the
built-in default: land and sea fight each other but never air, air fights everything. The data
file ships the defaults written out, so it is the obvious place to retune.
- Condition `me can attack other` — consults the matchup table
  → `ConditionKind.CanAttackTarget`. `other` or a named target (`me can attack Bomber`); both
sides' own `my unit type` decide, so a ranged air unit answers for air. `can` was added to the
parser's condition marks to let the line classify.
- Mismatched attacks are ignored (no damage)
  → `EventRuntime.DeliverToSheet` drops a `"damage"` / `"hurt"` message between two types that
cannot fight *before* it reaches the target's sheet, so no health change, no `accuracy` credit
and no last-attacker bookkeeping. A message with no sender counts as a land attacker, so nothing
that existed before changes behaviour (all of it is land on land).

### Part C — Formation movement [x]
- When multiple units are selected and given `goto`/`attack-move`, they arrange in formation (line, wedge, column)
  → `Runtime/Formations.cs`: `Slot(kind, index, count, spacing, heading)` returns each unit's
offset in the group's local frame. The shape is the `Formation` global (`line` default, `wedge`,
`column`; `row`/`v`/`file` also parse) and the gap is `FormationSpacing` (default 80). No new verb:
`FlaxActions.FormationSlot` reads the acting unit's index in `Runtime.Selection` and offsets the
destination of any **ground** order — `goto there` and `attack-move to there` both go through it
(`BeginOrderMove` for orders, the `goto there` branch for click-walks).
- Each unit walks to its offset position, not all to the same point
  → line spreads across the direction of travel, wedge trails two flankers per rank, column
files behind the point; a lone unit, an unselected one, or `FormationSpacing` 0 gets no offset,
so every existing single-unit command is unchanged. Actor targets are deliberately untouched:
units converge on the actor and an attack order's stop distance is measured from it.
- Units maintain spacing while moving
  → slots are `FormationSpacing` apart and the destination itself moved, so the units' paths
target different points; there is no per-tick separation pass (units may still bunch up passing a
narrow gap).

### Notes
- The bridge half (selection index → offset destination) has no unit tests; the pure slot maths
and the global readers are covered by `Tests/`. Editor play-check (select three units, click a
spot, watch them fan out) still pending.

---

## Task 24 — RTS Minimap [x]

Goal: add a minimap UI showing explored terrain, unit dots, and click-to-pan camera control.

### Part A — Minimap UI [x]
- Second camera rendering to a small render texture
  → **deliberately not a render texture.** The minimap is drawn from data instead: a
`MinimapPanel` UI control fills one block per sampled cell from `TerrainMap` and
`FogOfWarManager`, which already know exactly what the player may see. No second camera, texture
or material to wire up, and the fog can never disagree with the panel because there is nothing to
capture. Authoring: a UI Control actor named `Minimap` with control type `MinimapPanel` (Flax's
documented custom-control workflow).
- Terrain and unit markers drawn on the minimap
  → `Runtime/Minimap.cs` builds a `MinimapSnapshot` per tick (cells, unit dots with player and
selection, camera view rectangle); `Bridge/MinimapView.cs` draws it and feeds the actors in.
- Fog of war reflected on minimap (unexplored = hidden)
  → each cell stores the `FogState` for `Fog.LocalPlayer`: unexplored is near-black, explored is
dimmed remembered terrain, visible is full colour. Units are only plotted when
`Fog.IsVisibleTo` lets the local player see them.

### Part B — Minimap interaction [x]
- Click on minimap pans the main camera to that world position
  → `MinimapView.PanTo` maps the pixel back to a world point and focuses the free camera there
(entering free-pan mode the same way `camera free` does, Task 26), so a click also releases a
follow.
- Drag to select area on minimap
  → a press that moves past 4 px is a drag: `Minimap.DragRect` clips it to the panel and
`Selection.ReplaceSelection` selects the local player's visible units inside it.

### Notes
- Globals: `Minimap` (0 hides), `MinimapExtent` (2000), `MinimapSize` (220) and `MinimapGrid`
(32, capped at 96). The camera view rectangle is an approximation from `CameraHeight`, not a
frustum projection.
- The bridge half (custom control, mouse capture, drawing, selecting actors) compiles but has no
unit tests; the mapping, clipping, sampling and snapshot assembly are covered by `Tests/`. Editor
play-check still pending (assign the control type, click, drag).


---

## Task 25 — Enemy AI: Brain, Economy & Attack [ ]

Goal: give the enemy a *player brain*, not just scripts on individual units — it mines, spends,
expands, builds, trains, scouts, defends and attacks, and can be tuned without recompiling.

### How RTS games actually do this (and what we copy)

Every serious RTS opponent is a **layered manager stack**, not one big state machine. Published
Archon bots (EISBot, SPAR, AIUR, Nova/BBQ) all land on the same shape:

1. **Strategy** — one slow decision layer. Picks a game plan (rush / balanced / turtle), tracks a
   phase (opening → mid → late) and sets targets: worker count, supply, building list, unit mix,
   attack timing. Ticked about once a second, or on a phase change.
2. **Managers** — one per concern, each acting on the strategy's targets: income/workers,
   construction, production, combat, scouting. They talk to the game, not to each other, so a
   broken economy never stops the army from defending. Contention is settled by a spending
   priority order (defence > rebuild > supply > army > expansion).
3. **Execution** — units get ordinary orders (`attack-move`, `attack`, `goto`) and the game's own
   systems do the rest (pathing, ranges, formations, matchups).

Bots differ in where the *policy* lives: AIUR/MicroRTS keep it in scripted rule tables, Nova in
hand-written C#, research bots in planners (HTN/search). For this project the split is the same as
the rest of the codebase: **data drives policy, code drives decisions, events drive reactions.**

### Design for this project

| Layer | Lives in | Why |
| --- | --- | --- |
| Strategy profile (build order steps, targets, timings) | `GameFiles/AiStrategies.events` — plain `set` globals, the same pattern as `Matchups.events` and the RTS `UnitTypes.events` | designers/mods retune without a rebuild; hot-reloads like every other data file |
| Brain loop, planners, spending priority | new `Runtime/EnemyBrain.cs` + `AiEconomy` / `AiBuilder` / `AiProduction` / `AiArmy` (pure C#, no Flax types) | needs per-tick counts over every unit and building; unit-tested with the existing `FakeWorld` harness |
| Issuing orders | existing `OrderManager`, `ProductionManager`, `ConstructionManager` | one code path for player and AI, so AI orders obey the same rules |
| Per-unit reactions and income ticks | ordinary `.events` on units/resources/buildings | `when me arrives`, `when me is attacked`, `add 1 to Player2 Minerals` — already supported |

Strategy data uses numbered globals instead of a new grammar, e.g.
`set AiRushStep1 to "Barracks"`, `set AiRushAttackUnits to 6`, `set AiRushWorkers to 6`.
The brain walks `Ai<Strategy>Step1..N` until one is missing — no parser work, and a mod adds a
step by adding a line.

### Content is data, not code (audit before Part A)

A grep of `Source/Game/**/*.cs` for RTS names (`Barracks`, `Marine`, `CommandCenter`, `SupplyDepot`,
`Worker`, `Pylon`, …) returns **only** example strings in parser hints, `WordRegistry` rows, doc
comments and `Matchups.events` — no building or unit name appears in any decision path. The real
hardcoded things, and what to do about them:

| Hardcoded thing | Where | Verdict |
| --- | --- | --- |
| TK4 typed-enemy stats (`Zombie`, `Turret`) | `Runtime/EnemyTypes.cs` built-ins | Non-RTS content, overridden by `EnemyTypes.events`. Leave it; the RTS must never add entries here |
| TK4 weapon defaults | `Runtime/Weapons.cs` built-ins | Same: overridden by `WeaponData.events`. Leave it |
| Unit-type and attack-type vocabulary (`land/air/sea`, `melee/ranged`) | `UnitTypes.cs`, `AttackTypes.cs` + the default `Matchups` table | Closed vocabulary the engine reasons with, so it stays code — but a fourth type or third attack type needs a code change. Known limit, documented |
| Resource names (`Minerals`, `Gas`, `Ore`, `Gems`, `Power`, `RUs`, supply) | `ResourceManager` | **Fixed.** The list, aliases, which names mean supply and which resource `<Building>Cost` charges all come from `Resources.events` (built-in nine as defaults), read before the other sheets parse. Adding Credits/Tiberium is an eventsheet edit, no code |
| Actor-name conventions (`Player`, `Camera`, `Minimap`, `SelectionRing`, `ControlGroup0…9`) | `FlaxWorld`, `EventsManager`, `MinimapView`, `SelectionView` | Scene conventions with one-time warnings, but `Player` is game-specific. Move the lookup names to globals when convenient (`PlayerActorName`, `CameraActorName`) |
| Numeric defaults (grid 64, build time 5 s, formation spacing 80, pan 600) | `ConstructionManager`, `ProductionManager`, `Formations`, `CameraRig` | Fine: every one is overridable by a global or a per-building variable |

**Rule for this task:** a planner may only learn a unit, building or resource name from
`UnitTypes.events`, the strategy profile, or the resource list — never from a literal in C#. The
acceptance check greps the new `Ai*.cs` / `EnemyBrain.cs` files for those names and fails on a hit.

**Unit data for the RTS is `UnitTypes.events`, not `EnemyTypes.events`.** `EnemyTypes.events` is
the TK4-style typed-enemy table for the non-RTS scenes (speed/sight/health/weapon per enemy name)
and stays exactly as it is. The RTS needs its own unit table — cost, build time, supply, hit
points, attack type, range, speed, what it can attack, which building trains it — read at boot the
same way `Matchups.events` already is, with `UnitTypes.cs` (land/air/sea) unchanged underneath.
The AI reads it for unit costs and the composition ratio instead of hard-coding names.

### Part A — The brain loop (`Runtime/EnemyBrain.cs`) [ ]
- `EnemyBrain.Tick(runtime, dt)`, one instance per AI player, ticked from `EventRuntime` like
  `ProductionManager`/`ConstructionManager`
- Resolves the profile from `AiStrategy` (falling back to `balanced`), reads its targets, keeps a
  phase (`opening` / `mid` / `late`) from game time + supply + army size
- Spending priority when several managers want resources: defence → rebuild → supply → army →
  expansion → tech
- Actions: `start enemy ai for player 2`, `stop enemy ai`; condition `enemy ai is on`; value
  `enemy ai phase`, `enemy army size`; trigger `when enemy ai phase changes`
- Default: no brain runs unless a sheet starts one — old scenes are untouched

### Part B — Economy and workers (`Runtime/AiEconomy.cs`) [ ]
- **Separate per-player banks (decided).** `Player1 Minerals` / `Player2 Minerals` (and Gas, Ore,
  …) are the real stockpiles; the bare `Minerals` global stays as **player 1's** alias, so every
  existing single-player economy file keeps working untouched. `AiEconomy.CanAfford(variables,
  player, building)` mirrors `ResourceManager.CanAfford` against that player's bank, and the HUD
  reads the same names.
- Resource names are already data (see the audit above): `Resources.events` declares the list,
  spellings, supply names and the default cost resource; the AI's per-player banks read the same
  declared names, so a style's economy needs no code change
- Bank income comes from the game style (Part H): StarCraft workers mine, Red Alert ore trucks
  haul, and a style without workers uses the passive building income this project ships today.
- Worker bookkeeping: count workers, find idle ones, keep `Ai<Strategy>Workers` employed
- Assign idle workers to the nearest resource node (actor tagged `ResourceNode`, with
  `my NodeAmount`), `goto` it, and re-assign when the node runs dry
- Income itself stays authored: a worker next to a node ticks
  `add 1 to Player2 Minerals`, a drop-off adds the carry — no new gathering engine, and the same
  rule serves the human player's workers
- Supply: build a supply provider when `SupplyUsed` is within one unit of `SupplyMax`

### Part C — Construction (`ConstructionManager.FindEmptyCellNear`) [ ]
- `FindEmptyCellNear(variables, x, z, radius, building)` — spiral/ring search over the occupancy
  grid `TryReserveCells` already keeps, returns the nearest cell whose footprint is free
- Scoring for *where* to build: near the base for economy, forward toward the enemy for barracks
  and defences, away from the enemy for expansions; refuse spots inside marked water/blocked
  terrain (reuse `TerrainMap.IsPassable`)
- `Runtime/AiBuilder.cs`: turns the next build-order step into `place <building> at <spot>` calls,
  one at a time, remembering what it already has; rebuilds a destroyed building once
- Expansion: new base when the profile's `ExpandAt` supply/mineral threshold is met and the bank
  can afford it

### Part D — Production (`Runtime/AiProduction.cs`) [ ]
- Keeps every production building busy from the profile's unit mix (e.g. 3 melee : 2 ranged),
  gated by `CanAfford` and supply headroom
- Uses the existing `ProductionManager` queue so build times, rally points and
  `when training finished` all behave the same as for the player
- Priority: defence units first while under attack, then the profile mix

### Part E — Army: scouting, attack timing, defence (`Runtime/AiArmy.cs`) [ ]
- **Scouting:** pick the cheapest idle unit, send it `goto` the nearest unexplored cell (from
  `FogOfWarManager` + `Minimap` sampling), record the first enemy building it can see as
  `AiEnemyBaseX/Z`; keep scouting until the base is found
- **Attack decision:** attack when any profile threshold is met — army size (`AttackUnits`), army
  value, game time (`AttackTime`), or the enemy army being seen and smaller; otherwise keep units
  home and hold position
- **Attack execution:** gather idle combat units, `attack-move to` the enemy base (formations from
  Task 23 Part C spread them), re-issue when the group shrinks below `RetreatAt`, retreat to base
- **Defence:** when a friendly building takes damage or an enemy is visible inside the base radius,
  recall the army to that point and defend before resuming the attack
- No per-unit micro in v1: no focus fire, no kiting — attack-move and the existing combat rules
  do the fighting (documented as the first place to improve later)

### Part F — Reactive overrides [ ]
- Under attack → recall army, queue defenders, then resume
- Lost a building → one rebuild request, outranking new construction
- No workers left → fall back to passive income buildings, keep army production running
- Banked and supply-capped for N seconds → spend on tech/expansion instead of idling
- Lost the whole army → hold at base until `AttackUnits` is reached again

### Part G — Content, difficulty and acceptance [ ]
- `GameFiles/AiStrategies.events` shipping three profiles: **rush** (early barracks, cheap melee,
  attack at 6 units), **balanced** (economy, ranged tech, attack at 15 units or 8 minutes),
  **turtle** (defences, expansions, counter-attack after the first assault)
- Difficulty knobs: reaction delay, income multiplier, unit-cap target, aggression
  (`Ai<Difficulty>Income`, `Ai<Difficulty>Reaction`), applied through the bank and tick rate —
  no rule-bending or hidden information
- Example content: `UnitTypes.events` (the RTS unit table), `AiStrategies.events`, per-style
  `AiStarCraft.events` / `AiRedAlert.events`, plus `CommandCenter.events`, `Barracks.events`,
  `Marine.events`, `Worker.events` and `ResourceNode.events` (Task 19 Part A) so a full enemy game
  exists to test against
- Acceptance: a headless test scene runs 10 minutes of simulated ticks and asserts the brain built
  ≥1 barracks, trained units, expanded once and launched an attack; a playable scene where the
  enemy beats an idle player
- Tests: pure planners under `Tests/` (spending priority, build-order stepping, spot search,
  attack thresholds, worker assignment); the order-issuing bridge stays untested as usual

### Part H — Game style: StarCraft or Red Alert [ ]
- One global picks the flavour: `set GameStyle to "starcraft"` (default) or `"redalert"`
  (`"homeworld"` reserved for the 3D collector/RU strand in Task 19 Part C)
- New pure `Runtime/RtsStyle.cs` reads it and exposes the flags the planners ask about, so no
  manager hard-codes a game name: `IncomeModel` (`Workers` / `OreTrucks` / `Passive`),
  `PowerModel` (`None` / `Plants`), `SupplyModel` (`Depots` / `Cap` / `RUs`),
  `ProductionModel` (`PerBuildingQueue` / `GlobalSidebar`), `DefenceModel`, `TechDepth`
- **StarCraft flavour:** workers mine minerals and gas, supply depots/pylons gate units, per-
  building production and a two- or three-tier tech tree (Barracks → Factory → Starport),
  expansions on mineral patches, AI attacks in waves when `AttackUnits` and a tech timing are met
- **Red Alert flavour:** ore fields with ore trucks that drive themselves back to a refinery,
  power plants feeding `Power` (a deficit slows production — Task 19 Part B), sidebar-style base
  building, static defences (turrets, tesla coil) and superweapon timers, AI rushes with cheap
  units and forward-builds defences
- Everything in Parts A–G is shared; the style only changes which income/supply/production rules
  apply and which data file supplies the names: `GameFiles/AiStarCraft.events` and
  `GameFiles/AiRedAlert.events` (same `set` global pattern), so a game can mix a Red Alert economy
  with StarCraft units if it wants to
- Acceptance: the same brain runs both styles on the same test scene, each reaches its first
  attack inside the test window, and a test asserts the style flags (Red Alert builds a power
  plant before its first production building; StarCraft trains a worker before its first barracks)

### Open questions for this task
- Map bounds for expansion — a global `MapMinX/Z`/`MapMaxX/Z`, or the fog's known extent?
- Multiple difficulties / personalities in v1, or one balanced profile first?
- Does the human player's economy also switch with `GameStyle`, or is the style AI-only until
  Task 19's content exists? (Design intent: both, one economy per game.)
- Homeworld's 3D movement and RU collectors — a third style flag set now, or after Task 19 Part C?
- Rename the hardcoded actor conventions (`Player`, `Camera`) to globals now, or when a second
  playable scene needs different names?

---

## Task 26 — RTS Camera Controls [x]

Goal: add RTS-style free camera — mouse-wheel zoom, mouse-edge panning, and arrow-key/WASD panning — so the player can look around the 3D map.

### Part A — Mouse-wheel zoom [x]
- New action: `zoom camera by <n>` / `camera zoom in` / `camera zoom out` — adjusts camera height/depth offset (or field of view)
  → `Runtime/CameraRig.cs` (`ZoomBy`/`ZoomedIn`/`ZoomedOut`, `HeightFor`/`DepthFor`) plus the
`ZoomCamera` verb in `ActionWords`/`FlaxActions`. Positive zooms in. The zoom multiplies the
authored `CameraHeight`/`CameraDepth` by `1/zoom`, and applies to `camera follows` framings too.
- Min/max zoom clamp so the camera cannot zoom into the floor or out to infinity
  → `MinZoom` 0.25 / `MaxZoom` 4, and `HeightFor` never drops below `MinHeight` 30 (without
pushing a scene that authored a lower camera up).
- Bind `when wheel scrolls up/down` to increment/decrement zoom
  → the trigger already existed; `zoom camera by 0.1` / `-0.1` is the body. Documented in
`keywords.html` with the wheel example.

### Part B — Mouse-edge panning [x]
- When the mouse is within N pixels of a screen edge, pan the camera in that direction each tick
  → `CameraRig.EdgeDirection` + `EventsManager.UpdateCameraPan` (unit vector, diagonals at the
same speed as a straight edge). Each edge maps to the ground direction under it.
- Panning releases any active `camera follows` target while the mouse is at the edge
  → free-pan mode only exists after `camera free`, which releases every target; a follow/at/aim
verb re-enters the old mode and turns free-pan off, so the two never fight.
- Configurable edge thickness and scroll speed
  → `CameraEdgeThickness` (12) and `CameraPanSpeed` (600/s at zoom 1); `CameraEdgePan` 0 turns
edge panning off while keeping free mode.

### Part C — Arrow-key / WASD panning [x]
- `when Left/Right/Up/Down is held` pans the camera in that direction each tick
  → built into free-pan mode (no event needed) *and* available as
`when Left Arrow is held → pan camera left by 12` for authored speeds.
- WASD keys also pan the camera (camera-relative forward/back/left/right)
  → A/D/W/S read alongside the arrows. There is no camera yaw in this project, so screen forward
is world +Z and screen right +X.
- Panning speed scales with zoom level (slower when zoomed in)
  → `CameraRig.PanSpeedFor(base, zoom) = base / zoom`.

### Part D — Camera state [x]
- `my camera zoom` / `my camera position` — readable camera state for events
  → `my camera zoom` is a number, `my camera position` the pan focus as `"x, 0, z"`; both parse
as one multiword variable (EventParser) and `if my camera zoom is 2` works (Conditions).
- `camera free` releases follow and returns to free-pan mode
  → `FreeCamera` clears the targets, arms free mode, seeds the pan focus from where the camera
already is (no jump) and fills only the unset feeling globals.
- `camera follows <unit>` locks onto a selected unit (already exists; works alongside free pan)
  → unchanged, and it now scales its offset by the zoom.

### Notes
- The bridge half (mouse/keyboard reads, `UpdateCameraPan`, placing the camera) is compiled but
not unit tested; the pure rules (clamps, edge/key directions, step maths, zoom-scaled offsets)
are covered by `Tests/`. Editor play-check still pending: `camera free`, then wheel, edges and WASD.


## Task 27 — RTS Abilities & Upgrades [ ]

Goal: StarCraft-style unit abilities (stim pack, psi storm, cloak), tech upgrades and a research
queue — while the ability itself stays **authored in events**. This task only adds the plumbing
the language cannot express today.

### What events already do (so the task only fills gaps)

- **A stim pack is already a sheet**: `when Q is pressed` → `if my StimCooldown is 0` → `deduct` a
  cost → `set my Speed to 120` → `set my StimCooldown to 10` → `wait 8 seconds` → restore.
  `when every 1 seconds` ticks the cooldown; `set state stimmed` gates other triggers; sounds and
  animations exist.
- **AoE damage exists** via `explode radius 320`, which spawns the `Explosion` template and sends
  real `damage` messages inside the radius — they pass through armour/shield/health and the Task 23
  matchup gate.
- **Upgrades work** as `deduct` + `wait` on the building plus a global the unit reads on spawn
  (`when me appears`) or on change (`when UpgradeStim changes`). `my Armor`, `my Shield`,
  `my MaxHealth` and `my attack range` are all real, stat-clamped, order-respected values.
- **Command cards** are authorable: `when StimButton is pressed`, key triggers, and `bind` for
  labels/progress bars.

The gaps are specific: no click-to-target mode, weapon/tech values are global (so an upgrade buffs
both players' guns), timed buffs have no stacking/refresh/expiry model, research has no queue, and
cooldowns cost one polling trigger per unit.

### Part A — Target-pick mode (`Runtime/Targeting.cs`) [x]
- Pure `Targeting` state: caster, ability name, what it accepts (`Point` / `Unit` / `Self`), range,
  validity test, and a timeout — `TryPick(actor)`, `TryPick(x, z)`, `Cancel()`, `Tick(dt)`
  → `Runtime/Targeting.cs`: `TargetAccepts` flags + `TargetingSession` (caster, ability, accepts,
  range, timeout/remaining, a `UnitValid` delegate for matchup/fog, and the pick: `PickX/Z`,
  `PickActor`, `PickIsUnit`) plus `TargetingManager` on `EventRuntime.Targeting` with `Begin`,
  `MoveCaster`, `SessionOf`, `TryPick(caster, actor, x, z)`, `TryPick(caster, x, z)`, `Cancel`,
  `Forget`, `Clear` and `Tick(dt)` (returns the casters whose mode lapsed). One aim at a time — a
  second `aim` replaces the first. Reach is XZ from the caster; 0 means anywhere. A landed pick
  ends the waiting mode but keeps the pick readable until the next `aim` / `cancel aim`, which is
  what a trigger body needs.
- Words: `aim "psi storm"` enters the mode; `when my aim picks a target` fires with the pick, and
  `aim x` / `aim z` / `aim target` / `aim is a unit` read it; `cancel aim` leaves
  → `ActionVerb.Aim` / `CancelAim`; the optional qualifiers are `at a point` / `at a unit` /
  `at me` (default: a point or a unit) and `for <n> seconds` (default 8). `TriggerKind.AimPicked`
  (both `when my aim picks a target` and the bare spelling) is pushed to the caster's sheet only,
  with the ability in `Name` and the unit in `Other`. Values `my aim x` / `my aim z` /
  `my aim target` parse as one variable each through `EventContext.ReadMy`;
  `ConditionKind.AimIsUnit` is `my aim is a unit` (unit pick, not bare ground).
- Bridge half: while aiming, a left click raycasts the ground and the actor under the cursor
  (`EventsManager` already casts for `Mouse`), right click or Escape cancels, and an optional
  `AimMarker` actor follows the cursor. Clicks that hit UI or are out of range are ignored, not
  consumed
  → `EventsManager.TryAimPick` reuses the click raycast; a hit on an actor that owns an
  `EventSheet` is a unit pick, anything else is a point pick. A landed pick consumes the click;
  a UI hit (button still runs, aim stays armed), a missing ray or a refused pick falls through to
  the ordinary click path. Right click and Escape cancel *and* consume; Escape outside aim mode is
  still an ordinary key press. The `AimMarker` scene actor is optional (absent = no marker),
  found once and re-found after a scene load.
- Validity reuses what exists: `OrderManager.AttackRangeOf`, `Matchups.CanAttack`, fog visibility,
  `unit is <type>` conditions
  → `TargetingManager` owns the reach, and `FlaxActions.StartAim` installs a `UnitValid` rule that
  asks `FogOfWarManager.IsVisibleTo(localPlayer)` and `Matchups.CanAttackBetween(caster, target)`;
  the caster itself is always a legal self pick. Choose the kind with `at a point|a unit|me`.
- **AoE primitive for point abilities**: `explode` destroys the caster, so add
  `damage area around <spot> by <n> for radius <r>` — same damage path, same matchup gate, no
  caster required (a storm kills a group without killing the caster)
  → `ActionVerb.DamageArea`; `<spot>` is an actor, `there`, or `my aim target` / `my aim` (the pick),
  radius defaults to 150 and accepts a value (`for radius my blast`), the caster is neither
  destroyed nor spared, every actor in the radius takes a `"damage"` message from the caster (so
  armour → shield → health and the matchup gate apply), bullets are skipped so splash never chains,
  and there is no visual/sound of its own — the sheet plays those.
- Default: no aiming mode unless an event starts one, so old scenes are untouched
  → yes: nothing polls unless `Targeting` has a session, the marker is optional, and `DamageArea`
  only runs where a sheet writes it.

#### Part A notes
- 34 new pure checks (mask, reach, validity delegate, timeout, cancel/replace, parser shapes for
  `aim` / `cancel aim` / `damage area`, trigger + condition + value parsing, the push routing to
  the caster's sheet only) -> **1626 passed, 0 failed**; Game module build 0 errors.
- The bridge half (raycast pick, click/Escape consumption, marker, the damage loop over live
  actors) has no unit tests, as usual; editor play-check pending (assign an ability key, `aim`,
  click a point and a unit, right click out).

### Part B — Per-player tech and weapon values (`Runtime/Tech.cs`) [x]
- Upgrade levels are per player: `Player2 UpgradeStim is 1`, set by research or events;
  `Tech.LevelOf(variables, player, upgrade)` reads them and defaults to 0
- `WeaponData.Effective(variables, player, weapon, field)` = base stat × per-player multiplier +
  per-player bonus (`Player2 PistolDamage`, `Player2 PistolDamageBonusMultiplier`), so an upgrade
  to *your* marines no longer buffs the enemy's guns
- The acting unit's player comes from `Fog.PlayerOf` (falling back to `my Player`), the same rule
  the minimap and banks use
- Unit-local stats stay as they are — speed, armour, range and shields are already per unit; tech
  that touches them is a `set` line in the upgrade's event
- No change to `WeaponData.events`: it keeps the base numbers, the player-scoped values sit on top
  → `Runtime/Tech.cs`: `PlayerPrefix`/`VariableName` (`VariableName(1, "UpgradeStim")` →
  `"Player2 UpgradeStim"`), `WeaponBonusName`/`WeaponMultiplierName`, `PlayerIndexOf(context)`
  (fog first, then `my Player`, then 0), `LevelOf`/`SetLevel` (negative levels read as 0),
  `Scale(base, …)` = `base × multiplier + bonus` with a missing multiplier 1 and bonus 0, and
  `TryOverride` for text/flag fields. Player indexing follows the minimap: fog index 0 is `Player1`,
  index 1 is `Player2`, the same numbering `Player1 Score` already used. Names are looked up through
  `FindGlobalName`, so `player2 upgradestim` still reaches the right global.
  → `WeaponData.Effective(variables, player, weapon, field)` returns the effective `ValueBox`:
  numeric fields (`Damage`, `FireRate`, `Spread`, `Multiplier`, `Reload`, `AmmoPerShot`, `Ammo`,
  `Speed`, `Knockback`, `Life`, `Blast`) take the formula, `Bullet`/`Sound`/`Smoke`/`Bounce`/
  `Explosive`/`Pierce` take a per-player override or the base. `ForActor` applies that scope, so
  `spawn Bullet` stamps the shooter's own `damage` and `reload` uses the holder's reload; `TryGet`
  stays base-only, so table reads and the unknown-weapon help are unchanged.
- Words that now parse: `set Player2 UpgradeStim to 1`, the run-together `Player2UpgradeStim`, and
  `if Player2 UpgradeStim is 1` — `EventParser.ReadVariable` and `Conditions.Parse` join a
  `<PlayerN>` plus a capitalised name into one global, after the `Players`/`PlayerInput` joins so
  `Player1 Score` and `Player1 ShootKey` keep their own spellings.

#### Part B notes
- 22 new pure checks (level defaults/isolation/`SetLevel`/negative clamp, the spaced and run-together
  parser joins, the condition read, `Effective` base vs multiplier vs bonus vs both, text override,
  per-player isolation, `WeaponData.events` unchanged, `PlayerIndexOf` through the fog and the
  `my Player` fallback, and `ForActor` stamping 15 for a player-2 marine with +5) ->
  **1648 passed, 0 failed**; Game module build 0 errors (`Flax.Build -build -arch=x64
  -platform=Windows -configuration=Debug -target=GameEditorTarget`).
- No new bridge code beyond the `ForActor`/`reload` call sites; editor play-check pending (give a
  marine `Player2 PistolDamage 5` and confirm its bullets hit for 15 while the other side hits for 10).

### Part C — Buffs and debuffs (`Runtime/Buffs.cs`) [x]
- One buff: name, duration, stack rule (`refresh` / `stack` / `ignore`), modifiers, source actor,
  expiry; a single `Tick(dt)` expires everything and restores stats
- Words: `add buff "stim" to me for 8 seconds`, `remove buff "stim" from me`; triggers
  `when buff "stim" starts` / `... ends`; value `my "stim" time left`
- Base-value capture: the first buff records `my BaseSpeed` / `my BaseArmor` and the manager writes
  the aggregate, so overlapping buffs restore correctly instead of fighting each other
- Default: a sheet that never adds a buff keeps writing `my Speed` by hand exactly as today
  → `Runtime/Buffs.cs`: `BuffRule` (`Refresh` default / `Stack` / `Ignore`), `BuffInstance`
  (name, duration, remaining, rule, source, modifiers) and `BuffModifier` (`Stat` + `Amount` or
  `Multiplier`). The `Buffs` manager holds the running copies per actor, captures a stat's base the
  first time a buff touches it (writing `my Base<Stat>` too, and remembering a sheet's own
  `Base<Stat>`), writes `my <Stat> = base × Π multipliers + Σ amounts` on every change, and puts
  the base back when the last buff on that stat ends (dropping `Base<Stat>`, and removing the stat
  again if the actor never had it). Health stats go through `HealthManager.TryStat`, so
  `my Max Health` buffs `my MaxHealth`. `Tick(dt)` returns the `(actor, name, source)` ends so the
  runtime pushes them; `Clear` (death) and `Reset` (scene) restore everything.
- Words: `add buff "<name>" [to <target>] for <n> seconds [refresh|stack|ignore]
  [adding <value> to my <stat>] [multiplying my <stat> by <value>]` (clauses chain with `and`,
  the target defaults to the acting sheet) and `remove buff "<name>" [from <target>]` →
  `ActionVerb.AddBuff` / `RemoveBuff`, with `EventParser.ParseAdd`/`ParseRemove` routing the two
  shapes to raw actions before the `add <value> to <var>` / `remove … from my bag` statements.
  `BuffCommands.ExecuteAdd/ExecuteRemove` resolve the target (`me` / `other` / a named actor),
  evaluate the modifier values, apply the rule and raise the pushes. A refresh reports no new
  start, so re-pressing stim doesn't re-fire `when buff "stim" starts`.
  → `TriggerKind.BuffStarts` / `BuffEnds` from `when buff "<name>" starts|ends`, pushed to the
  buffed actor's sheet with the buff name matched and the source in `other`; the ends are raised in
  `EventRuntime.Tick` before the drain, so the event runs in the tick the timer ran out.
  `my "<name>" time left` parses as one variable and reads the longest remaining copy.
- `WordRegistry` rows for `add buff`, `remove buff`, both triggers and the value.

#### Part C notes
- 33 new pure checks (start/base capture, refresh vs stack vs ignore, overlapping add+multiply,
  independent stats and partial removal, canonical `Max Health` buffing, expiry restoring every
  stat and dropping `Base<Stat>`, no double-reporting, a sheet's own `BaseSpeed` restored, `Clear`,
  an unbuffed sheet left untouched, the add/remove parse shapes with defaults/rules/chained
  clauses, the missing-duration error, both triggers, the value form, an end-to-end run where
  `add buff` buffs a named actor and both pushes land on its own sheet with `other` as the source,
  and a cleared actor that is never ticked or rewritten again)
  -> **1681 passed, 0 failed**; Game module build 0 errors (`Flax.Build -build -arch=x64
  -platform=Windows -configuration=Debug -target=GameEditorTarget`).
- Bridge half: `FlaxActions` gets only the two `case` labels delegating to `BuffCommands`; editor
  play-check pending (press stim, watch `my Speed` jump and come back, and `when buff "stim" ends`
  fire once).

### Part D — Research queue (`Runtime/ResearchManager.cs`) [x]
- Per-building queue mirroring `ProductionManager`: `Start`, `Cancel`, `ProgressOf`, `Remaining`,
  `Tick`, costs through `ResourceManager`, and `when research starts` / `finishes` / `queue is empty`
- Words: `research "Stim Pack" at Barracks`, `cancel research "Stim Pack"`
- Upgrades are data: `GameFiles/Upgrades.events` (`set StimPackTime to 30`, `set StimPackCost to
  100`, `set StimPackRequires to "Barracks"`), read at boot like `Matchups.events`
- Why a manager rather than a `wait` in a sheet: cancel with refund, progress bars via `bind`, and
  a queue that survives the building being selected away from

#### Part D notes
- `Runtime/ResearchManager.cs` holds both halves. `Upgrades` (static) reads `Upgrades.events` the
  `Matchups` way — one row per block of `set <Upgrade><Field> to ...` literals, suffixes `Time`,
  `Cost`, `Requires`, `Levels`/`MaxLevel`/`Level` (a name that ends in none of them is ignored).
  `KeyOf` strips whitespace, so `"Stim Pack"` and `StimPack` reach one row and one level global
  (`Player2 StimPack`). No file (or a reload with none) leaves every scene exactly as before.
- `ResearchManager` is `ProductionManager`'s shape: queues keyed by the building actor, `Enqueue`
  raises `ResearchStarted` immediately, `Tick(runtime, time)` raises `ResearchFinished` → next
  `ResearchStarted` → `ResearchQueueEmpty`, all before the push drain so the event runs that tick.
  `ProgressOf` is 0–100 like `ConstructionManager.ProgressOf`, `Remaining` is seconds, and the
  manager writes `my ResearchProgress` / `my ResearchName` on the building for a `bind progress`
  card. `Destroyed` drops the queue; `Reset` clears every queue.
- Costs are charged at enqueue, so a cancel can refund and the bank cannot be spent twice. Prices
  come from live `<Upgrade><Resource>Cost` globals plus the file's declared `Cost` for the default
  resource. `ResourceManager` gained `CostsOf` (a name's cost globals) and `BankName` /
  `BankValue` / `WriteBank`: a research order spends its **owner's** bank — `Player2 Minerals`
  when the game keeps per-player banks, otherwise the bare global, so `Minerals` stays player 1's
  alias and existing single-player economy files are untouched. The owner comes from the fog
  entry for the building, falling back to `my Player`. `cancel` refunds exactly what was paid,
  scaled by `ResearchRefund` (1 by default), and a destroyed building keeps its loss.
- Finishing applies the upgrade through Part B: `Tech.SetLevel` raises `Player<N> <Upgrade>` for
  the building's owner only, capped by the row's `Levels` (1 when nobody says, 0 = unlimited).
  `StimPackRequires` refuses research at a different building; a duplicate in the queue, an
  unaffordable price, or a level already at the cap is refused without charging, and each refusal
  gets one bridge warning.
- Words: `research "<upgrade>" [at <building>]` reads a quoted name like `cast` (a two-word
  upgrade stays one name; an unquoted single word also parses) and `cancel research "<upgrade>"
  [at <building>]` joins the `cancel` family. Verbs `Research` / `CancelResearch` route through
  `FlaxActions` to the manager, reusing `ResolveProductionBuilding` and the building's own name.
- Triggers: `when <building> starts|finishes researching "<upgrade>"`, the bare `when research
  starts` / `when research finishes`, and `when research queue is empty` / `when <building>
  research queue is empty` (also `my`). `EventRuntime.Matches` mirrors the production branch: the
  upgrade in `Name`, the building actor in `Other`, its template in `Material`, the owner in
  `PlayerA`. The registry rows use `<name>` for the quoted upgrade so trigger validation accepts
  it, and `keywords.html` documents the words.
- `GameFiles/Upgrades.events` ships Stim Pack, Psi Storm and three-level infantry weapon/armour
  rows; `EventsManager` loads it at boot and on reload next to `Matchups`.
- 46 new pure checks (both trigger shapes incl. quoted names and queue edges, both action parses,
  the data loader's suffixes/reset, duration precedence, charge-at-start, progress/remaining,
  started/finished/empty pushes on the right sheets, level landing on the right player, full and
  half refunds, per-player banks, every refusal, queue promotion, destroyed queue, the untouched
  default, and the registry rows) -> **1727 passed, 0 failed**; `Source/Game.csproj` builds with
  0 warnings and 0 errors (`dotnet build Source/Game.csproj -c Debug`; `Flax.Build` printed only
  its SDK notices here).
- Bridge half (real actor resolution, live charges, the bank's fog lookup) has no unit tests, as
  usual; editor play-check pending: `research "Stim Pack" at Barracks` with 100 minerals, watch
  `my ResearchProgress` rise, `cancel research "Stim Pack"` refund, then finish and see
  `Player1 StimPack` reach 1 while the other player stays at 0.

### Part E — Shared cooldowns (`Runtime/Cooldowns.cs`) [x]
- Per-actor named timers advanced in one loop: `Ready(actor, name)`, `Start(actor, name, seconds)`,
  `Remaining(actor, name)`, `Clear(actor)` on death
- Words: `put "stim" on cooldown for 10 seconds`, condition `my "stim" is ready`, value
  `my "stim" cooldown`
- Replaces the per-unit `when every 1 seconds` polling; the old polling keeps working, so nothing
  is taken away

#### Part E notes
- `Runtime/Cooldowns.cs` stores seconds-remaining per actor per name (`actor → name → seconds`,
  names case-insensitive), so `Tick(dt)` walks every timer once, drops the expired ones and never
  touches an absolute clock. `Start` refreshes rather than stacks; `seconds <= 0` clears the timer,
  so `for 0 seconds` reads ready immediately; `Remaining` returns 0 for an unknown or expired
  timer and `Ready` is `Remaining <= 0`. `Count`, `Forget`, `Clear(actor)` and `Reset` round it
  out. No Flax types.
- `EventRuntime.Tick` advances the whole table once between the buff expiry and the sound
  expiry, with the same one-second hitched-frame clamp the buffs use, so a long pause cannot
  drain every ability at once. `SceneStarted` calls `Cooldowns.Reset()`; `FlaxActions`'
  `ForgetSubject` calls `Cooldowns.Clear(actor)` beside the spell/buff/targeting clears, so a
  destroyed unit (or a clone that reuses its name) starts ready.
- The value `my "stim" cooldown` is parsed like `my "stim" time left`: `EventParser.ReadVariable`
  joins the quoted name into `cooldown stim` (`Cooldowns.VariableName`) and `EventContext.ReadMy`
  answers it from the manager before ordinary locals, so a HUD bind or a `set` line reads live
  seconds without mirroring a countdown.
- The condition `my "stim" is ready` is `ConditionKind.CooldownReady`, with `is not ready`
  setting `Negated`; `Conditions.Evaluate` asks the manager for the acting actor. It sits before
  the generic `<var> is <value>` handling, so a quoted name is never mistaken for a local
  variable.
- The action `put "stim" on cooldown for <n> seconds` is `ActionVerb.PutOnCooldown`, read by a
  new `put` case in `ActionWords` (quoted name like `cast`, optional trailing `seconds`).
  `FlaxActions.PutOnCooldown` starts it on the acting actor and warns once when run from the
  global sheet, which has no body to cool down.
- `WordRegistry` and `keywords.html` document the action, condition and value; the registry
  examples are parsed by the existing self-test.
- The old style is untouched: `when every 1 seconds` still fires (a check asserts it alongside a
  live shared timer), so no existing sheet loses its hand-rolled cooldown.
- 29 new pure checks (start/read/tick/expiry arithmetic, refresh instead of stacking, zero and
  negative durations, independent names and actors, one-loop expiry, `Clear` sparing other
  actors, `Reset`, the `cooldown <name>` mapping, the action/condition/value parses, the
  missing-duration help, the runtime `ReadMy` readout, both condition polarities across ticks,
  the surviving `when every` poll, and the registry rows) -> **1756 passed, 0 failed**;
  `Source/Game.csproj` builds with 0 warnings and 0 errors.
- Bridge half (the actor the word runs on, the death clear) has no unit tests, as usual; editor
  play-check pending: bind a key to `put "stim" on cooldown for 10 seconds`, gate the ability on
  `my "stim" is ready`, and confirm the value counts down and a destroyed unit comes back ready.

### Part F — Content, words, tests, acceptance [x]
- Example content: `Marine.events` stim (cost, cooldown, buff, sound), `HighTemplar.events` psi storm
  (aim → `damage area` at the pick), `Barracks.events` research, `Upgrades.events`, plus a command
  card using `bind` for cooldown and research progress
- `WordRegistry` rows and `keywords.html` rows for every new word
- Tests (pure): targeting validity/range/expiry/cancel, tech multipliers and per-player isolation,
  buff refresh vs stack vs ignore and expiry restoring base, research order/cancel/refund,
  cooldown arithmetic
- Acceptance: a marine stims (faster for 8 s, on cooldown after), a caster storms a clicked point
  and damages only valid targets, a finished research upgrades existing and future units for one
  player only, and a base game that uses none of these words behaves exactly as before

#### Part F notes
- Three example sheets ship in `GameFiles/`:
  - `Marine.events` — land/ranged stats, `my Mana`, then `when Q is pressed` gated on
    `my "stim" is ready` and the cost: `deduct my StimCost from my Mana`, an 8 s `stim` buff
    adding 40 Speed, `put "stim" on cooldown for 10 seconds`, one sound, and
    `bind StimLabel to my "stim" cooldown` for a card label. The buff restores Speed itself and
    the cooldown ends itself, so a second press during either is refused by the gate.
  - `Barracks.events` — `train Marine` behind `can afford Marine`, `research "Stim Pack"`
    (`StimPackRequires` confines it here, `StimPackCost` prices it), `when research finishes`,
    `when production queue is empty`, and `bind progress of ResearchBar to my ResearchProgress` /
    `bind ResearchLabel to my ResearchName` so a card shows the 0–100 progress the manager writes.
    The research press has **no** `can afford StimPack` gate: `Upgrades.events` values live in the
    loader, not in globals, so the manager checks the bank and warns — the gate would always fail.
  - `HighTemplar.events` — `aim "psi storm" at a point`, `put "psi storm" on cooldown for 15 s`,
    and `when my aim picks a target → damage area around my aim by 30 for radius 180` plus the
    particle/sound; `bind StormLabel` reads the cooldown. `Upgrades.events` already shipped with
    Part D.
- No new words were needed: every word in the content was registered and documented in Parts
  A–E, so the `WordRegistry` / `keywords.html` requirement is already met (and the registry
  example self-test stays green).
- Tests: 20 new pure checks — the three files parse, the stim buff/cooldown/research/aim/
  damage-area specs read back with their numbers, `Upgrades.events` ties Stim Pack to Barracks at
  100, a headless Marine run (Q spends 25 mana, Speed 90 → 130, a second Q during the cooldown
  spends nothing, Speed is back at tick 9, the cooldown is ready at tick 11), and a headless
  Barracks run (R queues research and charges 100, `Player1 StimPack` reaches 1 while
  `Player2 StimPack` stays unset) -> **1776 passed, 0 failed**; `Source/Game.csproj` builds with
  0 warnings and 0 errors.
- The editor/scene playtest stays deferred with the other play-checks (Task 20): the sheet steps
  are proved headless, but the command-card controls (`StimLabel`, `StormLabel`, `ResearchBar`,
  `ResearchLabel`), the click that resolves `aim`, the particle/sound assets and the selection
  rings are scene wiring. The full unit table, attack and typed combat stay in Task 19 Part A —
  these three sheets are the Task 27 examples, not the finished RTS content.

### Open questions for this task
- Ability definitions in data (`Abilities.events`: cost, cooldown, range, target kind) so the AI and
  a generated command card can read them — or is one authored card per unit enough?
- Aim marker: reuse the `SelectionRing` actor convention, or a new `AimMarker` actor?
- Buffs on buildings and buildings under research (production speed modifiers) in v1 or later?
- Does a generated ability hotbar from `UnitTypes.events` + `Abilities.events` belong here or in the
  UI work of Task 19?

---

## Open Questions (carried from Docs/rts-event-manager-support.md)

1. ~~Grid-based vs. free-form unit placement for buildings?~~ → Both: free-form by default, `on grid` opt-in via `place … on grid`; grid cell size from `BuildGridSize`.
2. How many players (1v1, FFA, teams)?
3. Supply cap mechanics — hard limit or soft warning?
4. Fog of war: explored areas stay visible or revert to fog?
5. Control groups: 1–0 keys or click-assign?
6. Homeworld 3D movement: full 3D or 2.5D plane?
7. Red Alert power system: power deficit slows production?
