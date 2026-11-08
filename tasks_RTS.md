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

### Part B — Resource Economy (`ResourceManager.cs`) [ ]
- Global variables: Minerals, Gas, SupplyUsed, SupplyMax, FoodCap, Ore, Gems, Power, RUs
- New triggers: `when Minerals becomes <n>`, `when Supply becomes <n>`, `when ResourcesChange`
- New conditions: `Minerals is higher than <n>`, `Supply Used is lower than Supply Max`,
  `can afford <building>` (inline cost check)
- New actions: `add <n> to Minerals`, `set Minerals to <n>`, `deduct <n> from Minerals`,
  `increase Supply Max by <n>`
- StarCraft minerals/gas/supply; Red Alert ore/gems/power; Homeworld RUs (resource units)

### Part C — Construction & Base Building (`ConstructionManager.cs`) [ ]
- New triggers: `when <building> is placed`, `when construction completes`,
  `when <building> is destroyed`
- New actions: `place <building> at <target>`, `cancel build`, `set build progress to <n>`
- New conditions: `<building> exists`, `<building> is under construction`,
  `enough Minerals for <building>`
- Grid-based vs free-form placement (see Open Questions)
- Red Alert-style base construction; StarCraft-style building placement; Homeworld shipyard/subsystems

### Part D — Production Queues & Unit Training [ ]
- New triggers: `when <building> finishes training <unit>`,
  `when <building> starts producing <unit>`, `when production queue is empty`
- New actions: `train <unit> at <building>`, `cancel training <unit>`,
  `set build time to <n> seconds`
- New conditions: `<building> is idle`, `queue count is <n>`, `me has build queue`
- StarCraft Barracks/CommandCenter training; Red Alert unit production; Homeworld strike-group fabrication

### Part E — Orders & Attack-Move (`OrderQueue.cs`) [ ]
- New triggers: `when order completes`, `when queued order starts`, `when path blocked`
- New actions: `attack-move to <target>`, `attack <target>`, `stop`, `hold position`,
  `add order to queue`, `clear orders`, `set rally point to <target>`
- Per-unit order state: `my current order`, `my order target`, `my order queue`
- StarCraft attack-move; Red Alert attack-move; Homeworld formations and strike-group orders

### Part F — Fog of War / Visibility (`FogOfWarManager.cs`) [ ]
- New triggers: `when area becomes revealed`, `when area becomes hidden`,
  `when <unit> enters fog`
- New actions: `reveal area around me by <radius>`, `hide area around me by <radius>`,
  `set visibility to <radius>`
- New conditions: `me is visible to enemy`, `me is hidden`
- Visibility mask per player (grid-based); units reveal a radius; buildings reveal footprint;
  explored persists vs reverts to fog (see Open Questions)
- Homeworld sensor range/stealth; StarCraft fog of war; Red Alert shroud

### Part G — Damage & Per-Unit Health [ ]
- Extend: `my Health` / `my MaxHealth` per-unit variables; `my Shield` / `my Armor` (optional)
- New triggers: `when my Health becomes 0`, `when my Health is lower than <n>`,
  `when message "damage <amount>" arrives`
- New actions: `set Health to <n>`, `add -<n> to Health`, `add <n> to Shield`,
  `set Shield to <n>`
- StarCraft unit HP/shields; Red Alert unit health; Homeworld subsystem damage

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

## Open Questions (carried from Docs/rts-event-manager-support.md)

1. Grid-based vs. free-form unit placement for buildings?
2. How many players (1v1, FFA, teams)?
3. Supply cap mechanics — hard limit or soft warning?
4. Fog of war: explored areas stay visible or revert to fog?
5. Control groups: 1–0 keys or click-assign?
6. Homeworld 3D movement: full 3D or 2.5D plane?
7. Red Alert power system: power deficit slows production?
