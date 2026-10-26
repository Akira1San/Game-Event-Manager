# Plan: Fix bullet movement and spawning issues

## Problem 1: Bullet doesn't move forward continuously

**Root cause:** `Bullet.events` uses `when me appears` → `move forward speed 80`. The `when me appears` trigger fires exactly once (when the sheet is added via `AddSheet`). The `move forward` action in `FlaxActions.Execute` is a one-time position step (`me.Position += direction * speed * DeltaTime`), not continuous. So the bullet moves a single tiny step (80 × ~0.016s ≈ 1.3 units) at spawn and then stops.

**Fix:** Change `Bullet.events` to use a repeating trigger so `move forward` runs every tick:
```
when every 0 seconds
  move forward speed 80
```
`TriggerKind.Every` is polled each tick in `EventRuntime.TryPolled()`. With `Seconds=0`, `Time - LastFire >= 0` is always true, so it fires every tick, moving the bullet continuously.

**Also needed:** `FlaxActions.Step(deltaTime)` is defined but **never called** in `EventsManager.OnUpdate()`. Add `FlaxActions.Step(delta);` so `OngoingMove` entries (for `goto`/`face`) actually process. This won't fix the bullet (which uses direct position change, not `OngoingMove`), but it's needed for future continuous movement features.

## Problem 2: Bullets spawn when moving, not just on LMB press

**Investigation needed:** The code path looks correct:
- `InputEdges()` raises `KeyPressed` only on the press **edge** (transition up→down), not while held
- `when Left Mouse is pressed` → `TriggerKind.KeyPressed`, `RequireName="Left Mouse"`
- `Matches()` checks `signal.Name == descriptor.RequireName` → `"W" != "Left Mouse"` → no match

Possible causes:
1. **Multiple Player actors** — if more than one actor named "Player" exists in the scene, each gets its own Player.events sheet, and all fire on LMB press
2. **User pressing LMB while moving** — perception issue; bullets do spawn on LMB but the user notices them while moving
3. **Stale Player.events** — hot reload may not have swapped in yet (0.25s poll cadence)

**Fix approach:** Add debug logging to `InputEdges()` to log when a shot is fired (trace which key triggered it), and verify only one Player actor exists in the scene. If duplicates exist, remove extras.

## Problem 3: Enemy spawn count (design question)

**Current behavior:** `Game.events` spawns a new Enemy every 5 seconds until `Score is 5`. Multiple enemies can be on screen simultaneously. Each spawned Enemy gets its own `Enemy.events` sheet via `AttachSpawnedActor`.

**Question:** Should there be only one enemy on screen at a time?
- If yes: add `unless Enemy exists` to the spawn event in `Game.events`
- If no: current behavior is correct (matches DESIGN.md Section 7)

---

## Files to change

| File | Change |
|------|--------|
| `Source/Game/GameFiles/Bullet.events` | `when me appears` → `when every 0 seconds` |
| `Source/Game/Bridge/EventsManager.cs` | Add `FlaxActions.Step(delta);` in `OnUpdate()` |
| `Source/Game/GameFiles/Game.events` | (Optional) Add `unless Enemy exists` to limit to one enemy — user decision |
| `Source/Game/GameFiles/Player.events` | Add debug/trace or verify single Player actor — investigation |

---

## Validation

1. Build passes (0 warnings, 0 errors)
2. Test suite: 161 passed, 2 pre-existing failures (unrelated)
3. In editor: press LMB → bullet spawns and travels forward continuously across the screen
4. In editor: press W → no bullet spawns (only on LMB)
5. Enemy spawn behavior matches user's preference (one vs. many)