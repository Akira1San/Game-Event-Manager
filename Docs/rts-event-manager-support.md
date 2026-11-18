# RTS Game Support — EventManager Design Document

## Scope

This document describes the gaps and required additions to bring the EventManager framework (Flax Engine) to feature parity with an RTS game like StarCraft. The existing event system covers ~30% of RTS requirements out-of-the-box; the remaining ~70% needs new code foundations plus .events content.

## RTS Feature Gap Analysis

| RTS Feature | Current Coverage | Needed? | Priority |
|-------------|------------------|---------|----------|
| Unit movement & pathfinding | Partial (goto via NavMesh) | Needs expansion | High |
| Unit selection & control groups | None | New system | High |
| Resource economy (minerals/gas/supply) | None | New system | High |
| Base building & production queues | None | New system | High |
| Attack-move & order queues | None | New system | High |
| Fog of war / visibility | None | New system | High |
| Damage & per-unit health | Partial (Lives only, no per-unit HP) | New system | High |
| Enemy AI states (patrol/chase/attack) | Full | Reusable | Medium |
| Line-of-sight detection | Full | Reusable | Medium |
| Animation control | Full | Reusable | Medium |
| Sound effects | Full | Reusable | Medium |
| Global state (score/lives) | Partial (score/lives exist, no resources) | Extend | Medium |

## What Already Works for RTS

The following existing patterns map directly to RTS mechanics:

### Movement
```
# Unit move order
when me is near Target by 2
  stop moving

# Patrol route
when me appears
  goto random Waypoint speed 100
```

### Combat engagement
```
# Attack when in range
when me is near Enemy by 1.5
  play anim "Attack"
  send message "damage" to Enemy

# Line-of-sight acquisition
when me sees Player by 12
  set my mode to "attack"
```

### Basic health
```
when me appears
  set my Health to 100

when message "damage" arrives
  add -10 to my Health
  unless my Health is higher than 0
  destroy me
```

### Global state (partial)
```
# Score tracking works, but resources do not
when game starts
  set Score to 0
  set Minerals to 500   # new variable — needs documentation
```

## Architecture Decision: Hybrid Approach ✅

### Options Considered

| Option | Approach | Risk | Backward-Compatible? |
|--------|----------|------|---------------------|
| **1** — Separate `rts.events` only | Add RTS triggers/actions only in `rts.events` | Low | Yes |
| **2** — Core upgrade only | Add RTS features to `Triggers.cs`/`Conditions.cs`/`FlaxActions.cs` | Medium | Yes (if optional) |
| **3** — Hybrid (chosen) | Core upgrades + separate `rts.events` | Low | Yes |

### Why Hybrid?

- **Existing events never change:** `Player.events`, `Enemy.events`, `Game.events`, `Menu.events` all continue working exactly as-is
- **New features available everywhere:** Any `.events` file can adopt core RTS triggers (per-unit health, resources, selection) if desired
- **RTS has its own file:** `rts.events` uses the new core features plus all existing triggers
- **Zero breaking changes:** Unknown trigger/condition/action types parse gracefully — no crashes, no migration

### Core System Upgrades (Backward-Compatible)

Add new trigger/condition/action types to `Triggers.cs`, `Conditions.cs`, `FlaxActions.cs`:

**New TriggerKind additions** (parse gracefully if unknown):
```csharp
Selected,           // when <unit> is selected
Deselected,         // when <unit> is deselected
SelectionChanged,   // when selection count/identity changes
ControlGroupPressed,// when control group 1-0 is pressed
ControlGroupHeld,   // when control group 1-0 is held
OrderCompletes,     // when current order finishes
OrderStarts,        // when queued order begins
BuildCompletes,     // when construction/production finishes
BuildStarts,        // when construction/production starts
AreaRevealed,       // when area enters visibility
AreaHidden,         // when area leaves visibility
ResourceChanges,    // when Minerals/Gas changes
SupplyChanges,      // when Supply changes
```

**New ConditionKind additions** (parse gracefully if unknown):
```csharp
Selected,           // me is selected
CanAfford,          // has enough resources for cost
SupplyEnough,       // supply used < supply max
IsVisible,          // unit is revealed to player
IsHidden,           // unit is in fog
BuildProgress,      // construction percentage
QueueCount,         // number of pending orders
```

**New Action verb additions** (parse gracefully if unknown):
```csharp
select me           // select this unit
deselect me         // deselect this unit
set my control group to <n>   // assign to control group 1-0
add <n> to Minerals             // resource management
deduct <n> from Minerals
train <unit> at <building>      // production
attack-move to <target>         // combat order
attack <target>                  // close to attack range and hold there
gather <node>                    // walk a worker to a resource node
repair <target>                  // walk to a damaged friendly
stop                             // cancel current order
hold position                    // freeze movement
```

### Separate `rts.events` File

`rts.events` lives in `GameFiles/` alongside `Player.events`, `Enemy.events`, `Game.events`. It uses **both** existing triggers and the new core RTS features:

```
# RTS Global setup — uses existing global state pattern
when game starts
  set Minerals to 500          ← new resource variable (core)
  set Gas to 0                 ← new resource variable (core)
  set SupplyMax to 10          ← new resource variable (core)
  set SupplyUsed to 0          ← new resource variable (core)

# Selection — uses new core triggers
when Marine is selected        ← new core trigger
  show SelectionRing

when Marine is deselected      ← new core trigger
  hide SelectionRing

# Combat — uses existing triggers + new core conditions
when Marine sees Enemy by 12   ← existing trigger (line-of-sight)
  set my mode to "attack"

when Marine is near Enemy by 3 ← existing trigger (near check)
  play anim "Attack"           ← existing action
  send message "damage" to Enemy ← existing action

when my Health is lower than 30← new core condition
  play anim "Damaged"

# Production — uses new core triggers/actions
every 2 seconds
  add 5 to Minerals            ← new core action (resources)

when Barracks is idle
  unless Minerals is lower than 50   ← new core condition (can afford)
  train Marine at Barracks   ← new core action (production)
  deduct 50 from Minerals    ← new core action (resources)

when Barracks finishes training Marine   ← new core trigger
  spawn Marine at Barracks   ← existing action
  select Marine              ← new core action (auto-select)
```

### Existing Events Remain Unchanged

**`Player.events`** (arcade) — continues working, no changes:
```
when W is held                 ← existing trigger
  move forward speed 45        ← existing action
when Left Mouse is pressed     ← existing trigger
  spawn Bullet at me           ← existing action
unless my Invulnerable is true ← existing condition
```

**`Enemy.events`** (patrol/chase/attack) — continues working, no changes:
```
when me appears                ← existing trigger
  set my mode to "patrol"      ← existing action
when me sees Player by 8       ← existing trigger
  set my mode to "chase"       ← existing action
when me is near Player by 1.5  ← existing trigger
  play anim "Attack"           ← existing action
```

**`Game.events`** (global state) — continues working, no changes:
```
when game starts               ← existing trigger
  set Score to 0               ← existing action
when Score changes             ← existing trigger
  set text of ScoreLabel to Score   ← existing action
```

### Migration Path

No migration required. Adoption is opt-in:

1. **Week 1:** Add core trigger/condition/action types (parse gracefully)
2. **Week 2:** Create `rts.events` using new features
3. **Later:** Other game types can optionally adopt per-unit health, resources, selection

---

## Missing RTS Systems — Design

### 1. Unit Selection System

**New trigger kinds:**
- `when <unit> is selected`
- `when selection changes`
- `when control group <n> is pressed` / `when control group <n> is held`
- `when box select starts` / `when box select ends`

**New conditions:**
- `me is selected`
- `selection count is <n>`
- `my control group <n> is assigned`

**New actions:**
- `select me`
- `deselect me`
- `set my control group to <n>`
- `select all <unit type>`
- `select units near me by <dist>`

**UI integration:**
- Selection ring/highlight actor spawned per selected unit
- Control group indicators (1–0 keys)

### 2. Resource Economy

**New global variables:**
- `Minerals`, `Gas`, `SupplyUsed`, `SupplyMax`, `FoodCap`

**New triggers:**
- `when Minerals becomes <value>`
- `when Supply becomes <value>`

**New conditions:**
- `Minerals is higher than 50`
- `Supply Used is lower than Supply Max`
- `can afford <building>` (inline cost check)

**New actions:**
- `add 50 to Minerals`
- `set Minerals to 500`
- `deduct 100 from Minerals`
- `increase Supply Max by 8`

**Example pattern:**
```
when game starts
  set Minerals to 500
  set Gas to 0
  set SupplyMax to 10

when every 2 seconds
  add 1 to Minerals   # passive income (workers)

when Score changes   # keep HUD in sync
  set text of MineralLabel to Minerals
```

### 3. Base Building & Construction ✅ Implemented

**New triggers:**
- `when <building> is placed` (also `when me is placed`)
- `when construction completes` (also `when my construction completes` / `when <building> construction completes`)
- `when <building> is destroyed` (also `when me is destroyed`)

**New actions:**
- `place <building> at <target>` — free-form placement (default)
- `place <building> at <target> on grid [by <n>]` — opt-in grid placement
- `cancel build`
- `set build progress to <n>` / `set build progress of <building> to <n>`
- `add <n> to build progress` / `add <n> to build progress of <building>`

**New conditions:**
- `<building> exists`
- `<building> is under construction`
- `enough Minerals for <building>`
- `build progress is higher than <n>` (supporting form for the mirrored progress local)

**Placement:**
- Free-form is the default: the spawned clone keeps the target's position/rotation.
- `on grid` snaps the X/Z position to cell centres and reserves the building's footprint. Cell size comes from `BuildGridSize` (default `64`); footprint size comes from `<Building>GridWidth` × `<Building>GridDepth` (both default `1`, minimum `1`). An `on grid by <n>` suffix overrides `BuildGridSize` for that call.
- `<target>` can be any actor or `Mouse` (the world point under the cursor).
- Occupied cells reject the placement; `cancel build` and destruction release the reserved cells.

**Runtime behaviour:**
- `place` spawns the building template's clone, registers it with `ConstructionManager`, and raises `when <building> is placed` / `when me is placed`.
- `set build progress to <n>` and `add <n> to build progress` clamp to 0–100 and mirror the value into the building's `BuildProgress` local, which is the value `build progress is higher than <n>` reads. Without `of`, they act on the sheet's own actor; use `set build progress of <building> to <n>` / `add <n> to build progress of <building>` from a global sheet. A line with no acting building and no `of` warns once instead of silently doing nothing.
- `when construction completes` fires once per completed building; `when my construction completes` filters to the sheet's own building, and `when <building> construction completes` filters to a building template. Completion is idempotent: it does not refire while progress stays at `100`.
- `cancel build` stops construction, clears progress, and frees the grid cell without raising `when <building> is destroyed`; the bridge removes the placed actor silently. Destroying a registered building raises that trigger and clears its state.
- `<building> is under construction` is true from placement until completion or cancellation.
- `enough <resource> for <building>` reuses the data-driven cost globals (`BarracksCost`, `BarracksGasCost`, `BarracksSupplyCost`, …). A building that declares no cost is always affordable.

**Example pattern:**
```
# Place a Barracks on the global sheet
when Q is pressed
  unless enough Minerals for Barracks
  deduct 150 from Minerals
  place Barracks at Mouse on grid

# On the Barracks sheet
when me is placed
  loop anim "building"

when every 0.5 seconds
  unless build progress is higher than 99
  add 10 to build progress

when my construction completes
  loop anim "idle"
  show text "Barracks ready!"
```

### 4. Production Queues & Unit Training

**New triggers:**
- `when <building> finishes training <unit>`
- `when <building> starts producing <unit>`
- `when production queue is empty`

**New actions:**
- `train <unit> at <building>`
- `cancel training <unit>`
- `set build time to <n> seconds`

**New conditions:**
- `<building> is idle`
- `queue count is <n>`
- `me has build queue`

**Example pattern:**
```
when Barracks is idle
  unless Supply Used is equal to Supply Max
  train Marine at Barracks
  deduct 50 from Minerals

when Barracks finishes training Marine
  spawn Marine at Barracks
  select Marine   # auto-select new unit
```

### 5. Attack-Move & Order Queues

**New trigger kinds:**
- `when order completes`
- `when queued order starts`
- `when path blocked`

**New actions:**
- `attack-move to <target>`
- `attack <target>`
- `gather <node>`
- `repair <target>`
- `stop`
- `hold position`
- `add order to queue`
- `clear orders`
- `set rally point to <target>`

**Per-unit order state:**
- `my current order` (move / attack / build / hold / stop)
- `my order target`
- `my order queue` (implicit via triggers)

**Example pattern:**
```
# Attack-move click
when <target> is clicked
  clear orders
  attack-move to <target>

when order completes
  set my current order to "idle"
  loop anim "Idle"
```

### 6. Fog of War

**New triggers:**
- `when area becomes revealed`
- `when area becomes hidden`
- `when <unit> enters fog`

**New actions:**
- `reveal area around me by <radius>`
- `hide area around me by <radius>`
- `set visibility to <radius>`

**New conditions:**
- `me is visible to enemy`
- `me is hidden`

**Architecture:**
- Visibility mask per player (grid-based)
- Units reveal a radius around themselves
- Buildings reveal their footprint
- Explored areas persist; unexplored = fog

### 7. Damage & Per-Unit Health ✅ Implemented

**Per-unit variables:**
- `my Health` / `my MaxHealth`
- `my Shield` / `my MaxShield`
- `my Armor`

They are ordinary per-actor variables; `HealthManager` only intervenes so writes stay in range. Spellings `Health`, `HP`, `Hit Points`, `Max Health`, `Max HP`, `Shield`, `Shields`, `Max Shield`, `Armor`/`Armour` all resolve to the same stat, and the spelling the sheet already used (`my health` vs `my Health`, `my Max Health` vs `my MaxHealth`) is preserved — the multiword and run-together forms both parse and clamp.

**New triggers:**
- `when my Health becomes 0` — already worked through the generic `when <var> becomes <value>` watcher; it is now guaranteed to fire once, because health clamps at 0.
- `when my Health is lower than <n>` — a downward edge (also `my Shield` / `my Armor`, and the bare `Health is lower than <n>` form on a unit's own sheet). The first tick sets the baseline, and a heal back above the threshold re-arms it.
- `when message "damage <amount>" arrives` — the quoted text is a template: each `<slot>` captures one number from the arriving message and is written onto the receiving sheet as `my <slot>` plus `my DamageTaken`. A slot-less `"damage"` stays an exact name match.

**New actions:**
- `set Health to <n>` — clamped to `0..MaxHealth` (`set MaxHealth to <n>` lowers the current health with it)
- `add -<n> to Health` — the damage path: `my Armor` is subtracted, `my Shield` absorbs what is left, then the remainder comes off `my Health`, which never goes below 0
- `deduct <n> from Health` — the same damage path
- `add <n> to Health` — heals, clamped at `MaxHealth`
- `add <n> to Shield` / `set Shield to <n>` — clamped to `0..MaxShield`
- `set Armor to <n>` / `add <n> to Armor` — clamped at 0

The bare `Health` / `Shield` / `Armor` names address the acting unit's own stat; on a global sheet they still write the global variable of that name. A unit whose sheet never set any health variable keeps the old plain arithmetic (no clamping, no warnings), so existing `.events` files are unchanged.

**Example pattern:**
```
when me appears
  set my MaxHealth to 60
  set my Health to 60
  set my Armor to 1
  set my MaxShield to 10
  set my Shield to 10

when message "damage <amount>" arrives
  add -10 to my Health        # damage: armor, then shields, then health
  spawn Explosion at me
  play sound "explosion"

when my Health is lower than 30
  play anim "Damaged"

when my Health becomes 0
  destroy me
```

A single hit of any size is applied from the captured number with a negated value —
`-my`, `-other's`, or a plain global (`-Score`):
```
when message "damage <amount>" arrives
  add -my DamageTaken to Health   # armor, then shields, then health — the full pipeline
```
`set my Health to my Health - my DamageTaken` also works when the sheet wants to assign
directly (clamped at 0, but no armor/shield soaking).

## New Code Foundations Required

### A. Selection Component (`Selection.cs`)
Track per-actor selection state and broadcast selection change events to sheets.

### B. Order Queue Component (`OrderQueue.cs`)
Per-actor ordered list of pending commands with priorities (move, attack, build, hold).

### C. Resource Manager (`ResourceManager.cs`)
Global singleton tracking Minerals, Gas, Supply. Emits change events.

### D. Construction Manager (`ConstructionManager.cs`) ✅ Implemented
Building placement validation, build-time progress, construction completion events, and optional grid occupancy. Free-form placement is the default; `on grid` snaps to cell centres and reserves `<Building>GridWidth` × `<Building>GridDepth` cells using `BuildGridSize` (default `64`). Build progress is clamped to 0–100, mirrored into each building's `BuildProgress` local, and completion is raised once per build.

### E. Fog of War Manager (`FogOfWarManager.cs`)
Visibility grid per player, reveal/hide operations, unit vision updates.

### H. Health Manager (`HealthManager.cs`) ✅ Implemented
Per-unit health bounds and the damage pipeline (armor → shield → health), plus `MessagePattern.cs` for template message triggers (`"damage <amount>"`). Pure logic with no Flax types, so the event runtime and the tests drive it directly.

### F. Extended EventWorld (`IEventWorld`) ✅ Implemented

The RTS state the event language already reads is exposed as one query surface, so a HUD, minimap or AI layer asks the world instead of reaching into the managers:
- `GetSelection()` → `IReadOnlyList<object>`, in click order
- `GetSelectedCount()` → `int`
- `GetResource(string name)` → `double` (any spelling; an unset resource is 0)
- `IsRevealed(object actor, int playerId)` → `bool` (player 0 is the local player's grid)
- `RevealArea(int playerId, double x, double z, double radius)` / `HideArea(...)` → explicit circle for one player, engine-free coordinates instead of a `Vector3` so the interface stays testable
- `GetBuildProgress(object building)` → `double` 0–100, 0 for a non-building
- `GetOrderQueue(object unit)` → `IReadOnlyList<string>`, active order first

`DefaultEventWorld` answers empty/no-op, `FlaxWorld` forwards to the live `EventRuntime` (null-safe before the manager exists), and the runtime keeps thin accessors (`SelectedActors`, `SelectedCount`, `ResourceValue`, `IsRevealedFor`, `RevealArea`, `HideArea`, `BuildProgressOf`, `OrderQueueOf`) that both paths share.

### G. New TriggerKind Additions
Add to `TriggerKind` enum:
```csharp
Selected,
Deselected,
SelectionChanged,
ControlGroupPressed,
OrderCompletes,
OrderStarts,
BuildCompletes,
BuildStarts,
AreaRevealed,
AreaHidden,
ResourceChanges,
SupplyChanges,
HealthLow,          // when my Health is lower than <n>
```

### H. New ConditionKind Additions
Add to `ConditionKind` enum:
```csharp
Selected,
CanAfford,
SupplyEnough,
IsVisible,
IsHidden,
BuildProgress,
QueueCount,
```

## Example RTS .events Files

### Marine.events (ranged infantry unit)
```
# Selection
when me is selected
  show SelectionRing
  set text of SelectionLabel to "Marine"

when me is deselected
  hide SelectionRing

# Combat
when me is near Enemy by 4
  unless my mode is "attack"
  set my mode to "attack"
  stop moving
  loop anim "Attack"

when message "damage" arrives
  add -10 to my Health
  unless my Health is higher than 0
  destroy me
  spawn Explosion at me
  play sound "marine_death"

# Idle when nothing to do
when every 0.5 seconds
  my mode is "idle"
  unless me is selected
  loop anim "Idle"
```

### Barracks.events (production building)
```
when me appears
  set build progress to 0
  set my Health to 800

# Training queue
when me is idle
  unless Minerals is lower than 50
  train Marine at me
  deduct 50 from Minerals
  set my current order to "training"

every 1 seconds
  my current order is "training"
  add 25 to build progress
  unless build progress is higher than 100

when build progress is higher than 100
  spawn Marine at me
  set my current order to "idle"
  set build progress to 0
```

### CommandCenter.events (economy hub)
```
when game starts
  set Minerals to 500
  set Gas to 0
  set SupplyMax to 10
  set SupplyUsed to 0

every 2 seconds   # SCVs passively gather
  add 5 to Minerals
  unless Minerals is higher than 2000   # cap for demo
```

### Global.events (RTS global state)
```
when Supply Used becomes Supply Max
  show text "Supply capped! Build more Supply Depots."
  wait 3 seconds
  hide text

when Minerals becomes 0
  show text "Out of minerals!"
  wait 2 seconds
  hide text
```

## Implementation Roadmap

### Phase 1 — Core Selection & Health (Week 1)
- Add `my Health`/`my MaxHealth` per-unit variables
- Add `when my Health becomes 0` trigger
- Add `select me` / `deselect me` actions
- Add `me is selected` condition
- Selection ring visual feedback

### Phase 2 — Resources & Economy (Week 1-2)
- Global Minerals/Gas/Supply variables
- `when Minerals becomes <n>` trigger
- `deduct <n> from Minerals` action
- Can-afford condition

### Phase 3 — Building & Production (Week 2-3)
- ✅ Construction placement with `place <building>` (free-form default, `on grid` opt-in)
- ✅ Build progress and completion events
- Unit training queue at buildings (not yet — Task 18 Part D)
- Spawn unit at building on complete (not yet — Task 18 Part D)

### Phase 4 — Orders & Attack-Move (Week 3-4)
- Order queue per unit
- Attack-move trigger/action
- Stop/hold-position commands

### Phase 5 — Fog of War (Week 4-5)
- Visibility grid per player
- Unit vision radius reveal
- Explored area persistence

## Recommended .events File Naming

- `Marine.events` — ranged infantry
- `Zergling.events` — melee infantry
- `Barracks.events` — production building
- `CommandCenter.events` — economy HQ
- `SupplyDepot.events` — supply provider
- `Global.events` — global resources & game state
- `Selection.events` — shared selection UI

## Notes

- The existing `goto` NavMesh pathfinding can serve as RTS unit movement foundation.
- Enemy AI patterns from `Enemy.events` (patrol/chase/attack) are reusable for RTS enemy units.
- Camera follow from `EventsManager.UpdateCameraFollow` can be adapted for RTS camera panning.
- All new code should follow existing patterns in `Triggers.cs`, `Conditions.cs`, `FlaxActions.cs`, and `EventWorld.cs`.

## Open Questions

1. ~~Grid-based vs. free-form unit placement for buildings?~~ **Resolved:** free-form is the default; `place … on grid [by <n>]` opts into grid snapping with `BuildGridSize` (default `64`) and per-building `<Building>GridWidth` / `<Building>GridDepth` footprints.
2. How many players (1v1, FFA, teams)?
3. Supply cap mechanics — hard limit or soft warning?
4. Fog of war: explored areas stay visible or revert to fog?
5. Control groups: 1–0 keys or click-assign?