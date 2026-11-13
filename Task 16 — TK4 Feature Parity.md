## Task 16 — TK4 Feature Parity `[ ]`

Goal: implement remaining Tapan Kaikki 4 (TK4) features not yet covered, achieving full feature parity with the original SDL/C++ game. Depends on Tasks 1–8, 14.

### Task 16 Implementation Notes (API reference — do not re-research)

**Token-saving approach:** Reuse existing systems instead of building new ones:

| Feature | Reuse instead of rebuild |
|---------|-------------------------|
| Wander / Still | Existing `move toward`, `face`, `stop moving`, `walk speed` — track angle + ticks in `FlaxActions` per-actor state (like `ParticleStates`). No new component class needed. |
| Hearing / Sound | Existing `IEventWorld.HasLineOfSight` for occlusion; `play sound` + a static sound event bus (sound name, position, radius, frame); poll in `Step`. |
| Weapon & Ammo | Existing global `Variables` — `Weapon Data` as named variables (`Pistol Damage`, `Pistol Ammo`); `reload` = timer in per-actor state; `spawn Bullet` reads current weapon's bullet type from variables. |
| Typed Enemies | `when me appears → set my Speed to 2` etc. — no new component; stats are variables initialized by `enemy type <name>`. |
| Particles (footprint/blood/gibs) | Task 14 `play particle` bridge — reuse `ParticleStates` and `AssetNames.Key`. `spawn Gib` reuses existing `Spawn`. |
| Multiplayer | `player <n>` = actor name convention (`Player1`…`Player4`); `nearest player` = `IEventWorld.GetNearestActor(me, tag="Player")`; per-player variables (`Player1 Score`). |
| Level Progression | Existing `LoadSceneByName` for `next level`; global variables persist across scene loads (already implemented); stats as variables. |

**Confirmed Flax 1.13 API members (minimal set):**

| API | Use |
|-----|-----|
| `FlaxEngine.Random` / `System.Random` | Wander random angle 0-359°, random duration, footprint left/right, blood particle angles |
| `Actor.Position` / `Actor.Orientation` | Wander movement, face toward target, sound source position |
| `IEventWorld.HasLineOfSight` | Sound occlusion (doesn't travel through walls) |
| `Level.FindActor("Player<N>")` | Multiplayer player references |
| `LoadSceneByName(name)` | `next level` |

**Trigger/condition/action mappings:**

| Concept | Trigger | Condition | Action |
|---------|---------|-----------|--------|
| Wander | — | `me is wandering` | `wander speed <n>` |
| Still | — | `me is still` | `still` |
| Hearing | `when me hears <sound> by <dist>` | `me hears <sound> by <dist>` | — |
| Weapon | `when reload completes` | `weapon is <name>`, `ammo <name> is <n>`, `reloading` | `set weapon to <name>`, `set ammo <name> to <n>`, `reload` |
| Enemy type | — | `my type is <name>` | `enemy type <name>` |
| Particles | `when particle <name> ends` | `particle <name> is playing` | `play particle <name>`, `stop particle <name>` |
| Multiplayer | `when player <n> joins/dies/score changes` | `player <n> exists/is near me` | `send message to player <n>`, `spawn at player <n>` |
| Level | `when level starts/completes`, `when all enemies dead` | `level number is <n>` | `next level`, `set level to <n>`, `save/load stats` |

**Data-driven design:** All enemy types, weapons, and stats live as named variables (no new data classes). `enemy type "Zombie"` → sets `my Speed=2`, `my SightDistance=10`, `my Health=50`, `my Weapon="Claws"`, `my Hostile=true` via variables.

### Part A — Random-Angle Wander & Still Mode
- [x] Word registry: `wander speed <n>` — pick random angle 0-359°, walk straight for random duration (20-60 ticks), repeat; `still` — stop moving but `face <target>` rotates toward target.
- [x] Parser: new action verbs `wander`, `still`; `wander` takes optional `speed` and `duration` qualifiers.
- [x] Runtime: `FlaxActions` tracks wander state per actor (current angle, ticks remaining); `still` = `stop moving` + continuous `face` toward nearest player.
- [x] Condition: `me is wandering`, `me is still`.
- [x] Unit test: wander changes angle periodically; still enemy rotates to face player.
- [x] Example: `Zombie.events` uses `wander`; `Turret.events` uses `still` + `face Player`.

### Part B — Hearing / Sound Reaction System
- [ ] Word registry: trigger `when me hears <sound> by <dist>` (edge: fires once when sound enters range + LOS), condition `me hears <sound> by <dist>` (polled).
- [ ] Sound event bus: `FlaxActions.EmitSound(soundName, position, volume, hearableRadius)` called by `play sound`, `spawn Bullet`, explosions; `EventsManager` tracks recent sounds per frame.
- [ ] `IEventWorld.HasLineOfSight` reused for occlusion (sound doesn't travel through walls).
- [ ] Enemy reaction: `when me hears "gunshot" by 12 → set my mode to "chase" → move toward sound position`.
- [ ] Unit test: gunshot at position → enemy within range + LOS gets `Hears` trigger; behind wall → no trigger.
- [ ] Example: `Enemy.events` adds `when me hears "gunshot" by SightDistance → set my mode to "chase" → move toward sound`.

### Part C — Weapon & Ammo Data System
- [ ] Word registry: `weapon <name>` (set current weapon), `ammo <name>` (current ammo count), `reload` (start reload), `when reload completes`.
- [ ] Data: weapon definitions in `WeaponData.events` (or JSON) — bullet type, fire rate, spread, multiplier, reload time, sound, smoke, ammo per shot.
- [ ] Actions: `set weapon to <name>`, `add <n> to ammo <name>`, `set ammo <name> to <n>`.
- [ ] Conditions: `ammo <name> is <value>`, `weapon is <name>`, `reloading`.
- [ ] Runtime: `FlaxActions` resolves weapon data; `spawn Bullet` uses current weapon's bullet type; reload timer suspends shooting.
- [ ] Unit test: weapon switch changes bullet type; reload blocks fire; ammo decrements per shot.
- [ ] Example: `Player.events` — `when 1 pressed → set weapon to "Pistol"`; `when Left Mouse pressed → unless reloading → spawn Bullet at me → add -1 to ammo Pistol`.

### Part C.5 — Weapon Variants & Wall Impact (TK4: Uzi, Shotgun, Rockets, Grenades)
Goal: different weapons behave differently — shotgun fires multiple pellets with spread, Uzi fires fast with little spread, rockets/grenades explode on wall impact, bullets bounce or stop on walls. TK4 `CBullet.cpp` drives this per bullet type: speed, angleAdd (spread), bulletAmount (pellets per shot), power (damage), pushPower (knockback), bounceType, time (lifetime), slow (deceleration), collisionTrigger (walls stop it), effect (particle on hit). Depends on Task 16 Part C (weapon/ammo data) and Task 16 Part E (particles).

- [ ] Word registry: `spread <n>` (pellet spread angle for shotgun), `bounce` (bullet bounces off walls), `explosive` (explodes on wall impact), `pierce` (bullet passes through enemies/walls).
- [ ] Data: weapon definitions in `WeaponData.events` — per weapon: bullet type, speed, spread, pellet count, fire rate, reload time, sound, smoke, ammo per shot, damage, knockback, bounce/explosive/pierce flags.
- [ ] Runtime: `spawn Bullet` reads current weapon's bullet type + spread; shotgun spawns multiple pellets at slightly different angles; Uzi has short reload + fast fire rate; rockets/grenades are slower with larger damage radius.
- [ ] Wall impact: `Bullet.events` — `when me touches Wall → destroy me` (default stop), or `bounce` → reflect angle and continue, or `explosive` → play particle "explosion" + damage nearby actors + destroy.
- [ ] Muzzle flash: `MuzzleFlash.events` already exists (particle + light on shoot); ensure it fires per weapon shot.
- [ ] Unit test: shotgun spawns N pellets with spread; Uzi fires faster than pistol; rocket explodes on wall (destroys itself + plays explosion particle); bullet bounces off wall when `bounce` set.
- [ ] Example: `Player.events` — `when 1 pressed → set weapon to "Pistol"`; `when 2 pressed → set weapon to "Shotgun"`; `when Left Mouse pressed → unless reloading → spawn Bullet at me → add -1 to ammo <weapon>`.

### Part D — Typed Enemy Definitions
- [x] Word registry: `enemy type <name>` (on `when me appears` or component) — applies base stats: speed, sight distance, health, default weapon, hostile flag, explosion death.
- [x] Data: `EnemyTypes.events` (or JSON) — e.g., `Zombie: speed 2, sight 10, health 50, weapon "Claws", hostile true`; `Turret: speed 0, sight 15, health 100, weapon "Cannon", hostile true, explosionDeath true`.
- [x] Runtime: `EventSheet` on enemy reads `enemy type` and initializes `my Speed`, `my SightDistance`, `my Health`, `my Weapon`, `my Hostile`, `my ExplosionDeath`.
- [x] Conditions: `my type is <name>`, `my health is lower than <n>`.
- [x] Unit test: spawning `Zombie` vs `Turret` applies different stats; `Turret` doesn't move but shoots.
- [x] Example: `Enemy.events` uses `my Speed`, `my SightDistance` instead of hardcoded values.

### Part E — TK4 Particle Effects (Footprint, Blood, Gibs)
- [ ] Integrates with Task 14 (Particle & Light Bridge).
- [ ] Footprint: `when me moves and me touches Floor → play particle "footprint" at feet` (random left/right).
- [ ] Blood: `when message "hurt" arrives → play particle "blood" at me` (16 particles, random angles).
- [ ] Gibs: `when my health becomes 0 → spawn Gib at me` (3x, random angle, velocity 2-4, lifetime 3s); `ExplosionDeath` variant: `play particle "explosion"` + burned gibs.
- [ ] Asset naming: `effects/footprint`, `effects/blood`, `effects/gib`, `effects/explosion`.
- [ ] Unit test: footprint triggers on floor contact; blood on hurt; gibs on death.
- [ ] Example: `Enemy.events` adds footprint/blood/gib triggers; `Turret.events` uses explosion death.

### Part F — Multiplayer Support (2 Players, Local)
Goal: 2 local players on one PC, each with their own actor (`Player1`, `Player2`), per-player key bindings set from an options menu, and optional joystick/gamepad input. Single screen (one camera) for now — split screen is deferred to its own task. Depends on Task 1–3, Task 16 Part H (player controls). TK4 reference: per-player `CKeys` bindings, local only, no networking.

- [ ] Word registry: `player <n>` (refers to player 1–2 only), `nearest player` (qualifier, picks closest of `Player1`/`Player2`), `all players` (for broadcast).
- [ ] Variables: `Player1 Score`, `Player2 Score`, … `Player1 Lives`, `Player2 Lives`; `my TargetPlayer` (set by vision/hearing).
- [ ] Triggers: `when player <n> joins`, `when player <n> dies`, `when player <n> score changes`.
- [ ] Conditions: `player <n> exists`, `player <n> is near me by <dist>`, `player <n> has tag <tag>`.
- [ ] Actions: `send message <msg> to player <n>`, `spawn <thing> at player <n>`.
- [ ] Cooperative mode: shared `Score`, `Lives` per player; `when all players dead → game over`.
- [ ] Deathmatch mode: `when player <n> kills player <m> → add 1 to Player<n> Kills`.
- [ ] Unit test: 2-player spawn → both have sheets; vision picks nearest; deathmatch scoring works.
- [ ] Example: `Game.events` tracks per-player score/lives; `Enemy.events` targets `nearest player`.
- [ ] Example scene: two actors `Player1` / `Player2`, each with an `EventSheet` component pointing at `TK_Player.events` (TK4 key controls), plus `TK_Enemy.events` for enemies.

### New todo tasks (input & split screen)
- [ ] **Input: per-player key bindings** — add a `PlayerInput` binding map per player (action → key/gamepad button), defaulting P1 to arrows+Space and P2 to WASD+Ctrl; route input so P1's keys hit Player1's sheet and P2's keys hit Player2's sheet (not broadcast to all).
- [ ] **Input: joystick/gamepad** — map gamepad buttons/axes to the same action words (`move`, `turn`, `shoot`) per player via Flax gamepad API.
- [ ] **Input: options menu rebinding** — UI scene listing each player's actions; capture a new key/gamepad button; save binding to the per-player map (stored as variables or a settings asset).
- [ ] **Split screen (deferred)** — multiple camera actors with viewport rects (side-by-side for 2 players), per-player `camera follows PlayerN` on each viewport; defer until single-screen 2-player is verified.

---

### Part G — Level Progression & Statistics
- [ ] Word registry: `next level` (loads next scene in build order), `level <name>` (current level name), `level number` (1-based).
- [ ] Statistics: `kills total`, `kills <enemyType>`, `accuracy`, `time elapsed`.
- [ ] Triggers: `when level starts`, `when level completes`, `when all enemies dead`.
- [ ] Actions: `add 1 to kills <enemyType>`, `set level to <n>`, `save stats`, `load stats`.
- [ ] Persistence: global variables survive scene load (already implemented); `wipe game variables` for new game.
- [ ] Unit test: kill 5 zombies → `kills Zombie` = 5; `next level` loads next scene; stats persist.
- [ ] Example: `Game.events` — `when all enemies dead → show text "Level complete!" → wait 2 → next level`.

### Part H — Player controls & camera (TK4 faithful)
- [x] Word registry: `turn left degrees <n>` / `turn right degrees <n>` (instant yaw on `me`), `when Left Arrow is held` / `when Right Arrow is held` / `when Up Arrow is held` / `when Down Arrow is held` / `when Space is pressed` — reuse existing key-press/held triggers and `turn` verb; no new grammar.
- [x] Parser: `turn` already parses `left`/`right` degrees; key-press triggers already parse. No new nodes.
- [x] Runtime: `FlaxActions.Turn` writes yaw only (`me.Orientation`, rotation-only, no position change); left = negative yaw, right = positive yaw (Flax sign already fixed in Task 12 D). Key-held input already flows through `EventsManager.InputEdges`.
- [x] Shooting: `when Space is pressed → spawn Bullet at me offset 30 → play sound "shot"`. Bullet inherits `me`'s facing so `move forward` flies where the player aims. The `offset <n>` qualifier is the 3D stand-in for TK4's `+14,+14` then forward-10 (XZ only, so it clears the shooter's body without lifting the shot).
- [x] Knockback: `Bullet.events` — `when me touches Enemy → push other back strength 100 for 0.2 seconds → send message "hit" → destroy me`; enemy/player hurt handlers already exist via `when message "hit" arrives` / `when me overlaps Enemy`. A bare `push` moves the target one tick (~0.3 cm) and is invisible, so the `for <n> seconds` qualifier is what makes knockback read like TK4.
- [x] Camera: `camera follows Player` — the follow existed, but it hard-coded a 45° tilt and did not centre the target. Framing now comes from `CameraHeight` / `CameraDepth` / `CameraPitch` globals with `CameraDepth = -CameraHeight / tan(CameraPitch)`; TK4 sets height 600, depth 0, pitch 90 (straight down, centred).
- [x] Unit test: key-held rotation changes `me.Orientation` yaw only; `Space` pressed spawns a bullet at the player facing its aim; bullet-on-enemy `push` applies knockback to the target.
- [x] Example: `TK_Player.events` — `Left Arrow`/`Right Arrow` rotate, `Up Arrow`/`Down Arrow` move, `Space` shoots; `TK_Bullet.events` pushes on hit then dies.

### Part I — Remaining TK4 Enemy Types (Task 16 Part D follow-up)
Goal: add the remaining TK4 enemy types beyond `Zombie` and `Turret`, each with TK4-authentic stats (speed, health, sight distance, weapon, hostile flag, explosion death). TK4 defines 10 enemy types total; Part D covered Zombie + Turret as built-ins. This part adds the other 8 as either built-ins in `EnemyTypes.cs` or data rows in `EnemyTypes.events`, plus any per-type enemy sheets (e.g. a Turret that stays still and faces the player, a Civilian that flees when shot). Depends on Task 16 Part D (typed enemy definitions).

- [ ] Word registry: no new words — `enemy type <name>` and `my type is <name>` already exist from Part D.
- [ ] Data: add the 8 missing TK4 types to `EnemyTypes.events` (`Pistolboy`, `ShotgunManiac`, `UziRebel`, `Commando`, `BazookaBoy`, `Civilian`, `Punisher`, `Flamer`, `Punk`, `GrenadeGay`) — or add them as built-ins in `EnemyTypes.cs` if data-driven globals aren't enough.
- [ ] Stats: TK4-authentic values per type — speed, health (energy), sight distance, current weapon, hostile/non-hostile, explosion death on kill.
- [ ] Per-type sheets: `Turret.events` uses `still` + `face Player` + shoots (speed 0, never wanders); `Civilian.events` flees when shot (turns away from bullet origin, `EWanderAround` away from shooter).
- [ ] Unit test: each of the 10 types applies distinct stats via `enemy type "<name>"`; `Civilian` is non-hostile (`my Hostile is false`); `Turret` speed 0 never wanders.
- [ ] Example: `Enemy.events` already dispatches on `my type` — verify a spawned `Turret` and a spawned `Zombie` behave differently (Turret stationary, Zombie patrols).