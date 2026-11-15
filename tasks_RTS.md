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

#### Part G follow-ups (from implementation — not blocking) [ ]
- [ ] `add -my <var> to Health` (a dynamic damage amount in one action) needs a negated-variable
  value form; for now use `set my Health to my Health - my DamageTaken` (direct assignment, no
  armor/shield soaking) or `add -<n> to Health` with a literal.
- [ ] A multiword stat name (`Max Health`) is read but only `MaxHealth` / `maxhealth` spellings are
  written by the ceiling logic, so pages that write `set my Max Health to 100` round-trip through
  the canonical local.

### Part H — Extended `IEventWorld` [ ]
- `GetSelection()` → list of selected actors
- `GetSelectedCount()` → int
- `GetResource(string name)` → value
- `IsRevealed(actor, playerId)` → bool
- `RevealArea(center, radius)` / `HideArea(...)`
- `GetBuildProgress(building)` → float
- `GetOrderQueue(unit)` → list

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

## Task 21 — RTS Command Panel & Orders [ ]

Goal: add attack-move, attack, stop, hold position, rally point, and per-unit order queue so a command panel can issue real RTS orders.

### Part A — Attack-move & attack [ ]
- New action verb: `attack-move to <target>` — move toward target, engaging any enemy that enters range along the way
- New action verb: `attack <target>` — stop moving and attack the named target in range
- New trigger: `when order completes`, `when queued order starts`
- New condition: `me is in range of <target>` (compares distance to my attack range)

### Part B — Stop & hold position [ ]
- `stop` — cancel current order and go idle
- `hold position` — freeze in place, do not pursue
- `clear orders`, `add order to queue`
- Per-unit order state: `my current order`, `my order target`

### Part C — Rally point [ ]
- `set rally point to <target>` — newly trained units auto-move there on spawn

---

## Task 22 — RTS Unit Types & Terrain [ ]

Goal: add land / air / sea unit types and per-terrain passability so units respect what they can walk/fly/swim on.

### Part A — Unit type [ ]
- Per-unit variable: `my unit type` (`land`, `air`, `sea`)
- Conditions: `me is land`, `me is air`, `me is sea`
- Ground units blocked by water; air units ignore ground obstacles; sea units restricted to water

### Part B — Walkable area / terrain [ ]
- Terrain passability map (land/water/air cells)
- Movement (`goto`, `move`) consults terrain per unit type
- Buildings only placeable on valid terrain for their type

---

## Task 23 — RTS Combat Matchups & Formations [ ]

Goal: add close vs range attack, attack matchup matrix, and formation movement for grouped units.

### Part A — Attack types [ ]
- Per-unit variable: `my attack type` (`melee` / `ranged`)
- `my attack range` — distance at which ranged attacks can hit
- Close combat: damage on contact; ranged combat: projectile flies to target

### Part B — Attack matchups [ ]
- Data-driven table: which unit types can attack which (land vs land, land vs air, air vs land, air vs air)
- Condition `me can attack other` — consults the matchup table
- Mismatched attacks are ignored (no damage)

### Part C — Formation movement [ ]
- When multiple units are selected and given `goto`/`attack-move`, they arrange in formation (line, wedge, column)
- Each unit walks to its offset position, not all to the same point
- Units maintain spacing while moving

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
