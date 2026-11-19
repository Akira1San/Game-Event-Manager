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

## Task 28 — Air units, hover height and lift-off [x]

Goal: StarCraft-style flight on a **2.5D plane** (the answer to open question 6): units and
buildings keep a height but still move over the XZ plane, so nothing needs full 3D pathfinding.
Task 22 already classifies a unit as `land` / `air` / `sea` for terrain passability and Task 23
already has land-vs-air matchup rows, but an `air` unit today walks the ground NavMesh at its
scene Y, and a building can never leave the ground.

### What events already do (so the task only fills gaps)

- **Unit type**: `set my unit type to "air"`, read back with `me is air`; air crosses land, water
  and marked void (`UnitTypes.CanCross`), and `place` refuses a building on ground its type cannot
  cross.
- **Vertical motion of a kind**: `jump` (character controller, dynamic body, or a scripted arc),
  `me is on ground` / `me is airborne`, `climb` for ladders and `platform` for lifts. All are
  momentary or fixed-path — none holds an altitude.
- **Ground movement**: `goto` / `move` / orders / formations all path through `Navigation.FindPath`
  and every proximity test uses `Distance2D`, i.e. XZ.

The gaps are specific: no altitude a unit keeps, no air path that skips the NavMesh, no lift-off /
land for buildings, and no targeting gate that refuses an air target for a land-only weapon.

### Part A — Altitude (`my Altitude`) [x]
- Per-unit height above the point it was placed at: `set my Altitude to 120`, read as `my Altitude`
  (and `my altitude`); a unit that never wrote one stays at 0, so existing sheets are untouched
- `goto` / `move` / formations keep the altitude instead of copying the destination's Y; `jump`
  and `platform` still own their own vertical motion
- Landing sets it back to 0 and snaps onto the ground under the unit

#### Part A notes
- `Runtime/Altitude.cs` is the pure half: `IsVariableName` (both spellings), `Of` (0 when unwritten,
  negative/unreadable clamps to 0), `IsAirborne`, and `GroundY` / `PinY` for the two conversions.
  No new word was needed — the altitude is an ordinary per-unit variable.
- `EventContext.ReadMy` answers any altitude spelling from the manager before ordinary locals, so a
  sheet that writes `my altitude` and a HUD that reads `my Altitude` see one value (locals are
  otherwise case-sensitive; without this the two spellings would drift apart).
- Bridge (`Bridge/FlaxActions.cs`): `StepAltitude` runs once per tick, walking the sheet list and
  for each actor with an altitude above 0 pinning `Position.Y` to `ground + altitude`, remembering
  the ground the first time; when a sheet writes the altitude back to 0 the unit is put down on
  that ground and leaves the table (dead actors are dropped too). Actors with no altitude are not
  touched at all, so `jump`, `platform`, characters and rigid bodies keep owning their Y.
- `MoveSubject` flattens `direction.Y` for a flying unit, and `ArriveAt` rebases the ground to the
  arrival point and re-pins the height, so `goto` slides a flyer along the plane and never drops it
  to a navmesh waypoint's Y. Characters with a `CharacterController` (or dynamic bodies) fight the
  pin and should not carry an altitude — air units belong on the transform/kinematic mode.
- Tests: 8 new pure checks (default ground, both spellings, negative clamp, ground/pinned Y round
  trip, name check, and a headless sheet that writes `my altitude` and reads `my Altitude` back plus
  the `my Altitude is higher than 0` gate) -> **1787 passed, 0 failed**; `Source/Game.csproj` builds
  with 0 warnings and 0 errors. The pin/land movement itself is bridge code with no unit tests, as
  usual; editor play-check pending (scene air actor with `my Altitude`, `goto` somewhere, then
  `set my Altitude to 0` and confirm it settles on the ground).

### Part B — Air path (`me is air` skips the NavMesh) [x]
- An air unit flies a straight XZ line at its altitude (no `FindPath`), ignoring terrain kinds,
  props and buildings, and stops at its own stop distance
- Path-blocked reporting does not fire for air units; a `goto` to an actor aims at that actor's
  altitude, so air-versus-air chases work
- Optional `fly to <spot> at <height>` so one line both moves and changes altitude

#### Part B notes
- `Runtime/AirPath.cs` is the pure half: `IsFlying` (the unit is type `air`, or it holds an
  `my Altitude` above 0 — which is what lets a lifted, still land-typed building fly),
  `ReportsBlocked` (a flyer has no route to lose, so it never raises `when path blocked`), and
  `ChaseAltitude` / `ChaseAltitudeFor` (a flyer copies a flying target's height and keeps its own
  over anything grounded). No Flax types, so the tests drive it directly.
- Bridge (`Bridge/FlaxActions.cs`): `IsFlyingActor` / `DirectRoute` / `RememberDirectMove` turn a
  flying move into one waypoint at the unit's own ground, in transform mode. Both entry points
  use it — the `goto there` branch, the `goto <actor>` branch and `BeginOrderMove` (orders,
  `attack-move`, `attack` stop-distance, rally) — so a flyer never calls `Navigation.FindPath`,
  never runs `StepOntoNavMesh` and never reports a blocked path. Formations still apply, because
  a flyer's group order goes through the same `FormationSlot`.
- `MoveSubject` moves a flyer along XZ and returns before the terrain gate and the collider
  probes, so it crosses water marks, walls and buildings; the `Step` waypoint check skips the
  terrain gate for flyers too (that one matters for a lifted land building, whose type still
  refuses water). `ArriveAt` needed no change: a direct move's destination Y is the unit's ground,
  so the existing re-pin keeps its height.
- `ChaseAltitude` seeds `AltitudeGrounds` from the current Y before writing the new `my Altitude`,
  so an air-versus-air chase climbs from the ground it is over instead of jumping off it.
- New word `fly to <spot> at <height>` → `ActionVerb.Fly` / `FlaxActions.FlyTo`: writes
  `my Altitude` (either spelling reads it back), pins the unit or puts it down, then flies the
  same direct line. `at 0` lands where the unit stands and covers the rest on the ground, the spot
  is an actor or `there`, and the height accepts a value (`at my CruiseHeight`). A line without a
  height warns with the shape. `WordRegistry` and `keywords.html` rows added.
- 11 new pure checks (flyer by type, by altitude, and at altitude 0, `ReportsBlocked`, the chase
  height in every direction plus the variables-driven read, both `fly` parse shapes, the
  missing-height help and the registry row) -> **1798 passed, 0 failed**; the module builds with 0
  errors (`Flax.Build -build -arch=x64 -platform=Windows -configuration=Debug
  -target=GameEditorTarget`), and the resulting `Game.CSharp.dll` contains the new `AirPath` and
  `FlyTo` symbols. Note `Source/Game.csproj` is stale (its explicit file list predates several
  runtime files), so `dotnet build Source/Game.csproj` is not the module check — Flax.Build is.
- The bridge half (direct route, collision bypass, altitude seeding) has no unit tests, as usual;
  editor play-check pending (an air actor flies over a wall and marked water, `fly to ... at 0`
  sets it down, and a flyer chasing a flyer meets at the target's height).

### Part C — Lift-off and land (buildings) [x]
- `lift off` on a building sheet raises it to its `my Altitude`, marks it airborne and pauses its
  production/research queues; `land` puts it down, re-checks terrain for its own unit type
  (refusing water for a barracks, marking `when path blocked`-style feedback) and resumes work
- `when <building> lifts off` / `when <building> lands` triggers and `me is airborne` for gates
  (no training while flying, no new construction on a flying building)
- A flying building is selectable and movable with the same orders; its footprint frees the build
  grid while airborne and re-reserves it on landing

#### Part C notes
- `Runtime/LiftOff.cs` is the pure half: `LandResult` (`Landed` / `NotFlying` / `GroundRefused` /
  `GridRefused`), one `LiftOffState` per building (template, height, whether it held grid cells)
  and `ResolveHeight` — the number on the line, else the `LiftOffHeight` global, else 200.
  `TryLift` writes `my Altitude` (Part A pins it), frees the grid footprint, pauses production and
  research and raises `LiftOff`; `TryLand` takes the ground and grid answers as booleans, so a
  refusal changes nothing and just raises `when path blocked` with the name `land` on the
  building's own sheet, while a landing zeroes the altitude, resumes both queues and raises
  `Lands`. An actor that only holds an altitude (`fly to … at …` on a building) can land too: it
  has no paused work to resume.
- Pausing lives in `ProductionManager` / `ResearchManager` as a set of paused buildings, not as a
  queue field: `SetPaused` never invents a queue, so `me has build queue` and `queue count` keep
  reporting what the building actually queued. `Tick` skips a paused building (orders and elapsed
  time survive untouched), and `Reset` / `Destroyed` forget the flag.
- `ConstructionManager` gained `CanReserveCells` (the footprint loop without reserving, now what
  `TryReserveCells` checks first) and `OwnsCells`, which is what lets a free-form building land
  without suddenly reserving cells it never had. Cells the same actor already holds count as free
  in that check, so a building that kept its footprint — raised by `fly to … at …` rather than
  `lift off` — lands on it instead of getting a false `GridRefused`; `LandBuilding` now dry-runs
  the grid for any building that still owns cells, drops them and re-takes the ones under where
  it touches down — using the template name from the placement table when there is no lift-off
  state, so a 2x2 building raised by `fly to … at …` re-takes all four cells, not a bare 1x1.
- Bridge (`Bridge/FlaxActions.cs`): `LiftOffBuilding` seeds `AltitudeGrounds` from the building's
  own Y before the height changes and uses an authored `my Altitude` as the rise height (else the
  global/default); `LandBuilding` tests the ground with `Terrain.IsPassable`, **not** `CanStep` —
  whose escape rule would wave a land building standing over water through — dry-runs the
  footprint with `CanReserveCells`, warns once per refusal and re-reserves on a successful
  landing. `ForgetSubject` drops a destroyed building's lift state with the other per-actor
  tables, so a clone never inherits "airborne".
- Triggers `TriggerKind.LiftOff` / `Lands` classify from `when <building> lifts off` and
  `when <building> lands` (plus the bare `when lift off` / `when land` forms, accepted because the
  registry rows give the parser their shapes) and join `IsBuildingKind`, so `me`/`my` mean the
  building's own sheet and any other name matches the template, exactly like `is placed`.
  `me is airborne` now also answers true for any actor holding an altitude, and `me is on ground`
  is its mirror — that pair is the gate for "no training / no new construction while flying".
- 39 new pure checks (height precedence, the grid dry run and `OwnsCells`, lift writing the
  altitude and freeing cells, double-lift refusal, the lift trigger, a paused queue keeping its
  remaining time and resuming, both landing refusals with the `path blocked` push, the successful
  landing and its trigger, a building that kept its cells landing on its own footprint, a 2x2 one
  landing whole, both
  trigger classifications incl. the bare forms, both action parses, the bad-shape help, the
  registry rows, and `me is airborne` / `me is on ground` for an altitude-holding building) ->
  **1837 passed, 0 failed**; `Flax.Build -build -arch=x64
  -platform=Windows -configuration=Debug -target=GameEditorTarget` reported 0 errors and the
  rebuilt `Game.CSharp.dll` contains `LiftOffBuilding` / `LandResult`.
- The bridge half (ground/grid answers, the altitude seeding, warn-once messages) has no unit
  tests, as usual; editor play-check pending (lift a barracks with `my Altitude 200`, watch its
  `train` queue stop and its grid cell free, `land` it over water and see the refusal, then over
  land and see the queue resume). The flying-building example sheet is still Part E.

### Part D — Air targeting gate [x]
- `target is air` / `target is land` condition and a `can attack air` / `can attack ground` gate
  that consults the Task 23 `Matchups` rows, so an attack order against an unreachable type is
  refused with one warning instead of firing
- Ground-only units keep their old behaviour in scenes that never wrote a matchup row
- **Part D notes.** `Runtime/AirTargeting.cs` holds the whole answer: `Canonical` reads `ground`
  as the land type, `TargetOf` resolves the `target` pronoun as the active order's target first
  and the actor whose event is running (`other`) second, `IsType` / `CanAttackType` read
  `my unit type` through `UnitTypes`, and `Refusal` phrases the warning and names the
  `<Mine>CanAttack<Theirs>` global that would allow the pair. `target` also joined the pronoun
  list in `Conditions.FindTarget`, so `me can attack target`, `me is in range of target` and the
  other thing-naming conditions resolve it. `me can attack air` is parsed before the generic
  `me can attack <thing>` branch, which would otherwise look for a scene actor called "air".
  The gate itself lives in `StartAttack` only: a forbidden pair warns once and returns before
  the order exists, so a ground unit never walks across the map to swing at a flying one, while
  `attack-move` is deliberately untouched and still travels. No new matchup data — the default
  table (land and sea cannot reach air) is what fires the refusal, and `LandCanAttackAir`
  opens both the condition and the order.
- **Part D follow-up — altitude counts as air.** The gate does not read `my unit type` alone:
  `AirTargeting.EffectiveTypeOf` reports `air` for any unit holding an altitude above 0, so a
  building lifted with `lift off` (Part C) stops being a ground target the moment it leaves the
  floor and becomes one again when it lands. `Matchups.CanAttackBetween` asks that same helper,
  so `me can attack other`, `me can attack target`, the `attack` refusal and the runtime's
  damage drop all agree — a ground army cannot hit a lifted building by any of those paths.

### Part E — Content, words, tests, acceptance [x]
- Example content: an air unit (`Observer.events` or a Wraith-style sheet) that hovers, crosses
  water and marked void, and a flying building example extending `Barracks.events`
- `WordRegistry` rows and `keywords.html` rows for every new word; pure tests for altitude
  retention, air path, lift-off/land state, terrain refusal on landing, queue pausing and the
  targeting gate
- Acceptance: an air unit hovers and flies over a wall and a lake, then lands on ground; a
  barracks lifts off, moves, is refused over water and lands; a marine refuses an air target while
  an anti-air unit engages it; a scene that writes none of the new words behaves exactly as before

#### Part E notes
- **`Observer.events`** is the air unit. `when me appears` writes `my unit type` `air`, its stats,
  and `my CruiseHeight` 140 copied into `my Altitude`, so the spawn height and every later flight
  height are one authored number. `F` runs `fly to there at my CruiseHeight` (one straight XZ
  line, no NavMesh, no terrain gate), `G` runs `land` and `when me lands` prints, and
  `when my Health becomes 0` destroys it. The header names it as the anti-air half of the
  walkthrough: air-versus-air is allowed by `AirCanAttackAir`, while a Marine's `attack` on it is
  refused by `LandCanAttackAir` being false — the default table, no new rows.
- **`Barracks.events`** became the flying building. `when me appears` already set `unit type`
  `land`, so it is a genuine land-typed building that leaves the ground: `L` sets `my Altitude`
  to 200 and runs `lift off`, `K` runs `land`, and `when me lifts off` / `when me lands` print.
  The `train Marine` press now starts with `unless my Altitude is higher than 0`, which is the
  flying gate; it reads the altitude rather than `me is airborne` on purpose, so a building
  standing on its own ground never depends on a ground probe (the airborne condition's second
  half is `!World.IsOnGround`) and training cannot be blocked by a static model's collider.
- **Words**: `WordRegistry` already had `lift`, `land`, `fly` and `target is air` from Parts B–D, but
  `keywords.html` had the lift-off trigger row and no *action* rows for `lift off` / `land`. Both
  rows were added next to `fly to`, describing the height precedence, the paused queues, the freed
  grid footprint, the two landing refusals, and the altitude-counts-as-air rule.
- **Tests** (8 new checks, 1870 total): `Observer.events` parses into 5 events and its `fly` spec
  carries a height; running the sheet headlessly on a `FakeActor` leaves an `air` unit hovering at
  140 with `AirPath.IsFlying` true; it can itself engage aircraft; a Marine's `attack` on it is
  refused naming `LandCanAttackAir`; a unit that writes none of the new words stays a plain
  ground unit; and the extended `Barracks.events` parses with a `lift off` and a `land` spec.
  The earlier parts' pure checks already cover altitude retention, the air path, lift/land state,
  terrain refusal, queue pausing and the gate, so Part E adds the content that uses them rather
  than repeating them.
- **Acceptance (editor, not run here — the movement/lift half is bridge code with no unit tests):**
  1. `Observer` on a unit with `Observer` as its Event File: it spawns ~140 above the floor with
     a ring, `F` flies it over a wall and a water cell marked with `mark water …`, and `G` puts
     it down on the ground underneath.
  2. `Barracks` with a `train Marine` queued: `L` raises it 200 up, the progress bar stops, its
     grid cell turns free, `K` over marked water warns `that spot is water` and keeps it flying,
     and `K` over land lands it and resumes the queue.
  3. A `Marine` ordered to `attack` the airborne `Observer` warns once naming `LandCanAttackAir`
     and never walks to it; the `Observer` ordered to `attack` the Marine engages.
  4. A scene with none of the new words (any pre-Task-28 sheet) behaves exactly as before: no
     altitude, `me is airborne` unchanged, land-versus-land matchups unchanged.

---

## Task 29 — StarCraft-style mouse input & command card [ ]

Goal: make the RTS playable with the mouse the way StarCraft is — click or drag to select,
right-click to order, and a command card whose icon buttons train, build and cast for whatever is
selected — without adding a single hard-coded UI or unit rule to the C# runtime. This is the
implementation half of Task 20 Part A: that part's acceptance check is "select units via click /
box-select, then issue orders", and today none of it is authored.

### What events already do (so the task only fills gaps)

- **Click detection**: `EventsManager` raycasts the scene (`Physics.LineCastAll`, UI first, then the
  nearest 3D hit) and broadcasts the click *by name* — the clicked actor's own `when me is clicked`
  fires, other sheets can listen with `when Ground is clicked`, and a second left click inside
  `DoubleClickSeconds` becomes `when me is double clicked`. `RightClicked` / `MiddleClicked` exist
  too, and a right click that cancels an `aim` (Task 27 Part A) does not broadcast.
- **Selection state**: `Selection.ReplaceSelection` / `Add` / `Remove` / `Clear`, `me is selected`,
  `selection count is <n>`, `when selection changes`, `select me` / `select all <type>` /
  `deselect all`, control groups on 1–0 (host-recalled, `set my control group to <n>` in a sheet),
  `Selection.OnSelectionChanged` for the ring/label views. `EventRuntime.SelectActor` is how bridge
  code selects.
- **Orders**: `move` / `goto` / `attack-move` / `attack <target>` / `stop` / `hold position` / rally
  / queued orders, formations, air path and the air targeting gate (Tasks 21, 23, 28).
- **Icon widgets**: UI `Button` press/release edges raise `when <button> is pressed`, and `bind`
  copies a label/bar to a variable, so a card can already be drawn from authored controls.
- **A `Mouse` target** exists for `place` / `mark` / `goto there`, but `FlaxActions.LastClickPosition`
  is written **only on a left click** that was not consumed by `aim`, and the minimap's
  `SelectionView.SelectRect` is the only rectangle-select in the project.

The gaps are specific: nothing selects on click (the bridge broadcasts and stops), there is no 3D
drag-select, right click has no world position and no order, a card cannot name "what is currently
selected", and no command-card scene/content exists.

### Part A — Click-to-select [x]
- Default left-click behaviour without breaking existing sheets: clicking a unit or building with an
  event sheet selects it and clears the previous selection, shift-click adds or removes, and a click
  on empty ground clears — so a scene that authored nothing still behaves the same and one that
  wrote its own `when me is clicked` handler keeps it (`select me` remains the authored path)
- Opt-in and per-actor, not hard-coded: a `selectable` / `selection priority` opt-out for scenery,
  ground planes and UI controls so clicking a floor decal does not select it
- A scripted/draggable actor that is clicked should not also be dragged or shot by that click
- Double click selects every on-screen unit of the same type (StarCraft's double-click), reusing the
  existing `DoubleClicked` trigger
- Which sheet selects: the clicked actor's own sheet if it has one, otherwise nothing — no global
  "RtsSelection.events" magic

#### Part A notes
- `Runtime/ClickSelect.cs` is the pure half: `ClickSelectMode` (`None` / `Replace` / `Add` /
  `Remove`), `IsSelectable` (an actor with a sheet is clickable; one without a sheet never is —
  that is the ground, a prop, a bullet, a UIControl — and a written `my Selectable` overrules in
  either direction, with `0`, `false`, `no` and `off` all meaning opt-out), `Resolve` (plain click
  replaces, Shift adds an unselected actor and removes a selected one), `ClearsOnGround` (a plain
  click clears, Shift keeps) and `SameKind`, which matches two units by the `.events` file driving
  their sheets rather than by a hard-coded unit table.
- Runtime: `EventRuntime.SelectOnClick(actor, shift)` applies that decision to the live `Selection`
  (`ReplaceSelection` / `Select` / `Deselect` / `Clear`) and returns the mode, so the bridge can
  tell a unit click from a ground click; `SelectSameKind(reference, candidates, shift)` replaces
  (or extends, with Shift) with every candidate whose sheet has the reference's `SourceName`.
- Bridge (`EventsManager.cs`): the 3D raycast branch goes through `HandleWorldClick`, which records
  the double click (the shared `RecordClick` also serves the UI path) and applies the default
  selection **before** `RaiseClick`. A double click calls `SelectSameKindOnScreen`: `SceneActors()`
  filtered by `Runtime.FindSheetFor`, non-`UIControl`, and `camera.IsPointOnView(actor.Position)`
  (no camera in the scene = no filtering). A click that hits no collider, or an actor with no
  sheet, falls through to `Runtime.SelectOnClick(null, shift)`, which is the ground clear. UI
  clicks keep the old path and never select anything.
- Authoring is unchanged: pushes are queued and run later in the tick, so a `when me is clicked`
  handler sees `me is selected` and can still `deselect all` and win, exactly as before. A scene
  that authored nothing now gets StarCraft selection; a sheet that wants to be scenery with a
  sheet writes `set my Selectable to 0`.
- 17 new pure checks (selectable default, opt-out, opt-in and both spellings, the resolve matrix,
  the ground clear with and without Shift, same-kind matching, headless select / Shift-remove /
  opt-out / ground-clear against a real `EventRuntime`, and a double click selecting two Marines
  but not the Barracks, then Shift adding it) -> **1887 passed, 0 failed**; the module builds with
  0 errors (`Flax.Build -build -arch=x64 -platform=Windows -configuration=Debug
  -target=GameEditorTarget`).
- The picking half (raycast, camera test, Shift read) has no unit tests, as usual; editor
  play-check pending (click a Marine and see its ring, Shift-click a second, click the ground to
  clear, double-click a Marine and see every Marine on screen selected). Drag-select is Part B.

### Part B — Box (drag) select [x]
- Left-drag on empty ground draws a marquee and selects the friendly units inside it on release;
  a drag below a few pixels stays a plain click, Escape cancels, and the drag runs through
  `Selection.ReplaceSelection` so rings, labels and `when selection changes` fire once
- The rectangle is screen-space over projected actor positions (no physics volume needed), the same
  way `MinimapView.SelectRect` already picks minimap blips; buildings are only included if the
  marquee covers them whole, as in StarCraft, and `Shift` keeps the old selection
- The marquee itself is a `UIControl` (a scene-authored frame) so the runtime owns no drawing, and
  the existing edge-pan and camera controls (Task 26) must not fight it while dragging

#### Part B notes
- `Runtime/BoxSelect.cs` is the pure half: `BoxScreenRect` (`FromCorners` normalises either drag
  direction, `Contains` counts the edges as inside, `Covers` is the whole-rectangle test buildings
  need) and `BoxSelectCandidate` (projected centre, projected bounds, `Building` / `Selectable` /
  `OnScreen` / `Friendly`). `BoxSelect.IsDrag` is the threshold (4 px, compared on the squared
  distance), `Includes` is one actor's verdict — a unit joins when its centre is inside, a building
  only when the rectangle covers its whole projected box, and a building with no measured box never
  joins — and `Resolve` builds the selection: kept actors first (Shift), then the new ones, deduped,
  with an empty result meaning "clear".
- Runtime: `EventRuntime.SelectInBox(marquee, candidates, shift)` resolves and applies it with one
  `Selection.ReplaceSelection`, so rings, labels and `when selection changes` fire once per drag.
- Bridge (`EventsManager.cs`): `HandleMouseClick` now answers with a bool — true when the click
  belonged to a UI control or a selectable actor, false for bare ground or scenery — and only a
  false press calls `BeginBoxSelect`, so a press on a unit stays a Part A click. `UpdateBoxSelect`
  shows the marquee once the pointer clears the threshold, `EndBoxSelect` applies it on release (a
  sub-threshold press was already the click) and Escape calls `CancelBoxSelect`. `UpdateCameraPan`
  skips edge panning while `_boxDragging`, so the camera no longer slides the world under the
  rectangle; arrow/WASD panning still works and the drag re-projects on release.
- Bridge (`MarqueeView.cs`): `MarqueePanel` is a scene-authored `UIControl` (translucent fill plus
  border) named `Marquee`; the runtime only sets its corners — converted into control space with
  `PointFromParent`, so a scaled canvas still lines up — and toggles the actor active, owning no
  drawing. `MarqueeView.Candidates` projects every non-UI actor with `Camera.ProjectPoint`, takes
  the eight corners of `Actor.BoundingBox` for buildings, and marks a unit friendly/visible exactly
  the way `MinimapView.SelectRect` does (fog on: `PlayerOf == LocalPlayer` and `IsVisibleTo`; fog
  off: everything counts). A camera-less scene falls back to the ground plane and the bounding
  sphere, so a test or tool scene still box-selects. Without the `Marquee` actor a drag still
  selects, it just draws nothing — one warning if the control type is wrong, the same rule the
  minimap follows.
- 17 new checks (rect normalisation and edges, `Covers`, the 4 px threshold on both axes, unit
  centre vs building whole, the four filters, plain/Shift/empty `Resolve`, and the runtime path:
  two units in one drag with exactly one `SelectionChanged` and two `Selected`, a half-covered
  building staying out then joining when covered whole, Shift keeping the old selection, and a
  plain empty drag clearing) -> **1904 passed, 0 failed**; the module builds with 0 errors
  (`Flax.Build -build -arch=x64 -platform=Windows -configuration=Debug -target=GameEditorTarget
  -workspace=<project>`).
- Picking and drawing are bridge code, so as usual they have no unit tests: editor play-check still
  pending (drag a box on the ground to see the frame, release to select the Marines inside,
  Shift-drag to add, Escape to cancel, and drag toward a screen edge to confirm the camera no longer
  pans under the rectangle).

### Part C — Right-click orders [x]
- Right click records a world position like a left click does (`LastClickPosition` today is
  left-only) so `there` / `Mouse` mean "where I last clicked with either button"
- Context-sensitive default order for whatever is selected, authored in one sheet: enemy under the
  cursor → `attack` it, friendly damaged unit → nothing/repair, own building → no order, resource
  node → `gather`, empty ground → `move` there; shift keeps the order-queue append behaviour
- An order given while an ability `aim` is pending is cancelled first (already true) and an order
  given to nothing selected does nothing
- Buildings and lifted buildings (Task 28) accept the same right click for `set rally` / move

#### Part C notes
- **Where the rules live.** `Runtime/RightClickOrders.cs` holds the table as data:
  `RightClickTarget` (nothing / ground / enemy / friendly / own building / resource node),
  `DefaultOrderKind` (none / move / attack / gather / repair / rally) and `RightClickRule[] Table`
  — enemy → `attack`, ground → move, resource node → `gather`, damaged friendly → `repair`, own
  building and a healthy ally → no order. Editing
  `Table` (or `RuleFor`) is how a game changes the default; the runtime itself has no unit rule.
  `Classify` turns the hit into a target from four facts (an actor was hit, a point was recorded,
  it is a building, its fog player matches) and `Resolve` adds the selection's own shape: nothing
  selected → no order, a standing building → `rally` wherever the click landed, a lifted building
  → the unit rules (it is airborne, so it can be ordered about).
- **The whole selection, Shift-aware.** `FlaxActions.IssueDefaultOrder` walks
  `Runtime.Selection.Selected`, decides per actor (a mixed selection can therefore rally its
  buildings and move its units with one click), and writes either `StartOrder` or — with Shift —
  `AddOrder`, then starts the walk only for actors whose queue was empty. A promoted queued order
  is started by the bridge's existing `PendingOrderMoves` loop. An attack goes through
  `AirTargeting.Refusal` (one warning for the click, not one per unit) and stops at the unit's
  attack range; moves spread over formation slots as `goto there` already did.
- **Orders own their destination.** `UnitOrder` gained `HasPoint`/`PointX/Y/Z` (`SetPoint`), and
  `BeginOrderMove` prefers it over the global `LastClickPosition`. Without this a Shift-queued move
  or a five-unit fan-out would all have walked to the most recent click — the bug this part could
  not have shipped without. `attack`/`attack-move to there` and `set rally point to there` capture
  the click at issue time too, and `UnitOrderQueue` keeps `RallyHasPoint`/`RallyX/Y/Z` so
  `ApplyRally` hands a trained unit the spot the player picked, not the last click anywhere.
- **A second click retargets.** `IssueDefaultOrder` used to start the walk only when the unit had
  no orders, so a right click on a unit already walking replaced the order in the queue but left
  the old path running: the unit finished the first walk, and `FinishGoto`'s `Complete` then ate
  the new order, so the second destination was dropped. `RightClickOrders.StartsWalkNow(append,
  hasOrders)` now answers that question — a replacing order always walks, an appended one only
  when nothing was running to queue behind — and `BeginOrderMove`'s `Remember` drops the stale
  `Goto` move for the subject, so the click retargets mid-walk instead of fighting the old path.
- **Right click records a position.** `EventsManager` now runs the same world raycast for the right
  button and writes `FlaxActions.LastClickPosition` before broadcasting `RightClicked`, so the
  authored `when … is right-clicked` handler and the order both see it. The right button alone
  falls back to the y = 0 ground plane when there is no collider (the left click stays
  collider-only), so an order still lands on a floor with no collider. `HandleMouseClick` now
  reports the 3D actor it hit and whether a `UIControl` swallowed the click, and a UI hit issues
  no order — a command-card button never also orders the selection.
- **One spec point that did not survive contact.** `Mouse` is not a click: only `place`/`mark`
  read it, live from the cursor, and it was left that way — only `there` was widened to both
  buttons.
- **`gather` and `repair` are orders, not jobs.** Both words exist (`gather <node>`,
  `repair <target>`), `UnitOrderKind` gained `Gather`/`Repair` so the queue, `my current order`
  and `when gather order completes` read them by name, and the table now answers a resource node
  with `gather` and a damaged friendly with `repair`. What the engine does is still only the walk:
  the order stops `OrderManager.DefaultInteractionRange` (60) short of its target — near enough to
  touch, far enough that a building's own footprint stays reachable — and the sheet works from
  `me is near …`. Task 30 Part B still owns income, so no mining or healing engine was added, and
  a unit without a repair line simply walks over. `RightClickOrders.IsDamaged` (`my Health` below
  `my MaxHealth`, read through `HealthManager` so a sheet spelling it `my HP` answers too) gates
  the friendly row: a healthy ally is still no order, and an actor with no health variables is
  never damaged, which keeps repair off scenery and off the older TK sheets.
  `FlaxActions.StartGather`/`StartRepair` warn once when the target carries no node tag or is at
  full health.
- **Enemy means another fog player, or `my Hostile`.** `RightClickOrders.Classify` takes
  `samePlayer` from `FogOfWarManager.PlayerOf`, which reads `my Player` (default 0). On its own
  that made every unauthored scene one team, so the default attack never fired against the
  shipped enemy sheets. The fallback is the flag those sheets already carry: `my Hostile`, which
  `enemy type` writes and `when all enemies dead` reads. `RightClickOrders.IsDeclaredEnemy` asks
  for `IsHostile(target) && !IsHostile(me)`, so a `Worker` right-clicking a `Zombie` attacks with
  no `set my Player to 1` anywhere, while two hostile actors stay one side. Authored players still
  win where they exist (different players are enemies whether or not either is hostile), and a
  scene that wants its enemies silent leaves `Hostile` off — same flag, no second switch.
  Accepting both signals is on purpose: a scene can mix RTS content (`my Player` teams) with
  other-game content that only knows `my Hostile`, and neither author has to set the other.
- **Fog hides models, not colliders.** `SetFogVisibility` only swaps draw passes, so a unit the
  local player cannot see still catches the right click's ray. `HiddenFromPlayer` (different fog
  player *and* not `IsVisibleTo`) answers `Classify`'s `hidden`, which reads the click as bare
  ground: the selection walks to the point instead of attacking something the player cannot see,
  and no enemy position leaks. An unauthored scene is untouched because everyone sits on fog
  player 0, which is the local player — never hidden.
- **Kept deliberately.** A right click that cancels an `aim` is still swallowed (Task 27 Part A
  documented that, and a move order on the abort click would be a surprise), an order with nothing
  selected does nothing and warns nothing, and a click on a `UIControl` raises only its authored
  trigger. 19 new checks (table, classification, rally-vs-move-vs-attack, the lifted-building
  rule, node tagging, the stored point surviving a queue promotion, rally coordinates, plus 4
  follow-up checks for the `my Hostile` fallback and the fog-hidden case, plus 8 for the
  `gather`/`repair` words, the damage gate and the order names) ->
  **1935 passed, 0 failed**; the Game module builds with 0 errors and the assembly carries the new
  symbols. Editor play-check of the actual click targeting, timing and camera feel still pending.

### Part D — The selection as an event value [x]
- Something a card can name, so one shared card serves any selection: `my selected unit`, `my
  selected building`, `selected count`, and "is the selection all one type" so a card can pick
  between a unit and a building layout
- Orders issued by a card must apply to the **whole** selection, not just the actor whose sheet
  pressed the button — `train Marine at Barracks` names its target today, and this part adds the
  form that means "whatever I selected"; the same for `attack` / `move` / `stop`
- No C# UI rules: the card decides its own layout from these values

#### Part D notes
- **What a card reads.** Four computed values, handled in `EventContext.ReadMy` exactly like
  `my current order` so a value and a condition never disagree: `my selected unit` (the first
  selected unit, `""` when there is none), `my selected building` (the first building),
  `my selected kind` (that unit's `.events` name, so every Marine answers `Marine` however the
  scene named the instance) and `my selected count` / `my selection count` (the size). They read
  the live `Selection`, so nothing has to be mirrored per tick.
- **One answer to "is this a building".** `EventRuntime.IsBuildingActor` is the build registry, or
  the lift-off state, or a production queue — the three ways this project already knows an actor
  is a building. `selected buildings`, `selection is all buildings` and `my selected building` all
  ask it, so they cannot disagree, and scenery counts as a unit (a shared card shows its unit
  layout, and the scenery's own sheet decides what the buttons do).
- **The layout question.** `selection is all units` / `selection is all buildings` /
  `selection is all <kind>` (`ConditionKind.SelectionAllIs`, evaluated against the live selection).
  Nothing selected is never "all" of anything, so an empty selection draws no card. Kind matching
  lives in `SelectionInfo.SameKind`: case-insensitive, a trailing `.events` optional — deliberately
  Part A's double-click notion (the file that drives the sheet), not `select all <type>`'s actor
  names, so `selection is all Marine` works for `Marine`, `Marine2`, `Marine 7`.
- **One button, the whole selection.** `selected units <order>`, `selected buildings <order>` and a
  bare `selected <order>` (every selected actor, units and buildings together) are a prefix parsed
  in `ActionWords.ParseSelected` and dispatched by `FlaxActions.ExecuteOnSelection`: the same spec
  runs once per selected actor with `me` rebound and **that actor's** sheet attached, so formation
  slots, attack range and its refusal, the `gather`/`repair` interaction range, rally points and a
  per-building `train` all behave exactly as they do when the line sits on a unit's own sheet. A
  card on the shared Game sheet and a card on `Marine.events` therefore issue the same order.
  `SelectionInfo.AllowsOrderScope` is the whitelist — the order verbs (`goto`, `move`, `stop`,
  `hold position`, `attack`, `attack-move`, `gather`, `repair`, `train`, `cancel training`,
  `research`, `set rally point`, `clear orders`, `fly`, `lift off`, `land`, `cast`) — so
  `selected print "hi"` is refused with a message instead of quietly running once per selected unit.
  Two Barracks selected and `selected buildings train Marine` queues one Marine at **each**, which is
  StarCraft's rule; `selected units goto there` still fans out over the Task 23 formation slots.
- **Warnings stay one per press.** The per-actor path uses the same `WarnOnce`, so an empty
  selection, a refused matchup or a missing production building says its piece once.
- **Deliberately not built here.** No C# card, layout or icon rule: Part E owns the widget and the
  content. `attack-move`'s targeting cursor (press the button, then click) is Part E content too —
  the word already takes `there`, it just needs a card to set the point. A building keeps its own
  sheet's words; there is no generic "building card" in the engine.
- **The card can also ask directly.** `my selected count is <n>`, `my selected kind is <kind>`,
  `my selected unit is <name>` and `my selected building is <name>` are conditions on the same
  computed values (the Conditions parser reads the multiword subject as one state, exactly like
  `my current order is`), so a card can branch without mirroring the selection into variables.
  `my selected kind` reads the bare kind (`Marine` for `Marine.events`), which is what makes
  `my selected kind is Marine` match.
- 35 new checks (scope filtering and order, first-of-each, the empty selection, kind normalisation,
  the verb whitelist, four parsed shapes plus two refusals, the runtime reads, the three
  selection-shape conditions against live selections, the two value parses, and the two
  value conditions) -> **1997 passed, 0 failed**. The Game module's
  response file compiles with 0 errors. `Flax.Build` itself currently fails on a pre-existing,
  unrelated problem: `Runtime/SaveFile.cs` (Task 30 Part A) uses `System.Text.Json` and the Game
  target's reference set does not include it, so the editor target does not link until that
  reference is added. Editor play-check of a real card press is still pending with Parts A-C.

### Part E — Command card content [ ]
- A `CommandCard`/`CommandPanel` scene widget with icon buttons for the Mariner/Barracks/Gateway
  example units: move, stop, hold, attack-move, plus unit-specific `train <unit>` / `build
  <building>` / ability buttons, each one a `when <button> is pressed` block in the selected unit's
  own sheet
- Affordability and cooldown feedback from data the project already has: mineral/gas cost
  (`can afford`), `my train time`, queue length (below a queue cap), Task 27 `cooldowns` and the
  flying-building gate — a disabled button must show why, either as a grey tint via `bind` or a
  printed reason
- A hotkey column that reuses the existing key triggers, tooltip on hover (`Hovered` already fires),
  and a current-order readout (`my current order`, `my order queue`) so the player can see what the
  unit is doing
- Icon assets stay authored/content-side; the task ships the wiring and one card per example unit,
  not an art pipeline

#### Part E notes — production gates (first slice, the part stays open)
- **A queue cap now exists.** `ProductionManager.DefaultQueueCap` (5), overridden by
  `<Building>QueueCap` and then `ProductionQueueCap`, resolved through `ResolveQueueCap` /
  `QueueCapOf`. `Enqueue` returns false instead of silently overflowing, and `train` warns once
  ("“Barracks” already has 5 units queued — its production queue is full"), so the acceptance
  step "spam the icon to hit the queue cap" is a real refusal rather than a no-op.
- **The gate a card shares.** `queue has room` (`ConditionKind.QueueHasRoom` → `CanEnqueue`) is
  the sheet-facing form of the same question the enqueue itself asks, so a Train button and its
  hotkey cannot drift from what the engine will accept.
- **The two numbers a card shows.** `my train time` reads `CurrentDurationOf` — the data-driven
  `<Unit>BuildTime` → `<Building>BuildTime` → `ProductionBuildTime` → 5 — and reads 0 when the
  queue is empty; `my queue cap` reads the cap in force. Both are ordinary values, so a label can
  `bind` to them.
- **One manager per scene decides where the card lives.** `EventsManager` keeps a single
  `Instance` and disables any second one with a warning, and its button poll only walks its own
  scene's actors — so a card cannot be an additive HUD scene with its own manager, and no
  `RtsCommandCard.scene` was authored. The card UI belongs in the gameplay scene, with this sheet
  attached to that scene's manager actor.
- **The authored scene.** `Content/scenes/RTS_Test_0.scene` now carries the whole card: a `ui
  canvas`, the seven `Button` UIControls (`StopButton`, `HoldButton`, `TrainMarineButton`,
  `CancelMarineButton`, `LiftButton`, `LandButton`, `ResearchButton`, anchored to the bottom-right
  corner), one `RTS` actor holding the scene's `EventsManager` plus an `EventSheet` on
  `CommandCard`, and a `Barracks` with two `Marine`s built from the project's box model with the
  shipped sheets attached, so the card has something to select and order on the first play. The
  scene files were written as scene JSON by hand (the editor is not available here), so nothing in
  it has been loaded, seen or play-checked.
- **A building says it is one.** A building dropped into a scene by hand was never placed by the
  build registry, so it had no building state at all — `selection is all buildings` was false and
  the card would have shown the unit layout. `set my Building to 1` (`ConstructionManager.
  MarkVariable`, both spellings, same flag rules as `my Selectable`) is now a fourth answer inside
  `EventRuntime.IsBuildingActor`, which is what `Barracks.events` writes in `when me appears`. The
  right-click rally asks the same method rather than `Construction.IsBuilding`, so a scene-authored
  building also takes orders instead of being walked into.
- **The card sheet ships.** `GameFiles/CommandCard.events` is the shared card: `when scene
  starts` hides every button, `when selection changes` shows the unit buttons or the building
  buttons from `selection is all units` / `all buildings` (a hidden button stops polling, so the
  other layout cannot be clicked), and the buttons are the Part D orders —
  `selected units stop` / `hold position` on the unit panel, `selected buildings train Marine` /
  `cancel training Marine` / `lift off` / `land` / `research "Stim Pack"` on the building panel —
  each behind the same stacked gates its hotkey uses (`can afford`, `queue has room`, the selection
  kind). Four `when every 0.5 seconds` blocks drive Train's greying — disabled when the layout is
  wrong, when the unit is unaffordable, or when the queue is full, and enabled only when all three
  hold — because a body cannot branch on "which gate failed". Seven checks pin the sheet: it
  parses, every action and condition line is a known word, it carries a `set button` and a
  queue-cap gate, and Train/Stop/Lift/Research come out as the right verb and scope.
- **Still open in Part E:** the card's icons, per-button labels and hover tooltips (the shipped
  buttons are text-only `Button` controls in the editor's default style), real unit art in place
  of the placeholder boxes, the `Marquee` drag rectangle (needs a UIControl whose control type is
  `MarqueePanel`, a custom control the editor has to instantiate), a conditional colour/tint *bind* (today a tint is
  `set color of` as an action — `bind` only drives text and progress, so "grey via bind" needs a
  polling card sheet or a new bind kind; the shipped sheet greys through `set button ... disabled`
  instead), selection-scoped hotkeys (an existing `when W is pressed` fires whatever is selected,
  so the card's guards have to sit above it), and the acceptance play-check. Two content gaps the
  sheet documents: Move/Attack/Attack-Move buttons need a targeting cursor no word offers yet (the
  right click already issues those orders), and an ability button cannot reuse the unit's own
  `when Q is pressed` block because nothing runs another sheet's block for every selected unit —
  so Stim stays on `Marine.events` and a card `train`/`stop` is all a button can reuse.

### Part F — Words, tests, acceptance [x]
- [x] `WordRegistry` rows and `keywords.html` rows for every new word (selection-as-value, the
  multi-unit order forms); an audit pass so the documented shapes and the parser agree
- [x] Pure tests: click routing decisions (select / add / clear / ignore), marquee bounds math, the
  right-click order resolution table, the multi-unit order fan-out and its refusal path
- [x] Acceptance list written (the steps below); it has **not** been played through — that needs the
  editor and this task's notes say so
- [x] The bridge half (raycast picking, marquee drawing, UI hit-testing) has no unit tests by the
  project's convention; recorded in the notes

#### Part F notes
- **The audit runs as a test, in both directions.** `Tests/Program.cs` reads `keywords.html` from
  the repo root, strips markup and then decodes entities — that order matters, because `&lt;thing&gt;`
  is a placeholder in the *text*, so decoding first would let the tag strip delete every `<thing>`
  — and asks two questions: every example the reference prints is a line the parser accepts, and
  every word `WordRegistry` offers has a row there. The first direction passes clean, which is the
  answer the audit was for: no documented shape has drifted from the parser.
- **The second direction found the real backlog, and it is now closed.** It started at 73 words the
  registry offers with no row in the reference — all older than this task: the UI triggers
  (`when <thing> text changes`, `is focused` / `loses focus` / `submits` / `is double-clicked` /
  `is right-clicked` / `is pressed` / `is released`, `<thing> is pressed`/`is released`/`is
  disabled`/`is focused`, `text of <thing> is <value>`), the production and research edges
  (`when <building> construction completes`, `finishes training <unit>`, `starts producing <unit>`,
  `production queue is empty` and its named form, `starts`/`finishes researching <name>`,
  `when research finishes`, `<order> order completes`, `queue count is <n>`, `<building> is idle`,
  `me has build queue`, `set build time`), the buff words (`when buff "<name>" starts`/`ends`,
  `my "<name>" time left`, `add buff`, `remove buff`), fog and aiming (`area becomes revealed` /
  `hidden`, `<unit> enters fog`, `me is visible to enemy`, `me is hidden`, `reveal`, `hide area
  around me by <radius>`, `set visibility to <radius>`, `when aim picks a target`), the input verbs
  (`enable`/`disable`/`toggle`/`unfocus`/`navigate`), the camera and combat words, the two- and
  three-operator `set`, and the remaining value forms. **All 73 rows are written now**, in the section
  each word belongs to — 31 triggers, 16 conditions, 17 actions, and 6 value forms plus the
  `bind … format "<template>"` variant next to the other binds — and `Tests/keyword-gaps.txt` is
  empty apart from its header. The mechanism stays: a registered word with no row fails the test
  unless it is listed there, and a listed word that has since been documented fails it too, so a
  future gap has to be named deliberately.
- **Two shapes cannot be read as ordinary lines**, and the audit says why instead of skipping quietly:
  `bind` is sugar that expands into its own top-level event (so it is parsed as one), and
  `otherwise` only means something under the condition line above it (so it is the one documented
  sample outside the parse check, alongside the handful of rows whose example contains a
  placeholder).
- **The four pure-test bullets were already covered**, so Part F points at them rather than
  duplicating assertions: click routing in the Part A block (`ClickSelect.Resolve`'s replace/add/
  remove/none table, `ClearsOnGround`, the sheet/`my Selectable` rules and `SelectOnClick` against a
  live selection), marquee bounds in Part B (`BoxScreenRect.FromCorners`/`Contains`/`Covers`,
  `BoxSelect.IsDrag`, unit-centre vs whole-building-box inclusion, and `BoxSelect.Resolve` keeping
  the old selection on Shift), the right-click table in Part C (`RightClickOrders.Classify`'s six
  targets plus `IsHostile`/`IsDeclaredEnemy`/`Resolve`), and the fan-out in Part D (scope filtering
  and order, `SelectionInfo.AllowsOrderScope`'s verb whitelist and its refusal message, and the
  per-actor `train`).
- **What has no test** is the bridge: raycast picking, the marquee's `SetCorners` projection, the
  UI hit-test that decides a press belongs to a button, and the `ExecuteOnSelection` loop itself
  (it needs Flax types). Those are the play-check's job.
- **The gathered finding is a documentation gap, not a language bug.** `my Building` (added earlier
  in this part) joined the building words, and the reference already documents it; nothing in the
  parse audit was wrong, so no word changed shape in this slice.
- **The audit's shape comparison had one trap of its own:** block-level tags become a space but
  inline ones vanish, because a row's own spacing lives in the text — `when buff "` + `<name>` +
  `" starts` must read back with no gaps around the placeholder. Replacing every tag with a space
  (the first attempt) made exactly the eight rows that wrap a placeholder in quotes look
  undocumented.
- 9 new checks (the reference exists and lists samples, no new gap, no stale gap, the pinned count
  matches, every printed example parses, only the declared shape rows skip, and every word this
  task added is in both the registry and the reference) -> **2041 passed, 0 failed** with the gap
  list empty.
- **Acceptance list (not yet run — needs the editor):**
  1. Open `Content/scenes/RTS_Test_0.scene` and press play. The two Marines stand on the floor and
     the Barracks is the large box; no card buttons are visible yet.
  2. Left click a Marine: its selection ring appears and the unit card's two buttons (`Stop`,
     `Hold`) fade in at the bottom right of the canvas.
  3. Drag a box over both Marines: both get rings, both stay selected on release.
  4. Press `S` (or the Stop button) with both selected: both stop, and the sheet prints nothing odd
     in the console.
  5. Right click near the Barracks: both Marines walk there — the group order comes from one click.
  6. Left click the Barracks: the unit buttons go away, the building buttons appear, and `Train
     Marine` is greyed only while the Marine is unaffordable or the queue is full.
  7. Press `W` (or Train) five times: five Marines queue, the sixth press is refused with the
     "production queue is full" warning, and the button is greyed while the queue is full.
  8. Click empty ground: no card, no selection ring.
  9. Open a scene with no selection or UI content (for example `Content/scenes/PhysicsTest.scene`)
     and confirm it still plays exactly as before — the selection code is opt-in per scene through
     its sheets and the manager's button poll finds no buttons there.

#### Part F follow-up — the play-check found right-click doing nothing
- **What the play-check showed.** Step 5 failed: selecting a Marine worked (`Marine selected`
  printed), but a right click on the floor produced `Marine: order started` and `Marine: path
  blocked` in the same millisecond, twice, and the unit never moved — even after the scene got box
  colliders and a NavMesh. Two separate causes, both in the bridge:
  1. **The order speed was a literal.** `BeginOrderMove` was called with `5` at every site (move,
     attack, attack-move, gather, repair, build, rally, and the queued-order promotion), so the
     Marine's `set my Speed to 90` was ignored and any walk would have been 5 cm/s — invisible.
  2. **A failed `Navigation.FindPath` was treated as a dead end.** A click a few centimetres off
     the baked mesh, a body whose pivot sits under it, or a mesh Flax is still baking (it bakes in
     the background, `Navigation.IsBuildingNavMesh`) all answer "no path", and the order stopped
     there after raising the trigger.
- **What changed.** `OrderManager.MoveSpeedOf(variables, unit)` is now the one answer for a unit's
  pace: `my Speed`, else `my MoveSpeed`, else `DefaultMoveSpeed` (100) — a zero or negative value
  counts as unset, because a sheet writing 0 should not freeze a unit. Every `BeginOrderMove` call
  passes it instead of `5`, the queued-order promotion and the rally read the same helper, and
  `goto` with no written `speed` uses it too (an explicit `speed <n>` still wins). `TryNavRoute`
  queries the mesh and, if that fails, snaps both ends with `Navigation.FindClosestPoint` and asks
  again; `goto there`, `goto <actor>` and every order use it. When even the snapped query has no
  answer the unit hears `when path blocked` (the route really was not found) and then walks the
  straight line, so a level whose mesh is missing, still building or smaller than the click moves
  the unit instead of leaving it standing. `WarnNoNavRoute` says once which actor and which point,
  whether the mesh is still building, and what to check (`NavMeshBoundsVolume` coverage, walkable
  floor) so the scene problem is visible instead of silent.
- **Checks:** 5 new pure ones (`MoveSpeedOf` default, `my Speed`, zero, `my MoveSpeed`, and the
  null-variable/null-unit reads) -> **2046 passed, 0 failed**; `GameEditorTarget` builds with 0
  errors and 0 warnings. `keywords.html` gained the note on `goto` (pace from `my Speed`, default
  100; straight line when the mesh has no route) and on `when path blocked` (the walk still starts
  along the straight line).
- **Not verified:** the fix has not been played in the editor. The next play-check is: select a
  Marine, right click the floor, and expect either a NavMesh walk or a single
  "No NavMesh route … Walking the straight line instead" warning followed by movement at
  `my Speed` (90), then `Marine: order complete` on arrival.

##### Part F follow-up 2 — a fallback that contradicts the trigger
- **The contradiction.** Follow-up 1 made a failed `Navigation.FindPath` mean both "the path is
  blocked" and "walk anyway". That is two different claims at once: the straight line sent a unit
  through walls whenever the mesh *had* answered with no route, and the sheet heard `when path
  blocked` for a walk that was still going. A trigger named "blocked" has to mean the unit
  stopped.
- **The rule now.** One query, three answers, decided by `NavRoute.Classify`
  (`Source/Game/Runtime/NavRoute.cs`, pure and testable):
  - **`Found`** — a route, walked as given. The raw query runs first, then the same query between
    both ends snapped by `Navigation.FindClosestPoint`, which is what rescues a pivot a hair under
    the mesh or a click on the floor's very edge.
  - **`Blocked`** — both ends sit on a baked mesh (within `NavRoute.OnMeshTolerance`, measured
    horizontally, so a unit's pivot above the floor still counts as on it) and even the snapped
    query has no route. The unit really stops and `when path blocked` fires. Walls, props, the
    terrain grid and a refused landing are all this case.
  - **`Unavailable`** — the mesh has no opinion: `Navigation.IsBuildingNavMesh`, no nearest point
    at all (nothing baked), or a point that snapped further than the tolerance (a click off the
    mesh, a body pushed onto geometry). Nothing is in the way, so the unit walks the straight line
    and **no** trigger fires.
- **Where it is wired.** `FlaxActions.QueryNavRoute` replaced `TryNavRoute` and returns the
  answer; `goto there`, `goto <actor>` and `BeginOrderMove` (orders, rally, queued promotion) each
  branch on it. `NavRoute.Stops` is the only path that raises the trigger; `NavRoute.WalksStraight`
  is the only path that takes the straight line. `WarnPathBlocked` joins `WarnNoNavRoute` so the
  log says which of the two happened, once per scene.
- **Checks:** 11 new pure ones over the decision table (`Found` beats everything, a building mesh
  never blocks, a missing mesh is not a block, an off-mesh click is not a block, a stranded unit
  walks straight, a snapped query recovering a route, on-mesh with no route is the block, and that
  only `Unavailable` walks straight / only `Blocked` stops) -> **2057 passed, 0 failed**;
  `GameEditorTarget` builds with 0 errors.
- **Not verified:** still an editor play-check: right click the floor and either path around the
  walls, or — with no NavMesh covering the Marine — one "No NavMesh route …" warning, a straight
  walk and **no** `Marine: path blocked` line.

##### Part F follow-up 3 — the floor was an ally
- **What the play-check showed.** After follow-up 2, clicking the empty sky moved the Marines but
  clicking the floor under them did nothing. The two clicks get different answers from the ray:
  the sky misses everything, so `RightClickPoint` falls back to the y=0 plane and the order has a
  point and no actor, while the floor's `MeshCollider` catches the ray and the order gets
  `target = Floor`.
- **Why that stopped the walk.** An actor-carrying right click goes through `RightClickOrders`.
  `Classify` saw the Floor as an actor that is not a building, not hostile and not a resource node,
  so it answered `Friendly`; `Resolve` answers a healthy friendly with **no order**, so the Marine
  was told nothing. Scenery was being read as an ally because classification only asked what the
  actor *was not*.
- **What changed.** The bridge now decides what "an actor" means before it classifies: if the hit
  actor carries no `EventSheet`, it is bare ground, not a unit or a building — the same rule
  `TryAimPick` already used for aiming — so the order passes `target = null` with the hit point and
  walks there, exactly like a click on the sky. A sheet is what makes an actor something that can
  hold orders; a prop, a wall or the floor has no sheet by definition, and a scene cannot fake it
  into being an ally.
- **Checks:** no new pure tests (`RightClickOrders` is unchanged — this is which target the bridge
  hands it); the existing 2057 checks still pass and `Flax.Build -target=GameEditorTarget` exits 0.
- **Not verified:** unplayed in the editor. Next play-check: select a Marine, right click the floor
  directly under it, and expect the same walk a click on the sky produces.

##### Part E follow-up — hotkeys were global, and training produced nothing
- **What the play-check showed.** Three things were wrong at once on `RTS_Test_0`: pressing `L`
  lifted the Barracks without selecting it, `K` seemed to do nothing, pressing the card's Lift
  button did not lift it, and pressing Train queued an invisible Marine — no readout, no unit on
  the map.
- **Why the keys were global.** A key press is broadcast to every sheet in the scene
  (`RaisePushAll` with the key name), so `when L is pressed` on the Barracks sheet ran whether or
  not the building was selected. Nothing in a sheet said "only when I am the selection".
  `when W is pressed`, `when R is pressed`, `when L is pressed` and `when K is pressed` now each
  begin with `me is selected`, which is the same gate the card's buttons get from
  `selection is all buildings` — so a hotkey and its button can no longer disagree about who they
  act on. `K` was most likely never broken as a word: the landing only makes sense once the
  building is up, and the play-check mixed up which presses had actually taken effect; the gate
  plus the existing `Barracks: airborne` / `Barracks: down` prints now make the state visible in
  the log.
- **Why the card's Lift did not lift.** `lift off` took its height from the number on the word,
  then `my Altitude`, then the `LiftOffHeight` global, then 200. The L key set `my Altitude` to 20
  first, but the card could only say `selected buildings lift off` — a card cannot write another
  actor's variables — so the button lifted the same building to the 200 default. The building
  shot up out of the camera's view and looked untouched. `LiftOffManager.LiftHeightVariable`
  (`my LiftHeight`) is now the per-building rise height, read before the global and the default,
  and `Barracks.events` writes `set my LiftHeight to 20` once in `when me appears`. The L key, the
  card's Lift button and `selected buildings lift off` therefore all rise by 20, and the L block
  no longer has to pre-set `my Altitude` to get there.
- **Why training produced nothing.** `train Marine` only fills a queue; nothing in the project
  ever put the finished unit on the map. `when Barracks finishes training Marine` now
  `spawn Marine at me offset 120`, so the unit appears at the building's door with that scene's
  `Marine` sheet attached. The scene gained a `Marine` staging actor for the clone (the pattern
  `spawn` documents: "place an actor with that name in the scene, hidden if you like"); it sits
  below the floor at y = -500 so it is never seen or selected, and every clone is repositioned by
  the spawn itself.
- **The numbers.** Two computed reads join the production read-outs: `my queue count` (orders
  waiting) and `my train left` (whole seconds left on the active one, `Math.Ceiling` so a label
  never prints `4.0000001`). Both are read through `ReadMy`, so `bind` polls them each tick and a
  countdown updates with no timer mirrored into a variable. The Barracks sheet binds the card's
  own Train button to both — `bind TrainMarineButton to my queue count and my train left format
  "Train Marine (W)  {0} left, {1}s"` — so the button doubles as the readout instead of needing
  another label actor in the scene. The card's three button blocks also print what was pressed
  (`Card: train Marine`, `Card: lift off`, `Card: land`), which is what tells a play-check whether
  a press arrived at all.
- **Checks:** 13 new ones (`my queue count` and `my train left` reads including the countdown and
  the idle zero, both new names parsing as one value, `my LiftHeight` beating the global while a
  written height beats both and a zero LiftHeight counting as unset, the three hotkey blocks being
  selection-scoped, the spawn naming `Marine` at `me`, and the sheet carrying the readout bind and
  its own lift height) -> **2070 passed, 0 failed**; `Flax.Build -target=GameEditorTarget` exits 0.
  The two card tests that read a button's first action were adjusted to skip the diagnostic print.
- **Not verified:** none of this has been played. The next play-check is: click the Barracks, press
  `W` and watch the Train button count down and a Marine appear beside the building; press `L` (it
  rises 20) then `K` (it comes down); click the card's Lift and Land buttons for the same two
  moves; finally click a Marine and press `W` — nothing should be queued, because the Barracks is
  no longer selected.

#### Play-check answers noted during this round
- **Why a Marine does not settle onto the floor.** The marines are `StaticModel` + `BoxCollider`
  actors with no body: `BoxCollider` is a query shape for raycasts and sweeps, not a collider that
  resolves contact. Nothing in the scene has a `RigidBody`/`CharacterController`, and the sheets do
  not set `Use Physics Movement`, so a marine simply stays at its authored Y
  (`Marine1` 37.55, `Marine2` 37.07) and pans along X/Z. Contact with the floor needs a body: a
  `CharacterController` (with the sheet's `Use Physics Movement` ticked, so `MoveSubject` drives it)
  or a dynamic `RigidBody`, both editor-side changes.
- **Built-in NavMesh or an RTS-specific one.** Keep Flax's `Navigation` for now. It answers what an
  RTS needs at this scale — a click-to-point walk, a path around props, and a truthful "blocked" —
  and a grid, flow field or coroutine-based group pathing system is a rewrite that a playtest has
  not asked for. The seam is already in place for it: every walk asks one question,
  `FlaxActions.QueryNavRoute`, and every answer is one of `NavRoute.Classify`'s three cases, so a
  different backend replaces that function without touching a single `.events` file. Build the
  substitute when a measured problem (many units on one corridor, formation jitter, cost of
  per-unit paths) shows up, not before.

### Open questions for this task
- Shift-to-add on click: is `Selection.Add` enough, or does the selection need a "primary" actor for
  the card to read?
- Does the card live on one sheet that hears `when selection changes`, or on every unit sheet with a
  `bind`ed layout swap?
- Marquee over buildings whole-vs-any: StarCraft uses whole for buildings and any for units — copy
  both?
- Click-to-select as an opt-in `selectable` word, or a default with an opt-out for scenery? The
  default changes behaviour for existing scenes that intentionally made a unit non-selectable.

---

## Open Questions (carried from Docs/rts-event-manager-support.md)

1. ~~Grid-based vs. free-form unit placement for buildings?~~ → Both: free-form by default, `on grid` opt-in via `place … on grid`; grid cell size from `BuildGridSize`.
2. How many players (1v1, FFA, teams)?
3. Supply cap mechanics — hard limit or soft warning?
4. Fog of war: explored areas stay visible or revert to fog?
5. Control groups: 1–0 keys or click-assign? — **keys**: 1–0 recalls a group, Ctrl+1–0
   assigns the current selection (`EventRuntime.AssignControlGroup`); sheets can also assign with
   `set my control group to <n>`. Click-assign was dropped: the command card has no group buttons.
6. Homeworld 3D movement: full 3D or 2.5D plane? — **2.5D**: movement and pathing stay on the XZ
   plane; flight is a height a unit keeps, added by Task 28 Part A–C.
7. Red Alert power system: power deficit slows production?
