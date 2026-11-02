# Plan: Fix Guard Stops After Some Laps (Arrival Re-arm Bug)

## Problem
When `goto random Waypoint` picks the **same waypoint** the guard is already sitting on:
1. `FindPath` from current position to same position → path = `[target]` (already there)
2. `Step` immediately completes: distance is 0 < 0.001 → removes the move
3. `PollArrival` already has that waypoint in `contacts` (from the arrival that just fired) → never refires
4. Guard sits there forever with no active move

## Root Cause
In `FlaxActions.Remember()`, when a new goto move is added for the same subject, it cancels the old one and adds the new. But it doesn't clear the arrival contact for that target. So if the guard `goto`s to the same waypoint it's already at, the arrival contact blocks re-firing.

## Fix Location
`Source/Game/Bridge/FlaxActions.cs` → `Remember(OngoingMove move)` method

## Implementation
In the goto branch of `Remember()`, after cancelling old goto moves for the same subject, check if the subject is already within `ArrivalSlack` of the target. If so, clear that target from the arrival contacts via the runtime so `PollArrival` can re-arm.

Since `FlaxActions` doesn't have direct access to `EventRuntime`, we need to either:
1. Pass a callback to `Remember()` that clears contacts, or
2. Have `FlaxActions` track its own "recently arrived at" set, or
3. Expose a static method on `EventRuntime` to clear contacts for a subject+target pair

Option 3 is cleanest: add `EventRuntime.ClearArrivalContact(subject, target)` that removes the target from all sheet contacts for that subject.

## Files to Modify
1. `Source/Game/Runtime/EventRuntime.cs` - add `ClearArrivalContact(object subject, object target)` method
2. `Source/Game/Bridge/FlaxActions.cs` - call it in `Remember()` for goto moves when subject is at target

## Validation
- Guard patrols indefinitely in editor (no more stopping after laps)
- Works when `goto random Waypoint` picks same waypoint
- Works after moving waypoints and rebuilding navmesh
- All 209 existing tests still pass
- Flax module builds clean (0 warnings/0 errors)

## Test to Add
Add a unit test in `Tests/Program.cs` that:
1. Creates guard + two waypoints
2. Forces `goto` to same waypoint twice in a row
3. Verifies arrival fires twice (not just once)