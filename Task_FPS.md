# Task: FPS Controls — First-Person Player Loop (Phase 1 of Deus Ex: HR build)

Goal: turn the Event Manager from a top-down shooter into a first-person player controller
suitable for an immersive sim / Deus Ex: Human Revolution-style game. Depends on Tasks 1–3
(parser, runtime, bridge) being code-complete. Moves the camera with the mouse, resolves movement
relative to view direction, and adds crouch/sprint/jump/stance. All player logic is written in the
event language; the bridge supplies the FPS-specific C# that existing words cannot express.

> Related: `TASKS.md` → Task 4 (Top-Down Shooter) already proves movement/shooting exist; this
> task reframes them for first-person and adds stance/camera words.
> Source of truth for language behavior: `DESIGN.md`, `keywords.html`.
> Target engine: Flax 1.13 via C# scripting (`EventManager/EventManager/`).

## Status
- [x] Phase 1 shipped: mouse look, first-person camera mode, `lock cursor`, `FPS_Player.events`
  and 15 unit checks. Still open from Phase 1: the editor play-check, and Phase 2–5 (stance,
  sprint stamina, ADS/FOV, gunplay, the acceptance scene).

## Context / reuse (already in repo)
- [ ] `Runtime/PhysicsMovement.cs` (Task 12): `MoveSubject`, body modes (Transform/Dynamic/Kinematic/CharacterController), `MaxStepPerTick`, `MoveStep`, ground check, fall-speed decaying — reusable for the FPS body.
- [ ] `Bridge/FlaxActions.cs`: `MoveSubject`, `turn`, `look at`, `face`, `push`, `goto`, `teleport`, `spawn`, `stop moving` — the movement verbs already exist; FPS needs a camera-relative direction resolver and a look input.
- [ ] `Bridge/EventsManager.cs` + `Bridge/EventSheet.cs`: input edges (`IsKeyHeld`, `GetAction`, `MouseScrollDelta`), polled/pushed trigger split, once-per-tick, `me`/`other` routing.
- [ ] `Runtime/EventRuntime.cs`: tick engine (resume waits → pushed queues → polled), `SheetInstance.UsePhysicsMovement` flag, `EventContext.Sheet`.
- [ ] `Bridge/FlaxWorld.cs` (`IEventWorld`): `NameOf`, `HasLineOfSight`, `DistanceToPoint`, `IsTouching`, `Actors` — sight/sound detection already wired.
- [ ] `Runtime/ValueEvaluator.cs` + `Runtime/EventVariables.cs`: `my Stamina`, `my Health`, `my AugEnergy` vars; `add`/`set`/clamps.
- [ ] `Runtime/WeaponMath`/`Weapons.cs` (Task 16): `WeaponClock`, ammo, reload, spread — reusable for gunplay.
- [ ] `.events` files live in `Source/Game/GameFiles/`; scenes under `Content/scenes/`.
- Project target: `EventManager/EventManager/EventManager.flaxproj` (Flax 1.13; see `Docs/FlaxSetup.md`).

## FPS words to add (event language)

These are the *new* trigger/action/condition words this task introduces. Everything else
(move, spawn, send message, play sound, hide/show, bind, anim) is reused unchanged.

### Triggers
- [ ] `when <axis> is moved` — continuous mouse/analog look input (pitch/yaw delta each tick).
- [ ] `when <key> is pressed / held / released` — already exist; FPS uses W/A/S/D + Shift + Ctrl + Space + mouse buttons.
- [ ] `when me is on ground` / `when me is airborne` — stance/gating query (exposes existing ground check).

### Actions
- [ ] `set look sensitivity to <n>` / `set look invert <axis> to true` — camera feel knobs (writes globals `LookSensitivity` / `LookInvertX` / `LookInvertY`).
- [ ] `crouch` / `stand` / `toggle crouch` — stance change; shrinks CharacterController height/radius, slows speed.
- [ ] `sprint` / `walk` — pace toggle; reads/drains `my Stamina`.
- [ ] `jump [strength <n>]` — vertical impulse (reuses Task 7 G4 phase 3 ground query).
- [ ] `aim down sights` / `stop aiming` — FOV zoom + accuracy (writes `my Aiming` flag).
- [ ] `hide cursor` / `show cursor` / `lock cursor` — FPS mouse capture.

### Conditions
- [ ] `my Stance is "stand" / "crouch" / "prone"` — stance read.
- [ ] `my Aiming is true / false` — ADS state read.
- [ ] `my Stamina is higher/lower than <n>` — sprint gate (existing var read).

## Bridge C# to write

- [x] `Runtime/MouseLook.cs` (pure): yaw/pitch accumulation from mouse delta, pitch clamp (±89°), yaw wrap, sensitivity/invert applied, per tick. Unit tested. (Shipped under this name instead of `FPSLook.cs`.)
- [x] `EventsManager.UpdateFirstPersonCamera()`: reads `Input.MousePositionDelta` each tick, applies yaw to `me.Orientation` (world Y only) and pitch to the player's own camera child (`LocalOrientation`, local X). The mode lives in `CameraState` (`FirstPerson`), so it releases/absorbs the other camera rigs like they do; the child camera is found with `FlaxWorld.FindCameraUnder`. Enabled by the `First Person` tick box on the player's `EventSheet` (not a per-sheet instance field in FlaxActions).
- [ ] `Bridge/FlaxActions.MoveSubject` direction override: when the actor is the FPS player and `MoveRelativeToCamera` is set on the sheet, resolve `forward/back/left/right` from the *camera view direction* (flattened to XZ), not the actor's facing. `face`/`look at` stay actor-relative (unchanged).
- [ ] `Bridge/FlaxActions.Crouch/Stand/ToggleCrouch()`: adjusts `CharacterController` height/radius (or scales the actor transform in transform mode), writes `my Stance`, clamps speed.
- [ ] `Bridge/FlaxActions.Sprint/Walk()`: speed multiplier read from `my SprintSpeed` (default 2×), drains `my Stamina` each tick while held (5 per 0.1 s), regenerates when released (4 per 0.25 s), falls back to walk at 0.
- [ ] `Bridge/FlaxActions.Jump(strength)`: vertical impulse per body mode (CharacterController fall-speed write / dynamic `LinearVelocity.Y` / kinematic `JumpStep` arc); refused while airborne; default 350 cm/s.
- [ ] `Bridge/FlaxActions.AimDownSights()` / `StopAiming()`: writes `my Aiming`, narrows camera FOV (or moves camera closer) and reduces weapon spread while active.

## Files to create / edit
- [x] `Source/Game/GameFiles/FPS_Player.events` — the first-person player sheet (capture + move + run gate + jump; crouch/ADS/shoot pending Phases 2–4).
- [ ] `Source/Game/GameFiles/FPS_Gun.events` (optional, reuses `TK_Bullet.events` pattern) — bullet fired from camera center, muzzle flash, damage on hit.
- [ ] `Content/scenes/FPS_Test.scene` — player actor with `EventSheet → FPS_Player.events`, camera as child, floor + crates + one enemy, `EventsManager` on scene-root `Game`.
- [x] `Source/Game/Runtime/MouseLook.cs` (pure) + unit tests (paths corrected: there is no `Events/Runtime`).
- [x] `Source/Game/Runtime/CameraState.cs` — first-person mode beside follow / at / aim / free.
- [x] `Source/Game/Bridge/EventsManager.cs` — the per-tick look rig; `Source/Game/Bridge/FlaxWorld.cs` — the player's own camera lookup.
- [ ] `Crouch/Stand`, `Sprint/Walk`, `AimDownSights` branches (Phases 2–4).
- [ ] `WordRegistry.cs` — register the new trigger/action/condition entries with examples.
- [ ] `EventParser.cs` — accept the new verb shapes + `set look sensitivity` / `set look invert`.
- [ ] `keywords.html` — add rows for new words.

## Phase plan
### Phase 1 — Camera + look (mouse look)
- [x] `MouseLook.cs` pure rules + unit tests (yaw/pitch clamp, yaw wrap, sensitivity/invert, zero delta).
- [x] Look rig in the bridge: mouse delta each tick, yaw on player Y, pitch on the camera child; `lock cursor` / `unlock cursor` (`Screen.CursorLock`), with the drawn cursor suppressed while captured and the pointer always restored on stop.
- [x] `when <axis> is moved` — **deliberately not shipped**: look is continuous state, and a pushed trigger per mouse tick would be noise; the rig polls the delta each tick, which is the project's model.
- [x] `FPS_Player.events`: `when game starts → lock cursor` (plus WASD, a Shift run gate, Space jump and Escape to release the pointer).
- [ ] Editor check: look around with the mouse, cursor locked, pitch clamped. (Needs a camera parented under the player with `First Person` ticked.)

### Phase 2 — Camera-relative movement + stance
- [ ] `MoveRelativeToCamera` opt-in flag on `SheetInstance` (like `UsePhysicsMovement`); no new word.
- [ ] `FlaxActions.MoveSubject`: resolve move dir from camera forward/right when flag set; actor rotation unaffected by strafe.
- [ ] `crouch` / `stand` / `toggle crouch` actions + `my Stance` condition; `CharacterController` height/radius shrink + speed clamp.
- [ ] `FPS_Player.events`: W/A/S/D → `move ... speed my Pace`; `Left Ctrl` → crouch; `Left Shift` → sprint.
- [ ] Editor check: WASD moves relative to where you're looking; crouch lowers the camera/collider.

### Phase 3 — Sprint/stamina + jump
- [ ] `sprint` / `walk` actions; stamina drain/regenerate loop; `my Stamina` gate blocks sprint at 0.
- [ ] `jump [strength <n>]` action + `me is on ground` / `me is airborne` conditions (expose existing ground check).
- [ ] `FPS_Player.events`: Space → jump (gated on on-ground); stamina HUD labels via `bind`.
- [ ] Editor check: sprint drains stamina, jump only when grounded, no double jump.

### Phase 4 — Aim down sights + gunplay
- [ ] `aim down sights` / `stop aiming` + `my Aiming` condition; FOV zoom + spread reduction.
- [ ] `FPS_Player.events`: Right Mouse held → ADS; Left Mouse → spawn bullet from camera center + `play sound "shot"`.
- [ ] Reuse `TK_Bullet.events` damage-on-contact; `send message "hit"` to nearest enemy.
- [ ] Editor check: ADS zooms, hip vs aimed spread feel different, shooting damages enemy.

### Phase 5 — Acceptance scene
- [ ] `FPS_Test.scene`: floor, cover crates, one `Enemy` (reuse `Enemy.events` hurt-on-contact), `SpawnPoint`, `EventsManager` on root.
- [ ] Play: move, look, crouch, sprint, jump, shoot, kill enemy, see feedback.
- [ ] File any vocabulary friction to `DESIGN.md §9`.

## Done-when (acceptance)
- [ ] Mouse look works: yaw rotates the player, pitch rotates the camera child, pitch clamped, sensitivity/invert knobs change feel.
- [ ] WASD moves relative to camera view; strafing does not rotate the actor.
- [ ] Crouch lowers camera + collider and slows pace; sprint drains `my Stamina` and blocks at 0.
- [ ] Jump works only while `me is on ground`, no chained jumps while holding the key.
- [ ] Aim down sights zooms/narrows spread; firing spawns a bullet from camera center that damages an enemy.
- [ ] All new unit tests green; Flax module build 0 errors; editor play-check in `FPS_Test.scene` clean.

## Risks / open questions
- [ ] Flax 1.13 `Input` API for raw mouse delta vs action-mapped axes — confirm `Input.MouseDelta` or `Input.GetAxis("Mouse X/Y")` shape before writing `FPSCamera`.
- [ ] Camera-as-child-of-player: pitch rotation must not roll the player; verify the pivot node is a separate child actor so only its local pitch changes.
- [ ] CharacterController height shrink on crouch may intersect the floor; clamp the minimum height or accept a small crouch offset.
- [ ] `move forward` currently resolves from actor facing (DESIGN §5); the camera-relative override is opt-in via `MoveRelativeToCamera` so existing top-down sheets are unchanged.
- [ ] `hide cursor`/`lock cursor`: Flax cursor API surface to confirm (`Input.SetMouseCursorVisible`, cursor lock mode).
- [ ] Note vocabulary friction in `DESIGN.md §9` (e.g. wanting `move relative to camera` as a word instead of a flag).

## Session log hook
- [ ] When parts land, add a line to `Session Log.md` (date, "Task_FPS.md — Phase N", files touched).
- [ ] Update `TASKS.md` to reference `Task_FPS.md` as the FPS build track.

---

# Phase 2 — Augmentations & Praxis (core Deus Ex identity)

Goal: active augs (cloak, speed, jump, wall-punch, taser), passive augs (health, recoil, hacking), an
energy pool, and Praxis points to unlock/upgrade. All aug state lives in per-actor variables so the
event language drives it; a C# `AugManager` owns active-aug ticking and unlock gating.

Depends on Phase 1 (FPS controls) and Task 3 (variables + runtime).

### New words
- [ ] `set my Praxis to <n>` / `add <n> to my Praxis` — spendable unlock currency.
- [ ] `my Aug <name> is <level>` / `set my Aug <name> to <level>` — per-aug level read/write.
- [ ] `when my Aug <name> becomes <level>` — unlock trigger.
- [ ] `activate <aug name>` / `deactivate <aug name>` — toggle an active aug.
- [ ] `when <aug name> is active` / `when <aug name> is not active` — condition.
- [ ] `set my AugEnergy to <n>` / `add <n> to my AugEnergy` — energy pool.
- [ ] `when my AugEnergy is lower than <n>` — low-energy condition.
- [ ] `set my Aug <name> cooldown to <n>` / `when <aug name> cooldown ends`.
- [ ] `spend <n> Praxis on <aug name>` — combined unlock + deduct action.

### Bridge C#
- [ ] `Runtime/AugManager.cs` (pure, unit tested): dictionary aug name → `{level, active, costPerSec, cooldownRemaining, prerequisites}`; `CanUnlock`, `Unlock`, `SetActive`, `TickEnergy(dt)`.
- [ ] `Bridge/FlaxActions`: `ActivateAug(name)` / `DeactivateAug(name)`; energy deducted each tick while active, auto-deactivates at 0; `spend Praxis` validates level caps + prereqs then deducts.
- [ ] Aug effects wired to existing words: **Cloak** → `hide me` + reduces enemy sight radius; **Speed** → move-speed multiplier; **Jump** → vertical impulse on `jump`; **Taser** → ranged non-lethal `send message "stun"`.
- [ ] `WordRegistry.cs` + `EventParser.cs`: new trigger/action/condition entries; `keywords.html` rows.

### Files
- [ ] `Source/Game/GameFiles/Augs.events` — global Praxis/aug sheet.
- [ ] `Source/Game/GameFiles/Player_Augs.events` — per-player aug activation (e.g. `when F is pressed → activate Cloak`).
- [ ] Unit tests: unlock gating, energy drain to 0 deactivates, prereq chain, cloak sight-radius multiplier.

### Done-when
- [ ] Praxis earned → spent → aug level rises; `when my Aug Cloak becomes 1` fires.
- [ ] Activate Cloak → player hidden + enemies detect at shorter range; energy drains, deactivates at 0.
- [ ] Speed aug multiplies move speed; Jump aug raises jump strength.
- [ ] `spend` blocked when Praxis insufficient or prereq missing (friendly warning, no crash).

---

# Phase 3 — Stealth, Detection & Cover

Goal: guard alert states (idle/suspicious/alert/combat), line-of-sight + sound-based detection,
shadow/light visibility, cover snap/peek/lean/blind-fire, takedowns (lethal/non-lethal), body dragging
and hiding. Reuses Task 7 (`sees`/`stops seeing`/`near`) and Task 16 Part B (sound bus).

Depends on Phase 1 + Phase 2 (cloak reduces sight radius).

### New words
- [ ] `set my AlertLevel to <n>` / `my AlertLevel is higher/lower than <n>` — guard alert state.
- [ ] `when my AlertLevel becomes "combat"` / `"suspicious"` / `"idle"` — escalation triggers.
- [ ] `when me is in shadow` / `when me is in light` — lighting visibility (light-probe sample).
- [ ] `snap to cover <thing>` / `unsnap cover` — cover attach/detach.
- [ ] `peek left` / `peek right` / `center` — cover leaning.
- [ ] `blind fire <weapon>` — shoot from cover without line of sight.
- [ ] `when me is snapped to cover` / `when me is not snapped to cover` — cover condition.
- [ ] `perform takedown <lethal|nonlethal>` — anim + outcome.
- [ ] `drag body <thing>` / `hide body in <thing>` — body disposal.
- [ ] `send alarm to <security system>` — escalate cameras/turrets.
- [ ] `disable <camera>` / `hack <camera>` — security bypass.

### Bridge C#
- [ ] `Runtime/StealthSystem.cs` (pure): per-NPC `AlertLevel` state machine; sight radius narrows in dark / widens in light; sound-radius hearing from the existing bus.
- [ ] `Runtime/CoverSystem.cs`: raycast from cover point to find valid cover surfaces; snap player to socket; peek offsets.
- [ ] `Bridge/FlaxActions`: `SnapToCover` / `UnsnapCover`; `PeekLeft/Right/Center` (camera offset + aim ray from peek position); `BlindFire` (accuracy penalty); `PerformTakedown` (anim + damage or sleep + `my Conscious = false`); `DragBody` (parent body to player, move together); `HideBodyIn` (move into container, disable collision, disable sight/sound).
- [ ] `when me is in shadow/light`: bridge samples scene lighting at player position (light-probe distance or simple light list).
- [ ] `WordRegistry.cs` + `EventParser.cs` + `keywords.html` rows.

### Files
- [ ] `Source/Game/GameFiles/Guard.events` — alert-state brain (patrol → suspicious → combat → search → patrol).
- [ ] `Source/Game/GameFiles/Player_Stealth.events` — cover snap/peek, takedown, body drag/hide.
- [ ] `Content/scenes/Stealth_Test.scene` — guard patrol route, dark/light patches, cover crates, dumpster, player.
- [ ] Unit tests: alert escalation, shadow narrows sight, cover peek shifts aim ray, takedown non-lethal sets conscious false, body hidden removes from sight.

### Done-when
- [ ] Guard patrols, spots player (`sees`), escalates to combat, loses sight → search → patrol.
- [ ] Player in shadow is detected later than in light.
- [ ] Snap to cover → peek left/right shifts camera + aim; blind fire works without LOS.
- [ ] Non-lethal takedown puts guard to sleep; body can be dragged into dumpster and hidden.

---

# Phase 4 — Dialogue & Branching Conversations

Goal: NPC talk with dialogue lines, choices, stat/aug gates, portraits, VO, disposition tracking,
and conversation-node jumping. Composed from existing UI words (`show`/`hide`/`set text of`/`bind`)
plus a small set of new dialogue words. Reuses Task 6 (menus/UI) and Task 15 (binds/progress).

Depends on Phase 1 + Phase 2 (aug gates for dialogue).

### New words
- [ ] `when <npc> is talked to` — dialogue start trigger (replaces `when me is clicked` for talk).
- [ ] `show dialogue "<line>"` / `show dialogue of <npc>` — centre-screen subtitle.
- [ ] `show choice "<text>" goto <label>` / `hide choice "<text>"` — dialogue options.
- [ ] `set dialogue branch to "<label>"` / `goto dialogue "<label>"` — jump to conversation node.
- [ ] `when choice "<text>" is picked` — choice selection trigger.
- [ ] `if player has aug <name>` / `if my <stat> is higher than <n>` — dialogue gate conditions (reuse `if`/`unless`).
- [ ] `show portrait "<npc>"` / `hide portrait "<npc>"` — face display.
- [ ] `play voice "<npc>_<line>"` — VO tied to a dialogue line.
- [ ] `set my <npc> disposition to <n>` / `my <npc> disposition is higher than <n>` — NPC attitude.
- [ ] `end dialogue` — close conversation UI.

### Bridge C#
- [ ] `Runtime/DialogueGraph.cs` (pure): node-based data (lines, choices, conditions, next-node IDs) loaded from a text/JSON asset.
- [ ] `Bridge/FlaxActions`: choice UI buttons created from dialogue choices; picking one sets `dialogue choice = <id>` and pushes a `choice picked` event; portrait/VO playback.
- [ ] Condition evaluation against player vars, aug levels, inventory, past flags (persistence).
- [ ] Typewriter text effect via Task 15 Tweens (character-by-character reveal).
- [ ] `WordRegistry.cs` + `EventParser.cs` + `keywords.html` rows.

### Files
- [ ] `Source/Game/GameFiles/Dialogue_Data.json` (or `.events` data) — conversation nodes for one NPC.
- [ ] `Source/Game/GameFiles/NPC_Talk.events` — dialogue sheet.
- [ ] `Content/scenes/Dialogue_Test.scene` — NPC actor, dialogue panel, choice buttons, portrait.
- [ ] Unit tests: choice picked → branch jumps; aug gate hides/shows choice; disposition changes dialogue line.

### Done-when
- [ ] Talk to NPC → dialogue panel + choices appear.
- [ ] Picking a choice jumps to the correct node; aug-locked choice hidden until aug unlocked.
- [ ] Disposition changes unlock different lines; `end dialogue` closes the UI.
- [ ] VO plays on line; portrait shows the NPC face.

---

# Phase 5 — Hacking Minigame & Security

Goal: terminal hacking (capture nodes, avoid detection, difficulty, aug modifiers), security cameras,
turrets, alarm panels, laser grids. Reuses Task 15 (UI binds/progress/sliders) for the hacking UI.

Depends on Phase 1 + Phase 2 (hacking aug reduces detection).

### New words
- [ ] `when <terminal> is hacked` — hack start trigger.
- [ ] `open hacking UI` / `close hacking UI` — show/hide minigame.
- [ ] `capture node <name>` — claim a node.
- [ ] `when hack detected` / `when hack complete` / `when hack failed` — outcome triggers.
- [ ] `set hack difficulty to <n>` / `my hack level is <n>` — difficulty + player hack aug.
- [ ] `add <n> to my hack progress` — progress bar.
- [ ] `spend <n> my AugEnergy on hack` — aug-assisted hack.
- [ ] `when <camera> sees me` — camera detection (cone FOV, distinct from NPC sight).
- [ ] `disable <camera>` / `enable <camera>` — hack or EMP bypass.
- [ ] `when <turret> targets me` / `turret <thing> fire at me` — turret lock-on/shooting.
- [ ] `when alarm is raised` / `when alarm is silenced` — global alarm state.
- [ ] `raise alarm` / `silence alarm`.
- [ ] `laser grid <thing> is active` / `deactivate <laser grid>` — hazard toggle.

### Bridge C#
- [ ] `Runtime/HackingGame.cs` (pure): node graph (capture nodes, detection paths, datastores), capture race, detection timer, difficulty scaling.
- [ ] `Runtime/SecuritySystem.cs`: `SecurityCamera` (cone FOV + range raycast, detection meter), `Turret` (tracks player, fires projectiles on lock, disable/hackable), `AlarmSystem` (global raised/silenced), `LaserGrid` (damage-over-time volume).
- [ ] `Bridge/FlaxActions`: hacking UI nodes as buttons/sliders; `CaptureNode`; camera detection meter; turret aim+fire; alarm toggles security behavior; laser grid damage tick.
- [ ] Hacking aug: reduces detection time, reveals paths, or auto-captures nodes.
- [ ] `WordRegistry.cs` + `EventParser.cs` + `keywords.html` rows.

### Files
- [ ] `Source/Game/GameFiles/Hacking.events` — terminal hack sheet.
- [ ] `Source/Game/GameFiles/Security.events` — camera/turret/alarm/laser sheets.
- [ ] `Content/scenes/Security_Test.scene` — camera, turret, alarm panel, laser grid, hackable terminal, player.
- [ ] Unit tests: capture race wins before detection; difficulty scales node count; disabled camera stops detecting; alarm raised → turrets aggressive.

### Done-when
- [ ] Hack a terminal → node graph appears; capture nodes before detection → success reward.
- [ ] Camera sees player → detection meter fills → alarm raised.
- [ ] Disable camera → it stops detecting.
- [ ] Turret targets and fires on lock; disabled turret stops firing.
- [ ] Alarm raised → all cameras/turrets switch to combat mode; silenced → back to idle.

---

# Phase 6 — Grid Inventory, Economy & Vendors

Goal: grid/Tetris inventory, weapon slots, ammo types, consumables, credits, buy/sell vendors,
crafting. The existing `my bag` (name+count) is reused as the text fallback; grid UI is new.

Depends on Phase 1 + Task 8 (items & bag).

### New words
- [ ] `add <thing> to my inventory at <x>,<y>` — grid slot placement.
- [ ] `remove <thing> from my inventory slot <n>`.
- [ ] `my inventory has <thing>` / `my inventory count of <thing>`.
- [ ] `set my Credits to <n>` / `add <n> to my Credits`.
- [ ] `buy <thing> for <n> credits` / `sell <thing> for <n> credits` — vendor transaction.
- [ ] `when <vendor> is traded with` — vendor open trigger.
- [ ] `show inventory` / `hide inventory` — grid UI toggle.
- [ ] `equip in slot <n>` / `unequip slot <n>` — weapon slot management.
- [ ] `combine <thing> with <thing> into <thing>` — crafting.

### Bridge C#
- [ ] `Runtime/GridInventory.cs`: 2D slot array, item width/height, overlap validation, drag-to-move.
- [ ] `Runtime/Economy.cs`: credits, vendor buy/sell price lists.
- [ ] `Runtime/CraftingRecipe.cs`: input items → output item, consumables consumed on craft.
- [ ] `Bridge/FlaxActions`: inventory grid UI (slot buttons, tooltips), vendor buy/sell dialogs, craft output.
- [ ] `WordRegistry.cs` + `EventParser.cs` + `keywords.html` rows.

### Files
- [ ] `Source/Game/GameFiles/Inventory.events` — player inventory sheet.
- [ ] `Source/Game/GameFiles/Vendor.events` — buy/sell sheet.
- [ ] `Content/scenes/Inventory_Test.scene` — inventory panel, vendor NPC, loot crates.
- [ ] Unit tests: grid placement validates overlap; buy deducts credits + adds item; craft consumes inputs.

### Done-when
- [ ] Pick up items → they occupy grid slots; `my inventory count of <thing>` reads correctly.
- [ ] Talk to vendor → buy/sell UI; buying deducts credits and adds item to inventory.
- [ ] Combine components → crafted item appears, inputs consumed.
- [ ] Equip in weapon slot → weapon active; switching slots changes the held weapon.

---

# Phase 7 — Quests, Objectives & XP/Leveling

Goal: mission objectives, quest log, mission failure, branching outcomes, XP table, Praxis
awards. Reuses Task 16 Part G (levels/stats) for the scoring baseline.

Depends on Phase 1 + Phase 2 (Praxis awards).

### New words
- [ ] `set objective "<id>" to "<text>"` / `show objective "<id>"` / `hide objective "<id>"`.
- [ ] `complete objective "<id>"` / `fail objective "<id>"`.
- [ ] `when objective "<id>" is completed` / `when all objectives are completed`.
- [ ] `set mission state to "<state>"` — "active"/"failed"/"complete".
- [ ] `when mission state becomes "failed"` — game over / restart.
- [ ] `add journal entry "<text>"` / `show journal`.
- [ ] `add <n> XP` / `my XP is <n>` / `my XP to next level is <n>`.
- [ ] `when my XP becomes <n>` — level-up trigger.
- [ ] `gain <n> Praxis` / `my Praxis is <n>`.
- [ ] `award <n> XP for "<reason>"` — e.g. stealth takedown, hack success.

### Bridge C#
- [ ] `Runtime/QuestManager.cs` (pure): objective registry, completion conditions, quest log UI, failure states.
- [ ] `Runtime/ProgressionSystem.cs`: XP table, level-ups grant Praxis points.
- [ ] `Bridge/FlaxActions`: objective tracker UI, journal panel, mission-failure → `load scene` checkpoint or `restart scene`.
- [ ] XP reasons: stealth takedown, hack success, exploration, objective completion; non-lethal vs lethal multipliers.
- [ ] `WordRegistry.cs` + `EventParser.cs` + `keywords.html` rows.

### Files
- [ ] `Source/Game/GameFiles/Quests.events` — mission objectives sheet.
- [ ] `Source/Game/GameFiles/Progression.events` — XP/Praxis sheet.
- [ ] `Content/scenes/Mission_Test.scene` — objective markers, journal UI, failure trigger.
- [ ] Unit tests: objective complete → tracker updates; all objectives complete → mission state complete; XP threshold → level-up + Praxis.

### Done-when
- [ ] Mission start → objectives shown; completing all → "Mission complete!" + next level.
- [ ] Failing an objective → mission state failed → game over/restart.
- [ ] XP awarded for actions; crossing a threshold grants a level + Praxis.
- [ ] Journal shows entries; objective tracker lists active objectives.

---

# Phase 8 — Save / Load & Checkpoints

Goal: full persistent state (globals, per-object vars, inventory, aug levels, quest flags, NPC
states, positions), named checkpoints, quicksave/load. Extends Task 8 Part E (`save game`/`load game`
design already recorded in DESIGN §9).

Depends on Phase 1 + Phase 6 (inventory) + Phase 7 (quest flags).

### New words
- [ ] `save game "<slot>"` / `load game "<slot>"` — full state (extends `save stats`/`load stats`).
- [ ] `quick save` / `quick load` — slot 0.
- [ ] `set checkpoint "<name>"` / `respawn at checkpoint "<name>"` — named checkpoint + full world state.
- [ ] `when game saved` / `when game loaded` — post-save/load triggers.
- [ ] `delete save "<slot>"` / `save slot exists "<slot>"` — slot management.

### Bridge C#
- [ ] `Runtime/SaveFile.cs` (already designed in DESIGN §9 Parts A–D): JSON save at `Saves/<slot>.json`, schema versioning, atomic write via `.tmp` + replace.
- [ ] `Runtime/SaveGames.cs` (already designed): word handlers returning `Result` with friendly sentence; `when game saved`/`loaded` pushed via `RaisePushAll`.
- [ ] `Bridge/FlaxActions`: capture/apply transform (via `TeleportSubject`), capture/apply active flag, recreate spawned roots (prefabs then clones), merge per-object vars, replace bag/equipment.
- [ ] `WordRegistry.cs` + `EventParser.cs` + `keywords.html` rows.

### Files
- [ ] `Source/Game/GameFiles/SaveSystem.events` — quicksave/autosave triggers.
- [ ] `Content/scenes/Save_Test.scene` — checkpoint markers, save/load UI buttons.
- [ ] Unit tests: save → modify → load restores globals, inventory, aug levels, NPC positions; missing/damaged slot → friendly warning, no crash.

### Done-when
- [ ] `save game "slot1"` writes a valid JSON; `load game "slot1"` restores everything.
- [ ] Quicksave/quickload work via slot 0; autosave never overwrites player saves.
- [ ] Checkpoint set → death → `respawn at checkpoint` restores position + state.
- [ ] `when game loaded` fires after restore; no in-flight waits/tweens resume mid-air (by design).

---

# Phase 9 — Environmental Interactions

Goal: vents, ladders, keypads, emails/notes (lore), movable objects (crates/boxes). Reuses Task 7
G4 phases 3–4 (jump, ladders) and Task 15 (UI).

Depends on Phase 1.

### New words
- [ ] `crawl into vent <thing>` / `crawl out of vent` — vent mode (smaller collider, slower speed).
- [ ] `climb ladder <thing>` — vertical movement along ladder (already in G4 phase 4).
- [ ] `enter keypad <thing>` — opens code-entry UI.
- [ ] `set code of <keypad> to <digits>` / `when code of <keypad> is entered` — code check.
- [ ] `read <note>` / `show note "<text>"` — lore popup.
- [ ] `push <thing>` / `pull <thing>` — sustained drag of physics crates (distinct from `push` impulse).
- [ ] `stack <thing> on <thing>` — physics stacking.

### Bridge C#
- [ ] `Runtime/VentVolume.cs`: trigger volume swaps player to crouch/small-collider mode, reroutes movement.
- [ ] `Runtime/Ladder.cs` (G4 phase 4 reuse): X/Z lock to ladder axis, Y climb, span clamp, `me is climbing` condition.
- [ ] `Bridge/FlaxActions`: `EnterKeypad` (numeric-entry UI, compares to stored code, unlocks door on match); `ReadNote` (lore popup UI); `DragObject` (sustained drag of a physics body); `StackObject` (snap-onto placement).
- [ ] `WordRegistry.cs` + `EventParser.cs` + `keywords.html` rows.

### Files
- [ ] `Source/Game/GameFiles/Environment.events` — vent/ladder/keypad/note sheets.
- [ ] `Content/scenes/Environment_Test.scene` — vent shaft, ladder, keypad door, note, movable crates.
- [ ] Unit tests: keypad wrong code → denied; right code → door unlocks; vent crawl shrinks collider.

### Done-when
- [ ] Crawl into vent → smaller collider + slower speed; crawl out restores normal.
- [ ] Keypad: enter wrong code → denied; right code → door unlocks/open.
- [ ] Read note → lore text popup; close → back to game.
- [ ] Push a crate → sustained drag; stack crate on another → physics stack holds.

---

# Phase 10 — Non-Lethal vs Lethal & Consequences

Goal: track playstyle (stealth/combat/lethal/non-lethal); NPCs and story react to kill count,
takedowns, hacks, alerts. Reuses Phase 3 (takedowns) + Phase 5 (hacking).

Depends on Phase 1 + Phase 3 + Phase 5.

### New words
- [ ] `my Playstyle is "stealth" / "combat" / "lethal" / "nonlethal"` — tracked state.
- [ ] `when nonlethal takedown performed` / `when lethal takedown performed` — playstyle triggers.
- [ ] `set my LethalCount to <n>` / `my LethalCount is higher than <n>` — consequence gate.
- [ ] `set my StealthCount to <n>` / `my StealthCount is higher than <n>`.
- [ ] NPC dialogue changes based on `my LethalCount` / `my StealthCount` (reuse Phase 4 dialogue gates).

### Bridge C#
- [ ] `Runtime/ConsequenceTracker.cs` (pure): tallies kills/takedowns/hacks/alerts; exposed as readable vars for dialogue conditions.
- [ ] `Bridge/FlaxActions`: on lethal takedown/kill → increment `LethalCount`; on non-lethal → `StealthCount`; on hack success / alarm raised → respective counters.
- [ ] `WordRegistry.cs` + `EventParser.cs` + `keywords.html` rows.

### Files
- [ ] `Source/Game/GameFiles/Consequences.events` — tally sheet.
- [ ] Unit tests: lethal kill increments LethalCount; dialogue gate `my LethalCount is higher than 5` changes NPC line.

### Done-when
- [ ] Lethal takedown → `LethalCount` rises; non-lethal → `StealthCount` rises.
- [ ] NPC dialogue branches on `my LethalCount` / `my StealthCount`.
- [ ] Playstyle string reflects dominant approach.

---

# Overall acceptance (all phases)
- [ ] FPS player moves, looks, crouches, sprints, jumps, shoots (Phase 1).
- [ ] Praxis unlocks augs; active augs drain energy and affect gameplay (Phase 2).
- [ ] Guards detect via sight/sound/light; cover, peek, takedowns, body hiding work (Phase 3).
- [ ] Dialogue with choices, aug gates, disposition, VO, portraits (Phase 4).
- [ ] Hacking minigame, cameras, turrets, alarms, laser grids (Phase 5).
- [ ] Grid inventory, credits, vendors, crafting (Phase 6).
- [ ] Objectives, quest log, XP, Praxis awards, mission fail/complete (Phase 7).
- [ ] Save/load, checkpoints, quicksave (Phase 8).
- [ ] Vents, ladders, keypads, notes, movable crates (Phase 9).
- [ ] Lethal/non-lethal tracking changes NPC dialogue (Phase 10).
- [ ] All new unit tests green; Flax module build 0 errors; editor play-checks per phase clean.
- [ ] Vocabulary friction noted in `DESIGN.md §9`.
