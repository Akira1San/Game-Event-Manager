# Plan: Task 7 Part E — Forward-facing goto movement

## Goal
Make `goto` orient the actor in its travel direction so walking characters face forward while patrolling/chasing.

## Context (from codebase)

- `OngoingMove` (`FlaxActions.cs:7-20`) has `FaceTarget` bool but no forward-facing flag.
- `FlaxActions.Step()` (`FlaxActions.cs:161-208`): the `Goto` branch moves toward `move.Path[move.PathIndex]` but never rotates the subject. `FaceTarget` (from `face`) snaps to `move.Target.Position` each tick via `LookAt`.
- `ActionWords.cs` goto case (`ActionWords.cs:90-111`): parses `goto <thing> [speed <n>]`, sets `spec.Thing`, `spec.Number`. No `facing forward` qualifier.
- `FlaxActions.cs` goto Execute (`FlaxActions.cs:365-394`): builds `OngoingMove` with `Goto = true`, `Path`, `Speed`, `UsePhysics`. No forward-facing flag set.
- `Remember()` dedup (`FlaxActions.cs:487-515`): checks `FaceTarget` and `Goto` but not a forward-facing flag.
- `WordRegistry.cs` (`WordRegistry.cs:55-56`): `goto` examples lack `facing forward`.
- Flax forward is `Vector3.Forward` (0, 0, -1); world forward = `Vector3.Transform(Vector3.Forward, actor.Orientation)`.
- `FlaxActions.Step(delta)` is called from `EventsManager.OnUpdate` (`EventsManager.cs:372`), so `OngoingMove` entries already process.

## Design decisions

### 1. Opt-in qualifier (recommended over default)
Add optional qualifier `goto <thing> facing forward`. Do NOT make it default — existing scenes (e.g. Enemy patrol) should not suddenly start rotating. Matches TASKS.md wording "Add optional qualifier ... (or make it default) to opt in" — choosing the safer opt-in path.

### 2. Rotation target
Face the **next waypoint** (`move.Path[move.PathIndex]`), not the final target. The actor should look in its direction of travel. Use instant `LookAt(waypoint)` each tick (same approach as `face`, which calls `LookAt` every frame and appears smooth).

### 3. Data model
Add `bool FaceForward` to `OngoingMove`. Set it from `spec.FaceForward` in the goto Execute path. Add `spec.FaceForward` to `ActionSpec`.

## Files to touch

### `Runtime/ActionWords.cs`
- Add `public bool FaceForward;` to `ActionSpec`.
- In goto case (`ActionWords.cs:90-111`): after parsing `speed`, check if next words are `facing forward`; if so set `spec.FaceForward = true` and consume those tokens.

### `Bridge/FlaxActions.cs`
- Add `public bool FaceForward;` to `OngoingMove`.
- In goto Execute (`FlaxActions.cs:365-394`): set `FaceForward = spec.FaceForward` on the `OngoingMove`.
- In `Step()` Goto branch (`FlaxActions.cs:182-207`): after moving toward the waypoint (in the `else` branch where `MoveSubject` is called), if `move.FaceForward`, call `move.Subject.LookAt(move.Path[move.PathIndex])` to face direction of travel.
- In `Remember()` dedup (`FlaxActions.cs:509-511`): add `existing.FaceForward == move.FaceForward` to the equality check.

### `Events/WordRegistry.cs`
- Add WordEntry: `new WordEntry { Kind = "action", Word = "goto", Example = "goto Player facing forward" }`.

### `GameFiles/Enemy.events`
- Add `facing forward` to goto commands so the enemy faces its patrol/chase direction (test of Part E in the existing scene).

### `DESIGN.md` §9
- Note any vocabulary gap (e.g. no `facing backward` / per-speed turn smoothing).

## Validation
- Build: `dotnet build "EventManager/EventManager/Source/Game.csproj"` → 0 warnings, 0 errors.
- Unit tests: `dotnet run --project "EventManager/EventManager/Tests/EventManagerTests.csproj"` → all green.
- Editor: open `EnemyTest.scene`, play, verify the Enemy cube rotates to face each waypoint as it patrols (not just slides sideways).
- Editor: verify `goto Player facing forward` makes the enemy face the player while chasing.

## Open questions (none blocking)
- Smooth vs instant rotation: recommend instant `LookAt` per tick (matches `face` behavior). If user wants easing, add a turn-speed qualifier later (parked in §9).
- `facing forward` vs `face forward`: TASKS.md says `facing forward`. Use that exact wording.
