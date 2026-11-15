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

## Task 24 — RTS Minimap [ ]

Goal: add a minimap UI showing explored terrain, unit dots, and click-to-pan camera control.

### Part A — Minimap UI [ ]
- Second camera rendering to a small render texture
- Terrain and unit markers drawn on the minimap
- Fog of war reflected on minimap (unexplored = hidden)

### Part B — Minimap interaction [ ]
- Click on minimap pans the main camera to that world position
- Drag to select area on minimap

---

## Task 25 — Enemy AI Building Placement [ ]

Goal: give enemy AI the ability to find and place buildings at valid empty spots automatically.

### Part A — Build spot finding [ ]
- `ConstructionManager.FindEmptyCellNear(anchor, radius)` — returns a free grid cell
- AI evaluates nearby resources, own buildings, and enemy distance before placing

### Part B — Enemy build logic [ ]
- `.events` script for enemy base building using `place <building> at <found spot>`
- Expansion: enemy places new bases when resources allow

---

## Task 26 — RTS Camera Controls [ ]

Goal: add RTS-style free camera — mouse-wheel zoom, mouse-edge panning, and arrow-key/WASD panning — so the player can look around the 3D map.

### Part A — Mouse-wheel zoom [ ]
- New action: `zoom camera by <n>` / `camera zoom in` / `camera zoom out` — adjusts camera height/depth offset (or field of view)
- Min/max zoom clamp so the camera cannot zoom into the floor or out to infinity
- Bind `when wheel scrolls up/down` to increment/decrement zoom

### Part B — Mouse-edge panning [ ]
- When the mouse is within N pixels of a screen edge, pan the camera in that direction each tick
- Panning releases any active `camera follows` target while the mouse is at the edge
- Configurable edge thickness and scroll speed

### Part C — Arrow-key / WASD panning [ ]
- `when Left/Right/Up/Down is held` pans the camera in that direction each tick
- WASD keys also pan the camera (camera-relative forward/back/left/right)
- Panning speed scales with zoom level (slower when zoomed in)

### Part D — Camera state [ ]
- `my camera zoom` / `my camera position` — readable camera state for events
- `camera free` releases follow and returns to free-pan mode
- `camera follows <unit>` locks onto a selected unit (already exists; works alongside free pan)

---

## Open Questions (carried from Docs/rts-event-manager-support.md)

1. ~~Grid-based vs. free-form unit placement for buildings?~~ → Both: free-form by default, `on grid` opt-in via `place … on grid`; grid cell size from `BuildGridSize`.
2. How many players (1v1, FFA, teams)?
3. Supply cap mechanics — hard limit or soft warning?
4. Fog of war: explored areas stay visible or revert to fog?
5. Control groups: 1–0 keys or click-assign?
6. Homeworld 3D movement: full 3D or 2.5D plane?
7. Red Alert power system: power deficit slows production?
